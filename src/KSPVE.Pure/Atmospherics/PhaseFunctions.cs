using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Scattering phase functions. Angular functions are normalised so the integral over the sphere is 1 (per steradian).</summary>
public static class PhaseFunctions
{
    /// <summary>
    /// Lambertian-sphere phase function (Russell 1916): (sin α + (π − α)cos α)/π. NOTE: this is the
    /// astronomical phase function normalised to 1 at α = 0, not a per-steradian density.
    /// </summary>
    public static double LambertSphere(double phaseAngleRadians)
    {
        double a = phaseAngleRadians;
        return (Math.Sin(a) + (Math.PI - a) * Math.Cos(a)) / Math.PI;
    }

    /// <summary>Henyey &amp; Greenstein 1941: (1 − g²)/(4π(1 + g² − 2g cosθ)^1.5).</summary>
    public static double HenyeyGreenstein(double cosTheta, double g)
        => (1.0 - g * g) / (4.0 * Math.PI * Math.Pow(1.0 + g * g - 2.0 * g * cosTheta, 1.5));

    /// <summary>Rayleigh: 3/(16π)(1 + cos²θ).</summary>
    public static double Rayleigh(double cosTheta)
        => 3.0 / (16.0 * Math.PI) * (1.0 + cosTheta * cosTheta);

    /// <summary>
    /// Cornette &amp; Shanks 1992 (Applied Optics 31, 3152), the normalised Henyey-Greenstein/Rayleigh hybrid:
    /// 3(1 − g²)(1 + cos²θ)/(8π(2 + g²)(1 + g² − 2g cosθ)^1.5). Equals Rayleigh for g = 0.
    /// </summary>
    public static double CornetteShanks(double cosTheta, double g)
        => 3.0 * (1.0 - g * g) * (1.0 + cosTheta * cosTheta)
           / (8.0 * Math.PI * (2.0 + g * g) * Math.Pow(1.0 + g * g - 2.0 * g * cosTheta, 1.5));
}
