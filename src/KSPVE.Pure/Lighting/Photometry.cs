using System;

namespace KSPVE.Pure.Lighting;

/// <summary>Conversions between radiometric and photometric quantities for starlight.</summary>
public static class Photometry
{
    /// <summary>
    /// Luminous efficacy of sunlight above the atmosphere, lumens per watt
    /// (about 1361 W/m^2 corresponds to about 128,000 lux).
    /// </summary>
    public const double SolarLuminousEfficacy = 93.0;

    /// <summary>Irradiance in W/m^2 at <paramref name="distanceMeters"/> from a star of the given luminosity.</summary>
    public static double FluxAtDistance(double luminosityWatts, double distanceMeters)
    {
        if (!(distanceMeters > 0.0)) throw new ArgumentOutOfRangeException(nameof(distanceMeters), distanceMeters, "Distance must be positive.");
        return luminosityWatts / (4.0 * Math.PI * distanceMeters * distanceMeters);
    }

    /// <summary>Illuminance in lux for an irradiance in W/m^2.</summary>
    public static double IlluminanceLux(double fluxWattsPerSquareMeter, double efficacy = SolarLuminousEfficacy)
        => fluxWattsPerSquareMeter * efficacy;
}
