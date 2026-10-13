using System;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Lighting;

namespace KSPVE.Pure.Aurora;

/// <summary>
/// From a line's brightness in rayleighs to the linear RGB the HDR scene holds. A rayleigh is a column emission rate of
/// 10^10 photons per second per square metre of column in all directions, so a line of 1 R seen along the column has a
/// photon radiance of 10^10 / (4 pi) photons s^-1 m^-2 sr^-1 (Hunten, Roach and Chamberlain 1956). Each photon carries
/// hc / lambda; the line's colour is its XYZ under the CIE 1931 observer and so its linear Rec.709 RGB.
/// </summary>
public static class AuroraPhotometry
{
    public const double PlanckConstant = 6.62607015e-34;
    public const double SpeedOfLight = 299792458.0;

    /// <summary>Photons per second per square metre per steradian for 1 rayleigh viewed along the column.</summary>
    public const double PhotonRadiancePerRayleigh = 1.0e10 / (4.0 * Math.PI);

    /// <summary>Lumens per watt at 555 nm (the definition of the candela).</summary>
    public const double MaximumLuminousEfficacy = 683.0;

    /// <summary>
    /// The solar constant at the home planet's distance (W/m^2), the figure Photometry's efficacy is quoted against. The scene
    /// is relative to sunlight, so a unit of it is a perfectly white diffuse surface in full sun: its luminance is the
    /// illuminance divided by pi.
    /// </summary>
    public const double SolarConstant = 1361.0;

    /// <summary>Luminance of the scene's unit 1.0 (cd/m^2): a white Lambertian surface under the full Sun, about 40,000.</summary>
    public static double SceneUnitLuminance => Photometry.IlluminanceLux(SolarConstant) / Math.PI;

    /// <summary>Energy radiance (W m^-2 sr^-1) of <paramref name="rayleighs"/> of a line at <paramref name="wavelengthNm"/>.</summary>
    public static double EnergyRadiance(double rayleighs, double wavelengthNm) =>
        rayleighs * PhotonRadiancePerRayleigh * PlanckConstant * SpeedOfLight / (wavelengthNm * 1e-9);

    /// <summary>Photopic luminance (cd/m^2) of a line of <paramref name="rayleighs"/> at <paramref name="wavelengthNm"/>.</summary>
    public static double Luminance(double rayleighs, double wavelengthNm) =>
        MaximumLuminousEfficacy * CieColor.LuminousEfficiency(wavelengthNm) * EnergyRadiance(rayleighs, wavelengthNm);

    /// <summary>
    /// Physical linear RGB radiance, in cd/m^2 (Rec.709 primaries), of a line of <paramref name="rayleighs"/> at
    /// <paramref name="wavelengthNm"/>. A line outside the sRGB gamut (a saturated green) is desaturated towards white just
    /// far enough to be displayable, keeping its luminance.
    /// </summary>
    public static Rgb LineLuminanceRgb(double rayleighs, double wavelengthNm)
    {
        double w = EnergyRadiance(rayleighs, wavelengthNm) * MaximumLuminousEfficacy;
        CieColor.XyzOfLine(wavelengthNm, out double x, out double y, out double z);
        return CieColor.XyzToLinearRgb(x * w, y * w, z * w, true);
    }

    /// <summary>The same as scene units (1.0 = a sunlit white surface).</summary>
    public static Rgb LineSceneRgb(double rayleighs, double wavelengthNm) =>
        LineLuminanceRgb(rayleighs, wavelengthNm) / SceneUnitLuminance;

    /// <summary>
    /// A set of lines' linear RGB, in cd/m^2, summed as XYZ before the conversion to RGB so that a mixture is placed
    /// where its colour really falls (mauve is violet plus red, which the sRGB gamut can hold even if each line alone cannot).
    /// </summary>
    public static Rgb MixLuminanceRgb(System.Collections.Generic.IEnumerable<(double wavelengthNm, double rayleighs)> lines)
    {
        double sx = 0, sy = 0, sz = 0;
        foreach ((double wavelength, double rayleighs) in lines)
        {
            double w = EnergyRadiance(rayleighs, wavelength) * MaximumLuminousEfficacy;
            CieColor.XyzOfLine(wavelength, out double x, out double y, out double z);
            sx += x * w; sy += y * w; sz += z * w;
        }
        return CieColor.XyzToLinearRgb(sx, sy, sz, true);
    }
}

/// <summary>The CIE 1931 2-degree standard observer and the conversion to linear sRGB.</summary>
public static class CieColor
{
    // Multi-lobe Gaussian fit of the colour matching functions: Wyman, Sloan and Shirley, "Simple Analytic Approximations
    // to the CIE XYZ Color Matching Functions" (JCGT 2013), accurate to a fraction of a per cent of the tables.
    private static double Lobe(double l, double mu, double s1, double s2)
    {
        double t = (l - mu) / (l < mu ? s1 : s2);
        return Math.Exp(-0.5 * t * t);
    }

    public static void XyzOfLine(double wavelengthNm, out double x, out double y, out double z)
    {
        double l = wavelengthNm;
        x = 1.056 * Lobe(l, 599.8, 37.9, 31.0) + 0.362 * Lobe(l, 442.0, 16.0, 26.7) - 0.065 * Lobe(l, 501.1, 20.4, 26.2);
        y = 0.821 * Lobe(l, 568.8, 46.9, 40.5) + 0.286 * Lobe(l, 530.9, 16.3, 31.1);
        z = 1.217 * Lobe(l, 437.0, 11.8, 36.0) + 0.681 * Lobe(l, 459.0, 26.0, 13.8);
    }

    /// <summary>The photopic luminous efficiency V(lambda) (the CIE y-bar).</summary>
    public static double LuminousEfficiency(double wavelengthNm)
    {
        XyzOfLine(wavelengthNm, out _, out double y, out _);
        return y;
    }

    /// <summary>
    /// XYZ to linear Rec.709 RGB. With <paramref name="fitGamut"/> a colour with a negative channel gets white added until it
    /// has none, then is scaled back to its luminance, so a monochromatic line shows as its most saturated displayable colour.
    /// </summary>
    public static Rgb XyzToLinearRgb(double x, double y, double z, bool fitGamut)
    {
        double r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        double g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        double b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        if (fitGamut)
        {
            double lowest = Math.Min(r, Math.Min(g, b));
            if (lowest < 0.0)
            {
                // White (equal-energy in linear RGB) of the same luminance carries the missing amount.
                double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double add = -lowest;
                r += add; g += add; b += add;
                double after = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                double k = after > 1e-300 ? Math.Max(lum, 0.0) / after : 0.0;
                r *= k; g *= k; b *= k;
            }
        }
        return new Rgb(r, g, b);
    }
}
