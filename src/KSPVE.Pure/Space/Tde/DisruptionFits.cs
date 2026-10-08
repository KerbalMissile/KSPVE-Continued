using System;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// Fits to hydrodynamic simulations of tidal disruptions of polytropic stars (Guillochon and Ramirez-Ruiz 2013, ApJ
/// 767, 25, appendix A): the peak fallback rate Mdot_peak = A M6^-1/2 m*^2 r*^-3/2 Suns/yr, its time
/// t_peak = B M6^1/2 m*^-1 r*^3/2 yr, and the mass lost, C M*, as functions of the penetration factor beta, for
/// gamma = 5/3 (n = 1.5, low-mass stars) and gamma = 4/3 (n = 3, Sun-like). Other indices blend the two linearly in n;
/// beta is held inside each fit's range.
/// </summary>
public static class DisruptionFits
{
    /// <summary>Beta at which the whole star is torn apart: 0.9 for gamma = 5/3, 1.85 for gamma = 4/3.</summary>
    public static double FullDisruptionBeta(double n) => Blend(n, 0.9, 1.85);

    public static double PeakRateCoefficient(double beta, double n) => Blend(n, A53(Clamp(beta, 0.5, 2.5)), A43(Clamp(beta, 0.6, 4.0)));

    public static double PeakTimeCoefficient(double beta, double n) => Blend(n, B53(Clamp(beta, 0.5, 2.5)), B43(Clamp(beta, 0.6, 4.0)));

    /// <summary>Fraction of the star's mass torn off (1 for a full disruption).</summary>
    public static double MassLost(double beta, double n)
    {
        double five = beta >= 0.9 ? 1.0 : Math.Min(1.0, C53(Math.Max(beta, 0.5)));
        double four = beta >= 1.85 ? 1.0 : Math.Min(1.0, C43(Math.Max(beta, 0.6)));
        return Blend(n, five, four);
    }

    private static double A53(double b) => Math.Exp((10.253 - 17.380 * b + 5.9988 * b * b) / (1.0 - 0.46573 * b - 4.5066 * b * b));
    private static double A43(double b) => Math.Exp((27.261 - 27.516 * b + 3.8716 * b * b) / (1.0 - 3.2605 * b - 1.3865 * b * b));
    private static double B53(double b) => (-0.30908 + 1.1804 * Math.Sqrt(b) - 1.1764 * b) / (1.0 + 1.3089 * Math.Sqrt(b) - 4.1940 * b);
    private static double B43(double b) => (-0.38670 + 0.57291 * Math.Sqrt(b) - 0.31231 * b) / (1.0 - 1.2744 * Math.Sqrt(b) - 0.90053 * b);
    private static double C53(double b) => Math.Exp((3.1647 - 6.3777 * b + 3.1797 * b * b) / (1.0 - 3.4137 * b + 2.4616 * b * b));
    private static double C43(double b) => Math.Exp((12.996 - 31.149 * b + 12.865 * b * b) / (1.0 - 5.3232 * b + 6.4262 * b * b));

    private static double Blend(double n, double fiveThirds, double fourThirds)
    {
        double f = Clamp((n - 1.5) / 1.5, 0.0, 1.0);
        return fiveThirds + (fourThirds - fiveThirds) * f;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>
    /// The debris' energy distribution dM/ds ~ (1 - s^2)^w (s the scaled energy, in [-1, 1]) whose fallback has the
    /// simulated shape: Mdot ~ (dM/ds) |ds/dt| with t ~ |s|^-3/2 peaks at s* = sqrt(2.5 / (2.5 + 2w)), and
    /// Mdot_peak t_peak / M_debris = (2/3) c_w (1 - s*^2)^w s* depends on w alone (c_w normalises dM/ds). Returns the
    /// w matching <paramref name="peakRateTimesTime"/> (0 = flat, the widest the frozen-in picture allows).
    /// </summary>
    public static double EnergyShape(double peakRateTimesTime)
    {
        if (peakRateTimesTime >= PeakProduct(0.0)) return 0.0;
        double lo = 0.0, hi = 40.0;
        for (int i = 0; i < 60; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (PeakProduct(mid) > peakRateTimesTime) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    public static double PeakEnergy(double w) => Math.Sqrt(2.5 / (2.5 + 2.0 * w));

    public static double PeakProduct(double w)
    {
        double s = PeakEnergy(w);
        return 2.0 / 3.0 * ShapeNorm(w) * Math.Pow(1.0 - s * s, w) * s;
    }

    /// <summary>1 / integral of (1 - s^2)^w over [-1, 1].</summary>
    public static double ShapeNorm(double w)
    {
        const int steps = 2000;
        double sum = 0.0;
        for (int i = 0; i < steps; i++)
        {
            double s = -1.0 + (i + 0.5) * 2.0 / steps;
            sum += Math.Pow(1.0 - s * s, w);
        }
        return 1.0 / (sum * 2.0 / steps);
    }
}
