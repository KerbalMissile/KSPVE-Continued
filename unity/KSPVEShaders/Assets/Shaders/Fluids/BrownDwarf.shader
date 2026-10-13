Shader "Hidden/KSPVE/Fluids/BrownDwarf"
{
    // A body's fluid-dynamics atmosphere on Camera ScaledSpace (KSPVE.Fluids.FluidRenderer): a shell just over the scaled mesh
    // (drawn with the depth test, so planets in front hide it), shaded from the simulation's fields. Each pixel looks along the
    // line of sight through three cloud decks above a clear deep layer. The decks' condensate (the texture's rgb) gives their
    // optical depths; the temperature anomaly (alpha) shifts their temperatures; the colour of a deck at a temperature is
    // looked up in a table built from Planck's law, the CIE eye response and the sodium and potassium line wings
    // (KSPVE.Pure.Fluids.DwarfSpectrum). The decks are composited top down: each emits as a blackbody weighted by its
    // emissivity 1 - exp(-tau / mu), and what is left is transmitted to the deeper ones, so cooler upper clouds obscure the glow
    // of the hotter layers below and gaps let it through. Limb darkening is the Eddington-Barbier temperature at slant optical
    // depth one. Starlight from other stars adds reflected light from the decks' albedos.
    // tools/BlackHolePreview/BrownDwarfPreview.cs is a CPU copy of this file used to tune the look.
    CGINCLUDE
            #include "UnityCG.cginc"

            #define PI 3.14159265
            #define LUT_COLUMNS 48.0
            #define LUT_MU_BINS 5.0
            #define LUT_ROWS 20.0

            float4 _KSPVE_FldCenter;     // xyz: the body's centre relative to the camera (scaled units); w: its radius (scaled units)
            float4 _KSPVE_FldAxisX;      // the planet's longitude 0 direction now (world, unit)
            float4 _KSPVE_FldAxisY;      // north
            float4 _KSPVE_FldAxisZ;      // longitude 90 east
            float4 _KSPVE_FldCam;        // x: angular size of a pixel (rad), y: radiance of the Sun's disc times the display boost, z: reflected-light scale, w: shell scale
            float4 _KSPVE_FldDeckT;      // x, y, z: the decks' temperatures (K), w: the clear deep layer's
            float4 _KSPVE_FldDeckS;      // x, y, z: the share of the temperature anomaly each deck feels, w: the deep layer's
            float4 _KSPVE_FldTau;        // x, y, z: the decks' optical depth at condensate 1, w: the share of it in the wavelength shown (1 visible, 0.4 near-infrared)
            float4 _KSPVE_FldView;       // x: 1 for the thermal-infrared view (false-colour ramp), 0 for the eye
            float4 _KSPVE_FldRefl0;      // rgb: deck albedo times its colour (reflected light)
            float4 _KSPVE_FldRefl1;
            float4 _KSPVE_FldRefl2;
            float4 _KSPVE_FldStarA;      // xyz: direction to a star (world, unit), w: its strength relative to the Sun at the home planet (0 = none)
            float4 _KSPVE_FldStarB;
            float4 _KSPVE_FldStarC;
            sampler2D _KSPVE_FldFields;  // rgb: condensate of the three decks, a: temperature anomaly (K); u = longitude / 2 pi, v = latitude / pi + 1/2
            sampler2D _KSPVE_FldEmis;    // 48 x 20 table: chromaticity in rgb, ln luminance in a; row = deck * 5 + mu bin

            struct appdata { float4 vertex : POSITION; };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 rel : TEXCOORD0;   // this point of the shell relative to the camera
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // The unit sphere mesh has radius 0.5 and the shell is scale * radius in size.
                o.rel = _KSPVE_FldCenter.xyz + v.vertex.xyz * (2.0 * _KSPVE_FldCenter.w * _KSPVE_FldCam.w);
                return o;
            }

            // The table's colour for a deck at a temperature and viewing angle.
            float3 Emit(float deck, float mu, float kelvin)
            {
                float xm = (LUT_MU_BINS - 1.0) * saturate((mu - 0.04) / 0.96);
                float b0 = min(floor(xm), LUT_MU_BINS - 2.0);
                float tm = xm - b0;
                float u = saturate(log(max(kelvin, 20.0) / 120.0) / log(3600.0 / 120.0));
                u = (u * (LUT_COLUMNS - 1.0) + 0.5) / LUT_COLUMNS;
                float4 a = tex2Dlod(_KSPVE_FldEmis, float4(u, (deck * LUT_MU_BINS + b0 + 0.5) / LUT_ROWS, 0, 0));
                float4 b = tex2Dlod(_KSPVE_FldEmis, float4(u, (deck * LUT_MU_BINS + b0 + 1.5) / LUT_ROWS, 0, 0));
                return lerp(a.rgb, b.rgb, tm) * exp(lerp(a.a, b.a, tm));
            }

            // The false-colour ramp of a thermal-infrared image: deep red-brown, orange, yellow, white.
            float3 Ramp(float x)
            {
                float3 c0 = float3(0.015, 0.0, 0.0), c1 = float3(0.30, 0.05, 0.02), c2 = float3(0.95, 0.38, 0.05), c3 = float3(1.0, 0.78, 0.28), c4 = float3(1.0, 1.0, 0.85);
                if (x < 0.25) return lerp(c0, c1, x / 0.25);
                if (x < 0.5) return lerp(c1, c2, (x - 0.25) / 0.25);
                if (x < 0.78) return lerp(c2, c3, (x - 0.5) / 0.28);
                return lerp(c3, c4, saturate((x - 0.78) / 0.22));
            }

            float3 Reflected(float3 n, float3 albedoColor, float4 star)
            {
                return albedoColor * star.w * smoothstep(-0.05, 0.25, dot(n, star.xyz));
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 rd = normalize(i.rel);
                float3 c = _KSPVE_FldCenter.xyz;
                float R = _KSPVE_FldCenter.w;
                float cd = dot(c, rd);
                float dist2 = dot(c, c);
                float dmin = sqrt(max(dist2 - cd * cd, 0.0));
                float pixel = max(_KSPVE_FldCam.x * sqrt(dist2), 1e-6);
                float coverage = saturate((R - dmin) / pixel + 0.5);
                if (coverage <= 0.0) discard;

                float h = cd * cd - (dist2 - R * R);
                float3 n;
                if (h > 0.0) n = normalize(rd * (cd - sqrt(h)) - c);
                else n = normalize(rd * cd - c);   // the limb's antialiasing fringe
                float mu = max(dot(n, -rd), 0.03);

                float3 local = float3(dot(n, _KSPVE_FldAxisX.xyz), dot(n, _KSPVE_FldAxisY.xyz), dot(n, _KSPVE_FldAxisZ.xyz));
                float lat = asin(clamp(local.y, -1.0, 1.0));
                float lon = atan2(local.z, local.x);
                float4 f = tex2Dlod(_KSPVE_FldFields, float4(frac(lon / (2.0 * PI) + 1.0), lat / PI + 0.5, 0, 0));
                float theta = f.a;

                // Eddington-Barbier limb darkening: the temperature where the slant optical depth is one.
                float limb = pow((mu + 2.0 / 3.0) / (5.0 / 3.0), 0.25);
                float slant = 1.0 / max(mu, 0.05);

                float3 emission = 0.0;
                float trans = 1.0;
                float3 decks = float3(_KSPVE_FldDeckT.x, _KSPVE_FldDeckT.y, _KSPVE_FldDeckT.z);
                float3 shares = float3(_KSPVE_FldDeckS.x, _KSPVE_FldDeckS.y, _KSPVE_FldDeckS.z);
                float3 taus = float3(_KSPVE_FldTau.x, _KSPVE_FldTau.y, _KSPVE_FldTau.z) * max(f.rgb, 0.0) * slant * _KSPVE_FldTau.w;
                float3 cover = 1.0 - exp(-taus);
                [unroll] for (int d = 0; d < 3; d++)
                {
                    float kelvin = (decks[d] + theta * shares[d]) * limb;
                    emission += trans * cover[d] * Emit((float)d, mu, kelvin);
                    trans *= 1.0 - cover[d];
                }
                emission += trans * Emit(3.0, mu, (_KSPVE_FldDeckT.w + theta * _KSPVE_FldDeckS.w) * limb);
                float3 color = emission * _KSPVE_FldCam.y;
                if (_KSPVE_FldView.x > 0.5)
                {
                    // Thermal infrared: the table is grey; the radiance relative to the clear deep layer picks the colour.
                    float reference = Emit(3.0, 1.0, _KSPVE_FldDeckT.w).g;
                    // The ramp runs from the radiance of the top cloud deck (dark) to that of the clear deep layer (white).
                    float top = Emit(0.0, 1.0, _KSPVE_FldDeckT.x).g;
                    float x = saturate(log(max(emission.g, 1e-30) / top) / log(reference / top));
                    x = pow(x, 0.7) * lerp(0.6, 1.0, sqrt(mu));   // lifts the middle of the range; the limb is darker
                    return float4(Ramp(x) * reference * _KSPVE_FldCam.y * coverage, coverage);
                }

                // Reflected starlight: the decks' albedo colours weighted by what is on top.
                float w0 = 1.0 - exp(-_KSPVE_FldTau.x * max(f.r, 0.0));
                float w1 = (1.0 - w0) * (1.0 - exp(-_KSPVE_FldTau.y * max(f.g, 0.0)));
                float w2 = (1.0 - w0 - w1) * (1.0 - exp(-_KSPVE_FldTau.z * max(f.b, 0.0)));
                float3 albedo = w0 * _KSPVE_FldRefl0.rgb + w1 * _KSPVE_FldRefl1.rgb + w2 * _KSPVE_FldRefl2.rgb;
                color += _KSPVE_FldCam.z * (Reflected(n, albedo, _KSPVE_FldStarA) + Reflected(n, albedo, _KSPVE_FldStarB) + Reflected(n, albedo, _KSPVE_FldStarC));

                return float4(color * coverage, coverage);
            }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Back ZWrite Off ZTest LEqual
        Blend One OneMinusSrcAlpha

        Pass // 0: the shell (premultiplied)
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
    Fallback Off
}
