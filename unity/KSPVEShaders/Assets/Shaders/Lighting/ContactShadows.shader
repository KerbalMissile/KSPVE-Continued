Shader "Hidden/KSPVE/ContactShadows"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // Runs at the sun light's AfterScreenspaceMask: short screen-space ray march towards the sun,
        // min-blended into the screen-space shadow mask so fine detail the shadow map misses is shadowed.
        Pass
        {
            BlendOp Min
            Blend One One
            ColorMask R
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"

            sampler2D_float _CameraDepthTexture;
            float4 _KSPVE_TanHalfFov;        // tan(fovX / 2), tan(fovY / 2)
            float4 _KSPVE_SunDirectionView;  // towards the sun, view space with +Z forward
            float4 _KSPVE_ContactShadow;     // ray length m, thickness m, max distance m, frame

            float Depth(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0))); }
            float3 ViewPosition(float2 uv, float depth) { return float3((uv * 2.0 - 1.0) * _KSPVE_TanHalfFov.xy * depth, depth); }
            float2 ProjectToUv(float3 p) { return p.xy / (p.z * _KSPVE_TanHalfFov.xy) * 0.5 + 0.5; }

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float depth = Depth(i.uv);
                if (depth > _KSPVE_ContactShadow.z) return 1;
                float3 p = ViewPosition(i.uv, depth);
                float3 stepVector = _KSPVE_SunDirectionView.xyz * (_KSPVE_ContactShadow.x / 12.0);
                float jitter = KSPVEInterleavedGradientNoise(i.pos.xy, _KSPVE_ContactShadow.w);
                float3 ray = p + stepVector * (jitter + 0.5) + normalize(p) * (-0.002 * depth);

                float shadow = 1.0;
                [unroll] for (int k = 0; k < 12; k++)
                {
                    if (ray.z <= 0.05) break;
                    float2 uv = ProjectToUv(ray);
                    if (any(uv < 0.0) || any(uv > 1.0)) break;
                    float delta = ray.z - Depth(uv);
                    if (delta > 0.02 * (1.0 + depth * 0.01) && delta < _KSPVE_ContactShadow.y)
                    {
                        shadow = (float)k / 12.0; // softer when the occluder is further along the ray
                        break;
                    }
                    ray += stepVector;
                }
                return float4(shadow, shadow, shadow, shadow);
            }
            ENDCG
        }
    }
    Fallback Off
}
