using System;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>PCG-XSH-RR 64/32 generator (O'Neill 2014, "PCG: A Family of Simple Fast Space-Efficient Statistically Good Algorithms").</summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private ulong _state;
    private readonly ulong _inc;

    /// <summary>Matches the reference pcg32_srandom_r(seed as initstate, stream as initseq).</summary>
    public Pcg32(ulong seed, ulong stream)
    {
        _state = 0;
        _inc = (stream << 1) | 1UL;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        unchecked
        {
            _state = old * Multiplier + _inc;
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }
    }

    /// <summary>Uniform double in [0,1) from 53 random bits (two draws).</summary>
    public double NextDouble()
    {
        ulong hi = NextUInt() >> 5;  // 27 bits
        ulong lo = NextUInt() >> 6;  // 26 bits
        return (hi * 67108864.0 + lo) / 9007199254740992.0;
    }

    /// <summary>Standard normal via Box-Muller (cosine branch only, so each call consumes exactly four draws).</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble(); // (0,1]
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
