namespace VolumetricBlackholeFx.Pure.Space.Relativity;

/// <summary>Thin accretion disk helpers (Shakura and Sunyaev 1973; Novikov and Thorne 1973).</summary>
public static class AccretionDisk
{
    /// <summary>
    /// Circular-orbit speed (fraction of c) measured by a local static observer: sqrt(m/(r-2m)).
    /// Bardeen, Press, Teukolsky 1972. Returns 1 at or inside the horizon.
    /// </summary>
    public static double OrbitalSpeedStaticFrame(double radius, double m)
    {
        double d = radius - 2.0 * m;
        return d > 0 ? System.Math.Sqrt(m / d) : 1.0;
    }

    /// <summary>
    /// T ~ r^(-3/4) (1 - sqrt(r_in/r))^(1/4), scaled so the maximum (at r = 49/36 r_in) equals the peak.
    /// This is the Newtonian zero-torque profile, not the full Novikov-Thorne relativistic one.
    /// </summary>
    public static double ShakuraSunyaevTemperature(double radius, double innerRadius, double peakTemperatureKelvin)
    {
        if (radius <= innerRadius) return 0.0;
        double norm = Shape(49.0 / 36.0 * innerRadius, innerRadius);
        return peakTemperatureKelvin * Shape(radius, innerRadius) / norm;
    }

    /// <summary>g * T.</summary>
    public static double ObservedTemperature(double emittedKelvin, double shiftFactorG) => shiftFactorG * emittedKelvin;

    /// <summary>g^4: bolometric intensity scales as g^4 (Luminet 1979).</summary>
    public static double ObservedBolometricIntensityScale(double g)
    {
        double g2 = g * g;
        return g2 * g2;
    }

    private static double Shape(double r, double rin) =>
        System.Math.Pow(r, -0.75) * System.Math.Pow(1.0 - System.Math.Sqrt(rin / r), 0.25);
}
