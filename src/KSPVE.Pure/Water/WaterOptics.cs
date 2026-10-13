using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Water;

/// <summary>
/// How light behaves at and in water: the Fresnel reflection of the surface, and the colour of the water column from its
/// absorption and scattering (red is absorbed first, so deep water is blue, and green where there is plankton).
/// Colours are worked at three wavelengths standing for the display's red, green and blue (610, 550 and 465 nm).
/// </summary>
public static class WaterOptics
{
    /// <summary>Refractive index of seawater at 589 nm (Quan and Fry 1995: 1.339 at 35 PSU, 20 C).</summary>
    public const double SeawaterIndex = 1.339;

    /// <summary>The wavelengths (nm) of the red, green and blue channels used here.</summary>
    public static readonly Double3 Wavelengths = new Double3(610.0, 550.0, 465.0);

    /// <summary>
    /// Absorption coefficient of pure water (1/m) at the three wavelengths: Pope and Fry (1997), 0.289 at 610 nm,
    /// 0.0565 at 550 nm, 0.0105 at 465 nm. Red goes in a few metres, blue lasts tens.
    /// </summary>
    public static readonly Double3 PureAbsorption = new Double3(0.289, 0.0565, 0.0105);

    /// <summary>
    /// Scattering coefficient of pure water (1/m): Morel (1974), 0.00288 (lambda / 500 nm)^-4.32.
    /// </summary>
    public static Double3 PureScattering() =>
        new Double3(Rayleigh(Wavelengths.X), Rayleigh(Wavelengths.Y), Rayleigh(Wavelengths.Z));

    private static double Rayleigh(double nm) => 0.00288 * Math.Pow(nm / 500.0, -4.32);

    /// <summary>
    /// Absorption (1/m) of water holding chlorophyll at <paramref name="chlorophyll"/> mg/m3: pure water plus phytoplankton
    /// a_ph = 0.06 A(lambda) C^0.65 (Morel and Maritorena 2001; A rounded to 0.10 at 610 nm, 0.39 at 550 nm and 0.74 at 465 nm).
    /// </summary>
    public static Double3 Absorption(double chlorophyll)
    {
        double c = Math.Pow(Math.Max(chlorophyll, 0.0), 0.65);
        return PureAbsorption + new Double3(0.10, 0.39, 0.74) * (0.06 * c);
    }

    /// <summary>
    /// Backscattering coefficient (1/m): half of pure water's scattering plus the particles' share, with
    /// b_p = 0.30 C^0.62 at 550 nm falling as 1/lambda (Gordon and Morel 1983) and the particle backscatter ratio
    /// 0.002 + 0.01 (0.5 - 0.25 log10 C) (Morel 1988).
    /// </summary>
    public static Double3 Backscattering(double chlorophyll)
    {
        double c = Math.Max(chlorophyll, 1e-3);
        double bp550 = 0.30 * Math.Pow(c, 0.62);
        double ratio = Math.Max(0.002 + 0.01 * (0.5 - 0.25 * Math.Log10(c)), 0.001);
        Double3 w = PureScattering() * 0.5;
        return new Double3(
            w.X + ratio * bp550 * 550.0 / Wavelengths.X,
            w.Y + ratio * bp550,
            w.Z + ratio * bp550 * 550.0 / Wavelengths.Z);
    }

    /// <summary>
    /// Irradiance reflectance of deep water (the share of the light going down that comes back up): 0.33 b_b / (a + b_b)
    /// (Morel and Prieur 1977). Per channel; about 0.05 blue and 0.0003 red for clear ocean.
    /// </summary>
    public static Double3 DeepReflectance(double chlorophyll) => DeepReflectance(Absorption(chlorophyll), Backscattering(chlorophyll));

    public static Double3 DeepReflectance(Double3 a, Double3 bb) =>
        new Double3(0.33 * bb.X / (a.X + bb.X), 0.33 * bb.Y / (a.Y + bb.Y), 0.33 * bb.Z / (a.Z + bb.Z));

    /// <summary>
    /// The coefficient (1/m) the light's two-way trip to a depth fades at: K_d + K_u, with K_d = (a + b_b) / cos(sun inside
    /// the water) going down and K_u = 1.04 (a + b_b) / cos(view inside the water) coming up (Maritorena, Morel and Gentili
    /// 1994, the shallow-water reflectance model).
    /// </summary>
    public static Double3 TwoWayAttenuation(Double3 a, Double3 bb, double cosSunInWater, double cosViewInWater)
    {
        double ds = 1.0 / Math.Max(cosSunInWater, 0.05), dv = 1.04 / Math.Max(cosViewInWater, 0.05);
        return new Double3((a.X + bb.X) * (ds + dv), (a.Y + bb.Y) * (ds + dv), (a.Z + bb.Z) * (ds + dv));
    }

    /// <summary>
    /// The water-leaving reflectance (irradiance ratio) over a bottom of albedo <paramref name="bottom"/> at vertical depth
    /// <paramref name="depth"/> (m): R = Rinf (1 - e^-Kz) + bottom e^-Kz (Maritorena et al. 1994). Divided by pi it is the
    /// radiance per unit irradiance.
    /// </summary>
    public static Double3 ShallowReflectance(Double3 deep, Double3 k, double depth, Double3 bottom)
    {
        double ex = Math.Exp(-k.X * depth), ey = Math.Exp(-k.Y * depth), ez = Math.Exp(-k.Z * depth);
        return new Double3(deep.X * (1 - ex) + bottom.X * ex, deep.Y * (1 - ey) + bottom.Y * ey, deep.Z * (1 - ez) + bottom.Z * ez);
    }

    /// <summary>
    /// The fraction of the light from the sea floor that reaches the eye through <paramref name="pathLength"/> metres of
    /// water: exp(-(a + b_b) * path). Used to tint what is seen through the water.
    /// </summary>
    public static Double3 Transmittance(double chlorophyll, double pathLength)
    {
        Double3 a = Absorption(chlorophyll), bb = Backscattering(chlorophyll);
        return new Double3(Math.Exp(-(a.X + bb.X) * pathLength), Math.Exp(-(a.Y + bb.Y) * pathLength), Math.Exp(-(a.Z + bb.Z) * pathLength));
    }

    /// <summary>
    /// Fresnel reflectance of an unpolarised ray on a flat interface from the side of index 1 into index
    /// <paramref name="n"/> (the exact Fresnel equations), <paramref name="cosIncidence"/> the cosine of the angle from the normal.
    /// 0.02 straight down for water, 1 at grazing.
    /// </summary>
    public static double Fresnel(double cosIncidence, double n = SeawaterIndex)
    {
        double ci = Math.Min(Math.Max(cosIncidence, 0.0), 1.0);
        double sin2t = (1.0 - ci * ci) / (n * n);
        double ct = Math.Sqrt(Math.Max(1.0 - sin2t, 0.0));
        double rs = (ci - n * ct) / (ci + n * ct);
        double rp = (n * ci - ct) / (n * ci + ct);
        return 0.5 * (rs * rs + rp * rp);
    }

    /// <summary>Reflectance at normal incidence, ((n-1)/(n+1))^2.</summary>
    public static double NormalReflectance(double n = SeawaterIndex) => (n - 1.0) * (n - 1.0) / ((n + 1.0) * (n + 1.0));

    /// <summary>Schlick's approximation, the cheap one the shaders use.</summary>
    public static double Schlick(double cosIncidence, double n = SeawaterIndex)
    {
        double f0 = NormalReflectance(n);
        return f0 + (1.0 - f0) * Math.Pow(1.0 - Math.Min(Math.Max(cosIncidence, 0.0), 1.0), 5.0);
    }

    /// <summary>Snell's law: the cosine of the angle of the ray inside the water.</summary>
    public static double CosRefracted(double cosIncidence, double n = SeawaterIndex)
    {
        double s2 = (1.0 - cosIncidence * cosIncidence) / (n * n);
        return Math.Sqrt(Math.Max(1.0 - s2, 0.0));
    }
}
