using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Seasons;

/// <summary>
/// How the air's temperature changes with height, taken from a table (the game's own atmosphere temperature curve:
/// (altitude, temperature - temperature at the surface) pairs, piecewise linear, constant beyond the ends).
/// </summary>
public sealed class AltitudeProfile
{
    private readonly double[] _altitude, _offset;

    public AltitudeProfile(IList<double> altitude, IList<double> temperature)
    {
        if (altitude.Count != temperature.Count || altitude.Count < 2) throw new ArgumentException("a profile needs at least two points");
        _altitude = new double[altitude.Count];
        _offset = new double[altitude.Count];
        for (int i = 0; i < _altitude.Length; i++)
        {
            _altitude[i] = altitude[i];
            _offset[i] = temperature[i] - temperature[0];
        }
    }

    /// <summary>The change of temperature (K) from the surface at <paramref name="altitude"/> (m).</summary>
    public double Offset(double altitude)
    {
        int n = _altitude.Length;
        if (altitude <= _altitude[0]) return _offset[0] + (altitude - _altitude[0]) * Slope(0);
        if (altitude >= _altitude[n - 1]) return _offset[n - 1];
        int i = 0;
        while (i < n - 2 && altitude > _altitude[i + 1]) i++;
        double f = (altitude - _altitude[i]) / (_altitude[i + 1] - _altitude[i]);
        return _offset[i] + (_offset[i + 1] - _offset[i]) * f;
    }

    private double Slope(int i) => (_offset[i + 1] - _offset[i]) / Math.Max(_altitude[i + 1] - _altitude[i], 1e-6);
}

/// <summary>
/// A planet's temperatures: the energy balance model's zonal mean at sea level through the year, the way the air cools with
/// height, and the day's swing about the mean. Where the game has its own atmosphere temperature curve it is used for the
/// height dependence (so the two agree on the profile); otherwise the environmental lapse rate of a moist atmosphere,
/// 0.65 g / cp (6.4 K/km on Earth), down to the skin temperature (the tropopause: (OLR / 2 sigma)^(1/4), 214 K for Earth).
/// </summary>
public sealed class PlanetClimate
{
    private readonly EnergyBalance _balance;
    private readonly AltitudeProfile _profile;
    private readonly double _gravity, _heatCapacityGas;
    private readonly double _skin;
    private readonly double _landAdmittance;
    private readonly bool _atmosphere;

    /// <param name="gravity">Surface gravity (m/s^2).</param>
    /// <param name="profile">The game's temperature against altitude, or null.</param>
    /// <param name="heatCapacityGas">The air's specific heat at constant pressure (J/kg/K; 1004 for Earth air).</param>
    public PlanetClimate(EnergyBalance balance, double gravity, AltitudeProfile profile, double heatCapacityGas = 1004.0)
    {
        _balance = balance;
        _gravity = gravity;
        _profile = profile;
        _heatCapacityGas = heatCapacityGas;
        _atmosphere = balance.Parameters.PressureAtm >= 1e-3;
        _skin = Math.Pow(balance.MeanOutgoing / (2.0 * EnergyBalance.StefanBoltzmann), 0.25);
        // Thermal inertia (J/m^2/K/s^0.5): dry soil 1500, the mixed ocean about 15000, airless regolith 60 (the Moon's).
        double ocean = 0.0;
        for (int i = 0; i < EnergyBalance.Bands; i++) ocean += balance.OceanShareOf(i) * Math.Cos(EnergyBalance.Latitude(i));
        double weight = 0.0;
        for (int i = 0; i < EnergyBalance.Bands; i++) weight += Math.Cos(EnergyBalance.Latitude(i));
        ocean /= weight;
        _landAdmittance = _atmosphere ? (1.0 - ocean) * 1500.0 + ocean * 15000.0 : 60.0;
    }

    public EnergyBalance Balance => _balance;
    public SeasonOrbit Orbit => _balance.Orbit;

    /// <summary>The moist environmental lapse rate (K/m).</summary>
    public double LapseRate => _atmosphere ? 0.65 * _gravity / _heatCapacityGas : 0.0;

    /// <summary>The temperature at the top of the troposphere (K), below which the air only cools with height.</summary>
    public double SkinTemperature => _skin;

    /// <summary>The zonal-mean air temperature (K) at sea level.</summary>
    public double SeaLevel(double latitude, double yearFraction) => _balance.Temperature(latitude, yearFraction);

    /// <summary>The day's mean air temperature (K) at a latitude, height above sea level (m) and time of the year.</summary>
    public double Temperature(double latitude, double yearFraction, double altitude)
    {
        double t0 = SeaLevel(latitude, yearFraction);
        if (!_atmosphere) return t0;
        double offset = _profile != null ? _profile.Offset(altitude) : -LapseRate * Math.Max(altitude, 0.0);
        double t = t0 + offset;
        // Not colder than the skin temperature (only for the physical profile: the game's table has its own top).
        return _profile != null ? t : Math.Max(t, Math.Min(_skin, t0));
    }

    /// <summary>The height (m) at which the day's mean air temperature is 0 C: the freezing level (negative: below the sea; huge: never freezes).</summary>
    public double FreezingAltitude(double latitude, double yearFraction, double top = 30000.0)
    {
        const double Freezing = 273.15;
        double t0 = SeaLevel(latitude, yearFraction);
        if (t0 <= Freezing) return -1e5;
        if (!_atmosphere) return 1e9;
        double lo = 0.0, hi = top;
        if (Temperature(latitude, yearFraction, hi) > Freezing) return 1e9;
        for (int i = 0; i < 30; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Temperature(latitude, yearFraction, mid) > Freezing) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>
    /// The air temperature (K) at the hour <paramref name="hourAngle"/> (rad, 0 at local noon, +pi at midnight): the day's
    /// mean plus the first harmonics of the day's heating, each answered by a slab of the ground and the air's mixed layer
    /// (admittance Y = h + sqrt(i k w) I + i k w C: radiation and convection, the soil's thermal inertia I, the heat capacity
    /// C of 500 m of air), linearised about the mean. On a slowly turning world with no air the swing is the radiative one
    /// (the Moon's equator: about 150 K either side of 240 K).
    /// </summary>
    public double Temperature(double latitude, double yearFraction, double altitude, double hourAngle)
    {
        double mean = Temperature(latitude, yearFraction, altitude);
        return Math.Max(mean + DiurnalOffset(latitude, yearFraction, mean, hourAngle), 2.7);
    }

    /// <summary>The day's swing about the mean at an hour angle (K).</summary>
    public double DiurnalOffset(double latitude, double yearFraction, double mean, double hourAngle)
    {
        var p = _balance.Parameters;
        double declination = _balance.Orbit.DeclinationAt(yearFraction);
        double flux = p.SolarConstant * _balance.Orbit.DistanceFactorAt(yearFraction);
        double omega = 2.0 * Math.PI / Math.Max(p.SolarDay, 1.0);
        double albedo = p.Albedo;
        const int Samples = 48;
        double radiative = 4.0 * EnergyBalance.StefanBoltzmann * mean * mean * mean / (1.0 + 0.75 * _balance.Opacity);
        double convective = _atmosphere ? 10.0 * Math.Sqrt(p.PressureAtm) : 0.0;
        double air = _atmosphere ? p.PressureAtm * 1.2 * 288.0 / Math.Max(mean, 50.0) * _heatCapacityGas * 500.0 : 0.0;
        double result = 0.0;
        for (int k = 1; k <= 3; k++)
        {
            // The k-th Fourier harmonic of the absorbed flux over the day.
            double re = 0.0, im = 0.0;
            for (int s = 0; s < Samples; s++)
            {
                double h = 2.0 * Math.PI * (s + 0.5) / Samples - Math.PI;
                double q = Insolation.Instant(flux, latitude, declination, h) * (1.0 - albedo);
                re += q * Math.Cos(k * h);
                im += q * Math.Sin(k * h);
            }
            re *= 2.0 / Samples;
            im *= 2.0 / Samples;
            double kw = k * omega;
            double root = Math.Sqrt(0.5 * kw);
            double yr = radiative + convective + root * _landAdmittance;
            double yi = root * _landAdmittance + kw * air;
            double norm = yr * yr + yi * yi;
            // The forcing is Re[(re - i im) e^{i k h}]; the response Re[(re - i im) / Y e^{i k h}], Y = yr + i yi (the peak comes after noon).
            double a = (re * yr - im * yi) / norm, b = (re * yi + im * yr) / norm;
            result += a * Math.Cos(k * hourAngle) + b * Math.Sin(k * hourAngle);
        }
        return result;
    }
}
