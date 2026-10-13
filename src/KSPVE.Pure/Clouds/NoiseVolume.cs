using System;
using System.Globalization;

namespace KSPVE.Pure.Clouds;

/// <summary>
/// The tiling 3D noise a layer's clouds are carved from, generated from its noise settings (<see cref="CloudNoise"/>):
/// Worley noise (billowing cells; "spherical" makes them puffier), Perlin noise (soft gradients), or both mixed as
/// Perlin-Worley (Perlin noise remapped by the Worley cells: billows with soft insides). Values 0..1 over a cube of
/// Size^3 voxels that tiles in every direction, x fastest. The same settings give the same noise on any computer, so the
/// CPU preview and the GPU sample identical data.
/// </summary>
public sealed class NoiseVolume
{
    public readonly int Size;
    public readonly float[] Data;

    private NoiseVolume(int size, float[] data)
    {
        Size = size;
        Data = data;
    }

    /// <summary>A key naming these settings at this size: layers with the same key can share one volume.</summary>
    public static string KeyOf(CloudNoise noise, int size)
    {
        string Octaves(NoiseOctaves o) => o == null ? "-" : string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}", o.Octaves, o.Periods, o.Persistence, o.Lacunarity, o.Spherical);
        return size.ToString(CultureInfo.InvariantCulture) + "|" + Octaves(noise?.Worley) + "|" + Octaves(noise?.Perlin);
    }

    public static NoiseVolume Generate(CloudNoise noise, int size, ulong seed = 0x5EED)
    {
        size = Math.Max(size, 4);
        var data = new float[size * size * size];
        NoiseOctaves worley = noise?.Worley, perlin = noise?.Perlin;
        if (worley == null && perlin == null)
        {
            for (int i = 0; i < data.Length; i++) data[i] = 1f;   // no noise: the clouds' shape is their coverage alone
            return new NoiseVolume(size, data);
        }
        float[] w = worley != null ? Fbm(worley, size, seed, true) : null;
        float[] p = perlin != null ? Fbm(perlin, size, seed ^ 0xA5A5A5A5UL, false) : null;
        for (int i = 0; i < data.Length; i++)
        {
            if (w != null && p != null)
            {
                // Perlin-Worley: the Perlin noise remapped from [worley - 1, 1] to [0, 1].
                double low = w[i] - 1.0;
                data[i] = (float)Math.Min(Math.Max((p[i] - low) / (1.0 - low), 0.0), 1.0);
            }
            else data[i] = w != null ? w[i] : p[i];
        }
        Stretch(data);
        return new NoiseVolume(size, data);
    }

    /// <summary>The noise at (u, v, w) in tiles (1 = the cube's width), trilinear, wrapping.</summary>
    public float Sample(double u, double v, double w)
    {
        double x = Frac(u) * Size - 0.5, y = Frac(v) * Size - 0.5, z = Frac(w) * Size - 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
        float fx = (float)(x - x0), fy = (float)(y - y0), fz = (float)(z - z0);
        int x1 = Wrap(x0 + 1), y1 = Wrap(y0 + 1), z1 = Wrap(z0 + 1);
        x0 = Wrap(x0); y0 = Wrap(y0); z0 = Wrap(z0);
        float c00 = Lerp(At(x0, y0, z0), At(x1, y0, z0), fx), c10 = Lerp(At(x0, y1, z0), At(x1, y1, z0), fx);
        float c01 = Lerp(At(x0, y0, z1), At(x1, y0, z1), fx), c11 = Lerp(At(x0, y1, z1), At(x1, y1, z1), fx);
        return Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
    }

    private float At(int x, int y, int z) => Data[(z * Size + y) * Size + x];
    private int Wrap(int i) => ((i % Size) + Size) % Size;
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Octaves of tiling Worley or Perlin noise summed (each lacunarity times finer, persistence times fainter).</summary>
    private static float[] Fbm(NoiseOctaves o, int size, ulong seed, bool worley)
    {
        var sum = new float[size * size * size];
        int octaves = Math.Max(1, Math.Min((int)Math.Round(o.Octaves), 8));
        int lacunarity = Math.Max(1, (int)Math.Round(o.Lacunarity));   // whole numbers keep each octave tiling
        double amplitude = 1.0, total = 0.0;
        int periods = Math.Max(1, (int)Math.Round(o.Periods));
        for (int k = 0; k < octaves; k++)
        {
            int cells = periods;
            for (int j = 0; j < k; j++) cells *= lacunarity;
            cells = Math.Min(cells, size);   // finer than a voxel adds nothing
            ulong octaveSeed = Mix(seed + (ulong)k * 0x9E3779B97F4A7C15UL);
            if (worley) AddWorley(sum, size, cells, octaveSeed, o.Spherical, (float)amplitude);
            else AddPerlin(sum, size, cells, octaveSeed, (float)amplitude);
            total += amplitude;
            amplitude *= o.Persistence;
        }
        float scale = (float)(1.0 / total);
        for (int i = 0; i < sum.Length; i++) sum[i] *= scale;
        return sum;
    }

    /// <summary>
    /// One octave of inverted Worley noise (1 - distance to the nearest feature point, a point per cell of a cells^3 grid
    /// that wraps), made rounder and puffier by <paramref name="spherical"/>.
    /// </summary>
    private static void AddWorley(float[] sum, int size, int cells, ulong seed, double spherical, float amplitude)
    {
        // Feature points, one per cell, in cell units.
        var points = new double[cells * cells * cells * 3];
        for (int c = 0; c < cells * cells * cells; c++)
        {
            ulong h = Mix(seed ^ (ulong)c * 0xD1B54A32D192ED03UL);
            points[3 * c] = Unit(h, 1);
            points[3 * c + 1] = Unit(h, 2);
            points[3 * c + 2] = Unit(h, 3);
        }
        double cellSize = (double)size / cells;
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double px = (x + 0.5) / cellSize, py = (y + 0.5) / cellSize, pz = (z + 0.5) / cellSize;
            int cx = (int)px, cy = (int)py, cz = (int)pz;
            double best = double.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy, nz = cz + dz;
                int wx = ((nx % cells) + cells) % cells, wy = ((ny % cells) + cells) % cells, wz = ((nz % cells) + cells) % cells;
                int c = (wz * cells + wy) * cells + wx;
                double fx = nx + points[3 * c] - px, fy = ny + points[3 * c + 1] - py, fz = nz + points[3 * c + 2] - pz;
                double d2 = fx * fx + fy * fy + fz * fz;
                if (d2 < best) best = d2;
            }
            double d = Math.Min(Math.Sqrt(best), 1.0);
            double value = 1.0 - d;
            if (spherical > 0.0) value += (Math.Sqrt(Math.Max(1.0 - d * d, 0.0)) - value) * Math.Min(spherical, 1.0);
            sum[(z * size + y) * size + x] += (float)(value * amplitude);
        }
    }

    /// <summary>One octave of tiling Perlin (gradient) noise on a cells^3 lattice, mapped to 0..1.</summary>
    private static void AddPerlin(float[] sum, int size, int cells, ulong seed, float amplitude)
    {
        var gradients = new double[cells * cells * cells * 3];
        for (int c = 0; c < cells * cells * cells; c++)
        {
            ulong h = Mix(seed ^ (ulong)c * 0xD1B54A32D192ED03UL);
            // A direction spread evenly over the sphere.
            double zz = 2.0 * Unit(h, 1) - 1.0, a = 2.0 * Math.PI * Unit(h, 2), r = Math.Sqrt(1.0 - zz * zz);
            gradients[3 * c] = r * Math.Cos(a);
            gradients[3 * c + 1] = r * Math.Sin(a);
            gradients[3 * c + 2] = zz;
        }
        double cellSize = (double)size / cells;
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double px = (x + 0.5) / cellSize, py = (y + 0.5) / cellSize, pz = (z + 0.5) / cellSize;
            int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py), z0 = (int)Math.Floor(pz);
            double fx = px - x0, fy = py - y0, fz = pz - z0;
            double value = 0.0;
            for (int dz = 0; dz <= 1; dz++)
            for (int dy = 0; dy <= 1; dy++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int wx = (x0 + dx) % cells, wy = (y0 + dy) % cells, wz = (z0 + dz) % cells;
                int c = (wz * cells + wy) * cells + wx;
                double gx = fx - dx, gy = fy - dy, gz = fz - dz;
                double dot = gradients[3 * c] * gx + gradients[3 * c + 1] * gy + gradients[3 * c + 2] * gz;
                double weight = Fade(dx == 0 ? 1.0 - fx : fx) * Fade(dy == 0 ? 1.0 - fy : fy) * Fade(dz == 0 ? 1.0 - fz : fz);
                value += weight * dot;
            }
            sum[(z * size + y) * size + x] += (float)((0.5 + 0.5 * value / 0.866) * amplitude);   // |value| < sqrt(3)/2
        }
    }

    /// <summary>Perlin's fade curve, as a weight: 1 at the corner, 0 a cell away.</summary>
    private static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    /// <summary>Spreads the values over the whole of 0..1.</summary>
    private static void Stretch(float[] data)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (float v in data) { if (v < lo) lo = v; if (v > hi) hi = v; }
        if (hi - lo < 1e-6f) return;
        float scale = 1f / (hi - lo);
        for (int i = 0; i < data.Length; i++) data[i] = (data[i] - lo) * scale;
    }

    private static double Unit(ulong h, int n) => (Mix(h + (ulong)n * 0x632BE59BD9B4E019UL) >> 11) * (1.0 / (1UL << 53));

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
