// Snow, glaciers and sea ice blended into a scaled planet's colour texture (Graphics.Blit from the original _MainTex into
// a render texture the scaled material uses instead), so the planet keeps being lit by KSP's own shaders. The maps are
// latitude by longitude (row 0 the south, column 0 at longitude -180), read at the planet texture's own layout (see below).
Shader "Hidden/KSPVE/Climate/SnowBlend"
{
    Properties
    {
        _MainTex ("Planet colour", 2D) = "grey" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _KSPVE_SnowMaps ("Snow maps", 2D) = "black" {}
        _KSPVE_SnowBlend ("Blend", Vector) = (0.86, 0, 512, 0.9)
        _KSPVE_SnowColors ("Colours", Vector) = (1, 0.95, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BumpMap;                // the planet's normal map (tangent space), when it has one
            sampler2D _KSPVE_SnowMaps;         // R snow cover, G perennial ice, B sea ice
            float4 _KSPVE_SnowBlend;           // x snow albedo, y has a normal map, z noise scale (cells round the planet), w detail
            float4 _KSPVE_SnowColors;          // x ice brightness, y sea ice brightness, z..w unused

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Value noise that repeats every `period` cells along x, so the map's longitude seam does not show.
            float ValueNoise(float2 p, float period)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float x0 = fmod(i.x, period), x1 = fmod(i.x + 1.0, period);
                float a = Hash21(float2(x0, i.y)), b = Hash21(float2(x1, i.y));
                float c = Hash21(float2(x0, i.y + 1.0)), d = Hash21(float2(x1, i.y + 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 uv, float cells)
            {
                float2 p = float2(uv.x * cells, uv.y * cells * 0.5);
                float period = cells;
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * ValueNoise(p, period);
                    p *= 2.0;
                    period *= 2.0;
                    a *= 0.5;
                }
                return v / 0.9375;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 baseColor = tex2D(_MainTex, i.uv);
                // The planet texture runs west from longitude 0 a quarter of the way across (u = 0.25 - lon / 360); the snow
                // maps are on the latitude-longitude grid (column 0 at -180, east): the same place is at u' = 0.75 - u.
                float3 maps = tex2D(_KSPVE_SnowMaps, float2(frac(0.75 - i.uv.x), i.uv.y)).rgb;
                float snow = maps.x, ice = maps.y, sea = maps.z;
                if (snow + ice + sea < 0.003) return baseColor;

                // Steep ground sheds its snow: from the normal map (z up in tangent space; flat is 1).
                float retention = 1.0;
                if (_KSPVE_SnowBlend.y > 0.5)
                {
                    float3 n = UnpackNormal(tex2D(_BumpMap, i.uv));
                    retention = smoothstep(0.55, 0.9, n.z);
                }
                float noise = Fbm(i.uv, _KSPVE_SnowBlend.z);
                float patch = (noise - 0.5) * _KSPVE_SnowBlend.w;
                float cover = saturate((snow * retention - 0.5) / 0.35 + 0.5 + patch);
                float glacier = saturate(ice * (0.8 + 0.4 * noise));
                float seaCover = saturate((sea - 0.1) / 0.8 + patch * 0.5);

                float3 snowColor = _KSPVE_SnowBlend.x * float3(0.985, 0.99, 1.0);
                float3 iceColor = _KSPVE_SnowColors.x * float3(0.62, 0.74, 0.82);
                float3 seaColor = _KSPVE_SnowColors.y * float3(0.86, 0.9, 0.94);

                float3 color = baseColor.rgb;
                color = lerp(color, snowColor, cover);
                color = lerp(color, iceColor, glacier);
                color = lerp(color, seaColor, seaCover);
                float alpha = baseColor.a * (1.0 - 0.9 * seaCover);
                return float4(color, alpha);
            }
            ENDCG
        }
    }
}
