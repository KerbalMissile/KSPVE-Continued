using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Geometric-optics (Descartes) rainbow angles.</summary>
public static class Rainbow
{
    const double RadToDeg = 180.0 / Math.PI;

    /// <summary>
    /// Refractive index of pure water at 20 C, 400-700 nm (valid roughly 0.18-1.13 µm), from the
    /// Sellmeier fit of Daimon &amp; Masumura 2007, Applied Optics 46, 3811 (as tabulated on refractiveindex.info).
    /// </summary>
    public static double WaterRefractiveIndex(double wavelengthNanometers)
    {
        double l2 = wavelengthNanometers * wavelengthNanometers * 1e-6; // µm^2
        double n2 = 1.0
            + 5.684027565e-1 * l2 / (l2 - 5.101829712e-3)
            + 1.726177391e-1 * l2 / (l2 - 1.821153936e-2)
            + 2.086189578e-2 * l2 / (l2 - 2.620722293e-2)
            + 1.130748688e-1 * l2 / (l2 - 1.069792721e1);
        return Math.Sqrt(n2);
    }

    /// <summary>
    /// Minimum deviation of a ray with <paramref name="internalReflections"/> (k ≥ 1) reflections inside a
    /// sphere: cos²i = (n² − 1)/(k(k+2)) (Descartes), D = 180°·k + 2i − 2(k+1)r with sin i = n sin r.
    /// </summary>
    public static double MinimumDeviationDegrees(double refractiveIndex, int internalReflections)
    {
        if (internalReflections < 1) throw new ArgumentOutOfRangeException(nameof(internalReflections));
        int k = internalReflections;
        double cos2i = (refractiveIndex * refractiveIndex - 1.0) / (k * (k + 2.0));
        double i = Math.Acos(Math.Sqrt(cos2i));
        double r = Math.Asin(Math.Sin(i) / refractiveIndex);
        return 180.0 * k + (2.0 * i - 2.0 * (k + 1) * r) * RadToDeg;
    }

    /// <summary>
    /// Angular radius of the bow from the antisolar point: 180° − D for k = 1, D − 180° for k = 2
    /// For k ≥ 3, D mod 360° is folded into [0°, 180°] (these bows lie near the Sun and are not physically observed).
    /// </summary>
    public static double RainbowAngleDegrees(double refractiveIndex, int internalReflections)
    {
        double d = MinimumDeviationDegrees(refractiveIndex, internalReflections);
        if (internalReflections == 1) return 180.0 - d;
        if (internalReflections == 2) return d - 180.0;
        double a = d % 360.0;
        return a > 180.0 ? 360.0 - a : a;
    }
}
