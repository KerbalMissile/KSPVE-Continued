using System;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Stars;

/// <summary>
/// The light of a star's surface and surroundings: limb darkening from the effective temperature, the corona's brightness,
/// and how an eclipse reveals it. Brightnesses are relative to the star's mean disc brightness unless said otherwise.
/// </summary>
public static class StarOptics
{
    /// <summary>hc/k in nm K.</summary>
    private const double SecondRadiation = 1.438777e7;

    /// <summary>The red, green and blue wavelengths (nm) a linear sRGB image is made of, in effect.</summary>
    public const double Red = 610.0, Green = 550.0, Blue = 465.0;

    /// <summary>Planck's law, B_lambda up to a constant factor (the same for every temperature at one wavelength).</summary>
    public static double Planck(double wavelengthNm, double kelvin)
    {
        double x = SecondRadiation / (wavelengthNm * Math.Max(kelvin, 1.0));
        return 1.0 / (Math.Pow(wavelengthNm, 5.0) * (Math.Exp(Math.Min(x, 700.0)) - 1.0));
    }

    /// <summary>
    /// The temperature (K) at optical depth tau in a grey atmosphere in radiative equilibrium (Eddington's solution),
    /// T^4 = (3/4) Teff^4 (tau + 2/3).
    /// </summary>
    public static double GreyTemperature(double effective, double tau) =>
        effective * Math.Pow(0.75 * (Math.Max(tau, 0.0) + 2.0 / 3.0), 0.25);

    /// <summary>
    /// The intensity leaving the disc at direction cosine <paramref name="mu"/> (1 at the centre, 0 at the limb), relative to
    /// the centre's, at one wavelength: the Eddington-Barbier relation, I(mu) = S(tau = mu), with the Planck function at the
    /// grey atmosphere's temperature there. The limb is cooler, so darker, and darker and redder the shorter the
    /// wavelength: at 550 nm the Sun's limb (mu = 0.1) is about 0.35 of the centre, close to the measured law.
    /// </summary>
    public static double LimbRatio(double mu, double effective, double wavelengthNm)
    {
        double m = Math.Min(Math.Max(mu, 0.0), 1.0);
        return Planck(wavelengthNm, GreyTemperature(effective, m)) / Planck(wavelengthNm, GreyTemperature(effective, 1.0));
    }

    /// <summary>
    /// The disc's mean intensity relative to its centre's at a wavelength, by integrating over the disc (weight mu dmu).
    /// </summary>
    public static double MeanOverCentre(double effective, double wavelengthNm)
    {
        const int n = 200;
        double sum = 0.0, weight = 0.0;
        for (int i = 0; i < n; i++)
        {
            double mu = (i + 0.5) / n;
            sum += LimbRatio(mu, effective, wavelengthNm) * mu;
            weight += mu;
        }
        return sum / weight;
    }

    /// <summary>
    /// The K-corona's brightness (scattering by free electrons) at <paramref name="rho"/> stellar radii from the centre in the
    /// sky plane, in units of the mean disc brightness: Baumbach-Allen (Allen's Astrophysical Quantities, the equatorial
    /// formula), 1e-6 (0.0532 rho^-2.5 + 1.425 rho^-7 + 2.565 rho^-17). It is about 2.7e-6 at 1.03 R, 1e-6 at 1.15 R,
    /// 2e-8 at 2 R. Over the poles it is about 0.45 of that (<paramref name="polar"/> 0..1).
    /// </summary>
    public static double KCorona(double rho, double polar = 0.0)
    {
        double r = Math.Max(rho, 1.0);
        double k = 1e-6 * (0.0532 * Math.Pow(r, -2.5) + 1.425 * Math.Pow(r, -7.0) + 2.565 * Math.Pow(r, -17.0));
        return k * (1.0 - 0.55 * Math.Min(Math.Max(polar, 0.0), 1.0));
    }

    /// <summary>The F-corona (sunlight scattered by interplanetary dust), much fainter than the K close in and dominant beyond about 3 radii.</summary>
    public static double FCorona(double rho) => 7e-9 * Math.Pow(Math.Max(rho, 1.0), -2.2);

    /// <summary>
    /// How much of the corona an eye can make out: the disc's veiling glare hides it until the disc is almost entirely covered.
    /// <paramref name="visibleFraction"/> is the share of the disc's light still reaching the eye (1 = no eclipse, 0 = total).
    /// A deep partial eclipse (below a thousandth) shows it; 0.3 per cent and more hides it.
    /// </summary>
    public static double CoronaVisibility(double visibleFraction)
    {
        double f = Math.Min(Math.Max(visibleFraction, 0.0), 1.0);
        double x = Math.Min(Math.Max((f - 1e-4) / (3e-3 - 1e-4), 0.0), 1.0);
        return 1.0 - x * x * (3.0 - 2.0 * x);
    }

    /// <summary>
    /// The share of a limb-darkened disc's light that gets through when a body of angular radius
    /// <paramref name="occluderRadius"/> covers it, seen with the occluder's centre <paramref name="separation"/> from the
    /// disc's (any one angular unit); Eclipse's lens integral with the linear law matched to this star at 550 nm.
    /// </summary>
    public static double DiscVisibleFraction(double starRadius, double occluderRadius, double separation, double effective)
    {
        // The linear coefficient that matches I(mu = 0.5)/I(1) of the Eddington law.
        double u = 2.0 * (1.0 - LimbRatio(0.5, effective, Green));
        return Eclipse.VisibleFractionLimbDarkened(starRadius, occluderRadius, separation, Math.Min(Math.Max(u, 0.0), 1.0), 64);
    }
}
