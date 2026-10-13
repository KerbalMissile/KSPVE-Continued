// Offline preview of KSPVE.Pure.Space.Particles structures (evaporating planets, impact debris, synestias, ...), drawn with
// the same point-splat maths as unity/KSPVEShaders/Assets/Shaders/Space/Structures.shader.
// Usage: dotnet run -c Release --project tools/BlackHolePreview -- structure "<preset name or kind>" out.png [width] [height]
// Environment: ST_<setting key>=value overrides a setting (ST_count=20000); ST_YAW / ST_PITCH (degrees; yaw 0 looks from the
// star's side towards the planet away from the star, 180 looks towards the star), ST_DIST (body radii, 0 = fit), ST_UT (s),
// ST_EXPOSURE, ST_BODY (earth, jupiter, hotjupiter, asteroid, saturn), ST_STARAU, ST_TARGET (x,y,z body radii), ST_FIELD (field of view, degrees).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Particles;

internal static class StructurePreview
{
    const double Au = 1.495978707e11, MSun = 1.98847e30, MEarth = 5.9722e24, REarth = 6.371e6, MJup = 1.8982e27, RJup = 7.1492e7;
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    static double Env(string name, double fallback)
    {
        string s = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(s) ? fallback : double.Parse(s, Ci);
    }

    public static int Run(string[] args)
    {
        string what = args.Length > 1 ? args[1] : "Like the reference";
        string path = args.Length > 2 ? args[2] : "structure.png";
        int width = args.Length > 3 ? int.Parse(args[3], Ci) : 1280, height = args.Length > 4 ? int.Parse(args[4], Ci) : 800;

        StructureConfig config;
        StructureSettings.Preset preset = StructureSettings.Presets.FirstOrDefault(p => string.Equals(p.Name, what, StringComparison.OrdinalIgnoreCase) || p.Name.StartsWith(what, StringComparison.OrdinalIgnoreCase));
        if (preset != null)
        {
            config = new StructureConfig { Kind = preset.Kind };
            StructureSettings.Apply(preset, config);
        }
        else if (Enum.TryParse(what, true, out StructureKind kind)) config = new StructureConfig { Kind = kind };
        else { Console.Error.WriteLine("No preset or kind called " + what); return 1; }
        foreach (StructureParam p in StructureSettings.Params)
        {
            string env = Environment.GetEnvironmentVariable("ST_" + p.Key);
            if (!string.IsNullOrEmpty(env)) config.Set(p.Key, double.Parse(env, Ci));
        }
        config.Set("speculative", Math.Max(config.N("speculative"), preset != null ? config.N("speculative") : 1.0));
        config.Clamp();

        string bodyName = Environment.GetEnvironmentVariable("ST_BODY");
        if (string.IsNullOrEmpty(bodyName)) bodyName = config.Kind == StructureKind.EvaporatingPlanet ? "hotjupiter" : config.Kind == StructureKind.RubblePile ? "asteroid" : config.Kind == StructureKind.RingingMoon ? "saturn" : "earth";
        double mass, radius, starAu;
        switch (bodyName)
        {
            case "jupiter": mass = MJup; radius = RJup; starAu = 5.2; break;
            case "hotjupiter": mass = 0.69 * MJup; radius = 1.38 * RJup; starAu = 0.047; break;
            case "saturn": mass = 5.68e26; radius = 5.8e7; starAu = 9.5; break;
            case "asteroid": mass = 5e11; radius = 250.0; starAu = 1.5; break;
            case "kic": mass = 0.02 * MEarth; radius = 2.5e6; starAu = 0.0044; break;
            case "superearth": mass = 5.7 * MEarth; radius = 2.2 * REarth; starAu = 0.5; break;
            default: mass = MEarth; radius = REarth; starAu = 1.0; break;
        }
        if (config.Kind == StructureKind.EvaporatingPlanet && bodyName == "hotjupiter") { }
        starAu = Env("ST_STARAU", starAu);
        double starMass = config.Kind == StructureKind.EvaporatingPlanet ? 1.1 * MSun : MSun, d = starAu * Au, muStar = UniversalKepler.G * starMass;
        double ut = Env("ST_UT", config.Kind == StructureKind.EvaporatingPlanet ? 4e5 : 0.0);
        var ctx = new StructureContext
        {
            Ut = ut, BodyMu = UniversalKepler.G * mass, BodyRadius = radius, HasStar = true, StarMu = muStar, StarRadius = 6.957e8, StarTemperature = config.N("starTemperature"),
            BodyFromStar = new Double3(d, 0, 0), BodyVelocity = new Double3(0, Math.Sqrt(muStar / d), 0), Axis = new Double3(0, 0, 1)
        };

        var set = new ParticleSet();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Structures.Generate(config, ctx, set);
        Console.WriteLine($"{StructureSettings.KindName(config.Kind)}: {set.Count} particles in {clock.ElapsedMilliseconds} ms");
        if (config.Kind == StructureKind.EvaporatingPlanet)
        {
            EvaporationState st = Structures.EvaporationNow(config, ctx);
            Console.WriteLine($"  atmosphere left {st.AtmosphereLeft:0.###}, planet {st.Mass:0.###e+0} kg {st.Radius:0.###e+0} m, XUV flux {st.XuvFlux:0.###} W/m2, energy-limited {st.EnergyLimited:0.###e+0}, overflow {st.RocheOverflow:0.###e+0}, dust {st.DustSublimation:0.###e+0} kg/s, lobe fill {st.FillFactor:0.###}, K {st.ErkaevK:0.###}");
        }

        if (set.Count > 0)
        {
            var emit = Enumerable.Range(0, set.Count).Select(i => set.Items[i].Emit.Y).OrderBy(v => v).ToArray();
            var refl = Enumerable.Range(0, set.Count).Select(i => set.Items[i].Reflect.Y).OrderBy(v => v).ToArray();
            var size = Enumerable.Range(0, set.Count).Select(i => set.Items[i].Radius).OrderBy(v => v).ToArray();
            Console.WriteLine($"  emit median {emit[emit.Length / 2]:0.###e+0} max {emit[^1]:0.###e+0}; reflect median {refl[refl.Length / 2]:0.###e+0} max {refl[^1]:0.###e+0}; radius median {size[size.Length / 2]:0.###e+0} m");
        }
        // Camera.
        double yaw = Env("ST_YAW", 40.0) * Math.PI / 180.0, pitch = Env("ST_PITCH", 20.0) * Math.PI / 180.0;
        var radii = new List<double>();
        for (int i = 0; i < set.Count; i++) radii.Add(set.Items[i].Position.Length);
        radii.Sort();
        double reach = radii.Count > 0 ? radii[(int)(radii.Count * 0.97)] : radius;
        Double3 target = Double3.Zero;
        string targetEnv = Environment.GetEnvironmentVariable("ST_TARGET");
        if (!string.IsNullOrEmpty(targetEnv))
        {
            double[] t = targetEnv.Split(',').Select(s => double.Parse(s, Ci)).ToArray();
            target = new Double3(t[0], t[1], t[2]) * radius;
        }
        else if ((config.Kind == StructureKind.EvaporatingPlanet || config.Kind == StructureKind.ImpactDebris) && set.Count > 0)
        {
            // Centre on the middle of the particles.
            Double3 sum = Double3.Zero;
            for (int i = 0; i < set.Count; i++) sum = sum + set.Items[i].Position;
            target = sum / set.Count;
        }
        double fov = Env("ST_FIELD", 40.0) * Math.PI / 180.0, tanY = Math.Tan(fov / 2.0), tanX = tanY * width / height;
        double dist = Env("ST_DIST", 0.0) * radius;
        if (dist <= 0.0)
        {
            double extent = 0.0;
            for (int i = 0; i < set.Count; i++) extent = Math.Max(extent, (set.Items[i].Position - target).Length);
            extent = Math.Min(extent, reach * 1.3);
            dist = Math.Max(extent / Math.Min(tanY, tanX) * 1.05, radius * 3.0);
        }
        Double3 eye = target + new Double3(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch)) * dist;
        Double3 forward = (target - eye).Normalized();
        Double3 right = Double3.Cross(forward, new Double3(0, 0, 1)).Normalized();
        Double3 up = Double3.Cross(right, forward);

        Double3 sun = ctx.ToStar;
        Double3 starColor = StructureLight.Blackbody(config.N("starTemperature"));
        double g = config.N("scatterAsymmetry"), brightness = config.N("brightness") * config.N("visibilityBoost");
        double minPx = config.N("minPixels");
        double pixelAngle = 2.0 * tanY / height;
        var hdr = new float[width * height * 3];

        // The body: a lit sphere, so the structure has its planet to sit on.
        bool drawBody = Env("ST_BODYDRAW", 1.0) > 0.5;
        Double3 bodyColor = new Double3(0.5, 0.45, 0.4);
        double bodyAlbedo = 0.3;

        // Back to front, composited "over" (premultiplied): thick clouds hide what is behind them, thin gas only adds light.
        var order = Enumerable.Range(0, set.Count).OrderByDescending(i => Double3.Dot(set.Items[i].Position - eye, forward)).ToArray();
        foreach (int i in order)
        {
            StructureParticle p = set.Items[i];
            if (p.Radius <= 0.0) continue;
            Double3 rel = p.Position - eye;
            double z = Double3.Dot(rel, forward);
            if (z <= 0.0) continue;
            double sx = Double3.Dot(rel, right) / (z * tanX), sy = Double3.Dot(rel, up) / (z * tanY);
            double angle = p.Radius / z;
            double minAngle = minPx * 0.5 * pixelAngle;
            double drawAngle = Math.Max(angle, minAngle);
            double energy = angle < minAngle ? (angle / minAngle) * (angle / minAngle) : 1.0;
            double radiusPx = drawAngle / pixelAngle;
            if (radiusPx > 600.0) radiusPx = 600.0;
            // Hidden behind the body?
            if (drawBody)
            {
                double along = Double3.Dot(-eye, rel.Normalized());
                Double3 closest = eye + rel.Normalized() * along;
                if (along > 0 && along < rel.Length && closest.Length < radius) continue;
            }
            // Henyey-Greenstein phase, normalised to 1 for g = 0 (angle between the star's light and the way to the eye).
            double cosScatter = Double3.Dot(sun, (eye - p.Position).Normalized()) * -1.0;
            double hg = (1.0 - g * g) / Math.Pow(1.0 + g * g - 2.0 * g * cosScatter, 1.5);
            Double3 radiance = new Double3(p.Reflect.X * starColor.X, p.Reflect.Y * starColor.Y, p.Reflect.Z * starColor.Z) * hg + p.Emit;
            radiance = radiance * (brightness * energy);
            double alpha = Math.Min(p.Opacity, 1.0) * 0.9 * energy;
            double cx = (sx * 0.5 + 0.5) * width, cy = (0.5 - sy * 0.5) * height;
            int r = (int)Math.Ceiling(radiusPx * (p.Softness > 0.5 ? 2.2 : 1.1));
            for (int yy = Math.Max((int)cy - r, 0); yy <= Math.Min((int)cy + r, height - 1); yy++)
            for (int xx = Math.Max((int)cx - r, 0); xx <= Math.Min((int)cx + r, width - 1); xx++)
            {
                double q = Math.Sqrt((xx + 0.5 - cx) * (xx + 0.5 - cx) + (yy + 0.5 - cy) * (yy + 0.5 - cy)) / Math.Max(radiusPx, 0.5);
                double hard = 1.0 - Smooth(0.75, 1.0, q), soft = Math.Exp(-2.5 * q * q);
                double profile = hard + (soft - hard) * p.Softness;
                if (profile <= 0.001) continue;
                int k = (yy * width + xx) * 3;
                float keep = (float)(1.0 - alpha * profile);
                hdr[k] = hdr[k] * keep + (float)(radiance.X * profile);
                hdr[k + 1] = hdr[k + 1] * keep + (float)(radiance.Y * profile);
                hdr[k + 2] = hdr[k + 2] * keep + (float)(radiance.Z * profile);
            }
        }

        // The sphere of the body and stars behind, then a filmic-ish tonemap with a little bloom.
        var rng = new Random(5);
        double exposure = Env("ST_EXPOSURE", 1.0);
        var rgb = new byte[width * height * 3];
        var bloom = Bloom(hdr, width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int k = (y * width + x) * 3;
            double sx = ((x + 0.5) / width * 2.0 - 1.0) * tanX, sy = (1.0 - (y + 0.5) / height * 2.0) * tanY;
            Double3 ray = (forward + right * sx + up * sy).Normalized();
            double cr = hdr[k] + 0.6 * bloom[k], cg = hdr[k + 1] + 0.6 * bloom[k + 1], cb = hdr[k + 2] + 0.6 * bloom[k + 2];
            if (drawBody)
            {
                double b = Double3.Dot(eye, ray), cc = eye.LengthSquared - radius * radius, disc = b * b - cc;
                if (disc > 0.0)
                {
                    double tHit = -b - Math.Sqrt(disc);
                    if (tHit > 0.0)
                    {
                        Double3 n = (eye + ray * tHit).Normalized();
                        double lit = Math.Max(Double3.Dot(n, sun), 0.0);
                        cr = bodyColor.X * bodyAlbedo * lit * brightness + 0.02;
                        cg = bodyColor.Y * bodyAlbedo * lit * brightness + 0.02;
                        cb = bodyColor.Z * bodyAlbedo * lit * brightness + 0.02;
                    }
                }
            }
            double Map(double v) => 1.0 - Math.Exp(-v * exposure);
            rgb[k] = (byte)(255.0 * Math.Pow(Map(cr), 1.0 / 2.2));
            rgb[k + 1] = (byte)(255.0 * Math.Pow(Map(cg), 1.0 / 2.2));
            rgb[k + 2] = (byte)(255.0 * Math.Pow(Map(cb), 1.0 / 2.2));
            if (rng.NextDouble() < 0.0006 && cr + cg + cb < 0.05) { byte s = (byte)(120 + rng.Next(120)); rgb[k] = rgb[k + 1] = rgb[k + 2] = s; }
        }
        Program.WritePngPublic(path, width, height, rgb);
        Console.WriteLine("Wrote " + Path.GetFullPath(path));
        return 0;
    }

    static double Smooth(double a, double b, double x)
    {
        double t = Math.Min(Math.Max((x - a) / (b - a), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    // A cheap bloom: the image blurred at two scales by separable box filters.
    static float[] Bloom(float[] src, int w, int h)
    {
        var result = new float[src.Length];
        foreach (int radius in new[] { 2, 6 })
        {
            float[] blurred = Blur(Blur(Blur(src, w, h, radius), w, h, radius), w, h, radius);   // three box passes make a gaussian
            for (int i = 0; i < result.Length; i++) result[i] += blurred[i] * 0.5f;
        }
        return result;
    }

    static float[] Blur(float[] src, int w, int h, int r)
    {
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < h; y++)
            {
                double acc = 0;
                for (int x = -r; x <= r; x++) acc += src[(y * w + Math.Min(Math.Max(x, 0), w - 1)) * 3 + c];
                for (int x = 0; x < w; x++)
                {
                    tmp[(y * w + x) * 3 + c] = (float)(acc / (2 * r + 1));
                    acc += src[(y * w + Math.Min(x + r + 1, w - 1)) * 3 + c] - src[(y * w + Math.Max(x - r, 0)) * 3 + c];
                }
            }
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                for (int y = -r; y <= r; y++) acc += tmp[(Math.Min(Math.Max(y, 0), h - 1) * w + x) * 3 + c];
                for (int y = 0; y < h; y++)
                {
                    dst[(y * w + x) * 3 + c] = (float)(acc / (2 * r + 1));
                    acc += tmp[(Math.Min(y + r + 1, h - 1) * w + x) * 3 + c] - tmp[(Math.Max(y - r, 0) * w + x) * 3 + c];
                }
            }
        }
        return dst;
    }
}
