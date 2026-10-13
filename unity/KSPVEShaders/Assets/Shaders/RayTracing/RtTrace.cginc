// KSPVE world-space ray tracing on DirectX 11: two-level BVH traversal in plain HLSL over StructuredBuffers. The trees
// are built on the CPU by KSPVE.Pure.RayTracing.BvhBuilder; this traversal mirrors BvhTraversal.Traverse exactly (the
// same slab test with its padding, the same nearer-child-first order, the same Moller-Trumbore with edge slack).
// Everything is in camera-relative world space (metres), so floats stay precise near the camera.
#ifndef KSPVE_RT_TRACE_INCLUDED
#define KSPVE_RT_TRACE_INCLUDED

#define RT_STACK 64
#define RT_SLAB_PAD 1.0000012
#define RT_EDGE_SLACK 1e-6
#define RT_MIN_DIR 1e-20

struct RtNode
{
    float3 lmin; int lidx;
    float3 lmax; int lcnt;
    float3 rmin; int ridx;
    float3 rmax; int rcnt;
};

struct RtTri
{
    float3 v0; int original;
    float3 e1; int pad0;
    float3 e2; int pad1;
};

struct RtTriData
{
    float2 uv0; float2 uv1; float2 uv2;
    uint submesh;
    uint c0; uint c1; uint c2;
};

struct RtInstance
{
    float4 w0; float4 w1; float4 w2;   // camera-relative world -> object, rows
    uint nodeOffset; uint triOffset; uint dataOffset; uint materialOffset;
};

struct RtMaterial
{
    float4 albedo;     // rgb tint, a: atlas slice (< 0: none)
    float4 emission;   // rgb emission, a: flags (1: vertex colours)
};

struct RtHit
{
    float t;
    uint instance;
    uint tri;          // slot in the instance's tree's triangles
    float u, v;
};

StructuredBuffer<RtNode> _RtNodes;
StructuredBuffer<RtTri> _RtTriangles;
StructuredBuffer<RtTriData> _RtTriangleData;
StructuredBuffer<RtInstance> _RtInstances;
StructuredBuffer<RtNode> _RtTopNodes;
StructuredBuffer<int> _RtTopOrder;
StructuredBuffer<RtMaterial> _RtMaterials;

float4 _RtInfo;   // x: instances, y: reach (m), z: frame, w: linear colour space (1) or gamma (0)

float3 RtSafeInverse(float3 d)
{
    float3 s = float3(d.x >= 0.0 ? max(d.x, RT_MIN_DIR) : min(d.x, -RT_MIN_DIR),
                      d.y >= 0.0 ? max(d.y, RT_MIN_DIR) : min(d.y, -RT_MIN_DIR),
                      d.z >= 0.0 ? max(d.z, RT_MIN_DIR) : min(d.z, -RT_MIN_DIR));
    return 1.0 / s;
}

bool RtSlab(float3 bmin, float3 bmax, float3 o, float3 inv, float tMax, out float tNear)
{
    float3 t0 = (bmin - o) * inv, t1 = (bmax - o) * inv;
    float3 tn = min(t0, t1), tf = max(t0, t1);
    tNear = max(max(tn.x, tn.y), max(tn.z, 0.0));
    float tFar = min(min(tf.x, tf.y), tf.z);
    return tNear <= min(tFar * RT_SLAB_PAD, tMax);
}

bool RtTriangle(RtTri tri, float3 o, float3 d, float tMax, out float t, out float u, out float v)
{
    t = 0.0; u = 0.0; v = 0.0;
    float3 p = cross(d, tri.e2);
    float det = dot(tri.e1, p);
    if (abs(det) < 1e-30) return false;
    float invDet = 1.0 / det;
    float3 tv = o - tri.v0;
    u = dot(tv, p) * invDet;
    if (!(u >= -RT_EDGE_SLACK && u <= 1.0 + RT_EDGE_SLACK)) return false;
    float3 q = cross(tv, tri.e1);
    v = dot(d, q) * invDet;
    if (!(v >= -RT_EDGE_SLACK && u + v <= 1.0 + RT_EDGE_SLACK)) return false;
    t = dot(tri.e2, q) * invDet;
    return t > 0.0 && t < tMax;
}

// One instance's tree, in its object space (d not normalised, so t is the world ray's t).
bool RtTraceInstance(RtInstance inst, float3 o, float3 d, bool anyHit, inout float tMax, inout RtHit hit)
{
    float3 inv = RtSafeInverse(d);
    int stack[RT_STACK];
    int sp = 0, node = 0;
    bool found = false;
    [loop] for (int guard = 0; guard < 4096; guard++)
    {
        RtNode n = _RtNodes[inst.nodeOffset + node];
        float tl = 0.0, tr = 0.0;
        bool hitL = n.lcnt >= 0 && RtSlab(n.lmin, n.lmax, o, inv, tMax, tl);
        bool hitR = n.rcnt >= 0 && RtSlab(n.rmin, n.rmax, o, inv, tMax, tr);
        bool leftFirst = !(hitL && hitR) || tl <= tr;
        int next = -1;
        [unroll] for (int side = 0; side < 2; side++)
        {
            bool right = (side == 0) != leftFirst;
            if (!(right ? hitR : hitL)) continue;
            int index = right ? n.ridx : n.lidx;
            int count = right ? n.rcnt : n.lcnt;
            if (count > 0)
            {
                [loop] for (int i = index; i < index + count; i++)
                {
                    float t, u, v;
                    if (RtTriangle(_RtTriangles[inst.triOffset + i], o, d, tMax, t, u, v))
                    {
                        tMax = t;
                        hit.t = t;
                        hit.tri = (uint)i;
                        hit.u = u;
                        hit.v = v;
                        found = true;
                        if (anyHit) return true;
                    }
                }
            }
            else if (next < 0) next = index;
            else if (sp < RT_STACK) stack[sp++] = index;
        }
        if (next >= 0) node = next;
        else if (sp > 0) node = stack[--sp];
        else break;
    }
    return found;
}

// The nearest hit (or, with anyHit, any hit) along o + t d for t in (0, tMax), through every instance.
bool RtTrace(float3 o, float3 d, float tMax, bool anyHit, out RtHit hit)
{
    hit = (RtHit)0;
    hit.t = tMax;
    if (_RtInfo.x < 0.5) return false;
    float3 inv = RtSafeInverse(d);
    int stack[RT_STACK];
    int sp = 0, node = 0;
    bool found = false;
    [loop] for (int guard = 0; guard < 4096; guard++)
    {
        RtNode n = _RtTopNodes[node];
        float tl = 0.0, tr = 0.0;
        bool hitL = n.lcnt >= 0 && RtSlab(n.lmin, n.lmax, o, inv, tMax, tl);
        bool hitR = n.rcnt >= 0 && RtSlab(n.rmin, n.rmax, o, inv, tMax, tr);
        bool leftFirst = !(hitL && hitR) || tl <= tr;
        int next = -1;
        [unroll] for (int side = 0; side < 2; side++)
        {
            bool right = (side == 0) != leftFirst;
            if (!(right ? hitR : hitL)) continue;
            int index = right ? n.ridx : n.lidx;
            int count = right ? n.rcnt : n.lcnt;
            if (count > 0)
            {
                [loop] for (int i = index; i < index + count; i++)
                {
                    uint which = (uint)_RtTopOrder[i];
                    RtInstance inst = _RtInstances[which];
                    float4 o4 = float4(o, 1.0);
                    float3 oo = float3(dot(inst.w0, o4), dot(inst.w1, o4), dot(inst.w2, o4));
                    float3 dd = float3(dot(inst.w0.xyz, d), dot(inst.w1.xyz, d), dot(inst.w2.xyz, d));
                    if (RtTraceInstance(inst, oo, dd, anyHit, tMax, hit))
                    {
                        hit.instance = which;
                        found = true;
                        if (anyHit) return true;
                    }
                }
            }
            else if (next < 0) next = index;
            else if (sp < RT_STACK) stack[sp++] = index;
        }
        if (next >= 0) node = next;
        else if (sp > 0) node = stack[--sp];
        else break;
    }
    return found;
}

#endif
