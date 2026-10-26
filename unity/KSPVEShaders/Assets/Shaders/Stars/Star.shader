Shader "Hidden/KSPVE/Stars/Star"
{
    // Prominences and coronal loops over the game's own star surface.
    // Structures are raymarched in stellar radii in the star's model frame.
    CGINCLUDE
            #include "UnityCG.cginc"

            #define STR_MAX 20
            #define OCC_MAX 8

            float4 _KSPVE_StarRect;        // screen rectangle to draw, NDC with y up: xmin, ymin, xmax, ymax
            float4 _KSPVE_StarCenter;      // xyz: the star's centre relative to the camera (scaled units); w: its radius (scaled units)
            float4 _KSPVE_StarRight;       // camera right (world)
            float4 _KSPVE_StarUp;          // camera up (world)
            float4 _KSPVE_StarForward;     // camera forward (world)
            float4 _KSPVE_StarMX;          // the model frame's x axis in world space
            float4 _KSPVE_StarMY;          // ... y (the star's north)
            float4 _KSPVE_StarMZ;          // ... z
            float4 _KSPVE_StarRay;         // tan(fovX/2), tan(fovY/2), bound radius (stellar radii), time (s)
            float4 _KSPVE_StarLook;        // y: disc radiance, z: view (0 eye, 1 H-alpha, 2 EUV), w: structures' visibility
            float _KSPVE_StarEclipseVeil;
            float4 _KSPVE_StarCounts;      // y: structures
            float4 _KSPVE_StarStrA[STR_MAX];      // xyz: footpoint directions, w: kind (0 arch)
            float4 _KSPVE_StarStrB[STR_MAX];      // xyz: second footpoint, w: height
            float4 _KSPVE_StarStrC[STR_MAX];      // x: width, y: ribbon half-width, z: nest, w: brightness
            float4 _KSPVE_StarStrD[STR_MAX];      // x: opacity, y: erupt, z: seed
            float4 _KSPVE_StarOcc[OCC_MAX];       // bodies that may cover the star: xyz centre relative to the camera, w radius (scaled units; 0 = none)

            static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 ndc : TEXCOORD0;
            };

            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 ndc = lerp(_KSPVE_StarRect.xy, _KSPVE_StarRect.zw, Corners[id]);
                o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.ndc = ndc;
                return o;
            }

            // --- noise (arithmetic: no texture needed) ------------------------------------------------
            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i), n100 = Hash13(i + float3(1, 0, 0)), n010 = Hash13(i + float3(0, 1, 0)), n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1)), n101 = Hash13(i + float3(1, 0, 1)), n011 = Hash13(i + float3(0, 1, 1)), n111 = Hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            // --- space ----------------------------------------------------------------------------
            float3 ToModel(float3 v) { return float3(dot(v, _KSPVE_StarMX.xyz), dot(v, _KSPVE_StarMY.xyz), dot(v, _KSPVE_StarMZ.xyz)); }

            // Distance along a ray (unit direction) to a sphere's near and far surfaces; false when it misses.
            bool Sphere(float3 ro, float3 rd, float3 c, float r, out float t0, out float t1)
            {
                float3 oc = ro - c;
                float b = dot(oc, rd), cc = dot(oc, oc) - r * r;
                float disc = b * b - cc;
                t0 = 0.0; t1 = 0.0;
                if (disc <= 0.0) return false;
                float s = sqrt(disc);
                t0 = -b - s; t1 = -b + s;
                return t1 > 0.0;
            }

            // --- prominences and loops ----------------------------------------------------------------
            // How the gas emits and absorbs in each view (display; KSPVE.Stars.StarRenderer documents the numbers). x: source
            // brightness as a fraction of the disc, yzw: its colour. Absorption: a gas of optical depth tau dims what is behind
            // it by exp(-tau) and adds its source (1 - exp(-tau)): dark on the disc, bright at the limb.
            float4 GasSource()
            {
                int view = (int)(_KSPVE_StarLook.z + 0.5);
                float vis = _KSPVE_StarLook.w;
                if (view == 0) return float4(1.5e-4 * vis, 1.0, 0.45, 0.5);        // white-light prominences: about a ten-thousandth of the disc
                if (view == 1) return float4(1.0 * vis, 1.0, 0.33, 0.10);         // H-alpha 656.3 nm: red-orange
                return float4(0.55 * vis, 1.0, 0.68, 0.22);                         // EUV: gold
            }

            // Absorption by structures, per view: 0 in the eye (a prominence is no thicker than this in white light), half in
            // H-alpha (the loops are drawn larger than life by LoopSize, so their full depth would cover the disc), a quarter in EUV.
            // The shape of a loop system: footpoints a and b (unit vectors) joined by an elliptical arch of apex height h.
            // Returns the gas density 0..1 at p. s is the position along the arch 0..1, for the caller's use.
            float ArchDensity(float3 p, float3 m, float3 u, float3 v, float3 w, float hc, float hv, float width, float ribbon, float nest, float seed, float erupt, out float core)
            {
                core = 0.0;
                float3 q = p - m;
                float pu = dot(q, u), pv = dot(q, v), pw = dot(q, w);
                if (pv < -0.03) return 0.0;
                float eu = pu / hc, ev = max(pv, 0.0) / hv;
                float e = sqrt(eu * eu + ev * ev + 1e-9);
                float theta = atan2(ev, eu);                        // 0 at footpoint A's side .. pi at B's
                float s = theta / 3.1415927;
                float g = sqrt(pu * pu / (hc * hc * hc * hc) + max(pv, 0.0) * max(pv, 0.0) / (hv * hv * hv * hv)) / e;
                float gap = 0.085;
                float e2 = e;
                if (nest > 1.5)
                {
                    float k = clamp(floor((1.0 - e) / gap + 0.5), 0.0, nest - 1.0);
                    e2 = e + k * gap;
                }
                float d = (e2 - 1.0) / max(g, 1e-3);
                // The tube is thicker at the top, thinner in the legs, and, for an erupting prominence, its legs pinch.
                float w0 = width * (0.65 + 0.55 * sin(theta)) * (1.0 + 0.5 * erupt);
                float tube = exp(-d * d / (w0 * w0));
                // Fibrils: a bundle of fine strands, running along the arch, side by side across it.
                float across = pw / max(ribbon, 1e-3);
                float ribbonShape = exp(-across * across * 1.4);
                // Fine strands: thin arcs side by side across the band (so they show seen face on), wavering along it, uneven across the ribbon.
                float wobble = 5.0 * Noise3(float3(s * 5.0, pw / max(ribbon, 1e-3) * 3.0, seed * 3.1));
                // Many thin strands, each its own brightness, with a slight twist along the arch.
                float ph = d / (w0 * 0.15) * 3.1415927 + wobble + s * 6.0 * (Hash13(float3(seed, 5.0, 1.0)) - 0.5);
                float strandId = floor(ph / 3.1415927);
                float strandBright = 0.35 + 1.0 * Hash13(float3(strandId, seed, 1.7));
                float strands = 0.08 + 0.92 * pow(saturate(0.5 + 0.5 * sin(ph)), 2.5) * strandBright * (0.5 + 1.0 * Noise3(float3(pw / max(width * 0.6, 1e-3), s * 3.0, seed)));
                // Knots of denser gas along the loops, and brighter towards the footpoints.
                float knots = 0.55 + 0.9 * Noise3(float3(s * 7.0, seed, pw / max(ribbon, 1e-3) * 2.0));
                float feet = 0.75 + 0.5 * pow(abs(cos(theta)), 3.0);
                // The erupting prominence's core: the dense cool gas at the top.
                core = erupt * exp(-(s - 0.5) * (s - 0.5) / 0.015) * exp(-d * d / (w0 * w0 * 4.0)) * ribbonShape;
                return tube * ribbonShape * strands * knots * feet;
            }

            // Integrates one prominence or loop along the ray.
            float2 Integrate(int k, float3 ro, float3 rd, float tLimit, float jitter)
            {
                float4 A = _KSPVE_StarStrA[k], B = _KSPVE_StarStrB[k], C = _KSPVE_StarStrC[k], D = _KSPVE_StarStrD[k];
                float height = B.w, width = C.x, brightness = C.w, opacity = D.x, erupt = D.y, seed = D.z;
                float3 a = A.xyz, b = B.xyz;
                float3 m = 0.5 * (a + b);
                float hc = max(0.5 * length(b - a), 1e-4);
                float3 u = (b - a) / (2.0 * hc);
                float3 up = m / max(length(m), 1e-4);
                float3 v = normalize(up - u * dot(up, u));
                float hv = max(1.0 + height - length(m), 1e-3);
                float3 fa = m, fu = u, fv = v, fw = cross(u, v);
                float3 centre = m + v * (0.5 * hv);
                float radius = sqrt(hc * hc + 0.25 * hv * hv + C.y * C.y) + 3.0 * width + 0.01;
                float t0, t1;
                if (!Sphere(ro, rd, centre, radius, t0, t1)) return float2(0.0, 0.0);
                t0 = max(t0, 0.0);
                t1 = min(t1, tLimit);
                if (t1 <= t0) return float2(0.0, 0.0);
                const int steps = 40;
                float dt = (t1 - t0) / steps;
                float kappa = 34.0 * opacity;
                float transmittance = 1.0, emission = 0.0, coreEmission = 0.0;
                [loop] for (int i = 0; i < steps; i++)
                {
                    float t = t0 + (i + jitter) * dt;
                    float3 p = ro + rd * t;
                    float core = 0.0;
                    float dens = ArchDensity(p, fa, fu, fv, fw, hc, hv, width, C.y, C.z, seed, erupt, core);
                    float dtau = kappa * dens * dt;
                    // Emission with self-absorption: the source function times what this step adds to the optical depth.
                    emission += transmittance * brightness * (1.0 - exp(-dtau));
                    coreEmission += transmittance * (1.0 - exp(-kappa * 2.0 * core * dt));
                    transmittance *= exp(-dtau);
                }
                return float2(emission, coreEmission);
            }

            // --- one pixel --------------------------------------------------------------------------------
            float4 frag(v2f i) : SV_Target
            {
                float3 dirW = normalize(_KSPVE_StarForward.xyz + _KSPVE_StarRight.xyz * (i.ndc.x * _KSPVE_StarRay.x) + _KSPVE_StarUp.xyz * (i.ndc.y * _KSPVE_StarRay.y));
                float radiusS = _KSPVE_StarCenter.w;
                float3 c = _KSPVE_StarCenter.xyz / radiusS;
                float3 ro = ToModel(-c);
                float3 rd = ToModel(dirW);
                int view = (int)(_KSPVE_StarLook.z + 0.5);
                float disc = _KSPVE_StarLook.y;
                float vis = _KSPVE_StarLook.w;
                float veil = view == 0 ? _KSPVE_StarEclipseVeil : 1.0;

                float tOcc = 1e30;
                [unroll] for (int k = 0; k < OCC_MAX; k++)
                {
                    float4 o = _KSPVE_StarOcc[k];
                    if (o.w <= 0.0) continue;
                    float to0, to1;
                    if (Sphere(0.0, dirW, o.xyz / radiusS, o.w / radiusS, to0, to1) && to1 > 0.0 && to0 < tOcc)
                        tOcc = max(to0, 0.0);
                }

                float t0s, t1s;
                bool hitStar = Sphere(ro, rd, 0.0, 1.0, t0s, t1s) && t0s > 0.0;
                float tLimit = min(hitStar ? t0s : 1e30, tOcc);
                float jitter = frac(Hash13(float3(i.pos.xy, 7.0)) + _KSPVE_StarRay.w * 0.618);
                float3 result = 0.0;
                float4 gas = GasSource();
                float3 gasColour = gas.yzw;
                float3 coreColour = lerp(gasColour, float3(1.0, 0.8, 0.45), 0.35);

                if (_KSPVE_StarCounts.y > 0.0)
                {
                    int count = (int)_KSPVE_StarCounts.y;
                    [loop] for (int s = 0; s < count; s++)
                    {
                        float2 r = Integrate(s, ro, rd, tLimit, jitter);
                        result += (r.x * gasColour + r.y * coreColour * 1.4) * gas.x * disc * veil;
                    }
                }

                return float4(result, 0.0);
            }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass // 0: the star (premultiplied)
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
