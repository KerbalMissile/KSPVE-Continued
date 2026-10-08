Shader "Hidden/KSPVE/Post/MotionBlur"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // Blur along per-pixel motion vectors (Camera 00 renders them for Scatterer's TAA).
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"

            sampler2D _MainTex;                          // linear working buffer
            float4 _MainTex_TexelSize;
            sampler2D_half _CameraMotionVectorsTexture;  // UV-space motion since last frame
            float4 _KSPVE_MotionBlur;                    // shutter scale, max length (uv), -, frame

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float2 uv = i.uv;
                float2 velocity = tex2D(_CameraMotionVectorsTexture, uv).xy * _KSPVE_MotionBlur.x;
                float len = length(velocity);
                if (len > _KSPVE_MotionBlur.y) velocity *= _KSPVE_MotionBlur.y / len;
                if (len * _MainTex_TexelSize.z < 0.5) return tex2D(_MainTex, uv);

                const int samples = 12;
                float jitter = KSPVEInterleavedGradientNoise(i.pos.xy, _KSPVE_MotionBlur.w) - 0.5;
                float3 sum = 0;
                [unroll] for (int k = 0; k < samples; k++)
                {
                    float t = (k + 0.5 + jitter) / samples - 0.5;
                    sum += tex2Dlod(_MainTex, float4(uv + velocity * t, 0, 0)).rgb;
                }
                return float4(sum / samples, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
