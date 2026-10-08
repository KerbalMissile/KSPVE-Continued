using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Light reflected by a planet onto a nearby receiver.</summary>
public static class Planetshine
{
    /// <summary>
    /// Illuminance from a Lambertian sphere: E = E_sun·p·(R/d)²·Φ(α), geometric albedo p = (2/3)·A_bond
    /// (Russell 1916; Seager, Exoplanets 2010, ch. 1). Far-field formula; distance is clamped to the
    /// body radius, and accuracy degrades near the surface (heuristic there).
    /// </summary>
    public static double IlluminanceLux(double sunIlluminanceLux, double bondAlbedo, double bodyRadiusMeters,
        double distanceFromBodyCenterMeters, double phaseAngleRadians)
    {
        double d = Math.Max(distanceFromBodyCenterMeters, bodyRadiusMeters);
        double ratio = bodyRadiusMeters / d;
        double p = 2.0 / 3.0 * bondAlbedo;
        return sunIlluminanceLux * p * ratio * ratio * PhaseFunctions.LambertSphere(phaseAngleRadians);
    }
}
