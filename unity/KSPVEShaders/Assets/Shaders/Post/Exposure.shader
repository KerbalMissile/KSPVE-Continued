Shader "Hidden/KSPVE/Post/Exposure"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: centre-weighted log-luminance into a 256x256 RGHalf target with mips
        // (R = weight * log2(L), G = weight); mip 8 then holds the weighted average.
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"

            sampler2D _MainTex; // KSP's gamma-encoded scene

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float3 c = KSPVESrgbToLinear(tex2D(_MainTex, i.uv).rgb);
                float lum = max(KSPVELuminance(c), 1e-5);
                float2 d = i.uv - 0.5;
                float w = exp(-dot(d, d) * 8.0);
                return float4(log2(lum) * w, w, 0, 1);
            }
            ENDCG
        }

        // Pass 1: temporal adaptation into a 1x1 RFloat holding log2 of the exposure multiplier.
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"

            sampler2D _MainTex;              // the log-luminance target, with mips
            sampler2D _KSPVE_PrevExposure;   // 1x1
            float4 _KSPVE_AEParams;          // key, min log2 exposure, max log2 exposure, compensation
            float4 _KSPVE_AESpeed;           // darkening speed, brightening speed, dt, reset

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float2 m = tex2Dlod(_MainTex, float4(0.5, 0.5, 0, 8)).rg;
                float averageLog2 = m.x / max(m.y, 1e-5);
                float target = log2(_KSPVE_AEParams.x) - averageLog2 + _KSPVE_AEParams.w;
                target = clamp(target, _KSPVE_AEParams.y, _KSPVE_AEParams.z);
                if (_KSPVE_AESpeed.w > 0.5) return float4(target, 0, 0, 1);

                float previous = tex2D(_KSPVE_PrevExposure, float2(0.5, 0.5)).r;
                // Exposure rises when the scene gets darker (slow, like dark adaptation).
                float speed = target > previous ? _KSPVE_AESpeed.x : _KSPVE_AESpeed.y;
                float next = previous + (target - previous) * (1.0 - exp(-_KSPVE_AESpeed.z * speed));
                return float4(next, 0, 0, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
