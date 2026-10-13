using System;

namespace KSPVE.Pure.RayTracing;

/// <summary>
/// The CPU reference for the GPU traversal: the same loop, in the same order, so the HLSL can mirror it line by line.
/// A ray is origin + t * direction for t in (0, tMax). The direction need not be normalised (t is in units of its
/// length). Backfaces are not culled.
///
/// The loop: start at node 0 with an empty stack of <see cref="BvhLimits.StackSize"/> ints. At each node, slab-test both
/// child boxes against [0, tMax] using the reciprocal direction; hit children are taken nearer first. A hit leaf has its
/// triangles tested at once (which may shorten tMax); the first hit interior child becomes the next node and a second
/// one is pushed. When there is no next node, pop the stack; stop when it is empty.
/// </summary>
public static class BvhTraversal
{
    /// <summary>Clamps a direction component away from zero (sign kept, +0 counts as positive) and returns its reciprocal.</summary>
    public static float SafeInverse(float d)
    {
        if (d >= 0f) return 1f / (d < BvhLimits.MinDirection ? BvhLimits.MinDirection : d);
        return 1f / (d > -BvhLimits.MinDirection ? -BvhLimits.MinDirection : d);
    }

    /// <summary>
    /// Ray vs box slab test over [0, tMax] with a precomputed reciprocal direction. Returns whether it hits, and the
    /// distance at which the ray enters (0 if the origin is inside), used to order the two children.
    /// </summary>
    public static bool Slab(float minX, float minY, float minZ, float maxX, float maxY, float maxZ,
        float ox, float oy, float oz, float ix, float iy, float iz, float tMax, out float tNear)
    {
        float t0 = (minX - ox) * ix, t1 = (maxX - ox) * ix;
        float tn = Math.Min(t0, t1), tf = Math.Max(t0, t1);
        t0 = (minY - oy) * iy; t1 = (maxY - oy) * iy;
        tn = Math.Max(tn, Math.Min(t0, t1)); tf = Math.Min(tf, Math.Max(t0, t1));
        t0 = (minZ - oz) * iz; t1 = (maxZ - oz) * iz;
        tn = Math.Max(tn, Math.Min(t0, t1)); tf = Math.Min(tf, Math.Max(t0, t1));
        tNear = Math.Max(tn, 0f);
        return tNear <= Math.Min(tf * BvhLimits.SlabPad, tMax);
    }

    /// <summary>
    /// Moller-Trumbore with the stored edges. Returns true for a hit with 0 &lt; t &lt; tMax and gives the distance and
    /// the barycentrics (the hit point is v0 + u * e1 + v * e2).
    /// </summary>
    public static bool IntersectTriangle(in GpuTriangle tri, float ox, float oy, float oz, float dx, float dy, float dz,
        float tMax, out float t, out float u, out float v)
    {
        t = u = v = 0f;
        float px = dy * tri.E2Z - dz * tri.E2Y, py = dz * tri.E2X - dx * tri.E2Z, pz = dx * tri.E2Y - dy * tri.E2X;
        float det = tri.E1X * px + tri.E1Y * py + tri.E1Z * pz;
        if (Math.Abs(det) < 1e-30f) return false; // parallel (or NaN, which also fails the later tests)
        float inv = 1f / det;
        float tx = ox - tri.V0X, ty = oy - tri.V0Y, tz = oz - tri.V0Z;
        float uu = (tx * px + ty * py + tz * pz) * inv;
        if (!(uu >= -BvhLimits.EdgeSlack && uu <= 1f + BvhLimits.EdgeSlack)) return false;
        float qx = ty * tri.E1Z - tz * tri.E1Y, qy = tz * tri.E1X - tx * tri.E1Z, qz = tx * tri.E1Y - ty * tri.E1X;
        float vv = (dx * qx + dy * qy + dz * qz) * inv;
        if (!(vv >= -BvhLimits.EdgeSlack && uu + vv <= 1f + BvhLimits.EdgeSlack)) return false;
        float tt = (tri.E2X * qx + tri.E2Y * qy + tri.E2Z * qz) * inv;
        if (!(tt > 0f && tt < tMax)) return false;
        t = tt;
        u = uu;
        v = vv;
        return true;
    }

    /// <summary>
    /// Nearest hit along the ray within (0, tMax). Returns true and the triangle's slot in
    /// <see cref="TriangleBvh.Triangles"/> (its mesh index is Triangles[slot].Original), the distance and barycentrics.
    /// </summary>
    public static bool IntersectTriangles(TriangleBvh bvh, Vec origin, Vec direction, float tMax,
        out int triangle, out float t, out float u, out float v)
    {
        return Traverse(bvh, origin, direction, tMax, false, out triangle, out t, out u, out v);
    }

    /// <summary>Any hit along the ray within (0, tMax), stopping at the first found (shadow rays). The result is whichever triangle was found first.</summary>
    public static bool AnyHitTriangles(TriangleBvh bvh, Vec origin, Vec direction, float tMax)
    {
        return Traverse(bvh, origin, direction, tMax, true, out _, out _, out _, out _);
    }

    private static bool Traverse(TriangleBvh bvh, Vec origin, Vec direction, float tMax, bool anyHit,
        out int triangle, out float t, out float u, out float v)
    {
        triangle = -1;
        t = u = v = 0f;
        if (bvh == null || bvh.NodeCount == 0) return false;

        float ox = origin.X, oy = origin.Y, oz = origin.Z;
        float dx = direction.X, dy = direction.Y, dz = direction.Z;
        float ix = SafeInverse(dx), iy = SafeInverse(dy), iz = SafeInverse(dz);
        BvhNode[] nodes = bvh.Nodes;
        GpuTriangle[] tris = bvh.Triangles;
        int[] stack = new int[BvhLimits.StackSize];
        int sp = 0;
        int node = 0;
        bool found = false;

        while (true)
        {
            ref BvhNode n = ref nodes[node];
            float tl = 0f, tr = 0f;
            bool hitL = n.LCount >= 0 && Slab(n.LMinX, n.LMinY, n.LMinZ, n.LMaxX, n.LMaxY, n.LMaxZ, ox, oy, oz, ix, iy, iz, tMax, out tl);
            bool hitR = n.RCount >= 0 && Slab(n.RMinX, n.RMinY, n.RMinZ, n.RMaxX, n.RMaxY, n.RMaxZ, ox, oy, oz, ix, iy, iz, tMax, out tr);

            // Visit order: the nearer hit child first.
            bool leftFirst = !(hitL && hitR) || tl <= tr;
            int next = -1;
            for (int pass = 0; pass < 2; pass++)
            {
                bool right = (pass == 0) != leftFirst;
                if (!(right ? hitR : hitL)) continue;
                int index = right ? n.RIndex : n.LIndex;
                int count = right ? n.RCount : n.LCount;
                if (count > 0)
                {
                    for (int i = index; i < index + count; i++)
                    {
                        if (IntersectTriangle(in tris[i], ox, oy, oz, dx, dy, dz, tMax, out float tt, out float uu, out float vv))
                        {
                            tMax = tt;
                            triangle = i;
                            t = tt;
                            u = uu;
                            v = vv;
                            found = true;
                            if (anyHit) return true;
                        }
                    }
                }
                else if (next < 0)
                {
                    next = index;
                }
                else
                {
                    stack[sp++] = index; // the nearer one is already `next`, so this is the farther
                }
            }

            if (next >= 0) node = next;
            else if (sp > 0) node = stack[--sp];
            else break;
        }
        return found;
    }

    /// <summary>
    /// The top level: collects every primitive whose box the ray crosses within (0, tMax] into results (unordered),
    /// returning how many; the caller then tests each (e.g. transforms the ray into the instance and walks its own
    /// BVH). Stops silently when results is full.
    /// </summary>
    public static int IntersectBoxes(BoxBvh bvh, Vec origin, Vec direction, float tMax, int[] results)
    {
        if (bvh == null || bvh.NodeCount == 0 || results == null) return 0;
        float ox = origin.X, oy = origin.Y, oz = origin.Z;
        float ix = SafeInverse(direction.X), iy = SafeInverse(direction.Y), iz = SafeInverse(direction.Z);
        BvhNode[] nodes = bvh.Nodes;
        int[] stack = new int[BvhLimits.StackSize];
        int sp = 0, node = 0, found = 0;

        while (true)
        {
            ref BvhNode n = ref nodes[node];
            int next = -1;
            for (int pass = 0; pass < 2; pass++)
            {
                bool right = pass == 1;
                int count = right ? n.RCount : n.LCount;
                if (count < 0) continue;
                bool hit = right
                    ? Slab(n.RMinX, n.RMinY, n.RMinZ, n.RMaxX, n.RMaxY, n.RMaxZ, ox, oy, oz, ix, iy, iz, tMax, out _)
                    : Slab(n.LMinX, n.LMinY, n.LMinZ, n.LMaxX, n.LMaxY, n.LMaxZ, ox, oy, oz, ix, iy, iz, tMax, out _);
                if (!hit) continue;
                int index = right ? n.RIndex : n.LIndex;
                if (count > 0)
                {
                    for (int i = index; i < index + count; i++)
                    {
                        if (found >= results.Length) return found;
                        results[found++] = bvh.Order[i];
                    }
                }
                else if (next < 0) next = index;
                else stack[sp++] = index;
            }
            if (next >= 0) node = next;
            else if (sp > 0) node = stack[--sp];
            else break;
        }
        return found;
    }
}
