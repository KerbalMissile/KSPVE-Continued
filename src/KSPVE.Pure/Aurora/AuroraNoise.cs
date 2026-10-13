using System;

namespace KSPVE.Pure.Aurora;

/// <summary>
/// The hash and value noise the aurora is made of. The shader (Aurora.shader) implements exactly the same integer hash and
/// lattice interpolation, so the offline preview and the game draw the same structure. Everything is a function of
/// position and a time phase, with no state: the same moment gives the same sky.
/// </summary>
public static class AuroraNoise
{
    /// <summary>A 32-bit integer hash (Chris Wellons' "lowbias32").</summary>
    public static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16; x *= 0x7feb352dU;
            x ^= x >> 15; x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>A number in [0, 1) from an integer key.</summary>
    public static double Rand(uint key) => (Hash(key) >> 8) * (1.0 / 16777216.0);

    public static uint Mix(uint a, uint b) => Hash(unchecked(a * 0x9E3779B1U) ^ Hash(b + 0x7F4A7C15U));

    public static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    public static double Value1(double x, uint seed)
    {
        double f = Math.Floor(x);
        uint i = unchecked((uint)(int)f);
        double t = Fade(x - f);
        double a = Rand(Mix(i, seed)), b = Rand(Mix(i + 1, seed));
        return a + (b - a) * t;
    }

    public static double Value2(double x, double y, uint seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        uint ix = unchecked((uint)(int)fx), iy = unchecked((uint)(int)fy);
        double tx = Fade(x - fx), ty = Fade(y - fy);
        uint s = Mix(seed, 0x51ED27U);
        double a = Rand(Mix(Mix(ix, iy), s)), b = Rand(Mix(Mix(ix + 1, iy), s));
        double c = Rand(Mix(Mix(ix, iy + 1), s)), d = Rand(Mix(Mix(ix + 1, iy + 1), s));
        double ab = a + (b - a) * tx, cd = c + (d - c) * tx;
        return ab + (cd - ab) * ty;
    }

    public static double Value3(double x, double y, double z, uint seed)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        uint ix = unchecked((uint)(int)fx), iy = unchecked((uint)(int)fy), iz = unchecked((uint)(int)fz);
        double tx = Fade(x - fx), ty = Fade(y - fy), tz = Fade(z - fz);
        uint s = Mix(seed, 0x2C9277U);
        double Corner(uint dx, uint dy, uint dz) => Rand(Mix(Mix(Mix(ix + dx, iy + dy), iz + dz), s));
        double x00 = Corner(0, 0, 0) + (Corner(1, 0, 0) - Corner(0, 0, 0)) * tx;
        double x10 = Corner(0, 1, 0) + (Corner(1, 1, 0) - Corner(0, 1, 0)) * tx;
        double x01 = Corner(0, 0, 1) + (Corner(1, 0, 1) - Corner(0, 0, 1)) * tx;
        double x11 = Corner(0, 1, 1) + (Corner(1, 1, 1) - Corner(0, 1, 1)) * tx;
        double y0 = x00 + (x10 - x00) * ty, y1 = x01 + (x11 - x01) * ty;
        return y0 + (y1 - y0) * tz;
    }

    /// <summary>
    /// 1D value noise that slides along its axis at <paramref name="speed"/> (noise units per second) without ever resetting:
    /// two copies a half-period apart, each wrapping every 300 s, are cross-faded so the moment of wrapping is invisible.
    /// <paramref name="phase"/> is fract(ut / 300).
    /// </summary>
    public static double Advected1(double x, double speed, double phase, uint seed)
    {
        double p2 = phase + 0.5 - Math.Floor(phase + 0.5);
        double w = 1.0 - Math.Abs(2.0 * phase - 1.0);
        double a = Value1(x - speed * 300.0 * phase, seed), b = Value1(x - speed * 300.0 * p2 + 137.1, seed);
        return b + (a - b) * w;
    }

    public static double Advected2(double x, double y, double vx, double vy, double phase, uint seed)
    {
        double p2 = phase + 0.5 - Math.Floor(phase + 0.5);
        double w = 1.0 - Math.Abs(2.0 * phase - 1.0);
        double a = Value2(x - vx * 300.0 * phase, y - vy * 300.0 * phase, seed);
        double b = Value2(x - vx * 300.0 * p2 + 71.3, y - vy * 300.0 * p2 + 19.7, seed);
        return b + (a - b) * w;
    }

    public static double Advected3(double x, double y, double z, double vx, double vy, double vz, double phase, uint seed)
    {
        double p2 = phase + 0.5 - Math.Floor(phase + 0.5);
        double w = 1.0 - Math.Abs(2.0 * phase - 1.0);
        double a = Value3(x - vx * 300.0 * phase, y - vy * 300.0 * phase, z - vz * 300.0 * phase, seed);
        double b = Value3(x - vx * 300.0 * p2 + 41.7, y - vy * 300.0 * p2 + 13.1, z - vz * 300.0 * p2 + 7.9, seed);
        return b + (a - b) * w;
    }

    public static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Max(0.0, Math.Min(1.0, (x - a) / (b - a)));
        return t * t * (3.0 - 2.0 * t);
    }
}
