using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Relative optical air mass.</summary>
public static class Airmass
{
    /// <summary>
    /// Kasten &amp; Young 1989 (Applied Optics 28, 4735): m = 1/(cos z + 0.50572·(96.07995 − z)^−1.6364),
    /// z in degrees. Zenith angle is clamped to [0, 90]; gives ~38 at the horizon.
    /// </summary>
    public static double KastenYoung(double zenithDegrees)
    {
        double z = Math.Min(Math.Max(zenithDegrees, 0.0), 90.0);
        return 1.0 / (Math.Cos(z * Math.PI / 180.0) + 0.50572 * Math.Pow(96.07995 - z, -1.6364));
    }
}

/// <summary>Beer-Lambert attenuation.</summary>
public static class Extinction
{
    /// <summary>exp(−τ·m): transmittance for a slant path of <paramref name="airmass"/> and zenith optical depth τ.</summary>
    public static double Transmittance(double airmass, double zenithOpticalDepth)
        => Math.Exp(-zenithOpticalDepth * airmass);
}
