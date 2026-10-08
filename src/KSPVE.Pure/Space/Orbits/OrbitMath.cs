using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Orbits;

/// <summary>
/// Kepler orbit propagation. Frame: right-handed inertial, +Z normal to the reference plane, +X toward the reference
/// direction. The Unity layer converts to KSP's Y-up left-handed world.
/// </summary>
public static class OrbitMath
{
    public static double MeanMotion(double mu, double a) => Math.Sqrt(mu / (a * a * a));

    public static double PeriodSeconds(double mu, double a) => 2.0 * Math.PI / MeanMotion(mu, a);

    /// <summary>Position and velocity from perifocal coordinates rotated by Rz(Omega) Rx(i) Rz(omega).</summary>
    public static (Double3 PositionMeters, Double3 VelocityMetersPerSecond) StateAt(
        OrbitalElements el, double muM3PerS2, double timeSeconds)
    {
        double a = el.SemiMajorAxisMeters, e = el.Eccentricity;
        double n = MeanMotion(muM3PerS2, a);
        double meanAnomaly = el.MeanAnomalyAtEpochRadians + n * (timeSeconds - el.EpochSeconds);
        double ea = KeplerSolver.SolveEccentricAnomaly(meanAnomaly, e);
        double cosE = Math.Cos(ea), sinE = Math.Sin(ea);
        double b = Math.Sqrt(1.0 - e * e);
        double r = a * (1.0 - e * cosE);

        double px = a * (cosE - e), py = a * b * sinE;
        double k = Math.Sqrt(muM3PerS2 * a) / r;
        double vx = -k * sinE, vy = k * b * cosE;

        double co = Math.Cos(el.LongitudeOfAscendingNodeRadians), so = Math.Sin(el.LongitudeOfAscendingNodeRadians);
        double ci = Math.Cos(el.InclinationRadians), si = Math.Sin(el.InclinationRadians);
        double cw = Math.Cos(el.ArgumentOfPeriapsisRadians), sw = Math.Sin(el.ArgumentOfPeriapsisRadians);

        // Columns of the perifocal -> inertial rotation (P and Q unit vectors).
        var p = new Double3(co * cw - so * sw * ci, so * cw + co * sw * ci, sw * si);
        var q = new Double3(-co * sw - so * cw * ci, -so * sw + co * cw * ci, cw * si);
        return (p * px + q * py, p * vx + q * vy);
    }
}
