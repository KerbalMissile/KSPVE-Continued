Shader "Hidden/KSPVE/Stars/Star"
{
    // A star's surface and surroundings on Camera ScaledSpace (KSPVE.Stars.StarRenderer): the photosphere (limb darkening from
    // the effective temperature by a grey atmosphere, granulation at three scales with level of detail, sunspots with umbra
    // and penumbra, faculae), the chromospheric rim and spicules, prominences and coronal loops raymarched as emitting,
    // absorbing gas, dark filaments on the disc, the corona (K-corona streamers, polar plumes) and CMEs (bright front, dark
    // cavity, bright core). One quad over the star's bounding sphere, premultiplied.
    // Everything is in stellar radii in the star's model frame (y = the star's north; KSPVE.Pure.Stars.StarActivity.Snapshot
    // gives structures in it). tools/BlackHolePreview/StarFx.cs is a line-for-line CPU copy of this file used to tune the look.
    // View (display setting): 0 eye (realistic; the faint things are drawn at their real strengths), 1 H-alpha telescope
    // (656.3 nm emission bright, the reference look), 2 extreme ultraviolet (Fe IX 17.1 nm, gold).
    CGINCLUDE
            #include "UnityCG.cginc"

            #define SPOT_MAX 24
            #define STR_MAX 20
            #define SHELL_MAX 3
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
            float4 _KSPVE_StarLook;        // x: effective temperature (K), y: disc radiance at its centre (HDR), z: view (0 eye, 1 H-alpha, 2 EUV), w: structures' visibility (display; 1)
            float4 _KSPVE_StarLook2;       // x: activity level, y: corona on (0/1), z: surface detail (display; 1 = real), w: eclipse veil (eye view: 1 shows the corona, 0 hides it)
            float4 _KSPVE_StarFlags;       // x: draw the surface (replace the game's), y: prominences & loops on, z: CMEs on, w: streamer belt spread 0..1
            float4 _KSPVE_StarCounts;      // x: spots, y: structures, z: CME shells, w: radians per target pixel
            float4 _KSPVE_StarTexture;     // x: texture turn (rad, the rotation of the granulation), y: flare level 0..1, z: mean activity-region latitude (rad), w: seed
            float4 _KSPVE_StarSpotA[SPOT_MAX];    // xyz: group direction, w: half separation of its spots
            float4 _KSPVE_StarSpotB[SPOT_MAX];    // xyz: unit axis (following -> leading), w: leading umbra radius
            float4 _KSPVE_StarSpotC[SPOT_MAX];    // x: faculae radius, y: strength 0..1, z: flare 0..1, w: seed
            float4 _KSPVE_StarSpotD[SPOT_MAX];    // x: age through the group's life 0..1
            float4 _KSPVE_StarStrA[STR_MAX];      // xyz: footpoint A (arch) or centre (filament), w: kind (0 arch, 1 filament)
            float4 _KSPVE_StarStrB[STR_MAX];      // xyz: footpoint B (arch) or axis (filament), w: height
            float4 _KSPVE_StarStrC[STR_MAX];      // x: width, y: ribbon half-width (arch) or half-length rad (filament), z: nest, w: brightness
            float4 _KSPVE_StarStrD[STR_MAX];      // x: opacity, y: erupt, z: seed
            float4 _KSPVE_StarShellA[SHELL_MAX];  // xyz: direction, w: apex radius
            float4 _KSPVE_StarShellB[SHELL_MAX];  // x: half width (rad), y: seed, z: core
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

            float3 Hash33(float3 p3)
            {
                p3 = frac(p3 * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yxx) * p3.zyx);
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

            // Cellular noise: distance to the nearest feature point and the gap to the second nearest (0 on cell walls). The
            // points drift with time so the cells boil.
            void Cells(float3 p, float boil, out float d1, out float lane)
            {
                float3 ip = floor(p), fp = frac(p);
                float a = 8.0, b = 8.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 o = 0.5 + 0.5 * sin(6.2831853 * (Hash33(ip + g) + boil));
                    float3 r = g + o - fp;
                    float d = dot(r, r);
                    if (d < a) { b = a; a = d; }
                    else if (d < b) b = d;
                }
                d1 = sqrt(a);
                lane = sqrt(b) - d1;
            }

            // --- colour ---------------------------------------------------------------------------
            // Blackbody chromaticity (Kim et al. 2002 Planckian locus; constants match ColorTemperature.cs).
            float3 Blackbody(float kelvin)
            {
                float t = clamp(kelvin, 1667.0, 25000.0);
                float t2 = t * t, t3 = t2 * t;
                float x = t < 4000.0
                    ? -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910
                    : -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;
                float x2 = x * x, x3 = x2 * x;
                float y = t < 2222.0 ? -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683
                        : (t < 4000.0 ? -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867
                                      : 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483);
                float3 xyz = float3(x / y, 1.0, (1.0 - x - y) / y);
                float3 rgb = float3(
                    3.2404542 * xyz.x - 1.5371385 * xyz.y - 0.4985314 * xyz.z,
                   -0.9692660 * xyz.x + 1.8760108 * xyz.y + 0.0415560 * xyz.z,
                    0.0556434 * xyz.x - 0.2040259 * xyz.y + 1.0572252 * xyz.z);
                rgb = max(rgb, 0.0);
                rgb /= max(max(rgb.r, max(rgb.g, rgb.b)), 1e-5);
                return rgb * saturate(kelvin / 1667.0);
            }

            float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // The star's colour at luminance 1.
            float3 StarLight(float kelvin)
            {
                float3 c = Blackbody(kelvin);
                c = max(Lum(c) + (c - Lum(c)) * 1.6, 0.0);   // a little deeper, as the other star drawings are
                return c / max(Lum(c), 1e-4);
            }

            // Grey atmosphere (Eddington): T^4 = 3/4 Teff^4 (tau + 2/3). Planck at the red, green and blue wavelengths gives
            // the limb's darkening and reddening (Eddington-Barbier: I(mu) = B(T(tau = mu))). Ratios to the centre cancel the
            // wavelength factors, leaving exp(c2 / (lambda T)) - 1.
            static const float3 Lambda = float3(610.0, 550.0, 465.0);
            static const float SecondRadiation = 1.438777e7;   // hc/k, nm K

            float GreyTemperature(float teff, float tau) { return teff * pow(0.75 * (tau + 0.6666667), 0.25); }

            float3 PlanckRatio(float t, float tCentre)
            {
                float3 inv = SecondRadiation / Lambda;
                return (exp(inv / tCentre) - 1.0) / (exp(inv / t) - 1.0);
            }

            float3 RotateY(float3 v, float angle)
            {
                float c = cos(angle), s = sin(angle);
                return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
            }

            float Disk(float d, float r, float soft) { return 1.0 - smoothstep(r - soft, r + soft, d); }

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

            // --- the surface ------------------------------------------------------------------------
            // Sunspot groups and faculae at a point on the unit sphere: x umbra, y penumbra, z faculae, w flare (0..1).
            float4 SpotField(float3 n, float soft)
            {
                float umbra = 0.0, pen = 0.0, fac = 0.0, fl = 0.0;
                int count = (int)_KSPVE_StarCounts.x;
                [loop] for (int k = 0; k < count; k++)
                {
                    float4 A = _KSPVE_StarSpotA[k], B = _KSPVE_StarSpotB[k], C = _KSPVE_StarSpotC[k];
                    float age = _KSPVE_StarSpotD[k].x;
                    float reach = C.x * 1.5 + A.w * 2.0 + 0.02;
                    float3 dc = n - A.xyz;
                    if (dot(dc, dc) > reach * reach) continue;
                    float strength = C.y, seed = C.w;
                    // A spot shrinks as its group decays.
                    float ru = B.w * (0.4 + 0.6 * strength);
                    float3 lead = normalize(A.xyz + B.xyz * A.w), fol = normalize(A.xyz - B.xyz * A.w);
                    // Ragged edges.
                    float edge = 1.0 + 0.5 * (Noise3((n - A.xyz) * (5.0 / max(ru, 1e-3)) + seed * 13.0) - 0.5);
                    float dl = length(n - lead) / edge, df = length(n - fol) / edge;
                    // The following spot dies first; old groups break into many pores.
                    float rl = ru, rf = ru * 0.55 * (1.0 - 0.75 * smoothstep(0.45, 0.95, age));
                    float s = max(soft, 0.12 * ru);
                    // Penumbra: radial filaments round the spot.
                    float3 t2 = cross(A.xyz, B.xyz);
                    float ang = atan2(dot(n - lead, t2), dot(n - lead, B.xyz));
                    float fib = 0.72 + 0.18 * Noise3(float3(ang * 5.0, dl / max(rl, 1e-3) * 2.0, seed)) + 0.10 * Noise3(float3(ang * 23.0, dl / max(rl, 1e-3) * 5.0, seed + 3.0));
                    float u = max(Disk(dl, rl, s), Disk(df, rf, s));
                    float p = max(Disk(dl, rl * 2.4, s * 1.5), Disk(df, rf * 2.4, s * 1.5)) * fib;
                    // Pores and small spots scattered about the group.
                    float pores = 3.0 + 4.0 * smoothstep(0.3, 0.9, age);
                    [unroll] for (int j = 0; j < 7; j++)
                    {
                        if (j >= pores) break;
                        float3 h = Hash33(float3(seed * 7.0, j, 3.7));
                        float3 pc = normalize(A.xyz + B.xyz * ((h.x - 0.5) * 3.0 * A.w) + t2 * ((h.y - 0.5) * 2.4 * A.w));
                        float rp = ru * (0.2 + 0.25 * h.z);
                        u = max(u, Disk(length(n - pc), rp, s) * 0.9);
                        p = max(p, Disk(length(n - pc), rp * 2.0, s));
                    }
                    umbra = max(umbra, u * strength);
                    pen = max(pen, p * strength);
                    // Faculae: the bright network round the group.
                    float dg = length(n - A.xyz);
                    float rough = 0.6 + 0.8 * Noise3(n * 70.0 + seed * 5.0);
                    fac = max(fac, strength * exp(-dg * dg / (C.x * C.x)) * rough);
                    // A flare brightens the ribbons beside the neutral line, between the two spots.
                    fl = max(fl, C.z * exp(-dot(n - A.xyz, n - A.xyz) / (0.5 * A.w * A.w + 1e-5)));
                }
                return float4(umbra, pen, fac, fl);
            }

            // The sun's three scales of cell: supergranules (diameter 0.043 R, the chromospheric network), mesogranules (0.01 R)
            // and granules (0.0014 R, rms contrast 15 per cent in white light), faded out as they shrink below the target pixel.
            // x: granulation multiplier (1 = mean), y: the network 0..1 (supergranule walls).
            float3 Granulation(float3 n, float footprint, float mu)
            {
                float detail = _KSPVE_StarLook2.z;
                float time = _KSPVE_StarRay.w;
                float3 q = RotateY(n, -_KSPVE_StarTexture.x);
                float d1, lane;
                Cells(q * 23.0, time * 1.2e-5, d1, lane);
                float network = 1.0 - smoothstep(0.0, 0.22, lane);                 // 1 on the walls
                float cellular = smoothstep(0.0, 0.35, lane) - 0.6;               // bright cell interiors, dark walls
                float superFade = saturate(1.0 - footprint / (0.043 * 0.8));
                Cells(q * 100.0, time * 2.0e-4, d1, lane);
                float meso = smoothstep(0.0, 0.3, lane) - 0.65;
                float mesoFade = saturate(1.0 - footprint / (0.01 * 0.8));
                Cells(q * 700.0, time * 1.4e-3, d1, lane);
                float gran = 0.9 * smoothstep(0.0, 0.32, lane) + 0.1 * (1.0 - d1) - 0.62;
                float granFade = saturate(1.0 - footprint / (0.0014 * 1.0));
                // Granule contrast falls towards the limb (the line of sight climbs through them).
                float c = lerp(0.12, 0.30, saturate(mu));
                float g = 1.0 + detail * (0.42 * c * gran * granFade + 0.08 * meso * mesoFade - 0.03 * network * superFade);
                return float3(g, network * superFade, cellular * superFade);
            }

            // The disc's colour at the point n (unit, the model frame) seen along rd: linear light.
            float3 Surface(float3 n, float3 rd, float footprint, out float spotDark)
            {
                float teff = _KSPVE_StarLook.x, disc = _KSPVE_StarLook.y;
                int view = (int)(_KSPVE_StarLook.z + 0.5);
                float mu = saturate(dot(n, -rd));
                float soft = max(footprint * 0.7, 0.0005);
                float4 spot = SpotField(n, soft);
                float3 gran = Granulation(n, footprint, mu);
                float lat = n.y;
                float tau = mu;
                float t = GreyTemperature(teff, tau);
                float tc = GreyTemperature(teff, 1.0);
                // Temperatures of the features, as factors on the local one: umbra about 0.69 of the photosphere (4000 K),
                // penumbra 0.93; faculae hotter, more so near the limb where they stand above the surface.
                float scale = 1.0;
                scale = lerp(scale, 0.93, spot.y);
                scale = lerp(scale, 0.69, spot.x);
                scale += spot.z * 0.035 * (0.35 + 1.65 * (1.0 - mu)) * (1.0 - spot.y);
                scale += spot.w * 0.25;
                float3 ratio = PlanckRatio(t * scale, tc);
                float3 star = StarLight(teff);
                float3 colour = star * ratio * gran.x;
                spotDark = saturate(spot.x + 0.5 * spot.y);
                if (view == 0) return colour * disc;
                if (view == 1)
                {
                    // H-alpha filter: the chromosphere's network of supergranule walls and plage glow over the same disc; dark
                    // filaments come from the structures. The network is real in the line (the photosphere is smooth).
                    // The cells of the network (about 45 across the disc) show as a mottle of bright cells, like the chromosphere in the line.
                    float net = gran.y * (0.18 + 0.5 * saturate(spot.z)) + 0.35 * spot.z + 0.6 * spot.w;
                    colour *= 1.0 + net * float3(0.55, 0.28, 0.12);
                    colour *= 1.0 + 0.30 * _KSPVE_StarLook2.z * gran.z;
                    colour = lerp(colour, colour * float3(1.0, 0.8, 0.62), 0.35 * (1.0 - mu));
                    return colour * disc;
                }
                // EUV (Fe IX 17.1 nm): the disc is dim, active regions and the network bright, polar coronal holes dark, the limb
                // brightens (the corona's emission is optically thin: more of it along a slanting line of sight).
                float hole = smoothstep(0.78, 0.96, abs(lat)) * 0.8;
                float euv = 0.10 + 0.55 * gran.y + 1.8 * spot.z + 0.7 * spot.x + 0.9 * spot.y + 2.5 * spot.w;
                euv *= (1.0 - hole) * (1.0 + 0.9 * (1.0 - mu));
                euv *= 0.8 + 0.4 * (gran.x - 1.0) * 4.0 + 0.2;
                return float3(1.0, 0.62, 0.17) * (euv * disc);
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
            float AbsorptionScale()
            {
                int view = (int)(_KSPVE_StarLook.z + 0.5);
                return view == 0 ? 0.0 : (view == 1 ? 0.5 : 0.25);
            }

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

            float FilamentDensity(float3 p, float3 c, float3 e, float H, float width, float halfLen, float seed)
            {
                float r = length(p);
                float h = r - 1.0;
                if (h < -0.012) return 0.0;
                float3 ph = p / r;
                float s = atan2(dot(ph, e), dot(ph, c));
                if (abs(s) > halfLen) return 0.0;
                float3 f = cross(c, e);
                float wOff = dot(ph, f) * r;
                float along = abs(s) / halfLen;
                float env = pow(saturate(1.0 - along * along), 0.6);
                float meander = (Noise3(float3(s * 5.0, seed, 1.7)) - 0.5) * 0.035;
                // Hedgerow: vertical curtains of different heights side by side.
                float column = Noise3(float3(s * 55.0, seed * 3.1, 5.0));
                float hc = H * (0.5 + 1.2 * column);
                float vertical = (1.0 - smoothstep(0.45, 1.0, max(h, 0.0) / hc)) * smoothstep(-0.012, 0.004, h);
                float sheet = exp(-(wOff - meander) * (wOff - meander) / (width * width));
                float threads = 0.5 + 0.5 * Noise3(float3(s * 240.0, max(h, 0.0) * 55.0, seed));
                return env * vertical * sheet * threads;
            }

            // Integrates one structure along the ray over [t0, t1] (model frame, distances in stellar radii from the camera):
            // x emission weight (source times optical depth added), y core emission, z optical depth. Optical depth per unit
            // density: kappa.
            float3 Integrate(int k, float3 ro, float3 rd, float tLimit, float jitter)
            {
                float4 A = _KSPVE_StarStrA[k], B = _KSPVE_StarStrB[k], C = _KSPVE_StarStrC[k], D = _KSPVE_StarStrD[k];
                float height = B.w, width = C.x, brightness = C.w, opacity = D.x, erupt = D.y, seed = D.z;
                float3 centre, fa = 0, fu = 0, fv = 0, fw = 0;
                float radius, hc = 1.0, hv = 1.0;
                bool arch = A.w < 0.5;
                if (arch)
                {
                    float3 a = A.xyz, b = B.xyz;
                    float3 m = 0.5 * (a + b);
                    hc = max(0.5 * length(b - a), 1e-4);
                    float3 u = (b - a) / (2.0 * hc);
                    float3 up = m / max(length(m), 1e-4);
                    float3 v = normalize(up - u * dot(up, u));
                    hv = max(1.0 + height - length(m), 1e-3);
                    fa = m; fu = u; fv = v; fw = cross(u, v);
                    centre = m + v * (0.5 * hv);
                    radius = sqrt(hc * hc + 0.25 * hv * hv + C.y * C.y) + 3.0 * width + 0.01;
                }
                else
                {
                    centre = A.xyz * (1.0 + 2.0 * height);
                    radius = 2.0 * sin(min(C.y, 1.5) * 0.5) + 4.5 * height + 3.0 * width + 0.02;
                }
                float t0, t1;
                if (!Sphere(ro, rd, centre, radius, t0, t1)) return 0.0;
                t0 = max(t0, 0.0);
                t1 = min(t1, tLimit);
                if (t1 <= t0) return 0.0;
                const int steps = 40;
                float dt = (t1 - t0) / steps;
                float kappa = (arch ? 34.0 : 48.0) * opacity;
                float transmittance = 1.0, emission = 0.0, coreEmission = 0.0, tau = 0.0;
                [loop] for (int i = 0; i < steps; i++)
                {
                    float t = t0 + (i + jitter) * dt;
                    float3 p = ro + rd * t;
                    float core = 0.0;
                    float dens = arch ? ArchDensity(p, fa, fu, fv, fw, hc, hv, width, C.y, C.z, seed, erupt, core)
                                      : FilamentDensity(p, A.xyz, B.xyz, height, width, C.y, seed);
                    float dtau = kappa * dens * dt;
                    // Emission with self-absorption: the source function times what this step adds to the optical depth.
                    emission += transmittance * brightness * (1.0 - exp(-dtau));
                    coreEmission += transmittance * (1.0 - exp(-kappa * 2.0 * core * dt));
                    transmittance *= exp(-dtau);
                    tau += dtau;
                }
                return float3(emission, coreEmission, tau);
            }

            // --- the corona ---------------------------------------------------------------------------
            // Baumbach-Allen K-corona, in units of the mean disc brightness, plus the F-corona (KSPVE.Pure.Stars.StarOptics).
            float KCorona(float rho)
            {
                float r = max(rho, 1.0);
                return 1e-6 * (0.0532 * pow(r, -2.5) + 1.425 * pow(r, -7.0) + 2.565 * pow(r, -17.0)) + 7e-9 * pow(r, -2.2);
            }

            // How the corona's density varies with direction (1 = mean): the streamer belt along the equator (narrow near
            // minimum, all round near maximum), radial rays, fainter polar coronal holes with thin plumes.
            float CoronaShape(float3 dir, float r)
            {
                float spread = _KSPVE_StarFlags.w;
                float lat = asin(clamp(dir.y, -1.0, 1.0));
                float lon = atan2(dir.z, dir.x);
                float halfWidth = lerp(0.3, 1.6, spread);
                float belt = exp(-lat * lat / (halfWidth * halfWidth));
                float base = lerp(0.25, 1.0, belt);
                float rays = 0.55 + 0.9 * Noise3(float3(lon * 3.5 + _KSPVE_StarTexture.w, lat * 5.0, 2.0));
                float fine = 0.7 + 0.6 * Noise3(float3(lon * 14.0, lat * 18.0, 7.0 + _KSPVE_StarTexture.w));
                // Polar plumes: thin bright rays in the holes, near the poles.
                float pole = smoothstep(0.9, 1.25, abs(lat));
                float plumes = pole * pow(Noise3(float3(lon * 9.0, 4.0, _KSPVE_StarTexture.w)), 3.0) * 2.5;
                // Helmet streamers' cusps above the belt: a bright core that thins with height.
                float helmet = belt * exp(-(r - 1.0) / 0.5) * (0.4 + 0.8 * Noise3(float3(lon * 6.0, 9.0, 3.0)));
                return base * rays * fine + plumes + 0.6 * helmet;
            }

            // The corona's radiance in the sky-plane at projected distance rho from the centre, along ro + rd t: the shape is
            // taken from a few samples along the line of sight, weighted towards the closest approach. Returns relative to the
            // disc's brightness (before the view's gain).
            float CoronaAlong(float3 ro, float3 rd, float tClosest, float rho)
            {
                float sum = 0.0, weight = 0.0;
                [unroll] for (int i = 0; i < 5; i++)
                {
                    float z = (float(i) - 2.0) * 0.8 * rho;
                    float t = tClosest + z;
                    float3 p = ro + rd * t;
                    float r = length(p);
                    float w = pow(max(r, 1.0), -3.5);
                    sum += w * CoronaShape(p / r, r);
                    weight += w;
                }
                return sum / max(weight, 1e-9);
            }

            // --- CMEs ---------------------------------------------------------------------------------
            // One shell (ice-cream cone: a sphere in a cone from the star): x emission (bright front), y the dark cavity's
            // optical depth, z the core. Radiance falls as r^-2.5 (the corona it travels through thins).
            float3 ShellAlong(int k, float3 ro, float3 rd, float tLimit, float jitter)
            {
                float4 A = _KSPVE_StarShellA[k], B = _KSPVE_StarShellB[k];
                float3 axis = A.xyz;
                float apex = A.w, hw = min(B.x, 1.5);
                float s = sin(hw);
                float a = apex * s / (1.0 + s), cc = apex - a;
                float3 sc = axis * cc;
                float t0, t1;
                if (!Sphere(ro, rd, sc, a * 1.35, t0, t1)) return 0.0;
                t0 = max(t0, 0.0); t1 = min(t1, tLimit);
                if (t1 <= t0) return 0.0;
                const int steps = 30;
                float dt = (t1 - t0) / steps;
                float front = 0.0, cavity = 0.0, core = 0.0;
                float3 coreC = axis * (cc - 0.22 * a);
                [loop] for (int i = 0; i < steps; i++)
                {
                    float3 p = ro + rd * (t0 + (i + jitter) * dt);
                    float r = length(p);
                    float cosAng = dot(p / r, axis);
                    float cone = smoothstep(cos(hw), cos(hw * 0.75), cosAng);
                    float dd = length(p - sc) / a;
                    float shell = exp(-(dd - 1.0) * (dd - 1.0) / 0.018);
                    // Structure within the front: fine loops of it.
                    shell *= 0.6 + 0.8 * Noise3(float3(p * (4.0 / a)) + B.y);
                    float inside = 1.0 - smoothstep(0.35, 0.82, dd);
                    float dr = length(p - coreC) / a;
                    float blob = exp(-dr * dr / 0.035) * (0.7 + 0.6 * Noise3(p * (9.0 / a) + B.y * 3.0));
                    float falloff = pow(max(r, 1.0), -2.5);
                    front += cone * shell * falloff * dt;
                    cavity += cone * inside * dt / max(a, 0.5);
                    core += cone * blob * falloff * dt * B.z;
                }
                return float3(front, cavity, core);
            }

            // --- one pixel --------------------------------------------------------------------------------
            float4 frag(v2f i) : SV_Target
            {
                float3 dirW = normalize(_KSPVE_StarForward.xyz + _KSPVE_StarRight.xyz * (i.ndc.x * _KSPVE_StarRay.x) + _KSPVE_StarUp.xyz * (i.ndc.y * _KSPVE_StarRay.y));
                float radiusS = _KSPVE_StarCenter.w;
                // Distances in stellar radii from the camera; the star at c.
                float3 c = _KSPVE_StarCenter.xyz / radiusS;
                float3 ro = ToModel(-c);
                float3 rd = ToModel(dirW);
                int view = (int)(_KSPVE_StarLook.z + 0.5);
                float disc = _KSPVE_StarLook.y;
                float vis = _KSPVE_StarLook.w;
                float veil = view == 0 ? _KSPVE_StarLook2.w : 1.0;

                // Bodies in front: the nearest hit along this ray (analytic spheres in world space, camera at the origin).
                float tOcc = 1e30;
                [unroll] for (int k = 0; k < OCC_MAX; k++)
                {
                    float4 o = _KSPVE_StarOcc[k];
                    if (o.w <= 0.0) continue;
                    float to0, to1;
                    // only a body in front of the camera covers the star: a ray's line also meets bodies behind it (both hits
                    // negative), which would black out every ray whose backward extension passes through them
                    if (Sphere(0.0, dirW, o.xyz / radiusS, o.w / radiusS, to0, to1) && to1 > 0.0 && to0 < tOcc) tOcc = max(to0, 0.0);
                }

                float t0s, t1s;
                bool hitStar = Sphere(ro, rd, 0.0, 1.0, t0s, t1s) && t0s > 0.0;
                float tSurf = hitStar ? t0s : 1e30;
                float tLimit = min(tSurf, tOcc);
                bool surfaceVisible = hitStar && tSurf < tOcc;
                float jitter = frac(Hash13(float3(i.pos.xy, 7.0)) + _KSPVE_StarRay.w * 0.618);

                float3 emission = 0.0;
                float3 result = 0.0;
                float alpha = 0.0;
                float absorb = 1.0;
                float footprint = _KSPVE_StarCounts.w * max(tSurf, 1.0);

                // Prominences and loops (and, in the eye view at its real strengths, nothing you would notice).
                float4 gas = GasSource();
                float3 gasColour = gas.yzw;
                float3 coreColour = lerp(gasColour, float3(1.0, 0.8, 0.45), 0.35);
                if (_KSPVE_StarFlags.y > 0.5)
                {
                    int count = (int)_KSPVE_StarCounts.y;
                    float absorbScale = AbsorptionScale();
                    [loop] for (int s = 0; s < count; s++)
                    {
                        float3 r = Integrate(s, ro, rd, tLimit, jitter);
                        emission += (r.x * gasColour + r.y * coreColour * 1.4) * gas.x * disc * veil;
                        absorb *= exp(-r.z * absorbScale);
                    }
                }

                if (surfaceVisible)
                {
                    float3 n = normalize(ro + rd * tSurf);
                    float spotDark;
                    float3 surface = Surface(n, rd, footprint, spotDark);
                    // A filament dims the disc behind it, but no further than H-alpha filaments do (0.6-0.8 of the
                    // disc's brightness): many overlapping ones must not black the star out.
                    absorb = max(absorb, 0.7);
                    result = surface * absorb + emission;
                    alpha = _KSPVE_StarFlags.x > 0.5 ? 1.0 : 0.0;
                    if (alpha < 0.5) result = emission;
                }
                else
                {
                    result = emission;
                }

                // Beyond the disc: the rim of the chromosphere with its spicules, the corona, CMEs.
                float b = dot(ro, rd);
                float rho = sqrt(max(dot(ro, ro) - b * b, 0.0));
                float tClosest = -b;
                if (!hitStar && tClosest > 0.0 && tClosest < tOcc)
                {
                    float3 pc = ro + rd * tClosest;
                    float h = rho - 1.0;
                    // Chromosphere: 2000 km (0.003 R) of red emission, spicules to 10000 km (0.015 R).
                    float azimuth = atan2(pc.z, pc.x) + pc.y * 3.0;
                    float spicule = pow(Noise3(float3(azimuth * 160.0, h * 40.0, _KSPVE_StarRay.w * 0.004)), 4.0);
                    float chromo = exp(-max(h, 0.0) / 0.0035) * (0.65 + 0.35 * Noise3(float3(azimuth * 40.0, 3.0, 1.0)))
                                 + spicule * 1.4 * exp(-max(h, 0.0) / 0.007);
                    float chromoGain = view == 0 ? 1.2e-3 : (view == 1 ? 0.9 : 0.04);
                    float3 chromoColour = view == 2 ? float3(1.0, 0.7, 0.25) : float3(1.0, 0.32, 0.30);
                    float3 rim = chromoColour * (chromo * chromoGain * disc * (view == 1 ? vis : 1.0) * veil);
                    // Corona.
                    float corona = 0.0;
                    float3 coronaColour = 1.0;
                    if (_KSPVE_StarLook2.y > 0.5)
                    {
                        float shape = CoronaAlong(ro, rd, tClosest, rho);
                        if (view == 0)
                        {
                            corona = KCorona(rho) * shape * disc * vis;
                            coronaColour = float3(1.0, 0.97, 0.93);
                        }
                        else if (view == 1)
                        {
                            corona = (0.050 * exp(-(rho - 1.0) / 0.30) + 0.010 / (rho * rho)) * (0.4 + 0.6 * _KSPVE_StarLook2.x) * shape * disc * vis;
                            coronaColour = float3(1.0, 0.42, 0.15);
                        }
                        else
                        {
                            corona = (0.20 * exp(-(rho - 1.0) / 0.20) + 0.025 / (rho * rho)) * (0.5 + 0.5 * _KSPVE_StarLook2.x) * shape * disc * vis;
                            coronaColour = float3(1.0, 0.68, 0.22);
                        }
                        corona *= veil;
                    }
                    // CMEs: the cavity empties the corona it passes through, the front and the core shine.
                    float cavity = 0.0;
                    float3 cme = 0.0;
                    if (_KSPVE_StarFlags.z > 0.5)
                    {
                        int shells = (int)_KSPVE_StarCounts.z;
                        float cmeGain = view == 0 ? 2.3e-7 : (view == 1 ? 0.12 : 0.15);
                        [loop] for (int q = 0; q < shells; q++)
                        {
                            float3 sh = ShellAlong(q, ro, rd, tOcc, jitter);
                            cavity += sh.y;
                            cme += (sh.x * lerp(float3(1.0, 0.97, 0.93), gasColour, view == 0 ? 0.0 : 0.5) + sh.z * coreColour * 1.6) * cmeGain * disc * (view == 0 ? vis : vis) * veil;
                        }
                    }
                    float3 outer = coronaColour * corona * exp(-1.6 * cavity) + cme;
                    result += rim + outer;
                }

                return float4(result, alpha);
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
