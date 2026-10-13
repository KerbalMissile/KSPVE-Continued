using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Particles;

/// <summary>
/// Two-body motion for any energy, by the universal variable (Battin; Curtis, "Orbital Mechanics for Engineering Students",
/// algorithm 3.4): a state (position, velocity) carried forward or back by any time under a gravitational parameter
/// <c>mu</c>. Ellipses are first reduced by whole periods, so a clump launched a thousand orbits ago costs no more than one
/// launched a minute ago. A repulsive field (<c>mu</c> &lt;= 0: radiation pressure stronger than gravity, beta &gt;= 1) has
/// no closed form here and is integrated with a fixed number of fourth-order Runge-Kutta steps.
/// </summary>
public static class UniversalKepler
{
    /// <summary>Gravitational constant (m^3 kg^-1 s^-2, CODATA 2018).</summary>
    public const double G = 6.67430e-11;

    /// <summary>
    /// The state <paramref name="dt"/> seconds on (back if negative) from (<paramref name="r0"/>, <paramref name="v0"/>) in a
    /// field of <paramref name="mu"/> (m^3/s^2) centred on the origin.
    /// </summary>
    public static void Propagate(double mu, Double3 r0, Double3 v0, double dt, out Double3 r, out Double3 v)
    {
        r = r0;
        v = v0;
        if (dt == 0.0) return;
        double r0m = r0.Length;
        if (r0m <= 0.0) return;
        if (mu <= 0.0)
        {
            Repulsive(Math.Abs(mu), r0, v0, dt, out r, out v);
            return;
        }

        double sqrtMu = Math.Sqrt(mu);
        double vr0 = Double3.Dot(r0, v0) / r0m;
        double alpha = 2.0 / r0m - v0.LengthSquared / mu;   // 1/a: > 0 ellipse, 0 parabola, < 0 hyperbola
        if (alpha > 1e-14)
        {
            double period = 2.0 * Math.PI / (sqrtMu * Math.Pow(alpha, 1.5));
            dt = Math.IEEERemainder(dt, period);
        }

        double chi = sqrtMu * Math.Abs(alpha) * dt;
        if (alpha <= 1e-14)
        {
            // Hyperbolas and parabolas: a guess that already lands near the root (Vallado's, bounded for safety).
            chi = Math.Sign(dt) * Math.Sqrt(-1.0 / (Math.Abs(alpha) > 1e-14 ? alpha : -1e-14)) * Math.Log(
                Math.Max(-2.0 * mu * Math.Abs(alpha) * Math.Abs(dt) /
                (Double3.Dot(r0, v0) + Math.Sign(dt) * Math.Sqrt(-mu / alpha) * (1.0 - r0m * alpha)), 1e-30));
            if (double.IsNaN(chi) || double.IsInfinity(chi) || Math.Abs(alpha) < 1e-12) chi = sqrtMu * dt / r0m;
        }
        double z = 0.0, c = 0.5, s = 1.0 / 6.0;
        for (int i = 0; i < 100; i++)
        {
            z = alpha * chi * chi;
            Stumpff(z, out c, out s);
            double f = r0m * vr0 / sqrtMu * chi * chi * c + (1.0 - alpha * r0m) * chi * chi * chi * s + r0m * chi - sqrtMu * dt;
            double df = r0m * vr0 / sqrtMu * chi * (1.0 - z * s) + (1.0 - alpha * r0m) * chi * chi * c + r0m;
            double step = f / df;
            if (double.IsNaN(step) || double.IsInfinity(step)) break;
            // A step may not carry chi more than a full chi away: keeps Newton tame on hyperbolas.
            double limit = Math.Max(Math.Abs(chi), 1.0) * 4.0;
            if (Math.Abs(step) > limit) step = Math.Sign(step) * limit;
            chi -= step;
            if (Math.Abs(step) < 1e-11 * Math.Max(1.0, Math.Abs(chi))) break;
        }
        z = alpha * chi * chi;
        Stumpff(z, out c, out s);
        double fF = 1.0 - chi * chi / r0m * c;
        double gF = dt - chi * chi * chi / sqrtMu * s;
        r = r0 * fF + v0 * gF;
        double rm = r.Length;
        double fDot = sqrtMu / (rm * r0m) * (z * s - 1.0) * chi;
        double gDot = 1.0 - chi * chi / rm * c;
        v = r0 * fDot + v0 * gDot;
    }

    /// <summary>Stumpff's functions C(z) and S(z).</summary>
    public static void Stumpff(double z, out double c, out double s)
    {
        if (z > 1e-6)
        {
            double sq = Math.Sqrt(z);
            c = (1.0 - Math.Cos(sq)) / z;
            s = (sq - Math.Sin(sq)) / (sq * sq * sq);
        }
        else if (z < -1e-6)
        {
            double sq = Math.Sqrt(-z);
            c = (Math.Cosh(sq) - 1.0) / -z;
            s = (Math.Sinh(sq) - sq) / (sq * sq * sq);
        }
        else
        {
            c = 0.5 - z / 24.0 + z * z / 720.0;
            s = 1.0 / 6.0 - z / 120.0 + z * z / 5040.0;
        }
    }

    /// <summary>
    /// Outward-pushed motion: acceleration +<paramref name="push"/> r / |r|^3 (beta &gt; 1 leaves a net push of (beta - 1) mu).
    /// Fixed-step RK4, 64 steps over the time asked.
    /// </summary>
    private static void Repulsive(double push, Double3 r0, Double3 v0, double dt, out Double3 r, out Double3 v)
    {
        const int steps = 64;
        double h = dt / steps;
        r = r0;
        v = v0;
        for (int i = 0; i < steps; i++)
        {
            Double3 k1r = v, k1v = Accel(push, r);
            Double3 k2r = v + k1v * (h * 0.5), k2v = Accel(push, r + k1r * (h * 0.5));
            Double3 k3r = v + k2v * (h * 0.5), k3v = Accel(push, r + k2r * (h * 0.5));
            Double3 k4r = v + k3v * h, k4v = Accel(push, r + k3r * h);
            r = r + (k1r + k2r * 2.0 + k3r * 2.0 + k4r) * (h / 6.0);
            v = v + (k1v + k2v * 2.0 + k3v * 2.0 + k4v) * (h / 6.0);
        }
    }

    private static Double3 Accel(double push, Double3 r)
    {
        double d = Math.Max(r.Length, 1e-9);
        return r * (push / (d * d * d));
    }
}
