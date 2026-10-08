using System.Collections.Generic;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>Gaussian over-density in mean longitude (L = Omega + omega + M).</summary>
public readonly struct BeltClump
{
    public double MeanLongitudeCenterRadians { get; }
    public double WidthRadians { get; }
    public double Weight { get; }

    public BeltClump(double meanLongitudeCenterRadians, double widthRadians, double weight)
    {
        MeanLongitudeCenterRadians = meanLongitudeCenterRadians;
        WidthRadians = widthRadians;
        Weight = weight;
    }
}

/// <summary>Statistical description of an asteroid belt.</summary>
public sealed class BeltDefinition
{
    public double InnerRadiusMeters { get; }
    public double OuterRadiusMeters { get; }
    /// <summary>Surface density is proportional to a^exponent.</summary>
    public double RadialPowerExponent { get; }
    public double EccentricityRayleighSigma { get; }
    public double InclinationRayleighSigmaRadians { get; }
    public int Count { get; }
    public ulong Seed { get; }
    public double MinRockRadiusMeters { get; }
    public double MaxRockRadiusMeters { get; }
    /// <summary>dN/dR is proportional to R^-q, q &gt; 1.</summary>
    public double SizePowerLawExponent { get; }
    public IReadOnlyList<BeltClump> Clumps { get; }

    public BeltDefinition(
        double innerRadiusMeters, double outerRadiusMeters, double radialPowerExponent,
        double eccentricityRayleighSigma, double inclinationRayleighSigmaRadians,
        int count, ulong seed,
        double minRockRadiusMeters, double maxRockRadiusMeters, double sizePowerLawExponent,
        IReadOnlyList<BeltClump> clumps = null)
    {
        InnerRadiusMeters = innerRadiusMeters;
        OuterRadiusMeters = outerRadiusMeters;
        RadialPowerExponent = radialPowerExponent;
        EccentricityRayleighSigma = eccentricityRayleighSigma;
        InclinationRayleighSigmaRadians = inclinationRayleighSigmaRadians;
        Count = count;
        Seed = seed;
        MinRockRadiusMeters = minRockRadiusMeters;
        MaxRockRadiusMeters = maxRockRadiusMeters;
        SizePowerLawExponent = sizePowerLawExponent;
        Clumps = clumps ?? new BeltClump[0];
    }
}
