#ifndef KSPVE_COMMON_INCLUDED
#define KSPVE_COMMON_INCLUDED
#include "UnityCG.cginc"

struct KSPVEFullscreenV2F
{
    float4 pos : SV_POSITION;
    float2 uv  : TEXCOORD0;
};

// Fullscreen triangle from SV_VertexID (CommandBuffer.DrawProcedural, MeshTopology.Triangles, 3 vertices).
// Matches Unity's built-in fullscreen blit: only the UV is flipped where UVs start at the top
// (D3D). Flipping the position as well would cancel the flip and mirror the image vertically.
KSPVEFullscreenV2F KSPVEVertFullscreen(uint id : SV_VertexID)
{
    KSPVEFullscreenV2F o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
    o.uv = uv;
#if UNITY_UV_STARTS_AT_TOP
    o.uv.y = 1.0 - o.uv.y;
#endif
    return o;
}

// Standard vertex for CommandBuffer.Blit (appdata_img).
KSPVEFullscreenV2F KSPVEVertBlit(appdata_img v)
{
    KSPVEFullscreenV2F o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    return o;
}

// Raw depth buffer value (reversed-Z on D3D11) to linear eye depth in world units.
// LinearEyeDepth/Linear01Depth use _ZBufferParams, which Unity sets correctly for reversed-Z.
float KSPVELinearEyeDepth(float rawDepth) { return LinearEyeDepth(rawDepth); }
float KSPVELinear01Depth(float rawDepth)  { return Linear01Depth(rawDepth); }

#endif
