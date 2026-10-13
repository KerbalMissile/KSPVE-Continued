using System;

namespace KSPVE.Pure.Fluids;

/// <summary>
/// A small deterministic random generator (SplitMix64 with Box-Muller normals) so a seed gives the same spin-up on every
/// machine and every run; nothing about it depends on the framework's own Random.
/// </summary>
public sealed class FluidRandom
{
    private ulong _state;
    private bool _hasSpare;
    private double _spare;

    public FluidRandom(ulong seed)
    {
        _state = seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
        NextULong();
    }

    public ulong NextULong()
    {
        unchecked
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Normal with mean 0 and variance 1.</summary>
    public double NextGaussian()
    {
        if (_hasSpare)
        {
            _hasSpare = false;
            return _spare;
        }
        double u = 1.0 - NextDouble(), v = NextDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u));
        _spare = r * Math.Sin(2.0 * Math.PI * v);
        _hasSpare = true;
        return r * Math.Cos(2.0 * Math.PI * v);
    }
}
