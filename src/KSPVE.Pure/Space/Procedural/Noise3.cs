using System;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>Deterministic 3D gradient noise (Perlin, "Improving Noise", SIGGRAPH 2002: quintic fade, 12 edge gradients).</summary>
public static class Noise3
{
    private static readonly int[] Grad =
    {
        1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
        1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
        0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
    };

    /// <summary>Gradient noise in [-1, 1]; zero at integer lattice points.</summary>
    public static double Gradient(double x, double y, double z, int seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        int ix = (int)fx, iy = (int)fy, iz = (int)fz;
        double dx = x - fx, dy = y - fy, dz = z - fz;
        double u = Fade(dx), v = Fade(dy), w = Fade(dz);

        double c000 = Corner(ix, iy, iz, seed, dx, dy, dz);
        double c100 = Corner(ix + 1, iy, iz, seed, dx - 1, dy, dz);
        double c010 = Corner(ix, iy + 1, iz, seed, dx, dy - 1, dz);
        double c110 = Corner(ix + 1, iy + 1, iz, seed, dx - 1, dy - 1, dz);
        double c001 = Corner(ix, iy, iz + 1, seed, dx, dy, dz - 1);
        double c101 = Corner(ix + 1, iy, iz + 1, seed, dx - 1, dy, dz - 1);
        double c011 = Corner(ix, iy + 1, iz + 1, seed, dx, dy - 1, dz - 1);
        double c111 = Corner(ix + 1, iy + 1, iz + 1, seed, dx - 1, dy - 1, dz - 1);

        double x00 = c000 + u * (c100 - c000), x10 = c010 + u * (c110 - c010);
        double x01 = c001 + u * (c101 - c001), x11 = c011 + u * (c111 - c011);
        double y0 = x00 + v * (x10 - x00), y1 = x01 + v * (x11 - x01);
        double result = y0 + w * (y1 - y0);
        return Math.Max(-1.0, Math.Min(1.0, result));
    }

    /// <summary>Fractal Brownian motion: summed octaves, normalised by total amplitude so the result stays in [-1, 1].</summary>
    public static double Fbm(double x, double y, double z, int octaves, double lacunarity, double gain, int seed)
    {
        double sum = 0, amplitude = 1, norm = 0, frequency = 1;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Gradient(x * frequency, y * frequency, z * frequency, seed + i * 1013);
            norm += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }
        return norm > 0 ? sum / norm : 0.0;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Corner(int ix, int iy, int iz, int seed, double dx, double dy, double dz)
    {
        ulong h = StableHash.Combine(StableHash.Combine(StableHash.Combine((ulong)(uint)seed, (ulong)(uint)ix), (ulong)(uint)iy), (ulong)(uint)iz);
        int g = (int)((h >> 32) % 12UL) * 3;
        return Grad[g] * dx + Grad[g + 1] * dy + Grad[g + 2] * dz;
    }
}
