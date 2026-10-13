using System;

namespace KSPVE.Pure.Stars;

/// <summary>
/// A small deterministic random stream for the star activity model: seeded from a star's seed and up to three integers
/// (a time slot, an index, a purpose), so any thing the star does is worked out again identically whenever it is asked
/// for (no state, so time warp, reloads and a second client always agree). SplitMix64 (Steele, Lea &amp; Flood 2014),
/// which is well mixed for consecutive integer inputs.
/// </summary>
public struct StarRandom
{
    private ulong _state;

    public StarRandom(ulong seed, long a, long b = 0, long c = 0)
    {
        unchecked
        {
            _state = seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            _state = Mix(_state ^ (ulong)a * 0xD1B54A32D192ED03UL);
            _state = Mix(_state ^ (ulong)b * 0x8CB92BA72F3D8DD7UL);
            _state = Mix(_state ^ (ulong)c * 0xAEF17502108EF2D9UL);
        }
    }

    private static ulong Mix(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>The next number, uniform in [0, 1).</summary>
    public double Next()
    {
        _state = Mix(_state);
        return (_state >> 11) * (1.0 / 9007199254740992.0);
    }

    public double Range(double low, double high) => low + (high - low) * Next();

    /// <summary>A standard normal number (Box-Muller).</summary>
    public double Gauss()
    {
        double u1 = 1.0 - Next(), u2 = Next();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Lognormal with the given median and logarithmic standard deviation.</summary>
    public double LogNormal(double median, double sigma) => median * Math.Exp(sigma * Gauss());

    /// <summary>A Poisson count with the given mean (Knuth's method for small means, normal approximation beyond 30).</summary>
    public int Poisson(double mean)
    {
        if (mean <= 0.0) return 0;
        if (mean > 30.0) return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * Gauss()));
        double limit = Math.Exp(-mean), product = Next();
        int k = 0;
        while (product > limit && k < 200)
        {
            k++;
            product *= Next();
        }
        return k;
    }

    /// <summary>A single number in [0, 1) for a fixed set of inputs, without keeping a stream.</summary>
    public static double Unit(ulong seed, long a, long b = 0, long c = 0) => new StarRandom(seed, a, b, c).Next();
}
