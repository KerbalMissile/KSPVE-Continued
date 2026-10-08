Shader "Hidden/KSPVE/Post/Final"
{
    Properties { _MainTex ("Graded", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // Contrast-adaptive sharpening (AMD FidelityFX CAS, as ported to ReShade), before/after split, dithering.
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"

            sampler2D _MainTex;            // display-encoded graded image
            float4 _MainTex_TexelSize;
            sampler2D _KSPVE_OriginalTex;  // KSP's untouched image, for the split compare
            float _KSPVE_Sharpen;          // 0..1
            float _KSPVE_Split;            // < 0 disabled, else split position in [0,1]
            float _KSPVE_Broadcast;        // 0..1 mission-control TV look
            float _KSPVE_Frame;

            float3 Tap(float2 uv, float2 o) { return tex2D(_MainTex, uv + o * _MainTex_TexelSize.xy).rgb; }

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float2 uv = i.uv;
                float3 e = Tap(uv, float2(0, 0));
                float3 color = e;

                if (_KSPVE_Sharpen > 0.0)
                {
                    float3 a = Tap(uv, float2(-1, -1));
                    float3 b = Tap(uv, float2( 0, -1));
                    float3 c = Tap(uv, float2( 1, -1));
                    float3 d = Tap(uv, float2(-1,  0));
                    float3 f = Tap(uv, float2( 1,  0));
                    float3 g = Tap(uv, float2(-1,  1));
                    float3 h = Tap(uv, float2( 0,  1));
                    float3 k = Tap(uv, float2( 1,  1));

                    float3 mnRGB = min(min(min(d, e), min(f, b)), h);
                    float3 mnRGB2 = min(mnRGB, min(min(a, c), min(g, k)));
                    mnRGB += mnRGB2;
                    float3 mxRGB = max(max(max(d, e), max(f, b)), h);
                    float3 mxRGB2 = max(mxRGB, max(max(a, c), max(g, k)));
                    mxRGB += mxRGB2;

                    float3 rcpMRGB = rcp(max(mxRGB, 1e-5));
                    float3 ampRGB = saturate(min(mnRGB, 2.0 - mxRGB) * rcpMRGB);
                    ampRGB = rsqrt(max(ampRGB, 1e-5));
                    const float peak = 8.0; // CAS contrast 0
                    float3 wRGB = -rcp(ampRGB * peak);
                    float3 rcpWeightRGB = rcp(4.0 * wRGB + 1.0);
                    float3 window = (b + d) + (f + h);
                    float3 sharpened = saturate((window * wRGB + e) * rcpWeightRGB);
                    color = lerp(e, sharpened, _KSPVE_Sharpen);
                }

                if (_KSPVE_Broadcast > 0.0)
                {
                    float2 px = _MainTex_TexelSize.xy;
                    float3 tv;
                    tv.r = tex2D(_MainTex, uv + float2(px.x * 1.5, 0)).r;
                    tv.g = color.g;
                    tv.b = tex2D(_MainTex, uv - float2(px.x * 1.5, 0)).b;
                    tv = lerp(KSPVELuminance(tv).xxx, tv, 0.85);
                    tv *= 0.93 + 0.07 * sin(i.pos.y * 3.14159265);
                    tv += (KSPVEInterleavedGradientNoise(i.pos.xy, _KSPVE_Frame * 7.0) - 0.5) * 0.04;
                    color = lerp(color, saturate(tv), _KSPVE_Broadcast);
                }

                if (_KSPVE_Split >= 0.0)
                {
                    if (uv.x < _KSPVE_Split) color = saturate(tex2D(_KSPVE_OriginalTex, uv).rgb);
                    if (abs(uv.x - _KSPVE_Split) < _MainTex_TexelSize.x * 1.5) color = float3(1, 1, 1);
                }

                // Triangular dither to hide 8-bit banding in skies and gradients.
                float n1 = KSPVEInterleavedGradientNoise(i.pos.xy, _KSPVE_Frame);
                float n2 = KSPVEInterleavedGradientNoise(i.pos.xy + 17.0, _KSPVE_Frame);
                color += (n1 + n2 - 1.0) / 255.0;
                return float4(color, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
