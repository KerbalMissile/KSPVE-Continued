using System;
using System.Runtime.InteropServices;

namespace KSPVE.Pure.RayTracing;

/// <summary>Limits and cost constants shared by the CPU builder, the CPU reference traversal and the GPU traversal.</summary>
public static class BvhLimits
{
    /// <summary>
    /// The deepest a tree may be, counting the root as depth 1. The GPU traversal keeps a fixed stack of this many
    /// ints; a descent pushes at most one entry per level below the root, so 64 entries can never overflow.
    /// </summary>
    public const int MaxDepth = 64;

    /// <summary>Size of the traversal stack (ints) the GPU and the reference traversal use.</summary>
    public const int StackSize = 64;

    /// <summary>Relative cost of visiting a node, in units of one triangle test (surface area heuristic).</summary>
    public const float TraversalCost = 1.0f;

    /// <summary>Relative cost of one primitive test (surface area heuristic).</summary>
    public const float IntersectionCost = 1.0f;

    /// <summary>
    /// Ray direction components smaller than this in magnitude are replaced by it (keeping the sign, +0 counting as
    /// positive) before the reciprocal is taken, so the slab test never meets an infinity or a NaN.
    /// </summary>
    public const float MinDirection = 1e-20f;

    /// <summary>
    /// The slab test's exit distance is scaled up by this (about ten rounding errors) so a ray passing exactly through a
    /// box's corner or edge, as rays aimed at mesh vertices do, is not lost to rounding in the box test while the
    /// triangle test (with its <see cref="EdgeSlack"/>) still accepts it.
    /// </summary>
    public const float SlabPad = 1.0000012f;

    /// <summary>
    /// Slack on the barycentric edge tests of the triangle intersection. Two triangles sharing an edge compute their
    /// barycentrics from different base vertices, so a ray through the edge could slip between them by one rounding
    /// error; this closes that crack (a ray in the slack may hit both, which is harmless for nearest/any hit).
    /// </summary>
    public const float EdgeSlack = 1e-6f;
}

/// <summary>A float 3-vector for ray work on the CPU (the GPU side is a float3).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vec
{
    public float X, Y, Z;

    public Vec(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>An axis-aligned box. An empty box has min = +infinity and max = -infinity, so any union with it is a no-op.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct Aabb
{
    public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

    public Aabb(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
    {
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    /// <summary>The box that contains nothing.</summary>
    public static Aabb Empty => new Aabb(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity,
        float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

    /// <summary>True when the box contains nothing (inverted on any axis).</summary>
    public bool IsEmpty => MinX > MaxX || MinY > MaxY || MinZ > MaxZ;

    /// <summary>Grows this box to contain another.</summary>
    public void Union(in Aabb o)
    {
        if (o.MinX < MinX) MinX = o.MinX;
        if (o.MinY < MinY) MinY = o.MinY;
        if (o.MinZ < MinZ) MinZ = o.MinZ;
        if (o.MaxX > MaxX) MaxX = o.MaxX;
        if (o.MaxY > MaxY) MaxY = o.MaxY;
        if (o.MaxZ > MaxZ) MaxZ = o.MaxZ;
    }

    /// <summary>Half the surface area (what the surface area heuristic needs); 0 for an empty box.</summary>
    public float HalfArea()
    {
        if (IsEmpty) return 0f;
        float dx = MaxX - MinX, dy = MaxY - MinY, dz = MaxZ - MinZ;
        return dx * dy + dy * dz + dz * dx;
    }
}

/// <summary>
/// A two-wide BVH node, 64 bytes, holding both children's boxes so one fetch tests both.
/// For each child: Count &gt; 0 is a leaf holding primitives [Index, Index + Count) of the BVH's primitive order;
/// Count == 0 is an interior child whose node is Index (relative to this BVH's first node); Count &lt; 0 is an empty
/// child, whose box is inverted (min = +inf, max = -inf). Node 0 is the root; a one-leaf BVH has the leaf as the root's
/// left child and an empty right child. A child's index is always greater than its parent's.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BvhNode
{
    public float LMinX, LMinY, LMinZ;
    public int LIndex;
    public float LMaxX, LMaxY, LMaxZ;
    public int LCount;
    public float RMinX, RMinY, RMinZ;
    public int RIndex;
    public float RMaxX, RMaxY, RMaxZ;
    public int RCount;
}

/// <summary>
/// A triangle prepared for Moller-Trumbore with precomputed edges (E1 = v1 - v0, E2 = v2 - v0), 48 bytes.
/// Original is the triangle's index in the input mesh, for fetching its UVs, material and colours.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuTriangle
{
    public float V0X, V0Y, V0Z;
    public int Original;
    public float E1X, E1Y, E1Z;
    public int Pad0;
    public float E2X, E2Y, E2Z;
    public int Pad1;
}

/// <summary>A built triangle BVH. <see cref="Nodes"/> may be longer than <see cref="NodeCount"/>; only the first NodeCount are valid.</summary>
public sealed class TriangleBvh
{
    public BvhNode[] Nodes = new BvhNode[0];
    public int NodeCount;
    /// <summary>The triangles in leaf order (a leaf's [Index, Index + Count) indexes this array).</summary>
    public GpuTriangle[] Triangles = new GpuTriangle[0];
    public Aabb Bounds = Aabb.Empty;
    /// <summary>The mesh's index buffer as given to the builder, kept so <see cref="BvhBuilder.Refit"/> can re-read vertices.</summary>
    public int[] SourceIndices = new int[0];
}

/// <summary>A built BVH over boxes. <see cref="Order"/> maps a leaf slot to the input box index.</summary>
public sealed class BoxBvh
{
    public BvhNode[] Nodes = new BvhNode[0];
    public int NodeCount;
    /// <summary>Input box index per leaf slot; only the first <see cref="PrimitiveCount"/> entries are valid.</summary>
    public int[] Order = new int[0];
    public int PrimitiveCount;
    public Aabb Bounds = Aabb.Empty;
}
