using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>Blackbody colour temperature to linear Rec.709.</summary>
public static class ColorTemperature
{
    public const double MinKelvin = 1667.0;
    public const double MaxKelvin = 25000.0;

    /// <summary>
    /// CIE 1931 xy of the Planckian locus, Kim et al. (2002) cubic-spline approximation, clamped to 1667 K - 25000 K.
    /// </summary>
    public static void PlanckianXy(double kelvin, out double x, out double y)
    {
        double t = Math.Max(MinKelvin, Math.Min(MaxKelvin, kelvin));
        double t2 = t * t, t3 = t2 * t;
        if (t <= 4000.0)
            x = -0.2661239e9 / t3 - 0.2343589e6 / t2 + 0.8776956e3 / t + 0.179910;
        else
            x = -3.0258469e9 / t3 + 2.1070379e6 / t2 + 0.2226347e3 / t + 0.240390;

        double x2 = x * x, x3 = x2 * x;
        if (t <= 2222.0)
            y = -1.1063814 * x3 - 1.34811020 * x2 + 2.18555832 * x - 0.20219683;
        else if (t <= 4000.0)
            y = -0.9549476 * x3 - 1.37418593 * x2 + 2.09137015 * x - 0.16748867;
        else
            y = 3.0817580 * x3 - 5.87338670 * x2 + 3.75112997 * x - 0.37001483;
    }

    /// <summary>XYZ (Y = 1) of the Planckian locus at the given temperature.</summary>
    public static Numerics.Double3 PlanckianXyz(double kelvin)
    {
        PlanckianXy(kelvin, out double x, out double y);
        return new Numerics.Double3(x / y, 1.0, (1.0 - x - y) / y);
    }

    /// <summary>Linear Rec.709 colour of a blackbody, negatives clamped, normalised so the max component is 1.</summary>
    public static Rgb ToLinearRec709(double kelvin)
    {
        var xyz = PlanckianXyz(kelvin);
        Rgb c = XyzToLinearRec709(xyz.X, xyz.Y, xyz.Z);
        c = new Rgb(Math.Max(0.0, c.R), Math.Max(0.0, c.G), Math.Max(0.0, c.B));
        double m = c.MaxComponent;
        return m > 0.0 ? c / m : c;
    }

    /// <summary>XYZ (D65) to linear sRGB/Rec.709 matrix as tabulated by Bruce Lindbloom (7 digits; the IEC 61966-2-1 matrix differs from the fourth decimal).</summary>
    public static Rgb XyzToLinearRec709(double x, double y, double z) => new Rgb(
        3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
        -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
        0.0556434 * x - 0.2040259 * y + 1.0572252 * z);

    /// <summary>Linear Rec.709 to XYZ (inverse of the matrix above, D65).</summary>
    public static Numerics.Double3 LinearRec709ToXyz(Rgb c) => new Numerics.Double3(
        0.4124564 * c.R + 0.3575761 * c.G + 0.1804375 * c.B,
        0.2126729 * c.R + 0.7151522 * c.G + 0.0721750 * c.B,
        0.0193339 * c.R + 0.1191920 * c.G + 0.9503041 * c.B);

    /// <summary>Planck's law: spectral radiance in W / (sr m^2 m) at the wavelength (m) and temperature (K).</summary>
    public static double PlanckSpectralRadiance(double wavelengthMeters, double kelvin)
    {
        const double h = 6.62607015e-34;
        const double c = 299792458.0;
        const double k = 1.380649e-23;
        double l5 = Math.Pow(wavelengthMeters, 5);
        return 2.0 * h * c * c / l5 / (Math.Exp(h * c / (wavelengthMeters * k * kelvin)) - 1.0);
    }
}
