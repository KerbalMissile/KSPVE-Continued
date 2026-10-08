using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>Result of <see cref="DiskDrag.Compute"/>: the pull in m/s^2, the local disk density weight (0..1), and the
/// vessel's speed relative to the gas (m/s).</summary>
public readonly struct DiskDragResult
{
    public readonly Double3 Acceleration;
    public readonly double Weight;
    public readonly double RelativeSpeed;

    public DiskDragResult(Double3 acceleration, double weight, double relativeSpeed)
    {
        Acceleration = acceleration;
        Weight = weight;
        RelativeSpeed = relativeSpeed;
    }
}

/// <summary>
/// Gas drag of a black hole's accretion disk on a vessel. The weight covers the whole disk the shader draws (full
/// strength from just outside the inner edge to near the outer edge, and up through the puffy surface); the gas moves
/// on circular Keplerian orbits and drags the vessel toward its velocity.
/// </summary>
public static class DiskDrag
{
    /// <summary>How much of the disk's pull applies at <paramref name="position"/> (relative to the body centre, m): 1 inside
    /// the visible disk, fading to 0 at its edges and above its surface.</summary>
    /// <param name="normal">Unit disk normal.</param>
    /// <param name="horizon">Horizon radius (m).</param>
    /// <param name="inner">Disk inner edge (horizon radii).</param>
    /// <param name="outer">Disk outer edge (horizon radii).</param>
    /// <param name="thickness">Half-thickness as a fraction of radius.</param>
    public static double Density(Double3 position, Double3 normal, double horizon, double inner, double outer, double thickness)
    {
        if (horizon <= 0.0) return 0.0;
        double h = Double3.Dot(position, normal);
        double r = (position - normal * h).Length / horizon;
        if (r <= 0.0) return 0.0;
        double radial = SmoothStep(inner * 0.85, inner * 1.1, r) * (1.0 - SmoothStep(outer * 0.75, outer * 1.05, r));
        double vertical = 1.0 - SmoothStep(1.2, 2.4, Math.Abs(h / horizon) / (thickness * r));
        return radial * vertical;
    }

    /// <param name="position">Vessel position relative to the body centre (m, world frame).</param>
    /// <param name="velocity">Vessel velocity relative to the body (m/s, world frame).</param>
    /// <param name="normal">Unit disk normal.</param>
    /// <param name="spin">+1 for gas orbiting counter-clockwise about the normal, -1 for the reverse.</param>
    /// <param name="mu">Body gravitational parameter (m^3/s^2).</param>
    /// <param name="horizon">Horizon radius (m).</param>
    /// <param name="inner">Disk inner edge (horizon radii).</param>
    /// <param name="outer">Disk outer edge (horizon radii).</param>
    /// <param name="thickness">Half-thickness as a fraction of radius.</param>
    /// <param name="timescale">Seconds to match the gas speed at full density.</param>
    /// <param name="maxAccel">Cap on the pull at the disk's outer edge (m/s^2); it rises inward with the gas density
    /// (like 1/r, as the shader's density does).</param>
    public static DiskDragResult Compute(Double3 position, Double3 velocity, Double3 normal, double spin, double mu,
        double horizon, double inner, double outer, double thickness, double timescale, double maxAccel)
    {
        if (timescale <= 0.0) return default;
        double weight = Density(position, normal, horizon, inner, outer, thickness);
        if (weight <= 0.0) return default;

        Double3 planar = position - normal * Double3.Dot(position, normal);
        double spinSign = spin < 0.0 ? -1.0 : 1.0;
        Double3 gas = Double3.Cross(normal, planar).Normalized() * (Math.Sqrt(mu / planar.Length) * spinSign);
        Double3 relative = velocity - gas;
        Double3 accel = relative * (-weight / timescale);

        double edge = outer * 0.75; // where the pull starts to fade
        double limit = maxAccel * weight * Math.Max(1.0, edge / Math.Max(planar.Length / horizon, inner));
        double length = accel.Length;
        if (length > limit) accel = length > 0.0 ? accel * (limit / length) : Double3.Zero;
        return new DiskDragResult(accel, weight, relative.Length);
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Min(1.0, Math.Max(0.0, (x - edge0) / (edge1 - edge0)));
        return t * t * (3.0 - 2.0 * t);
    }
}

/// <summary>Smooth, bounded turbulence for buffeting a vessel in the disk: each axis is a sum of incommensurate sines, so
/// it never repeats noticeably and stays within [-1, 1].</summary>
public static class DiskTurbulence
{
    private static readonly double[] Frequencies = { 0.37, 1.13, 2.71 }; // Hz
    private static readonly double[] Amplitudes = { 0.5, 0.33, 0.17 };  // sum to 1

    public static Double3 Sample(double time, int seed) =>
        new Double3(Axis(time, seed * 3 + 0), Axis(time, seed * 3 + 1), Axis(time, seed * 3 + 2));

    private static double Axis(double time, int stream)
    {
        double sum = 0.0;
        for (int i = 0; i < Frequencies.Length; i++)
        {
            double phase = Fraction(Math.Sin((stream * 3 + i) * 12.9898) * 43758.5453) * 2.0 * Math.PI;
            sum += Amplitudes[i] * Math.Sin(2.0 * Math.PI * Frequencies[i] * time + phase);
        }
        return sum;
    }

    private static double Fraction(double v) => v - Math.Floor(v);
}
