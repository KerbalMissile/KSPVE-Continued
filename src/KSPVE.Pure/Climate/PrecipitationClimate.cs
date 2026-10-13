using System;
using KSPVE.Pure.Clouds;

namespace KSPVE.Pure.Seasons;

/// <summary>
/// The yearly mean precipitation by latitude and season, from the weather model's own climate belts
/// (<see cref="Weather.ClimateHumidity"/>, which follow the sun through the year): the wet equator and storm tracks, the
/// dry subtropics and poles. Humidity above 0.2 gives water as the square of its excess, scaled so the wettest belt gets
/// about 2.2 m a year (Earth's tropics, GPCP) and the poles about 0.15 m (Earth's polar deserts).
/// </summary>
public static class PrecipitationClimate
{
    /// <summary>Water falling at sea level (m/s) at a latitude and fraction of the year, tabulated once.</summary>
    public static Func<double, double, double> From(Weather weather, SeasonOrbit orbit, SnowParameters parameters)
    {
        const int Phases = SnowClimatology.Phases, Lats = SnowClimatology.Latitudes;
        var table = new double[Phases * Lats];
        for (int k = 0; k < Phases; k++)
        {
            weather.Prepare(0.0, 0.0, orbit.DeclinationAt((k + 0.5) / Phases));
            for (int i = 0; i < Lats; i++)
            {
                double h = weather.ClimateHumidity(SnowClimatology.Latitude(i));
                double t = Math.Min(Math.Max((h - 0.2) / 0.65, 0.0), 1.0);
                table[k * Lats + i] = parameters.WettestAnnual * t * t * parameters.Moisture / SnowParameters.EarthYear;
            }
        }
        return (lat, year) =>
        {
            double s = year - Math.Floor(year);
            double x = s * Phases - 0.5;
            int k0 = (int)Math.Floor(x);
            double fk = x - k0;
            int k1 = ((k0 + 1) % Phases + Phases) % Phases;
            k0 = (k0 % Phases + Phases) % Phases;
            double y = (lat + 0.5 * Math.PI) / Math.PI * Lats - 0.5;
            int l0 = (int)Math.Floor(y);
            double fl = y - l0;
            int l1 = Math.Min(Math.Max(l0 + 1, 0), Lats - 1);
            l0 = Math.Min(Math.Max(l0, 0), Lats - 1);
            double a = table[k0 * Lats + l0] + (table[k0 * Lats + l1] - table[k0 * Lats + l0]) * fl;
            double b = table[k1 * Lats + l0] + (table[k1 * Lats + l1] - table[k1 * Lats + l0]) * fl;
            return a + (b - a) * fk;
        };
    }
}
