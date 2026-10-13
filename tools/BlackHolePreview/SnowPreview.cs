using System;
using System.Globalization;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Seasons;
using KSPVE.Pure.Textures;

/// <summary>
/// snow earth|kerbin out.png [size]: the snow, glacier and sea ice maps of a world over its year, four seasons in a 2 x 2
/// picture (a quarter of the year apart, top row first), over a made-up world of continents and mountain ranges, from the
/// real Pure models (energy balance, snow pack, snow field). Settings from the environment: SN_TILT (axial tilt, degrees;
/// default Earth's 23.44, Kerbin's 0), SN_ECC (eccentricity), SN_MOISTURE (1 = Earth), SN_MEAN (global mean temperature, K),
/// SN_SEED (the world), SN_OCEAN (sea level in the terrain noise, 0..1, default 0.5).
/// </summary>
internal static class SnowPreview
{
    private static double Env(string name, double fallback) =>
        double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    public static int Run(string[] args)
    {
        string world = args.Length > 1 ? args[1] : "earth";
        string output = args.Length > 2 ? args[2] : "snow-preview.png";
        int width = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 512;
        int height = width / 2;
        bool kerbin = world == "kerbin";
        double tilt = Env("SN_TILT", kerbin ? 0.0 : 23.44) * Math.PI / 180.0;
        double ecc = Env("SN_ECC", kerbin ? 0.0 : 0.0167);
        double year = kerbin ? 9203545.0 : SnowParameters.EarthYear;
        double perihelion = kerbin ? 0.0 : 283.0 * Math.PI / 180.0;
        double a = -Math.Sin(tilt) * Math.Sin(perihelion), b = -Math.Sin(tilt) * Math.Cos(perihelion);
        var spin = new Double3(a, b, Math.Cos(tilt));
        SeasonOrbit orbit = SeasonOrbit.FromAxis(spin, new Double3(1, 0, 0), new Double3(0, 1, 0), ecc, year);
        var parameters = new ClimateParameters
        {
            SolarConstant = 1361.0,
            MeanTemperature = Env("SN_MEAN", 288.15),
            SolarDay = kerbin ? 21600.0 : 86400.0,
            GravityRatio = kerbin ? 1.0 : 1.0,
            OceanShare = 0.7,
        };
        int seed = (int)Env("SN_SEED", 3);
        double sea = Env("SN_OCEAN", 0.5);

        // A made-up world: continents from low frequency noise, ranges from ridged noise, sea level where the share is right.
        const int TW = 128, TH = 64;
        var heights = new float[TW * TH];
        for (int r = 0; r < TH; r++)
            for (int c = 0; c < TW; c++)
            {
                double lat = -90.0 + (r + 0.5) * 180.0 / TH, lon = (c + 0.5) * 360.0 / TW;
                double x = Math.Cos(lat * Math.PI / 180) * Math.Cos(lon * Math.PI / 180), y = Math.Cos(lat * Math.PI / 180) * Math.Sin(lon * Math.PI / 180), z = Math.Sin(lat * Math.PI / 180);
                double continent = Fbm(x * 1.6, y * 1.6, z * 1.6, seed, 4);
                double ridge = 1.0 - Math.Abs(2.0 * Fbm(x * 5, y * 5, z * 5, seed + 11, 3) - 1.0);
                double h = (continent - sea) * 6000.0;
                if (h > 0) h += ridge * ridge * 3500.0 * Math.Min(h / 1200.0, 1.0);
                heights[r * TW + c] = (float)h;
            }
        // Make the map's longitude 0 at the left: texel columns are -180..180, so shift by half.
        var shifted = new float[heights.Length];
        for (int r = 0; r < TH; r++)
            for (int c = 0; c < TW; c++) shifted[r * TW + c] = heights[r * TW + (c + TW / 2) % TW];
        var terrain = new GreyMap(TW, TH, shifted, false);
        int wet = 0;
        foreach (float h in shifted) if (h < 0) wet++;
        parameters.OceanShare = (double)wet / shifted.Length;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var balance = new EnergyBalance(parameters, orbit);
        var climate = new PlanetClimate(balance, 9.81, null);
        var weather = new Weather(KSPVE.Pure.Clouds.Climate.For(kerbin ? 600000.0 : 6371000.0, kerbin ? 21549.4 : 86164.0, 3));
        var snowParameters = new SnowParameters { Moisture = Env("SN_MOISTURE", 1.0) };
        var snow = new SnowClimatology(climate, snowParameters, PrecipitationClimate.From(weather, orbit, snowParameters));
        var field = new SnowField(snow, terrain, true);
        Console.WriteLine($"{world}: tilt {tilt * 180 / Math.PI:F2} deg, e {ecc}, tau {balance.Opacity:F3}, global mean {balance.GlobalMean - 273.15:F1} C, models in {sw.ElapsedMilliseconds} ms");

        var rgb = new byte[2 * width * 2 * height * 3];
        double[] phases = { 0.125, 0.375, 0.625, 0.875 };
        for (int q = 0; q < 4; q++)
        {
            field.Update(phases[q], null);
            int ox = (q % 2) * width, oy = (q / 2) * height;
            double landSnow = 0, landArea = 0, seaIce = 0, seaArea = 0;
            for (int py = 0; py < height; py++)
            {
                double v = 1.0 - (py + 0.5) / height;   // north at the top
                double cos = Math.Cos((v - 0.5) * Math.PI);
                for (int px = 0; px < width; px++)
                {
                    double u = (px + 0.5) / width;
                    double z = terrain.Sample(u, v);
                    double sn = field.Snow.Sample(u, v), ice = field.Ice.Sample(u, v), si = field.SeaIce.Sample(u, v);
                    double r, g, bl;
                    if (z < 0.0)
                    {
                        double depth = Math.Min(-z / 4000.0, 1.0);
                        r = 0.05 - 0.03 * depth; g = 0.25 - 0.12 * depth; bl = 0.45 - 0.17 * depth;
                        r += (0.86 - r) * si; g += (0.9 - g) * si; bl += (0.94 - bl) * si;
                        seaArea += cos; seaIce += si * cos;
                    }
                    else
                    {
                        double t = Math.Min(z / 4500.0, 1.0);
                        r = 0.22 + 0.33 * t; g = 0.38 - 0.06 * t; bl = 0.14 + 0.2 * t;
                        double lat = Math.Abs(v - 0.5) * 2;
                        if (lat > 0.55 && lat < 0.8) { r *= 0.8; g *= 0.9; }
                        r += (0.86 - r) * sn; g += (0.88 - g) * sn; bl += (0.9 - bl) * sn;
                        r += (0.55 - r) * ice; g += (0.72 - g) * ice; bl += (0.85 - bl) * ice;
                        landArea += cos; landSnow += Math.Max(sn, ice) * cos;
                    }
                    int i = ((oy + py) * 2 * width + ox + px) * 3;
                    rgb[i] = (byte)(255 * Math.Pow(Math.Clamp(r, 0, 1), 1 / 2.2));
                    rgb[i + 1] = (byte)(255 * Math.Pow(Math.Clamp(g, 0, 1), 1 / 2.2));
                    rgb[i + 2] = (byte)(255 * Math.Pow(Math.Clamp(bl, 0, 1), 1 / 2.2));
                }
            }
            double dec = orbit.DeclinationAt(phases[q]) * 180 / Math.PI;
            Console.WriteLine($"year {phases[q]:F3}: sun over {dec:+0.0;-0.0} deg, N hemisphere {orbit.SeasonAt(phases[q], true)}, snow on {100 * landSnow / landArea:F1}% of land, sea ice on {100 * seaIce / Math.Max(seaArea, 1e-9):F1}% of sea; T equator {climate.SeaLevel(0, phases[q]) - 273.15:F1} C, 60N {climate.SeaLevel(60 * Math.PI / 180, phases[q]) - 273.15:F1} C, 60S {climate.SeaLevel(-60 * Math.PI / 180, phases[q]) - 273.15:F1} C");
        }
        Program.WritePngPublic(output, 2 * width, 2 * height, rgb);
        Console.WriteLine("wrote " + output);
        return 0;
    }

    private static double Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    private static double Noise(double x, double y, double z, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        double fx = x - xi, fy = y - yi, fz = z - zi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        double L(double p, double q, double t) => p + (q - p) * t;
        return L(L(L(Hash(xi, yi, zi, seed), Hash(xi + 1, yi, zi, seed), fx), L(Hash(xi, yi + 1, zi, seed), Hash(xi + 1, yi + 1, zi, seed), fx), fy),
                 L(L(Hash(xi, yi, zi + 1, seed), Hash(xi + 1, yi, zi + 1, seed), fx), L(Hash(xi, yi + 1, zi + 1, seed), Hash(xi + 1, yi + 1, zi + 1, seed), fx), fy), fz);
    }

    private static double Fbm(double x, double y, double z, int seed, int octaves)
    {
        double v = 0, a = 0.5, norm = 0;
        for (int i = 0; i < octaves; i++) { v += a * Noise(x, y, z, seed + i); norm += a; x *= 2; y *= 2; z *= 2; a *= 0.5; }
        return v / norm;
    }
}
