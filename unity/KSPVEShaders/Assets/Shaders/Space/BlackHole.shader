Shader "Hidden/KSPVE/Space/BlackHole"
{
    // Relativistic black hole with a thick, puffy, self-shadowed volumetric accretion disk that spins as a whole,
    // with sparks and debris riding in it (catalog #80, #81). A tidal disruption event, when a save runs one, is drawn in
    // the same march (TdeSample). Pass 2 draws the other kinds of object (SpaceObjects): neutron stars, dusty disks,
    // asteroid belts, nebulae and comets.
    // Pass 0 ray-marches the hole over a screen rectangle that covers its bounding sphere, premultiplied, either
    // straight onto Camera ScaledSpace after transparents or into a reduced-resolution buffer; pass 1 composites
    // that buffer. Rays are integrated in units of the horizon radius r_s (so M = 1/2) with the photon-orbit law
    // x'' = -3 M h^2 x / |x|^5 = -1.5 h^2 x / |x|^5 (h = |x cross v|), mirroring KSPVE.Pure.Space.Relativity.CurvedRay.
    // tools/BlackHolePreview is a line-for-line CPU port used to tune this without launching KSP.
    CGINCLUDE
            #include "UnityCG.cginc"

            sampler2D_float _CameraDepthTexture;
            sampler2D _KSPVE_BHBuffer;    // reduced-resolution march result (pass 1)
            float4 _KSPVE_BHRect;         // screen rectangle to draw, NDC with y up: xmin, ymin, xmax, ymax
            float4 _KSPVE_BHCenter;       // xyz: hole centre relative to the camera (scaled units); w: horizon radius (scaled units)
            float4 _KSPVE_BHNormal;       // xyz: disk normal (world); w: spin sign (+1 prograde about the normal, -1 retrograde)
            float4 _KSPVE_BHJetAxis;      // xyz: jet axis (world): the normal, or swept round it like a pulsar's beams
            float4 _KSPVE_BHDisk;         // inner radius, outer radius (horizon radii), half-thickness (fraction of r), turbulence 0..1
            float4 _KSPVE_BHLook;         // peak temperature K, brightness, disk rotation angle (rad, signed), time (s)
            float4 _KSPVE_BHJet;          // jets: strength (0 = off), length (horizon radii), width, gas speed (fraction of c)
            float4 _KSPVE_BHJet2;         // jets: spiral (0..1), swirl twist (rad per unit z^0.45), flow (z^0.45 per s), absorption
            float4 _KSPVE_BHJetKnots;     // jets breaking into blobs: x amount 0..1, y where (fraction of the length), z spacing (fraction of z^0.45 along it), w growth
            float4 _KSPVE_BHExtra;        // colour saturation, glow, photon ring, particles 0..1
            float4 _KSPVE_BHRay;          // tan(fovX/2), tan(fovY/2), max steps, bounding radius (horizon radii)
            float4 _KSPVE_BHOptions;      // test scene depth (0/1), radians per target pixel, debris 0..1, pulsar (0/1)
            float4 _KSPVE_BHRight;        // camera right (world)
            float4 _KSPVE_BHUp;           // camera up (world)
            float4 _KSPVE_BHForward;      // camera forward (world)
            float4 _KSPVE_BHForm;         // x: how much disk there is 0..1 (a tidal disruption builds it), y: its brightness multiplier,
                                          // z: spiral shocks while it forms 0..1, w: glow of the jet heads punching out 0..1
            #define BH_OCCLUDERS 8
            float4 _KSPVE_BHOccluders[BH_OCCLUDERS]; // bodies that may be in front: xyz centre relative to the eye, w radius (horizon radii; 0 = none)
            float4 _KSPVE_BHStreak;       // x: how much of the streak look this view gets 0..1 (BlackHoleSettings.StreakView); y: outerGlow
            float4 _KSPVE_BHMask;         // a forming disk fills only this band (horizon radii): x inner, y outer, z edge width (0 = all of it)
            float4 _KSPVE_BHTint;         // the disk's parts: x colours by hand 0..1 (diskTint), y inner brightness, z outer brightness
            float4 _KSPVE_BHTintInner;    // rgb: the inner disk's colour (and the glow's) by hand
            float4 _KSPVE_BHTintOuter;    // rgb: the outer disk's colour by hand
            float4 _KSPVE_BHJetColor;     // rgb: the jets' colour (and a neutron star's beams')
            float4 _KSPVE_BHRingColor;    // rgb: the photon ring's colour
            float4 _KSPVE_BHAdvanced;     // the disk, advanced: x density, y opacity, z emission, w lumps per horizon radius
            float4 _KSPVE_BHAdvanced2;    // x self-shadowing, y light from the inner disk, z streak winding, w streak stretch
            float4 _KSPVE_BHAdvanced3;    // x darkness of the lanes between streaks, y spacing of sparks and debris

            // The other kinds of object (KSPVE.Pure.Space.SpaceObjects), drawn by pass 2 in their own units (the w of
            // _KSPVE_BHCenter), round the body at _KSPVE_BHCenter. They share the hole's uniforms: _KSPVE_BHDisk (inner, outer,
            // half-thickness), _KSPVE_BHNormal (the disk's normal), _KSPVE_BHLook (brightness, rotation, time), _KSPVE_BHJetAxis
            // (a neutron star's magnetic axis), _KSPVE_BHRay (steps, bound) and the occluders.
            float4 _KSPVE_ObjKind;        // x: kind (SpaceObjectKind), y: the star's radius (rays stop there), z: its temperature (K), w: a neutron star's surface temperature (K)
            float4 _KSPVE_ObjShape;       // dusty disks: x flaring, y gaps (in _KSPVE_ObjGaps)
            float4 _KSPVE_ObjGaps[16];    // dusty disks' gaps (DiskGaps): x radius, y width (units), z depth 0..1
            float4 _KSPVE_ObjOuterDust;   // rgb: the dust's colour at the outer edge (dustColor at the inner)
            float4 _KSPVE_ObjRockColor;   // rgb: the rocks' colour
            float4 _KSPVE_ObjDust;        // dust, light falloff exponent, star glow (fraction of the outer radius), rock spacing (units)
            float4 _KSPVE_ObjRocks;       // rocks 0..1, rock size, rock albedo, w: the widest a rock may look (radius over distance; 0 = no limit)
            float4 _KSPVE_ObjCloud;       // nebula: dust lanes, cloud scale, cloud warp, filaments
            float4 _KSPVE_ObjNeutron;     // neutron star: field lines, accretion columns, beams, beam length
            float4 _KSPVE_ObjComet;       // comet: activity, coma size, ion tail length, dust tail length
            float4 _KSPVE_ObjComet2;      // comet: dust tail curve, dust tail width
            float4 _KSPVE_ObjColorA;      // rgb: nebula gas, comet ion tail, neutron star field lines
            float4 _KSPVE_ObjColorB;      // rgb: nebula's other gas, comet dust tail
            float4 _KSPVE_ObjDustColor;   // rgb: dust
            float4 _KSPVE_ObjComaColor;   // rgb: comet coma
            float4 _KSPVE_ObjSun;         // xyz: comet: towards its star
            float4 _KSPVE_ObjVel;         // xyz: comet: its direction of motion
            float4 _KSPVE_ObjExtra;       // x: nebula: how much of its gas lies in a hollow shell (a planetary nebula) 0..1; y: comet: dust tail striae;
                                          // z: a black hole on the same body: its horizon in these units (rays bend round it and end there), 0 = none
            float4 _KSPVE_ObjDisk2;       // dusty disks: x density falloff exponent, y edge softness, z clumpiness, w clump size
            float4 _KSPVE_ObjDisk3;       // dusty disks: x scattering asymmetry g, y rim glow, z rim temperature (K), w rocks' vertical spread
            float4 _KSPVE_ObjSpiral;      // dusty disks: x spiral arms, y their contrast 0..1, z winding; w: rocks' size spread
            float4 _KSPVE_ObjCloud2;      // nebula: x stretch along the normal, y raggedness, z emptiness between clouds
            float4 _KSPVE_ObjComet3;      // comet: brightness of x coma, y ion tail, z dust tail, w false nucleus
            float4 _KSPVE_ObjField;       // field lines: x loops round the axis, y shells, z shell ratio, w width (fraction of distance)
            float4 _KSPVE_ObjNeutron2;    // neutron star: x column width (fraction of inner radius), y beam widening, z hot spots
            float4 _KSPVE_ObjGalaxy;      // galaxy: x bulge size, y bulge brightness, z bar length (fractions of its radius), w star-forming knots

            // A tidal disruption event (KSPVE.Pure.Space.Tde.TidalDisruption, one TdeFrame), in horizon radii around the
            // hole. tools/BlackHolePreview/TdeRender.cs is the line-for-line CPU copy of this part.
            #define TDE_VERTS 128
            #define TDE_CHUNK 8
            #define TDE_CHUNKS 16
            #define TDE_BINS 32
            float4 _KSPVE_Tde;                 // x: on (0/1), y: stream vertices, z: chunks, w: radius of the disk & jets region
            float4 _KSPVE_TdeNormal;           // xyz: the star's orbit normal
            float4 _KSPVE_TdeStar;             // xyz: star centre, w: its presence 0..1 (0 = none)
            float4 _KSPVE_TdeStarAxis;         // xyz: its long (tidal) axis, w: long semi-axis
            float4 _KSPVE_TdeStarVel;          // xyz: its velocity (fraction of c), w: short semi-axis
            float4 _KSPVE_TdeStarLook;         // x: surface temperature (K), y: tidal tails 0..1, z: display glow
            float4 _KSPVE_TdeVerts[TDE_VERTS];     // stream: xyz position, w: half-width in the orbit plane
            float4 _KSPVE_TdeVertLook[TDE_VERTS];  // x: material coordinate, y: opacity per horizon radius, z: temperature (K), w: half-thickness
            float4 _KSPVE_TdeVertVel[TDE_VERTS];   // xyz: velocity (fraction of c), w: brightness 0..1 (negative: the stream breaks before it)
            float4 _KSPVE_TdeChunks[TDE_CHUNKS];   // bounding spheres of 8 segments: xyz centre, w radius
            float4 _KSPVE_TdeShock;            // xyz: self-intersection shock, w: its luminosity (Eddington units), 0 = none
            float4 _KSPVE_TdeShockLook;        // xyz: which way the shocked gas sprays, w: radius
            float4 _KSPVE_TdeEnv;              // outflow: x inner radius, y ln(outer / inner), z polar funnel density, w on (0/1)
            float _KSPVE_TdeEnvK[TDE_BINS];    // its opacity per horizon radius, log-spaced in radius
            float _KSPVE_TdeEnvT[TDE_BINS];    // its temperature (K)
            float4 _KSPVE_TdeLight;            // x: luminosity (Eddington units), y: display glow of the cooled stream, z: clump growth, w: least pixel coverage of the stream
            float4 _KSPVE_TdeFlash;            // xyz: the periapsis flash (shock breakout), w: its strength, 0 = none
            float4 _KSPVE_TdeFlashLook;        // x: the flash's radius

            // The disk's advanced settings (BlackHoleSettings; the look was tuned at the values in brackets).
            #define Absorb (_KSPVE_BHAdvanced.y)           // extinction per unit density along the view [0.35] (low: a bit translucent)
            #define ShadowAbsorb (_KSPVE_BHAdvanced2.x)    // extinction towards the inner disk [0.9] (keeps the puffy self-shadowing)
            #define LightStrength (_KSPVE_BHAdvanced2.y)   // light from the inner disk that lights the bulges [3]
            #define DensityScale (_KSPVE_BHAdvanced.x)     // disk density (divided by radius) [45]
            #define CloudScale (_KSPVE_BHAdvanced.w)       // puffy lumps per horizon radius [0.45]
            #define EmissionStrength (_KSPVE_BHAdvanced.z) // the plasma's own thermal glow [3.6]
            #define ParticleCell (_KSPVE_BHAdvanced3.y)  // horizon radii per particle cell [0.5]
            static const float JetBase = 12.0;

            // The disk seen from above (diskStreaks): its gas drawn out along its orbit into long arcs winding into a
            // trailing spiral, finely striated, with dark lanes between. tools/BlackHolePreview tunes these.
            #define StreakStretch (_KSPVE_BHAdvanced2.w) // arcs this many times longer along the orbit than across [30]
            static const float StreakRing = 2.0;        // noise circle size: how many arcs go round
            static const float StreakRadial = 1.6;      // bands across the disk, per horizon radius (times CloudScale)
            // Beyond this radius a disk's clumps, and the steps through a disk this big, grow with the radius
            // (BlackHoleSettings.DetailRadius), so a disk of any size looks alike at every scale.
            static const float DetailRadius = 20.0;
            #define StreakWinding (_KSPVE_BHAdvanced2.z) // spiral winding per e-fold of radius [5]
            static const float StreakFine = 6.0;        // fine striations, this much finer than the bands
            static const float StreakFineWeight = 0.85;
            static const float StreakArcLo = 0.25, StreakArcHi = 0.6;
            #define StreakGap (_KSPVE_BHAdvanced3.x)     // how dark the lanes between arcs are [0.85]
            static const float StreakHeat = 0.8;        // the arcs run hotter (whiter)
            static const float StreakExposure = 0.35;   // face on the whole glowing column is in view: toned down so the arcs read
            static const float StreakSaturation = 2.8;  // softer colour from above: white-blue arcs rather than cyan
            static const float StreakSoftRim = 0.8;     // the inner rim fades in softly round the dark centre
            static const float StreakGlowDim = 0.8, StreakRingDim = 1.0;   // glow and photon ring fainter from above          // spiral jets: radius ~ (z + JetBase)^0.55, thick from the base (BlackHoleSettings.JetBase)

            static const float2 Corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 ndc : TEXCOORD0; // screen position, NDC with y up
            };

            // A quad over _KSPVE_BHRect. When the camera renders into a texture its projection is flipped
            // (_ProjectionParams.x = -1), so the clip-space y follows it.
            v2f vert(uint id : SV_VertexID)
            {
                v2f o;
                float2 ndc = lerp(_KSPVE_BHRect.xy, _KSPVE_BHRect.zw, Corners[id]);
                o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, UNITY_NEAR_CLIP_VALUE, 1.0);
                o.ndc = ndc;
                return o;
            }

            // --- noise ----------------------------------------------------------------------------
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0, amplitude = 0.5;
                [unroll] for (int k = 0; k < 3; k++)
                {
                    sum += amplitude * Noise3(p);
                    p = p * 2.03 + 1.7;
                    amplitude *= 0.5;
                }
                return sum / 0.875;
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

            // Chroma boost around luminance (blackbody colours are pale; the look wants them vivid).
            float3 SaturateBy(float3 c, float s)
            {
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                return max(l + (c - l) * s, 0.0);
            }

            float3 Saturate(float3 c) { return SaturateBy(c, _KSPVE_BHExtra.x); }

            float3 LinearToSrgb(float3 c)
            {
                c = max(c, 0);
                return lerp(1.055 * pow(c, 1.0 / 2.4) - 0.055, c * 12.92, step(c, 0.0031308));
            }

            // Shakura-Sunyaev thin-disk temperature, peak at r = 49/36 r_in (AccretionDisk.cs).
            float DiskTemperature(float r, float inner, float peak)
            {
                if (r <= inner) return 0.0;
                float rPeak = inner * (49.0 / 36.0);
                return peak * pow(r / rPeak, -0.75) * pow(saturate((1.0 - sqrt(inner / r)) * 7.0), 0.25);
            }

            // --- the disk -------------------------------------------------------------------------
            // Disk frame: t, b span the disk plane. The disk turns as a whole by _KSPVE_BHLook.z (the outer disk is
            // sped up to the inner edge's rate, so nothing shears into swirls).
            void DiskAxes(out float3 n, out float3 t, out float3 b)
            {
                n = _KSPVE_BHNormal.xyz;
                t = normalize(cross(n, abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
                b = cross(n, t);
            }

            // Jet frame: the same as the disk's unless the jets sweep round the spin axis.
            void JetAxes(out float3 n, out float3 t, out float3 b)
            {
                n = _KSPVE_BHJetAxis.xyz;
                t = normalize(cross(n, abs(n.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0)));
                b = cross(n, t);
            }

            // A point in the disk's rotating frame (horizon radii), where the lumps, particles and debris ride.
            float3 ToDiskFrame(float3 x, out float r)
            {
                float3 n, t, b;
                DiskAxes(n, t, b);
                float height = dot(x, n);
                float3 q = x - height * n;
                r = length(q);
                float angle = atan2(dot(q, b), dot(q, t)) - _KSPVE_BHLook.z;
                return float3(r * cos(angle), r * sin(angle), height);
            }

            // Shape of the disk at x, without the hot clumps; the light march uses this alone.
            // pw: the point's warped position in the rotating frame, for the hot clumps; ps: its place in the streak pattern.
            // The disk's cloud noise at scale level (2^-level of the inner disk's clump frequency), with its warped position.
            void Clouds(float3 pd, float level, out float3 pw, out float warp, out float lumps, out float billows)
            {
                float3 pc = pd * (CloudScale * exp2(-level)) + float3(17.3, 5.1, 9.7) * level;
                float3 w = float3(Noise3(pc * 0.5 + 11.3), Noise3(pc * 0.5 + 27.1), Noise3(pc * 0.5 + 41.7)) - 0.5;
                pw = pc + w * 0.5;
                warp = (Noise3(pc * 0.3 + 3.3) - 0.5) * 0.8;
                lumps = Fbm(pw);
                billows = 1.0 - abs(2.0 * Noise3(pw * 2.7 + 5.1) - 1.0);
            }

            // The radius the streak pattern runs on: the radius itself, then logarithmic beyond DetailRadius.
            float StreakRadius(float r) { return r <= DetailRadius ? r : DetailRadius * (1.0 + log(r / DetailRadius)); }

            // pw1, cloudBlend: the next scale's warped position and how much of it, beyond DetailRadius.
            float DiskShapeStreaks(float3 x, out float r, out float3 pw, out float3 ps, out float3 pw1, out float cloudBlend)
            {
                pw = 0.0;
                ps = 0.0;
                pw1 = 0.0;
                cloudBlend = 0.0;
                float3 pd = ToDiskFrame(x, r);
                float height = pd.z;
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                if (r < inner * 0.8 || r > outer * 1.1) return 0.0;
                float ht = _KSPVE_BHDisk.z * r;
                if (abs(height) > 3.0 * ht) return 0.0;
                float outerFade = 1.0 - smoothstep(outer * 0.35, outer, r); // outer disk thins into translucent wisps
                float face = _KSPVE_BHStreak.x, cloud = 1.0 - face;
                // Seen from above the inner rim fades in softly, so the dark centre has a soft edge.
                float radial = smoothstep(inner * 0.85, inner * (1.1 + StreakSoftRim * face), r) * outerFade * outerFade;
                if (_KSPVE_BHMask.z > 0.0)
                    radial *= smoothstep(_KSPVE_BHMask.x - _KSPVE_BHMask.z, _KSPVE_BHMask.x + _KSPVE_BHMask.z, r)
                        * (1.0 - smoothstep(_KSPVE_BHMask.y - _KSPVE_BHMask.z, _KSPVE_BHMask.y + _KSPVE_BHMask.z, r));
                if (radial <= 0.0) return 0.0;
                float turbulence = _KSPVE_BHDisk.w;

                // Puffy, irregular surface: big rounded lumps with smaller billows on them bulge the disk's half-height.
                float warp = 0.0, lumps = 0.0, billows = 0.0;
                if (cloud > 0.0)
                {
                    // True 3D position in the rotating frame. Beyond DetailRadius the clumps grow with the radius: the noise
                    // is taken at the two power-of-two scales around the wanted one and blended, so it stays seamless.
                    float level = log2(max(1.0, r / DetailRadius));
                    float k0 = floor(level);
                    float blend = level - k0;
                    float w0, l0, b0;
                    Clouds(pd, k0, pw, w0, l0, b0);
                    if (blend > 0.001)
                    {
                        float w1, l1, b1;
                        Clouds(pd, k0 + 1.0, pw1, w1, l1, b1);
                        w0 = lerp(w0, w1, blend);
                        l0 = lerp(l0, l1, blend);
                        b0 = lerp(b0, b1, blend);
                        cloudBlend = blend;
                    }
                    warp = w0 * cloud;
                    lumps = l0 * cloud;
                    billows = b0 * cloud;
                }
                if (face > 0.0)
                {
                    // Seen from above, the gas is sheared along its orbit (the inner disk orbits faster): its clumps are
                    // drawn out into long arcs winding into a trailing spiral. The noise runs round a circle (no seam) whose
                    // size sets how many arcs go round; the radius runs along the third axis, so neighbouring orbits differ.
                    float a = atan2(pd.y, pd.x) + _KSPVE_BHNormal.w * StreakWinding * log(r / inner);
                    float ring = StreakRing * rsqrt(1.0 + StreakStretch);
                    ps = float3(ring * cos(a), ring * sin(a),
                        (StreakRadius(r) * StreakRadial + 0.8 * height * DetailRadius / max(r, DetailRadius)) * CloudScale);
                    lumps += Fbm(ps) * face;
                    billows += (1.0 - abs(2.0 * Noise3(ps * 2.7 + 5.1) - 1.0)) * face;
                }
                float surface = 0.5 + turbulence * (1.3 * lumps + 0.3 * billows);
                float hAbs = abs(height / ht - warp);
                float body = 1.0 - smoothstep(surface - 0.3, surface, hAbs); // 1 inside, rounded edge at the surface
                if (_KSPVE_BHForm.z > 0.0)
                {
                    // While it forms, two spiral shocks wind through it (gas still settling onto circular orbits).
                    float arms = 0.5 + 0.5 * cos(2.0 * atan2(pd.y, pd.x) - 5.0 * log(r / inner + 1.0) + 1.3 * lumps);
                    body *= max(1.0 + _KSPVE_BHForm.z * (1.8 * arms - 0.9), 0.0);
                }

                return body * radial * (DensityScale / r) * _KSPVE_BHForm.x;
            }

            float DiskShape(float3 x, out float r, out float3 pw)
            {
                float3 ps, pw1;
                float cloudBlend;
                return DiskShapeStreaks(x, r, pw, ps, pw1, cloudBlend);
            }

            // Sparks of white-hot matter and dark chunks of debris riding in the disk. One candidate per cell of a grid in
            // the rotating frame; each is counted by the one ray segment its closest approach falls in, and its size never
            // drops below the pixel footprint (energy-conserving), so distant particles fade instead of flickering.
            void AccumulateParticles(float3 x0, float3 x1, float traveled, float presence, float temperature, float g,
                inout float3 color, inout float transmittance)
            {
                float particles = _KSPVE_BHExtra.w, debrisAmount = _KSPVE_BHOptions.z;
                if (presence <= 0.0 || particles + debrisAmount <= 0.0) return;
                float unusedR;
                float3 a = ToDiskFrame(x0, unusedR);
                float3 ab = ToDiskFrame(x1, unusedR) - a;
                float ab2 = dot(ab, ab);
                if (ab2 < 1e-8) return;
                float3 baseCell = floor((a + ab * 0.5) / ParticleCell - 0.5);
                float footprint = traveled * _KSPVE_BHOptions.y;
                float3 spark = Saturate(Blackbody(temperature * g * 1.4)) * (g * g * g);
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float3 cell = baseCell + float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
                    float kind = Hash3(cell + 71.3);
                    if (kind > 0.32) continue; // most cells are empty
                    float3 jitter = float3(Hash3(cell + 1.7), Hash3(cell + 9.2), Hash3(cell + 23.5));
                    float3 p = (cell + 0.2 + jitter * 0.6) * ParticleCell;
                    float s = dot(p - a, ab) / ab2;
                    if (s < 0.0 || s >= 1.0) continue; // counted by the segment its closest approach falls in
                    float3 d = a + ab * s - p;
                    bool debris = kind < 0.08;
                    float radius = debris ? 0.07 + 0.08 * jitter.x : 0.012 + 0.018 * jitter.y;
                    float sigma = max(radius, footprint);
                    float fall = exp(-dot(d, d) / (sigma * sigma)) * (radius * radius) / (sigma * sigma) * presence;
                    if (debris)
                    {
                        // Dark rock in front of the glow, with a faint rim lit by the disk.
                        float cover = saturate(debrisAmount * fall * 0.95);
                        color += transmittance * spark * (0.04 * cover);
                        transmittance *= 1.0 - cover;
                    }
                    else
                    {
                        color += transmittance * spark * (particles * (3.0 + 8.0 * jitter.z * jitter.z * jitter.z) * fall); // a few much brighter
                    }
                }
            }

            // Relativistic jets along the spin axis: a collimated column (width grows as z^0.55). With the spiral it is a solid,
            // bright tube, thick from its rounded base (radius ~ (z + JetBase)^0.55), whose surface is streaked by fine bands
            // of gas winding round it; the whole pattern streams outward (and so appears to spin). The pattern lives in
            // zeta = z^0.45, where the swirl's pitch keeps pace with the widening jet and the flow speeds up outward. The glow
            // fades halfway out and the gas carries on as a darker, smoky plume. The gas absorbs, so the tube looks solid.
            // Doppler beaming for gas moving at _KSPVE_BHJet.w makes the approaching jet outshine the receding one. Dimmed by
            // the disk in front of it and lensed like everything else.
            void AccumulateJets(float3 x, float3 v, float dt, inout float3 color, inout float transmittance)
            {
                float strength = _KSPVE_BHJet.x, len = _KSPVE_BHJet.y, width = _KSPVE_BHJet.z, speed = _KSPVE_BHJet.w;
                float spiralAmount = _KSPVE_BHJet2.x, twistRate = _KSPVE_BHJet2.y, flowRate = _KSPVE_BHJet2.z, absorb = _KSPVE_BHJet2.w;
                if (strength <= 0.0) return;
                float3 n, t, b;
                JetAxes(n, t, b);
                float z = dot(x, n), az = abs(z);
                if (_KSPVE_BHForm.w > 0.0)
                {
                    // The heads punching out through the gas round the hole: bright knots at the jets' tips.
                    float hr = width * pow(len + JetBase * spiralAmount, 0.55);   // as wide as the jet there
                    float hd = az - 0.95 * len;
                    float3 hp = x - z * n;
                    float head = exp(-(hd * hd + dot(hp, hp)) / (hr * hr));
                    color += transmittance * float3(0.75, 0.88, 1.0) * (_KSPVE_BHForm.w * strength * 0.25 * head * dt / hr);
                }
                float start = 1.1 * (1.0 - spiralAmount);                                               // the spiral tube starts at the hole
                float knotAmount = _KSPVE_BHJetKnots.x;
                if (az < start || az > len * (knotAmount > 0.0 ? 1.25 : 1.0)) return;
                float3 perp = x - z * n;
                float rho = length(perp);
                float R = width * pow(az + JetBase * spiralAmount, 0.55);
                if (rho > (knotAmount > 0.0 ? 7.0 : 3.0) * R) return;
                float s = rho / R;
                float tail = (1.0 - smoothstep(len * 0.6, len, az)) * pow(2.0 / max(az, 2.0), 0.6);
                float along = smoothstep(1.1, 2.5, az) * tail;

                // Angle around the axis, and the advected coordinate along it (the pattern streams outward).
                float phi = atan2(dot(perp, b), dot(perp, t));
                float flow = pow(az, 0.45) - flowRate * _KSPVE_BHLook.w + (z < 0.0 ? 7.3 : 0.0);
                float twist = twistRate * flow;

                // Turbulence twisted along with the swirl, so the wisps ride the spiral.
                float tw = phi - 0.5 * twist * spiralAmount;
                float3 q = float3(s * cos(tw) * 1.8, s * sin(tw) * 1.8, flow * 2.6);
                float nz = Fbm(q);
                float lumps = 0.35 + 1.3 * nz * nz;

                float plain = (exp(-2.5 * s * s) + 0.25 * exp(-0.6 * s * s)) * lumps;                   // spine + sheath
                // Spiral core: a solid tube with a rounded end, its surface streaked by bands of brighter gas winding round it.
                // The swirl changes how brightly the gas glows, not how much it absorbs, so it still shows where the tube is thick.
                float u = saturate((az - start) / (0.85 * R));
                float cap = max(sqrt(u * (2.0 - u)), 1e-3);                                             // rounded end
                float column = exp(-pow(s / (0.85 * cap), 10.0));
                float swirl = 0.5 + 0.5 * cos(12.0 * phi - twist + 1.5 * nz);
                float streaks = 0.7 + (0.6 * swirl - 0.3) * smoothstep(0.15, 0.6, s);                   // on the surface, not the axis
                // The glow fades out halfway along; the gas carries on as a darker, smoky plume.
                float glowFade = 1.0 - smoothstep(len * 0.3, len * 0.75, az);
                float spiralGlow = column * streaks * (0.9 + 0.2 * nz) * glowFade;
                float spiralGas = 4.0 * column * (1.0 - glowFade * 0.4) * (0.5 + nz);
                float density = along * plain + (tail * spiralGlow - along * plain) * spiralAmount;
                float gas = along * plain + (tail * spiralGas - along * plain) * spiralAmount;
                if (knotAmount > 0.0)
                {
                    // Past the break the beam comes apart into a chain of blobs (plasmoids) streaming out with the flow, each a
                    // turbulent puff with a brighter bow-shaped front, swelling and fading as it goes. They are spaced in
                    // z^0.45, so further out they are further apart, as the flow speeds up.
                    float breakAt = _KSPVE_BHJetKnots.y * len;
                    float knotWeight = knotAmount * smoothstep(breakAt * 0.85, breakAt * 1.15 + 1.0, az);
                    float side = z < 0.0 ? 0.37 : 0.0;
                    float spacing = max(_KSPVE_BHJetKnots.z, 1e-3) * pow(len, 0.45);
                    float moved = flowRate * _KSPVE_BHLook.w;
                    float k0 = floor((pow(az, 0.45) - moved) / spacing - side);
                    float blob = 0.0;
                    [unroll] for (int j = 0; j < 2; j++)   // the blobs either side, so big ones overlap rather than cut off
                    {
                        float kq = k0 + j;
                        float zc = pow(max((kq + side) * spacing + moved, 1e-3), 1.0 / 0.45);
                        float seed = kq * 12.9898 + side * 78.233;
                        float hb = frac(sin(seed) * 43758.5453), hx = frac(sin(seed + 1.7) * 43758.5453), hy = frac(sin(seed + 3.1) * 43758.5453);
                        float Rb = width * pow(zc + JetBase * spiralAmount, 0.55) * (0.7 + 1.0 * _KSPVE_BHJetKnots.w * zc / len) * (0.75 + 0.5 * hb);
                        float2 wobble = (float2(hx, hy) - 0.5) * (0.7 * Rb);
                        float3 local = float3(dot(perp, t) - wobble.x, dot(perp, b) - wobble.y, az - zc) / Rb;
                        float3 seedOffset = float3(kq * 3.7, side * 9.1, _KSPVE_BHLook.w * 0.05);
                        float turb = Fbm(local * 1.6 + seedOffset) - 0.5;
                        float wisps = 1.0 - abs(2.0 * Noise3(local * 4.0 + seedOffset * 1.3) - 1.0);   // filaments
                        // Squashed at the front, drawn out into a wispy trail behind.
                        float sd = length(float3(local.xy, local.z * (local.z > 0.0 ? 1.7 : 0.75))) + 0.9 * turb;
                        float shellD = (sd - 0.65) / 0.15, coreD = sd / 0.4;
                        float front = 0.3 + 0.7 * smoothstep(-0.5, 0.7, local.z);   // the bow-shaped leading cap
                        blob += (exp(-shellD * shellD) * front * (0.5 + wisps * wisps) + 0.25 * exp(-coreD * coreD)) * (0.6 + 0.8 * hb);
                    }
                    float fade = pow(2.0 / max(az, 2.0), 0.6) * (1.0 - smoothstep(len * 0.95, len * 1.25, az));
                    blob *= 3.0 * fade;
                    density = lerp(density, blob, knotWeight);
                    gas = lerp(gas, blob * 0.5, knotWeight);   // glowing plasma: they hide little behind them
                }

                // Relativistic beaming: the gas moves away from the hole along the axis.
                float cosT = dot(n * sign(z), -normalize(v));
                float gamma = rsqrt(1.0 - speed * speed);
                float boost = gamma * (1.0 - speed * cosT);
                float beam = min(1.0 / (boost * boost * boost * gamma * gamma * gamma), 10.0);   // D^3 / gamma^3 (D = 1 / boost), capped

                float3 jetColor = lerp(_KSPVE_BHJetColor.rgb, 1.0, clamp(0.4 * density * beam / 8.0, 0.0, 0.35));
                color += transmittance * jetColor * (strength * 2.5 * density * beam * dt);
                transmittance *= exp(-absorb * gas * dt / R);
            }

            void AccumulateDisk(float3 x, float3 v, float dt, float traveled, inout float3 color, inout float transmittance)
            {
                float r;
                float3 pw;
                float3 ps, pw1;
                float cloudBlend;
                float shape = DiskShapeStreaks(x, r, pw, ps, pw1, cloudBlend);
                if (shape < 1e-3) return;
                float face = _KSPVE_BHStreak.x;
                // From above the streak pattern varies across the disk only, not up through it, so it stays smooth.
                float3 flat = float3(ps.xy, StreakRadius(r) * StreakRadial * CloudScale);
                float streakHeat = face > 0.0 ? Fbm(flat * 1.3 + 9.1) : 0.0;
                float cloudHeat = 0.0;
                if (face < 1.0)
                {
                    cloudHeat = Fbm(pw * 1.3 + 9.1);
                    if (cloudBlend > 0.0) cloudHeat = lerp(cloudHeat, Fbm(pw1 * 1.3 + 9.1), cloudBlend);
                }
                float heat = cloudHeat * (1.0 - face) + streakHeat * face;
                float density = shape * (0.6 + 0.4 * heat); // hot clumps inside a continuous body: no holes
                float lane = 1.0;
                if (face > 0.0)
                {
                    // From above: a few big bright arcs, finely striated along the orbit, with dark lanes between them. The gas
                    // is too thick for thinner lanes to show, so the light dims with them too.
                    float arcs = smoothstep(StreakArcLo, StreakArcHi, streakHeat);
                    float fine = 1.0 - abs(2.0 * Noise3(float3(flat.xy * 1.5, flat.z * StreakFine) + 7.7) - 1.0);
                    lane = 1.0 - face * StreakGap * (1.0 - arcs * (1.0 - StreakFineWeight + StreakFineWeight * fine * fine));
                    density *= lane;
                    lane *= 1.0 - face * (1.0 - StreakExposure);
                }

                // Self-shadowing towards the hot inner disk: lit flanks face the hole, crevices and far sides fall dark.
                // Two shape-only samples (average clump density) keep this cheap.
                float3 toHole = -normalize(x);
                float unusedR;
                float3 unusedPw;
                float d1 = DiskShape(x + toHole * 0.4, unusedR, unusedPw);
                float d2 = DiskShape(x + toHole * 1.2, unusedR, unusedPw);
                float lightTransmittance = exp(-(d1 * 0.55 + d2 * 0.85) * 0.8 * ShadowAbsorb);

                float3 vn = normalize(v);
                float cosScatter = dot(-toHole, -vn);
                const float hgG = 0.35;
                float phase = 0.25 + 0.75 * (1.0 - hgG * hgG) / pow(1.0 + hgG * hgG - 2.0 * hgG * cosScatter, 1.5);
                float inner = _KSPVE_BHDisk.x;
                float incident = LightStrength * pow(inner / max(r, inner), 2.0);
                // outerGlow: beyond DetailRadius the disk is shown as if exposed for it, lit by the inner disk: warmed by
                // its light (an irradiated disk's temperature falls only as r^-1/2), the inverse-square falloff made up, and
                // the light it scatters in the inner disk's colour.
                float outerLit = _KSPVE_BHStreak.y * smoothstep(DetailRadius, 3.0 * DetailRadius, r);
                float exposure = 1.0 + outerLit * ((r / DetailRadius) * (r / DetailRadius) - 1.0);
                incident *= exposure;

                // Relativistic shift: gravitational redshift times Doppler from the disk's orbital motion.
                float3 n = _KSPVE_BHNormal.xyz;
                float peak = _KSPVE_BHLook.x;
                float knots = smoothstep(0.45, 0.75, heat) * _KSPVE_BHDisk.w;
                float temperature = DiskTemperature(r, inner, peak) * (0.9 + (0.3 + StreakHeat * face) * knots);
                if (outerLit > 0.0) temperature = max(temperature, peak * 0.3 * sqrt(inner / r) * (0.9 + 0.3 * knots) * min(outerLit, 1.0));
                float3 orbit = normalize(cross(n, x - dot(x, n) * n)) * _KSPVE_BHNormal.w;
                float beta = min(sqrt(0.5 / max(r - 1.0, 0.05)), 0.95);
                float gamma = rsqrt(1.0 - beta * beta);
                float doppler = 1.0 / (gamma * (1.0 - beta * dot(orbit, -vn)));
                float gravitational = sqrt(saturate(1.0 - 1.0 / length(x)));
                float g = doppler * gravitational;
                float beaming = g * g * g;

                // Hot plasma in its own blackbody colour at the Doppler-shifted temperature: white-hot where it races towards
                // you, then yellow, gold and deep orange further out. Brighter in the hot clumps, darker in the shadowed
                // crevices, plus light from the inner disk scattered off the bulges.
                // From above the colour is softer: the bright streaks go white-blue rather than cyan.
                float3 hot = SaturateBy(Blackbody(temperature * g), lerp(_KSPVE_BHExtra.x, StreakSaturation, face));
                // The disk's parts by hand: colours shading from the inner to the outer edge (keeping each part's brightness),
                // and brightness inside and out, over the log of the radius.
                float span = saturate(log(r / inner) / max(log(_KSPVE_BHDisk.y / inner), 1e-3));
                if (_KSPVE_BHTint.x > 0.0)
                {
                    float3 tint = lerp(_KSPVE_BHTintInner.rgb, _KSPVE_BHTintOuter.rgb, span);
                    float lum = dot(hot, float3(0.2126, 0.7152, 0.0722));
                    hot = lerp(hot, tint * (lum / max(dot(tint, float3(0.2126, 0.7152, 0.0722)), 1e-4)), _KSPVE_BHTint.x);
                }
                float partBrightness = lerp(_KSPVE_BHTint.y, _KSPVE_BHTint.z, span);
                float shade = 0.3 + 0.7 * lightTransmittance;
                float3 thermal = hot * (pow(temperature / peak, 3.0) * EmissionStrength * (0.6 + 1.4 * knots) * shade * exposure);
                float3 lightColor = outerLit > 0.0 ? lerp(hot, Saturate(Blackbody(peak * 0.8)), min(outerLit, 1.0)) : hot;
                float3 scattered = lightColor * (incident * lightTransmittance * phase * 0.4);
                color += transmittance * (thermal + scattered) * (_KSPVE_BHForm.y * beaming * density * lane * dt * partBrightness);
                transmittance *= exp(-density * Absorb * dt);

                AccumulateParticles(x, x + v * dt, traveled, saturate(shape / 5.0) * (1.0 - face * (1.0 - StreakExposure)), temperature, g, color, transmittance);
            }

            // --- the tidal disruption event -------------------------------------------------------------
            static const float TdeSourceScale = 2.0;   // an opaque surface at the disk's peak temperature looks about as bright as the disk there
            static const float TdeSaturation = 1.4;    // gentler than the disk's: stars look white-ish, not candy-coloured
            static const float StreamMaxDepth = 1.6;   // optical depth drawn through the stream's middle: soft and gaseous, its structure showing
            static const float EnvelopeMaxDepth = 0.35; // most optical depth of the outflow's haze any one view passes through: a glow, never a wall
            static const float VisibleBand = 26170.0;  // hc / (lambda k) at 550 nm, kelvin

            float Sq(float v) { return v * v; }

            // Visible-band (550 nm) brightness of a blackbody, unnormalised.
            float PlanckV(float kelvin) { return 1.0 / (exp(min(VisibleBand / max(kelvin, 500.0), 80.0)) - 1.0); }

            // Brightness relative to the disk at its peak temperature: physical up to that (a 6,000 K star is ~1.5% of it),
            // compressed logarithmically above, as the eye does (a 10^6 K disk is ~25x brighter).
            float PerceivedRatio(float ratio) { return ratio <= 1.0 ? ratio : 1.0 + 0.6 * log(ratio); }

            float3 Thermal(float kelvin)
            {
                float3 c = Blackbody(kelvin);
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                c = max(l + (c - l) * TdeSaturation, 0.0);
                return c * (TdeSourceScale * PerceivedRatio(PlanckV(kelvin) / PlanckV(_KSPVE_BHLook.x)));
            }

            // g = nu_obs / nu_emit for gas at x moving at beta (fraction of c, static frame), the photon heading along -vn.
            float Shift(float3 x, float3 beta, float3 vn)
            {
                float b2 = min(dot(beta, beta), 0.99);
                float gamma = rsqrt(1.0 - b2);
                float doppler = 1.0 / (gamma * (1.0 - dot(beta, -vn)));
                return doppler * sqrt(saturate(1.0 - 1.0 / length(x)));
            }

            // The event at x: total opacity k (per horizon radius), opacity-weighted source (emission = k * source), pure
            // glow (emission per unit length that does not absorb), and the largest safe step from here. The disk it builds
            // is the hole's own, drawn by AccumulateDisk.
            float TdeSample(float3 x, float3 vn, float traveled, out float k, out float3 emit, out float3 glow, out float envK, out float3 envEmit)
            {
                k = 0.0; emit = 0.0; glow = 0.0; envK = 0.0; envEmit = 0.0;
                float limit = 1e9;
                float r = length(x);
                float fp = traveled * _KSPVE_BHOptions.y;
                float3 n = _KSPVE_TdeNormal.xyz;
                float time = _KSPVE_BHLook.w;

                // The star: a tidally stretched, opaque ellipsoid with a glowing halo, gas pouring off its tips as it nears
                // the tidal radius (the inner tail leads, the outer trails), dissolving into the stream once torn apart.
                if (_KSPVE_TdeStar.w > 0.0)
                {
                    float3 y = x - _KSPVE_TdeStar.xyz;
                    float3 axis = _KSPVE_TdeStarAxis.xyz;
                    float3 vel = _KSPVE_TdeStarVel.xyz;
                    float la = _KSPVE_TdeStarAxis.w, sa = _KSPVE_TdeStarVel.w, presence = _KSPVE_TdeStar.w, tail = _KSPVE_TdeStarLook.y;
                    float la2 = sqrt(la * la + fp * fp), sa2 = sqrt(sa * sa + fp * fp);
                    float a = dot(y, axis);
                    float3 yp = y - a * axis;
                    float rho2 = a * a / (la2 * la2) + dot(yp, yp) / (sa2 * sa2);
                    float tailLength = tail * 6.0 * sa;
                    float d = length(y) - (1.2 * la2 + tailLength + 2.0 * sa2);
                    limit = min(limit, max(d, 0.1 * sa2));
                    float temp0 = _KSPVE_TdeStarLook.x;
                    float glowStar = _KSPVE_TdeStarLook.z;
                    float g = Shift(x, vel, vn);
                    if (rho2 < 1.21)
                    {
                        float cover = pow(la * sa * sa / (la2 * sa2 * sa2), 2.0 / 3.0);      // projected area kept / smeared
                        float tau = min(40.0, -log(1.0 - 0.98 * cover));
                        float ks = tau / (2.0 * sa2) * 3.0 * (1.0 - smoothstep(0.81, 1.0, rho2)) * presence;
                        float3 normal = normalize(a / (la2 * la2) * axis + yp / (sa2 * sa2) + 1e-9);
                        float limb = 0.4 + 0.6 * abs(dot(normal, vn));                           // Eddington limb darkening
                        float stretch = la / max(sa, 1e-6) - 1.0;
                        float temp = temp0 * (1.0 - 0.1 * stretch * a * a / (la2 * la2));        // gravity darkening at the tips
                        float granules = 0.88 + 0.24 * Fbm(y / sa2 * 6.0 + time * 0.02);
                        float3 src = Thermal(temp * g) * (limb * granules * glowStar);
                        k += ks; emit += ks * src;
                    }
                    // Halo: the star's own light scattered in its wind and the gas lifting off it (its light kept, not
                    // multiplied, when it is smaller than a pixel).
                    float rho = sqrt(rho2);
                    if (rho < 4.0)
                        glow += Thermal(temp0 * g) * (glowStar * presence * 0.05 * exp(-max(rho - 1.0, 0.0) / 0.15) / sa2 * (la * sa) / (la2 * sa2));
                    // Tidal tails through L1 (towards the hole) and L2 (away), bending ahead and behind along the orbit.
                    if (tail > 0.001)
                    {
                        float side = a < 0.0 ? -1.0 : 1.0;
                        float u = (abs(a) - 0.8 * la2) / max(tailLength, 1e-6);
                        if (u > 0.0 && u < 1.0)
                        {
                            float3 along = normalize(vel - dot(vel, axis) * axis + 1e-9);
                            float3 bend = along * (-side * u * u * tailLength * 0.5);
                            float3 off = yp - bend;
                            float r0 = (0.45 - 0.3 * u) * sa;
                            float radius = sqrt(r0 * r0 + fp * fp);                                      // smeared to the pixel, light kept
                            float nt = Fbm(float3(u * 6.0, dot(off, along) / radius, side * 3.1) + float3(time * 0.05, 0.0, 0.0));
                            float kt = tail * presence * 2.5 * r0 / (radius * radius) * exp(-dot(off, off) / (radius * radius)) * (1.0 - u) * (1.0 - u) * (0.4 + 1.2 * nt);
                            if (kt > 1e-5)
                            {
                                k += kt; emit += kt * Thermal(temp0 * 0.85 * g) * glowStar;
                                limit = min(limit, 0.3 * radius);
                            }
                        }
                    }
                }

                // The debris stream: a turbulent tube, optically thick in the middle with a soft edge and a faint halo,
                // clumped along its gas, glowing at its own temperature and lit by the flare. A negative brightness on a
                // vertex marks a break: no segment joins it to the one before (the stream ran out past the render radius).
                int count = (int)_KSPVE_Tde.y;
                if (count > 1)
                {
                    float bestK = 0.0, nearest = 1e9;
                    float4 bLook = 0.0, bVel = 0.0;
                    float3 bOff = 0.0;
                    float bWa = 1.0, bWz = 1.0, bDa = 0.0, bDz = 0.0;
                    int chunks = (int)_KSPVE_Tde.z;
                    [loop] for (int c = 0; c < chunks; c++)
                    {
                        float4 ch = _KSPVE_TdeChunks[c];
                        float dc = length(x - ch.xyz) - ch.w - 4.0 * fp;
                        // Well clear of the chunk: its bounding sphere is a safe step. Near it, test its segments, so a ray
                        // grazing the sphere is not left creeping along it.
                        if (dc > 0.25 * ch.w) { nearest = min(nearest, dc); continue; }
                        int last = min(c * TDE_CHUNK + TDE_CHUNK, count - 1);
                        [loop] for (int s = c * TDE_CHUNK; s < last; s++)
                        {
                            if (_KSPVE_TdeVertVel[s + 1].w < 0.0) continue; // the stream breaks here
                            float4 va = _KSPVE_TdeVerts[s], vb = _KSPVE_TdeVerts[s + 1];
                            float3 ab = vb.xyz - va.xyz;
                            float ab2 = dot(ab, ab);
                            if (ab2 < 1e-12) continue;
                            float u = saturate(dot(x - va.xyz, ab) / ab2);
                            float3 off = x - (va.xyz + ab * u);
                            float4 look = lerp(_KSPVE_TdeVertLook[s], _KSPVE_TdeVertLook[s + 1], u);
                            float wa = lerp(va.w, vb.w, u), wz = look.w;
                            float wa2 = sqrt(wa * wa + fp * fp), wz2 = sqrt(wz * wz + fp * fp);
                            float dist = length(off);
                            float reach = 4.0 * max(wa2, wz2);
                            nearest = min(nearest, dist - reach);
                            if (dist > reach) continue;
                            float3 tangent = ab * rsqrt(ab2);
                            float3 vert = n - dot(n, tangent) * tangent;
                            vert = dot(vert, vert) > 1e-8 ? normalize(vert) : normalize(cross(tangent, abs(tangent.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                            float3 across = cross(tangent, vert);
                            float da = dot(off, across) / wa2, dz = dot(off, vert) / wz2;
                            float q2 = da * da + dz * dz;
                            if (q2 > 16.0) continue;
                            float cover = max(sqrt(wa * wz / (wa2 * wz2)), _KSPVE_TdeLight.w);   // pixel coverage, with the display floor
                            float tau = min(min(look.y * 2.0 * min(wa, wz), StreamMaxDepth), -log(1.0 - 0.98 * min(cover, 1.0)));
                            // A core and a faint halo of gas lifted off it.
                            float kk = tau / (1.7725 * min(wa2, wz2)) * (exp(-q2) + 0.015 * exp(-q2 * 0.25));
                            if (kk > bestK)
                            {
                                bestK = kk; bLook = look; bVel = lerp(_KSPVE_TdeVertVel[s], _KSPVE_TdeVertVel[s + 1], u);
                                bVel.w = lerp(abs(_KSPVE_TdeVertVel[s].w), _KSPVE_TdeVertVel[s + 1].w, u);
                                bOff = off; bWa = wa2; bWz = wz2; bDa = da; bDz = dz;
                            }
                        }
                    }
                    limit = min(limit, bestK > 0.0 ? 0.35 * min(bWa, bWz) : max(nearest, 0.3 * max(fp, 0.02)));
                    if (bestK > 0.0)
                    {
                        // Turbulent filaments, and clumps where it fragments under its own gravity; both ride with the gas.
                        float wisps = 0.35 + 1.3 * Fbm(float3(bLook.x * 70.0, bDa * 0.8, bDz * 0.8) + float3(0.0, 0.0, time * 0.03));
                        float clump = lerp(1.0, smoothstep(0.3, 0.62, Noise3(float3(bLook.x * 90.0, bDa * 0.6, bDz * 0.6))) + 0.05, _KSPVE_TdeLight.z);
                        float ks = bestK * clump * wisps;
                        float knots = 0.85 + 0.3 * Noise3(float3(bLook.x * 40.0, 1.7, 2.3));   // hotter and cooler stretches along it
                        float3 own = Thermal(bLook.z * knots * Shift(x, bVel.xyz, vn));
                        // Embers: knots of gas still glowing hot where clumps collapse.
                        own *= 1.0 + 5.0 * smoothstep(0.8, 0.95, Noise3(float3(bLook.x * 260.0, bDa * 2.5, bDz * 2.5 + time * 0.2)));
                        // Lit by the central source: the flare (Eddington units) plus the quiescent disk, falling off as 1/r^2.
                        float lit = 0.5 + 0.5 * dot(normalize(bOff + 1e-9), -x / r);
                        float incident = (0.4 + _KSPVE_TdeLight.x) * min(1.0, 64.0 / (r * r));
                        float3 scattered = Saturate(Blackbody(30000.0)) * (TdeSourceScale * 0.25 * incident * lit);
                        // The display boost is for cool gas only: hot gas is bright enough as it is.
                        float boost = 1.0 + (_KSPVE_TdeLight.y - 1.0) * (1.0 - smoothstep(7000.0, 16000.0, bLook.z));
                        float3 src = (own + scattered) * (boost * bVel.w);
                        k += ks; emit += ks * src;
                    }
                }

                // The self-intersection shock: a white-hot knot where the returning stream hits the incoming one, spraying
                // shocked gas along the returning stream's motion in a widening fan.
                if (_KSPVE_TdeShock.w > 0.0)
                {
                    float3 y = x - _KSPVE_TdeShock.xyz;
                    float3 dir = _KSPVE_TdeShockLook.xyz;
                    float rad = sqrt(_KSPVE_TdeShockLook.w * _KSPVE_TdeShockLook.w + fp * fp);
                    float len = 8.0 * rad;
                    float d = length(y) - (len * 2.0 + 3.0 * rad);
                    limit = min(limit, max(d, 0.25 * rad));
                    if (d < 0.0)
                    {
                        float along = dot(y, dir);
                        float perp = length(y - along * dir);
                        float core = exp(-dot(y, y) / (rad * rad));
                        float spread = rad * (0.7 + 2.5 * max(along, 0.0) / len);
                        float fan = along > -rad ? exp(-Sq(max(along, 0.0) / len) - Sq(perp / spread)) * (0.4 + 1.2 * Fbm(y / rad * 0.7 + time * 0.1)) : 0.0;
                        glow += Thermal(_KSPVE_TdeShock.w > 0.3 ? 60000.0 : 40000.0) * (0.12 * min(_KSPVE_TdeShock.w, 3.0) * (core + 0.5 * fan) / rad);
                    }
                }

                // The periapsis flash: a brief white-hot burst riding with the debris.
                if (_KSPVE_TdeFlash.w > 0.0)
                {
                    float rad = sqrt(_KSPVE_TdeFlashLook.x * _KSPVE_TdeFlashLook.x + fp * fp);
                    float dy = length(x - _KSPVE_TdeFlash.xyz);
                    limit = min(limit, max(dy - 4.0 * rad, 0.25 * rad));
                    glow += Thermal(80000.0) * (0.3 * _KSPVE_TdeFlash.w * exp(-dy * dy / (rad * rad)) / rad);
                }

                // The outflow's envelope: radial profile, thinner in the polar funnels, streaked by the outflow.
                if (_KSPVE_TdeEnv.w > 0.0)
                {
                    float lr = log(r / _KSPVE_TdeEnv.x) / _KSPVE_TdeEnv.y * (TDE_BINS - 1);
                    if (lr >= 0.0 && lr <= TDE_BINS - 1)
                    {
                        limit = min(limit, 0.06 * r);
                        int b = min((int)lr, TDE_BINS - 2);
                        float f = lr - b;
                        float c = dot(x, n) / r;
                        float streaks = 0.35 + 1.3 * Fbm(x / r * 3.0 + float3(0.0, lr * 0.06 - time * 0.01, 0.0));
                        float ke = lerp(_KSPVE_TdeEnvK[b], _KSPVE_TdeEnvK[b + 1], f) * (_KSPVE_TdeEnv.z + (1.0 - _KSPVE_TdeEnv.z) * (1.0 - c * c)) * streaks;
                        float te = lerp(_KSPVE_TdeEnvT[b], _KSPVE_TdeEnvT[b + 1], f);
                        envK = ke; envEmit = ke * Thermal(te);
                    }
                }
                return limit;
            }

            float4 fragMarch(v2f i) : SV_Target
            {
                float2 ndc = i.ndc;
                float2 uv = ndc * 0.5 + 0.5;
                float3 forward = _KSPVE_BHForward.xyz;
                float3 v = normalize(forward + _KSPVE_BHRight.xyz * ndc.x * _KSPVE_BHRay.x + _KSPVE_BHUp.xyz * ndc.y * _KSPVE_BHRay.y);

                float rs = _KSPVE_BHCenter.w;
                float3 x = -_KSPVE_BHCenter.xyz / rs; // camera position relative to the hole, horizon radii
                float bound = _KSPVE_BHRay.w;

                // Only rays that enter the bounding sphere are bent; everything else is left to the scene.
                float b = dot(x, v);
                float c = dot(x, x) - bound * bound;
                float disc = b * b - c;
                if (disc < 0.0) return 0;
                float s = sqrt(disc);
                float tEnter = max(0.0, -b - s);
                if (-b + s < 0.0) return 0;

                // Something nearer than the hole (a planet in front) hides it. Bodies are tested as exact spheres, whatever
                // draws them (planet packs' shaders need not write the depth texture, and near a planet the local camera
                // draws it); the scaled scene's depth catches anything else. The renderer only passes bodies, and only
                // asks for the depth texture, when one could be in front.
                float sceneDistance = 1e30;
                if (_KSPVE_BHOptions.x > 0.5)
                    sceneDistance = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)) / max(dot(v, forward), 1e-4) / rs;
                [unroll] for (int o = 0; o < BH_OCCLUDERS; o++)
                {
                    float4 body = _KSPVE_BHOccluders[o];
                    if (body.w <= 0.0) continue;
                    float bo = dot(body.xyz, v);
                    float co = dot(body.xyz, body.xyz) - body.w * body.w;
                    float disco = bo * bo - co;
                    if (co < 0.0) sceneDistance = 0.0;                                         // the eye is inside it
                    else if (bo > 0.0 && disco > 0.0) sceneDistance = min(sceneDistance, bo - sqrt(disco));
                }
                if (sceneDistance < tEnter) return 0;

                x += v * tEnter;
                float traveled = tEnter;
                float3 hv = cross(x, v);
                float h2 = dot(hv, hv);

                float3 color = 0;
                float transmittance = 1.0;
                float envDepth = 0.0;   // the outflow's haze passed through so far (capped: EnvelopeMaxDepth)
                bool captured = false;
                float rMin = 1e9;
                float3 glowColor = Saturate(Blackbody(_KSPVE_BHLook.x * 0.6));
                if (_KSPVE_BHTint.x > 0.0)
                {
                    // Coloured by hand: the inner disk's colour, as bright as the physical glow.
                    float3 tint = _KSPVE_BHTintInner.rgb;
                    float lum = dot(glowColor, float3(0.2126, 0.7152, 0.0722));
                    glowColor = lerp(glowColor, tint * (lum / max(dot(tint, float3(0.2126, 0.7152, 0.0722)), 1e-4)), _KSPVE_BHTint.x);
                }
                // A pulsar: no shadow, no bending, no disk; rays stop at its surface and the star body shows through.
                bool pulsar = _KSPVE_BHOptions.w > 0.5;
                bool tde = _KSPVE_Tde.x > 0.5;
                float bend = pulsar ? 0.0 : 1.0;
                int maxSteps = (int)_KSPVE_BHRay.z;
                float jitter = 0.2 + 0.8 * frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                [loop] for (int k = 0; k < maxSteps; k++)
                {
                    float r = length(x);
                    rMin = min(rMin, r);
                    if (r < 1.0) { captured = !pulsar; break; }
                    if (r > bound * 1.001 && dot(x, v) > 0.0) break;
                    if (traveled > sceneDistance) break;

                    // Adaptive step: proportional to r, and finer near the disk plane so the disk is never skipped.
                    float nearPlane = abs(dot(x, _KSPVE_BHNormal.xyz));
                    // A disk bigger than DetailRadius: its clumps, and so the steps, grow with the radius out there. Smaller
                    // disks keep the fixed steps they were tuned with.
                    float grain = _KSPVE_BHDisk.y > DetailRadius ? max(1.0, r / DetailRadius) : 1.0;
                    float dtCap = tde && r > _KSPVE_Tde.w ? 0.08 * r : 2.0 * grain;   // beyond the disk & jets the event may need long strides
                    float dt = clamp(min(0.08 * r, 0.35 * max(nearPlane, 0.3 * _KSPVE_BHDisk.z * r)), 0.004, dtCap);
                    // Fine steps inside the disk layer: about three per clump out where the clumps are big.
                    if (nearPlane < 5.0 * _KSPVE_BHDisk.z * r && r < _KSPVE_BHDisk.y * 1.2) dt = min(dt, 0.2 * grain * min(grain, 3.0));
                    float alongJet = abs(dot(x, _KSPVE_BHJetAxis.xyz));
                    if (_KSPVE_BHJet.x > 0.0 && alongJet > 1.1 * (1.0 - _KSPVE_BHJet2.x) && alongJet < _KSPVE_BHJet.y * (_KSPVE_BHJetKnots.x > 0.0 ? 1.25 : 1.0))   // and inside the jets
                    {
                        float jetR = _KSPVE_BHJet.z * pow(alongJet + JetBase * _KSPVE_BHJet2.x, 0.55);
                        if (length(x - dot(x, _KSPVE_BHJetAxis.xyz) * _KSPVE_BHJetAxis.xyz) < (_KSPVE_BHJetKnots.x > 0.0 ? 7.0 : 3.0) * jetR)
                            dt = min(dt, (0.35 - 0.15 * _KSPVE_BHJet2.x) * jetR);
                    }
                    float tdeK = 0.0, envK = 0.0;
                    float3 tdeEmit = 0.0, tdeGlow = 0.0, envEmit = 0.0;
                    if (tde) dt = min(dt, max(TdeSample(x, normalize(v), traveled, tdeK, tdeEmit, tdeGlow, envK, envEmit), 0.0005));
                    if (k == 0) dt *= jitter; // per-pixel start offset turns step banding into fine grain
                    dt = min(dt, max(sceneDistance - traveled, 1e-5));   // stop at the surface of whatever is in front
                    if (envK > 0.0)
                    {
                        float ke = min(envK, max(EnvelopeMaxDepth - envDepth, 0.0) / dt);
                        envDepth += ke * dt;
                        tdeK += ke;
                        tdeEmit += envEmit * (ke / envK);
                    }

                    // Glow: hot, thin plasma filling the space around the hole (lensed along with everything else).
                    color += transmittance * glowColor * (_KSPVE_BHExtra.y * (1.0 - StreakGlowDim * _KSPVE_BHStreak.x) * 0.03 * exp(-(r - 1.0) / 2.2)) * dt;
                    if (!pulsar) AccumulateDisk(x, v, dt, traveled, color, transmittance);
                    AccumulateJets(x, v, dt, color, transmittance);
                    if (tdeK > 0.0 || any(tdeGlow > 0.0))
                    {
                        // Exact for a uniform slab: the event's gas is often far too opaque for emission * dt.
                        float absorbed = 1.0 - exp(-tdeK * dt);
                        color += transmittance * (tdeEmit / max(tdeK, 1e-12) * absorbed + tdeGlow * dt);
                        transmittance *= 1.0 - absorbed;
                    }
                    if (transmittance < 0.01) break;

                    // Midpoint (RK2) step of x'' = -1.5 h^2 x / r^5.
                    float3 a1 = -1.5 * bend * h2 * x / pow(r, 5.0);
                    float3 xm = x + v * (0.5 * dt);
                    float3 vm = v + a1 * (0.5 * dt);
                    float rm = max(length(xm), 1e-3);
                    float3 am = -1.5 * bend * h2 * xm / pow(rm, 5.0);
                    x += vm * dt;
                    v += am * dt;
                    traveled += dt * length(vm);
                }

                // Photon ring: rays that skim the photon sphere (r = 1.5) before escaping pile up light from every
                // orbit around the hole, giving a razor-thin bright ring at the edge of the shadow.
                if (!captured && !pulsar)
                {
                    float d = (rMin - 1.5) / 0.045;
                    color += _KSPVE_BHRingColor.rgb * (_KSPVE_BHExtra.z * (1.0 - StreakRingDim * _KSPVE_BHStreak.x) * 8.0 * exp(-d * d));
                }

                // KSP renders in gamma space, so write sRGB-encoded colour.
                color = LinearToSrgb(1.0 - exp(-color * _KSPVE_BHLook.y));
                float alpha = captured ? 1.0 : 1.0 - transmittance;
                return float4(color, alpha);
            }

            // --- other kinds of object (pass 2) -----------------------------------------------------------------------
            // tools/BlackHolePreview/Objects.cs is the line-for-line CPU copy of this part.
            #define KIND_NEUTRON 1
            #define KIND_PROTO 2
            #define KIND_DEBRIS 3
            #define KIND_BELT 4
            #define KIND_NEBULA 5
            #define KIND_COMET 6
            #define KIND_GALAXY 7
            static const float FieldShellStart = 1.6;                          // SpaceObjects
            #define FieldShellRatio (_KSPVE_ObjField.z)
            #define FieldShells (_KSPVE_ObjField.y)
            static const float NebulaRadius = 10.0;                            // SpaceObjects.NebulaShaderRadius

            int ObjKind() { return (int)(_KSPVE_ObjKind.x + 0.5); }

            float Hash1(float n) { return frac(sin(n) * 43758.5453); }

            // A cell's hash, evenly spread over [0,1) (Hash3's product form piles up near 0, which a probability test can't take).
            float CellHash(float3 c, float salt) { return Hash1(dot(c, float3(127.1, 311.7, 74.7)) * 0.0137 + salt); }

            // Henyey-Greenstein phase, scaled so isotropic scattering is 1.
            float PhaseHG(float cosAngle, float g) { return (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * cosAngle, 1.5); }

            // A star's colour at luminance 1.
            float3 StarLight(float kelvin)
            {
                float3 c = Blackbody(kelvin);
                return c / max(dot(c, float3(0.2126, 0.7152, 0.0722)), 1e-4);
            }

            // The gaps planets clear (DiskGaps: spread over the disk, or placed one by one); they cut a belt's rocks too.
            float GapFactor(float r)
            {
                float gaps = 1.0;
                int gapCount = (int)_KSPVE_ObjShape.y;
                [loop] for (int k = 0; k < 16; k++)
                {
                    if (k >= gapCount) break;
                    float4 gap = _KSPVE_ObjGaps[k];
                    gaps *= 1.0 - gap.z * exp(-Sq((r - gap.x) / gap.y));
                }
                return gaps;
            }

            // Dust density of a protoplanetary or debris disk (or an asteroid belt's dust) at x. Protoplanetary: flared
            // (thicker further out), its density falling outward, cut by gaps where planets clear their orbits. Debris: one
            // narrow, thin ring.
            float DustDisk(float3 x, out float r, out float3 pd)
            {
                pd = ToDiskFrame(x, r);
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                // Its edges and gaps go by the distance from the star (orbits' sizes), its thickness by the angle above the
                // plane (their inclinations): a thick belt is lens-shaped, not a cylinder.
                float rs = length(pd);
                float soft = _KSPVE_ObjDisk2.y;
                if (rs < inner * max(1.0 - 0.4 * soft, 0.0) || rs > outer * (1.0 + 0.1 * soft)) return 0.0;
                float h = _KSPVE_BHDisk.z * r * pow(r / outer, _KSPVE_ObjShape.x);
                if (abs(pd.z) > 4.0 * h) return 0.0;
                float radial;
                if (ObjKind() == KIND_DEBRIS)
                {
                    float rc = 0.5 * (inner + outer), wr = 0.5 * (outer - inner);
                    radial = exp(-2.0 * Sq((rs - rc) / (wr * soft)));
                }
                else
                    radial = smoothstep(inner * (1.0 - 0.4 * soft), inner * (1.0 + 0.15 * soft), rs)
                        * (1.0 - smoothstep(outer * (1.0 - 0.1 * soft), outer * (1.0 + 0.1 * soft), rs)) * pow(inner / rs, _KSPVE_ObjDisk2.x);
                float gaps = GapFactor(rs);
                float vertical = exp(-0.5 * Sq(pd.z / h));
                float clumpy = _KSPVE_ObjDisk2.z;
                float clumps = 1.0 - 0.5 * clumpy + clumpy * Fbm(pd * (8.0 * _KSPVE_ObjDisk2.w / outer) + 3.1);
                if (_KSPVE_ObjSpiral.x > 0.0)
                {
                    // Spiral arms: density waves winding out from the inner disk.
                    float arms = cos(_KSPVE_ObjSpiral.x * atan2(pd.y, pd.x) - _KSPVE_ObjSpiral.z * log(r / inner));
                    clumps *= 1.0 + _KSPVE_ObjSpiral.y * arms;
                }
                return _KSPVE_ObjDust.x * radial * gaps * gaps * vertical * max(clumps, 0.0) / h;
            }

            // Starlight scattered by the dust (forward-scattering grains: brightest between the star and the eye), dimmed by
            // the dust between it and the star, with the inner rim hot enough to glow.
            void AccumulateDustDisk(float3 x, float3 vn, float dt, inout float3 color, inout float transmittance)
            {
                float r;
                float3 pd;
                float rho = DustDisk(x, r, pd);
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                if (rho * outer < 4e-4) return;
                float d = length(x);
                float r1, r2;
                float3 p1, p2;
                float s1 = DustDisk(x * 0.9, r1, p1), s2 = DustDisk(x * 0.72, r2, p2);
                float lightT = exp(-(s1 * 0.1 + s2 * 0.18) * d);
                float illum = pow(outer / max(d, inner * 0.5), _KSPVE_ObjDust.y);
                float phase = 0.25 + 0.75 * PhaseHG(dot(x / d, -vn), _KSPVE_ObjDisk3.x) * 0.5;
                float3 dustColor = lerp(_KSPVE_ObjDustColor.rgb, _KSPVE_ObjOuterDust.rgb, saturate((r - inner) / max(outer - inner, 1e-6)));
                float3 scattered = dustColor * StarLight(_KSPVE_ObjKind.z) * (illum * lightT * phase);
                float3 rim = Blackbody(_KSPVE_ObjDisk3.z) * (_KSPVE_ObjDisk3.y * smoothstep(inner * 1.8, inner * 0.9, r));
                color += transmittance * (scattered + rim) * (rho * dt);
                transmittance *= exp(-rho * dt);
            }

            // The star's glow in the gas round it (a young star is still wrapped in dust).
            void AccumulateStarGlow(float3 x, float dt, inout float3 color, float transmittance)
            {
                float size = _KSPVE_ObjDust.z * (ObjKind() == KIND_NEBULA ? NebulaRadius : _KSPVE_BHDisk.y);
                if (size <= 0.0) return;
                float d = length(x);
                float glow = 0.05 * size / (d * d + 0.01 * size * size) * exp(-d / size);
                color += transmittance * StarLight(_KSPVE_ObjKind.z) * (glow * dt);
            }

            // How likely a cell of rock space at pd (the disk's frame) holds a rock: a belt, or a debris ring.
            float RockField(float3 pd)
            {
                float r = length(pd.xy), rs = length(pd);   // as DustDisk: edges by distance, thickness by angle
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                float radial = ObjKind() == KIND_DEBRIS
                    ? exp(-2.0 * Sq((rs - 0.5 * (inner + outer)) / (0.5 * (outer - inner))))
                    : smoothstep(inner * 0.9, inner * 1.15, rs) * (1.0 - smoothstep(outer * 0.85, outer * 1.1, rs));
                float h = max(_KSPVE_BHDisk.z * r * _KSPVE_ObjDisk3.w, 1e-6);
                return _KSPVE_ObjRocks.x * radial * GapFactor(rs) * exp(-0.5 * Sq(pd.z / h));
            }

            // Rocks: one candidate per cell of a grid in the disk's frame, counted by the ray segment its closest approach
            // falls in, lit on the side facing the star. Never smaller than the pixel footprint, keeping their light, so
            // distant rocks become a fine glitter rather than flickering.
            void AccumulateRocks(float3 x0, float3 x1, float traveled, inout float3 color, inout float transmittance)
            {
                if (_KSPVE_ObjRocks.x <= 0.0) return;
                float unusedR;
                float3 a = ToDiskFrame(x0, unusedR);
                float3 ab = ToDiskFrame(x1, unusedR) - a;
                float ab2 = dot(ab, ab);
                float cell = _KSPVE_ObjDust.w;
                if (ab2 < 1e-10 * cell * cell) return;
                float3 vn = ab * rsqrt(ab2);
                float3 baseCell = floor((a + ab * 0.5) / cell - 0.5);
                float footprint = traveled * _KSPVE_BHOptions.y;
                float3 light = StarLight(_KSPVE_ObjKind.z);
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float3 c = baseCell + float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
                    float3 jitter = float3(CellHash(c, 1.7), CellHash(c, 9.2), CellHash(c, 23.5));
                    float3 p = (c + 0.2 + jitter * 0.6) * cell;
                    // The hash holds only ~8 bits (frac of sin x 43758) and is exactly 0 one cell in 256: below that a
                    // cell's chance is taken as none.
                    float chance = RockField(p);
                    if (chance < 1.0 / 256.0 || CellHash(c, 71.3) >= chance) continue;
                    float s = dot(p - a, ab) / ab2;
                    if (s < 0.0 || s >= 1.0) continue;
                    float3 dv = a + ab * s - p;
                    float dd = dot(dv, dv);
                    float radius = _KSPVE_ObjRocks.y * cell * (0.08 + 0.35 * _KSPVE_ObjSpiral.w * jitter.x * jitter.x * jitter.x);
                    // Up close no rock looks wider than rockMaxAngle: they are sized to the belt to be seen from afar.
                    if (_KSPVE_ObjRocks.w > 0.0) radius = min(radius, _KSPVE_ObjRocks.w * (traveled + s * sqrt(ab2)));
                    float sigma = max(radius, footprint);
                    float cover = saturate(exp(-dd / (sigma * sigma)) * (radius * radius) / (sigma * sigma));
                    if (cover < 1e-4) continue;
                    // The rock's visible face, lit by the star at the centre.
                    float3 nrm = normalize(dv - vn * sqrt(max(radius * radius - dd, 0.0)) + 1e-6);
                    float lit = max(dot(nrm, -normalize(p)), 0.0);
                    float illum = pow(outer / max(length(p), inner * 0.5), _KSPVE_ObjDust.y);
                    float albedo = _KSPVE_ObjRocks.z * (0.6 + 0.8 * jitter.z);
                    float3 rock = light * (albedo * (illum * lit * 1.4 + 0.02)) * _KSPVE_ObjRockColor.rgb * float3(1.0, 0.95 + 0.1 * jitter.y, 0.95 + 0.1 * jitter.y);
                    color += transmittance * rock * cover;
                    transmittance *= 1.0 - cover;
                }
            }

            // A nebula NebulaRadius across: glowing gas in two colours, bright filaments, dark dust lanes, a ragged edge.
            void Nebula(float3 x, out float gas, out float mix, out float filament, out float dust)
            {
                gas = 0.0; mix = 0.0; filament = 0.0; dust = 0.0;
                if (length(x) > 1.3 * NebulaRadius) return;
                float unusedR;
                float3 pd = ToDiskFrame(x, unusedR);
                // Stretched along the normal and warped twice, so it isn't a ball.
                float3 xs = float3(pd.x * 0.75, pd.z * 0.75 * _KSPVE_ObjCloud2.x, pd.y);
                float3 q = xs * (0.22 * _KSPVE_ObjCloud.y) + float3(0.0, _KSPVE_BHLook.w * 0.002, 0.0);
                float3 w = float3(Fbm(q + 1.7), Fbm(q + 9.2), Fbm(q + 4.3)) - 0.5;
                float3 p = q + w * (2.2 * _KSPVE_ObjCloud.z);
                float3 w2 = float3(Noise3(p * 2.1 + 5.5), Noise3(p * 2.1 + 8.8), Noise3(p * 2.1 + 2.2)) - 0.5;
                p += w2 * (0.45 * _KSPVE_ObjCloud.z);
                float edge = (1.0 - smoothstep(2.0, 11.5, length(xs) * (0.3 + _KSPVE_ObjCloud2.y * Fbm(pd * 0.12 + 6.6))))
                    * (1.0 - smoothstep(NebulaRadius, 1.3 * NebulaRadius, length(x)));
                // A planetary nebula's shell: the gas piled into a hollow, ragged shell.
                float shell = (length(xs) / NebulaRadius) * (0.85 + 0.3 * Fbm(p * 0.5 + 1.1));
                edge *= 1.0 + _KSPVE_ObjExtra.x * (4.0 * exp(-Sq((shell - 0.55) / 0.06)) - 1.0);
                if (edge <= 0.0) return;
                float n = Fbm(p);
                gas = edge * pow(max(n * 2.1 - _KSPVE_ObjCloud2.z, 0.0), 2.0) * _KSPVE_ObjDust.x;
                mix = smoothstep(0.25, 0.75, Fbm(p * 0.7 + 7.7));
                mix = lerp(mix, smoothstep(0.45, 0.62, shell), _KSPVE_ObjExtra.x);   // a shell: one gas inside, the other outside (O III, H-alpha)
                // Filaments: ridges of two octaves, thin and bright where the gas is.
                float ridge1 = 1.0 - abs(2.0 * Noise3(p * 2.6 + 2.2) - 1.0);
                float ridge2 = 1.0 - abs(2.0 * Noise3(p * 6.1 + 4.4) - 1.0);
                filament = edge * (pow(ridge1, 12.0) + 0.5 * pow(ridge2, 16.0)) * smoothstep(0.32, 0.6, n) * _KSPVE_ObjDust.x * _KSPVE_ObjCloud.w;
                dust = edge * max(Fbm(p * 1.4 + 3.3) * 2.0 - 1.05, 0.0) * _KSPVE_ObjCloud.x;
            }

            // A comet at the centre, its star along _KSPVE_ObjSun and moving along _KSPVE_ObjVel: a glowing coma, a straight
            // ion tail pointing directly away from the star, streaked and knotted, and a dust tail curving back along the
            // orbit (the grains keep the comet's motion as the starlight pushes them out), sharp on its leading edge.
            void AccumulateComet(float3 x, float dt, float fp, inout float3 color, inout float transmittance)
            {
                float act = _KSPVE_ObjComet.x;
                if (act <= 0.0) return;
                float r = length(x);
                float3 away = -_KSPVE_ObjSun.xyz, vel = _KSPVE_ObjVel.xyz;
                float time = _KSPVE_BHLook.w;
                // Coma: a soft glow round the nucleus, greenish (C2), with a bright false nucleus.
                float coma = exp(-r / _KSPVE_ObjComet.y) / (0.3 + r) * act;
                color += transmittance * _KSPVE_ObjComaColor.rgb * (coma * 0.45 * _KSPVE_ObjComet3.x * dt);
                float core = exp(-r * r / max(0.02, fp * fp));
                color += transmittance * float3(1.0, 0.97, 0.9) * (core * 3.0 * _KSPVE_ObjComet3.w * act * dt / max(0.15, fp));

                float s = dot(x, away);
                if (s <= -0.5) return;
                float sp = max(s, 0.0);
                // Ion tail: straight away from the star, narrow, blue (CO+), streamers and knots drifting outward.
                float3 off = x - away * sp;
                float d = length(off);
                float w = max((0.25 + 0.025 * sp) * _KSPVE_ObjComet2.z, fp);
                float3 e1 = normalize(cross(away, abs(away.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 e2 = cross(away, e1);
                float around = atan2(dot(off, e2), dot(off, e1));
                float streak = _KSPVE_ObjComet2.w;
                float streamers = 1.0 - 0.7 * streak + 1.4 * streak * Sq(Noise3(float3(cos(around) * 3.0, sin(around) * 3.0, sp * 0.05 - time * 0.3)));
                float knots = 1.0 - 0.4 * streak + 0.8 * streak * Noise3(float3(0.3, 1.7, sp * 0.12 - time * 0.5));
                float ion = exp(-Sq(d / w)) * exp(-sp / _KSPVE_ObjComet.z) * smoothstep(-0.5, 1.0, s) * streamers * knots / w;
                color += transmittance * _KSPVE_ObjColorA.rgb * (ion * 0.5 * _KSPVE_ObjComet3.y * act * dt);

                // Dust tail: the grains spread back along the orbit (older ones lag more): a sharp leading edge, a soft trailing one.
                float dustLength = _KSPVE_ObjComet.w, widthAmount = _KSPVE_ObjComet2.y;
                float3 curve = away * sp - vel * (_KSPVE_ObjComet2.x * sp * sp / dustLength + 0.1 * sp);
                float3 offD = x - curve;
                float lag = dot(offD, -vel);
                float wd = max(0.25 + 0.04 * sp * widthAmount, fp);
                float wl = lag > 0.0 ? wd * (1.0 + 2.5 * widthAmount * sp / (sp + 20.0)) : wd;
                float side = length(offD + lag * vel);
                float d2 = Sq(lag / wl) + Sq(side / (wd * 1.4));
                float striae = 1.0 - 0.3 * _KSPVE_ObjExtra.y + 0.6 * _KSPVE_ObjExtra.y * Noise3(float3(lag * 0.7 / (1.0 + 0.05 * sp), sp * 0.02, 4.4));
                float dustTail = exp(-d2) * exp(-sp / dustLength) * smoothstep(-0.5, 1.5, s) * striae / sqrt(wd * wl);
                color += transmittance * _KSPVE_ObjColorB.rgb * (dustTail * 0.35 * _KSPVE_ObjComet3.z * act * dt);
            }

            // A galaxy NebulaRadius across, in the disk's frame: a bulge of old stars (stretched into a bar), a thin disk of stars
            // wound into logarithmic spiral arms, bright with young blue stars and pink star-forming knots, dark dust lanes
            // along the arms' inner edges, mottled with star clouds. No arms and a big bulge: an elliptical; warp: irregular.
            void Galaxy(float3 x, out float3 emit, out float absorb)
            {
                emit = 0.0;
                absorb = 0.0;
                float R = NebulaRadius;
                if (length(x) > 1.25 * R) return;
                float unusedR;
                float3 pd = ToDiskFrame(x, unusedR);
                float irregular = _KSPVE_ObjCloud.z;
                if (irregular > 0.0)
                    pd.xy += (float2(Fbm(pd * 0.15 + 1.7), Fbm(pd * 0.15 + 9.2)) - 0.5) * (4.0 * irregular);
                float r = length(pd.xy), z = pd.z;
                float hz = max(_KSPVE_BHDisk.z, 1e-3) * R;
                // Bulge (Sersic n = 2), stretched along x into the bar.
                float rb = max(_KSPVE_ObjGalaxy.x, 1e-3) * R;
                float qb = length(float3(pd.x / (1.0 + _KSPVE_ObjGalaxy.z * R / rb), pd.y, pd.z * 1.6)) / rb;
                float bulge = 0.25 * exp(-3.67 * (sqrt(qb) - 1.0)) / rb * (1.0 - smoothstep(0.85 * R, 1.2 * R, length(x)));
                // Disk: exponential in radius and height, fading at the edge.
                float disk = exp(-r / (0.3 * R)) * exp(-abs(z) / hz) * (1.0 - smoothstep(0.85 * R, 1.2 * R, r)) / hz;
                // Spiral arms, from the bar's ends.
                float arms = _KSPVE_ObjSpiral.x;
                float theta = atan2(pd.y, pd.x);
                float lr = log(max(r, 0.02 * R) / R);
                float armMix = 0.0, lane = 0.0;
                if (arms > 0.0)
                {
                    float phase = arms * (theta - _KSPVE_ObjSpiral.z * lr) + 1.5 * (Fbm(pd * 0.5 + 4.4) - 0.5);
                    float wave = 0.5 + 0.5 * cos(phase);
                    float wave2 = wave * wave;
                    armMix = _KSPVE_ObjSpiral.y * wave2 * wave2 * wave * smoothstep(0.03 * R, 0.25 * R, r);
                    float dustWave = 0.5 + 0.5 * cos(phase + 0.7);   // trailing just inside each arm
                    lane = dustWave * dustWave * dustWave * dustWave;
                }
                float clumpy = _KSPVE_ObjDisk2.z;
                float mottle = max(1.0 - 0.5 * clumpy + clumpy * Fbm(pd * 1.8 + 2.2), 0.0);
                // Star-forming knots: along the arms, or scattered through an irregular galaxy.
                float knots = smoothstep(0.6, 0.76, Fbm(pd * 3.0 + 6.1)) * (arms > 0.0 ? armMix : 0.5 * irregular) * _KSPVE_ObjGalaxy.w;
                // Between the arms only old stars; in them young blue ones; the dust lanes dim what is in them.
                float interArm = 1.0 - 0.75 * _KSPVE_ObjSpiral.y * (arms > 0.0 ? 1.0 : 0.0);
                float3 diskLight = _KSPVE_BHTintOuter.rgb * interArm + _KSPVE_ObjColorA.rgb * (3.0 * armMix);
                float dimmed = 1.0 - min(0.7 * lane * _KSPVE_ObjCloud.x, 0.9);
                emit = 0.5 * (_KSPVE_BHTintInner.rgb * (bulge * _KSPVE_ObjGalaxy.y) + disk * mottle * dimmed * (diskLight + _KSPVE_ObjColorB.rgb * (8.0 * knots)));
                // Dust: in a thinner layer, in lanes along the arms (or smooth with none).
                float dustLayer = exp(-abs(z) / (0.5 * hz)) * exp(-r / (0.4 * R)) * (1.0 - smoothstep(0.8 * R, 1.1 * R, r));
                absorb = _KSPVE_ObjCloud.x * dustLayer * (arms > 0.0 ? 0.25 + lane : 0.6) * (0.5 + Fbm(pd * 2.5 + 7.7)) * 8.0 / hz;
            }

            // A frame round the neutron star's magnetic axis.
            void MagneticAxes(out float3 m, out float3 e1, out float3 e2)
            {
                m = _KSPVE_BHJetAxis.xyz;
                e1 = normalize(cross(m, abs(m.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
                e2 = cross(m, e1);
            }

            // Dipole field lines: glowing loops r = L sin^2(theta) about the magnetic axis, on a few shells and meridians.
            void AccumulateFieldLines(float3 x, float dt, float fp, inout float3 color, float transmittance)
            {
                float strength = _KSPVE_ObjNeutron.x;
                float starR = _KSPVE_ObjKind.y;
                float r = length(x);
                if (strength <= 0.0 || starR <= 0.0 || r < starR) return;
                float3 m, e1, e2;
                MagneticAxes(m, e1, e2);
                float c = dot(x, m) / r;
                float s2 = max(1.0 - c * c, 1e-4);
                float L = r / s2;
                float l0 = starR * FieldShellStart;
                float k = round(log(L / l0) / log(FieldShellRatio));
                if (k < 0.0 || k > FieldShells - 1.0) return;
                float lk = l0 * pow(FieldShellRatio, k);
                float spacing = 2.0 * UNITY_PI / max(_KSPVE_ObjField.x, 1.0);
                float phi = atan2(dot(x, e2), dot(x, e1)) + k * 0.4;
                float dphi = phi - round(phi / spacing) * spacing;
                float dr = r - lk * s2;
                float dperp = r * sqrt(s2) * dphi;
                float w = max(_KSPVE_ObjField.w * r, fp);
                float glow = exp(-(dr * dr + dperp * dperp) / (w * w)) * strength / (1.0 + 0.3 * k) / w;
                color += transmittance * _KSPVE_ObjColorA.rgb * (glow * 0.06 * dt);
            }

            // Accretion columns: gas lifted off the disk's inner edge along the field lines and onto the magnetic poles.
            void AccumulateColumns(float3 x, float dt, inout float3 color, inout float transmittance)
            {
                float strength = _KSPVE_ObjNeutron.y;
                float r = length(x);
                float lm = _KSPVE_BHDisk.x;
                if (strength <= 0.0 || r < _KSPVE_ObjKind.y || r > lm * 1.1) return;
                float3 m, e1, e2;
                MagneticAxes(m, e1, e2);
                float c = dot(x, m) / r;
                float s2 = max(1.0 - c * c, 1e-4);
                float dl = (r / s2 - lm) / (_KSPVE_ObjNeutron2.x * lm);
                float curtain = exp(-dl * dl);
                if (curtain < 1e-3) return;
                float phi = atan2(dot(x, e2), dot(x, e1));
                float streams = 0.25 + 1.5 * Sq(Noise3(float3(cos(phi) * 2.5, sin(phi) * 2.5, r * 0.3 - _KSPVE_BHLook.w * 2.0)));
                float heat = pow(lm / r, 1.5);
                float gas = curtain * streams * strength;
                color += transmittance * Blackbody(25000.0 * min(heat, 3.0)) * (gas * heat * 0.6 * dt);
                transmittance *= exp(-gas * 0.15 * dt);
            }

            // The pulsar's beams: two narrow cones of light along the magnetic axis.
            void AccumulateBeams(float3 x, float dt, inout float3 color, float transmittance)
            {
                float strength = _KSPVE_ObjNeutron.z;
                if (strength <= 0.0) return;
                float3 m = _KSPVE_BHJetAxis.xyz;
                float z = dot(x, m), az = abs(z);
                if (az < _KSPVE_ObjKind.y) return;
                float perp = length(x - z * m);
                float rad = 0.25 + _KSPVE_ObjNeutron2.y * az;
                float beam = exp(-Sq(perp / rad)) * exp(-az / _KSPVE_ObjNeutron.w) / rad;
                color += transmittance * lerp(_KSPVE_BHJetColor.rgb, 1.0, 0.55) * (beam * strength * 0.8 * dt);
            }

            float4 fragObject(v2f i) : SV_Target
            {
                float2 ndc = i.ndc;
                float2 uv = ndc * 0.5 + 0.5;
                float3 forward = _KSPVE_BHForward.xyz;
                float3 v = normalize(forward + _KSPVE_BHRight.xyz * ndc.x * _KSPVE_BHRay.x + _KSPVE_BHUp.xyz * ndc.y * _KSPVE_BHRay.y);

                float unit = _KSPVE_BHCenter.w;
                float3 x = -_KSPVE_BHCenter.xyz / unit;   // camera position relative to the object, its units
                float bound = _KSPVE_BHRay.w;
                float b = dot(x, v);
                float c = dot(x, x) - bound * bound;
                float disc = b * b - c;
                if (disc < 0.0) return 0;
                float sq = sqrt(disc);
                float tEnter = max(0.0, -b - sq);
                if (-b + sq < 0.0) return 0;

                // Anything nearer hides it: bodies as exact spheres (its own star among them), and the scaled scene's depth.
                float sceneDistance = 1e30;
                if (_KSPVE_BHOptions.x > 0.5)
                    sceneDistance = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv)) / max(dot(v, forward), 1e-4) / unit;
                [unroll] for (int o = 0; o < BH_OCCLUDERS; o++)
                {
                    float4 body = _KSPVE_BHOccluders[o];
                    if (body.w <= 0.0) continue;
                    float bo = dot(body.xyz, v);
                    float co = dot(body.xyz, body.xyz) - body.w * body.w;
                    float disco = bo * bo - co;
                    if (co < 0.0) sceneDistance = 0.0;
                    else if (bo > 0.0 && disco > 0.0) sceneDistance = min(sceneDistance, bo - sqrt(disco));
                }
                if (sceneDistance < tEnter) return 0;

                x += v * tEnter;
                float traveled = tEnter;
                float3 hv = cross(x, v);
                float h2 = dot(hv, hv);
                int kind = ObjKind();
                bool neutron = kind == KIND_NEUTRON;
                // Bent by a neutron star's own gravity, or by a black hole on the same body (x'' = -1.5 h^2 x r_s / r^5 with
                // r_s its horizon in these units); otherwise straight. Round a hole the rays end at its horizon (starR).
                float hole = _KSPVE_ObjExtra.z;
                float bend = neutron ? 1.0 : hole;
                float starR = _KSPVE_ObjKind.y;
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y, thick = _KSPVE_BHDisk.z;
                float3 color = 0;
                float transmittance = 1.0;
                bool solid = false;
                // Round a black hole, only the gas in front of its disk and jets may dim them: once the ray reaches that
                // region, the share of the hole's light let through is kept (gas beyond it still glows, but hides nothing).
                float holeReach = _KSPVE_ObjExtra.w;
                bool reachedHole = false;
                float frontTransmittance = 1.0;
                int maxSteps = (int)_KSPVE_BHRay.z;
                float jitter = 0.2 + 0.8 * frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                [loop] for (int k = 0; k < maxSteps; k++)
                {
                    float r = length(x);
                    if (r > bound * 1.001 && dot(x, v) > 0.0) break;
                    if (!reachedHole && r < holeReach) { reachedHole = true; frontTransmittance = transmittance; }
                    if (traveled > sceneDistance) break;
                    if (r < starR)
                    {
                        if (neutron)
                        {
                            // The neutron star's surface, seen through its own lensing: hot, brightest at the magnetic poles.
                            float3 nrm = x / r, vn0 = normalize(v);
                            float pole = exp(-(1.0 - abs(dot(nrm, _KSPVE_BHJetAxis.xyz))) * 30.0);
                            float limb = 0.55 + 0.45 * abs(dot(nrm, vn0));
                            color += transmittance * (SaturateBy(Blackbody(_KSPVE_ObjKind.w), 1.3) * limb + Blackbody(_KSPVE_ObjKind.w * 3.0) * (pole * _KSPVE_ObjNeutron2.z));
                            solid = true;
                        }
                        break;   // another star: the game draws its surface
                    }
                    float nearPlane = abs(dot(x, _KSPVE_BHNormal.xyz));
                    float dt = min(0.08 * r, neutron ? 2.0 : outer / 20.0);
                    float3 vn = normalize(v);
                    float fp = traveled * _KSPVE_BHOptions.y;
                    if (neutron)
                    {
                        if (nearPlane < 5.0 * thick * r && r < outer * 1.2) dt = min(dt, 0.2);
                        dt = min(dt, max(0.05 * r, 0.05));
                    }
                    else if (kind == KIND_NEBULA)
                        dt = r < 1.2 * NebulaRadius ? 0.02 * NebulaRadius : max(0.025 * NebulaRadius, r - 1.15 * NebulaRadius);
                    else if (kind == KIND_COMET)
                        dt = min(max(0.15 * r, 0.03), max(_KSPVE_ObjComet.z, _KSPVE_ObjComet.w) / 30.0);
                    else if (kind == KIND_GALAXY)
                    {
                        // Fine steps through the thin disk and the bulge, long ones above and below.
                        float hz = max(_KSPVE_BHDisk.z, 1e-3) * NebulaRadius;
                        dt = nearPlane > 4.0 * hz ? clamp(nearPlane - 3.0 * hz, 0.3 * hz, 0.12 * NebulaRadius) : max(0.3 * hz, 0.004 * NebulaRadius);
                        dt = min(dt, max(0.15 * _KSPVE_ObjGalaxy.x * NebulaRadius, 0.25 * max(r - _KSPVE_ObjGalaxy.x * NebulaRadius, 0.0)));
                    }
                    else if (nearPlane < 6.0 * thick * r && r < outer * 1.4 && r > inner * 0.5)
                        dt = min(dt, max(0.02 * r, _KSPVE_ObjDust.w * 0.7));   // dusty disks: fine steps through the layer
                    if (!neutron && _KSPVE_ObjNeutron.x > 0.0 && r < starR * FieldShellStart * pow(FieldShellRatio, FieldShells - 1.0) * 1.1)
                        dt = min(dt, max(0.05 * r, 0.05 * starR));   // and through the field lines
                    if (hole > 0.0 && !neutron) dt = min(dt, max(0.08 * r, 0.02 * hole));   // short steps where the hole bends the rays
                    if (k == 0) dt *= jitter;
                    dt = min(dt, max(sceneDistance - traveled, 1e-5));

                    if (neutron)
                    {
                        AccumulateDisk(x, v, dt, traveled, color, transmittance);
                        AccumulateColumns(x, dt, color, transmittance);
                        AccumulateFieldLines(x, dt, fp, color, transmittance);
                        AccumulateBeams(x, dt, color, transmittance);
                    }
                    else if (kind == KIND_NEBULA)
                    {
                        float gas, mix, filament, dust;
                        Nebula(x, gas, mix, filament, dust);
                        float3 glowGas = lerp(_KSPVE_ObjColorA.rgb, _KSPVE_ObjColorB.rgb, mix) * gas + _KSPVE_ObjColorA.rgb * (filament * 0.8);
                        color += transmittance * glowGas * dt;
                        transmittance *= exp(-(gas * 0.12 + dust * 1.6) * dt);
                        AccumulateStarGlow(x, dt, color, transmittance);   // a star inside, lighting it
                    }
                    else if (kind == KIND_COMET)
                        AccumulateComet(x, dt, fp, color, transmittance);
                    else if (kind == KIND_GALAXY)
                    {
                        float3 emit;
                        float absorb;
                        Galaxy(x, emit, absorb);
                        color += transmittance * emit * dt;
                        transmittance *= exp(-absorb * dt);
                    }
                    else
                    {
                        AccumulateDustDisk(x, vn, dt, color, transmittance);
                        if (kind == KIND_PROTO) AccumulateStarGlow(x, dt, color, transmittance);
                        else AccumulateRocks(x, x + v * dt, traveled, color, transmittance);
                    }
                    if (!neutron) AccumulateFieldLines(x, dt, fp, color, transmittance);   // any star may have them
                    if (transmittance < 0.01) break;

                    // Midpoint (RK2) step of x'' = -1.5 h^2 x / r^5 (straight, and no 1/r^5 at all, unless it bends).
                    if (bend > 0.0)
                    {
                        float3 a1 = -1.5 * bend * h2 * x / pow(max(r, 1e-6), 5.0);
                        float3 xm = x + v * (0.5 * dt);
                        float3 vm = v + a1 * (0.5 * dt);
                        float rm = max(length(xm), 1e-3);
                        float3 am = -1.5 * bend * h2 * xm / pow(rm, 5.0);
                        x += vm * dt;
                        v += am * dt;
                        traveled += dt * length(vm);
                    }
                    else
                    {
                        x += v * dt;
                        traveled += dt;
                    }
                }
                color = LinearToSrgb(1.0 - exp(-color * _KSPVE_BHLook.y));
                return float4(color, solid ? 1.0 : 1.0 - (reachedHole ? frontTransmittance : transmittance));
            }

            // Upscales the reduced-resolution march. The buffer was written with the same clip-space y as the camera
            // target, so its rows run the other way when the projection is not flipped.
            float4 fragComposite(v2f i) : SV_Target
            {
                float2 uv = float2(i.ndc.x * 0.5 + 0.5, 0.5 - 0.5 * i.ndc.y * _ProjectionParams.x);
                return tex2D(_KSPVE_BHBuffer, uv);
            }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass // 0: ray-march (premultiplied)
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragMarch
            ENDCG
        }

        Pass // 1: composite the reduced-resolution buffer (premultiplied)
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }

        Pass // 2: ray-march another kind of object (premultiplied)
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment fragObject
            ENDCG
        }
    }
    Fallback Off
}
