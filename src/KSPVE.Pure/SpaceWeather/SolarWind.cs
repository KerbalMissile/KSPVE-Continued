using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.SpaceWeather;

/// <summary>
/// The solar wind at a place and moment: what a planet's magnetosphere meets (and so how strong and how far from the poles
/// its aurorae are). SI units throughout.
/// </summary>
public struct SolarWindState
{
    /// <summary>Radial speed (m/s). Quiet: about 400 km/s; a fast CME: 1000-3000 km/s.</summary>
    public double Speed;
    /// <summary>Proton number density (1/m^3). Quiet at 1 AU: about 5e6.</summary>
    public double Density;
    /// <summary>Interplanetary magnetic field strength (T). Quiet at 1 AU: about 5e-9.</summary>
    public double FieldStrength;
    /// <summary>
    /// The field's component along the star's north (its spin axis, about the system's orbital pole) in tesla: negative is
    /// "southward", which couples into a prograde planet's dipole and drives storms.
    /// </summary>
    public double FieldNorth;
    /// <summary>Energetic particles (solar energetic particle event) relative to quiet times: 1 quiet, 1e3-1e5 in a big event.</summary>
    public double EnergeticParticles;
    /// <summary>0 outside a CME, 1 inside its shocked sheath or magnetic cloud.</summary>
    public double InCme;

    public const double ProtonMass = 1.67262192e-27;

    /// <summary>Ram (dynamic) pressure, rho v^2 (Pa). Quiet at 1 AU: about 1.3e-9 Pa (1.3 nPa).</summary>
    public double DynamicPressure => Density * ProtonMass * Speed * Speed;

    /// <summary>
    /// The solar wind's dawn-to-dusk electric field for a southward field, v Bs (V/m): what powers geomagnetic storms
    /// (the Burton relation's driver). Zero for a northward field.
    /// </summary>
    public double Coupling => FieldNorth < 0.0 ? -FieldNorth * Speed : 0.0;

    /// <summary>
    /// The quiet wind of a Sun-like star at <paramref name="distance"/> metres, scaled so that at
    /// <paramref name="referenceDistance"/> it is the Sun's at 1 AU (KSP calibrates sunlight at the home planet the same way).
    /// Density falls as 1/r^2 at constant speed; the field as 1/r (the Parker spiral's azimuthal field, far out).
    /// </summary>
    public static SolarWindState Quiet(double distance, double referenceDistance)
    {
        double r = Math.Max(distance, 1.0) / Math.Max(referenceDistance, 1.0);
        return new SolarWindState
        {
            Speed = 4.0e5,
            Density = 5.0e6 / (r * r),
            FieldStrength = 5.0e-9 / Math.Max(r, 1e-3),
            FieldNorth = 0.0,
            EnergeticParticles = 1.0,
        };
    }
}

/// <summary>Where the solar wind's state comes from (the star effects feature sets <see cref="SpaceWeather.Source"/>).</summary>
public interface ISolarWindSource
{
    /// <summary>
    /// The wind at <paramref name="position"/> (metres from the star <paramref name="star"/>'s centre, in an inertial frame
    /// with y the star's north) at game time <paramref name="ut"/> (s).
    /// </summary>
    SolarWindState At(string star, Double3 position, double ut);
}

/// <summary>The system's space weather: its current source, or a quiet wind at the home planet's distance without one.</summary>
public static class SpaceWeather
{
    public static ISolarWindSource Source;

    /// <summary>The distance at which every star's wind is the Sun's at 1 AU (the home planet's orbit), m.</summary>
    public static double ReferenceDistance = 1.495978707e11;

    public static SolarWindState At(string star, Double3 position, double ut) =>
        Source != null ? Source.At(star, position, ut) : SolarWindState.Quiet(position.Length, ReferenceDistance);
}
