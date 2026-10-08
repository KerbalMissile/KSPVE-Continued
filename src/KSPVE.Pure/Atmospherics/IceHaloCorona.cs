using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Ice-crystal halos (hexagonal prisms).</summary>
public static class IceHalo
{
    const double DegToRad = Math.PI / 180.0;
    const double RadToDeg = 180.0 / Math.PI;

    /// <summary>
    /// Minimum deviation of a prism, D = 2·asin(n·sin(A/2)) − A (e.g. Tape &amp; Moilanen, Atmospheric Halos, 2006).
    /// A = 60° gives the 22° halo, A = 90° the 46° halo. Returns NaN if the ray is totally internally reflected.
    /// </summary>
    public static double MinimumDeviationDegrees(double refractiveIndex, double prismAngleDegrees)
    {
        double s = refractiveIndex * Math.Sin(prismAngleDegrees * DegToRad / 2.0);
        if (s > 1.0) return double.NaN;
        return 2.0 * Math.Asin(s) * RadToDeg - prismAngleDegrees;
    }

    /// <summary>
    /// Parhelion (sundog) displacement from the Sun for horizontal plate crystals, using Bravais' effective
    /// index n′ = √(n² − sin²e)/cos e in the 60° prism formula. The result is the deviation in the
    /// horizontal-rotation plane (azimuthal offset), 22° at e = 0 and growing with solar elevation;
    /// it is not the great-circle separation. NaN once no parhelion exists (n′·sin30° ≥ 1, e ≳ 60°).
    /// </summary>
    public static double ParhelionOffsetDegrees(double solarElevationDegrees, double refractiveIndex)
    {
        double e = solarElevationDegrees * DegToRad;
        double sinE = Math.Sin(e);
        double nEff = Math.Sqrt(refractiveIndex * refractiveIndex - sinE * sinE) / Math.Cos(e);
        return MinimumDeviationDegrees(nEff, 60.0);
    }
}

/// <summary>Coronae from diffraction by cloud droplets.</summary>
public static class Corona
{
    /// <summary>
    /// HEURISTIC: angular radius (radians) of the first dark ring using the Airy-disk approximation
    /// θ ≈ 1.22·λ/d (Born &amp; Wolf, Principles of Optics, §8.5.2). Real coronae from Mie scattering by
    /// a droplet size distribution differ in ring positions and contrast (Bohren &amp; Huffman 1983).
    /// </summary>
    public static double FirstRingAngularRadiusRadians(double dropletDiameterMeters, double wavelengthMeters)
        => 1.22 * wavelengthMeters / dropletDiameterMeters;
}
