using System.Runtime.InteropServices;
using UnityEngine;

namespace KSPVE.RayTracing;

/// <summary>
/// One placed mesh for the tracer (64 bytes; RtTrace.cginc's RtInstance): the rows of its camera-relative world-to-object
/// matrix, and where its tree, triangles, triangle data and materials start in the shared buffers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RtInstance
{
    public Vector4 WorldToObject0, WorldToObject1, WorldToObject2;
    public uint NodeOffset, TriangleOffset, DataOffset, MaterialOffset;
}

/// <summary>
/// What a hit needs to know about one of a mesh's triangles (40 bytes; RtTrace.cginc's RtTriangleData), in the mesh's
/// own triangle order: its corners' UVs and colours (RGBA8, white without vertex colours) and its submesh.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RtTriangleData
{
    public Vector2 Uv0, Uv1, Uv2;
    public uint Submesh;
    public uint Color0, Color1, Color2;
}

/// <summary>
/// A surface as the tracer shades it (32 bytes; RtTrace.cginc's RtMaterial): its albedo tint (rgb) and atlas slice
/// (a; negative for none), its emission (rgb) and flags (a: 1 = multiply by the vertex colours).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RtMaterial
{
    public Vector4 Albedo;
    public Vector4 Emission;
}
