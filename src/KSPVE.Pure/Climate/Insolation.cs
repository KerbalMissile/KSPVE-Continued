using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Seasons;

/// <summary>
/// Sunlight on a spinning, tilted planet: the standard daily-mean insolation formula (Berger 1978; see also Hartmann,
/// Global Physical Climatology, eq. 2.4), Kepler's equation for where a planet is in its orbit, and the sun's declination
/// through the year from the planet's axis against its orbit.
/// </summary>
public static class Insolation
{
    /// <summary>
    /// The day's mean flux on a horizontal surface at <paramref name="latitude"/> (rad) when the sun's declination is
    /// <paramref name="declination"/> (rad): Q = S / pi * (h0 sin(lat) sin(dec) + cos(lat) cos(dec) sin h0), with h0 the
    /// half-day length as an hour angle (cos h0 = -tan(lat) tan(dec); 0 in polar night, pi in polar day).
    /// <paramref name="flux"/> is the irradiance at the planet's present distance from its star (W/m^2).
    /// </summary>
    public static double DailyMean(double flux, double latitude, double declination)
    {
        double limit = 0.5 * Math.PI - 1e-9;
        latitude = Math.Max(-limit, Math.Min(limit, latitude));
        double product = -Math.Tan(latitude) * Math.Tan(declination);
        double h0 = product >= 1.0 ? 0.0 : product <= -1.0 ? Math.PI : Math.Acos(product);
        double q = h0 * Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Sin(h0);
        return Math.Max(flux / Math.PI * q, 0.0);
    }

    /// <summary>The flux on a horizontal surface at a given hour angle (rad, 0 at local noon), the sun at <paramref name="declination"/>.</summary>
    public static double Instant(double flux, double latitude, double declination, double hourAngle)
    {
        double sinElevation = Math.Sin(latitude) * Math.Sin(declination) + Math.Cos(latitude) * Math.Cos(declination) * Math.Cos(hourAngle);
        return flux * Math.Max(sinElevation, 0.0);
    }

    /// <summary>Kepler's equation: the true anomaly (rad) for a mean anomaly (rad) on an orbit of eccentricity <paramref name="e"/> (0 &lt;= e &lt; 1).</summary>
    public static double TrueAnomaly(double meanAnomaly, double e)
    {
        e = Math.Max(0.0, Math.Min(e, 0.99));
        double m = meanAnomaly - 2.0 * Math.PI * Math.Floor(meanAnomaly / (2.0 * Math.PI));
        double big = e < 0.8 ? m : Math.PI;
        for (int i = 0; i < 30; i++)
        {
            double step = (big - e * Math.Sin(big) - m) / (1.0 - e * Math.Cos(big));
            big -= step;
            if (Math.Abs(step) < 1e-13) break;
        }
        return 2.0 * Math.Atan2(Math.Sqrt(1.0 + e) * Math.Sin(big / 2.0), Math.Sqrt(1.0 - e) * Math.Cos(big / 2.0));
    }

    /// <summary>The flux relative to its value at the semi-major axis: (a / r)^2 = ((1 + e cos nu) / (1 - e^2))^2.</summary>
    public static double DistanceFactor(double trueAnomaly, double e)
    {
        double f = (1.0 + e * Math.Cos(trueAnomaly)) / (1.0 - e * e);
        return f * f;
    }
}

/// <summary>Which season a hemisphere is in.</summary>
public enum SeasonName { None, Spring, Summer, Autumn, Winter }

/// <summary>
/// A planet's seasons: its rotation axis against the orbit it travels round its star (a moon takes its planet's orbit).
/// The sun's declination is sin(dec) = sin(obliquity) sin(lambda), lambda = true anomaly + the longitude of perihelion
/// measured from the northern spring equinox. Built from the real vectors, so a stock planet (axis along its orbit's normal)
/// has none, a real one (Earth 23.4 degrees, Mars 25.2, Uranus 97.8) has its own, and a retrograde planet's is on the
/// other side of 90 degrees.
/// </summary>
public sealed class SeasonOrbit
{
    /// <summary>The orbit round the star: eccentricity and period (s).</summary>
    public double Eccentricity, Period;
    /// <summary>The axis against the orbit's normal (rad, 0..pi); the sun's greatest declination is its sine.</summary>
    public double Obliquity { get; private set; }
    /// <summary>The longitude of perihelion (rad) from the northern spring equinox.</summary>
    public double PerihelionLongitude { get; private set; }

    /// <summary>The angle of the rotation axis from the orbit's normal as it really is (before any pretend tilt).</summary>
    public double RealObliquity { get; private set; }

    public SeasonOrbit(double eccentricity, double period)
    {
        Eccentricity = Math.Max(0.0, Math.Min(eccentricity, 0.99));
        Period = period;
    }

    /// <summary>
    /// From the planet's north pole direction and its orbit's perihelion direction and the direction a quarter of an orbit
    /// later (all in one inertial frame, from the star's side the planet moves along; they need not be unit vectors).
    /// </summary>
    public static SeasonOrbit FromAxis(Double3 north, Double3 perihelion, Double3 quarter, double eccentricity, double period)
    {
        Double3 s = Unit(north), e = Unit(perihelion), q = Unit(quarter);
        // The sun is seen from the planet along -(cos nu e + sin nu q).
        double a = Double3.Dot(s, e), b = Double3.Dot(s, q);
        Double3 normal = Cross(e, q);
        double cosObliquity = Double3.Dot(s, normal);
        var orbit = new SeasonOrbit(eccentricity, period);
        double sin = Math.Sqrt(a * a + b * b);
        orbit.Obliquity = orbit.RealObliquity = Math.Atan2(sin, cosObliquity);
        // -(a cos nu + b sin nu) = sin(eps) sin(nu + w)  =>  sin w = -a / sin(eps), cos w = -b / sin(eps)
        orbit.PerihelionLongitude = sin > 1e-9 ? Math.Atan2(-a, -b) : 0.0;
        return orbit;
    }

    /// <summary>The same orbit with the axis tilted to <paramref name="obliquity"/> (rad) the same way round (a display setting: stock Kerbin has none).</summary>
    public SeasonOrbit WithObliquity(double obliquity)
    {
        var tilted = new SeasonOrbit(Eccentricity, Period) { Obliquity = Math.Abs(obliquity), RealObliquity = RealObliquity };
        tilted.PerihelionLongitude = Math.Sin(RealObliquity) > 1e-6 ? PerihelionLongitude : 0.0;
        return tilted;
    }

    /// <summary>The sun's declination (rad; the latitude it is overhead at) at a true anomaly.</summary>
    public double Declination(double trueAnomaly) =>
        Math.Asin(Math.Max(-1.0, Math.Min(1.0, Math.Sin(Obliquity) * Math.Sin(trueAnomaly + PerihelionLongitude))));

    /// <summary>The true anomaly at a fraction of the way through the year, measured in mean anomaly (time since perihelion / period).</summary>
    public double TrueAnomalyAt(double yearFraction) => Insolation.TrueAnomaly(2.0 * Math.PI * yearFraction, Eccentricity);

    /// <summary>The sun's declination (rad) at <paramref name="yearFraction"/> of the way from perihelion.</summary>
    public double DeclinationAt(double yearFraction) => Declination(TrueAnomalyAt(yearFraction));

    /// <summary>Starlight relative to the mean distance, at a fraction of the year.</summary>
    public double DistanceFactorAt(double yearFraction) => Insolation.DistanceFactor(TrueAnomalyAt(yearFraction), Eccentricity);

    /// <summary>The solar longitude (rad, 0 at the northern spring equinox, 90 degrees at the northern summer solstice).</summary>
    public double SolarLongitudeAt(double yearFraction)
    {
        double l = TrueAnomalyAt(yearFraction) + PerihelionLongitude;
        return l - 2.0 * Math.PI * Math.Floor(l / (2.0 * Math.PI));
    }

    /// <summary>The season of the northern (<paramref name="north"/> true) or southern hemisphere at a fraction of the year.</summary>
    public SeasonName SeasonAt(double yearFraction, bool north)
    {
        if (Math.Sin(Obliquity) < 0.02) return SeasonName.None;
        double l = SolarLongitudeAt(yearFraction);
        int quarter = (int)Math.Floor(l / (0.5 * Math.PI)) & 3;
        if (!north) quarter = (quarter + 2) & 3;
        return (SeasonName)(quarter + 1);
    }

    private static Double3 Unit(Double3 v)
    {
        double l = v.Length;
        return l > 0.0 ? v / l : v;
    }

    private static Double3 Cross(Double3 a, Double3 b) =>
        new Double3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
