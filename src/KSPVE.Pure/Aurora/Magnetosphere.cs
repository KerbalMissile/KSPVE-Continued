using System;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;

namespace KSPVE.Pure.Aurora;

/// <summary>
/// A planet's internal field as a tilted, offset dipole (the first approximation for every planet with a dynamo), and the
/// geometry of its field lines: where a line through a point touches the surface, and where the closed lines of a given
/// equatorial distance come down (the invariant latitude).
/// </summary>
public struct Dipole
{
    /// <summary>Field at the magnetic equator on the surface (T). Earth 3.1e-5, Jupiter 4.2e-4 (equator, 14 Gauss), Saturn 2.1e-5.</summary>
    public double EquatorialField;
    /// <summary>Angle between the dipole axis and the spin axis (rad).</summary>
    public double Tilt;
    /// <summary>Longitude (rad, east) of the geomagnetic north pole's tilt, in the body's turning frame.</summary>
    public double TiltLongitude;
    /// <summary>The dipole's centre, in planetary radii, in the body frame (x lat0/lon0, y north, z lon 90E).</summary>
    public Double3 Offset;
    /// <summary>+1: moment parallel to the spin axis (Jupiter, Saturn); -1: antiparallel (Earth), where a southward interplanetary field reconnects.</summary>
    public double Polarity;

    public bool Exists => EquatorialField > 0.0;

    /// <summary>The unit vector along the geomagnetic axis (towards the pole in the spin axis's hemisphere), in the body frame.</summary>
    public Double3 Axis => new Double3(Math.Sin(Tilt) * Math.Cos(TiltLongitude), Math.Cos(Tilt), Math.Sin(Tilt) * Math.Sin(TiltLongitude));

    /// <summary>Equatorial distance (in planetary radii) of the field line landing at invariant latitude <paramref name="latitude"/> (rad): L = 1 / cos^2.</summary>
    public static double LShell(double invariantLatitude)
    {
        double c = Math.Cos(invariantLatitude);
        return 1.0 / Math.Max(c * c, 1e-9);
    }

    /// <summary>Invariant latitude (rad) where the field line of shell <paramref name="l"/> (radii) meets the surface: acos(1 / sqrt(L)).</summary>
    public static double InvariantLatitude(double l) => Math.Acos(Math.Min(1.0, 1.0 / Math.Sqrt(Math.Max(l, 1.0))));

    /// <summary>
    /// Where the dipole field line through a point at distance <paramref name="r"/> (radii) and magnetic colatitude
    /// <paramref name="colatitude"/> meets the surface: the footprint's colatitude. A line is r = L sin^2(theta), so the footprint has
    /// sin^2(theta0) = sin^2(theta) / r. This is exact for a dipole and makes aurora structure follow the field lines.
    /// </summary>
    public static double FootprintColatitude(double r, double colatitude)
    {
        double s = Math.Sin(colatitude);
        double s0 = s * s / Math.Max(r, 1.0);
        return Math.Asin(Math.Sqrt(Math.Min(1.0, Math.Max(s0, 0.0))));
    }

    /// <summary>The field's strength (T) at distance r (radii) and magnetic colatitude: B0 (1/r)^3 sqrt(1 + 3 cos^2(theta)).</summary>
    public double FieldAt(double r, double colatitude)
    {
        double c = Math.Cos(colatitude);
        return EquatorialField / (r * r * r) * Math.Sqrt(1.0 + 3.0 * c * c);
    }
}

/// <summary>
/// The size of a magnetosphere: the distance at which the planet's field pressure balances the solar wind's push.
/// </summary>
public static class Magnetopause
{
    public const double VacuumPermeability = 4.0e-7 * Math.PI;

    /// <summary>
    /// Chapman-Ferraro stand-off distance in planetary radii at the subsolar point. Pressure balance of the wind's dynamic
    /// pressure (times <paramref name="stagnation"/>, 0.88 for a gas-dynamic flow past the nose) against the magnetic pressure of
    /// the dipole compressed by the magnetopause currents, (f B0 / r^3)^2 / 2 mu0, with f = 2 (the field doubles at the surface
    /// of the current sheet): r = (f^2 B0^2 / (2 mu0 k rho v^2))^(1/6). Earth at 1 AU, quiet: about 10.4 radii.
    /// </summary>
    public static double StandOff(double equatorialField, double dynamicPressure, double compression = 2.0, double stagnation = 0.88)
    {
        double pressure = Math.Max(dynamicPressure, 1e-16) * stagnation;
        double r6 = compression * compression * equatorialField * equatorialField / (2.0 * VacuumPermeability * pressure);
        return Math.Pow(Math.Max(r6, 1e-12), 1.0 / 6.0);
    }

    /// <summary>
    /// A southward interplanetary field reconnects with the planet's and erodes the dayside: the Bz dependence of Shue et al.
    /// (1998) r0 = (10.22 + 1.29 tanh(0.184 (Bz + 8.14))) Pd^(-1/6.6), as a factor on the Bz = 0 value. Bz in tesla, positive northward.
    /// </summary>
    public static double Erosion(double bzTesla)
    {
        double bz = bzTesla * 1e9;
        double with = 10.22 + 1.29 * Math.Tanh(0.184 * (bz + 8.14));
        double without = 10.22 + 1.29 * Math.Tanh(0.184 * 8.14);
        return with / without;
    }
}

/// <summary>
/// A Kp-like index of how hard the solar wind drives the magnetosphere, from the Newell et al. (2007) coupling function
/// dPhi/dt = v^(4/3) B_T^(2/3) sin^(8/3)(theta_c / 2), which predicts the energy entering through reconnection better than
/// any single parameter (v in km/s, B_T in nT, theta_c the interplanetary field's clock angle).
/// </summary>
public static class Activity
{
    /// <summary>The coupling function (km/s)^(4/3) nT^(2/3) for a wind and the clock angle of its field (rad; 0 = northward).</summary>
    public static double Newell(double speedMs, double fieldTesla, double clockAngle)
    {
        double v = Math.Max(speedMs, 0.0) / 1000.0, b = Math.Max(fieldTesla, 0.0) * 1e9;
        double s = Math.Sin(0.5 * clockAngle);
        return Math.Pow(v, 4.0 / 3.0) * Math.Pow(b, 2.0 / 3.0) * Math.Pow(Math.Abs(s), 8.0 / 3.0);
    }

    /// <summary>
    /// The coupling of a solar wind state to a planet whose moment has the given polarity (+1 parallel to its star's north,
    /// -1 antiparallel as Earth's): the clock angle comes from the field component along the star's north, the rest of the
    /// field taken as lying in the plane perpendicular to it.
    /// </summary>
    public static double Coupling(SolarWindState wind, double polarity)
    {
        double total = Math.Max(wind.FieldStrength, Math.Abs(wind.FieldNorth));
        // The component that reconnects: southward for an Earth-like moment, northward for the opposite one. The clock angle is
        // measured from the direction that does not couple, so it is 180 degrees when the field reconnects fully.
        double bz = -polarity * wind.FieldNorth;
        double clock = Math.Acos(Math.Max(-1.0, Math.Min(1.0, bz / Math.Max(total, 1e-30))));
        return Newell(wind.Speed, total, clock);
    }

    /// <summary>
    /// Kp from the coupling. Calibrated on typical events: a quiet wind (400 km/s, 5 nT, no Bz) gives about 1.5; 800 km/s and
    /// 30 nT southward gives about 8; Kp is capped at 9 and floored at 0.
    /// </summary>
    public static double KpFromCoupling(double coupling)
    {
        if (!(coupling > 1.0)) return 0.0;
        double kp = -15.8 + 4.9 * Math.Log10(coupling);
        return Math.Max(0.0, Math.Min(9.0, kp));
    }

    /// <summary>
    /// The magnetosphere's response is slow: Kp is a three-hour index and storms build over hours, so the index follows the wind
    /// through an exponential memory of about <paramref name="memorySeconds"/>, sampled at eight earlier moments.
    /// </summary>
    public static double Kp(Func<double, SolarWindState> windAt, double ut, double polarity, double memorySeconds = 5400.0)
    {
        double sum = 0.0, weights = 0.0;
        for (int i = 0; i < 8; i++)
        {
            double age = i * memorySeconds * 0.5;
            double w = Math.Exp(-age / memorySeconds);
            sum += w * KpFromCoupling(Coupling(windAt(ut - age), polarity));
            weights += w;
        }
        return sum / weights;
    }
}

/// <summary>Where the auroral oval lies and how it is set by the solar wind and the activity.</summary>
public struct OvalState
{
    /// <summary>Colatitude (rad) of the oval's centre on the night side (midnight).</summary>
    public double NightColatitude;
    /// <summary>Fraction by which the dayside colatitude is less than the night's (the oval is off-centre, towards the night).</summary>
    public double DayShift;
    /// <summary>Half-width of the oval's active band (rad).</summary>
    public double HalfWidth;
    /// <summary>Magnetopause stand-off in planetary radii, and the Kp-like index.</summary>
    public double StandOff, Kp;

    public double NightLatitudeDegrees => 90.0 - NightColatitude * 180.0 / Math.PI;

    /// <summary>Colatitude (rad) of the oval at magnetic local time angle <paramref name="nu"/> from midnight (0 at midnight, pi at noon).</summary>
    public double ColatitudeAt(double nu) => NightColatitude * (1.0 - DayShift * 0.5 * (1.0 - Math.Cos(nu)));
}

public static class OvalModel
{
    /// <summary>
    /// The night side of the oval maps to the inner edge of the plasma sheet, which lies at about 0.63 of the stand-off
    /// distance at quiet times (6.6 Earth radii for 10.4: the oval at 67 degrees) and moves in by a factor of exp(-0.065 Kp)
    /// as activity rises, to about L of 2.4 (49 degrees) at Kp 8 or 9. The dayside oval sits about a third nearer the pole than the night
    /// (the observed offset of the oval's centre, some 4 degrees anti-sunward: Feldstein and Starkov 1967).
    /// </summary>
    public static OvalState SolarDriven(Dipole field, SolarWindState wind, double kp)
    {
        double standOff = Magnetopause.StandOff(field.EquatorialField, wind.DynamicPressure) * Magnetopause.Erosion(wind.FieldNorth * (field.Polarity >= 0 ? -1.0 : 1.0));
        double l = Math.Max(1.5, 0.634 * standOff * Math.Exp(-0.065 * kp));
        double lat = Dipole.InvariantLatitude(l);
        var state = new OvalState
        {
            NightColatitude = 0.5 * Math.PI - lat,
            DayShift = 0.35,
            HalfWidth = (1.6 + 0.30 * kp) * Math.PI / 180.0,
            StandOff = standOff,
            Kp = kp,
        };
        return state;
    }

    /// <summary>
    /// A gas giant's main oval maps to the region where the magnetosphere stops co-rotating, so it is nearly fixed in
    /// latitude (Jupiter: 16 degrees of colatitude) and is only nudged by the wind's pressure.
    /// </summary>
    public static OvalState Corotating(Dipole field, SolarWindState wind, double colatitudeDegrees, double halfWidthDegrees)
    {
        double standOff = Magnetopause.StandOff(field.EquatorialField, wind.DynamicPressure);
        return new OvalState
        {
            NightColatitude = colatitudeDegrees * Math.PI / 180.0,
            DayShift = 0.08,
            HalfWidth = halfWidthDegrees * Math.PI / 180.0,
            StandOff = standOff,
            Kp = 0.0,
        };
    }
}
