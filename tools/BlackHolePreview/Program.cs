// CPU port of unity/KSPVEShaders/Assets/Shaders/Space/BlackHole.shader, line for line, to preview the
// image without launching KSP.
// Usage: dotnet run -- <out.png> [width] [height] [inclinationDeg] [distance] [time]
//        dotnet run -- debug
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading.Tasks;

internal static partial class Program
{
    // Kcalbeloh defaults (KSPVE_BLACKHOLE_Kcalbeloh.cfg)
    const float Inner = 3f, Outer = 14f, Thickness = 0.12f, Turbulence = 0.8f, Spin = 1f, RotationSpeed = 1f;
    const float Ring = 1f;
    static readonly float Glow = EnvFloat("BH_GLOW", 1f);
    // Look, overridable for tuning: BH_PEAK (kelvin), BH_SAT (colour saturation) and BH_BRIGHT environment variables.
    static readonly float PeakK = EnvFloat("BH_PEAK", 40000f), Saturation = EnvFloat("BH_SAT", 3.4f), Brightness = EnvFloat("BH_BRIGHT", 3.5f);
    static float EnvFloat(string name, float fallback) =>
        float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    static int MaxSteps = 300;
    // Jets along the spin axis: strength (BH_JET, 0 = off), length (horizon radii), collimation (width = JetWidth * z^0.55),
    // gas speed (fraction of c) and the visual outward flow of the knots (horizon radii per second).
    static readonly float JetStrength = EnvFloat("BH_JET", 1.3f);
    static readonly float JetLength = EnvFloat("BH_JET_LEN", 60f), JetWidth = EnvFloat("BH_JET_W", 0.9f), JetSpeed = EnvFloat("BH_JET_V", 0.85f);
    // Spiral (0 = plain column, 1 = solid spiral core), turns of the swirl along the jet, outward speed of the pattern at
    // mid-jet (horizon radii per second) and how much the jet gas absorbs (optical depth across one jet radius per unit density).
    static readonly float JetSpiral = EnvFloat("BH_JET_SPIRAL", 1f), JetTurns = EnvFloat("BH_JET_TURNS", 4f);
    static readonly float JetFlowMid = EnvFloat("BH_JET_FLOW", 30f), JetAbsorb = EnvFloat("BH_JET_ABSORB", 0.6f);
    // The pattern lives in zeta = z^0.45, where the swirl's pitch keeps pace with the widening jet (radius ~ z^0.55), so
    // the swirl keeps its angle and the flow speeds up outward. Twist in radians per unit zeta; flow in zeta per second.
    static readonly float JetTwist = 2f * MathF.PI * JetTurns / (MathF.Pow(JetLength, 0.45f) - MathF.Pow(1.1f, 0.45f));
    static readonly float JetFlow = JetFlowMid * 0.45f / MathF.Pow(JetLength * 0.5f, 0.55f);
    // With the spiral the jet is a thick tube from its rounded base: its radius follows (z + JetBase)^0.55.
    const float JetBase = 12f;
    static readonly float BaseBound = MathF.Max(Outer * 1.25f + 8f, JetStrength > 0
        ? MathF.Sqrt(JetLength * JetLength + MathF.Pow(3f * JetWidth * MathF.Pow(JetLength + JetBase * JetSpiral, 0.55f), 2f)) + 4f : 0f);
    static float Bound = BaseBound;
    // The disk and jets as they are now (BlackHoles.StateOf): as configured, or as far as a tidal disruption has built them.
    static float DiskPresence = 1f, DiskBright = 1f, InnerNow = Inner, OuterNow = Outer, ThickNow = Thickness;
    static float MaskIn, MaskOut, MaskSoft;   // band a forming disk fills (MaskSoft 0 = all of it)
    static float JetNow = JetStrength, JetLengthNow = JetLength, DiskSpiral = 0f, JetHeads = 0f, LaunchFlash = 0f;
    static readonly Vector3 N = Vector3.UnitY; // disk normal in the preview frame
    static float Time = 0f;
    static float PixelAngle = 0.002f; // radians per pixel, so particles never shrink below a pixel
    const float ParticleAmount = 1f, DebrisAmount = 1f;

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "tde-info") return TdeInfo.Run(args);
        if (args.Length > 0 && args[0] == "tde") return RenderTde(args);
        if (args.Length > 0 && args[0] == "tde-svg") return TdeSvg.Run(args);
        if (args.Length > 0 && args[0] == "palette")
        {
            foreach (float k in new[] { 2500f, 3500f, 4500f, 6000f, 7500f, 9000f })
            {
                Vector3 c = Saturate(Blackbody(k));
                Console.Write($"{k,6}K lin=({c.X:F2},{c.Y:F2},{c.Z:F2}) srgb:");
                foreach (float e in new[] { 0.5f, 1f, 2f, 4f, 8f })
                {
                    Vector3 m = Tonemap(c * e);
                    Console.Write($" [{(int)(LinearToSrgb(m.X) * 255)},{(int)(LinearToSrgb(m.Y) * 255)},{(int)(LinearToSrgb(m.Z) * 255)}]");
                }
                Console.WriteLine();
            }
            return 0;
        }
        if (args.Length > 0 && args[0] == "debug")
        {
            Vector3 cam = new Vector3(0, MathF.Cos(80f * MathF.PI / 180f), -MathF.Sin(80f * MathF.PI / 180f)) * 32f;
            foreach (float side in new[] { 5f, -5f })
            {
                Vector3 d = Vector3.Normalize(new Vector3(side, 0, 0) - cam);
                Jitter = 0.5f;
                Vector4 c = Trace(cam, d);
                Console.WriteLine($"x={side,5}: color=({c.X:F3},{c.Y:F3},{c.Z:F3}) alpha={c.W:F3}");
            }
            return 0;
        }

        if (args.Length > 0 && args[0] == "anim")
        {
            // anim <prefix> w h inclination distance frames fps: numbered frames of the spinning disk.
            int frames = int.Parse(args[6]), fps = int.Parse(args[7]);
            for (int f = 0; f < frames; f++)
                Main(new[] { $"{args[1]}{f:D3}.png", args[2], args[3], args[4], args[5], (f / (float)fps).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return 0;
        }

        string path = args.Length > 0 ? args[0] : "bh-preview.png";
        int w = args.Length > 1 ? int.Parse(args[1]) : 640;
        int h = args.Length > 2 ? int.Parse(args[2]) : 360;
        float inclination = (args.Length > 3 ? float.Parse(args[3]) : 80f) * MathF.PI / 180f;
        float distance = args.Length > 4 ? float.Parse(args[4]) : 32f;
        Time = args.Length > 5 ? float.Parse(args[5]) : 0f;

        // Camera looking at the hole from 'distance' horizon radii, 'inclination' from the disk normal.
        Vector3 camPos = new Vector3(0, MathF.Cos(inclination), -MathF.Sin(inclination)) * distance;
        Vector3 forward = Vector3.Normalize(-camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        if (float.IsNaN(right.X)) right = Vector3.UnitX;
        Vector3 up = Vector3.Cross(forward, right);
        float tanY = MathF.Tan(EnvFloat("BH_FOV", 60f) * 0.5f * MathF.PI / 180f), tanX = tanY * w / h;   // vertical field of view
        PixelAngle = 2f * tanY / h;

        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w, v = 1f - (py + 0.5f) / h;
                Vector3 dir = Vector3.Normalize(forward + right * (u * 2 - 1) * tanX + up * (v * 2 - 1) * tanY);
                Jitter = 0.2f + 0.8f * Frac(MathF.Sin(px * 12.9898f + py * 78.233f) * 43758.5453f);
                Vector4 c = Trace(camPos, dir);
                // The shader writes sRGB-encoded colour premultiplied over the scene; composite over black here.
                int i = (py * w + px) * 3;
                rgb[i] = ToByte(c.X); rgb[i + 1] = ToByte(c.Y); rgb[i + 2] = ToByte(c.Z);
            }
        });
        WritePng(path, w, h, rgb);
        Console.WriteLine($"wrote {path} ({w}x{h}, inclination {inclination * 180 / MathF.PI:F0} deg, distance {distance} r_s)");
        return 0;
    }

    static byte ToByte(float encoded) => (byte)Math.Clamp((int)(encoded * 255f + 0.5f), 0, 255);

    static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    [ThreadStatic] static float Jitter;
    [ThreadStatic] static int LastSteps;
    [ThreadStatic] static string LastExit;
    [ThreadStatic] static System.Text.StringBuilder RayLog;

    static Vector4 Trace(Vector3 x, Vector3 v)
    {
        float b = Vector3.Dot(x, v), c = Vector3.Dot(x, x) - Bound * Bound, disc = b * b - c;
        if (disc < 0) return SkyOnly(v);
        float s = MathF.Sqrt(disc), tEnter = MathF.Max(0, -b - s);
        if (-b + s < 0) return SkyOnly(v);
        x += v * tEnter;
        float traveled = tEnter;
        Vector3 hv = Vector3.Cross(x, v);
        float h2 = Vector3.Dot(hv, hv);
        Vector3 color = Vector3.Zero;
        float transmittance = 1f;
        float envDepth = 0f;   // the outflow's haze passed through so far (capped: TdeRender.EnvelopeMaxDepth)
        bool captured = false;
        float rMin = 1e9f;
        Vector3 hazeColor = Saturate(Blackbody(PeakK * 0.6f));
        LastExit = "steps";
        for (int k = 0; k < MaxSteps; k++)
        {
            LastSteps = k;
            float r = x.Length();
            rMin = MathF.Min(rMin, r);
            if (r < 1f) { captured = true; LastExit = "captured"; break; }
            if (r > Bound * 1.001f && Vector3.Dot(x, v) > 0) { LastExit = "escaped"; break; }
            float nearPlane = MathF.Abs(Vector3.Dot(x, N));
            float dtCap = TdeOn && r > TdeCore ? 0.08f * r : 2f;   // beyond the disk & jets the event may need long strides
            float dt = Math.Clamp(MathF.Min(0.08f * r, 0.35f * MathF.Max(nearPlane, 0.3f * Thickness * r)), 0.004f, dtCap);
            if (nearPlane < 5f * ThickNow * r && r < OuterNow * 1.2f) dt = MathF.Min(dt, 0.2f); // fine steps inside the cloud layer
            if (JetNow > 0 && nearPlane > 1.1f * (1f - JetSpiral) && nearPlane < JetLengthNow)                       // and inside the jets
            {
                float jetR = JetWidth * MathF.Pow(nearPlane + JetBase * JetSpiral, 0.55f);
                if ((x - Vector3.Dot(x, N) * N).Length() < 3f * jetR) dt = MathF.Min(dt, (0.35f - 0.15f * JetSpiral) * jetR);
            }
            float tdeK = 0f, envK = 0f; Vector3 tdeEmit = Vector3.Zero, tdeGlow = Vector3.Zero, envEmit = Vector3.Zero;
            if (TdeOn) dt = MathF.Min(dt, MathF.Max(TdeSample(x, Vector3.Normalize(v), traveled, out tdeK, out tdeEmit, out tdeGlow, out envK, out envEmit), 0.0005f));
            if (k == 0) dt *= Jitter;
            if (envK > 0f)
            {
                float ke = MathF.Min(envK, MathF.Max(EnvelopeMaxDepth - envDepth, 0f) / dt);
                envDepth += ke * dt;
                tdeK += ke;
                tdeEmit += envEmit * (ke / envK);
            }
            RayLog?.AppendLine($"{k}: r={r:0.0} dt={dt:0.###} tdeK={tdeK:0.###}");

            // Glow: hot, thin plasma filling the space around the hole (lensed along with everything else).
            color += transmittance * hazeColor * (Glow * DiskPresence * (1f + 3f * LaunchFlash) * 0.03f * MathF.Exp(-(r - 1f) / 2.2f)) * dt;
            AccumulateDisk(MainDisk(), x, v, dt, traveled, ref color, ref transmittance);
            AccumulateJets(x, v, dt, ref color, ref transmittance);
            if (tdeK > 0f || tdeGlow != Vector3.Zero)
            {
                // Exact for a uniform slab: the event's gas is often far too opaque for emission * dt.
                float a = 1f - MathF.Exp(-tdeK * dt);
                color += transmittance * (tdeEmit / MathF.Max(tdeK, 1e-12f) * a + tdeGlow * dt);
                transmittance *= 1f - a;
            }
            if (transmittance < 0.01f) { LastExit = "opaque"; break; }

            Vector3 a1 = -1.5f * h2 * x / MathF.Pow(r, 5);
            Vector3 xm = x + v * (0.5f * dt), vm = v + a1 * (0.5f * dt);
            float rm = MathF.Max(xm.Length(), 1e-3f);
            Vector3 am = -1.5f * h2 * xm / MathF.Pow(rm, 5);
            x += vm * dt; v += am * dt;
            traveled += dt * vm.Length();
        }

        // Photon ring: rays that skim the photon sphere (r = 1.5) before escaping pile up light from every
        // orbit around the hole, giving a razor-thin bright ring at the edge of the shadow.
        if (!captured)
        {
            float d = (rMin - 1.5f) / 0.045f;
            color += new Vector3(0.9f, 0.95f, 1f) * (Ring * 8f * MathF.Exp(-d * d));
        }

        // The sky behind, seen along the bent ray: in the game this is KSP's skybox (lensed by Singularity).
        if (SkyOn && !captured) color += transmittance * StarSky(Vector3.Normalize(v)) / Brightness;
        color = Tonemap(color * Brightness);
        color = new Vector3(LinearToSrgb(color.X), LinearToSrgb(color.Y), LinearToSrgb(color.Z));
        return new Vector4(color, captured ? 1f : 1f - transmittance);
    }

    const float Absorb = 0.35f, ShadowAbsorb = 0.9f, LightStrength = 3f, DensityScale = 45f, CloudScale = 0.45f, EmissionStrength = 3.6f;

    /// <summary>The whole disk turns together at the orbital rate of its inner edge (rad/s): the outer disk is
    /// sped up to match, so nothing shears into swirls.</summary>
    static float SpinRate => RotationSpeed * 0.75f; // once every ~8 s at rotationSpeed 1

    /// <summary>
    /// A cloudy, self-shadowed disk (the hole's own, MainDisk). Ecc makes it eccentric about the hole (circles in r + e (q . Peri)); Cloud sets the clump size.
    /// </summary>
    struct DiskP
    {
        public Vector3 N, T, B, Peri;
        public float Spin, Inner, Outer, Thick, Turb, Angle, Peak, Presence, Bright, Cloud, Ecc, Particles, Debris, Cell, Spiral;
        public float MaskIn, MaskOut, MaskSoft;
    }

    static DiskP MainDisk()
    {
        Vector3 t = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        return new DiskP
        {
            N = N, T = t, B = Vector3.Cross(N, t), Peri = t, Spin = Spin, Inner = InnerNow, Outer = OuterNow, Thick = ThickNow,
            Turb = Turbulence, Angle = Spin * SpinRate * Time, Peak = PeakK, Presence = DiskPresence, Bright = DiskBright, Cloud = CloudScale,
            Ecc = 0f, Particles = ParticleAmount * DiskPresence, Debris = DebrisAmount * DiskPresence, Cell = ParticleCell, Spiral = DiskSpiral,
            MaskIn = MaskIn, MaskOut = MaskOut, MaskSoft = MaskSoft
        };
    }

    /// <summary>Shape of the disk at x (horizon radii), without the hot clumps. The light march uses this alone;
    /// pw is the point's position in the disk's rotating frame, for the hot clumps; re its (eccentric) radius.</summary>
    static float DiskShape(in DiskP d, Vector3 x, out float re, out Vector3 pw)
    {
        pw = Vector3.Zero;
        float height = Vector3.Dot(x, d.N);
        Vector3 q = x - height * d.N;
        float r = q.Length();
        re = r + d.Ecc * Vector3.Dot(q, d.Peri);
        if (re < d.Inner * 0.8f || re > d.Outer * 1.1f) return 0f;
        float ht = d.Thick * re;
        if (MathF.Abs(height) > 3f * ht) return 0f;
        float outerFade = 1 - SmoothStep(d.Outer * 0.35f, d.Outer, re); // outer disk thins into translucent wisps
        float radial = SmoothStep(d.Inner * 0.85f, d.Inner * 1.1f, re) * outerFade * outerFade;
        if (d.MaskSoft > 0f)
            radial *= SmoothStep(d.MaskIn - d.MaskSoft, d.MaskIn + d.MaskSoft, re) * (1f - SmoothStep(d.MaskOut - d.MaskSoft, d.MaskOut + d.MaskSoft, re));
        if (radial <= 0) return 0f;

        float phi = MathF.Atan2(Vector3.Dot(q, d.B), Vector3.Dot(q, d.T));
        float angle = phi - d.Angle;

        // True 3D position in the rotating frame, with light turbulent offsets.
        Vector3 pc = new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), height) * d.Cloud;
        Vector3 w = new Vector3(Noise3(pc * 0.5f + new Vector3(11.3f)), Noise3(pc * 0.5f + new Vector3(27.1f)), Noise3(pc * 0.5f + new Vector3(41.7f))) - new Vector3(0.5f);
        pw = pc + w * 0.5f;

        // Puffy, irregular surface: big rounded lumps with smaller billows on them bulge the disk's half-height.
        float warp = (Noise3(pc * 0.3f + new Vector3(3.3f)) - 0.5f) * 0.8f;
        float lumps = Fbm(pw);
        float billows = 1f - MathF.Abs(2f * Noise3(pw * 2.7f + new Vector3(5.1f)) - 1f);
        float surface = 0.5f + d.Turb * (1.3f * lumps + 0.3f * billows);
        float hAbs = MathF.Abs(height / ht - warp);
        float body = 1f - SmoothStep(surface - 0.3f, surface, hAbs); // 1 inside, rounded edge at the surface
        if (d.Spiral > 0f)
        {
            // While it forms, two spiral shocks wind through it (gas still settling onto circular orbits).
            float arms = 0.5f + 0.5f * MathF.Cos(2f * angle - 5f * MathF.Log(re / d.Inner + 1f) + 1.3f * lumps);
            body *= MathF.Max(1f + d.Spiral * (1.8f * arms - 0.9f), 0f);
        }

        return body * radial * (DensityScale / re) * d.Presence;
    }

    static void AccumulateDisk(in DiskP d, Vector3 x, Vector3 v, float dt, float traveled, ref Vector3 color, ref float transmittance)
    {
        float shape = DiskShape(d, x, out float r, out Vector3 pw);
        if (shape < 1e-3f) return;
        float heat = Fbm(pw * 1.3f + new Vector3(9.1f));
        float density = shape * (0.6f + 0.4f * heat); // hot clumps inside a continuous body: no holes

        // Self-shadowing towards the hot inner disk: lit flanks face the hole, crevices and far sides fall dark.
        // Two shape-only samples (average clump density) keep this cheap; they reach as far as the clumps are big.
        Vector3 toHole = -Vector3.Normalize(x);
        float reach = CloudScale / d.Cloud;
        float d1 = DiskShape(d, x + toHole * (0.4f * reach), out _, out _);
        float d2 = DiskShape(d, x + toHole * (1.2f * reach), out _, out _);
        float lightTransmittance = MathF.Exp(-(d1 * 0.55f + d2 * 0.85f) * 0.8f * ShadowAbsorb * reach);

        Vector3 vn = Vector3.Normalize(v);
        float cosScatter = Vector3.Dot(-toHole, -vn);
        const float hgG = 0.35f;
        float phase = 0.25f + 0.75f * (1 - hgG * hgG) / MathF.Pow(1 + hgG * hgG - 2 * hgG * cosScatter, 1.5f);
        float incident = LightStrength * MathF.Pow(d.Inner / MathF.Max(r, d.Inner), 2f);

        float knots = SmoothStep(0.45f, 0.75f, heat) * d.Turb;
        float temperature = DiskTemperature(r, d.Inner, d.Peak) * (0.9f + 0.3f * knots);
        Vector3 orbit = Vector3.Normalize(Vector3.Cross(d.N, x - Vector3.Dot(x, d.N) * d.N)) * d.Spin;
        float beta = MathF.Min(MathF.Sqrt(0.5f / MathF.Max(r - 1f, 0.05f)), 0.95f);
        float gamma = 1f / MathF.Sqrt(1 - beta * beta);
        float doppler = 1f / (gamma * (1 - beta * Vector3.Dot(orbit, -vn)));
        float grav = MathF.Sqrt(Math.Clamp(1 - 1f / x.Length(), 0f, 1f));
        float g = doppler * grav;
        float beaming = g * g * g;

        // Hot plasma in its own blackbody colour at the Doppler-shifted temperature: white-hot where it races towards
        // you, then yellow, gold and deep orange further out. Brighter in the hot clumps, darker in the shadowed
        // crevices, plus light from the inner disk scattered off the bulges.
        Vector3 hot = Saturate(Blackbody(temperature * g));
        float shade = 0.3f + 0.7f * lightTransmittance;
        Vector3 thermal = hot * (MathF.Pow(temperature / d.Peak, 3) * EmissionStrength * (0.6f + 1.4f * knots) * shade);
        Vector3 scattered = hot * (incident * lightTransmittance * phase * 0.4f);
        color += transmittance * (thermal + scattered) * (d.Bright * beaming * density * dt);
        transmittance *= MathF.Exp(-density * Absorb * dt);

        AccumulateParticles(d, x, x + v * dt, traveled, Math.Clamp(shape / 5f, 0f, 1f), temperature, g, ref color, ref transmittance);
    }

    /// <summary>
    /// Relativistic jets along the spin axis: a collimated column (width grows as z^0.55). With JetSpiral it is a solid,
    /// bright core whose surface is streaked by bands of gas winding round it, and the whole pattern streams outward (and
    /// so appears to spin). The gas glows blue-white and absorbs, so the core looks solid and the far side stays hidden.
    /// Doppler beaming for gas moving at JetSpeed makes the approaching jet outshine the receding one. Dimmed by the disk
    /// in front of it and lensed like the rest.
    /// </summary>
    static void AccumulateJets(Vector3 x, Vector3 v, float dt, ref Vector3 color, ref float transmittance)
    {
        if (JetNow <= 0f) return;
        float z = Vector3.Dot(x, N), az = MathF.Abs(z);
        if (JetHeads > 0f)
        {
            // The heads punching out through the gas round the hole: bright knots at the jets' tips.
            float hr = JetWidth * MathF.Pow(JetLengthNow + JetBase * JetSpiral, 0.55f);   // as wide as the jet there
            float hd = az - 0.95f * JetLengthNow;
            float rr = (x - z * N).LengthSquared();
            float head = MathF.Exp(-(hd * hd + rr) / (hr * hr));
            color += transmittance * new Vector3(0.75f, 0.88f, 1f) * (JetHeads * JetNow * 0.25f * head * dt / hr);
        }
        float start = 1.1f * (1f - JetSpiral);                                                       // the spiral tube starts at the hole
        if (az < start || az > JetLengthNow) return;
        Vector3 perp = x - z * N;
        float rho = perp.Length();
        float R = JetWidth * MathF.Pow(az + JetBase * JetSpiral, 0.55f);
        if (rho > 3f * R) return;
        float s = rho / R;
        float tail = (1f - SmoothStep(JetLengthNow * 0.6f, JetLengthNow, az)) * MathF.Pow(2f / MathF.Max(az, 2f), 0.6f);
        float along = SmoothStep(1.1f, 2.5f, az) * tail;

        // Angle around the axis, and the advected coordinate along it (the pattern streams outward at JetFlow).
        Vector3 t = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        Vector3 bb = Vector3.Cross(N, t);
        float phi = MathF.Atan2(Vector3.Dot(perp, bb), Vector3.Dot(perp, t));
        float flow = MathF.Pow(az, 0.45f) - JetFlow * Time + (z < 0f ? 7.3f : 0f);
        float twist = JetTwist * flow;

        // Turbulence twisted along with the swirl, so the wisps ride the spiral.
        float tw = phi - 0.5f * twist * JetSpiral;
        var q = new Vector3(s * MathF.Cos(tw) * 1.8f, s * MathF.Sin(tw) * 1.8f, flow * 2.6f);
        float n = Fbm(q);
        float lumps = 0.35f + 1.3f * n * n;

        float plain = (MathF.Exp(-2.5f * s * s) + 0.25f * MathF.Exp(-0.6f * s * s)) * lumps;       // spine + sheath
        // Spiral core: a solid column with a soft edge, its surface streaked by bands of brighter gas winding round it.
        // The swirl changes how brightly the gas glows, not how much it absorbs, so it still shows where the column is thick.
        float u = Math.Clamp((az - start) / (0.85f * R), 0f, 1f);
        float cap = MathF.Max(MathF.Sqrt(u * (2f - u)), 1e-3f);                                      // rounded end
        float column = MathF.Exp(-MathF.Pow(s / (0.85f * cap), 10f));
        float swirl = 0.5f + 0.5f * MathF.Cos(12f * phi - twist + 1.5f * n);
        float streaks = 0.7f + (0.6f * swirl - 0.3f) * SmoothStep(0.15f, 0.6f, s);                   // on the surface, not the axis
        // The glow fades out halfway along; the gas carries on as a darker, smoky plume.
        float glowFade = 1f - SmoothStep(JetLengthNow * 0.3f, JetLengthNow * 0.75f, az);
        float spiralGlow = column * streaks * (0.9f + 0.2f * n) * glowFade;
        float spiralGas = 4f * column * (1f - glowFade * 0.4f) * (0.5f + n);
        float density = along * plain + (tail * spiralGlow - along * plain) * JetSpiral;
        float gas = along * plain + (tail * spiralGas - along * plain) * JetSpiral;

        // Relativistic beaming: the gas moves away from the hole along the axis at JetSpeed.
        float cosT = Vector3.Dot(N * MathF.Sign(z), -Vector3.Normalize(v));
        float gamma = 1f / MathF.Sqrt(1f - JetSpeed * JetSpeed);
        float boost = gamma * (1f - JetSpeed * cosT);
        float beam = MathF.Min(1f / (boost * boost * boost * gamma * gamma * gamma), 10f);   // D^3 / gamma^3 (D = 1 / boost), capped
                                                                                            // so a jet seen end-on doesn't drown the view

        Vector3 jetColor = Vector3.Lerp(new Vector3(0.3f, 0.55f, 1f), Vector3.One, Math.Clamp(0.4f * density * beam / 8f, 0f, 0.35f));
        color += transmittance * jetColor * (JetNow * 2.5f * density * beam * dt);
        transmittance *= MathF.Exp(-JetAbsorb * gas * dt / R);
    }

    const float ParticleCell = 0.5f; // horizon radii per particle cell

    /// <summary>A point in the disk's rotating frame, where the particles and debris ride with the spin.</summary>
    static Vector3 ToDiskFrame(in DiskP d, Vector3 x)
    {
        float height = Vector3.Dot(x, d.N);
        Vector3 q = x - height * d.N;
        float r = q.Length();
        float angle = MathF.Atan2(Vector3.Dot(q, d.B), Vector3.Dot(q, d.T)) - d.Angle;
        return new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), height);
    }

    /// <summary>
    /// Sparks of white-hot matter and dark chunks of debris riding in the disk. One candidate per cell of a grid in the
    /// rotating frame; each is counted by the one ray segment its closest approach falls in, and its size never drops
    /// below the pixel footprint (energy-conserving), so distant particles fade instead of flickering.
    /// </summary>
    static void AccumulateParticles(in DiskP d, Vector3 x0, Vector3 x1, float traveled, float presence, float temperature, float g,
        ref Vector3 color, ref float transmittance)
    {
        if (presence <= 0f || d.Particles + d.Debris <= 0f) return;
        Vector3 a = ToDiskFrame(d, x0), ab = ToDiskFrame(d, x1) - a;
        float ab2 = Vector3.Dot(ab, ab);
        if (ab2 < 1e-8f) return;
        Vector3 m = (a + ab * 0.5f) / d.Cell - new Vector3(0.5f);
        Vector3 baseCell = new Vector3(MathF.Floor(m.X), MathF.Floor(m.Y), MathF.Floor(m.Z));
        float footprint = traveled * PixelAngle;
        float size = d.Cell / ParticleCell;
        Vector3 spark = Saturate(Blackbody(temperature * g * 1.4f)) * (g * g * g);
        for (int k = 0; k < 8; k++)
        {
            Vector3 cell = baseCell + new Vector3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
            float kind = Hash3(cell + new Vector3(71.3f));
            if (kind > 0.32f) continue; // most cells are empty
            Vector3 jitter = new Vector3(Hash3(cell + new Vector3(1.7f)), Hash3(cell + new Vector3(9.2f)), Hash3(cell + new Vector3(23.5f)));
            Vector3 p = (cell + new Vector3(0.2f) + jitter * 0.6f) * d.Cell;
            float s = Vector3.Dot(p - a, ab) / ab2;
            if (s < 0f || s >= 1f) continue; // counted by the segment its closest approach falls in
            Vector3 dd = a + ab * s - p;
            bool debris = kind < 0.08f;
            float radius = (debris ? 0.07f + 0.08f * jitter.X : 0.012f + 0.018f * jitter.Y) * size;
            float sigma = MathF.Max(radius, footprint);
            float fall = MathF.Exp(-Vector3.Dot(dd, dd) / (sigma * sigma)) * (radius * radius) / (sigma * sigma) * presence;
            if (debris)
            {
                // Dark rock in front of the glow, with a faint rim lit by the disk.
                float cover = Math.Clamp(d.Debris * fall * 0.95f, 0f, 1f);
                color += transmittance * spark * (0.04f * cover);
                transmittance *= 1f - cover;
            }
            else
            {
                color += transmittance * spark * (d.Particles * d.Bright * (3f + 8f * jitter.Z * jitter.Z * jitter.Z) * fall); // a few much brighter
            }
        }
    }

    /// <summary>Per-channel exponential roll-off: the hottest regions saturate to white, the rest keep their colour.</summary>
    static Vector3 Tonemap(Vector3 c) => new Vector3(1 - MathF.Exp(-c.X), 1 - MathF.Exp(-c.Y), 1 - MathF.Exp(-c.Z));

    /// <summary>Chroma boost around luminance (a real 25,000 K blackbody is only pale blue).</summary>
    static Vector3 Saturate(Vector3 c)
    {
        float l = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        return Vector3.Max(new Vector3(l) + (c - new Vector3(l)) * Saturation, Vector3.Zero);
    }

    static float DiskTemperature(float r, float inner, float peak)
    {
        if (r <= inner) return 0;
        float rPeak = inner * (49f / 36f);
        return peak * MathF.Pow(r / rPeak, -0.75f) * MathF.Pow(Math.Clamp((1 - MathF.Sqrt(inner / r)) * 7f, 0f, 1f), 0.25f);
    }

    static Vector3 Blackbody(float kelvin)
    {
        float t = Math.Clamp(kelvin, 1667f, 25000f), t2 = t * t, t3 = t2 * t;
        float x = t < 4000f ? -0.2661239e9f / t3 - 0.2343589e6f / t2 + 0.8776956e3f / t + 0.179910f
                            : -3.0258469e9f / t3 + 2.1070379e6f / t2 + 0.2226347e3f / t + 0.240390f;
        float x2 = x * x, x3 = x2 * x;
        float y = t < 2222f ? -1.1063814f * x3 - 1.34811020f * x2 + 2.18555832f * x - 0.20219683f
                : t < 4000f ? -0.9549476f * x3 - 1.37418593f * x2 + 2.09137015f * x - 0.16748867f
                            : 3.0817580f * x3 - 5.87338670f * x2 + 3.75112997f * x - 0.37001483f;
        float X = x / y, Y = 1f, Z = (1 - x - y) / y;
        var rgb = new Vector3(3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z,
                             -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z,
                              0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z);
        rgb = Vector3.Max(rgb, Vector3.Zero);
        rgb /= MathF.Max(MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z)), 1e-5f);
        return rgb * Math.Clamp(kelvin / 1667f, 0f, 1f);
    }

    static float Hash3(Vector3 p)
    {
        p = Frac(p * 0.3183099f + new Vector3(0.1f));
        p *= 17f;
        return Frac(p.X * p.Y * p.Z * (p.X + p.Y + p.Z));
    }

    static float Noise3(Vector3 x)
    {
        Vector3 i = new Vector3(MathF.Floor(x.X), MathF.Floor(x.Y), MathF.Floor(x.Z));
        Vector3 f = x - i;
        f = f * f * (new Vector3(3f) - 2f * f);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(L(L(Hash3(i), Hash3(i + new Vector3(1, 0, 0)), f.X), L(Hash3(i + new Vector3(0, 1, 0)), Hash3(i + new Vector3(1, 1, 0)), f.X), f.Y),
                 L(L(Hash3(i + new Vector3(0, 0, 1)), Hash3(i + new Vector3(1, 0, 1)), f.X), L(Hash3(i + new Vector3(0, 1, 1)), Hash3(i + new Vector3(1, 1, 1)), f.X), f.Y), f.Z);
    }

    static float Fbm(Vector3 p)
    {
        float sum = 0, a = 0.5f;
        for (int k = 0; k < 3; k++) { sum += a * Noise3(p); p = p * 2.03f + new Vector3(1.7f); a *= 0.5f; }
        return sum / 0.875f;
    }

    static float Frac(float v) => v - MathF.Floor(v);
    static Vector3 Frac(Vector3 v) => new Vector3(Frac(v.X), Frac(v.Y), Frac(v.Z));
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float SmoothStep(float e0, float e1, float x) { float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f); return t * t * (3 - 2 * t); }

    // Minimal PNG writer (RGB8, zlib via ZLibStream).
    static void WritePng(string path, int w, int h, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4]; WriteBE(crc, 0, (int)Crc32(td)); s.Write(crc);
    }

    static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte d in data)
        {
            crc ^= d;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
