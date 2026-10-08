using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Contrail persistence classes.</summary>
public enum ContrailKind
{
    None,
    ShortLived,
    Persistent,
}

/// <summary>Condensation effects: transonic vapor cones and contrails.</summary>
public static class Condensation
{
    /// <summary>
    /// HEURISTIC (not physics): 0..1 visibility of a Prandtl-Glauert vapor cone. Zero outside Mach 0.85-1.15,
    /// smoothstep up to a peak at Mach 0.98 and down again; humidity (fraction 0..1) ramps smoothstep from 0.6
    /// to 1.0; dynamic pressure saturates as 1 − exp(−q/15 kPa).
    /// </summary>
    public static double VaporConeIntensity(double mach, double relativeHumidity, double dynamicPressurePascals)
    {
        const double lo = 0.85, peak = 0.98, hi = 1.15;
        if (mach <= lo || mach >= hi) return 0.0;
        double machTerm = mach < peak ? SmoothStep(lo, peak, mach) : 1.0 - SmoothStep(peak, hi, mach);
        double humidityTerm = SmoothStep(0.6, 1.0, relativeHumidity);
        double q = Math.Max(dynamicPressurePascals, 0.0);
        double pressureTerm = 1.0 - Math.Exp(-q / 15000.0);
        return machTerm * humidityTerm * pressureTerm;
    }

    /// <summary>
    /// Schmidt-Appleman threshold temperature (°C) below which contrails form, with G = EI·c_p·p/(ε·Q·(1 − η))
    /// (Pa/K, c_p = 1004 J/kg/K, ε = 0.622) and the Schumann 1996 (Meteorol. Z. 5, 4) fit
    /// T_LM = −46.46 + 9.43·ln(G − 0.053) + 0.72·ln²(G − 0.053). This is the liquid-saturation threshold for
    /// zero ambient humidity, valid for G above 0.053; returns −∞ if G ≤ 0.053 (no contrails).
    /// </summary>
    public static double SchmidtApplemanThresholdCelsius(double ambientPressurePascals, double propulsionEfficiency,
        double emissionIndexWaterKgPerKg = 1.25, double fuelHeatJPerKg = 43.2e6)
    {
        const double cp = 1004.0, epsilon = 0.622;
        double g = emissionIndexWaterKgPerKg * cp * ambientPressurePascals
                   / (epsilon * fuelHeatJPerKg * (1.0 - propulsionEfficiency));
        double x = g - 0.053;
        if (x <= 0.0) return double.NegativeInfinity;
        double ln = Math.Log(x);
        return -46.46 + 9.43 * ln + 0.72 * ln * ln;
    }

    /// <summary>
    /// HEURISTIC simplification: contrail forms when ambient T is below the threshold (ignores the humidity
    /// dependence of the true threshold); persistent when additionally ice-supersaturated (RHi ≥ 1), else short-lived.
    /// </summary>
    public static ContrailKind ContrailForms(double ambientTemperatureCelsius, double thresholdCelsius, double relativeHumidityIce)
    {
        if (ambientTemperatureCelsius >= thresholdCelsius) return ContrailKind.None;
        return relativeHumidityIce >= 1.0 ? ContrailKind.Persistent : ContrailKind.ShortLived;
    }

    static double SmoothStep(double e0, double e1, double x)
    {
        double t = Math.Min(Math.Max((x - e0) / (e1 - e0), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
