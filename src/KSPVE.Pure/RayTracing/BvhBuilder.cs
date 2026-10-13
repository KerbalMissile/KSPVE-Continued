using System;

namespace KSPVE.Pure.RayTracing;

/// <summary>
/// Builds the bounding volume hierarchies the path tracer walks: a binned surface-area-heuristic (SAH) split on
/// primitive centroids, written straight into the two-wide <see cref="BvhNode"/> layout the GPU reads. Triangle meshes
/// get the full treatment (all three axes); the per-frame top level over instance boxes uses the longest axis only,
/// which is several times faster and nearly as good for boxes that are already well separated.
/// </summary>
public static class BvhBuilder
{
    /// <summary>Bins for the triangle builder (all three axes tried).</summary>
    private const int TriangleBins = 16;

    /// <summary>Bins for the box builder (longest axis only, built every frame).</summary>
    private const int BoxBins = 8;

    private const int MaxBins = 32;

    /// <summary>
    /// Builds a BVH over a triangle mesh. Positions are xyz per vertex, indices three per triangle. Triangles that are
    /// degenerate (zero area, NaN or infinite coordinates, or an index outside the vertex array) are dropped; the
    /// survivors keep their input index in <see cref="GpuTriangle.Original"/>. Leaves hold at most maxLeafSize triangles
    /// except where the depth cap forces a larger one.
    /// </summary>
    public static TriangleBvh BuildTriangles(float[] positions, int[] indices, int maxLeafSize = 4)
    {
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        if (indices == null) throw new ArgumentNullException(nameof(indices));

        int triCount = indices.Length / 3;
        int vertCount = positions.Length / 3;
        Scratch s = Scratch.Get();
        s.EnsurePrims(triCount);
        if (s.Tmp.Length < triCount) s.Tmp = new GpuTriangle[Capacity(triCount)];

        // Prepare each triangle with its edges, then take its box from exactly what the traversal will reconstruct
        // (v0, v0 + e1, v0 + e2), so a box can never be a rounding error smaller than the triangle it holds.
        int n = 0;
        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[3 * t], i1 = indices[3 * t + 1], i2 = indices[3 * t + 2];
            if ((uint)i0 >= (uint)vertCount || (uint)i1 >= (uint)vertCount || (uint)i2 >= (uint)vertCount) continue;
            if (!MakeTriangle(positions, i0, i1, i2, t, out GpuTriangle tri)) continue;
            s.Tmp[n] = tri;
            TriangleBox(in tri, out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
            SetPrim(ref s.Prims[n], n, x0, y0, z0, x1, y1, z1);
            n++;
        }

        var result = new TriangleBvh { SourceIndices = indices };
        BvhNode[] nodes = new BvhNode[Math.Max(1, n)];
        result.Bounds = Run(s, nodes, n, maxLeafSize, TriangleBins, true, out int nodeCount);
        if (nodes.Length != nodeCount) Array.Resize(ref nodes, nodeCount);
        result.Nodes = nodes;
        result.NodeCount = nodeCount;

        var tris = new GpuTriangle[n];
        for (int i = 0; i < n; i++) tris[i] = s.Tmp[s.Prims[i].Id];
        result.Triangles = tris;
        return result;
    }

    /// <summary>
    /// Builds a BVH over boxes (the top level over instances; rebuilt every frame, so it is quick and allocation-free
    /// when a previous <see cref="BoxBvh"/> is passed in to be refilled). Boxes must be valid (min &lt;= max, finite).
    /// </summary>
    public static BoxBvh BuildBoxes(Aabb[] boxes, int count, int maxLeafSize = 1, BoxBvh reuse = null)
    {
        if (boxes == null) throw new ArgumentNullException(nameof(boxes));
        count = Math.Max(0, Math.Min(count, boxes.Length));
        Scratch s = Scratch.Get();
        s.EnsurePrims(count);
        for (int i = 0; i < count; i++)
        {
            ref Aabb a = ref boxes[i];
            SetPrim(ref s.Prims[i], i, a.MinX, a.MinY, a.MinZ, a.MaxX, a.MaxY, a.MaxZ);
        }

        BoxBvh result = reuse ?? new BoxBvh();
        int need = Math.Max(1, count);
        if (result.Nodes.Length < need) result.Nodes = new BvhNode[Capacity(need)];
        if (result.Order.Length < count) result.Order = new int[Capacity(count)];
        result.Bounds = Run(s, result.Nodes, count, maxLeafSize, BoxBins, false, out int nodeCount);
        result.NodeCount = nodeCount;
        result.PrimitiveCount = count;
        for (int i = 0; i < count; i++) result.Order[i] = s.Prims[i].Id;
        return result;
    }

    /// <summary>
    /// Updates a triangle BVH in place after the mesh's vertices moved (same topology, same triangle set): re-reads each
    /// triangle's corners from the new positions, then recomputes the boxes bottom-up. The tree shape is kept, so heavy
    /// deformation slowly degrades quality; rebuild if the mesh changes a lot.
    /// </summary>
    public static void Refit(TriangleBvh bvh, float[] positions)
    {
        if (bvh == null) throw new ArgumentNullException(nameof(bvh));
        if (positions == null) throw new ArgumentNullException(nameof(positions));
        int[] indices = bvh.SourceIndices;
        GpuTriangle[] tris = bvh.Triangles;
        for (int i = 0; i < tris.Length; i++)
        {
            int o = tris[i].Original;
            int i0 = indices[3 * o], i1 = indices[3 * o + 1], i2 = indices[3 * o + 2];
            // A triangle that has collapsed keeps its edges (zero or NaN): it simply stops being hit.
            tris[i].V0X = positions[3 * i0]; tris[i].V0Y = positions[3 * i0 + 1]; tris[i].V0Z = positions[3 * i0 + 2];
            tris[i].E1X = positions[3 * i1] - positions[3 * i0];
            tris[i].E1Y = positions[3 * i1 + 1] - positions[3 * i0 + 1];
            tris[i].E1Z = positions[3 * i1 + 2] - positions[3 * i0 + 2];
            tris[i].E2X = positions[3 * i2] - positions[3 * i0];
            tris[i].E2Y = positions[3 * i2 + 1] - positions[3 * i0 + 1];
            tris[i].E2Z = positions[3 * i2 + 2] - positions[3 * i0 + 2];
        }

        // Children always have a higher index than their parent, so one backwards sweep sees every child first.
        BvhNode[] nodes = bvh.Nodes;
        for (int ni = bvh.NodeCount - 1; ni >= 0; ni--)
        {
            ref BvhNode nd = ref nodes[ni];
            Aabb l = ChildBoxForRefit(nodes, tris, nd.LIndex, nd.LCount, nd.LMinX, nd.LMinY, nd.LMinZ, nd.LMaxX, nd.LMaxY, nd.LMaxZ);
            Aabb r = ChildBoxForRefit(nodes, tris, nd.RIndex, nd.RCount, nd.RMinX, nd.RMinY, nd.RMinZ, nd.RMaxX, nd.RMaxY, nd.RMaxZ);
            nd.LMinX = l.MinX; nd.LMinY = l.MinY; nd.LMinZ = l.MinZ; nd.LMaxX = l.MaxX; nd.LMaxY = l.MaxY; nd.LMaxZ = l.MaxZ;
            nd.RMinX = r.MinX; nd.RMinY = r.MinY; nd.RMinZ = r.MinZ; nd.RMaxX = r.MaxX; nd.RMaxY = r.MaxY; nd.RMaxZ = r.MaxZ;
        }

        Aabb b = Aabb.Empty;
        if (bvh.NodeCount > 0)
        {
            ref BvhNode root = ref nodes[0];
            if (root.LCount >= 0) b.Union(new Aabb(root.LMinX, root.LMinY, root.LMinZ, root.LMaxX, root.LMaxY, root.LMaxZ));
            if (root.RCount >= 0) b.Union(new Aabb(root.RMinX, root.RMinY, root.RMinZ, root.RMaxX, root.RMaxY, root.RMaxZ));
        }
        bvh.Bounds = b;
    }

    /// <summary>
    /// Updates a box BVH in place after the boxes moved or resized (same count, same indices): far cheaper than a
    /// rebuild, so a per-frame top level can refit most frames and rebuild every so often as the tree's quality drifts.
    /// </summary>
    public static void RefitBoxes(BoxBvh bvh, Aabb[] boxes)
    {
        if (bvh == null) throw new ArgumentNullException(nameof(bvh));
        if (boxes == null) throw new ArgumentNullException(nameof(boxes));
        BvhNode[] nodes = bvh.Nodes;
        int[] order = bvh.Order;
        for (int ni = bvh.NodeCount - 1; ni >= 0; ni--)
        {
            ref BvhNode nd = ref nodes[ni];
            for (int side = 0; side < 2; side++)
            {
                int index = side == 0 ? nd.LIndex : nd.RIndex, count = side == 0 ? nd.LCount : nd.RCount;
                if (count < 0) continue;
                Aabb b = Aabb.Empty;
                if (count > 0)
                {
                    for (int i = index; i < index + count; i++) b.Union(in boxes[order[i]]);
                }
                else
                {
                    ref BvhNode c = ref nodes[index];
                    if (c.LCount >= 0) b.Union(new Aabb(c.LMinX, c.LMinY, c.LMinZ, c.LMaxX, c.LMaxY, c.LMaxZ));
                    if (c.RCount >= 0) b.Union(new Aabb(c.RMinX, c.RMinY, c.RMinZ, c.RMaxX, c.RMaxY, c.RMaxZ));
                }
                if (side == 0)
                {
                    nd.LMinX = b.MinX; nd.LMinY = b.MinY; nd.LMinZ = b.MinZ; nd.LMaxX = b.MaxX; nd.LMaxY = b.MaxY; nd.LMaxZ = b.MaxZ;
                }
                else
                {
                    nd.RMinX = b.MinX; nd.RMinY = b.MinY; nd.RMinZ = b.MinZ; nd.RMaxX = b.MaxX; nd.RMaxY = b.MaxY; nd.RMaxZ = b.MaxZ;
                }
            }
        }

        Aabb all = Aabb.Empty;
        if (bvh.NodeCount > 0)
        {
            ref BvhNode root = ref nodes[0];
            if (root.LCount >= 0) all.Union(new Aabb(root.LMinX, root.LMinY, root.LMinZ, root.LMaxX, root.LMaxY, root.LMaxZ));
            if (root.RCount >= 0) all.Union(new Aabb(root.RMinX, root.RMinY, root.RMinZ, root.RMaxX, root.RMaxY, root.RMaxZ));
        }
        bvh.Bounds = all;
    }

    private static Aabb ChildBoxForRefit(BvhNode[] nodes, GpuTriangle[] tris, int index, int count,
        float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        if (count < 0) return new Aabb(minX, minY, minZ, maxX, maxY, maxZ); // empty: keep the inverted box
        Aabb b = Aabb.Empty;
        if (count > 0)
        {
            for (int i = index; i < index + count; i++)
            {
                TriangleBox(in tris[i], out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
                b.Union(new Aabb(x0, y0, z0, x1, y1, z1));
            }
            return b;
        }
        ref BvhNode c = ref nodes[index];
        if (c.LCount >= 0) b.Union(new Aabb(c.LMinX, c.LMinY, c.LMinZ, c.LMaxX, c.LMaxY, c.LMaxZ));
        if (c.RCount >= 0) b.Union(new Aabb(c.RMinX, c.RMinY, c.RMinZ, c.RMaxX, c.RMaxY, c.RMaxZ));
        return b;
    }

    private static int Capacity(int n)
    {
        // Round up so a per-frame rebuild with a slowly changing count doesn't reallocate every frame.
        return n + (n >> 2) + 16;
    }

    private static bool MakeTriangle(float[] p, int i0, int i1, int i2, int original, out GpuTriangle t)
    {
        float v0x = p[3 * i0], v0y = p[3 * i0 + 1], v0z = p[3 * i0 + 2];
        float e1x = p[3 * i1] - v0x, e1y = p[3 * i1 + 1] - v0y, e1z = p[3 * i1 + 2] - v0z;
        float e2x = p[3 * i2] - v0x, e2y = p[3 * i2 + 1] - v0y, e2z = p[3 * i2 + 2] - v0z;
        float cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
        float area2 = cx * cx + cy * cy + cz * cz;
        t = default;
        // NaN and infinity both fail this (every comparison with NaN is false; infinity is excluded explicitly).
        if (!(area2 > 0f) || float.IsInfinity(area2)) return false;
        t.V0X = v0x; t.V0Y = v0y; t.V0Z = v0z; t.Original = original;
        t.E1X = e1x; t.E1Y = e1y; t.E1Z = e1z;
        t.E2X = e2x; t.E2Y = e2y; t.E2Z = e2z;
        return true;
    }

    private static void TriangleBox(in GpuTriangle t, out float x0, out float y0, out float z0, out float x1, out float y1, out float z1)
    {
        float ax = t.V0X + t.E1X, ay = t.V0Y + t.E1Y, az = t.V0Z + t.E1Z;
        float bx = t.V0X + t.E2X, by = t.V0Y + t.E2Y, bz = t.V0Z + t.E2Z;
        x0 = Math.Min(t.V0X, Math.Min(ax, bx)); x1 = Math.Max(t.V0X, Math.Max(ax, bx));
        y0 = Math.Min(t.V0Y, Math.Min(ay, by)); y1 = Math.Max(t.V0Y, Math.Max(ay, by));
        z0 = Math.Min(t.V0Z, Math.Min(az, bz)); z1 = Math.Max(t.V0Z, Math.Max(az, bz));
    }

    /// <summary>Builds the whole tree over the scratch primitives; returns the overall bounds.</summary>
    private static Aabb Run(Scratch s, BvhNode[] nodes, int n, int maxLeaf, int bins, bool allAxes, out int nodeCount)
    {
        s.Nodes = nodes;
        s.NodeCount = 0;
        s.MaxLeaf = Math.Max(1, maxLeaf);
        s.Bins = Math.Max(2, Math.Min(bins, MaxBins));
        s.AllAxes = allAxes;

        Aabb box = RangeBox(s, 0, n);
        if (n == 0)
        {
            nodes[0] = default;
            SetEmpty(ref nodes[0], false);
            SetEmpty(ref nodes[0], true);
            s.NodeCount = 1;
        }
        else
        {
            Plan plan = ChooseSplit(s, 0, n);
            if (ShouldBeLeaf(s, n, in box, in plan))
            {
                nodes[0] = default;
                SetChild(ref nodes[0], false, in box, 0, n);
                SetEmpty(ref nodes[0], true);
                s.NodeCount = 1;
            }
            else
            {
                BuildNode(s, 0, n, in plan, 1);
            }
        }
        nodeCount = s.NodeCount;
        return box;
    }

    /// <summary>True when a range should simply be a leaf rather than split further.</summary>
    private static bool ShouldBeLeaf(Scratch s, int n, in Aabb box, in Plan plan)
    {
        if (n <= 1) return true;
        if (n > s.MaxLeaf) return false;
        float area = box.HalfArea();
        if (!(area > 0f) || plan.Axis < 0) return true;
        float splitCost = BvhLimits.TraversalCost + BvhLimits.IntersectionCost * plan.Sum / area;
        return n * BvhLimits.IntersectionCost <= splitCost;
    }

    /// <summary>Creates the node for [start, end) with the chosen split, builds both children, and returns its index.</summary>
    private static int BuildNode(Scratch s, int start, int end, in Plan plan, int depth)
    {
        int self = s.NodeCount++;
        int mid = Partition(s, start, end, in plan, out Aabb lb, out Aabb rb);

        MakeChild(s, start, mid, in lb, depth, out int li, out int lc);
        MakeChild(s, mid, end, in rb, depth, out int ri, out int rc);

        ref BvhNode nd = ref s.Nodes[self];
        SetChild(ref nd, false, in lb, li, lc);
        SetChild(ref nd, true, in rb, ri, rc);
        return self;
    }

    private static void MakeChild(Scratch s, int start, int end, in Aabb box, int parentDepth, out int index, out int count)
    {
        int n = end - start;
        if (n <= 0)
        {
            index = 0;
            count = -1;
            return;
        }
        if (n == 1 || parentDepth >= BvhLimits.MaxDepth)
        {
            index = start;
            count = n;
            return;
        }
        Plan plan = ChooseSplit(s, start, end);
        if (ShouldBeLeaf(s, n, in box, in plan))
        {
            index = start;
            count = n;
            return;
        }
        index = BuildNode(s, start, end, in plan, parentDepth + 1);
        count = 0;
    }

    /// <summary>A chosen split: the axis (-1 = plain median split), the bin boundary, and the SAH sum area*count of both sides.</summary>
    private struct Plan
    {
        public int Axis;
        public int Bin; // primitives in bins 0..Bin go left
        public int Bins; // how many bins the plan used
        public float CMin, Scale;
        public float Sum;
    }

    private static float Centroid(ref Prim p, int axis)
    {
        return axis == 0 ? p.CX : axis == 1 ? p.CY : p.CZ;
    }

    /// <summary>Bins the range's centroids and finds the cheapest SAH split; falls back to the median when no axis has any spread.</summary>
    private static Plan ChooseSplit(Scratch s, int start, int end)
    {
        Prim[] prims = s.Prims;
        int n = end - start;
        // Small ranges don't need many bins; the sweeps would cost more than the binning.
        int B = n <= 8 ? 4 : n <= 32 ? 8 : s.Bins;

        // Centroid bounds of the range.
        float cx0 = float.PositiveInfinity, cy0 = float.PositiveInfinity, cz0 = float.PositiveInfinity;
        float cx1 = float.NegativeInfinity, cy1 = float.NegativeInfinity, cz1 = float.NegativeInfinity;
        for (int i = start; i < end; i++)
        {
            ref Prim p = ref prims[i];
            if (p.CX < cx0) cx0 = p.CX;
            if (p.CX > cx1) cx1 = p.CX;
            if (p.CY < cy0) cy0 = p.CY;
            if (p.CY > cy1) cy1 = p.CY;
            if (p.CZ < cz0) cz0 = p.CZ;
            if (p.CZ > cz1) cz1 = p.CZ;
        }

        float ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
        int first = 0, last = 2;
        if (!s.AllAxes)
        {
            int longest = (ex >= ey && ex >= ez) ? 0 : (ey >= ez ? 1 : 2);
            first = last = longest;
        }

        float[] bmin = s.BinMin, bmax = s.BinMax;
        int[] bcnt = s.BinCount;
        float[] rArea = s.RightArea;
        int[] rCount = s.RightCount;
        var best = new Plan { Axis = -1, Sum = float.PositiveInfinity, Bins = B };

        for (int axis = first; axis <= last; axis++)
        {
            float cmin = axis == 0 ? cx0 : axis == 1 ? cy0 : cz0;
            float ext = axis == 0 ? ex : axis == 1 ? ey : ez;
            if (!(ext > 0f)) continue;
            float scale = B / ext;
            if (float.IsInfinity(scale)) continue;

            int baseBin = axis * MaxBins;
            for (int b = 0; b < B; b++)
            {
                int k = baseBin + b;
                bcnt[k] = 0;
                int m = 3 * k;
                bmin[m] = bmin[m + 1] = bmin[m + 2] = float.PositiveInfinity;
                bmax[m] = bmax[m + 1] = bmax[m + 2] = float.NegativeInfinity;
            }
            for (int i = start; i < end; i++)
            {
                ref Prim p = ref prims[i];
                int k = baseBin + BinOf(Centroid(ref p, axis), cmin, scale, B);
                bcnt[k]++;
                int m = 3 * k;
                if (p.MinX < bmin[m]) bmin[m] = p.MinX;
                if (p.MinY < bmin[m + 1]) bmin[m + 1] = p.MinY;
                if (p.MinZ < bmin[m + 2]) bmin[m + 2] = p.MinZ;
                if (p.MaxX > bmax[m]) bmax[m] = p.MaxX;
                if (p.MaxY > bmax[m + 1]) bmax[m + 1] = p.MaxY;
                if (p.MaxZ > bmax[m + 2]) bmax[m + 2] = p.MaxZ;
            }

            // Sweep from the right to get each suffix's area and count, then from the left to price every split.
            float rx0 = float.PositiveInfinity, ry0 = rx0, rz0 = rx0, rx1 = float.NegativeInfinity, ry1 = rx1, rz1 = rx1;
            int rn = 0;
            for (int b = B - 1; b >= 1; b--)
            {
                int k = baseBin + b, m = 3 * k;
                rn += bcnt[k];
                if (bmin[m] < rx0) rx0 = bmin[m];
                if (bmin[m + 1] < ry0) ry0 = bmin[m + 1];
                if (bmin[m + 2] < rz0) rz0 = bmin[m + 2];
                if (bmax[m] > rx1) rx1 = bmax[m];
                if (bmax[m + 1] > ry1) ry1 = bmax[m + 1];
                if (bmax[m + 2] > rz1) rz1 = bmax[m + 2];
                rCount[b] = rn;
                rArea[b] = rn > 0 ? HalfArea(rx0, ry0, rz0, rx1, ry1, rz1) : 0f;
            }
            float lx0 = float.PositiveInfinity, ly0 = lx0, lz0 = lx0, lx1 = float.NegativeInfinity, ly1 = lx1, lz1 = lx1;
            int ln = 0;
            for (int b = 0; b < B - 1; b++)
            {
                int k = baseBin + b, m = 3 * k;
                ln += bcnt[k];
                if (bmin[m] < lx0) lx0 = bmin[m];
                if (bmin[m + 1] < ly0) ly0 = bmin[m + 1];
                if (bmin[m + 2] < lz0) lz0 = bmin[m + 2];
                if (bmax[m] > lx1) lx1 = bmax[m];
                if (bmax[m + 1] > ly1) ly1 = bmax[m + 1];
                if (bmax[m + 2] > lz1) lz1 = bmax[m + 2];
                int rc = rCount[b + 1];
                if (ln == 0 || rc == 0) continue;
                float sum = HalfArea(lx0, ly0, lz0, lx1, ly1, lz1) * ln + rArea[b + 1] * rc;
                if (sum < best.Sum)
                {
                    best.Sum = sum;
                    best.Axis = axis;
                    best.Bin = b;
                    best.CMin = cmin;
                    best.Scale = scale;
                }
            }
        }

        // Every axis keeps its own slice of the bin arrays, so the winner's bins are still intact for Partition to merge
        // into the child boxes.
        return best;
    }

    /// <summary>
    /// Splits [start, end) by the plan (reordering the primitives in place) and returns the first index of the right
    /// half. When the plan is a median split, or the bins put everything on one side, it halves the range by count
    /// instead, so the recursion always makes progress (duplicates, coincident centroids, NaN).
    /// </summary>
    private static int Partition(Scratch s, int start, int end, in Plan plan, out Aabb lb, out Aabb rb)
    {
        Prim[] prims = s.Prims;
        int mid;
        if (plan.Axis >= 0)
        {
            int B = plan.Bins, axis = plan.Axis, bin = plan.Bin;
            float cmin = plan.CMin, scale = plan.Scale;
            int i = start, j = end - 1;
            while (i <= j)
            {
                if (BinOf(Centroid(ref prims[i], axis), cmin, scale, B) <= bin) i++;
                else
                {
                    Prim tmp = prims[i];
                    prims[i] = prims[j];
                    prims[j] = tmp;
                    j--;
                }
            }
            mid = i;
            if (mid > start && mid < end)
            {
                // Child boxes come free from the bins, which still hold the winning axis.
                int baseBin = axis * MaxBins;
                lb = Aabb.Empty;
                rb = Aabb.Empty;
                for (int b = 0; b < B; b++)
                {
                    int k = baseBin + b, m = 3 * k;
                    if (s.BinCount[k] == 0) continue;
                    var bb = new Aabb(s.BinMin[m], s.BinMin[m + 1], s.BinMin[m + 2], s.BinMax[m], s.BinMax[m + 1], s.BinMax[m + 2]);
                    if (b <= bin) lb.Union(in bb); else rb.Union(in bb);
                }
                return mid;
            }
        }

        mid = (start + end) >> 1;
        lb = RangeBox(s, start, mid);
        rb = RangeBox(s, mid, end);
        return mid;
    }

    private static Aabb RangeBox(Scratch s, int start, int end)
    {
        Aabb b = Aabb.Empty;
        Prim[] prims = s.Prims;
        for (int i = start; i < end; i++)
        {
            ref Prim p = ref prims[i];
            if (p.MinX < b.MinX) b.MinX = p.MinX;
            if (p.MinY < b.MinY) b.MinY = p.MinY;
            if (p.MinZ < b.MinZ) b.MinZ = p.MinZ;
            if (p.MaxX > b.MaxX) b.MaxX = p.MaxX;
            if (p.MaxY > b.MaxY) b.MaxY = p.MaxY;
            if (p.MaxZ > b.MaxZ) b.MaxZ = p.MaxZ;
        }
        return b;
    }

    private static int BinOf(float c, float cmin, float scale, int bins)
    {
        float f = (c - cmin) * scale;
        if (!(f >= 0f)) return 0; // also catches NaN
        int b = (int)f;
        return b >= bins ? bins - 1 : b;
    }

    private static float HalfArea(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
        return dx * dy + dy * dz + dz * dx;
    }

    private static void SetChild(ref BvhNode n, bool right, in Aabb b, int index, int count)
    {
        if (count < 0)
        {
            SetEmpty(ref n, right);
            return;
        }
        if (!right)
        {
            n.LMinX = b.MinX; n.LMinY = b.MinY; n.LMinZ = b.MinZ; n.LIndex = index;
            n.LMaxX = b.MaxX; n.LMaxY = b.MaxY; n.LMaxZ = b.MaxZ; n.LCount = count;
        }
        else
        {
            n.RMinX = b.MinX; n.RMinY = b.MinY; n.RMinZ = b.MinZ; n.RIndex = index;
            n.RMaxX = b.MaxX; n.RMaxY = b.MaxY; n.RMaxZ = b.MaxZ; n.RCount = count;
        }
    }

    private static void SetEmpty(ref BvhNode n, bool right)
    {
        const float inf = float.PositiveInfinity;
        if (!right)
        {
            n.LMinX = n.LMinY = n.LMinZ = inf; n.LIndex = 0;
            n.LMaxX = n.LMaxY = n.LMaxZ = -inf; n.LCount = -1;
        }
        else
        {
            n.RMinX = n.RMinY = n.RMinZ = inf; n.RIndex = 0;
            n.RMaxX = n.RMaxY = n.RMaxZ = -inf; n.RCount = -1;
        }
    }

    /// <summary>
    /// One primitive while building: its box, centroid and id, kept together so partitioning moves one record and the
    /// binning passes read memory in order.
    /// </summary>
    private struct Prim
    {
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public float CX, CY, CZ;
        public int Id;
    }

    private static void SetPrim(ref Prim p, int id, float x0, float y0, float z0, float x1, float y1, float z1)
    {
        p.MinX = x0; p.MinY = y0; p.MinZ = z0; p.MaxX = x1; p.MaxY = y1; p.MaxZ = z1;
        p.CX = 0.5f * (x0 + x1); p.CY = 0.5f * (y0 + y1); p.CZ = 0.5f * (z0 + z1);
        p.Id = id;
    }

    /// <summary>Per-thread working storage, reused between builds so a per-frame rebuild allocates nothing.</summary>
    private sealed class Scratch
    {
        [ThreadStatic] private static Scratch _instance;

        public Prim[] Prims = new Prim[0];
        public GpuTriangle[] Tmp = new GpuTriangle[0];

        public readonly float[] BinMin = new float[3 * MaxBins * 3];
        public readonly float[] BinMax = new float[3 * MaxBins * 3];
        public readonly int[] BinCount = new int[3 * MaxBins];
        public readonly float[] RightArea = new float[MaxBins];
        public readonly int[] RightCount = new int[MaxBins];

        public BvhNode[] Nodes;
        public int NodeCount;
        public int MaxLeaf;
        public int Bins;
        public bool AllAxes;

        public static Scratch Get()
        {
            return _instance ?? (_instance = new Scratch());
        }

        public void EnsurePrims(int n)
        {
            if (Prims.Length < n) Prims = new Prim[Capacity(n)];
        }
    }
}
