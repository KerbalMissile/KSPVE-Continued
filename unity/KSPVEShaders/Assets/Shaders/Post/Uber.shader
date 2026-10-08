Shader "Hidden/KSPVE/Post/Uber"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: linearize. KSP's gamma-encoded HDR scene -> linear, exposed working buffer.
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"
            #include "../Include/KSPVEPost.cginc"

            sampler2D _MainTex;

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float3 c = KSPVESrgbToLinear(tex2D(_MainTex, i.uv).rgb) * KSPVEExposure();
                return float4(min(c, 65000.0), 1);
            }
            ENDCG
        }

        // Pass 1: display. Bloom, lens dirt, white balance, grading, night vision, tonemap, vignette, grain.
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"
            #include "../Include/KSPVEColor.cginc"

            sampler2D _MainTex;               // linear, exposed working buffer
            float4 _MainTex_TexelSize;
            sampler2D _KSPVE_BloomTex;        // linear bloom
            sampler2D _KSPVE_LensDirtTex;
            float _KSPVE_BloomIntensity;
            float _KSPVE_LensDirt;
            float4 _KSPVE_WB0, _KSPVE_WB1, _KSPVE_WB2; // row-major white-balance matrix
            float _KSPVE_Contrast;
            float _KSPVE_Saturation;
            float _KSPVE_Vibrance;
            float _KSPVE_NightVision;
            float _KSPVE_Tonemapper;          // 0 None, 1 Rolloff, 2 Neutral, 3 AgX, 4 ACES
            float _KSPVE_Vignette;
            float _KSPVE_Grain;
            float _KSPVE_ChromaticAberration;
            float _KSPVE_Spikes;
            float _KSPVE_SensorSmear;
            float _KSPVE_Frame;

            // Diffraction spikes: streaks through the bloom (bright) image, strongest along the two main axes.
            float3 Spikes(float2 uv)
            {
                float aspect = _MainTex_TexelSize.z * _MainTex_TexelSize.y; // width / height
                const float2 dirs[4] = { float2(1, 0), float2(0, 1), float2(0.7071, 0.7071), float2(0.7071, -0.7071) };
                float3 sum = 0;
                [unroll] for (int d = 0; d < 4; d++)
                {
                    float axisWeight = d < 2 ? 1.0 : 0.35;
                    float2 dir = dirs[d] * float2(1.0 / aspect, 1.0);
                    [unroll] for (int k = 1; k <= 8; k++)
                    {
                        float t = k / 8.0;
                        float w = (1.0 - t) * (1.0 - t) * axisWeight;
                        float2 offset = dir * t * 0.12;
                        sum += (tex2Dlod(_KSPVE_BloomTex, float4(uv + offset, 0, 0)).rgb +
                                tex2Dlod(_KSPVE_BloomTex, float4(uv - offset, 0, 0)).rgb) * w;
                    }
                }
                return sum * 0.05;
            }

            // CCD-style vertical smear: very bright sources bleed along their sensor column.
            float3 SensorSmear(float2 uv)
            {
                float3 sum = 0;
                [unroll] for (int k = 0; k < 16; k++)
                    sum += tex2Dlod(_KSPVE_BloomTex, float4(uv.x, (k + 0.5) / 16.0, 0, 3)).rgb;
                return sum / 16.0;
            }

            // KSPVE Rolloff: identity below the knee, Neutral's highlight compression above it.
            float3 Rolloff(float3 color)
            {
                const float startCompression = 0.8 - 0.04;
                const float desaturation = 0.15;
                float peak = max(color.r, max(color.g, color.b));
                if (peak < startCompression) return color;
                const float d = 1.0 - startCompression;
                float newPeak = 1.0 - d * d / (peak + d - startCompression);
                color *= newPeak / peak;
                float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
                return lerp(color, newPeak.xxx, g);
            }

            float3 Tonemap(float3 c)
            {
                c = max(c, 0);
                if (_KSPVE_Tonemapper < 0.5) return saturate(c);
                if (_KSPVE_Tonemapper < 1.5) return Rolloff(c);
                return KSPVETonemap(c, _KSPVE_Tonemapper - 1.0); // 1 Neutral, 2 AgX, 3 ACES
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float2 uv = i.uv;
                float3 c;
                if (_KSPVE_ChromaticAberration > 0.0)
                {
                    float2 dir = uv - 0.5;
                    float2 offset = dir * dot(dir, dir) * _KSPVE_ChromaticAberration * 0.06;
                    c.r = tex2D(_MainTex, uv - offset).r;
                    c.g = tex2D(_MainTex, uv).g;
                    c.b = tex2D(_MainTex, uv + offset).b;
                }
                else
                {
                    c = tex2D(_MainTex, uv).rgb;
                }

                float3 bloom = tex2D(_KSPVE_BloomTex, uv).rgb;
                c += bloom * _KSPVE_BloomIntensity;
                c += bloom * tex2D(_KSPVE_LensDirtTex, uv).rgb * _KSPVE_LensDirt;
                if (_KSPVE_Spikes > 0.0) c += Spikes(uv) * _KSPVE_Spikes;
                if (_KSPVE_SensorSmear > 0.0) c += SensorSmear(uv) * _KSPVE_SensorSmear;

                c = float3(dot(_KSPVE_WB0.xyz, c), dot(_KSPVE_WB1.xyz, c), dot(_KSPVE_WB2.xyz, c));
                c = max(c, 0);

                // Contrast in log space around mid-grey, which leaves 0.18 unchanged.
                const float midGrey = 0.18;
                c = midGrey * pow(c / midGrey + 1e-6, _KSPVE_Contrast);

                float luma = KSPVELuminance(c);
                float maxC = max(c.r, max(c.g, c.b));
                float minC = min(c.r, min(c.g, c.b));
                float chroma = (maxC - minC) / max(maxC, 1e-4);
                float sat = _KSPVE_Saturation + _KSPVE_Vibrance * (1.0 - chroma);
                c = max(lerp(luma.xxx, c, sat), 0);

                // Purkinje shift: very dark areas lose colour and drift blue, as rod vision takes over.
                if (_KSPVE_NightVision > 0.0)
                {
                    float night = saturate(1.0 - KSPVELuminance(c) / 0.05) * _KSPVE_NightVision;
                    float3 scotopic = KSPVELuminance(c) * float3(0.70, 0.85, 1.25);
                    c = lerp(c, scotopic, night);
                }

                c = Tonemap(c);

                // Natural vignette, aspect-corrected; 1 at the centre, 1 - intensity at the corners.
                float2 d = (uv - 0.5) * float2(_MainTex_TexelSize.z * _MainTex_TexelSize.y, 1.0);
                float r2 = dot(d, d) / dot(float2(0.5 * _MainTex_TexelSize.z * _MainTex_TexelSize.y, 0.5), float2(0.5 * _MainTex_TexelSize.z * _MainTex_TexelSize.y, 0.5));
                c *= saturate(1.0 - _KSPVE_Vignette * r2);

                // Film grain, stronger in shadows and midtones, fading out towards black: with no light there is no
                // grain, and on a black sky the noise (its negative half clamped away) would show as grey dots.
                if (_KSPVE_Grain > 0.0)
                {
                    float n = Hash(i.pos.xy + frac(_KSPVE_Frame * 0.1234) * 173.0) - 0.5;
                    float lum = saturate(KSPVELuminance(c));
                    float response = (1.0 - 0.6 * sqrt(lum)) * saturate(sqrt(lum / 0.02));
                    c = max(c + n * _KSPVE_Grain * 0.12 * response, 0);
                }

                return float4(KSPVELinearToSrgb(saturate(c)), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
