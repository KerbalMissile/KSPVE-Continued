using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Fraction of a solar disc left visible behind a circular occluder. All angles in one consistent unit.</summary>
public static class Eclipse
{
    /// <summary>Exact circle-circle overlap (standard lens-area formula) for a uniformly bright disc; returns 1 − covered fraction.</summary>
    public static double VisibleFractionUniform(double sunAngularRadius, double occluderAngularRadius, double separation)
    {
        double R = sunAngularRadius, r = occluderAngularRadius, d = Math.Abs(separation);
        if (d >= R + r) return 1.0;
        double overlap;
        if (d <= Math.Abs(R - r))
        {
            double m = Math.Min(R, r);
            overlap = Math.PI * m * m;
        }
        else
        {
            double a = (d * d + R * R - r * r) / (2.0 * d * R);
            double b = (d * d + r * r - R * R) / (2.0 * d * r);
            double k = (-d + R + r) * (d + R - r) * (d - R + r) * (d + R + r);
            overlap = R * R * Math.Acos(Clamp(a)) + r * r * Math.Acos(Clamp(b)) - 0.5 * Math.Sqrt(Math.Max(k, 0.0));
        }
        return Clamp01(1.0 - overlap / (Math.PI * R * R));
    }

    /// <summary>
    /// Limb-darkened disc, I(μ) = 1 − u(1 − μ), μ = √(1 − (ρ/R)²) (linear law, e.g. Cox, Allen's
    /// Astrophysical Quantities 4th ed.). Midpoint integration over <paramref name="rings"/> annuli; each
    /// annulus is covered by the fraction of its circle lying inside the occluder (arc angle / π).
    /// Returns the visible fraction of the disc's total flux.
    /// </summary>
    public static double VisibleFractionLimbDarkened(double sunAngularRadius, double occluderAngularRadius, double separation,
        double linearLimbDarkeningU, int rings)
    {
        double R = sunAngularRadius, r = occluderAngularRadius, d = Math.Abs(separation);
        if (d >= R + r) return 1.0;
        if (rings < 1) throw new ArgumentOutOfRangeException(nameof(rings));
        double total = 0, covered = 0, dr = R / rings;
        for (int i = 0; i < rings; i++)
        {
            double rho = (i + 0.5) * dr;
            double x = rho / R;
            double mu = Math.Sqrt(Math.Max(1.0 - x * x, 0.0));
            double w = (1.0 - linearLimbDarkeningU * (1.0 - mu)) * rho; // 2π dρ common factor cancels
            total += w;
            covered += w * CoveredArcFraction(rho, r, d);
        }
        return Clamp01(1.0 - covered / total);
    }

    static double CoveredArcFraction(double rho, double r, double d)
    {
        if (rho <= r - d) return 1.0;
        if (rho >= d + r || rho <= d - r) return 0.0;
        double c = (rho * rho + d * d - r * r) / (2.0 * rho * d);
        return Math.Acos(Clamp(c)) / Math.PI;
    }

    static double Clamp(double v) => v < -1.0 ? -1.0 : (v > 1.0 ? 1.0 : v);
    static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
}
