Shader "Hidden/KSPVE/Aurora/Aurora"
{
    // Aurorae and airglow of a planet (KSPVE.Pure.Aurora): an emissive ray-march (no scattering) through the glowing layers.
    // This is a line-for-line port of KSPVE.Pure.Aurora.AuroraTracer, which the offline preview (tools/BlackHolePreview,
    // "aurora") runs, so what is tuned there is what is drawn here; keep the two in step.
    // Structure is a function of where a point's magnetic field line meets the surface, not of the point, so every
    // curtain is field-aligned and the rays converge overhead in a corona. Discrete arcs are thin sheets whose crossing a
    // ray finds and integrates exactly (Gauss-Hermite); the smooth kinds are marched in steps; the airglow layers between
    // their shells.
    // Pass 0: march into a (reduced-resolution) buffer. Pass 1: blend with the reprojected history (temporal accumulation).
    // Pass 2: add the result to the camera target.
    CGINCLUDE
            #include "UnityCG.cginc"

            sampler2D_float _CameraDepthTexture;
            sampler2D _KSPVE_AuLut;       // emission per metre (x 1e12, half floats): width = height samples, one row per EmissionRow (13)
            sampler2D _KSPVE_AuLow;       // pass 1, 2: the marched buffer
            sampler2D _KSPVE_AuHistory;   // pass 1: last frame's accumulated buffer

            float4 _KSPVE_Au0;            // x: body radius (m); yzw: dipole centre (m, body frame)
            float4 _KSPVE_AuAxis;         // xyz: geomagnetic axis (body frame); w: Kp
            float4 _KSPVE_AuNoon;         // xyz: noon direction; w: oval colatitude at midnight (rad)
            float4 _KSPVE_AuDawn;         // xyz: dawn direction; w: dayside shift of the oval
            float4 _KSPVE_AuSun;          // xyz: direction to the star; w: oval half-width (rad)
            float4 _KSPVE_AuStorm0;       // onset, surge, east (angles from midnight); strength
            float4 _KSPVE_AuStorm1;       // growth, breakup, bulge, pulsating
            float4 _KSPVE_AuStorm2;       // x: substorm active (expansion or recovery) 0/1
            float4 _KSPVE_AuKinds0;       // arc, hard share, soft share, diffuse (kR)
            float4 _KSPVE_AuKinds1;       // proton, black, sar, steve
            float4 _KSPVE_AuKinds2;       // theta, global, crustal, giant
            float4 _KSPVE_AuGlow;         // airglow rayleighs: green, sodium, hydroxyl, red
            float4 _KSPVE_AuTime;         // tA = fract(ut/600), tP = fract(ut/300), tH = fract(ut/3600), seed (24 bits)
            float4 _KSPVE_AuSlabs0;       // arc slab low, high (m), smooth slab low, high
            float4 _KSPVE_AuSlabs1;       // SAR slab low, high; spots, crustal counts
            float4 _KSPVE_AuGlowLow;      // airglow layers' lower, upper edges (m); 0 = none
            float4 _KSPVE_AuGlowHigh;
            float4 _KSPVE_AuLutInfo;      // low height (m), high height (m), samples, rows
            float4 _KSPVE_AuAtmo;         // Rayleigh optical depth at the surface (rgb), scale height (m)
            float4 _KSPVE_AuView;         // night exposure gain, aurora boost, airglow boost, frame index
            float4 _KSPVE_AuCam;          // xyz: camera in the body frame (m); w: pixel angle (rad)
            float4 _KSPVE_AuRight;        // body frame; w: tan(fov/2) x aspect
            float4 _KSPVE_AuUp;           // body frame; w: tan(fov/2)
            float4 _KSPVE_AuForward;      // body frame; w: 1 when the camera depth texture limits the rays
            float4 _KSPVE_AuPrevRight;    // last frame's camera, for the reprojection of the history; w: history weight (0 = none)
            float4 _KSPVE_AuPrevUp;
            float4 _KSPVE_AuPrevForward;
            float4 _KSPVE_AuSpots[4];     // footprint spots: colatitude, angle from midnight, brightness, tail
            float4 _KSPVE_AuCrust[4];     // crustal regions: direction xyz, radius (in body radii)
            float4 _KSPVE_AuCrust2;       // their strengths

            static const float Km = 1000.0;
            static const float PI = 3.14159265;
            static const int CoarseNodes = 40, SmoothSteps = 48, SarSteps = 20, GlowSteps = 14, MaxCrossings = 14;
            static const float GhX[5] = { 0.0, 0.9585724646, -0.9585724646, 2.0201828705, -2.0201828705 };
            static const float GhW[5] = { 0.9453087205, 0.3936193232, 0.3936193232, 0.0199532421, 0.0199532421 };
            static const float LutScale = 1e-12;   // the table is stored times 1e12 (half floats)

            static const int RowArc = 0, RowArcHard = 1, RowSoftArc = 2, RowDiffuse = 3, RowProton = 4, RowSteve = 5, RowSar = 6,
                RowGlowGreen = 7, RowGlowSodium = 8, RowGlowHydroxyl = 9, RowGlowRed = 10, RowGlobal = 11, RowGiant = 12;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 ndc : TEXCOORD0;
            };

            // A full-screen triangle; the clip-space y follows the camera's projection flip like the black hole's quad.
            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 ndc = float2((id << 1) & 2, id & 2) * 2.0 - 1.0;
                o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.ndc = ndc;
                return o;
            }

            // --- noise: the same integer hash and lattice as KSPVE.Pure.Aurora.AuroraNoise ---------------------
            uint Hash(uint x)
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
            float Rand(uint key) { return (Hash(key) >> 8) * (1.0 / 16777216.0); }
            uint Mix(uint a, uint b) { return Hash((a * 0x9E3779B1U) ^ Hash(b + 0x7F4A7C15U)); }
            float Fade(float t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

            float Value1(float x, uint seed)
            {
                float f = floor(x);
                uint i = (uint)((int)f);
                float t = Fade(x - f);
                float a = Rand(Mix(i, seed)), b = Rand(Mix(i + 1, seed));
                return a + (b - a) * t;
            }

            float Value2(float x, float y, uint seed)
            {
                float fx = floor(x), fy = floor(y);
                uint ix = (uint)((int)fx), iy = (uint)((int)fy);
                float tx = Fade(x - fx), ty = Fade(y - fy);
                uint s = Mix(seed, 0x51ED27U);
                float a = Rand(Mix(Mix(ix, iy), s)), b = Rand(Mix(Mix(ix + 1, iy), s));
                float c = Rand(Mix(Mix(ix, iy + 1), s)), d = Rand(Mix(Mix(ix + 1, iy + 1), s));
                float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
                return ab + (cd - ab) * ty;
            }

            float Corner3(uint ix, uint iy, uint iz, uint s) { return Rand(Mix(Mix(Mix(ix, iy), iz), s)); }

            float Value3(float x, float y, float z, uint seed)
            {
                float fx = floor(x), fy = floor(y), fz = floor(z);
                uint ix = (uint)((int)fx), iy = (uint)((int)fy), iz = (uint)((int)fz);
                float tx = Fade(x - fx), ty = Fade(y - fy), tz = Fade(z - fz);
                uint s = Mix(seed, 0x2C9277U);
                float c000 = Corner3(ix, iy, iz, s), c100 = Corner3(ix + 1, iy, iz, s);
                float c010 = Corner3(ix, iy + 1, iz, s), c110 = Corner3(ix + 1, iy + 1, iz, s);
                float c001 = Corner3(ix, iy, iz + 1, s), c101 = Corner3(ix + 1, iy, iz + 1, s);
                float c011 = Corner3(ix, iy + 1, iz + 1, s), c111 = Corner3(ix + 1, iy + 1, iz + 1, s);
                float x00 = c000 + (c100 - c000) * tx, x10 = c010 + (c110 - c010) * tx;
                float x01 = c001 + (c101 - c001) * tx, x11 = c011 + (c111 - c011) * tx;
                float y0 = x00 + (x10 - x00) * ty, y1 = x01 + (x11 - x01) * ty;
                return y0 + (y1 - y0) * tz;
            }

            // Value noise that slides without ever resetting: two copies a half period apart, cross-faded (phase = fract(ut / 300)).
            float Advected1(float x, float speed, float phase, uint seed)
            {
                float p2 = phase + 0.5 - floor(phase + 0.5);
                float w = 1.0 - abs(2.0 * phase - 1.0);
                float a = Value1(x - speed * 300.0 * phase, seed), b = Value1(x - speed * 300.0 * p2 + 137.1, seed);
                return b + (a - b) * w;
            }

            float Advected2(float x, float y, float vx, float vy, float phase, uint seed)
            {
                float p2 = phase + 0.5 - floor(phase + 0.5);
                float w = 1.0 - abs(2.0 * phase - 1.0);
                float a = Value2(x - vx * 300.0 * phase, y - vy * 300.0 * phase, seed);
                float b = Value2(x - vx * 300.0 * p2 + 71.3, y - vy * 300.0 * p2 + 19.7, seed);
                return b + (a - b) * w;
            }

            float Advected3(float3 q, float3 v, float phase, uint seed)
            {
                float p2 = phase + 0.5 - floor(phase + 0.5);
                float w = 1.0 - abs(2.0 * phase - 1.0);
                float a = Value3(q.x - v.x * 300.0 * phase, q.y - v.y * 300.0 * phase, q.z - v.z * 300.0 * phase, seed);
                float b = Value3(q.x - v.x * 300.0 * p2 + 41.7, q.y - v.y * 300.0 * p2 + 13.1, q.z - v.z * 300.0 * p2 + 7.9, seed);
                return b + (a - b) * w;
            }

            float SS(float a, float b, float x)
            {
                float t = saturate((x - a) / (b - a));
                return t * t * (3.0 - 2.0 * t);
            }

            // --- state shared by the functions -------------------------------------------------------------
            static float gR, gTA, gTP, gTH, gHw, gLambda, gKp;
            static float3 gC, gAxis, gNoon, gDawn, gSun;
            static uint gSeed;

            struct Geo
            {
                float R, ThF, Nu, ThO, D, Y;
                float3 Dir;
            };

            float OvalColat(float nu)
            {
                return _KSPVE_AuNoon.w * (1.0 - _KSPVE_AuDawn.w * 0.5 * (1.0 - cos(nu)));
            }

            Geo GeoOf(float3 p)
            {
                Geo g;
                float3 q = p - gC;
                float rq = max(length(q), 1.0);
                float mu = dot(q, gAxis) / rq;
                float s0 = (1.0 - mu * mu) / max(rq / gR, 1.0);
                g.R = rq / gR;
                g.ThF = asin(sqrt(min(1.0, max(s0, 0.0))));
                g.Nu = atan2(dot(q, gDawn), -dot(q, gNoon));
                g.ThO = OvalColat(g.Nu);
                g.D = (g.ThF - g.ThO) * gR;
                g.Y = g.Nu * gR * max(sin(g.ThO), 0.05);
                g.Dir = p / max(length(p), 1.0);
                return g;
            }

            // --- night: how dark the sky is ----------------------------------------------------------------
            float Darkness(float sinSun) { return 1.0 - SS(-0.3090, -0.1045, sinSun); }

            float ViewerDark(float3 o)
            {
                float lo = max(length(o), 1.0);
                float alt = lo - gR;
                float dark = Darkness(dot(o / lo, gSun));
                float ground = 1.0 - SS(20.0 * Km, 120.0 * Km, alt);
                return 1.0 + (dark - 1.0) * ground;
            }

            float ExposureAt(float3 p, float viewerDark)
            {
                float dark = min(Darkness(dot(p / max(length(p), 1.0), gSun)), viewerDark);
                return 1.0 + (_KSPVE_AuView.x - 1.0) * dark;
            }

            // --- the emission table --------------------------------------------------------------------------
            float3 RowColour(int row, float h)
            {
                float u = (h - _KSPVE_AuLutInfo.x) / (_KSPVE_AuLutInfo.y - _KSPVE_AuLutInfo.x);
                if (u < 0.0 || u > 1.0) return 0.0;
                return tex2Dlod(_KSPVE_AuLut, float4(u, (row + 0.5) / _KSPVE_AuLutInfo.w, 0, 0)).rgb * LutScale;
            }

            // --- extinction by the air below -----------------------------------------------------------------
            float3 Transmittance(float3 o, float3 d, float t)
            {
                float sc = max(0.0, min(t, -dot(o, d)));
                float tau = 0.0;
                float H = _KSPVE_AuAtmo.w;
                [loop] for (int side = 0; side < 2; side++)
                {
                    float len = side == 0 ? sc : t - sc;
                    if (len < 1.0) continue;
                    [loop] for (int j = 0; j < 6; j++)
                    {
                        float u = (j + 0.5) / 6.0;
                        float s = side == 0 ? sc - len * u * u : sc + len * u * u;
                        float h = length(o + d * s) - gR;
                        tau += exp(-max(h, 0.0) / H) / H * (2.0 * len * u / 6.0);
                    }
                }
                return exp(-tau * _KSPVE_AuAtmo.rgb);
            }

            // --- shells: the parts of a ray between two spheres ----------------------------------------------
            int Shell(float3 o, float3 d, float rLow, float rHigh, float tMax, out float2 t0, out float2 t1)
            {
                t0 = 0.0; t1 = 0.0;
                float b = dot(o, d);
                float cl = length(cross(o, d));   // the line's distance from the centre; r^2 - cl^2 without the cancellation
                float discH = (rHigh - cl) * (rHigh + cl);
                if (discH <= 0.0) return 0;
                float sh = sqrt(discH);
                float a1 = max(-b - sh, 0.0), a2 = min(-b + sh, tMax);
                if (a2 <= a1) return 0;
                float discL = (rLow - cl) * (rLow + cl);
                int n = 0;
                if (discL > 0.0)
                {
                    float sl = sqrt(discL);
                    float b1 = -b - sl, b2 = -b + sl;
                    if (b1 > a1) { t0.x = a1; t1.x = min(a2, b1); n = 1; }
                    if (b2 < a2)
                    {
                        if (n == 0) { t0.x = max(a1, b2); t1.x = a2; }
                        else { t0.y = max(a1, b2); t1.y = a2; }
                        n++;
                    }
                }
                else { t0.x = a1; t1.x = a2; n = 1; }
                return n;
            }

            // --- the kinds -----------------------------------------------------------------------------------
            float WindowStorm(float nu)
            {
                if (_KSPVE_AuStorm2.x < 0.5) return 0.0;
                return SS(_KSPVE_AuStorm0.y - 0.3, _KSPVE_AuStorm0.y + 0.1, nu) * (1.0 - SS(_KSPVE_AuStorm0.z - 0.1, _KSPVE_AuStorm0.z + 0.3, nu));
            }

            float Fold(float y, float wb)
            {
                float a1 = 4.0 * Km + 8.0 * Km * gKp / 9.0;
                float breakup = _KSPVE_AuStorm1.y * _KSPVE_AuStorm0.w * wb;
                float s1 = 2.0 * Advected1(y / (150.0 * Km), 0.25 / 150.0, gTP, gSeed ^ 0xF01DU) - 1.0;
                float s2 = 2.0 * Advected1(y / (45.0 * Km), 0.3 / 45.0, gTP, gSeed ^ 0xF02DU) - 1.0;
                float s3 = 2.0 * Advected1(y / (16.0 * Km), 0.5 / 16.0, gTP, gSeed ^ 0xF03DU) - 1.0;
                float big = sin(2.0 * PI * (y / (300.0 * Km) - gTA) + 0.4);
                return a1 * s1 + 0.35 * a1 * s2 + (0.4 * Km + 4.0 * Km * breakup) * s3 + 30.0 * Km * breakup * big;
            }

            float DriftOffset() { return 25.0 * Km * sin(2.0 * PI * gTH); }

            float ArcCoordinate(Geo g, out float wb)
            {
                wb = WindowStorm(g.Nu);
                return g.D - Fold(g.Y, wb) - DriftOffset();
            }

            void ArcCell(int i, float wb, out bool exists, out float centre, out float width, out float amp)
            {
                uint k = (uint)(i * 4) ^ gSeed;
                float pa = 0.22 + 0.15 * min(gKp, 9.0) / 9.0 + 0.3 * _KSPVE_AuStorm1.y * wb;
                exists = Rand(k) < pa;
                centre = i + 0.5 + 0.30 * (Rand(k + 1) - 0.5);
                width = gLambda * (0.035 + 0.055 * Rand(k + 2));
                float a = Rand(k + 3);
                amp = 0.25 + 0.75 * a * sqrt(a);
            }

            float ArcZone(Geo g, float wb)
            {
                float bulge = _KSPVE_AuStorm1.z * wb * _KSPVE_AuStorm0.w;
                float growth = _KSPVE_AuStorm1.x * _KSPVE_AuStorm0.w;
                float dc = 0.2 * gHw + 0.5 * gHw * growth;
                float sp = gHw * (1.0 + 1.4 * bulge), se = gHw * 0.9;
                float x = (g.D - dc) / (g.D < dc ? sp : se);
                float env = exp(-0.5 * x * x);
                float a = (g.Nu + 0.2) / 0.95, b = (g.Nu - 1.4) / 0.7;
                float mlt = 0.2 + 0.8 * exp(-0.5 * a * a) + 0.3 * exp(-0.5 * b * b);
                return env * min(mlt, 1.0);
            }

            float AlongArc(float y, int cell, float wb, float rayScale)
            {
                uint s = gSeed ^ (uint)(cell * 7919 + 13);
                float seg = SS(0.12, 0.4, Value1(y / (350.0 * Km) + cell * 17.3, s));
                float ell = max(1.0 * Km * (0.6 + Rand(s)), rayScale);
                float n = Advected1(y / ell + cell * 11.7, 0.4 * Km / ell, gTP, s ^ 0x77U);
                float ray = 0.3 + 0.7 * n * n;
                float ripple = 1.0 + (0.15 + 0.4 * _KSPVE_AuStorm1.y * wb) * sin(2.0 * PI * (y / (12.0 * Km) - 100.0 * gTA));
                return seg * ray * ripple;
            }

            float BlackAmount(Geo g, float wb)
            {
                if (_KSPVE_AuKinds1.y <= 0.0) return 0.0;
                float zone = SS(-1.7, -0.9, g.Nu) * (1.0 - SS(1.5, 2.2, g.Nu));
                float u = (g.D - 1.5 * gHw - 0.7 * Fold(g.Y, wb)) / (45.0 * Km);
                int i = (int)floor(u);
                uint k = (uint)(i * 4) ^ gSeed ^ 0x3B1AU;
                if (Rand(k) > 0.45) return 0.0;
                float c = i + 0.5 + 0.3 * (Rand(k + 1) - 0.5);
                float w = 6.0 * Km / (45.0 * Km);
                float x = (u - c) / w;
                float along = SS(0.3, 0.6, Value1(g.Y / (200.0 * Km) + i * 5.1, k));
                return _KSPVE_AuKinds1.y * zone * exp(-0.5 * x * x) * along;
            }

            // --- discrete arcs: where a ray crosses a sheet, its integral ----------------------------------------
            void AddArcSheet(float3 o, float3 d, float tStar, float a, float b, float width, float dDdt, int cell, float amp, float hard,
                float pixelAngle, float vdark, float3 trans, inout float3 sum)
            {
                float sigmaT = width / max(dDdt, 1e-6);
                sigmaT = min(sigmaT, 60.0 * Km);
                float scale = sigmaT * 1.41421356;
                float boost = _KSPVE_AuKinds0.x * _KSPVE_AuView.y;
                [loop] for (int j = 0; j < 5; j++)
                {
                    float t = tStar + scale * GhX[j];
                    if (t < a || t > b) continue;
                    float3 p = o + d * t;
                    Geo g = GeoOf(p);
                    float h = length(p) - gR;
                    float wb;
                    ArcCoordinate(g, wb);
                    float zone = ArcZone(g, wb);
                    float along = AlongArc(g.Y, cell, wb, 2.0 * pixelAngle * t);
                    float black = 1.0 - 0.6 * BlackAmount(g, wb);
                    float ia = boost * amp * zone * along * black * GhW[j] * scale;
                    if (ia <= 0.0) continue;
                    float e = ExposureAt(p, vdark);
                    float3 c = (1.0 - hard) * RowColour(RowArc, h) + hard * RowColour(RowArcHard, h);
                    float3 soft = RowColour(RowSoftArc, h) * _KSPVE_AuKinds0.z;
                    sum += (c + soft) * trans * (ia * e);
                }
            }

            void AddSteve(float3 o, float3 d, float tStar, float a, float b, float dsdt, float pixelAngle, float vdark, float3 trans, inout float3 sum)
            {
                float boost = _KSPVE_AuKinds1.w * _KSPVE_AuView.y;
                [loop] for (int layer = 0; layer < 2; layer++)
                {
                    float w = layer == 0 ? 9.0 * Km : 3.0 * Km;
                    float sigmaT = min(w / max(dsdt, 1e-6), 60.0 * Km);
                    float scale = sigmaT * 1.41421356;
                    [loop] for (int j = 0; j < 5; j++)
                    {
                        float t = tStar + scale * GhX[j];
                        if (t < a || t > b) continue;
                        float3 p = o + d * t;
                        Geo g = GeoOf(p);
                        float h = length(p) - gR;
                        float window = SS(-2.4, -1.7, g.Nu) * (1.0 - SS(-0.5, 0.1, g.Nu));
                        float along = SS(0.25, 0.5, Value1(g.Y / (900.0 * Km), gSeed ^ 0x57E7E5U));
                        float inten = boost * window * (0.35 + 0.65 * along) * GhW[j] * scale;
                        float3 c;
                        if (layer == 0) c = RowColour(RowSteve, h) * inten;
                        else
                        {
                            float comb = pow(0.5 + 0.5 * cos(2.0 * PI * g.Y / (4.5 * Km) + 2.3 * Value1(g.Y / (200.0 * Km), gSeed)), 6.0);
                            float on = SS(0.45, 0.7, Value1(g.Y / (500.0 * Km), gSeed ^ 0xFE5CEU));
                            c = RowColour(RowArc, h) * (0.35 * inten * comb * on);
                        }
                        sum += c * trans * ExposureAt(p, vdark);
                    }
                }
            }

            float3 TraceArcs(float3 o, float3 d, float tMax, float jitter, float pixelAngle, float vdark)
            {
                float2 t0, t1;
                int n = Shell(o, d, gR + _KSPVE_AuSlabs0.x, gR + _KSPVE_AuSlabs0.y, tMax, t0, t1);
                float3 sum = 0.0;
                int crossings = 0;
                float hard = saturate(_KSPVE_AuKinds0.y);
                float nightColat = _KSPVE_AuNoon.w;
                float steve = _KSPVE_AuKinds1.w;
                bool arcsOn = _KSPVE_AuKinds0.x > 0.0;
                [loop] for (int iv = 0; iv < n; iv++)
                {
                    float a = iv == 0 ? t0.x : t0.y, b = iv == 0 ? t1.x : t1.y;
                    if (b - a < 1.0) continue;
                    float3 trans = Transmittance(o, d, a);
                    float dt = (b - a) / CoarseNodes;
                    float prevU = 0.0, prevT = 0.0, prevS = 0.0;
                    bool havePrev = false, prevInZone = false;
                    [loop] for (int k = 0; k <= CoarseNodes; k++)
                    {
                        float t = min(a + k * dt, b);
                        float3 p = o + d * t;
                        Geo g = GeoOf(p);
                        bool inZone = abs(g.D) < 4.5 * gHw + 2.0 * gHw * _KSPVE_AuStorm1.z;
                        float wb = 0.0, u = 0.0;
                        if (inZone && arcsOn) u = ArcCoordinate(g, wb) / gLambda;
                        float s = (g.ThF - (nightColat + 0.07)) * gR;
                        if (havePrev)
                        {
                            if (inZone && prevInZone && arcsOn)
                            {
                                float lo = min(u, prevU), hi = max(u, prevU);
                                int iEnd = (int)floor(hi + 0.7);
                                [loop] for (int i = (int)floor(lo - 0.7); i <= iEnd && crossings < MaxCrossings; i++)
                                {
                                    bool exists; float centre, width, amp;
                                    ArcCell(i, wb, exists, centre, width, amp);
                                    if (!exists || centre < lo || centre > hi || hi - lo < 1e-9) continue;
                                    float tStar = prevT + (centre - prevU) / (u - prevU) * (t - prevT);
                                    float dudt = (u - prevU) / (t - prevT);
                                    AddArcSheet(o, d, tStar, a, b, width, abs(dudt) * gLambda, i, amp, hard, pixelAngle, vdark, trans, sum);
                                    crossings++;
                                }
                            }
                            if (steve > 0.0 && s * prevS < 0.0)
                            {
                                float tStar = prevT + (0.0 - prevS) / (s - prevS) * (t - prevT);
                                float dsdt = (s - prevS) / (t - prevT);
                                AddSteve(o, d, tStar, a, b, abs(dsdt), pixelAngle, vdark, trans, sum);
                            }
                        }
                        prevU = u; prevT = t; prevS = s; prevInZone = inZone; havePrev = true;
                    }
                }
                return sum;
            }

            // --- the smooth kinds -----------------------------------------------------------------------------
            void PulsePatch(Geo g, out float mask, out float pulse)
            {
                float cell = 70.0 * Km;
                float u = g.Y / cell, v = g.D / cell;
                int iu = (int)floor(u), iv = (int)floor(v);
                uint k = ((uint)(iu * 73856093) ^ (uint)(iv * 19349663)) ^ gSeed ^ 0x9B15U;
                mask = 0.0; pulse = 0.0;
                if (Rand(k) > 0.65) return;
                float cx = (iu + 0.5 + 0.1 * (Rand(k + 1) - 0.5)) * cell, cy = (iv + 0.5 + 0.1 * (Rand(k + 2) - 0.5)) * cell;
                float radius = (20.0 + 12.0 * Rand(k + 3)) * Km;
                float warp = 0.65 + 0.7 * Value2(g.Y / (22.0 * Km), g.D / (22.0 * Km), k ^ 0x1234U);
                float dy = (g.Y - cx) / 1.6, dd = g.D - cy;
                float dist = sqrt(dy * dy + dd * dd);
                mask = 1.0 - SS(0.55 * radius * warp, radius * warp, dist);
                float freq = floor(30.0 + 270.0 * Rand(k + 4));
                float phase = Rand(k + 5) * 2.0 * PI;
                pulse = SS(-0.25, 0.25, sin(2.0 * PI * freq * gTA + phase));
            }

            float3 SmoothAt(float3 p, float h)
            {
                float3 c = 0.0;
                Geo g = GeoOf(p);
                float wb = WindowStorm(g.Nu);
                float nightColat = _KSPVE_AuNoon.w;
                float giantStrength = _KSPVE_AuKinds2.w;
                if (nightColat > 0.0 && giantStrength <= 0.0)
                {
                    float black = BlackAmount(g, wb);
                    if (_KSPVE_AuKinds0.w > 0.0)
                    {
                        float zd = (g.D - 1.4 * gHw) / (1.7 * gHw);
                        float dz = exp(-0.5 * zd * zd);
                        float wd = 0.3 + 0.7 * SS(-1.6, -0.7, g.Nu) * (1.0 - SS(1.6, 2.6, g.Nu));
                        float nz = 0.55 + 0.45 * Advected2(g.Y / (90.0 * Km), g.D / (90.0 * Km), 0.15 / 90.0, 0.0, gTP, gSeed ^ 0xD1FFU);
                        float pw = 0.0, pulse = 0.0, mask = 0.0;
                        if (_KSPVE_AuStorm1.w > 0.0)
                        {
                            float wp = SS(0.0, 0.7, g.Nu) * (1.0 - SS(2.0, 2.7, g.Nu));
                            pw = _KSPVE_AuStorm1.w * wp;
                            PulsePatch(g, mask, pulse);
                        }
                        float level = (1.0 - mask * pw) + mask * pw * pulse * 2.2;
                        float ia = _KSPVE_AuKinds0.w * dz * wd * nz * level * (1.0 - 0.9 * black);
                        if (ia > 0.0) c += RowColour(RowDiffuse, h) * ia;
                    }
                    if (_KSPVE_AuKinds1.x > 0.0)
                    {
                        float zp = (g.D - 2.5 * gHw) / (1.5 * gHw);
                        float pz = exp(-0.5 * zp * zp);
                        float wpr = 0.35 + 0.65 * SS(-2.2, -1.0, g.Nu) * (1.0 - SS(-0.2, 0.6, g.Nu));
                        float nz = 0.8 + 0.2 * Value2(g.Y / (200.0 * Km), g.D / (200.0 * Km), gSeed ^ 0x9407U);
                        c += RowColour(RowProton, h) * (_KSPVE_AuKinds1.x * pz * wpr * nz);
                    }
                    if (_KSPVE_AuKinds2.x > 0.0)
                    {
                        float cap = 1.0 - SS(g.ThO - 0.7 * _KSPVE_AuSun.w, g.ThO - 0.1 * _KSPVE_AuSun.w, g.ThF);
                        float nx = g.ThF * gR * -cos(g.Nu), ny = g.ThF * gR * sin(g.Nu);
                        float y0 = 0.045 * gR * sin(2.0 * PI * gTH + 1.7);
                        float yy = ny - y0 - 30.0 * Km * sin(2.0 * PI * nx / (1500.0 * Km));
                        float th = exp(-0.5 * (yy / (22.0 * Km)) * (yy / (22.0 * Km))) * (0.6 + 0.4 * Value1(nx / (300.0 * Km), gSeed ^ 0x7E7AU));
                        c += (RowColour(RowDiffuse, h) + RowColour(RowSoftArc, h) * 0.6) * (_KSPVE_AuKinds2.x * cap * th);
                    }
                }
                if (giantStrength > 0.0)
                {
                    float hwG = gHw;
                    float mn = exp(-0.5 * (g.D / hwG) * (g.D / hwG)) * (0.55 + 0.45 * Advected2(g.Y / (800.0 * Km), g.D / (800.0 * Km), 0.1 / 800.0, 0.0, gTP, gSeed ^ 0x61A7U))
                        * (0.65 + 0.35 * cos(g.Nu));
                    float spots = 0.0;
                    int spotCount = (int)(_KSPVE_AuSlabs1.z + 0.5);
                    [loop] for (int i = 0; i < spotCount; i++)
                    {
                        float4 sp = _KSPVE_AuSpots[i];
                        float dn = g.Nu - sp.y;
                        dn -= 2.0 * PI * floor((dn + PI) / (2.0 * PI));
                        float dth = (g.ThF - sp.x) * gR;
                        float along = dn * gR * max(sin(sp.x), 0.05);
                        float spotW = 0.012 * gR;
                        float spot = exp(-0.5 * (dth * dth + along * along) / (spotW * spotW));
                        float tail = 0.0;
                        if (dn > 0.0 && dn < sp.w) tail = exp(-dn / (0.4 * sp.w)) * 0.3 * exp(-0.5 * (dth / (0.02 * gR)) * (dth / (0.02 * gR)));
                        spots += sp.z * (spot + tail);
                    }
                    c += RowColour(RowGiant, h) * (giantStrength * mn + spots);
                }
                if (_KSPVE_AuKinds2.y > 0.0)
                {
                    float3 q = g.Dir * (gR / (220.0 * Km));
                    float n = Advected3(q, float3(0.1 / 220.0, 0.0, 0.0), gTP, gSeed ^ 0x6104U);
                    float patch = 0.04 + 0.96 * SS(0.55, 0.85, n);
                    c += RowColour(RowGlobal, h) * (_KSPVE_AuKinds2.y * patch);
                }
                if (_KSPVE_AuKinds2.z > 0.0)
                {
                    float sumRegion = 0.0;
                    int crustalCount = (int)(_KSPVE_AuSlabs1.w + 0.5);
                    float strengths[4] = { _KSPVE_AuCrust2.x, _KSPVE_AuCrust2.y, _KSPVE_AuCrust2.z, _KSPVE_AuCrust2.w };
                    [loop] for (int j = 0; j < crustalCount; j++)
                    {
                        float4 s = _KSPVE_AuCrust[j];
                        float ang = acos(clamp(dot(g.Dir, s.xyz), -1.0, 1.0));
                        float dist = ang * gR;
                        float gzd = dist / max(0.45 * s.w * gR, 1.0);
                        float gz = exp(-0.5 * gzd * gzd);
                        float3 q = g.Dir * (gR / (25.0 * Km));
                        float ridge = 1.0 - abs(2.0 * Value3(q.x, q.y, q.z, gSeed ^ 0xC857U) - 1.0);
                        float flick = 1.0 + 0.3 * sin(2.0 * PI * (40.0 * gTA) + j * 2.0);
                        sumRegion += strengths[j] * gz * (0.2 + 0.8 * ridge * ridge * ridge) * flick;
                    }
                    c += RowColour(RowGlobal, h) * (_KSPVE_AuKinds2.z * sumRegion);
                }
                return c;
            }

            float3 TraceSmooth(float3 o, float3 d, float tMax, float jitter, float pixelAngle, float vdark)
            {
                float2 t0, t1;
                int n = Shell(o, d, gR + _KSPVE_AuSlabs0.z, gR + _KSPVE_AuSlabs0.w, tMax, t0, t1);
                float3 sum = 0.0;
                float total = 0.0;
                if (n > 0) total += t1.x - t0.x;
                if (n > 1) total += t1.y - t0.y;
                if (total < 1.0) return sum;
                [loop] for (int iv = 0; iv < n; iv++)
                {
                    float a = iv == 0 ? t0.x : t0.y, b = iv == 0 ? t1.x : t1.y;
                    int steps = max(4, (int)round(SmoothSteps * (b - a) / total));
                    float dt = (b - a) / steps;
                    float3 trans = Transmittance(o, d, a);
                    [loop] for (int k = 0; k < steps; k++)
                    {
                        float t = a + (k + jitter) * dt;
                        float3 p = o + d * t;
                        float h = length(p) - gR;
                        float3 c = SmoothAt(p, h);
                        sum += c * trans * (dt * ExposureAt(p, vdark) * _KSPVE_AuView.y);
                    }
                }
                return sum;
            }

            float3 TraceSar(float3 o, float3 d, float tMax, float jitter, float vdark)
            {
                float2 t0, t1;
                int n = Shell(o, d, gR + _KSPVE_AuSlabs1.x, gR + _KSPVE_AuSlabs1.y, tMax, t0, t1);
                float3 sum = 0.0;
                float total = 0.0;
                if (n > 0) total += t1.x - t0.x;
                if (n > 1) total += t1.y - t0.y;
                if (total < 1.0) return sum;
                [loop] for (int iv = 0; iv < n; iv++)
                {
                    float a = iv == 0 ? t0.x : t0.y, b = iv == 0 ? t1.x : t1.y;
                    int steps = max(3, (int)round(SarSteps * (b - a) / total));
                    float dt = (b - a) / steps;
                    float3 trans = Transmittance(o, d, a);
                    [loop] for (int k = 0; k < steps; k++)
                    {
                        float t = a + (k + jitter) * dt;
                        float3 p = o + d * t;
                        Geo g = GeoOf(p);
                        float thSar = _KSPVE_AuNoon.w + 0.12;
                        float x = (g.ThF - thSar) * gR / (280.0 * Km);
                        float w = SS(-2.4, -1.2, g.Nu) * (1.0 - SS(1.4, 2.4, g.Nu));
                        float nz = 0.7 + 0.3 * Value1(g.Y / (400.0 * Km), gSeed ^ 0x5A2U);
                        float ia = _KSPVE_AuKinds1.z * exp(-0.5 * x * x) * w * nz;
                        if (ia <= 0.0) continue;
                        sum += RowColour(RowSar, length(p) - gR) * trans * (ia * dt * ExposureAt(p, vdark) * _KSPVE_AuView.y);
                    }
                }
                return sum;
            }

            float GlowStructure(float3 u, int layer)
            {
                float3 q = u * (gR / (200.0 * Km));
                float patch = 1.0 + 0.6 * (Value3(q.x, q.y, q.z, gSeed ^ (0x61A0U + (uint)layer)) - 0.5);
                float3 q2 = u * (gR / (350.0 * Km));
                float env = SS(0.35, 0.7, Value3(q2.x, q2.y, q2.z, gSeed ^ 0x6A7EU));
                float3 d1 = normalize(float3(0.3, 0.2, 0.93)), d2 = normalize(float3(-0.8, 0.1, 0.59));
                float w1 = sin(2.0 * PI * (asin(clamp(dot(u, d1), -1.0, 1.0)) * gR / (25.0 * Km) - 1.0 * gTA));
                float w2 = sin(2.0 * PI * (asin(clamp(dot(u, d2), -1.0, 1.0)) * gR / (45.0 * Km) - 2.0 * gTA) + 0.8);
                return max(0.0, patch * (1.0 + env * (0.18 * w1 + 0.12 * w2)));
            }

            float3 TraceGlow(float3 o, float3 d, float tMax, float jitter, float vdark)
            {
                float3 sum = 0.0;
                float strength[4] = { _KSPVE_AuGlow.x, _KSPVE_AuGlow.y, _KSPVE_AuGlow.z, _KSPVE_AuGlow.w };
                float lows[4] = { _KSPVE_AuGlowLow.x, _KSPVE_AuGlowLow.y, _KSPVE_AuGlowLow.z, _KSPVE_AuGlowLow.w };
                float highs[4] = { _KSPVE_AuGlowHigh.x, _KSPVE_AuGlowHigh.y, _KSPVE_AuGlowHigh.z, _KSPVE_AuGlowHigh.w };
                [loop] for (int layer = 0; layer < 4; layer++)
                {
                    if (strength[layer] <= 0.0 || highs[layer] <= lows[layer]) continue;
                    int row = RowGlowGreen + layer;
                    float2 t0, t1;
                    int n = Shell(o, d, gR + lows[layer], gR + highs[layer], tMax, t0, t1);
                    float total = 0.0;
                    if (n > 0) total += t1.x - t0.x;
                    if (n > 1) total += t1.y - t0.y;
                    if (total < 1.0) continue;
                    [loop] for (int iv = 0; iv < n; iv++)
                    {
                        float a = iv == 0 ? t0.x : t0.y, b = iv == 0 ? t1.x : t1.y;
                        int steps = max(3, (int)round(GlowSteps * (b - a) / total));
                        float dt = (b - a) / steps;
                        float3 trans = Transmittance(o, d, a);
                        [loop] for (int k = 0; k < steps; k++)
                        {
                            float t = a + (k + jitter) * dt;
                            float3 p = o + d * t;
                            float lp = max(length(p), 1.0);
                            float3 u = p / lp;
                            float h = lp - gR;
                            float3 c = RowColour(row, h);
                            float w = GlowStructure(u, layer);
                            sum += c * trans * (strength[layer] / 1000.0 * w * dt * ExposureAt(p, vdark) * _KSPVE_AuView.z);
                        }
                    }
                }
                return sum;
            }

            float3 Trace(float3 o, float3 d, float tMax, float jitter, float pixelAngle)
            {
                // The solid body stops the ray.
                float cl = length(cross(o, d));
                float disc0 = (gR - cl) * (gR + cl);
                if (disc0 > 0.0)
                {
                    float th = -dot(o, d) - sqrt(disc0);
                    if (th > 0.0) tMax = min(tMax, th);
                }
                float vdark = ViewerDark(o);
                float3 sum = 0.0;
                bool hasField = _KSPVE_AuNoon.w > 0.0;
                if (_KSPVE_AuSlabs0.y > _KSPVE_AuSlabs0.x && hasField && (_KSPVE_AuKinds0.x > 0.0 || _KSPVE_AuKinds1.w > 0.0)) sum += TraceArcs(o, d, tMax, jitter, pixelAngle, vdark);
                bool smoothAny = _KSPVE_AuKinds0.w > 0.0 || _KSPVE_AuKinds1.x > 0.0 || _KSPVE_AuKinds2.x > 0.0 || _KSPVE_AuKinds2.y > 0.0 || _KSPVE_AuKinds2.z > 0.0 || _KSPVE_AuKinds2.w > 0.0;
                if (_KSPVE_AuSlabs0.w > _KSPVE_AuSlabs0.z && smoothAny) sum += TraceSmooth(o, d, tMax, jitter, pixelAngle, vdark);
                if (_KSPVE_AuSlabs1.y > _KSPVE_AuSlabs1.x && hasField && _KSPVE_AuKinds1.z > 0.0) sum += TraceSar(o, d, tMax, jitter, vdark);
                if (_KSPVE_AuGlow.x + _KSPVE_AuGlow.y + _KSPVE_AuGlow.z + _KSPVE_AuGlow.w > 0.0) sum += TraceGlow(o, d, tMax, jitter, vdark);
                return sum;
            }

            void Setup()
            {
                gR = _KSPVE_Au0.x; gC = _KSPVE_Au0.yzw; gAxis = _KSPVE_AuAxis.xyz; gKp = _KSPVE_AuAxis.w;
                gNoon = _KSPVE_AuNoon.xyz; gDawn = _KSPVE_AuDawn.xyz; gSun = _KSPVE_AuSun.xyz;
                gHw = max(_KSPVE_AuSun.w, 0.01) * gR;
                gTA = _KSPVE_AuTime.x; gTP = _KSPVE_AuTime.y; gTH = _KSPVE_AuTime.z;
                gSeed = Hash((uint)(_KSPVE_AuTime.w + 0.5) ^ 0xA5A5U);
                gLambda = 60.0 * Km * (1.0 - 0.4 * min(gKp, 9.0) / 9.0);
            }

            // Pass 0: the march. Interleaved gradient noise offsets the samples, and moves each frame (golden ratio).
            float4 fragMarch(v2f i) : SV_Target
            {
                Setup();
                float2 ndc = i.ndc;
                float3 forward = _KSPVE_AuForward.xyz;
                float3 d = normalize(forward + _KSPVE_AuRight.xyz * (ndc.x * _KSPVE_AuRight.w) + _KSPVE_AuUp.xyz * (ndc.y * _KSPVE_AuUp.w));
                float3 o = _KSPVE_AuCam.xyz;
                float tMax = 1e12;
                if (_KSPVE_AuForward.w > 0.5)
                {
                    float2 uv = ndc * 0.5 + 0.5;
                    float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                    // Reversed-Z on D3D11: 0 is the far plane (the sky); anything nearer than it is a surface the rays must stop at.
                    if (depth > 1e-7) tMax = LinearEyeDepth(depth) / max(dot(d, forward), 1e-4);
                }
                float ign = frac(52.9829189 * frac(dot(i.pos.xy, float2(0.06711056, 0.00583715))));
                float jitter = frac(ign + 0.61803398 * _KSPVE_AuView.w);
                return float4(Trace(o, d, tMax, jitter, _KSPVE_AuCam.w), 1.0);
            }

            // Pass 1: exponential blend with the history, reprojected by direction (the aurora is far away).
            float4 fragResolve(v2f i) : SV_Target
            {
                float2 ndc = i.ndc;
                float2 cur = float2(ndc.x * 0.5 + 0.5, 0.5 - 0.5 * ndc.y * _ProjectionParams.x);
                float3 now = tex2D(_KSPVE_AuLow, cur).rgb;
                float weight = _KSPVE_AuPrevRight.w;
                if (weight <= 0.0) return float4(now, 1.0);
                float3 d = normalize(_KSPVE_AuForward.xyz + _KSPVE_AuRight.xyz * (ndc.x * _KSPVE_AuRight.w) + _KSPVE_AuUp.xyz * (ndc.y * _KSPVE_AuUp.w));
                float depthAlong = dot(d, _KSPVE_AuPrevForward.xyz);
                float2 prevNdc = float2(dot(d, _KSPVE_AuPrevRight.xyz) / (depthAlong * _KSPVE_AuRight.w), dot(d, _KSPVE_AuPrevUp.xyz) / (depthAlong * _KSPVE_AuUp.w));
                if (depthAlong <= 0.0 || abs(prevNdc.x) >= 1.0 || abs(prevNdc.y) >= 1.0) return float4(now, 1.0);
                float2 prevUv = float2(prevNdc.x * 0.5 + 0.5, 0.5 - 0.5 * prevNdc.y * _ProjectionParams.x);
                float3 history = tex2D(_KSPVE_AuHistory, prevUv).rgb;
                // Where the new march is far from the history (a sparkle, or a real change) the history gives way.
                float3 diff = abs(history - now) / (history + now + 1e-6);
                float keep = weight * (1.0 - saturate(max(diff.r, max(diff.g, diff.b)) * 1.0));
                return float4(lerp(now, history, keep), 1.0);
            }

            // Pass 2: onto the camera target (additive; the buffer was written with the target's clip-space y).
            float4 fragComposite(v2f i) : SV_Target
            {
                float2 uv = float2(i.ndc.x * 0.5 + 0.5, 0.5 - 0.5 * i.ndc.y * _ProjectionParams.x);
                return float4(tex2D(_KSPVE_AuLow, uv).rgb, 0.0);
            }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0: march
        {
            Blend Off
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragMarch
            ENDCG
        }

        Pass // 1: temporal blend
        {
            Blend Off
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragResolve
            ENDCG
        }

        Pass // 2: add to the target
        {
            Blend One One
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }
    }
    Fallback Off
}
