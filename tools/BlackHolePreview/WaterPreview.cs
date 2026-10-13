using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using KSPVE.Pure.Water;
using KSPVE.Pure.Water.Hydrology;
using D3 = KSPVE.Pure.Numerics.Double3;

/// <summary>
/// Offline looks at KSPVE's water, with the real KSPVE.Pure maths and a CPU copy of Water/*.shader's shading.
///   water-ocean out.png [width height]   a patch of sea. Environment: WP_WIND (m/s at 10 m, 10), WP_FETCH (m, 2e5), WP_CAM (camera
///     height m, 4), WP_PITCH (deg below the horizon, 6), WP_SUN (sun elevation deg, 28), WP_SUNAZ (deg from the view direction, 160),
///     WP_CHL (chlorophyll mg/m3, 0.1), WP_TIME (s), WP_FOV (deg, 60), WP_EXPOSURE, WP_DEPTH (m of water over a sandy floor, 0 = deep),
///     WP_WAVESCALE (1), WP_CHOP (1), WP_SEED.
///   water-rivers out.png [size]          a synthetic landscape's rivers and lakes from above, then an oblique view of the water
///     meshes over the terrain (out_oblique.png). Environment: WP_SEED, WP_RAIN (humidity 0..1, 0.6), WP_MINQ (m3/s, 0.5), WP_CELL (m, 125).
/// </summary>
internal static class WaterPreview
{
    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    private static float Smooth(float a, float b, float x) { float t = Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    public static int Run(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: water-ocean|water-rivers out.png [...]"); return 1; }
        return args[0] == "water-ocean" ? Ocean(args) : Rivers(args);
    }

    // ------------------------------------------------------------------ ocean

    private sealed class Layer
    {
        public OceanCascade Cascade;
        public CascadeField Field;
        public double Length;
        public int N;
        public double Fade0, Fade1;   // the footprint (m) over which the layer fades out

        /// <summary>Bilinear periodic sample of one field at a horizontal position.</summary>
        public float Sample(double[] data, double x, double z)
        {
            double u = x / Length * N, v = z / Length * N;
            double fu = Math.Floor(u), fv = Math.Floor(v);
            int x0 = ((int)fu % N + N) % N, y0 = ((int)fv % N + N) % N, x1 = (x0 + 1) % N, y1 = (y0 + 1) % N;
            double tx = u - fu, ty = v - fv;
            double a = data[y0 * N + x0] * (1 - tx) + data[y0 * N + x1] * tx, b = data[y1 * N + x0] * (1 - tx) + data[y1 * N + x1] * tx;
            return (float)(a * (1 - ty) + b * ty);
        }
    }

    private static Layer[] BuildSea(double wind, double fetch, double g, double time, ulong seed, int n)
    {
        double[] lengths = { 1024.0, 128.0, 16.0, 2.0 };
        var layers = new Layer[lengths.Length];
        double lowK = 2.0 * Math.PI / lengths[0] * 0.99;
        for (int i = 0; i < lengths.Length; i++)
        {
            double kHigh = i + 1 < lengths.Length ? 0.5 * Math.PI * n / lengths[i] : Math.PI * n / lengths[i] * 0.9;
            double kLow = i == 0 ? lowK : 0.5 * Math.PI * n / lengths[i - 1];
            var l = new Layer { N = n, Length = lengths[i], Field = new CascadeField(n) };
            l.Cascade = new OceanCascade(n, lengths[i], kLow, kHigh, wind, 0.0, fetch, g, double.PositiveInfinity, seed + (ulong)i * 7919UL);
            l.Cascade.Evaluate(time, l.Field);
            layers[i] = l;
        }
        return layers;
    }

    private static Vector3 Sky(Vector3 d, Vector3 sun)
    {
        float y = Math.Max(d.Y, 0f);
        // Clear sky: blue overhead, paler at the horizon (the sky's radiance in units where the sun's irradiance is 1 on a surface facing it).
        Vector3 zenith = new Vector3(0.05f, 0.13f, 0.32f), horizon = new Vector3(0.28f, 0.38f, 0.52f);
        Vector3 c = Vector3.Lerp(horizon, zenith, MathF.Pow(y, 0.55f));
        float cs = Math.Max(Vector3.Dot(d, sun), 0f);
        c += new Vector3(1f, 0.85f, 0.65f) * (0.18f * MathF.Pow(cs, 8f) + 0.5f * MathF.Pow(cs, 90f));
        if (cs > 0.99996f) c += new Vector3(1f, 0.95f, 0.85f) * 6000f;   // the disc: 0.27 degree half angle, E_sun / solid angle
        return c;
    }

    private static int Ocean(string[] args)
    {
        string output = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 960, height = args.Length > 3 ? int.Parse(args[3]) : 540;
        double wind = Env("WP_WIND", 10.0), fetch = Env("WP_FETCH", 2e5), g = OceanSpectrum.EarthGravity;
        double camH = Env("WP_CAM", 4.0), pitch = Env("WP_PITCH", 6.0) * Math.PI / 180.0, fov = Env("WP_FOV", 60.0) * Math.PI / 180.0;
        double sunElev = Env("WP_SUN", 28.0) * Math.PI / 180.0, sunAz = Env("WP_SUNAZ", 160.0) * Math.PI / 180.0;
        double chl = Env("WP_CHL", 0.1), time = Env("WP_TIME", 20.0), exposure = Env("WP_EXPOSURE", 1.6), floorDepth = Env("WP_DEPTH", 0.0);
        double waveScale = Env("WP_WAVESCALE", 1.0), chop = Env("WP_CHOP", 1.0);
        ulong seed = (ulong)Env("WP_SEED", 3.0);

        Layer[] layers = BuildSea(wind, fetch, g, time, seed, 128);
        SeaState sea = SeaState.From(wind, fetch, g);
        double cover = Math.Max(sea.WhitecapCoverage, 1e-6);
        double sigmaSlope = Math.Sqrt(OceanSpectrum.SlopeVariance(2.0 * Math.PI / 1024.0, 100.0, Math.Max(wind, 0.1), fetch, double.PositiveInfinity, g));
        double jacThreshold = Whitecaps.JacobianThreshold(cover, chop, sigmaSlope);
        Console.WriteLine($"wind {wind} m/s fetch {fetch / 1000:F0} km: Hs {sea.SignificantHeight:F2} m, peak wavelength {sea.PeakWavelength:F0} m, whitecaps {100 * sea.WhitecapCoverage:F2} %, slope variance {sea.SlopeVariance:F4}, J threshold {jacThreshold:F2}");

        Vector3 sun = Vector3.Normalize(new Vector3((float)(Math.Cos(sunElev) * Math.Sin(sunAz)), (float)Math.Sin(sunElev), (float)(Math.Cos(sunElev) * Math.Cos(sunAz))));
        D3 deep = WaterOptics.DeepReflectance(chl), absorb = WaterOptics.Absorption(chl), bb = WaterOptics.Backscattering(chl);
        var rgb = new byte[width * height * 3];
        float aspect = width / (float)height;
        float tanHalf = (float)Math.Tan(fov / 2.0);
        // Camera looks along +z (the wind blows towards +x, across the view), pitched down.
        Vector3 forward = new Vector3(0f, (float)-Math.Sin(pitch), (float)Math.Cos(pitch)), right = Vector3.UnitX, up = Vector3.Cross(forward, right);

        double hmax = 0.0;
        foreach (Layer l in layers) foreach (double h in l.Field.Height) hmax = Math.Max(hmax, Math.Abs(h));
        hmax = Math.Max(hmax, 0.05) * waveScale;

        Parallel.For(0, height, py =>
        {
            for (int px = 0; px < width; px++)
            {
                float sx = (2f * (px + 0.5f) / width - 1f) * tanHalf * aspect, sy = (1f - 2f * (py + 0.5f) / height) * tanHalf;
                Vector3 dir = Vector3.Normalize(forward + right * sx + up * sy);
                Vector3 o = new Vector3(0f, (float)camH, 0f);
                Vector3 col;
                if (dir.Y >= -1e-4f) col = Sky(dir, sun);
                else
                {
                    // March from the top of the waves to the bottom, then bisect.
                    double tTop = (camH - hmax) / -dir.Y, tBottom = (camH + hmax) / -dir.Y, tHit = tTop;
                    double step = Math.Max((tBottom - tTop) / 24.0, 0.02);
                    double prevT = tTop;
                    bool found = false;
                    double Height(double t)
                    {
                        double px3 = o.X + t * dir.X, pz3 = o.Z + t * dir.Z, hh = 0.0;
                        foreach (Layer l in layers) hh += l.Sample(l.Field.Height, px3, pz3);
                        return o.Y + t * dir.Y - hh * waveScale;
                    }
                    for (double t = tTop; t <= tBottom + step; t += step)
                    {
                        if (Height(t) <= 0.0) { found = true; double lo = prevT, hi = t; for (int it = 0; it < 14; it++) { double mid = 0.5 * (lo + hi); if (Height(mid) <= 0.0) hi = mid; else lo = mid; } tHit = 0.5 * (lo + hi); break; }
                        prevT = t;
                    }
                    if (!found) tHit = camH / -dir.Y;
                    float dist = (float)tHit;
                    double x = o.X + tHit * dir.X, z = o.Z + tHit * dir.Z;
                    // The footprint of a pixel at this distance decides which waves are still resolved (the rest go into roughness).
                    float footprint = dist * 2f * tanHalf / height / Math.Max(-dir.Y, 0.02f);
                    double slopeX = 0.0, slopeZ = 0.0, dxx = 0.0, dzz = 0.0, dxz = 0.0;
                    double resolvedK = Math.PI / Math.Max(footprint, 1e-3);
                    foreach (Layer l in layers)
                    {
                        double cell = l.Length / l.N;
                        float w = 1f - Smooth((float)(0.5 * cell), (float)(4.0 * cell), footprint * 0.5f);
                        if (w <= 0f) continue;
                        slopeX += w * l.Sample(l.Field.SlopeX, x, z); slopeZ += w * l.Sample(l.Field.SlopeZ, x, z);
                        dxx += w * l.Sample(l.Field.Dxx, x, z); dzz += w * l.Sample(l.Field.Dzz, x, z); dxz += w * l.Sample(l.Field.Dxz, x, z);
                    }
                    slopeX *= waveScale; slopeZ *= waveScale;
                    // The roughness the unresolved waves would add: Cox and Munk's total minus what is resolved here.
                    double resolvedVar = OceanSpectrum.SlopeVariance(2.0 * Math.PI / 1024.0, Math.Max(resolvedK, 0.01), Math.Max(wind, 0.1), fetch, double.PositiveInfinity, g, 120) * waveScale * waveScale;
                    float extraVar = (float)Math.Max(sea.SlopeVariance - resolvedVar, 0.0) * 0.5f;   // per axis
                    Vector3 n = Vector3.Normalize(new Vector3((float)-slopeX, 1f, (float)-slopeZ));
                    Vector3 v = -dir;
                    float cosV = Math.Max(Vector3.Dot(n, v), 0.02f);
                    float fres = (float)WaterOptics.Fresnel(cosV);
                    // Sky reflection (the rough surface averages the sky over its lobe: the horizon creeps up with roughness).
                    Vector3 r = Vector3.Reflect(dir, n);
                    if (r.Y < 0.01f) r = new Vector3(r.X, 0.01f + (0.01f - r.Y) * 0.3f, r.Z);
                    r = Vector3.Normalize(r);
                    Vector3 reflected = Sky(r, sun) * fres;
                    // Sun glitter: Gaussian microfacet distribution (slope variance = resolved facet + the unresolved) and the Fresnel of the facet.
                    Vector3 h = Vector3.Normalize(sun + v);
                    float nh = Math.Max(Vector3.Dot(n, h), 1e-3f);
                    float tan2 = (1f - nh * nh) / (nh * nh);
                    float sigma2 = Math.Max(extraVar, 2e-5f);
                    float d = MathF.Exp(-tan2 / (2f * sigma2)) / (2f * MathF.PI * sigma2 * nh * nh * nh * nh);
                    float cosL = Math.Max(Vector3.Dot(n, sun), 0f);
                    float fh = (float)WaterOptics.Fresnel(Math.Max(Vector3.Dot(sun, h), 0f));
                    float glint = fh * d / (4f * cosV) * (cosL > 0f ? 1f : 0f);
                    reflected += new Vector3(1f, 0.93f, 0.82f) * glint;
                    // Light from within the water.
                    float cosSun = Math.Max(sun.Y, 0.02f);
                    float sunIn = cosSun * (1f - (float)WaterOptics.Fresnel(cosSun));
                    Vector3 skyE = new Vector3(0.22f, 0.3f, 0.45f);
                    Vector3 irradiance = new Vector3(1f, 0.97f, 0.9f) * sunIn + skyE;
                    // Wave shading of the sun's share: facets turned from the sun get less (self-shadow roughly).
                    Vector3 body = new Vector3((float)deep.X, (float)deep.Y, (float)deep.Z) * irradiance / MathF.PI;
                    // Troughs are darker, crests lighter (a little light scattered through the crests, Tessendorf-style).
                    float crest = 1f;
                    foreach (Layer l in layers) crest += (float)(0.25 * l.Sample(l.Field.Height, x, z));
                    body *= Math.Max(crest, 0.5f);
                    if (floorDepth > 0.0)
                    {
                        double slant = floorDepth / Math.Max(WaterOptics.CosRefracted(cosV), 0.05);
                        D3 k = WaterOptics.TwoWayAttenuation(absorb, bb, WaterOptics.CosRefracted(cosSun), WaterOptics.CosRefracted(cosV));
                        D3 refl = WaterOptics.ShallowReflectance(deep, k, floorDepth, new D3(0.45, 0.4, 0.28));
                        body = new Vector3((float)refl.X, (float)refl.Y, (float)refl.Z) * irradiance / MathF.PI;
                        _ = slant;
                    }
                    body *= (1f - fres) / (float)(WaterOptics.SeawaterIndex * WaterOptics.SeawaterIndex);   // out through the surface: (1 - F) and the radiance falls by 1/n^2
                    // Whitecaps from the Jacobian (the folded part of the surface), with a bit of lacy texture from the fine cascades.
                    double jac = (1.0 + chop * dxx) * (1.0 + chop * dzz) - chop * chop * dxz * dxz;
                    float foam = Clamp01((float)((jacThreshold + 0.12 - jac) / 0.25));
                    if (floorDepth > 0.0)
                    {
                        // Waves running ashore: the crests of the swell tip over once the depth is within about a wave height of them.
                        float breaking = (float)Shoaling.BreakingFraction(sea.SignificantHeight, floorDepth);
                        float crestHeight = 0f;
                        for (int c = 0; c < 2; c++) crestHeight += layers[c].Sample(layers[c].Field.Height, x, z);
                        foam = Math.Max(foam, breaking * Smooth(0.15f * (float)sea.SignificantHeight, 0.45f * (float)sea.SignificantHeight, crestHeight));
                    }
                    float lace = 0.55f + 0.45f * Clamp01((float)(layers[3].Sample(layers[3].Field.Height, x * 3.1, z * 3.1) * 40.0 + 0.5));
                    foam *= lace;
                    Vector3 foamColor = new Vector3(0.9f, 0.92f, 0.95f) * (irradiance * 0.8f * Math.Max(cosL, 0.15f) / MathF.PI + skyE * 0.4f);
                    col = (body + reflected) * (1f - foam) + foamColor * foam;
                    // Distance haze towards the horizon sky.
                    float haze = 1f - MathF.Exp(-dist / 40000f);
                    col = Vector3.Lerp(col, Sky(Vector3.Normalize(new Vector3(dir.X, 0.01f, dir.Z)), sun) * 0.9f, haze);
                }
                col *= (float)exposure;
                Vector3 mapped = new Vector3(Tone(col.X), Tone(col.Y), Tone(col.Z));
                int i = (py * width + px) * 3;
                rgb[i] = (byte)(Srgb(mapped.X) * 255f + 0.5f); rgb[i + 1] = (byte)(Srgb(mapped.Y) * 255f + 0.5f); rgb[i + 2] = (byte)(Srgb(mapped.Z) * 255f + 0.5f);
            }
        });
        Program.WritePngPublic(output, width, height, rgb);
        Console.WriteLine("wrote " + output);
        return 0;
    }

    private static float Tone(float x) => x / (1f + x);   // Reinhard
    private static float Srgb(float v) => v <= 0.0031308f ? 12.92f * v : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;

    // ------------------------------------------------------------------ rivers

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    /// <summary>A mountainous landscape draining south to a coast: ridged fractal noise on a tilted base, with a few hollows.</summary>
    private static float[] Landscape(int n, double cell, int seed)
    {
        var z = new float[n * n];
        double size = n * cell;
        Parallel.For(0, n, y =>
        {
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float h = 0f, amp = 1f, freq = 3f, sum = 0f;
                for (int o = 0; o < 7; o++)
                {
                    float nv = ValueNoise(u * freq + 13.1f * o, v * freq + 7.7f * o, seed + o);
                    nv = 1f - MathF.Abs(2f * nv - 1f);   // ridged
                    h += amp * nv * nv; sum += amp; amp *= 0.5f; freq *= 2.05f;
                }
                h /= sum;
                float baseSlope = 0.85f * v;           // rises to the north
                float mountains = Smooth(0.2f, 0.9f, v);
                z[y * n + x] = (float)(-40.0 + 1900.0 * (baseSlope * 0.6f + h * (0.25f + 0.9f * mountains)) - 260.0);
            }
        });
        // A hollow or two in the middle ground, the sort of glacial basin that holds a lake.
        var rng = new Random(seed * 31 + 5);
        for (int k = 0; k < 3; k++)
        {
            float cx = (0.25f + 0.5f * (float)rng.NextDouble()) * n, cy = (0.3f + 0.4f * (float)rng.NextDouble()) * n, radius = (7f + 6f * (float)rng.NextDouble()) * 1f;
            float depth = 30f + 40f * (float)rng.NextDouble();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d < radius * 1.8f) z[y * n + x] -= depth * MathF.Exp(-(d * d) / (radius * radius));
                }
        }
        _ = size;
        return z;
    }

    private static int Rivers(string[] args)
    {
        string output = args[1];
        int size = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int seed = (int)Env("WP_SEED", 4.0);
        double cell = Env("WP_CELL", 125.0), humidity = Env("WP_RAIN", 0.6), minQ = Env("WP_MINQ", 0.5);
        const int n = 256;
        float[] terrain = Landscape(n, cell, seed);
        double precipitation = HydraulicGeometry.AnnualPrecipitation(humidity), runoff = HydraulicGeometry.AnnualRunoff(precipitation, 12.0);
        float perCell = (float)HydraulicGeometry.DischargeFromRunoff(runoff, cell * cell);
        Console.WriteLine($"precipitation {precipitation:F0} mm/yr, runoff {runoff:F0} mm/yr = {perCell * 1000:F3} L/s per cell");
        var input = new HydroInput { Width = n, Height = n, CellSize = cell, Terrain = terrain, Runoff = new float[n * n], SeaLevel = 0f, MinDischarge = minQ, LakeNetEvaporation = Math.Max(HydraulicGeometry.LakeEvaporation(Env("WP_LAT", 45.0) * Math.PI / 180.0) - precipitation, 0.0), LakeMinDepth = Env("WP_LAKEDEPTH", 4.0), LakeMinArea = Env("WP_LAKEAREA", 30.0) * cell * cell };
        for (int i = 0; i < input.Runoff.Length; i++) input.Runoff[i] = perCell;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        HydroResult r = HydroSolver.Solve(input);
        Console.WriteLine($"solved {n}x{n} in {watch.ElapsedMilliseconds} ms: {r.Reaches.Count} reaches, {r.Lakes.Count} lakes, max discharge {r.Discharge.Max():F0} m3/s");
        double length = 0.0;
        foreach (RiverReach reach in r.Reaches)
            for (int k = 1; k < reach.Points.Count; k++)
                length += Math.Sqrt(Math.Pow(reach.Points[k].X - reach.Points[k - 1].X, 2) + Math.Pow(reach.Points[k].Y - reach.Points[k - 1].Y, 2));
        Console.WriteLine($"river length {length / 1000:F0} km; widest {r.Reaches.Max(a => a.Points.Max(p => p.Channel.Width)):F0} m, fastest {r.Reaches.Max(a => a.Points.Max(p => p.Channel.Speed)):F1} m/s, waterfalls {r.Reaches.Sum(a => a.Points.Count(p => p.Fall > 0.5))} points");
        foreach (Lake l in r.Lakes.OrderByDescending(a => a.Area).Take(6)) Console.WriteLine($"lake {l.Id}: level {l.Level:F0} m, {l.Area / 1e6:F2} km2, depth {l.MaxDepth:F0} m");

        // ---- from above
        var rgb = new byte[size * size * 3];
        float scale = n / (float)size;
        float[] shade = Hillshade(terrain, n, cell);
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                int cx = Math.Min((int)(px * scale), n - 1), cy = Math.Min((int)((size - 1 - py) * scale), n - 1);
                int i = cy * n + cx;
                float h = terrain[i];
                Vector3 c;
                if (h <= 0f) c = new Vector3(0.05f, 0.18f, 0.38f);
                else
                {
                    float t = Clamp01(h / 1800f);
                    c = Vector3.Lerp(Vector3.Lerp(new Vector3(0.22f, 0.4f, 0.16f), new Vector3(0.45f, 0.38f, 0.26f), Smooth(0.0f, 0.5f, t)), new Vector3(0.9f, 0.9f, 0.92f), Smooth(0.6f, 0.95f, t));
                    c *= 0.45f + 0.8f * shade[i];
                }
                if (r.LakeIndex[i] >= 0) c = Vector3.Lerp(new Vector3(0.1f, 0.3f, 0.5f), new Vector3(0.02f, 0.1f, 0.25f), Clamp01(r.LakeDepth[i] / 25f));
                rgb[(py * size + px) * 3] = (byte)(Srgb(Clamp01(c.X)) * 255f); rgb[(py * size + px) * 3 + 1] = (byte)(Srgb(Clamp01(c.Y)) * 255f); rgb[(py * size + px) * 3 + 2] = (byte)(Srgb(Clamp01(c.Z)) * 255f);
            }
        float pxPerMetre = size / (float)(n * cell);
        foreach (RiverReach reach in r.Reaches)
            for (int k = 1; k < reach.Points.Count; k++)
            {
                RiverPoint a = reach.Points[k - 1], b = reach.Points[k];
                float widthPx = Math.Max((float)b.Channel.Width * pxPerMetre, 1.3f);
                Vector3 col = Vector3.Lerp(new Vector3(0.12f, 0.35f, 0.65f), new Vector3(0.85f, 0.92f, 1f), (float)b.Rapids);
                DrawLine(rgb, size, (float)a.X * pxPerMetre, size - (float)a.Y * pxPerMetre, (float)b.X * pxPerMetre, size - (float)b.Y * pxPerMetre, widthPx, col);
            }
        Program.WritePngPublic(output, size, size, rgb);
        Console.WriteLine("wrote " + output);

        // ---- oblique, through a software rasteriser: terrain and water meshes together
        string oblique = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output)) ?? ".", System.IO.Path.GetFileNameWithoutExtension(output) + "_oblique.png");
        Oblique(oblique, 1100, 620, terrain, n, cell, r, shade);
        return 0;
    }

    private static float[] Hillshade(float[] z, int n, double cell)
    {
        var s = new float[n * n];
        Vector3 light = Vector3.Normalize(new Vector3(-0.6f, 0.6f, 0.9f));
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dzdx = (z[y * n + Math.Min(x + 1, n - 1)] - z[y * n + Math.Max(x - 1, 0)]) / (float)(2 * cell);
                float dzdy = (z[Math.Min(y + 1, n - 1) * n + x] - z[Math.Max(y - 1, 0) * n + x]) / (float)(2 * cell);
                s[y * n + x] = Clamp01(Vector3.Dot(Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1f)), light));
            }
        return s;
    }

    private static void DrawLine(byte[] rgb, int size, float x0, float y0, float x1, float y1, float width, Vector3 c)
    {
        float dx = x1 - x0, dy = y1 - y0, len = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max((int)(len * 2f), 1);
        int radius = (int)MathF.Ceiling(width * 0.5f);
        for (int s = 0; s <= steps; s++)
        {
            float x = x0 + dx * s / steps, y = y0 + dy * s / steps;
            for (int oy = -radius; oy <= radius; oy++)
                for (int ox = -radius; ox <= radius; ox++)
                {
                    int px = (int)x + ox, py = (int)y + oy;
                    if (px < 0 || py < 0 || px >= size || py >= size) continue;
                    float d = MathF.Sqrt((px + 0.5f - x) * (px + 0.5f - x) + (py + 0.5f - y) * (py + 0.5f - y));
                    float a = Clamp01(width * 0.5f + 0.5f - d);
                    if (a <= 0f) continue;
                    int i = (py * size + px) * 3;
                    rgb[i] = (byte)(rgb[i] * (1 - a) + Srgb(c.X) * 255f * a); rgb[i + 1] = (byte)(rgb[i + 1] * (1 - a) + Srgb(c.Y) * 255f * a); rgb[i + 2] = (byte)(rgb[i + 2] * (1 - a) + Srgb(c.Z) * 255f * a);
                }
        }
    }

    private static void Oblique(string path, int w, int h, float[] terrain, int n, double cell, HydroResult r, float[] shade)
    {
        var rgb = new byte[w * h * 3];
        var depth = new float[w * h];
        var color = new Vector3[w * h];
        for (int i = 0; i < depth.Length; i++) { depth[i] = float.MaxValue; color[i] = new Vector3(0.55f, 0.7f, 0.9f) * (0.6f + 0.4f * (1f - (i / w) / (float)h)); }
        // Look at the lowest long river from above and behind, along the terrain.
        RiverReach main = r.Reaches.OrderByDescending(a => a.Points.Max(p => p.Channel.Width) * Math.Min(a.Points.Count, 60)).First();
        RiverPoint mid = main.Points[main.Points.Count / 3];
        RiverPoint ahead = main.Points[Math.Min(main.Points.Count / 3 + 14, main.Points.Count - 1)];
        Vector3 target = new Vector3((float)ahead.X, (float)ahead.Elevation, (float)ahead.Y);
        Vector3 back = Vector3.Normalize(new Vector3((float)(mid.X - ahead.X), 0f, (float)(mid.Y - ahead.Y)));
        Vector3 eye = new Vector3((float)mid.X, (float)mid.Elevation + 90f, (float)mid.Y) + back * 260f;
        Vector3 fwd = Vector3.Normalize(target - eye), rightV = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, fwd)), upV = Vector3.Cross(fwd, rightV);
        float tanHalf = MathF.Tan(0.5f * 62f * MathF.PI / 180f), aspect = w / (float)h;
        bool Project(Vector3 p, out Vector3 s)
        {
            Vector3 d = p - eye;
            float z = Vector3.Dot(d, fwd);
            s = default;
            if (z < 5f) return false;
            s = new Vector3(w * (0.5f + 0.5f * Vector3.Dot(d, rightV) / (z * tanHalf * aspect)), h * (0.5f - 0.5f * Vector3.Dot(d, upV) / (z * tanHalf)), z);
            return true;
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 ca, Vector3 cb, Vector3 cc, float bias)
        {
            if (!Project(a, out Vector3 sa) || !Project(b, out Vector3 sb) || !Project(c, out Vector3 sc)) return;
            float minX = MathF.Max(MathF.Min(sa.X, MathF.Min(sb.X, sc.X)), 0), maxX = MathF.Min(MathF.Max(sa.X, MathF.Max(sb.X, sc.X)), w - 1);
            float minY = MathF.Max(MathF.Min(sa.Y, MathF.Min(sb.Y, sc.Y)), 0), maxY = MathF.Min(MathF.Max(sa.Y, MathF.Max(sb.Y, sc.Y)), h - 1);
            float area = (sb.X - sa.X) * (sc.Y - sa.Y) - (sc.X - sa.X) * (sb.Y - sa.Y);
            if (MathF.Abs(area) < 1e-6f) return;
            for (int y = (int)minY; y <= (int)maxY; y++)
                for (int x = (int)minX; x <= (int)maxX; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((sb.X - px) * (sc.Y - py) - (sc.X - px) * (sb.Y - py)) / area;
                    float w1 = ((sc.X - px) * (sa.Y - py) - (sa.X - px) * (sc.Y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    float z = w0 * sa.Z + w1 * sb.Z + w2 * sc.Z - bias;
                    int i = y * w + x;
                    if (z >= depth[i]) continue;
                    depth[i] = z;
                    color[i] = w0 * ca + w1 * cb + w2 * cc;
                }
        }
        Vector3 TerrainColor(int i)
        {
            float t = Clamp01(terrain[i] / 1800f);
            Vector3 c = Vector3.Lerp(Vector3.Lerp(new Vector3(0.22f, 0.4f, 0.16f), new Vector3(0.45f, 0.38f, 0.26f), Smooth(0.0f, 0.5f, t)), new Vector3(0.9f, 0.9f, 0.92f), Smooth(0.6f, 0.95f, t));
            return c * (0.45f + 0.8f * shade[i]);
        }
        for (int y = 0; y + 1 < n; y++)
            for (int x = 0; x + 1 < n; x++)
            {
                int i00 = y * n + x, i10 = i00 + 1, i01 = i00 + n, i11 = i01 + 1;
                Vector3 p00 = new Vector3((float)((x + 0.5) * cell), Math.Max(terrain[i00], 0f), (float)((y + 0.5) * cell)), p10 = new Vector3((float)((x + 1.5) * cell), Math.Max(terrain[i10], 0f), (float)((y + 0.5) * cell));
                Vector3 p01 = new Vector3((float)((x + 0.5) * cell), Math.Max(terrain[i01], 0f), (float)((y + 1.5) * cell)), p11 = new Vector3((float)((x + 1.5) * cell), Math.Max(terrain[i11], 0f), (float)((y + 1.5) * cell));
                Vector3 sea = new Vector3(0.05f, 0.18f, 0.38f);
                Vector3 c00 = terrain[i00] <= 0f ? sea : TerrainColor(i00), c10 = terrain[i10] <= 0f ? sea : TerrainColor(i10), c01 = terrain[i01] <= 0f ? sea : TerrainColor(i01), c11 = terrain[i11] <= 0f ? sea : TerrainColor(i11);
                Triangle(p00, p01, p10, c00, c01, c10, 0f);
                Triangle(p10, p01, p11, c10, c01, c11, 0f);
            }
        // The water: rivers and lakes as built for the game, lifted a little over the ground.
        WaterMeshData rivers = WaterMeshes.Rivers(r, 0.5), lakes = WaterMeshes.Lakes(r);
        foreach (WaterMeshData m in new[] { rivers, lakes })
        {
            for (int t = 0; t + 2 < m.Indices.Count; t += 3)
            {
                Vector3 P(int idx) => new Vector3(m.East[idx], m.Altitude[idx], m.North[idx]);
                Vector3 C(int idx)
                {
                    float d = Clamp01(m.Depth[idx] / (m == lakes ? 25f : 2f));
                    Vector3 shallow = new Vector3(0.18f, 0.42f, 0.55f), deep = new Vector3(0.04f, 0.16f, 0.32f);
                    Vector3 c = Vector3.Lerp(shallow, deep, d);
                    return Vector3.Lerp(c, new Vector3(0.9f, 0.95f, 1f), Math.Max(m.Rapids[idx], m.Fall[idx]) * 0.8f);
                }
                int a = m.Indices[t], b = m.Indices[t + 1], c2 = m.Indices[t + 2];
                Triangle(P(a), P(b), P(c2), C(a), C(b), C(c2), 0f);
            }
        }
        for (int i = 0; i < depth.Length; i++)
        {
            rgb[i * 3] = (byte)(Srgb(Clamp01(color[i].X)) * 255f); rgb[i * 3 + 1] = (byte)(Srgb(Clamp01(color[i].Y)) * 255f); rgb[i * 3 + 2] = (byte)(Srgb(Clamp01(color[i].Z)) * 255f);
        }
        Program.WritePngPublic(path, w, h, rgb);
        Console.WriteLine("wrote " + path);
    }
}
