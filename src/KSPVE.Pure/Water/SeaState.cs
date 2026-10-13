using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Water;

/// <summary>
/// The sea for a wind: its wave statistics (from the JONSWAP spectrum), how much of it is white with breaking waves
/// (Monahan), and how rough it is for sun glitter (Cox and Munk). Everything depends only on the wind speed at 10 m, the fetch
/// and the gravity, so the weather moving the wind moves the sea.
/// </summary>
public sealed class SeaState
{
    public double Wind, Fetch, Gravity, Depth;
    /// <summary>Significant wave height (m) of the resolved band: 4 sqrt(m0).</summary>
    public double SignificantHeight;
    /// <summary>Angular frequency of the spectrum's peak (rad/s), the period and the wavelength there.</summary>
    public double PeakFrequency, PeakPeriod, PeakWavelength;
    /// <summary>Fraction of the sea covered by whitecaps (Monahan).</summary>
    public double WhitecapCoverage;
    /// <summary>Mean-square slope of the whole sea (Cox and Munk), and of the part along and across the wind.</summary>
    public double SlopeVariance, UpwindVariance, CrosswindVariance;

    /// <summary>Waves shorter than this (m) are not in the cascades; the glitter adds their roughness separately.</summary>
    public const double ShortestWavelength = 0.02;

    public static SeaState From(double wind, double fetch, double gravity, double depth = double.PositiveInfinity)
    {
        wind = Math.Max(wind, 0.0);
        var s = new SeaState { Wind = wind, Fetch = fetch, Gravity = gravity, Depth = depth };
        double u = Math.Max(wind, 0.05);
        s.PeakFrequency = OceanSpectrum.PeakFrequency(u, fetch, gravity);
        s.PeakPeriod = 2.0 * Math.PI / s.PeakFrequency;
        s.PeakWavelength = 2.0 * Math.PI / OceanSpectrum.WaveNumber(s.PeakFrequency, depth, gravity);
        double m0 = OceanSpectrum.HeightVariance(2.0 * Math.PI / 20000.0, 2.0 * Math.PI / ShortestWavelength, u, fetch, depth, gravity);
        s.SignificantHeight = wind < 0.05 ? 0.0 : 4.0 * Math.Sqrt(m0);
        s.WhitecapCoverage = Whitecaps.Coverage(wind);
        s.UpwindVariance = CoxMunk.UpwindVariance(wind);
        s.CrosswindVariance = CoxMunk.CrosswindVariance(wind);
        s.SlopeVariance = CoxMunk.SlopeVariance(wind);
        return s;
    }
}

/// <summary>Whitecap coverage of the sea.</summary>
public static class Whitecaps
{
    /// <summary>
    /// The fraction of the sea surface covered by whitecaps at a wind speed (m/s at 10 m): W = 3.84e-6 U^3.41 (Monahan and
    /// O'Muircheartaigh 1980). 0.3 percent at 7 m/s, 1.8 percent at 12 m/s, 22 percent at 25 m/s.
    /// </summary>
    public static double Coverage(double wind) => Math.Min(3.84e-6 * Math.Pow(Math.Max(wind, 0.0), 3.41), 1.0);

    /// <summary>
    /// The Jacobian of the surface's horizontal mapping below which a point counts as breaking, so that the share of the sea
    /// below it is <paramref name="coverage"/>. The Jacobian is 1 + lambda (Dxx + Dzz) to first order, a Gaussian whose
    /// standard deviation is lambda times the surface's rms slope (the divergence of the displacement is the height times k),
    /// so the threshold is 1 + lambda sigma z with z the normal quantile of the coverage.
    /// </summary>
    public static double JacobianThreshold(double coverage, double lambda, double rmsSlope)
    {
        coverage = Math.Min(Math.Max(coverage, 1e-9), 0.999);
        return 1.0 + lambda * rmsSlope * NormalQuantile(coverage);
    }

    /// <summary>The inverse of the standard normal distribution function (Acklam's rational approximation, relative error 1e-9).</summary>
    public static double NormalQuantile(double p)
    {
        double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
        const double low = 0.02425;
        if (p < low)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }
        if (p > 1.0 - low) return -NormalQuantile(1.0 - p);
        double r = p - 0.5, t = r * r;
        return (((((a[0] * t + a[1]) * t + a[2]) * t + a[3]) * t + a[4]) * t + a[5]) * r / (((((b[0] * t + b[1]) * t + b[2]) * t + b[3]) * t + b[4]) * t + 1.0);
    }
}

/// <summary>The slope statistics of the sea surface that decide the sun's glitter (Cox and Munk 1954, clean surface).</summary>
public static class CoxMunk
{
    /// <summary>Total mean-square slope: 0.003 + 0.00512 U, U the wind speed at 12.5 m (taken as the 10 m wind here).</summary>
    public static double SlopeVariance(double wind) => 0.003 + 0.00512 * Math.Max(wind, 0.0);

    /// <summary>Mean-square slope along the wind: 0.00316 U.</summary>
    public static double UpwindVariance(double wind) => 0.00316 * Math.Max(wind, 0.0);

    /// <summary>Mean-square slope across the wind: 0.003 + 0.00192 U.</summary>
    public static double CrosswindVariance(double wind) => 0.003 + 0.00192 * Math.Max(wind, 0.0);

    /// <summary>
    /// The probability density of the surface slopes (zUp along the wind, zCross across it): Cox and Munk's Gaussian
    /// part, 1 / (2 pi su sc) exp(-(zu^2/su^2 + zc^2/sc^2) / 2), su and sc the rms slopes (the Gram-Charlier skewness
    /// and peakedness terms are left out).
    /// </summary>
    public static double Pdf(double zUp, double zCross, double wind)
    {
        double vu = Math.Max(UpwindVariance(wind), 1e-6), vc = Math.Max(CrosswindVariance(wind), 1e-6);
        return Math.Exp(-0.5 * (zUp * zUp / vu + zCross * zCross / vc)) / (2.0 * Math.PI * Math.Sqrt(vu * vc));
    }

    /// <summary>
    /// The sun's glitter as a bidirectional reflectance factor (per steradian): F p(z) / (4 cos(sun) cos(view) cos^4(beta)), where
    /// the facet that mirrors the sun into the eye has the normal halfway between them and tilt beta. Directions point away
    /// from the surface; y is up and <paramref name="windDirection"/> a horizontal unit vector.
    /// </summary>
    public static double GlitterBrdf(Double3 toSun, Double3 toViewer, Double3 windDirection, double wind, double refractiveIndex = WaterOptics.SeawaterIndex)
    {
        double cs = toSun.Y, cv = toViewer.Y;
        if (cs <= 0.0 || cv <= 0.0) return 0.0;
        Double3 h = (toSun + toViewer).Normalized();
        double cosBeta = h.Y;
        if (cosBeta <= 1e-4) return 0.0;
        double zx = -h.X / cosBeta, zz = -h.Z / cosBeta;
        Double3 across = Double3.Cross(Double3.UnitY, windDirection);
        double zUp = zx * windDirection.X + zz * windDirection.Z, zCross = zx * across.X + zz * across.Z;
        double cosHalf = Math.Min(Math.Max(Double3.Dot(toSun, h), 0.0), 1.0);
        double fresnel = WaterOptics.Fresnel(cosHalf, refractiveIndex);
        return fresnel * Pdf(zUp, zCross, wind) / (4.0 * cs * cv * Math.Pow(cosBeta, 4.0));
    }
}
