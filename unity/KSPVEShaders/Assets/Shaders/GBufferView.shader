Shader "Hidden/KSPVE/Debug/GBufferView"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertFullscreen
            #pragma fragment frag
            #include "Include/KSPVECommon.cginc"

            sampler2D _CameraGBufferTexture0; // rgb albedo, a occlusion
            sampler2D _CameraGBufferTexture1; // rgb specular, a smoothness
            sampler2D _CameraGBufferTexture2; // rgb world normal (encoded 0..1)
            sampler2D_float _CameraDepthTexture;
            int _KSPVE_DebugMode;

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float4 g0 = tex2D(_CameraGBufferTexture0, i.uv);
                float4 g1 = tex2D(_CameraGBufferTexture1, i.uv);
                float4 g2 = tex2D(_CameraGBufferTexture2, i.uv);
                float3 c = 0;
                if (_KSPVE_DebugMode == 0)
                {
                    float d = KSPVELinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
                    // log scale: 1 cm (1e-2) .. 1e6 m (8 decades) mapped to 0..1
                    float t = saturate((log10(max(d, 1e-4)) + 2.0) / 8.0);
                    c = t.xxx;
                }
                else if (_KSPVE_DebugMode == 1) c = g2.rgb;
                else if (_KSPVE_DebugMode == 2) c = g0.rgb;
                else if (_KSPVE_DebugMode == 3) c = g1.rgb;
                else if (_KSPVE_DebugMode == 4) c = g1.aaa;
                else if (_KSPVE_DebugMode == 5) c = g0.aaa;
                return float4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
