Shader "Hidden/KSPVE/Post/DepthOfField"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }

    CGINCLUDE
    #pragma target 5.0
    #pragma only_renderers d3d11
    #include "../Include/KSPVECommon.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D_float _CameraDepthTexture;
    sampler2D _KSPVE_DofTex;
    float4 _KSPVE_Dof; // focus distance m (0 = auto-focus on screen centre), strength 0..1, max radius (half-res px), -

    float SceneDepth(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0))); }

    float FocusDistance()
    {
        if (_KSPVE_Dof.x > 0.0) return _KSPVE_Dof.x;
        float d = SceneDepth(float2(0.5, 0.5));
        d = min(d, SceneDepth(float2(0.48, 0.5)));
        d = min(d, SceneDepth(float2(0.52, 0.5)));
        d = min(d, SceneDepth(float2(0.5, 0.47)));
        d = min(d, SceneDepth(float2(0.5, 0.53)));
        return max(d, 0.5);
    }

    // Thin-lens-shaped circle of confusion in [0,1]: 0 in focus, 1 at infinity or half the focus distance.
    float CoC(float2 uv)
    {
        float f = FocusDistance();
        float d = SceneDepth(uv);
        return saturate(abs(1.0 / f - 1.0 / d) * f) * _KSPVE_Dof.y;
    }

    // Pass 0: half-resolution colour with CoC in alpha.
    float4 FragPrefilter(KSPVEFullscreenV2F i) : SV_Target
    {
        return float4(tex2D(_MainTex, i.uv).rgb, CoC(i.uv));
    }

    // Pass 1: bokeh gather over a golden-angle disc (scatter-as-gather: a sample contributes if its own CoC reaches here).
    float4 FragBokeh(KSPVEFullscreenV2F i) : SV_Target
    {
        float4 center = tex2D(_MainTex, i.uv);
        float maxRadius = _KSPVE_Dof.z;
        const int samples = 32;
        float3 sum = 0;
        float weight = 0;
        [unroll] for (int k = 0; k < samples; k++)
        {
            float r = sqrt((k + 0.5) / samples) * maxRadius;
            float a = k * 2.39996323;
            float2 offset = float2(cos(a), sin(a)) * r * _MainTex_TexelSize.xy;
            float4 s = tex2Dlod(_MainTex, float4(i.uv + offset, 0, 0));
            float w = saturate(s.a * maxRadius - r + 1.0);
            sum += s.rgb * w;
            weight += w;
        }
        float3 blurred = weight > 1e-4 ? sum / weight : center.rgb;
        return float4(blurred, center.a);
    }

    // Pass 2: composite the blurred half-resolution image over the sharp full-resolution one.
    float4 FragComposite(KSPVEFullscreenV2F i) : SV_Target
    {
        float3 sharp = tex2D(_MainTex, i.uv).rgb;
        float4 blurred = tex2D(_KSPVE_DofTex, i.uv);
        float blend = smoothstep(0.02, 0.2, max(CoC(i.uv), blurred.a));
        return float4(lerp(sharp, blurred.rgb, blend), 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragPrefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragBokeh
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragComposite
               ENDCG }
    }
    Fallback Off
}
