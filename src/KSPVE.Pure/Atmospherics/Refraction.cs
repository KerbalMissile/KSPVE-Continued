using System;

namespace KSPVE.Pure.Atmospherics;

/// <summary>Atmospheric refraction of celestial bodies near the horizon.</summary>
public static class Refraction
{
    const double DegToRad = Math.PI / 180.0;

    /// <summary>
    /// Refraction in arcminutes for an APPARENT altitude (Bennett 1982, as given in Meeus,
    /// Astronomical Algorithms 2nd ed., eq. 16.4): R = cot(h + 7.31/(h + 4.4)), for standard
    /// conditions (1010 mbar, 10 C). Altitude is clamped to -1 degree.
    /// </summary>
    public static double BennettArcminutes(double apparentAltitudeDegrees)
    {
        double h = Math.Max(apparentAltitudeDegrees, -1.0);
        return 1.0 / Math.Tan((h + 7.31 / (h + 4.4)) * DegToRad);
    }

    /// <summary>
    /// Refraction in arcminutes for a TRUE altitude (Saemundsson 1986, Sky &amp; Telescope 72, 70;
    /// Meeus eq. 16.5): R = 1.02 cot(h + 10.3/(h + 5.11)). Altitude is clamped to -4 degrees.
    /// </summary>
    public static double SaemundssonArcminutes(double trueAltitudeDegrees)
    {
        double h = Math.Max(trueAltitudeDegrees, -4.0);
        return 1.02 / Math.Tan((h + 10.3 / (h + 5.11)) * DegToRad);
    }

    /// <summary>
    /// Multiplier for refraction under non-standard conditions, (P/101000 Pa)·(283 K/T)
    /// (Meeus, Astronomical Algorithms ch. 16: correction for pressure and temperature).
    /// </summary>
    public static double PressureTemperatureScale(double pressurePascals, double temperatureKelvin)
        => pressurePascals / 101000.0 * (283.0 / temperatureKelvin);

    /// <summary>
    /// Apparent vertical / horizontal diameter ratio of the Sun (or Moon) from differential
    /// refraction of the upper and lower limbs (Saemundsson formula scaled by <paramref name="scale"/>).
    /// Horizontal extent is unaffected by refraction to first order.
    /// </summary>
    public static double SunVerticalFlattening(double trueAltitudeOfCenterDegrees, double sunAngularRadiusDegrees, double scale)
    {
        double upper = trueAltitudeOfCenterDegrees + sunAngularRadiusDegrees;
        double lower = trueAltitudeOfCenterDegrees - sunAngularRadiusDegrees;
        double apparentUpper = upper + scale * SaemundssonArcminutes(upper) / 60.0;
        double apparentLower = lower + scale * SaemundssonArcminutes(lower) / 60.0;
        return (apparentUpper - apparentLower) / (2.0 * sunAngularRadiusDegrees);
    }
}
