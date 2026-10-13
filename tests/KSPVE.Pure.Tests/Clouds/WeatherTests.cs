using System;
using System.Linq;
using KSPVE.Pure.Clouds;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Clouds;

public class WeatherTests
{
    private const double Deg = Math.PI / 180.0;

    // Kerbin: 600 km, a 6-hour day.
    private static Weather Kerbin(double rotation = 21549.4, ulong seed = 3) => new Weather(Climate.For(600000.0, rotation, seed));

    [Test]
    public void TheSameWeather_EveryTimeItIsWorkedOut()
    {
        Weather a = Kerbin(), b = Kerbin();
        a.Prepare(5e6, 0.3);
        b.Prepare(5e6, 0.3);
        for (double lat = -80; lat <= 80; lat += 20)
            for (double lon = -170; lon <= 170; lon += 40)
            {
                WeatherSample x = a.Sample(lat * Deg, lon * Deg), y = b.Sample(lat * Deg, lon * Deg);
                Assert.That((x.Humidity, x.Storm, x.Precipitation, x.WindEast, x.WindNorth), Is.EqualTo((y.Humidity, y.Storm, y.Precipitation, y.WindEast, y.WindNorth)));
            }
        Weather other = Kerbin(seed: 4);
        other.Prepare(5e6, 0.3);
        Assert.That(other.Systems.Select(s => s.Longitude), Is.Not.EqualTo(a.Systems.Select(s => s.Longitude)), "another seed, other weather");
    }

    [Test]
    public void EveryValue_StaysInRange()
    {
        Weather w = Kerbin();
        foreach (double t in new[] { 0.0, 1e5, 3.3e6, 9e7 })
        {
            w.Prepare(t, 1.0);
            for (double lat = -89; lat <= 89; lat += 7)
                for (double lon = -179; lon <= 179; lon += 13)
                {
                    WeatherSample s = w.Sample(lat * Deg, lon * Deg);
                    Assert.That(s.Humidity, Is.InRange(0.0, 1.0));
                    Assert.That(s.Storm, Is.InRange(0.0, 1.0));
                    Assert.That(s.Precipitation, Is.InRange(0.0, 1.0));
                    Assert.That(double.IsNaN(s.WindEast) || double.IsNaN(s.WindNorth), Is.False);
                }
        }
    }

    [Test]
    public void TheClimate_IsWetAtTheEquator_DryInTheSubtropics_WetInTheStormTracks()
    {
        Weather w = Kerbin();
        double equator = w.ClimateHumidity(0.0), subtropics = w.ClimateHumidity(26 * Deg), stormTracks = w.ClimateHumidity(55 * Deg);
        Assert.That(equator, Is.GreaterThan(subtropics + 0.2));
        Assert.That(stormTracks, Is.GreaterThan(subtropics + 0.1));
        Assert.That(w.ClimateHumidity(-26 * Deg), Is.EqualTo(subtropics), "the same either side of the equator");
    }

    [Test]
    public void ThePrevailingWinds_AreTradesWesterliesAndPolarEasterlies_ReversedOnAReversedPlanet()
    {
        Weather w = Kerbin();
        Assert.That(w.PrevailingWind(15 * Deg), Is.LessThan(0.0), "trade winds from the east");
        Assert.That(w.PrevailingWind(45 * Deg), Is.GreaterThan(0.0), "westerlies");
        Assert.That(w.PrevailingWind(-45 * Deg), Is.GreaterThan(0.0));
        Assert.That(w.PrevailingWind(75 * Deg), Is.LessThan(0.0), "polar easterlies");
        Assert.That(Kerbin(rotation: -21549.4).PrevailingWind(45 * Deg), Is.LessThan(0.0), "a planet turning the other way");
    }

    [Test]
    public void Systems_DriftWithTheWesterlies()
    {
        Weather w = Kerbin();
        const double t0 = 2e6, dt = 600.0;
        w.Prepare(t0, 0.0);
        var before = w.Systems.Where(s => Math.Abs(s.Latitude) > 40 * Deg && Math.Abs(s.Latitude) < 60 * Deg).ToList();
        Assert.That(before, Is.Not.Empty);
        w.Prepare(t0 + dt, 0.0);
        int moved = 0;
        foreach (Weather.WeatherSystem s in before)
        {
            // The same system a little later.
            Weather.WeatherSystem later = w.Systems.First(o => o.Id == s.Id);
            double step = later.Longitude - s.Longitude;
            step -= 2 * Math.PI * Math.Round(step / (2 * Math.PI));
            Assert.That(step, Is.GreaterThan(0.0), "carried east");
            moved++;
        }
        Assert.That(moved, Is.EqualTo(before.Count));
    }

    [Test]
    public void LowsTurnAnticlockwise_InTheNorth_OnAProgradePlanet()
    {
        Weather w = Kerbin();
        w.Prepare(4e6, 0.0);
        Weather.WeatherSystem low = w.Systems.Where(s => s.IsLow && s.Latitude > 35 * Deg && s.Strength > 0.4).OrderByDescending(s => s.Strength).First();
        Weather.WeatherSystem southLow = w.Systems.Where(s => s.IsLow && s.Latitude < -35 * Deg && s.Strength > 0.4).OrderByDescending(s => s.Strength).First();
        Assert.That(low.Turning, Is.EqualTo(1.0));
        Assert.That(southLow.Turning, Is.EqualTo(-1.0));
        // East of a northern low the wind turns north (anticlockwise), less the background wind.
        double east = low.Radius * 0.7 / Math.Cos(low.Latitude);
        WeatherSample eastSide = w.Sample(low.Latitude, low.Longitude + east), westSide = w.Sample(low.Latitude, low.Longitude - east);
        Assert.That(eastSide.WindNorth, Is.GreaterThan(westSide.WindNorth));
    }

    [Test]
    public void NothingPopsInOrOut_BetweenMoments()
    {
        Weather w = Kerbin();
        var grid = new (double lat, double lon)[400];
        var random = new Random(1);
        for (int i = 0; i < grid.Length; i++) grid[i] = ((random.NextDouble() - 0.5) * 170 * Deg, (random.NextDouble() - 0.5) * 358 * Deg);
        double worst = 0.0;
        for (double t = 1e6; t < 1e6 + 6 * 3600; t += 1800)
        {
            w.Prepare(t, 0.0);
            var now = grid.Select(p => w.Sample(p.lat, p.lon)).ToArray();
            w.Prepare(t + 30.0, 0.0);
            for (int i = 0; i < grid.Length; i++)
            {
                WeatherSample later = w.Sample(grid[i].lat, grid[i].lon);
                worst = Math.Max(worst, Math.Abs(later.Humidity - now[i].Humidity));
                worst = Math.Max(worst, Math.Abs(later.Storm - now[i].Storm));
            }
        }
        Assert.That(worst, Is.LessThan(0.05), "half a minute changes the weather by little");
    }

    [Test]
    public void Thunderstorms_FavourTheAfternoon()
    {
        var w = new Weather(new Climate { Radius = 600000.0, RotationPeriod = 21549.4, Storminess = 1.0, Humidity = 0.8, Seed = 9 });
        int afternoon = 0, night = 0;
        for (int i = 0; i < 200; i++)
        {
            double t = 1e6 + i * 7200.0;
            w.Prepare(t, 0.0);   // the sun over longitude 0
            foreach (Weather.StormCell c in w.Storms)
            {
                double hour = c.Longitude / Math.PI * 12.0 + 12.0;
                if (hour > 13 && hour < 18) afternoon++;
                else if (hour < 4 || hour > 23) night++;
            }
        }
        Assert.That(afternoon, Is.GreaterThan(2 * night), $"{afternoon} afternoon storms, {night} at night (equal spans of time)");
    }

    [Test]
    public void TheMap_IsALatitudeLongitudeGrid()
    {
        Weather w = Kerbin();
        w.Prepare(1e6, 0.0);
        var map = new float[4 * 32 * 16];
        w.FillMap(32, 16, map);
        WeatherSample corner = w.Sample(-0.5 * Math.PI + 0.5 * Math.PI / 16, -Math.PI + Math.PI / 32);
        Assert.That(map[0], Is.EqualTo((float)corner.Humidity), "row 0 the south, column 0 at -pi");
        Assert.That(map.All(v => v >= 0f && v <= 1f), Is.True);
    }
}
