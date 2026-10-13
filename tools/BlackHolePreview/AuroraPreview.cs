using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using D3 = KSPVE.Pure.Numerics.Double3;

/// <summary>
/// aurora out.png [config.cfg] [body]: the aurora and airglow of a body as the game's maths (AuroraTracer, the reference of
/// Aurora.shader) draws them, from a point on the ground or in orbit, with the body, atmosphere and activity from the config
/// and the environment. Settings (defaults in brackets): AU_W, AU_H (960, 540); AU_FRAMES (6, jittered frames averaged, the
/// shader's temporal accumulation); AU_LAT, AU_LON (deg, 65, -147), AU_ALT (m, 150), AU_HEADING (deg from north, 0), AU_PITCH
/// (deg up, 25), AU_FOV (vertical deg, 60); AU_UT (s, 1.2e6); AU_SOLAR_TIME (local solar time at the observer, hours, 0);
/// AU_DEC (deg, 10); the solar wind AU_SPEED (km/s, 400), AU_DENSITY (cm^-3, 5), AU_BT (nT, 5), AU_BZ (nT, 0), AU_EP (energetic
/// particles, 1); AU_EXPOSURE (stops added to the display, 0); AU_GAIN_EV (override the night exposure gain, stops);
/// AU_KP (force the activity index); AU_SUBSTORM (force a phase: growth, expansion, recovery); AU_BOOST, AU_GLOWBOOST;
/// AU_MODE=ground or orbit (orbit defaults: alt 400 km, pitch -22, FOV 50).
/// </summary>
internal static class AuroraPreview
{
    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    public static int Run(string[] args)
    {
        string output = args[1];
        string cfgPath = args.Length > 2 ? args[2] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "gamedata", "Config", "KSPVE_AURORA.cfg");
        string bodyName = args.Length > 3 ? args[3] : "Earth";
        bool orbit = (Environment.GetEnvironmentVariable("AU_MODE") ?? "ground") == "orbit";
        int width = (int)Env("AU_W", 960), height = (int)Env("AU_H", 540), frames = (int)Env("AU_FRAMES", 6);
        double lat = Env("AU_LAT", orbit ? 55 : 65) * Math.PI / 180.0, lon = Env("AU_LON", -147) * Math.PI / 180.0;
        double alt = Env("AU_ALT", orbit ? 400e3 : 150);
        double heading = Env("AU_HEADING", orbit ? 0 : 0) * Math.PI / 180.0, pitch = Env("AU_PITCH", orbit ? -22 : 25) * Math.PI / 180.0;
        double fov = Env("AU_FOV", orbit ? 50 : 60) * Math.PI / 180.0;
        double ut = Env("AU_UT", 1.2e6);

        // The body.
        AuroraBodyModel model = null;
        var issues = new List<ConfigIssue>();
        foreach (CfgNode root in CfgParser.Parse(File.ReadAllText(cfgPath)))
            foreach (CfgNode child in root.Children)
                if (child.Name == "BODY" && string.Equals(child.GetLast("name"), bodyName, StringComparison.OrdinalIgnoreCase))
                    model = AuroraBodyModel.FromConfig(child, issues);
        if (model == null) { Console.Error.WriteLine("No BODY " + bodyName + " in " + cfgPath); return 1; }
        foreach (ConfigIssue i in issues) Console.Error.WriteLine(i);
        double radius = bodyName == "Earth" ? 6371010.0 : bodyName == "Kerbin" ? 600000.0 : bodyName == "Jupiter" ? 66316000.0 : bodyName == "Mars" ? 3375800.0 : bodyName == "Venus" ? 6051840.0 : 6371010.0;
        radius = Env("AU_RADIUS", radius);
        AtmosphereProfile atmosphere = bodyName == "Earth" ? AtmosphereProfile.Earth()
            : AtmosphereProfile.Isothermal(Env("AU_P0", 101325), Env("AU_H0", model.Composition == Composition.H2He ? 25000 : 8500), radius, Env("AU_G", 9.8), model.Composition == Composition.H2He ? 0.0023 : 0.029, model.ExosphereTemperature);

        // The wind and the day.
        double speed = Env("AU_SPEED", 400) * 1e3, density = Env("AU_DENSITY", 5) * 1e6, bt = Env("AU_BT", 5) * 1e-9, bz = Env("AU_BZ", 0) * 1e-9, ep = Env("AU_EP", 1);
        SolarWindState wind = SolarWindState.Quiet(SpaceWeather.ReferenceDistance, SpaceWeather.ReferenceDistance);
        wind.Speed = speed; wind.Density = density; wind.FieldStrength = bt; wind.FieldNorth = bz; wind.EnergeticParticles = ep;
        double solarTime = Env("AU_SOLAR_TIME", 0), dec = Env("AU_DEC", 10) * Math.PI / 180.0;
        double sunLon = lon + (12.0 - solarTime) * Math.PI / 12.0;
        D3 sun = new D3(Math.Cos(dec) * Math.Cos(sunLon), Math.Sin(dec), Math.Cos(dec) * Math.Sin(sunLon));

        var moons = new List<AuroraSolver.MoonPosition>();
        if (model.Moons.Count > 0)
        {
            double[] distances = { 5.9, 9.4, 15.0, 26.0 };
            double[] phases = { 0.3, 1.9, 3.4, 5.0 };
            for (int i = 0; i < model.Moons.Count; i++)
            {
                double a = distances[i % 4] * radius, ph = phases[i % 4] + ut * 2 * Math.PI / (3600.0 * 24 * (i + 1));
                moons.Add(new AuroraSolver.MoonPosition { Name = model.Moons[i].Name, Position = new D3(a * Math.Cos(ph), a * 0.02, a * Math.Sin(ph)) });
            }
        }
        double forcedKp = Env("AU_KP", double.NaN);
        double? kpOverride = double.IsNaN(forcedKp) ? (double?)null : forcedKp;
        string phase = Environment.GetEnvironmentVariable("AU_SUBSTORM");
        if (!string.IsNullOrEmpty(phase))
        {
            // Move the clock to a moment of the wanted substorm phase.
            double kp = forcedKp;
            if (double.IsNaN(kp)) kp = AuroraSolver.Solve(model, radius, t => wind, ut, sun, moons).Oval.Kp;
            for (double t = ut; t < ut + 3 * 86400.0; t += 60.0)
            {
                SubstormState s = Substorms.At(t, kp, model.Seed);
                if (phase == "none" && s.Phase == SubstormPhase.None || phase == "growth" && s.Phase == SubstormPhase.Growth && s.Progress > 0.8 || phase == "expansion" && s.Phase == SubstormPhase.Expansion && s.Progress > 0.3 || phase == "recovery" && s.Phase == SubstormPhase.Recovery && s.Progress > 0.25)
                { ut = t; break; }
            }
        }
        AuroraFrame frame = AuroraSolver.Solve(model, radius, t => wind, ut, sun, moons, kpOverride);
        Console.Error.WriteLine($"{bodyName}: Kp {frame.Oval.Kp:0.0}, oval {frame.OvalLatitudeDegrees:0.0} deg, stand-off {frame.Oval.StandOff:0.0} R, storm {frame.Storm.Phase} ({frame.Storm.Progress:0.00}), arcs {frame.Arc:0.0} kR, diffuse {frame.Diffuse:0.0}, pulsating {frame.Pulsating:0.00}, black {frame.Black:0.00}, STEVE {(frame.StevePresent ? "yes" : "no")}, SAR {frame.Sar:0.00}, theta {(frame.ThetaPresent ? "yes" : "no")}");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        EmissionLut lut = EmissionLut.Build(atmosphere, model.Composition);
        Console.Error.WriteLine($"emission table built in {clock.ElapsedMilliseconds} ms");
        for (int row = 0; row < EmissionLut.Rows; row++)
        {
            int pk = double.IsNaN(lut.PeakHeight[row]) ? 0 : (int)((lut.PeakHeight[row] - lut.HeightLow) / (lut.HeightHigh - lut.HeightLow) * lut.Samples);
            pk = Math.Max(0, Math.Min(lut.Samples - 1, pk));
            double rr = lut.Rgb[row, pk, 0], gg = lut.Rgb[row, pk, 1], bb = lut.Rgb[row, pk, 2], m = Math.Max(rr, Math.Max(gg, bb));
            Console.Error.WriteLine($"  row {(EmissionRow)row,-12} peak {lut.PeakHeight[row] / 1000:0.0} km  [{lut.LowerEdge[row] / 1000:0.0}, {lut.UpperEdge[row] / 1000:0.0}]  colour {rr / m:0.00} {gg / m:0.00} {bb / m:0.00}  per kR: {m * 1000:0.00e0}/km");
        }
        double gain = Env("AU_GAIN_EV", double.NaN);
        var tracer = new AuroraTracer
        {
            F = frame, Lut = lut,
            NightGain = double.IsNaN(gain) ? NightExposure.Gain(1.8, 10.0, 6400.0) : Math.Pow(2.0, gain),
            AuroraBoost = Env("AU_BOOST", 1), GlowBoost = Env("AU_GLOWBOOST", 1),
            ScaleHeight = atmosphere.ScaleHeightAt(0),
        };
        string only = Environment.GetEnvironmentVariable("AU_ONLY");
        if (!string.IsNullOrEmpty(only))
        {
            string[] k = only.Split(',');
            bool On(string n) => Array.IndexOf(k, n) >= 0;
            tracer.Arcs = On("arcs"); tracer.Diffuse = On("diffuse"); tracer.Pulsating = On("pulsating"); tracer.Black = On("black"); tracer.Proton = On("proton");
            tracer.Sar = On("sar"); tracer.Steve = On("steve"); tracer.Theta = On("theta"); tracer.Global = On("global"); tracer.Crustal = On("crustal");
            tracer.Giant = On("giant"); tracer.Glow = On("glow");
        }
        tracer.Prepare();

        // The camera.
        D3 up = new D3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon));
        D3 east = new D3(-Math.Sin(lon), 0, Math.Cos(lon));
        D3 north = new D3(-Math.Sin(lat) * Math.Cos(lon), Math.Cos(lat), -Math.Sin(lat) * Math.Sin(lon));
        D3 origin = up * (radius + alt);
        D3 horiz = north * Math.Cos(heading) + east * Math.Sin(heading);
        D3 forward = (horiz * Math.Cos(pitch) + up * Math.Sin(pitch)).Normalized();
        // The body frame (x latitude 0 longitude 0, y north, z longitude 90 E) is left-handed.
        D3 right = D3.Cross(up, forward).Normalized();
        D3 camUp = D3.Cross(forward, right).Normalized();
        double tanY = Math.Tan(fov / 2), tanX = tanY * width / height;
        double pixelAngle = 2 * tanY / height;
        double exposure = Math.Pow(2.0, Env("AU_EXPOSURE", 0));

        if (Environment.GetEnvironmentVariable("AU_DEBUG") != null)
        {
            tracer.Locate(origin, out double mlt, out double dOval, out double thf);
            Console.Error.WriteLine($"observer: MLT {mlt:0.0} h, {dOval / 1000.0:0} km equatorward of the oval's line, footprint colatitude {thf:0.0} deg");
            Console.Error.WriteLine($"darkness: sun.up {D3.Dot(sun, up):0.00}; axis {frame.Axis}; noon {frame.Noon}; arcs {frame.Arc:0.0} diffuse {frame.Diffuse:0.0} glow {frame.GlowGreen}");
            {
                Rgb cc = tracer.Trace(origin, forward, 1e9, 0.5, pixelAngle);
                Console.Error.WriteLine($"  centre ray: {cc.R:0.0000e0} {cc.G:0.0000e0} {cc.B:0.0000e0}");
            }
            for (int k = 0; k < 7; k++)
            {
                double pt = (10 + 10 * k) * Math.PI / 180.0;
                D3 dd = (horiz * Math.Cos(pt) + up * Math.Sin(pt)).Normalized();
                Rgb c = tracer.Trace(origin, dd, 1e9, 0.5, pixelAngle);
                Console.Error.WriteLine($"  pitch {10 + 10 * k}: {c.R:0.0000e0} {c.G:0.0000e0} {c.B:0.0000e0}");
            }
        }
        var pixels = new double[width * height * 3];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (int f = 0; f < frames; f++)
                {
                    uint h = AuroraNoise.Mix((uint)(x + y * width), (uint)(f * 977 + 3));
                    double jit = (AuroraNoise.Rand(h) + f) / frames;
                    double u = ((x + 0.5) / width * 2 - 1) * tanX, v = (1 - (y + 0.5) / height * 2) * tanY;
                    D3 dir = (forward + right * u + camUp * v).Normalized();
                    Rgb c = tracer.Trace(origin, dir, 1e9, jit, pixelAngle);
                    r += c.R; g += c.G; b += c.B;
                }
                int i = (y * width + x) * 3;
                pixels[i] = r / frames; pixels[i + 1] = g / frames; pixels[i + 2] = b / frames;
            }
        });

        // A dark sky and the ground, then the display transform.
        var bytes = new byte[width * height * 3];
        uint starSeed = 99u;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                double u = ((x + 0.5) / width * 2 - 1) * tanX, v = (1 - (y + 0.5) / height * 2) * tanY;
                D3 dir = (forward + right * u + camUp * v).Normalized();
                double b0 = D3.Dot(origin, dir), disc = b0 * b0 - (D3.Dot(origin, origin) - radius * radius);
                bool hitsBody = disc > 0 && -b0 - Math.Sqrt(disc) > 0;
                double sky = 0.003 * (hitsBody ? 0.2 : 1.0);
                double r = pixels[i] + sky * 0.35, g = pixels[i + 1] + sky * 0.5, bl = pixels[i + 2] + sky * 0.9;
                if (!hitsBody)
                {
                    uint hs = AuroraNoise.Mix((uint)(x * 7 + y * width * 3), starSeed);
                    if (AuroraNoise.Rand(hs) > 0.9985) { double s = 0.02 + 0.2 * AuroraNoise.Rand(hs + 1); r += s; g += s; bl += s * 1.05; }
                }
                else if (!orbit) { r += 0.0008; g += 0.0009; bl += 0.0012; }
                r *= exposure; g *= exposure; bl *= exposure;
                bytes[i] = Encode(r); bytes[i + 1] = Encode(g); bytes[i + 2] = Encode(bl);
            }
        WritePng(output, width, height, bytes);
        return 0;
    }

    private static byte Encode(double v)
    {
        v = Math.Max(0, v);
        // ACES-like filmic curve, then sRGB.
        double a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        v = Math.Max(0, Math.Min(1, v * (a * v + b) / (v * (c * v + d) + e)));
        v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        return (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
    }

    private static void WritePng(string path, int w, int h, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, w); BigEndian(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }

    private static void BigEndian(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BigEndian(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in td) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
        var c = new byte[4]; BigEndian(c, 0, (int)~crc); s.Write(c);
    }
}
