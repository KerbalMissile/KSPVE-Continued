using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Relativity;

/// <summary>Result of <see cref="CurvedRay.Integrate"/>.</summary>
public sealed class RayResult
{
    public RayResult(bool captured, Double3 exitDirection, Double3 exitPosition, int steps)
    {
        Captured = captured;
        ExitDirection = exitDirection;
        ExitPosition = exitPosition;
        Steps = steps;
    }

    public bool Captured { get; }
    /// <summary>Unit vector along the final velocity.</summary>
    public Double3 ExitDirection { get; }
    public Double3 ExitPosition { get; }
    public int Steps { get; }
}

/// <summary>
/// Cartesian light-ray integrator as used by shaders: a = -3 m h^2 x / |x|^5 with h = |x cross v|
/// (h is constant, so it is evaluated once). This reproduces the Schwarzschild orbit equation
/// d2u/dphi2 = 3m u^2 - u exactly (Binet form with phi' = h/r^2). The often-quoted
/// -(3/2) h^2 x/|x|^5 is the same law written for Schwarzschild radius r_s = 2m = 1.
/// Refs: Riazuelo 2019 (arXiv:1903.05055); James et al. 2015 (Class. Quantum Grav. 32, 065001).
/// The affine parameter here is not proper time; only the path shape is physical.
/// </summary>
public static class CurvedRay
{
    /// <summary>Integrates with fixed-step RK4. The direction is normalised; the hole is at the origin.</summary>
    public static RayResult Integrate(Double3 origin, Double3 direction, double m, double stepLength,
        int maxSteps, double escapeRadius)
    {
        Double3 x = origin;
        Double3 v = direction.Normalized();
        double k = 3.0 * m * Double3.Cross(x, v).LengthSquared;
        double horizon2 = 4.0 * m * m;
        double dt = stepLength;
        int steps = 0;

        while (steps < maxSteps)
        {
            Double3 a1 = Acc(x, k);
            Double3 x2 = x + v * (0.5 * dt); Double3 v2 = v + a1 * (0.5 * dt);
            Double3 a2 = Acc(x2, k);
            Double3 x3 = x + v2 * (0.5 * dt); Double3 v3 = v + a2 * (0.5 * dt);
            Double3 a3 = Acc(x3, k);
            Double3 x4 = x + v3 * dt; Double3 v4 = v + a3 * dt;
            Double3 a4 = Acc(x4, k);
            x = x + (v + v2 * 2.0 + v3 * 2.0 + v4) * (dt / 6.0);
            v = v + (a1 + a2 * 2.0 + a3 * 2.0 + a4) * (dt / 6.0);
            steps++;

            double r2 = x.LengthSquared;
            if (r2 <= horizon2) return new RayResult(true, v.Normalized(), x, steps);
            if (r2 >= escapeRadius * escapeRadius && Double3.Dot(x, v) > 0)
                return new RayResult(false, v.Normalized(), x, steps);
        }
        return new RayResult(false, v.Normalized(), x, steps);
    }

    private static Double3 Acc(Double3 x, double k)
    {
        double r2 = x.LengthSquared;
        double r = System.Math.Sqrt(r2);
        return x * (-k / (r2 * r2 * r));
    }
}
