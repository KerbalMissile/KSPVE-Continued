using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KSPVE.Pure.RayTracing;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.RayTracing;

public class BvhTests
{
    // ---- helpers -------------------------------------------------------------------------------------------------

    private static void RandomSoup(Random rng, int triangles, float size, out float[] pos, out int[] idx)
    {
        pos = new float[triangles * 9];
        idx = new int[triangles * 3];
        for (int t = 0; t < triangles; t++)
        {
            float cx = (float)rng.NextDouble() * 20f - 10f, cy = (float)rng.NextDouble() * 20f - 10f, cz = (float)rng.NextDouble() * 20f - 10f;
            for (int k = 0; k < 3; k++)
            {
                pos[9 * t + 3 * k] = cx + ((float)rng.NextDouble() - 0.5f) * size;
                pos[9 * t + 3 * k + 1] = cy + ((float)rng.NextDouble() - 0.5f) * size;
                pos[9 * t + 3 * k + 2] = cz + ((float)rng.NextDouble() - 0.5f) * size;
                idx[3 * t + k] = 3 * t + k;
            }
        }
    }

    private static void Sphere(int subdivisions, out float[] pos, out int[] idx)
    {
        // A closed icosphere: every edge shared by exactly two triangles.
        float a = (1f + (float)Math.Sqrt(5.0)) / 2f;
        var v = new List<float[]>
        {
            new[] { -1f, a, 0f }, new[] { 1f, a, 0f }, new[] { -1f, -a, 0f }, new[] { 1f, -a, 0f },
            new[] { 0f, -1f, a }, new[] { 0f, 1f, a }, new[] { 0f, -1f, -a }, new[] { 0f, 1f, -a },
            new[] { a, 0f, -1f }, new[] { a, 0f, 1f }, new[] { -a, 0f, -1f }, new[] { -a, 0f, 1f },
        };
        var f = new List<int[]>
        {
            new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
            new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
            new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
            new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int i, int j)
            {
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                if (mid.TryGetValue(key, out int m)) return m;
                v.Add(new[] { (v[i][0] + v[j][0]) / 2, (v[i][1] + v[j][1]) / 2, (v[i][2] + v[j][2]) / 2 });
                return mid[key] = v.Count - 1;
            }
            var nf = new List<int[]>();
            foreach (int[] t in f)
            {
                int ab = Mid(t[0], t[1]), bc = Mid(t[1], t[2]), ca = Mid(t[2], t[0]);
                nf.Add(new[] { t[0], ab, ca });
                nf.Add(new[] { t[1], bc, ab });
                nf.Add(new[] { t[2], ca, bc });
                nf.Add(new[] { ab, bc, ca });
            }
            f = nf;
        }
        pos = new float[v.Count * 3];
        for (int i = 0; i < v.Count; i++)
        {
            float l = (float)Math.Sqrt(v[i][0] * v[i][0] + v[i][1] * v[i][1] + v[i][2] * v[i][2]);
            for (int k = 0; k < 3; k++) pos[3 * i + k] = v[i][k] / l;
        }
        idx = new int[f.Count * 3];
        for (int i = 0; i < f.Count; i++)
            for (int k = 0; k < 3; k++) idx[3 * i + k] = f[i][k];
    }

    private static Vec RandomDir(Random rng)
    {
        while (true)
        {
            double x = rng.NextDouble() * 2 - 1, y = rng.NextDouble() * 2 - 1, z = rng.NextDouble() * 2 - 1;
            double l = Math.Sqrt(x * x + y * y + z * z);
            if (l > 0.1 && l <= 1.0) return new Vec((float)(x / l), (float)(y / l), (float)(z / l));
        }
    }

    private static bool Brute(TriangleBvh bvh, Vec o, Vec d, float tMax, out int slot, out float t)
    {
        slot = -1;
        t = 0f;
        for (int i = 0; i < bvh.Triangles.Length; i++)
        {
            if (BvhTraversal.IntersectTriangle(in bvh.Triangles[i], o.X, o.Y, o.Z, d.X, d.Y, d.Z, tMax, out float tt, out _, out _))
            {
                tMax = tt;
                slot = i;
                t = tt;
            }
        }
        return slot >= 0;
    }

    /// <summary>Walks the tree: checks structure and returns the deepest level (root = 1).</summary>
    private static int CheckStructure(BvhNode[] nodes, int nodeCount, int primitiveCount)
    {
        var covered = new int[primitiveCount];
        int maxDepth = 0;
        var stack = new Stack<(int node, int depth)>();
        stack.Push((0, 1));
        int visited = 0;
        while (stack.Count > 0)
        {
            (int ni, int depth) = stack.Pop();
            visited++;
            maxDepth = Math.Max(maxDepth, depth);
            BvhNode n = nodes[ni];
            for (int side = 0; side < 2; side++)
            {
                int index = side == 0 ? n.LIndex : n.RIndex, count = side == 0 ? n.LCount : n.RCount;
                if (count > 0)
                {
                    for (int i = index; i < index + count; i++) covered[i]++;
                }
                else if (count == 0)
                {
                    Assert.That(index, Is.GreaterThan(ni), "children come after their parent");
                    Assert.That(index, Is.LessThan(nodeCount));
                    stack.Push((index, depth + 1));
                }
                else
                {
                    float minX = side == 0 ? n.LMinX : n.RMinX, maxX = side == 0 ? n.LMaxX : n.RMaxX;
                    Assert.That(float.IsPositiveInfinity(minX) && float.IsNegativeInfinity(maxX), "empty child box is inverted");
                }
            }
        }
        Assert.That(visited, Is.EqualTo(nodeCount), "every node reachable exactly once");
        for (int i = 0; i < primitiveCount; i++) Assert.That(covered[i], Is.EqualTo(1), "primitive " + i + " in exactly one leaf");
        return maxDepth;
    }

    private static void CheckBoxesContain(TriangleBvh bvh)
    {
        // Every child box must contain what is below it.
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            BvhNode n = bvh.Nodes[stack.Pop()];
            for (int side = 0; side < 2; side++)
            {
                int index = side == 0 ? n.LIndex : n.RIndex, count = side == 0 ? n.LCount : n.RCount;
                float x0 = side == 0 ? n.LMinX : n.RMinX, y0 = side == 0 ? n.LMinY : n.RMinY, z0 = side == 0 ? n.LMinZ : n.RMinZ;
                float x1 = side == 0 ? n.LMaxX : n.RMaxX, y1 = side == 0 ? n.LMaxY : n.RMaxY, z1 = side == 0 ? n.LMaxZ : n.RMaxZ;
                if (count > 0)
                {
                    for (int i = index; i < index + count; i++)
                    {
                        GpuTriangle t = bvh.Triangles[i];
                        float[] xs = { t.V0X, t.V0X + t.E1X, t.V0X + t.E2X };
                        float[] ys = { t.V0Y, t.V0Y + t.E1Y, t.V0Y + t.E2Y };
                        float[] zs = { t.V0Z, t.V0Z + t.E1Z, t.V0Z + t.E2Z };
                        foreach (float x in xs) Assert.That(x >= x0 && x <= x1);
                        foreach (float y in ys) Assert.That(y >= y0 && y <= y1);
                        foreach (float z in zs) Assert.That(z >= z0 && z <= z1);
                    }
                }
                else if (count == 0)
                {
                    BvhNode c = bvh.Nodes[index];
                    Assert.That(Math.Min(c.LMinX, c.RMinX) >= x0 && Math.Max(c.LMaxX, c.RMaxX) <= x1);
                    Assert.That(Math.Min(c.LMinY, c.RMinY) >= y0 && Math.Max(c.LMaxY, c.RMaxY) <= y1);
                    Assert.That(Math.Min(c.LMinZ, c.RMinZ) >= z0 && Math.Max(c.LMaxZ, c.RMaxZ) <= z1);
                    stack.Push(index);
                }
            }
        }
    }

    private static void CompareRays(TriangleBvh bvh, Random rng, int rays, Func<Random, Vec> origin, float tMax, int minHits = 0)
    {
        int hits = 0;
        for (int r = 0; r < rays; r++)
        {
            Vec o = origin(rng), d = RandomDir(rng);
            if (r % 2 == 0)
            {
                // Aim half the rays at a random triangle's first corner, so even tiny meshes get hit.
                GpuTriangle g = bvh.Triangles[rng.Next(bvh.Triangles.Length)];
                float ax = g.V0X - o.X, ay = g.V0Y - o.Y, az = g.V0Z - o.Z;
                float al = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
                if (al > 1e-3f) d = new Vec(ax / al, ay / al, az / al);
            }
            bool b = Brute(bvh, o, d, tMax, out int bslot, out float bt);
            bool h = BvhTraversal.IntersectTriangles(bvh, o, d, tMax, out int slot, out float t, out _, out _);
            Assert.That(h, Is.EqualTo(b), "ray " + r);
            Assert.That(BvhTraversal.AnyHitTriangles(bvh, o, d, tMax), Is.EqualTo(b), "any-hit, ray " + r);
            if (!b) continue;
            hits++;
            Assert.That(t, Is.EqualTo(bt).Within(1e-4).Percent, "t, ray " + r);
            if (slot != bslot)
                Assert.That(Math.Abs(t - bt), Is.LessThan(1e-5f * Math.Max(1f, bt)), "a different triangle is only allowed on an exact tie");
        }
        Assert.That(hits, Is.GreaterThanOrEqualTo(minHits), "the test should actually hit things");
    }

    // ---- layout --------------------------------------------------------------------------------------------------

    [Test]
    public void StructSizes_MatchTheGpuLayout()
    {
        Assert.That(Marshal.SizeOf(typeof(BvhNode)), Is.EqualTo(64));
        Assert.That(Marshal.SizeOf(typeof(GpuTriangle)), Is.EqualTo(48));
        Assert.That(Marshal.SizeOf(typeof(Aabb)), Is.EqualTo(24));
        Assert.That(Marshal.OffsetOf(typeof(BvhNode), "LIndex").ToInt32(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf(typeof(BvhNode), "LMaxX").ToInt32(), Is.EqualTo(16));
        Assert.That(Marshal.OffsetOf(typeof(BvhNode), "LCount").ToInt32(), Is.EqualTo(28));
        Assert.That(Marshal.OffsetOf(typeof(BvhNode), "RMinX").ToInt32(), Is.EqualTo(32));
        Assert.That(Marshal.OffsetOf(typeof(BvhNode), "RCount").ToInt32(), Is.EqualTo(60));
        Assert.That(Marshal.OffsetOf(typeof(GpuTriangle), "Original").ToInt32(), Is.EqualTo(12));
        Assert.That(Marshal.OffsetOf(typeof(GpuTriangle), "E2X").ToInt32(), Is.EqualTo(32));
    }

    // ---- triangles -----------------------------------------------------------------------------------------------

    [TestCase(1, 4)]
    [TestCase(2, 4)]
    [TestCase(7, 1)]
    [TestCase(500, 4)]
    [TestCase(3000, 2)]
    [TestCase(3000, 8)]
    public void RandomSoup_MatchesBruteForce(int triangles, int leaf)
    {
        var rng = new Random(1234 + triangles);
        RandomSoup(rng, triangles, 1.5f, out float[] pos, out int[] idx);
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx, leaf);
        Assert.That(bvh.Triangles.Length, Is.EqualTo(triangles));
        int depth = CheckStructure(bvh.Nodes, bvh.NodeCount, bvh.Triangles.Length);
        Assert.That(depth, Is.LessThanOrEqualTo(BvhLimits.MaxDepth));
        CheckBoxesContain(bvh);
        CompareRays(bvh, rng, 1500, r => new Vec((float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f), float.PositiveInfinity, 10);
        CompareRays(bvh, rng, 500, r => new Vec((float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f), 5f);
    }

    [Test]
    public void LeavesRespectTheLeafSize()
    {
        var rng = new Random(5);
        RandomSoup(rng, 2000, 1.0f, out float[] pos, out int[] idx);
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx, 3);
        for (int i = 0; i < bvh.NodeCount; i++)
        {
            Assert.That(bvh.Nodes[i].LCount, Is.LessThanOrEqualTo(3));
            Assert.That(bvh.Nodes[i].RCount, Is.LessThanOrEqualTo(3));
        }
    }

    [Test]
    public void Empty_BuildsAnEmptyRootThatNeverHits()
    {
        TriangleBvh bvh = BvhBuilder.BuildTriangles(new float[0], new int[0]);
        Assert.That(bvh.NodeCount, Is.EqualTo(1));
        Assert.That(bvh.Nodes[0].LCount, Is.LessThan(0));
        Assert.That(bvh.Nodes[0].RCount, Is.LessThan(0));
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0, 0, 0), new Vec(1, 0, 0), 100f, out _, out _, out _, out _), Is.False);
        Assert.That(BvhTraversal.AnyHitTriangles(bvh, new Vec(0, 0, 0), new Vec(1, 0, 0), 100f), Is.False);
    }

    [Test]
    public void OneTriangle_IsALeafUnderTheRoot()
    {
        float[] pos = { 0, 0, 5, 1, 0, 5, 0, 1, 5 };
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, new[] { 0, 1, 2 });
        Assert.That(bvh.NodeCount, Is.EqualTo(1));
        Assert.That(bvh.Nodes[0].LCount, Is.EqualTo(1));
        Assert.That(bvh.Nodes[0].RCount, Is.LessThan(0));
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0.25f, 0.25f, 0), new Vec(0, 0, 1), 100f, out int tri, out float t, out float u, out float v), Is.True);
        Assert.That(tri, Is.EqualTo(0));
        Assert.That(t, Is.EqualTo(5f).Within(1e-5));
        Assert.That(u, Is.EqualTo(0.25f).Within(1e-5));
        Assert.That(v, Is.EqualTo(0.25f).Within(1e-5));
        // Backfaces are hit too (same triangle from the other side), and tMax cuts it off.
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0.25f, 0.25f, 9), new Vec(0, 0, -1), 100f, out _, out t, out _, out _), Is.True);
        Assert.That(t, Is.EqualTo(4f).Within(1e-5));
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0.25f, 0.25f, 0), new Vec(0, 0, 1), 4.9f, out _, out _, out _, out _), Is.False);
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(2f, 2f, 0), new Vec(0, 0, 1), 100f, out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void AxisAlignedRays_DoNotProduceNaNMisses()
    {
        // Rays with zero direction components, travelling in an axis plane of a box-shaped mesh.
        var rng = new Random(77);
        RandomSoup(rng, 800, 1.0f, out float[] pos, out int[] idx);
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx);
        Vec[] dirs = { new Vec(1, 0, 0), new Vec(0, -1, 0), new Vec(0, 0, 1), new Vec(1, 1, 0), new Vec(0, 1, -1), new Vec(-1, 0, 1) };
        int hits = 0;
        for (int r = 0; r < 600; r++)
        {
            Vec o = new Vec((float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 20f - 10f, (float)rng.NextDouble() * 20f - 10f);
            Vec d = dirs[r % dirs.Length];
            bool b = Brute(bvh, o, d, float.PositiveInfinity, out _, out float bt);
            bool h = BvhTraversal.IntersectTriangles(bvh, o, d, float.PositiveInfinity, out _, out float t, out _, out _);
            Assert.That(h, Is.EqualTo(b));
            if (b)
            {
                hits++;
                Assert.That(t, Is.EqualTo(bt).Within(1e-4).Percent);
            }
        }
        Assert.That(hits, Is.GreaterThan(10));
    }

    [Test]
    public void ClosedMesh_RaysFromInsideNeverEscapeThroughSharedEdges()
    {
        Sphere(4, out float[] pos, out int[] idx); // 5120 triangles
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx);
        var rng = new Random(9);
        for (int r = 0; r < 20000; r++)
        {
            Vec o = new Vec((float)rng.NextDouble() * 0.2f - 0.1f, (float)rng.NextDouble() * 0.2f - 0.1f, (float)rng.NextDouble() * 0.2f - 0.1f);
            Assert.That(BvhTraversal.IntersectTriangles(bvh, o, RandomDir(rng), float.PositiveInfinity, out _, out _, out _, out _), Is.True, "ray " + r + " leaked");
        }
        // Rays exactly through vertices and edge midpoints, where the crack would be.
        for (int v = 0; v < pos.Length / 3; v += 7)
        {
            var d = new Vec(pos[3 * v], pos[3 * v + 1], pos[3 * v + 2]);
            Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0, 0, 0), d, float.PositiveInfinity, out _, out _, out _, out _), Is.True, "vertex " + v);
        }
    }

    [Test]
    public void AllCoincident_DoesNotLoopAndStaysShallow()
    {
        int n = 2000;
        float[] pos = new float[9 * n];
        int[] idx = new int[3 * n];
        for (int t = 0; t < n; t++)
        {
            float[] tri = { 0, 0, 1, 1, 0, 1, 0, 1, 1 };
            Array.Copy(tri, 0, pos, 9 * t, 9);
            for (int k = 0; k < 3; k++) idx[3 * t + k] = 3 * t + k;
        }
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx, 4);
        Assert.That(bvh.Triangles.Length, Is.EqualTo(n));
        Assert.That(CheckStructure(bvh.Nodes, bvh.NodeCount, n), Is.LessThanOrEqualTo(BvhLimits.MaxDepth));
        Assert.That(BvhTraversal.IntersectTriangles(bvh, new Vec(0.2f, 0.2f, 0), new Vec(0, 0, 1), 10f, out _, out float t2, out _, out _), Is.True);
        Assert.That(t2, Is.EqualTo(1f).Within(1e-5));
    }

    [Test]
    public void DegenerateAndInvalidTriangles_AreDropped()
    {
        float nan = float.NaN;
        float[] pos =
        {
            0, 0, 0, 1, 0, 0, 0, 1, 0,       // good (0,1,2)
            0, 0, 0, 1, 1, 1, 2, 2, 2,       // collinear (3,4,5)
            3, 3, 3, 3, 3, 3, 3, 3, 3,       // a point (6,7,8)
            0, 0, 0, nan, 0, 0, 0, 1, 0,     // NaN (9,10,11)
            0, 0, 1, 1, 0, 1, 0, 1, 1,       // good (12,13,14)
        };
        int[] idx = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 0, 1, 99 /* out of range */, 0, 0, 1 /* repeated vertex */ };
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx);
        Assert.That(bvh.Triangles.Length, Is.EqualTo(2));
        var originals = new HashSet<int> { bvh.Triangles[0].Original, bvh.Triangles[1].Original };
        Assert.That(originals, Is.EquivalentTo(new[] { 0, 4 }));
        CheckStructure(bvh.Nodes, bvh.NodeCount, 2);

        TriangleBvh none = BvhBuilder.BuildTriangles(pos, new[] { 3, 4, 5, 6, 7, 8 });
        Assert.That(none.Triangles.Length, Is.EqualTo(0));
        Assert.That(none.NodeCount, Is.EqualTo(1));
    }

    [Test]
    public void SkewedClusters_StayWithinTheDepthCap()
    {
        // Positions spread over many orders of magnitude, the worst case for a binned SAH.
        var rng = new Random(3);
        int n = 20000;
        float[] pos = new float[9 * n];
        int[] idx = new int[3 * n];
        for (int t = 0; t < n; t++)
        {
            float c = (float)Math.Pow(10.0, rng.NextDouble() * 6.0 - 3.0);
            for (int k = 0; k < 3; k++)
            {
                pos[9 * t + 3 * k] = c + (float)rng.NextDouble() * c * 1e-3f;
                pos[9 * t + 3 * k + 1] = c * (float)rng.NextDouble() * 1e-3f;
                pos[9 * t + 3 * k + 2] = (float)rng.NextDouble() * 1e-3f;
                idx[3 * t + k] = 3 * t + k;
            }
        }
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx, 1);
        int depth = CheckStructure(bvh.Nodes, bvh.NodeCount, bvh.Triangles.Length);
        TestContext.Out.WriteLine("skewed depth " + depth);
        Assert.That(depth, Is.LessThanOrEqualTo(BvhLimits.MaxDepth));
    }

    [Test]
    public void Refit_FollowsMovedVertices()
    {
        var rng = new Random(21);
        RandomSoup(rng, 1500, 1.0f, out float[] pos, out int[] idx);
        TriangleBvh bvh = BvhBuilder.BuildTriangles(pos, idx);

        // Shear, squash and jitter every vertex.
        float[] moved = new float[pos.Length];
        for (int i = 0; i < pos.Length; i += 3)
        {
            moved[i] = pos[i] * 0.7f + pos[i + 1] * 0.3f + 2f + (float)rng.NextDouble() * 0.2f;
            moved[i + 1] = pos[i + 1] * 1.4f - 1f;
            moved[i + 2] = pos[i + 2] + (float)Math.Sin(pos[i]) * 0.5f;
        }
        BvhBuilder.Refit(bvh, moved);
        CheckStructure(bvh.Nodes, bvh.NodeCount, bvh.Triangles.Length);
        CheckBoxesContain(bvh);

        // The refitted tree must agree with a brute force over the moved mesh, and with a fresh build.
        TriangleBvh fresh = BvhBuilder.BuildTriangles(moved, idx);
        CompareRays(bvh, rng, 1500, r => new Vec((float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f, (float)r.NextDouble() * 30f - 15f), float.PositiveInfinity);
        for (int r = 0; r < 500; r++)
        {
            Vec o = new Vec((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 30f - 15f), d = RandomDir(rng);
            bool a = BvhTraversal.IntersectTriangles(bvh, o, d, float.PositiveInfinity, out _, out float ta, out _, out _);
            bool b = BvhTraversal.IntersectTriangles(fresh, o, d, float.PositiveInfinity, out _, out float tb, out _, out _);
            Assert.That(a, Is.EqualTo(b));
            if (a) Assert.That(ta, Is.EqualTo(tb).Within(1e-4).Percent);
        }
        Assert.That(bvh.Bounds.MinX, Is.LessThanOrEqualTo(fresh.Bounds.MinX + 1e-4f));
        Assert.That(bvh.Bounds.MaxY, Is.EqualTo(fresh.Bounds.MaxY).Within(1e-4));
    }

    // ---- boxes ---------------------------------------------------------------------------------------------------

    private static Aabb[] RandomBoxes(Random rng, int n, float size)
    {
        var boxes = new Aabb[n];
        for (int i = 0; i < n; i++)
        {
            float x = (float)rng.NextDouble() * 200f - 100f, y = (float)rng.NextDouble() * 200f - 100f, z = (float)rng.NextDouble() * 200f - 100f;
            float sx = (float)rng.NextDouble() * size, sy = (float)rng.NextDouble() * size, sz = (float)rng.NextDouble() * size;
            boxes[i] = new Aabb(x, y, z, x + sx, y + sy, z + sz);
        }
        return boxes;
    }

    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(300, 1)]
    [TestCase(300, 4)]
    [TestCase(4000, 1)]
    public void Boxes_CandidatesMatchBruteForce(int count, int leaf)
    {
        var rng = new Random(count + 11);
        Aabb[] boxes = RandomBoxes(rng, count + 5, 12f); // a few extra beyond `count` that must be ignored
        BoxBvh bvh = BvhBuilder.BuildBoxes(boxes, count, leaf);
        Assert.That(bvh.PrimitiveCount, Is.EqualTo(count));
        int depth = CheckStructure(bvh.Nodes, bvh.NodeCount, count);
        Assert.That(depth, Is.LessThanOrEqualTo(BvhLimits.MaxDepth));

        var seen = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Assert.That(bvh.Order[i], Is.InRange(0, count - 1));
            Assert.That(seen[bvh.Order[i]], Is.False, "Order is a permutation");
            seen[bvh.Order[i]] = true;
        }

        int[] results = new int[count + 8];
        for (int r = 0; r < 300; r++)
        {
            Vec o = new Vec((float)rng.NextDouble() * 300f - 150f, (float)rng.NextDouble() * 300f - 150f, (float)rng.NextDouble() * 300f - 150f);
            Vec d = RandomDir(rng);
            float tMax = r % 2 == 0 ? float.PositiveInfinity : 80f;
            int found = BvhTraversal.IntersectBoxes(bvh, o, d, tMax, results);
            var got = new HashSet<int>();
            for (int i = 0; i < found; i++) Assert.That(got.Add(results[i]), "no duplicates");

            float ix = BvhTraversal.SafeInverse(d.X), iy = BvhTraversal.SafeInverse(d.Y), iz = BvhTraversal.SafeInverse(d.Z);
            var expect = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                Aabb a = boxes[i];
                if (BvhTraversal.Slab(a.MinX, a.MinY, a.MinZ, a.MaxX, a.MaxY, a.MaxZ, o.X, o.Y, o.Z, ix, iy, iz, tMax, out _)) expect.Add(i);
            }
            Assert.That(got, Is.EquivalentTo(expect), "ray " + r);
        }
    }

    [Test]
    public void Boxes_RefitFollowsMovedBoxes()
    {
        var rng = new Random(8);
        Aabb[] boxes = RandomBoxes(rng, 600, 6f);
        BoxBvh bvh = BvhBuilder.BuildBoxes(boxes, boxes.Length, 1);
        for (int i = 0; i < boxes.Length; i++)
        {
            float dx = (float)rng.NextDouble() * 40f - 20f, dy = (float)rng.NextDouble() * 40f - 20f;
            boxes[i] = new Aabb(boxes[i].MinX + dx, boxes[i].MinY + dy, boxes[i].MinZ, boxes[i].MaxX + dx + 3f, boxes[i].MaxY + dy, boxes[i].MaxZ);
        }
        BvhBuilder.RefitBoxes(bvh, boxes);
        int[] results = new int[700];
        for (int r = 0; r < 300; r++)
        {
            Vec o = new Vec((float)rng.NextDouble() * 300f - 150f, (float)rng.NextDouble() * 300f - 150f, (float)rng.NextDouble() * 300f - 150f);
            Vec d = RandomDir(rng);
            int found = BvhTraversal.IntersectBoxes(bvh, o, d, float.PositiveInfinity, results);
            var got = new HashSet<int>();
            for (int i = 0; i < found; i++) got.Add(results[i]);
            float ix = BvhTraversal.SafeInverse(d.X), iy = BvhTraversal.SafeInverse(d.Y), iz = BvhTraversal.SafeInverse(d.Z);
            var expect = new HashSet<int>();
            for (int i = 0; i < boxes.Length; i++)
            {
                Aabb a = boxes[i];
                if (BvhTraversal.Slab(a.MinX, a.MinY, a.MinZ, a.MaxX, a.MaxY, a.MaxZ, o.X, o.Y, o.Z, ix, iy, iz, float.PositiveInfinity, out _)) expect.Add(i);
            }
            Assert.That(got, Is.EquivalentTo(expect), "ray " + r);
        }
    }

    [Test]
    public void Boxes_CoincidentAndReused()
    {
        var boxes = new Aabb[500];
        for (int i = 0; i < boxes.Length; i++) boxes[i] = new Aabb(1, 1, 1, 2, 2, 2);
        BoxBvh bvh = BvhBuilder.BuildBoxes(boxes, boxes.Length, 1);
        Assert.That(CheckStructure(bvh.Nodes, bvh.NodeCount, 500), Is.LessThanOrEqualTo(BvhLimits.MaxDepth));
        int[] results = new int[600];
        Assert.That(BvhTraversal.IntersectBoxes(bvh, new Vec(0, 1.5f, 1.5f), new Vec(1, 0, 0), 10f, results), Is.EqualTo(500));

        // Rebuilding into the same object with fewer boxes reuses its arrays.
        BvhNode[] nodes = bvh.Nodes;
        BoxBvh again = BvhBuilder.BuildBoxes(boxes, 100, 1, bvh);
        Assert.That(again, Is.SameAs(bvh));
        Assert.That(again.Nodes, Is.SameAs(nodes));
        Assert.That(again.PrimitiveCount, Is.EqualTo(100));
        CheckStructure(again.Nodes, again.NodeCount, 100);
        Assert.That(BvhTraversal.IntersectBoxes(again, new Vec(0, 1.5f, 1.5f), new Vec(1, 0, 0), 10f, results), Is.EqualTo(100));
    }

    // ---- timing --------------------------------------------------------------------------------------------------

    [Test]
    public void Timing_LogsBuildSpeed()
    {
        var rng = new Random(2024);
        Sphere(6, out float[] spos, out int[] sidx); // 81,920 triangles
        RandomSoup(rng, 100000, 0.5f, out float[] pos, out int[] idx);

        // Other work shares this machine, so each figure is the best of several runs.
        BvhBuilder.BuildTriangles(pos, idx); // warm up the JIT
        var sw = new Stopwatch();
        double best = double.MaxValue;
        TriangleBvh soup = null;
        for (int i = 0; i < 6; i++)
        {
            sw.Restart();
            soup = BvhBuilder.BuildTriangles(pos, idx);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        TestContext.Out.WriteLine("100k random triangles: " + best.ToString("F1") + " ms, " + soup.NodeCount + " nodes");

        sw.Restart();
        TriangleBvh sphere = BvhBuilder.BuildTriangles(spos, sidx);
        sw.Stop();
        TestContext.Out.WriteLine(sidx.Length / 3 + " sphere triangles: " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms, " + sphere.NodeCount + " nodes");

        sw.Restart();
        BvhBuilder.Refit(sphere, spos);
        sw.Stop();
        TestContext.Out.WriteLine("refit of the sphere: " + sw.Elapsed.TotalMilliseconds.ToString("F2") + " ms");

        Aabb[] boxes = RandomBoxes(rng, 4000, 10f);
        BoxBvh reuse = BvhBuilder.BuildBoxes(boxes, boxes.Length);
        for (int i = 0; i < 20; i++) BvhBuilder.BuildBoxes(boxes, boxes.Length, 1, reuse);
        const int runs = 50;
        best = double.MaxValue;
        for (int batch = 0; batch < 8; batch++)
        {
            sw.Restart();
            for (int i = 0; i < runs; i++) BvhBuilder.BuildBoxes(boxes, boxes.Length, 1, reuse);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / runs);
        }
        TestContext.Out.WriteLine("4000 boxes (reused): " + best.ToString("F3") + " ms per build");

        best = double.MaxValue;
        for (int batch = 0; batch < 8; batch++)
        {
            sw.Restart();
            for (int i = 0; i < runs; i++) BvhBuilder.RefitBoxes(reuse, boxes);
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / runs);
        }
        TestContext.Out.WriteLine("4000 boxes refit: " + best.ToString("F3") + " ms");

        // Trace cost, so changes to the builder can be compared.
        var dirs = new Vec[2000];
        for (int i = 0; i < dirs.Length; i++) dirs[i] = RandomDir(rng);
        sw.Restart();
        int hits = 0;
        foreach (Vec d in dirs)
            if (BvhTraversal.IntersectTriangles(sphere, new Vec(0, 0, 0), d, float.PositiveInfinity, out _, out _, out _, out _)) hits++;
        sw.Stop();
        TestContext.Out.WriteLine("2000 rays in the sphere (" + hits + " hits): " + sw.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
        Assert.That(hits, Is.EqualTo(dirs.Length));
    }
}
