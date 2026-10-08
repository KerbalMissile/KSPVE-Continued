Shader "Hidden/KSPVE/AmbientOcclusion"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }

    CGINCLUDE
    #pragma target 5.0
    #pragma only_renderers d3d11
    #include "../Include/KSPVECommon.cginc"
    #include "../Include/KSPVEColor.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D_float _CameraDepthTexture;
    sampler2D _CameraGBufferTexture2;     // world normal * 0.5 + 0.5
    float4x4 _KSPVE_WorldToView;          // Camera 00 worldToCameraMatrix (Unity view space, -Z forward)
    float4 _KSPVE_TanHalfFov;             // tan(fovX / 2), tan(fovY / 2)
    float4 _KSPVE_AO;                     // radius m, intensity, max distance m, frame
    float4 _KSPVE_BlurDirection;          // uv step for the bilateral blur

    float Depth(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0))); }

    // View-space position with +Z forward (distance along the view direction).
    float3 ViewPosition(float2 uv, float depth) { return float3((uv * 2.0 - 1.0) * _KSPVE_TanHalfFov.xy * depth, depth); }

    float3 ViewNormal(float2 uv)
    {
        float3 n = tex2Dlod(_CameraGBufferTexture2, float4(uv, 0, 0)).xyz * 2.0 - 1.0;
        float3 v = mul((float3x3)_KSPVE_WorldToView, n);
        return normalize(float3(v.x, v.y, -v.z));
    }

    // Pass 0: occlusion from a spiral of samples within a world-space radius (half resolution).
    float4 FragOcclusion(KSPVEFullscreenV2F i) : SV_Target
    {
        float depth = Depth(i.uv);
        if (depth > _KSPVE_AO.z) return 1;
        float3 p = ViewPosition(i.uv, depth);
        float3 n = ViewNormal(i.uv);
        float radius = _KSPVE_AO.x;
        float2 radiusUv = radius / (depth * _KSPVE_TanHalfFov.xy * 2.0);

        const int samples = 12;
        float angle = KSPVEInterleavedGradientNoise(i.pos.xy, _KSPVE_AO.w) * 6.2831853;
        float occlusion = 0;
        [unroll] for (int k = 0; k < samples; k++)
        {
            float t = (k + 0.5) / samples;
            float a = angle + k * 2.39996323;
            float2 uv = i.uv + float2(cos(a), sin(a)) * sqrt(t) * radiusUv;
            float3 s = ViewPosition(uv, Depth(uv));
            float3 v = s - p;
            float vv = dot(v, v);
            if (vv < 1e-8) continue;
            float cosine = dot(v * rsqrt(vv), n);
            float falloff = saturate(1.0 - vv / (radius * radius));
            occlusion += max(0.0, cosine - 0.1) * falloff;
        }
        float ao = saturate(1.0 - 1.5 * occlusion / samples);
        return float4(ao, ao, ao, 1);
    }

    // Passes 1 and 2: depth-aware blur along _KSPVE_BlurDirection.
    float4 FragBlur(KSPVEFullscreenV2F i) : SV_Target
    {
        float center = tex2D(_MainTex, i.uv).r;
        float depth = Depth(i.uv);
        float sum = center;
        float weight = 1.0;
        [unroll] for (int k = 1; k <= 3; k++)
        {
            [unroll] for (int s = -1; s <= 1; s += 2)
            {
                float2 uv = i.uv + _KSPVE_BlurDirection.xy * k * s;
                float d = Depth(uv);
                float w = exp(-k * k / 8.0) * saturate(1.0 - abs(d - depth) / (0.05 * depth + 0.1));
                sum += tex2D(_MainTex, uv).r * w;
                weight += w;
            }
        }
        float ao = sum / weight;
        return float4(ao, ao, ao, 1);
    }

    // Pass 3: multiply the lit opaque scene (before transparents: clouds, plumes and particles stay untouched).
    float4 FragComposite(KSPVEFullscreenV2F i) : SV_Target
    {
        float ao = tex2D(_MainTex, i.uv).r;
        return float4(lerp(1.0, ao, _KSPVE_AO.y).xxx, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragOcclusion
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragBlur
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragBlur
               ENDCG }
        Pass { Blend DstColor Zero
               CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragComposite
               ENDCG }
    }
    Fallback Off
}
