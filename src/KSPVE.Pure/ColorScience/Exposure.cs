using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>
/// Physical exposure helpers. Source: Lagarde and de Rousiers, "Moving Frostbite to PBR" (SIGGRAPH 2014),
/// section on the physically based camera (saturation-based speed, K = 12.5, q = 0.65 -> factor 1.2).
/// </summary>
public static class Exposure
{
    /// <summary>EV100 = log2(L * S / K) with S = 100, K = 12.5 (reflected-light meter calibration).</summary>
    public static double Ev100FromAverageLuminance(double luminanceCdM2) =>
        Math.Log(luminanceCdM2 * 100.0 / 12.5, 2.0);

    /// <summary>EV100 = log2(N^2 / t * 100 / S).</summary>
    public static double Ev100FromCamera(double fNumber, double shutterSeconds, double iso) =>
        Math.Log(fNumber * fNumber / shutterSeconds * 100.0 / iso, 2.0);

    /// <summary>Luminance (cd/m^2) that saturates the sensor: 1.2 * 2^EV100.</summary>
    public static double MaxLuminanceFromEv100(double ev100) => 1.2 * Math.Pow(2.0, ev100);

    /// <summary>Multiplier taking scene luminance to [0,1] display range.</summary>
    public static double ExposureScale(double ev100) => 1.0 / MaxLuminanceFromEv100(ev100);
}
