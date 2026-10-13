// Offline preview of the fluid-dynamics brown dwarf (KSPVE.Pure.Fluids): spins the atmosphere up, advances it, and renders the
// body with a CPU copy of unity/KSPVEShaders/Assets/Shaders/Fluids/BrownDwarf.shader's shading.
// Usage: dotnet run -- browndwarf <out.png> [key=value ...]
//   w, h (pixels, 900)       T (effective temperature K, 1500)      type (L, T, Y, giant, hotjupiter; from T)
//   hours (rotation period, 5)  inc (sub-observer latitude, deg, 20)   phase (sub-observer longitude, deg, 0)
//   K (spectral truncation, 42)  grid (cloud grid longitudes, 256)  seed (1)  forcing (1)  patchiness (1)
//   time (extra seconds to advance after spin-up, 0)  frames (>1 writes a contact sheet of that many frames, rotation only)
//   view (eye or ir: the near-infrared false-colour view)  ev (exposure compensation in stops, 0)  boost (display brightness boost, 1)  map (also write the equirectangular map here)
//   zonal (also write the zonal-mean wind profile and jet count to the console)  star (reflect a star: strength relative to the Sun at 1 AU, 0)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KSPVE.Pure.Fluids;

internal static class BrownDwarfPreview
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private const double SunDiscRadiance = 2.0;

    private static Dictionary<string, string> Options(string[] args)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 2; i < args.Length; i++)
        {
            int eq = args[i].IndexOf('=');
            if (eq > 0) o[args[i].Substring(0, eq)] = args[i].Substring(eq + 1);
        }
        return o;
    }

    private static double Num(Dictionary<string, string> o, string key, double fallback) =>
        o.TryGetValue(key, out string s) && double.TryParse(s, NumberStyles.Float, Ci, out double v) ? v : fallback;

    public static int Run(string[] args)
    {
        string path = args.Length > 1 && !args[1].Contains("=") ? args[1] : "browndwarf.png";
        Dictionary<string, string> o = Options(args);
        int w = (int)Num(o, "w", 900), h = (int)Num(o, "h", 900);
        double temperature = Num(o, "T", 1500.0);
        double hours = Num(o, "hours", 5.0);
        double inc = Num(o, "inc", 20.0) * Math.PI / 180.0, phase = Num(o, "phase", 0.0) * Math.PI / 180.0;
        var profile = new FluidProfile
        {
            Body = "preview",
            Temperature = temperature,
            Resolution = (int)Num(o, "K", 42),
            TracerLongitudes = (int)Num(o, "grid", 256),
            Seed = (ulong)Num(o, "seed", 1),
            Forcing = Num(o, "forcing", 1.0),
            Patchiness = Num(o, "patchiness", 1.0)
        };
        profile.Kind = Decks.KindForTemperature(temperature);
        if (o.TryGetValue("type", out string typeText) && FluidProfile.TryParseKind(typeText, out FluidKind kind)) profile.Kind = kind;
        double radius = Num(o, "radius", 7.0e7);
        double gravity = Num(o, "g", FluidPhysics.DwarfGravity);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sim = new AtmosphereSimulation(profile, radius, hours * 3600.0, gravity);
        sim.SpinUp();
        Console.WriteLine($"{profile.Kind} dwarf {temperature:0} K, P = {hours} h, K = {sim.Model.K}: spun up in {sw.Elapsed.TotalSeconds:0.0} s ({sim.Steps} steps), rms wind {sim.Model.RmsSpeed():0} m/s");
        double extra = Num(o, "time", 0.0);
        if (extra > 0.0) sim.AdvanceTo(extra, 100000);
        double[] u = sim.Model.ZonalMeanWind();
        Console.WriteLine($"zonal jets (eastward): {Jets.Count(u, 0.3)}; Rhines degree {FluidPhysics.RhinesDegree(radius, sim.Omega, sim.Model.RmsSpeed()):0.0}");
        if (o.ContainsKey("zonal"))
            for (int j = 0; j < u.Length; j += 2) Console.WriteLine($"  lat {Math.Asin(sim.Model.Grid.Table.Mu[j]) * 180 / Math.PI,6:0.0}  u {u[j],8:0.0}");

        if (o.TryGetValue("view", out string viewText)) FluidSettings.View = viewText.StartsWith("i", StringComparison.OrdinalIgnoreCase) || viewText.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? FluidView.ThermalInfrared : FluidView.Eye;
        double boost = Num(o, "boost", 1.0);
        double ev = Math.Pow(2.0, Num(o, "ev", 0.0));
        double star = Num(o, "star", 0.0);
        int frames = (int)Num(o, "frames", 1);

        if (o.TryGetValue("map", out string mapPath))
        {
            int mw = 1024, mh = 512;
            var rgbMap = RenderMap(sim, mw, mh, boost, star);
            Normalise(rgbMap, ev, out byte[] mapBytes);
            Program.WritePngPublic(mapPath, mw, mh, mapBytes);
            Console.WriteLine("wrote " + mapPath);
        }

        if (frames <= 1)
        {
            double[] image = RenderDisc(sim, w, h, inc, phase, boost, star);
            Normalise(image, ev, out byte[] bytes);
            Program.WritePngPublic(path, w, h, bytes);
        }
        else
        {
            // A contact sheet of one rotation: the same brown dwarf at evenly spaced longitudes (rotational variability).
            int cols = Math.Min(frames, 4), rows = (frames + cols - 1) / cols;
            int fw = w / 2, fh = h / 2;
            var sheet = new double[cols * fw * rows * fh * 3];
            double sum = 0.0;
            var all = new List<double[]>();
            for (int f = 0; f < frames; f++)
            {
                double[] img = RenderDisc(sim, fw, fh, inc, phase + 2.0 * Math.PI * f / frames, boost, star);
                all.Add(img);
            }
            for (int f = 0; f < frames; f++)
            {
                int cx = f % cols, cy = f / cols;
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        for (int c = 0; c < 3; c++)
                            sheet[(((cy * fh + y) * cols * fw) + cx * fw + x) * 3 + c] = all[f][(y * fw + x) * 3 + c];
            }
            _ = sum;
            Normalise(sheet, ev, out byte[] bytes);
            Program.WritePngPublic(path, cols * fw, rows * fh, bytes);
        }
        Console.WriteLine("wrote " + path);
        return 0;
    }

    // ---------------------------------------------------------------- shading (mirrors BrownDwarf.shader)

    private sealed class Shader
    {
        public AtmosphereSimulation Sim;
        public double Boost, StarStrength;
        public float[] Fields;
        public int NLon, NLat;
        public double[] DeckKelvin;
        public FluidView View;

        public Shader(AtmosphereSimulation sim, double boost, double star)
        {
            Sim = sim;
            Boost = boost;
            StarStrength = star;
            View = FluidSettings.View;
            NLon = sim.Tracers.NLon;
            NLat = sim.Tracers.NLat;
            Fields = new float[NLon * NLat * 4];
            sim.WriteFields(Fields);
        }

        private double At(int i, int k, int ch)
        {
            i = Math.Clamp(i, 0, NLat - 1);
            k = ((k % NLon) + NLon) % NLon;
            return Fields[(i * NLon + k) * 4 + ch];
        }

        /// <summary>Bilinear sample of the field texture at a latitude and longitude (rad).</summary>
        public void Sample(double lat, double lon, double[] f)
        {
            double fi = (lat + Math.PI / 2) / Math.PI * NLat - 0.5;
            double fk = lon / (2.0 * Math.PI) * NLon - 0.5;
            int i0 = (int)Math.Floor(fi), k0 = (int)Math.Floor(fk);
            double ti = fi - i0, tk = fk - k0;
            for (int ch = 0; ch < 4; ch++)
            {
                double a = At(i0, k0, ch), b = At(i0, k0 + 1, ch), c = At(i0 + 1, k0, ch), d = At(i0 + 1, k0 + 1, ch);
                f[ch] = (a * (1 - tk) + b * tk) * (1 - ti) + (c * (1 - tk) + d * tk) * ti;
            }
        }

        private (double r, double g, double b) Lut(int deck, double mu, double kelvin)
        {
            // As the shader does: chromaticity interpolated linearly, luminance in its logarithm, bilinear in (temperature, mu).
            double xm = (EmissionLut.MuBins - 1) * Math.Clamp((mu - 0.04) / 0.96, 0.0, 1.0);
            int b0 = Math.Min((int)xm, EmissionLut.MuBins - 2);
            double tm = xm - b0;
            double xt = Math.Clamp(EmissionLut.CoordinateOf(kelvin), 0.0, 1.0) * (EmissionLut.Columns - 1);
            int c0 = Math.Min((int)xt, EmissionLut.Columns - 2);
            double tt = xt - c0;
            double cr = 0, cg = 0, cb = 0, ly = 0;
            for (int db = 0; db < 2; db++)
                for (int dc = 0; dc < 2; dc++)
                {
                    double wgt = (db == 0 ? 1 - tm : tm) * (dc == 0 ? 1 - tt : tt);
                    int o = (((deck * EmissionLut.MuBins) + b0 + db) * EmissionLut.Columns + c0 + dc) * 4;
                    var data = (View == FluidView.ThermalInfrared ? Sim.LutThermalInfrared : Sim.Lut).Data;
                    cr += wgt * data[o]; cg += wgt * data[o + 1]; cb += wgt * data[o + 2]; ly += wgt * data[o + 3];
                }
            double y = Math.Exp(ly);
            return (cr * y, cg * y, cb * y);
        }

        private static (double r, double g, double b) Ramp(double x)
        {
            (double, double, double) c0 = (0.015, 0.0, 0.0), c1 = (0.30, 0.05, 0.02), c2 = (0.95, 0.38, 0.05), c3 = (1.0, 0.78, 0.28), c4 = (1.0, 1.0, 0.85);
            static (double, double, double) L((double, double, double) a, (double, double, double) b, double t) => (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);
            return x < 0.25 ? L(c0, c1, x / 0.25) : x < 0.5 ? L(c1, c2, (x - 0.25) / 0.25) : x < 0.78 ? L(c2, c3, (x - 0.5) / 0.28) : L(c3, c4, Math.Clamp((x - 0.78) / 0.22, 0, 1));
        }

        /// <summary>Linear radiance (HDR, Sun's disc = 2) of the body at a point; mu = cosine of the viewing angle; nl = cosine of the angle to the star.</summary>
        public (double r, double g, double b) Shade(double lat, double lon, double mu, double nl)
        {
            var f = new double[4];
            Sample(lat, lon, f);
            double theta = f[3];
            // Eddington-Barbier limb darkening: the temperature where the slant optical depth is one.
            double limb = Math.Pow((mu + 2.0 / 3.0) / (5.0 / 3.0), 0.25);
            double slant = 1.0 / Math.Max(mu, 0.05);
            double r = 0, g = 0, b = 0, trans = 1.0;
            for (int d = 0; d < 3; d++)
            {
                double kelvin = (Sim.DeckKelvin[d] + theta * Sim.Forcing.DeckShare[d]) * limb;
                double tau = Sim.DeckSpecs[d].Tau * Math.Max(f[d], 0.0) * slant * (View == FluidView.ThermalInfrared ? EmissionLut.ThermalInfraredTauScale : 1.0);
                double absorb = 1.0 - Math.Exp(-tau);
                var e = Lut(d * 1 + 0, mu, Math.Max(kelvin, 20.0));
                r += trans * absorb * e.r; g += trans * absorb * e.g; b += trans * absorb * e.b;
                trans *= 1.0 - absorb;
            }
            var deep = Lut(3, mu, Math.Max((Sim.DeepKelvin + theta * 0.6) * limb, 20.0));
            r += trans * deep.r; g += trans * deep.g; b += trans * deep.b;
            if (View == FluidView.ThermalInfrared)
            {
                var dp = Lut(3, 1.0, Math.Max(Sim.DeepKelvin, 20.0));
                double top = Lut(0, 1.0, Math.Max(Sim.DeckKelvin[0], 20.0)).g;
                double x = Math.Clamp(Math.Log(Math.Max(g, 1e-30) / top) / Math.Log(dp.g / top), 0.0, 1.0);
                x = Math.Pow(x, 0.7) * (0.6 + 0.4 * Math.Sqrt(mu));
                var rc = Ramp(x);
                double sc = dp.g * SunDiscRadiance * Boost;
                return (rc.r * sc, rc.g * sc, rc.b * sc);
            }
            r *= SunDiscRadiance * Boost; g *= SunDiscRadiance * Boost; b *= SunDiscRadiance * Boost;
            if (StarStrength > 0.0 && nl > 0.0)
            {
                // Reflected light: the decks' albedo colours weighted by what is on top; the Sun at 1 AU lights a flat white disc at about 0.8.
                double rr = 0, gg = 0, bb = 0, t2 = 1.0;
                for (int d = 0; d < 3; d++)
                {
                    double tau = Sim.DeckSpecs[d].Tau * Math.Max(f[d], 0.0);
                    double cover = 1.0 - Math.Exp(-tau);
                    DeckSpec s = Sim.DeckSpecs[d];
                    rr += t2 * cover * s.Albedo * s.ReflectR; gg += t2 * cover * s.Albedo * s.ReflectG; bb += t2 * cover * s.Albedo * s.ReflectB;
                    t2 *= 1.0 - cover;
                }
                double k = StarStrength * nl * 0.8;
                r += k * rr; g += k * gg; b += k * bb;
            }
            return (r, g, b);
        }
    }

    private static double[] RenderDisc(AtmosphereSimulation sim, int w, int h, double inc, double phase, double boost, double star)
    {
        var shader = new Shader(sim, boost, star);
        var img = new double[w * h * 3];
        // Observer direction and screen axes in the planet frame (z north).
        var o = new[] { Math.Cos(inc) * Math.Cos(phase), Math.Cos(inc) * Math.Sin(phase), Math.Sin(inc) };
        double[] zAxis = { 0, 0, 1 };
        double dz = o[2];
        var up = new[] { zAxis[0] - dz * o[0], zAxis[1] - dz * o[1], zAxis[2] - dz * o[2] };
        double ul = Math.Sqrt(up[0] * up[0] + up[1] * up[1] + up[2] * up[2]);
        for (int i = 0; i < 3; i++) up[i] /= ul;
        var right = new[] { -o[1] * up[2] + o[2] * up[1], -o[2] * up[0] + o[0] * up[2], -o[0] * up[1] + o[1] * up[0] };   // (-o) x up
        // Star from the upper left in front of the viewer for the reflected light.
        var starDir = Norm(new[] { o[0] * 0.5 - right[0] * 0.8 + up[0] * 0.3, o[1] * 0.5 - right[1] * 0.8 + up[1] * 0.3, o[2] * 0.5 - right[2] * 0.8 + up[2] * 0.3 });
        double radius = 0.45 * Math.Min(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double sx = (x + 0.5 - w / 2.0) / radius, sy = -(y + 0.5 - h / 2.0) / radius;
                double r2 = sx * sx + sy * sy;
                int idx = (y * w + x) * 3;
                if (r2 >= 1.0) continue;
                double mu = Math.Sqrt(1.0 - r2);
                var p = new[] { right[0] * sx + up[0] * sy + o[0] * mu, right[1] * sx + up[1] * sy + o[1] * mu, right[2] * sx + up[2] * sy + o[2] * mu };
                double lat = Math.Asin(Math.Clamp(p[2], -1.0, 1.0));
                double lon = Math.Atan2(p[1], p[0]);
                if (lon < 0) lon += 2 * Math.PI;
                double nl = p[0] * starDir[0] + p[1] * starDir[1] + p[2] * starDir[2];
                var c = shader.Shade(lat, lon, mu, nl);
                // One pixel of anti-aliasing at the limb.
                double edge = Math.Clamp((1.0 - Math.Sqrt(r2)) * radius, 0.0, 1.0);
                img[idx] = c.r * edge; img[idx + 1] = c.g * edge; img[idx + 2] = c.b * edge;
            }
        return img;
    }

    private static double[] RenderMap(AtmosphereSimulation sim, int w, int h, double boost, double star)
    {
        var shader = new Shader(sim, boost, star);
        var img = new double[w * h * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double lat = Math.PI / 2 - (y + 0.5) / h * Math.PI;
                double lon = (x + 0.5) / w * 2 * Math.PI;
                var c = shader.Shade(lat, lon, 1.0, 1.0);
                int idx = (y * w + x) * 3;
                img[idx] = c.r; img[idx + 1] = c.g; img[idx + 2] = c.b;
            }
        return img;
    }

    private static double[] Norm(double[] v)
    {
        double l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        return new[] { v[0] / l, v[1] / l, v[2] / l };
    }

    /// <summary>
    /// Exposure as a camera does: the 99th percentile of the lit pixels is set to 0.8 (the adaptation the game's auto exposure
    /// does for a dark scene), then a gentle highlight roll-off and the sRGB curve.
    /// </summary>
    private static void Normalise(double[] img, double ev, out byte[] bytes)
    {
        var lum = new List<double>();
        for (int i = 0; i < img.Length; i += 3)
        {
            double l = 0.2126 * img[i] + 0.7152 * img[i + 1] + 0.0722 * img[i + 2];
            if (l > 0.0) lum.Add(l);
        }
        lum.Sort();
        double ref99 = lum.Count > 0 ? lum[(int)(lum.Count * 0.99)] : 1.0;
        double scale = 0.8 / Math.Max(ref99, 1e-30) * ev;
        bytes = new byte[img.Length];
        for (int i = 0; i < img.Length; i++)
        {
            double v = img[i] * scale;
            v = v / (1.0 + 0.25 * v);
            v = Math.Clamp(v, 0.0, 1.0);
            v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055;
            bytes[i] = (byte)Math.Round(v * 255.0);
        }
    }
}
