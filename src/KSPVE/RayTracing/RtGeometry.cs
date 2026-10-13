using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using KSPVE.Core;
using KSPVE.Pure.RayTracing;
using UnityEngine;

namespace KSPVE.RayTracing;

/// <summary>
/// Every mesh the tracer knows, each with its own BVH (built on a worker thread the first time the mesh is seen, from its
/// CPU copy), packed into three shared GPU buffers: tree nodes, triangles (leaf order) and triangle data (mesh order).
/// Ranges are handed out first-fit and given back when a mesh has gone unused for a while; the buffers grow by doubling.
/// A CPU mirror of each buffer is kept so growing re-uploads without rebuilding anything.
/// </summary>
internal sealed class RtGeometry : IDisposable
{
    /// <summary>One mesh's tree, and where it sits in the shared buffers once committed.</summary>
    internal sealed class Blas
    {
        public Mesh Mesh;
        public int VertexCount;
        public Bounds MeshBounds;
        public int NodeOffset = -1, NodeCount, TriangleOffset = -1, TriangleCount, DataOffset = -1, DataCount;
        public int SubmeshCount;
        public bool Ready, Failed, Building;
        public int LastUsedFrame;
        // Filled on the worker thread, consumed when committed.
        public TriangleBvh Bvh;
        public RtTriangleData[] Data;
    }

    /// <summary>A mesh's CPU arrays, copied on the main thread for the worker.</summary>
    private sealed class Job
    {
        public Blas Blas;
        public float[] Positions;
        public int[] Indices;
        public RtTriangleData[] Data;
    }

    /// <summary>First-fit ranges in a growable array.</summary>
    private sealed class Ranges
    {
        private readonly List<int> _starts = new List<int>(), _lengths = new List<int>();   // free ranges, sorted by start
        public int Capacity { get; private set; }

        public Ranges(int capacity)
        {
            Capacity = capacity;
            _starts.Add(0);
            _lengths.Add(capacity);
        }

        /// <summary>A range of <paramref name="length"/>, or -1 when none is free (then <see cref="Grow"/>).</summary>
        public int Take(int length)
        {
            for (int i = 0; i < _starts.Count; i++)
            {
                if (_lengths[i] < length) continue;
                int start = _starts[i];
                _starts[i] += length;
                _lengths[i] -= length;
                if (_lengths[i] == 0)
                {
                    _starts.RemoveAt(i);
                    _lengths.RemoveAt(i);
                }
                return start;
            }
            return -1;
        }

        public void Give(int start, int length)
        {
            if (length <= 0 || start < 0) return;
            int i = 0;
            while (i < _starts.Count && _starts[i] < start) i++;
            _starts.Insert(i, start);
            _lengths.Insert(i, length);
            // Join with the neighbours.
            if (i + 1 < _starts.Count && _starts[i] + _lengths[i] == _starts[i + 1])
            {
                _lengths[i] += _lengths[i + 1];
                _starts.RemoveAt(i + 1);
                _lengths.RemoveAt(i + 1);
            }
            if (i > 0 && _starts[i - 1] + _lengths[i - 1] == _starts[i])
            {
                _lengths[i - 1] += _lengths[i];
                _starts.RemoveAt(i);
                _lengths.RemoveAt(i);
            }
        }

        public void Grow(int newCapacity)
        {
            if (newCapacity <= Capacity) return;
            Give(Capacity, newCapacity - Capacity);
            Capacity = newCapacity;
        }
    }

    private const int NodeStride = 64, TriangleStride = 48, DataStride = 40;

    private readonly Dictionary<Mesh, Blas> _meshes = new Dictionary<Mesh, Blas>();
    private readonly ConcurrentQueue<Job> _done = new ConcurrentQueue<Job>();
    private readonly List<Mesh> _evict = new List<Mesh>();
    private readonly Ranges _nodeRanges = new Ranges(1 << 16), _triangleRanges = new Ranges(1 << 17), _dataRanges = new Ranges(1 << 17);
    private BvhNode[] _nodes = new BvhNode[1 << 16];
    private GpuTriangle[] _triangles = new GpuTriangle[1 << 17];
    private RtTriangleData[] _data = new RtTriangleData[1 << 17];
    private bool _nodesResized = true, _trianglesResized = true, _dataResized = true;
    private readonly List<Vector2Int> _nodeUploads = new List<Vector2Int>(), _triangleUploads = new List<Vector2Int>(), _dataUploads = new List<Vector2Int>();
    private int _building;
    private int _unreadable;

    public ComputeBuffer Nodes { get; private set; }
    public ComputeBuffer Triangles { get; private set; }
    public ComputeBuffer Data { get; private set; }

    /// <summary>Meshes with a committed tree, triangles in them, meshes skipped because the game keeps no CPU copy.</summary>
    public int MeshCount { get; private set; }
    public int TriangleCount { get; private set; }
    public int Unreadable => _unreadable;
    public int Building => _building;

    /// <summary>Vertices copied from meshes per frame at most (the copy is on the main thread).</summary>
    public int CopyBudget = 200000;
    private int _copiedThisFrame;

    /// <summary>
    /// The mesh's committed tree, or null while it is being built (it is queued the first time it is asked for, and again
    /// when its vertex count or bounds change, as a rebuilt terrain quad's do).
    /// </summary>
    public Blas Request(Mesh mesh, int frame)
    {
        if (mesh == null) return null;
        if (_meshes.TryGetValue(mesh, out Blas blas))
        {
            blas.LastUsedFrame = frame;
            if (blas.Failed) return null;
            if (!blas.Building && (!blas.Ready || blas.VertexCount != mesh.vertexCount || blas.MeshBounds != mesh.bounds))
                Queue(blas);   // not copied yet (over the frame's budget), or changed: rebuild, keeping the old tree meanwhile
            return blas.Ready ? blas : null;
        }
        blas = new Blas { Mesh = mesh, LastUsedFrame = frame };
        _meshes[mesh] = blas;
        Queue(blas);
        return null;
    }

    public void BeginFrame()
    {
        _copiedThisFrame = 0;
    }

    private void Queue(Blas blas)
    {
        Mesh mesh = blas.Mesh;
        if (!mesh.isReadable)
        {
            blas.Failed = true;
            _unreadable++;
            return;
        }
        int vertexCount = mesh.vertexCount;
        if (_copiedThisFrame > 0 && _copiedThisFrame + vertexCount > CopyBudget) return;   // next frame
        _copiedThisFrame += vertexCount;

        Vector3[] vertices = mesh.vertices;
        Vector2[] uv = mesh.uv;
        Color32[] colors = mesh.colors32;
        bool hasUv = uv != null && uv.Length == vertices.Length;
        bool hasColors = colors != null && colors.Length == vertices.Length;
        var indices = new List<int>(mesh.vertexCount * 2);
        var submeshOf = new List<int>(mesh.vertexCount);
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] t = mesh.GetTriangles(s);
            indices.AddRange(t);
            for (int k = 0; k < t.Length / 3; k++) submeshOf.Add(s);
        }
        var positions = new float[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            positions[3 * i] = vertices[i].x;
            positions[3 * i + 1] = vertices[i].y;
            positions[3 * i + 2] = vertices[i].z;
        }
        int[] index = indices.ToArray();
        var data = new RtTriangleData[index.Length / 3];
        for (int k = 0; k < data.Length; k++)
        {
            int a = index[3 * k], b = index[3 * k + 1], c = index[3 * k + 2];
            data[k] = new RtTriangleData
            {
                Uv0 = hasUv ? uv[a] : Vector2.zero,
                Uv1 = hasUv ? uv[b] : Vector2.zero,
                Uv2 = hasUv ? uv[c] : Vector2.zero,
                Submesh = (uint)submeshOf[k],
                Color0 = hasColors ? Pack(colors[a]) : 0xFFFFFFFFu,
                Color1 = hasColors ? Pack(colors[b]) : 0xFFFFFFFFu,
                Color2 = hasColors ? Pack(colors[c]) : 0xFFFFFFFFu,
            };
        }
        blas.VertexCount = vertexCount;
        blas.MeshBounds = mesh.bounds;
        blas.SubmeshCount = mesh.subMeshCount;
        blas.Building = true;
        var job = new Job { Blas = blas, Positions = positions, Indices = index, Data = data };
        Interlocked.Increment(ref _building);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                job.Blas.Bvh = BvhBuilder.BuildTriangles(job.Positions, job.Indices, 4);
                job.Blas.Data = job.Data;
            }
            catch (Exception ex)
            {
                job.Blas.Bvh = null;
                Log.Warning("Building a ray tracing tree failed: " + ex.Message);
            }
            _done.Enqueue(job);
        });
    }

    private static uint Pack(Color32 c) => c.r | (uint)c.g << 8 | (uint)c.b << 16 | (uint)c.a << 24;

    /// <summary>Puts finished trees into the shared buffers, forgets meshes unused for a while, and uploads what changed.</summary>
    public void Commit(int frame, int forgetAfterFrames)
    {
        while (_done.TryDequeue(out Job job))
        {
            Interlocked.Decrement(ref _building);
            Blas blas = job.Blas;
            blas.Building = false;
            if (!_meshes.TryGetValue(blas.Mesh, out Blas current) || current != blas) continue;   // forgotten meanwhile
            if (blas.Bvh == null)
            {
                blas.Failed = true;
                continue;
            }
            Release(blas);
            TriangleBvh bvh = blas.Bvh;
            blas.NodeCount = bvh.NodeCount;
            blas.TriangleCount = bvh.Triangles.Length;
            blas.DataCount = blas.Data.Length;
            blas.NodeOffset = Take(_nodeRanges, ref _nodes, blas.NodeCount, ref _nodesResized);
            blas.TriangleOffset = Take(_triangleRanges, ref _triangles, Math.Max(blas.TriangleCount, 1), ref _trianglesResized);
            blas.DataOffset = Take(_dataRanges, ref _data, Math.Max(blas.DataCount, 1), ref _dataResized);
            Array.Copy(bvh.Nodes, 0, _nodes, blas.NodeOffset, blas.NodeCount);
            Array.Copy(bvh.Triangles, 0, _triangles, blas.TriangleOffset, blas.TriangleCount);
            Array.Copy(blas.Data, 0, _data, blas.DataOffset, blas.DataCount);
            _nodeUploads.Add(new Vector2Int(blas.NodeOffset, blas.NodeCount));
            _triangleUploads.Add(new Vector2Int(blas.TriangleOffset, blas.TriangleCount));
            _dataUploads.Add(new Vector2Int(blas.DataOffset, blas.DataCount));
            blas.Ready = true;
            blas.Bvh = null;
            blas.Data = null;
            MeshCount++;
            TriangleCount += blas.TriangleCount;
        }

        // Forget meshes nobody has asked for lately (destroyed quads and parts, places left behind).
        _evict.Clear();
        foreach (KeyValuePair<Mesh, Blas> pair in _meshes)
            if (pair.Key == null || frame - pair.Value.LastUsedFrame > forgetAfterFrames && !pair.Value.Building) _evict.Add(pair.Key);
        foreach (Mesh mesh in _evict)
        {
            Blas blas = _meshes[mesh];
            Release(blas);
            _meshes.Remove(mesh);
        }

        Upload();
    }

    private void Release(Blas blas)
    {
        if (!blas.Ready) return;
        _nodeRanges.Give(blas.NodeOffset, blas.NodeCount);
        _triangleRanges.Give(blas.TriangleOffset, Math.Max(blas.TriangleCount, 1));
        _dataRanges.Give(blas.DataOffset, Math.Max(blas.DataCount, 1));
        MeshCount--;
        TriangleCount -= blas.TriangleCount;
        blas.Ready = false;
    }

    private static int Take<T>(Ranges ranges, ref T[] mirror, int length, ref bool resized)
    {
        int start = ranges.Take(length);
        while (start < 0)
        {
            int capacity = ranges.Capacity * 2;
            while (capacity < length) capacity *= 2;
            ranges.Grow(capacity);
            Array.Resize(ref mirror, capacity);
            resized = true;
            start = ranges.Take(length);
        }
        return start;
    }

    private void Upload()
    {
        Nodes = Sync(Nodes, _nodes, NodeStride, ref _nodesResized, _nodeUploads);
        Triangles = Sync(Triangles, _triangles, TriangleStride, ref _trianglesResized, _triangleUploads);
        Data = Sync(Data, _data, DataStride, ref _dataResized, _dataUploads);
    }

    private static ComputeBuffer Sync<T>(ComputeBuffer buffer, T[] mirror, int stride, ref bool resized, List<Vector2Int> uploads) where T : struct
    {
        if (buffer == null || resized || buffer.count != mirror.Length)
        {
            buffer?.Release();
            buffer = new ComputeBuffer(mirror.Length, stride, ComputeBufferType.Structured);
            buffer.SetData(mirror);
            resized = false;
            uploads.Clear();
            return buffer;
        }
        foreach (Vector2Int u in uploads)
            if (u.y > 0) buffer.SetData(mirror, u.x, u.x, u.y);
        uploads.Clear();
        return buffer;
    }

    public void Dispose()
    {
        Nodes?.Release();
        Triangles?.Release();
        Data?.Release();
        Nodes = Triangles = Data = null;
        _meshes.Clear();
    }
}
