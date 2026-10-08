using System;

namespace KSPVE.Pure.Space.Orbits;

/// <summary>Kepler's equation solver for elliptic orbits.</summary>
public static class KeplerSolver
{
    private const double Tolerance = 1e-14;
    private const int MaxIterations = 50;

    /// <summary>
    /// Solves E - e sin E = M for 0 &lt;= e &lt; 1. Newton-Raphson with Danby's starting guess
    /// E0 = M + 0.85 e sign(sin M) (Danby 1988, "Fundamentals of Celestial Mechanics"); M is wrapped to (-pi, pi].
    /// </summary>
    public static double SolveEccentricAnomaly(double meanAnomalyRadians, double eccentricity)
    {
        double m = Math.IEEERemainder(meanAnomalyRadians, 2.0 * Math.PI);
        if (m <= -Math.PI) m += 2.0 * Math.PI;
        if (eccentricity <= 0.0) return m;

        double sinM = Math.Sin(m);
        double e = m + 0.85 * eccentricity * (sinM < 0.0 ? -1.0 : 1.0);
        for (int i = 0; i < MaxIterations; i++)
        {
            double f = e - eccentricity * Math.Sin(e) - m;
            double delta = f / (1.0 - eccentricity * Math.Cos(e));
            e -= delta;
            if (Math.Abs(delta) < Tolerance) break;
        }
        return e;
    }
}
