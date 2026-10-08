Shader "Hidden/KSPVE/Space/BlackHole"
{
    // Relativistic black hole with a thick, puffy, self-shadowed volumetric accretion disk that spins as a whole,
    // with sparks and debris riding in it (catalog #80, #81). A tidal disruption event, when a save runs one, is drawn in
    // the same march (TdeSample).
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
            float4 _KSPVE_BHMask;         // a forming disk fills only this band (horizon radii): x inner, y outer, z edge width (0 = all of it)

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

            static const float Absorb = 0.35;           // extinction per unit density along the view (low: the disk is a bit translucent)
            static const float ShadowAbsorb = 0.9;      // extinction towards the inner disk (keeps the puffy self-shadowing)
            static const float LightStrength = 3.0;     // light from the inner disk that lights the bulges
            static const float DensityScale = 45.0;     // disk density (divided by radius)
            static const float CloudScale = 0.45;       // puffy lumps per horizon radius
            static const float EmissionStrength = 3.6;  // the plasma's own thermal glow
            static const float ParticleCell = 0.5;      // horizon radii per particle cell
            static const float JetBase = 12.0;          // spiral jets: radius ~ (z + JetBase)^0.55, thick from the base (BlackHoleSettings.JetBase)

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
            float3 Saturate(float3 c)
            {
                float l = dot(c, float3(0.2126, 0.7152, 0.0722));
                return max(l + (c - l) * _KSPVE_BHExtra.x, 0.0);
            }

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
            // pw: the point's warped position in the rotating frame, for the hot clumps.
            float DiskShape(float3 x, out float r, out float3 pw)
            {
                pw = 0.0;
                float3 pd = ToDiskFrame(x, r);
                float height = pd.z;
                float inner = _KSPVE_BHDisk.x, outer = _KSPVE_BHDisk.y;
                if (r < inner * 0.8 || r > outer * 1.1) return 0.0;
                float ht = _KSPVE_BHDisk.z * r;
                if (abs(height) > 3.0 * ht) return 0.0;
                float outerFade = 1.0 - smoothstep(outer * 0.35, outer, r); // outer disk thins into translucent wisps
                float radial = smoothstep(inner * 0.85, inner * 1.1, r) * outerFade * outerFade;
                if (_KSPVE_BHMask.z > 0.0)
                    radial *= smoothstep(_KSPVE_BHMask.x - _KSPVE_BHMask.z, _KSPVE_BHMask.x + _KSPVE_BHMask.z, r)
                        * (1.0 - smoothstep(_KSPVE_BHMask.y - _KSPVE_BHMask.z, _KSPVE_BHMask.y + _KSPVE_BHMask.z, r));
                if (radial <= 0.0) return 0.0;
                float turbulence = _KSPVE_BHDisk.w;

                // True 3D position in the rotating frame, with light turbulent offsets.
                float3 pc = pd * CloudScale;
                float3 w = float3(Noise3(pc * 0.5 + 11.3), Noise3(pc * 0.5 + 27.1), Noise3(pc * 0.5 + 41.7)) - 0.5;
                pw = pc + w * 0.5;

                // Puffy, irregular surface: big rounded lumps with smaller billows on them bulge the disk's half-height.
                float warp = (Noise3(pc * 0.3 + 3.3) - 0.5) * 0.8;
                float lumps = Fbm(pw);
                float billows = 1.0 - abs(2.0 * Noise3(pw * 2.7 + 5.1) - 1.0);
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
                if (az < start || az > len) return;
                float3 perp = x - z * n;
                float rho = length(perp);
                float R = width * pow(az + JetBase * spiralAmount, 0.55);
                if (rho > 3.0 * R) return;
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

                // Relativistic beaming: the gas moves away from the hole along the axis.
                float cosT = dot(n * sign(z), -normalize(v));
                float gamma = rsqrt(1.0 - speed * speed);
                float boost = gamma * (1.0 - speed * cosT);
                float beam = min(1.0 / (boost * boost * boost * gamma * gamma * gamma), 10.0);   // D^3 / gamma^3 (D = 1 / boost), capped

                float3 jetColor = lerp(float3(0.3, 0.55, 1.0), 1.0, clamp(0.4 * density * beam / 8.0, 0.0, 0.35));
                color += transmittance * jetColor * (strength * 2.5 * density * beam * dt);
                transmittance *= exp(-absorb * gas * dt / R);
            }

            void AccumulateDisk(float3 x, float3 v, float dt, float traveled, inout float3 color, inout float transmittance)
            {
                float r;
                float3 pw;
                float shape = DiskShape(x, r, pw);
                if (shape < 1e-3) return;
                float heat = Fbm(pw * 1.3 + 9.1);
                float density = shape * (0.6 + 0.4 * heat); // hot clumps inside a continuous body: no holes

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

                // Relativistic shift: gravitational redshift times Doppler from the disk's orbital motion.
                float3 n = _KSPVE_BHNormal.xyz;
                float peak = _KSPVE_BHLook.x;
                float knots = smoothstep(0.45, 0.75, heat) * _KSPVE_BHDisk.w;
                float temperature = DiskTemperature(r, inner, peak) * (0.9 + 0.3 * knots);
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
                float3 hot = Saturate(Blackbody(temperature * g));
                float shade = 0.3 + 0.7 * lightTransmittance;
                float3 thermal = hot * (pow(temperature / peak, 3.0) * EmissionStrength * (0.6 + 1.4 * knots) * shade);
                float3 scattered = hot * (incident * lightTransmittance * phase * 0.4);
                color += transmittance * (thermal + scattered) * (_KSPVE_BHForm.y * beaming * density * dt);
                transmittance *= exp(-density * Absorb * dt);

                AccumulateParticles(x, x + v * dt, traveled, saturate(shape / 5.0), temperature, g, color, transmittance);
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
                    float dtCap = tde && r > _KSPVE_Tde.w ? 0.08 * r : 2.0;   // beyond the disk & jets the event may need long strides
                    float dt = clamp(min(0.08 * r, 0.35 * max(nearPlane, 0.3 * _KSPVE_BHDisk.z * r)), 0.004, dtCap);
                    if (nearPlane < 5.0 * _KSPVE_BHDisk.z * r && r < _KSPVE_BHDisk.y * 1.2) dt = min(dt, 0.2); // fine steps inside the disk layer
                    float alongJet = abs(dot(x, _KSPVE_BHJetAxis.xyz));
                    if (_KSPVE_BHJet.x > 0.0 && alongJet > 1.1 * (1.0 - _KSPVE_BHJet2.x) && alongJet < _KSPVE_BHJet.y)                    // and inside the jets
                    {
                        float jetR = _KSPVE_BHJet.z * pow(alongJet + JetBase * _KSPVE_BHJet2.x, 0.55);
                        if (length(x - dot(x, _KSPVE_BHJetAxis.xyz) * _KSPVE_BHJetAxis.xyz) < 3.0 * jetR) dt = min(dt, (0.35 - 0.15 * _KSPVE_BHJet2.x) * jetR);
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
                    color += transmittance * glowColor * (_KSPVE_BHExtra.y * 0.03 * exp(-(r - 1.0) / 2.2)) * dt;
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
                    color += float3(0.9, 0.95, 1.0) * (_KSPVE_BHExtra.z * 8.0 * exp(-d * d));
                }

                // KSP renders in gamma space, so write sRGB-encoded colour.
                color = LinearToSrgb(1.0 - exp(-color * _KSPVE_BHLook.y));
                float alpha = captured ? 1.0 : 1.0 - transmittance;
                return float4(color, alpha);
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
    }
    Fallback Off
}
