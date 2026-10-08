using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// A polytropic star: the Lane-Emden equation theta'' + (2/xi) theta' + theta^n = 0, theta(0) = 1, theta'(0) = 0, with
/// density rho = rho_c theta^n (Chandrasekhar 1939, ch. IV). n = 3 is a Sun-like main-sequence star (radiative
/// envelope), n = 1.5 a fully convective one. Sampled on radius x = r/R in [0, 1]. Masses are fractions of the star's.
/// </summary>
public sealed class Polytrope
{
    private const int Samples = 1024;

    private readonly double[] _density = new double[Samples + 1];   // rho / rho_c at x = i / Samples
    private readonly double[] _mass = new double[Samples + 1];      // mass inside x, fraction of the star

    public double Index { get; }
    /// <summary>xi_1, where theta first reaches zero (the surface).</summary>
    public double FirstZero { get; }
    /// <summary>Central density over mean density: xi_1 / (3 |theta'(xi_1)|).</summary>
    public double CentralToMeanDensity { get; }

    public Polytrope(double index)
    {
        if (index < 0.0 || index >= 5.0) throw new ArgumentOutOfRangeException(nameof(index), "Polytropes with n >= 5 have no surface.");
        Index = index;

        // RK4 in xi from a series start near the centre: theta = 1 - xi^2/6 + n xi^4/120.
        const double h = 1e-4;
        double xi = 1e-3;
        double theta = 1.0 - xi * xi / 6.0 + index * Math.Pow(xi, 4) / 120.0;
        double dtheta = -xi / 3.0 + index * Math.Pow(xi, 3) / 30.0;
        var xs = new List<double> { 0.0, xi };
        var thetas = new List<double> { 1.0, theta };
        var derivs = new List<double> { 0.0, dtheta };
        while (theta > 0.0)
        {
            Step(ref xi, ref theta, ref dtheta, h, index);
            xs.Add(xi);
            thetas.Add(theta);
            derivs.Add(dtheta);
            if (xi > 50.0) throw new InvalidOperationException("Lane-Emden integration did not reach the surface.");
        }
        int last = xs.Count - 1;
        double f = thetas[last - 1] / (thetas[last - 1] - thetas[last]);
        FirstZero = xs[last - 1] + (xs[last] - xs[last - 1]) * f;
        double surfaceSlope = derivs[last - 1] + (derivs[last] - derivs[last - 1]) * f;
        CentralToMeanDensity = FirstZero / (3.0 * Math.Abs(surfaceSlope));
        double massScale = FirstZero * FirstZero * Math.Abs(surfaceSlope);

        // Resample on x = r/R. Mass inside xi is proportional to -xi^2 theta'(xi).
        for (int i = 0; i <= Samples; i++)
        {
            double target = FirstZero * i / Samples;
            int j = Math.Min((int)(target / h), xs.Count - 2);
            while (j + 1 < xs.Count - 1 && xs[j + 1] < target) j++;
            while (j > 0 && xs[j] > target) j--;
            double w = xs[j + 1] > xs[j] ? Math.Max(0.0, Math.Min(1.0, (target - xs[j]) / (xs[j + 1] - xs[j]))) : 0.0;
            double th = Math.Max(0.0, thetas[j] + (thetas[j + 1] - thetas[j]) * w);
            double dth = derivs[j] + (derivs[j + 1] - derivs[j]) * w;
            _density[i] = Math.Pow(th, index);
            _mass[i] = Math.Min(1.0, target * target * Math.Abs(dth) / massScale);
        }
        _density[Samples] = 0.0;
        _mass[Samples] = 1.0;
    }

    private static void Step(ref double xi, ref double theta, ref double dtheta, double h, double n)
    {
        double k1t = dtheta, k1d = Accel(xi, theta, dtheta, n);
        double k2t = dtheta + 0.5 * h * k1d, k2d = Accel(xi + 0.5 * h, theta + 0.5 * h * k1t, k2t, n);
        double k3t = dtheta + 0.5 * h * k2d, k3d = Accel(xi + 0.5 * h, theta + 0.5 * h * k2t, k3t, n);
        double k4t = dtheta + h * k3d, k4d = Accel(xi + h, theta + h * k3t, k4t, n);
        theta += h / 6.0 * (k1t + 2.0 * k2t + 2.0 * k3t + k4t);
        dtheta += h / 6.0 * (k1d + 2.0 * k2d + 2.0 * k3d + k4d);
        xi += h;
    }

    private static double Accel(double xi, double theta, double dtheta, double n) =>
        -Math.Pow(Math.Max(theta, 0.0), n) - 2.0 * dtheta / xi;

    private static double Sample(double[] table, double x)
    {
        double c = Math.Max(0.0, Math.Min(1.0, x)) * Samples;
        int i = Math.Min((int)c, Samples - 1);
        return table[i] + (table[i + 1] - table[i]) * (c - i);
    }

    /// <summary>rho / rho_c at radius x = r/R.</summary>
    public double Density(double x) => Sample(_density, x);

    /// <summary>Fraction of the star's mass inside x = r/R.</summary>
    public double EnclosedMass(double x) => Sample(_mass, x);
}
