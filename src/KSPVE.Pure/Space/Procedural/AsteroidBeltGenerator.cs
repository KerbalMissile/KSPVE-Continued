using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Orbits;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>One generated rock. Frame: right-handed inertial, +Z normal to the belt plane (converted to KSP's Y-up left-handed world by the Unity layer).</summary>
public sealed class AsteroidInstance
{
    public int Index { get; }
    public OrbitalElements Elements { get; }
    public double RadiusMeters { get; }
    /// <summary>Mesh variant 0..15.</summary>
    public int ShapeVariant { get; }
    public Double3 SpinAxis { get; }
    public double SpinRateRadiansPerSecond { get; }

    public AsteroidInstance(int index, OrbitalElements elements, double radiusMeters, int shapeVariant,
        Double3 spinAxis, double spinRateRadiansPerSecond)
    {
        Index = index;
        Elements = elements;
        RadiusMeters = radiusMeters;
        ShapeVariant = shapeVariant;
        SpinAxis = spinAxis;
        SpinRateRadiansPerSecond = spinRateRadiansPerSecond;
    }
}

/// <summary>Deterministic per-index belt generator: rock i depends only on (Seed, i).</summary>
public static class AsteroidBeltGenerator
{
    private const double TwoPi = 2.0 * Math.PI;
    private const double MaxEccentricity = 0.95;

    public static AsteroidInstance Generate(BeltDefinition def, int index)
    {
        ulong seed = StableHash.Combine(def.Seed, (ulong)index);
        var rng = new Pcg32(seed, (ulong)index);

        double a = SamplePowerLaw(rng.NextDouble(), def.InnerRadiusMeters, def.OuterRadiusMeters, def.RadialPowerExponent + 2.0);
        double e = Math.Min(MaxEccentricity, Rayleigh(rng.NextDouble(), def.EccentricityRayleighSigma));
        double inc = Math.Min(Math.PI * 0.5, Rayleigh(rng.NextDouble(), def.InclinationRayleighSigmaRadians));
        double node = rng.NextDouble() * TwoPi;
        double peri = rng.NextDouble() * TwoPi;
        double meanLongitude = SampleMeanLongitude(rng, def.Clumps);
        double m = Wrap(meanLongitude - node - peri);
        var elements = new OrbitalElements(a, e, inc, node, peri, m, 0.0);

        double radius = SamplePowerLaw(rng.NextDouble(), def.MinRockRadiusMeters, def.MaxRockRadiusMeters, 1.0 - def.SizePowerLawExponent);
        int variant = (int)(rng.NextUInt() & 15u);

        double cosT = 2.0 * rng.NextDouble() - 1.0, phi = rng.NextDouble() * TwoPi;
        double sinT = Math.Sqrt(Math.Max(0.0, 1.0 - cosT * cosT));
        var axis = new Double3(sinT * Math.Cos(phi), sinT * Math.Sin(phi), cosT);

        // Rotation period log-uniform between 2 h and 100 h.
        double periodHours = 2.0 * Math.Pow(50.0, rng.NextDouble());
        double spin = TwoPi / (periodHours * 3600.0);

        return new AsteroidInstance(index, elements, radius, variant, axis, spin);
    }

    public static IEnumerable<AsteroidInstance> GenerateRange(BeltDefinition def, int start, int count)
    {
        for (int i = 0; i < count; i++) yield return Generate(def, start + i);
    }

    /// <summary>Inverse-CDF sample of a density proportional to x^(k-1) on [lo, hi] (CDF ~ x^k); k = 0 is log-uniform.</summary>
    private static double SamplePowerLaw(double u, double lo, double hi, double k)
    {
        double x;
        if (Math.Abs(k) < 1e-9) x = lo * Math.Pow(hi / lo, u);
        else
        {
            double lk = Math.Pow(lo, k), hk = Math.Pow(hi, k);
            x = Math.Pow(lk + u * (hk - lk), 1.0 / k);
        }
        return Math.Max(lo, Math.Min(hi, x));
    }

    private static double Rayleigh(double u, double sigma) => sigma * Math.Sqrt(-2.0 * Math.Log(1.0 - u));

    private static double SampleMeanLongitude(Pcg32 rng, IReadOnlyList<BeltClump> clumps)
    {
        double pick = rng.NextDouble();
        double gauss = rng.NextGaussian();
        double uniform = rng.NextDouble() * TwoPi;
        double total = 1.0; // uniform background has weight 1
        foreach (var c in clumps) total += c.Weight;
        double t = pick * total;
        if (t < 1.0) return uniform;
        t -= 1.0;
        foreach (var c in clumps)
        {
            if (t < c.Weight) return Wrap(c.MeanLongitudeCenterRadians + c.WidthRadians * gauss);
            t -= c.Weight;
        }
        return uniform;
    }

    private static double Wrap(double angle)
    {
        double w = angle % TwoPi;
        return w < 0 ? w + TwoPi : w;
    }
}
