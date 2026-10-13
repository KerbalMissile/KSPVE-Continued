using System;
using System.Collections.Generic;
using KSPVE.Pure.RayTracing;
using UnityEngine;

namespace KSPVE.RayTracing;

/// <summary>
/// What the rays can hit, rebuilt around the camera every frame: the loaded craft's parts, the current body's terrain
/// quads and its towns and buildings (PQSCity), within reach of the rays. Each is an instance of its mesh's tree, placed
/// relative to the camera (so floats keep their precision whatever KSP's floating origin is doing); a top-level tree over
/// the instances' boxes is refitted every frame and rebuilt when the set changes or every few frames.
/// </summary>
internal sealed class RtScene : IDisposable
{
    private struct Candidate
    {
        public Renderer Renderer;
        public MeshFilter Filter;
        public int MaterialOffset;
    }

    private readonly List<Candidate> _candidates = new List<Candidate>();
    private readonly List<MeshRenderer> _scratch = new List<MeshRenderer>();
    private readonly Dictionary<Part, MeshRenderer[]> _partRenderers = new Dictionary<Part, MeshRenderer[]>();
    private readonly Dictionary<CelestialBody, MeshRenderer[]> _townRenderers = new Dictionary<CelestialBody, MeshRenderer[]>();
    private RtInstance[] _instances = new RtInstance[1024];
    private Aabb[] _boxes = new Aabb[1024];
    private Renderer[] _placed = new Renderer[1024];
    private Renderer[] _placedBefore = new Renderer[0];
    private int _placedBeforeCount;
    private BoxBvh _tlas;
    private float _nextGather;
    private int _framesSinceBuild;

    public readonly RtGeometry Geometry = new RtGeometry();
    public readonly RtMaterials Materials = new RtMaterials();

    public ComputeBuffer Instances { get; private set; }
    public ComputeBuffer TopNodes { get; private set; }
    public ComputeBuffer TopOrder { get; private set; }
    public int InstanceCount { get; private set; }
    public int CandidateCount => _candidates.Count;

    /// <summary>The scene has something to trace (and its buffers exist).</summary>
    public bool Ready => InstanceCount > 0 && Instances != null && TopNodes != null && Geometry.Nodes != null && Materials.Table != null;

    /// <summary>Gathers and places everything within <paramref name="range"/> (m) of the camera for this frame.</summary>
    public void Update(Camera camera, double range, int frame)
    {
        Geometry.BeginFrame();
        if (Time.unscaledTime >= _nextGather)
        {
            _nextGather = Time.unscaledTime + 0.5f;
            Gather(camera.transform.position, range);
        }

        Vector3 cam = camera.transform.position;
        float reach = (float)range;
        int count = 0;
        foreach (Candidate c in _candidates)
        {
            Renderer r = c.Renderer;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || c.Filter == null) continue;
            Bounds b = r.bounds;
            if (b.SqrDistance(cam) > reach * reach) continue;
            RtGeometry.Blas blas = Geometry.Request(c.Filter.sharedMesh, frame);
            if (blas == null) continue;

            Matrix4x4 m = r.localToWorldMatrix;
            m.m03 -= cam.x;
            m.m13 -= cam.y;
            m.m23 -= cam.z;
            Matrix4x4 inv = m.inverse;
            if (count == _instances.Length)
            {
                Array.Resize(ref _instances, count * 2);
                Array.Resize(ref _boxes, count * 2);
                Array.Resize(ref _placed, count * 2);
            }
            _instances[count] = new RtInstance
            {
                WorldToObject0 = inv.GetRow(0),
                WorldToObject1 = inv.GetRow(1),
                WorldToObject2 = inv.GetRow(2),
                NodeOffset = (uint)blas.NodeOffset,
                TriangleOffset = (uint)blas.TriangleOffset,
                DataOffset = (uint)blas.DataOffset,
                MaterialOffset = (uint)c.MaterialOffset,
            };
            Vector3 min = b.min - cam, max = b.max - cam;
            _boxes[count] = new Aabb(min.x, min.y, min.z, max.x, max.y, max.z);
            _placed[count] = r;
            count++;
        }
        InstanceCount = count;
        Geometry.Commit(frame, 600);
        Materials.Upload();
        if (count == 0) return;

        // The same instances in the same order as last frame: refit the top level; otherwise (or now and then, as boxes
        // drift relative to each other) rebuild it.
        bool same = _tlas != null && count == _placedBeforeCount && ++_framesSinceBuild < 30;
        if (same)
            for (int i = 0; i < count && same; i++)
                same = _placed[i] == _placedBefore[i];
        if (same) BvhBuilder.RefitBoxes(_tlas, _boxes);
        else
        {
            _tlas = BvhBuilder.BuildBoxes(_boxes, count, 1, _tlas);
            _framesSinceBuild = 0;
            if (_placedBefore.Length < _placed.Length) _placedBefore = new Renderer[_placed.Length];
            Array.Copy(_placed, _placedBefore, count);
            _placedBeforeCount = count;
        }

        Instances = Fit(Instances, count, 64);
        Instances.SetData(_instances, 0, 0, count);
        TopNodes = Fit(TopNodes, _tlas.NodeCount, 64);
        TopNodes.SetData(_tlas.Nodes, 0, 0, _tlas.NodeCount);
        TopOrder = Fit(TopOrder, _tlas.PrimitiveCount, 4);
        TopOrder.SetData(_tlas.Order, 0, 0, _tlas.PrimitiveCount);
    }

    private static ComputeBuffer Fit(ComputeBuffer buffer, int count, int stride)
    {
        if (buffer != null && buffer.count >= count) return buffer;
        buffer?.Release();
        int size = 256;
        while (size < count) size *= 2;
        return new ComputeBuffer(size, stride, ComputeBufferType.Structured);
    }

    /// <summary>The renderers within reach: loaded craft, the terrain quads and the towns of the body under the camera.</summary>
    private void Gather(Vector3 cam, double range)
    {
        _candidates.Clear();
        if (FlightGlobals.ready)
        {
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == null || v.parts == null) continue;
                if ((v.transform.position - cam).sqrMagnitude > (range + 200.0) * (range + 200.0)) continue;
                foreach (Part p in v.parts)
                {
                    if (p == null) continue;
                    if (!_partRenderers.TryGetValue(p, out MeshRenderer[] renderers) || renderers == null)
                    {
                        renderers = p.GetComponentsInChildren<MeshRenderer>(false);
                        _partRenderers[p] = renderers;
                    }
                    foreach (MeshRenderer r in renderers) Add(r);
                }
            }
        }

        CelestialBody body = FlightGlobals.currentMainBody;
        PQS pqs = body != null ? body.pqsController : null;
        if (pqs != null && pqs.isActive && pqs.quads != null)
        {
            double reach2 = range * range;
            foreach (PQ q in pqs.quads) Visit(q, cam, reach2);
            if (!_townRenderers.TryGetValue(body, out MeshRenderer[] towns) || towns == null)
            {
                _scratch.Clear();
                foreach (PQSCity city in pqs.GetComponentsInChildren<PQSCity>(true)) _scratch.AddRange(city.GetComponentsInChildren<MeshRenderer>(true));
                foreach (PQSCity2 city in pqs.GetComponentsInChildren<PQSCity2>(true)) _scratch.AddRange(city.GetComponentsInChildren<MeshRenderer>(true));
                towns = _scratch.ToArray();
                _townRenderers[body] = towns;
            }
            foreach (MeshRenderer r in towns)
                if (r != null && r.bounds.SqrDistance(cam) < reach2) Add(r);
        }

        // Parts that are gone.
        if (_partRenderers.Count > 4096) _partRenderers.Clear();
    }

    private void Visit(PQ q, Vector3 cam, double reach2)
    {
        if (q == null) return;
        if (q.isSubdivided && q.subNodes != null)
        {
            foreach (PQ child in q.subNodes) Visit(child, cam, reach2);
            return;
        }
        if (!q.isVisible || q.meshRenderer == null || !q.meshRenderer.enabled || q.mesh == null) return;
        if (q.meshRenderer.bounds.SqrDistance(cam) > reach2) return;
        _candidates.Add(new Candidate { Renderer = q.meshRenderer, Filter = q.meshFilter, MaterialOffset = Materials.BlockFor(q.meshRenderer.sharedMaterials) });
    }

    private void Add(MeshRenderer r)
    {
        if (r == null) return;
        var filter = r.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;
        Material[] materials = r.sharedMaterials;
        if (materials.Length > 0 && RtMaterials.IsTransparent(materials[0])) return;   // glass, plumes, particles
        _candidates.Add(new Candidate { Renderer = r, Filter = filter, MaterialOffset = Materials.BlockFor(materials) });
    }

    /// <summary>Forgets cached renderers (after a scene change).</summary>
    public void Reset()
    {
        _candidates.Clear();
        _partRenderers.Clear();
        _townRenderers.Clear();
        Materials.Clear();
        _tlas = null;
        _placedBeforeCount = 0;
        InstanceCount = 0;
    }

    public void Dispose()
    {
        Geometry.Dispose();
        Materials.Dispose();
        Instances?.Release();
        TopNodes?.Release();
        TopOrder?.Release();
        Instances = TopNodes = TopOrder = null;
    }
}
