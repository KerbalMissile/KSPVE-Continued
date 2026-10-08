Shader "Hidden/KSPVE/Post/Bloom"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }

    CGINCLUDE
    #pragma target 5.0
    #pragma only_renderers d3d11
    #include "../Include/KSPVECommon.cginc"
    #include "../Include/KSPVEColor.cginc"

    sampler2D _MainTex;              // linear
    float4 _MainTex_TexelSize;
    sampler2D _KSPVE_BloomBase;      // upsample: the current (higher-resolution) level
    float4 _KSPVE_BloomThreshold;    // threshold, threshold - knee, 2 * knee, 0.25 / knee
    float _KSPVE_BloomScatter;

    // 13-tap downsample (Jimenez 2014, "Next Generation Post Processing in Call of Duty").
    float3 Downsample13(float2 uv)
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 A = tex2D(_MainTex, uv + t * float2(-1, -1)).rgb;
        float3 B = tex2D(_MainTex, uv + t * float2( 0, -1)).rgb;
        float3 C = tex2D(_MainTex, uv + t * float2( 1, -1)).rgb;
        float3 D = tex2D(_MainTex, uv + t * float2(-0.5, -0.5)).rgb;
        float3 E = tex2D(_MainTex, uv + t * float2( 0.5, -0.5)).rgb;
        float3 F = tex2D(_MainTex, uv + t * float2(-1, 0)).rgb;
        float3 G = tex2D(_MainTex, uv).rgb;
        float3 H = tex2D(_MainTex, uv + t * float2( 1, 0)).rgb;
        float3 I = tex2D(_MainTex, uv + t * float2(-0.5, 0.5)).rgb;
        float3 J = tex2D(_MainTex, uv + t * float2( 0.5, 0.5)).rgb;
        float3 K = tex2D(_MainTex, uv + t * float2(-1, 1)).rgb;
        float3 L = tex2D(_MainTex, uv + t * float2( 0, 1)).rgb;
        float3 M = tex2D(_MainTex, uv + t * float2( 1, 1)).rgb;
        float3 o = (D + E + I + J) * 0.125;
        o += (A + B + G + F) * 0.03125;
        o += (B + C + H + G) * 0.03125;
        o += (F + G + L + K) * 0.03125;
        o += (G + H + M + L) * 0.03125;
        return o;
    }

    // 9-tap tent upsample.
    float3 UpsampleTent(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        float3 s = tex2D(_MainTex, uv - d.xy).rgb;
        s += tex2D(_MainTex, uv - d.wy).rgb * 2.0;
        s += tex2D(_MainTex, uv - d.zy).rgb;
        s += tex2D(_MainTex, uv + d.zw).rgb * 2.0;
        s += tex2D(_MainTex, uv).rgb * 4.0;
        s += tex2D(_MainTex, uv + d.xw).rgb * 2.0;
        s += tex2D(_MainTex, uv + d.zy).rgb;
        s += tex2D(_MainTex, uv + d.wy).rgb * 2.0;
        s += tex2D(_MainTex, uv + d.xy).rgb;
        return s * (1.0 / 16.0);
    }

    // Soft-knee threshold (Unity Post Processing v2 QuadraticThreshold).
    float3 Threshold(float3 color)
    {
        float br = max(color.r, max(color.g, color.b));
        float rq = clamp(br - _KSPVE_BloomThreshold.y, 0.0, _KSPVE_BloomThreshold.z);
        rq = _KSPVE_BloomThreshold.w * rq * rq;
        return color * max(rq, br - _KSPVE_BloomThreshold.x) / max(br, 1e-4);
    }

    float4 FragPrefilter(KSPVEFullscreenV2F i) : SV_Target
    {
        float3 c = min(Downsample13(i.uv), 64.0); // clamp fireflies from the sun and specular glints
        return float4(Threshold(c), 1);
    }

    float4 FragDownsample(KSPVEFullscreenV2F i) : SV_Target
    {
        return float4(Downsample13(i.uv), 1);
    }

    float4 FragUpsample(KSPVEFullscreenV2F i) : SV_Target
    {
        float3 base = tex2D(_KSPVE_BloomBase, i.uv).rgb;
        return float4(lerp(base, UpsampleTent(i.uv), _KSPVE_BloomScatter), 1);
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
               #pragma fragment FragDownsample
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex KSPVEVertBlit
               #pragma fragment FragUpsample
               ENDCG }
    }
    Fallback Off
}
