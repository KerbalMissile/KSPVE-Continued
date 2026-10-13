using System;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Seasons;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Seasons;

/// <summary>Earth, and a few other worlds, as the models take them.</summary>
internal static class Worlds
{
    public const double Deg = Math.PI / 180.0;

    /// <summary>An orbit with the axis tilted by <paramref name="tilt"/> degrees, the perihelion at solar longitude <paramref name="perihelion"/> (Earth: 283 degrees).</summary>
    public static SeasonOrbit Orbit(double tilt, double eccentricity, double period, double perihelion = 283.0)
    {
        double eps = tilt * Deg, w = perihelion * Deg;
        // sin(dec) = -(A cos nu + B sin nu) = sin(eps) sin(nu + w)
        double a = -Math.Sin(eps) * Math.Sin(w), b = -Math.Sin(eps) * Math.Cos(w);
        var spin = new Double3(a, b, Math.Cos(eps));
        return SeasonOrbit.FromAxis(spin, new Double3(1, 0, 0), new Double3(0, 1, 0), eccentricity, period);
    }

    public static SeasonOrbit EarthOrbit() => Orbit(23.44, 0.0167, SnowParameters.EarthYear);

    public static ClimateParameters Earth() => new ClimateParameters { MeanTemperature = 288.15, SolarDay = 86400.0 };

    public static PlanetClimate EarthClimate() => new PlanetClimate(new EnergyBalance(Earth(), EarthOrbit()), 9.81, null);

    public static SnowClimatology EarthSnow(PlanetClimate climate = null)
    {
        climate ??= EarthClimate();
        var weather = new Weather(KSPVE.Pure.Clouds.Climate.For(6371000.0, 86164.0, 3));
        var snow = new SnowParameters();
        return new SnowClimatology(climate, snow, PrecipitationClimate.From(weather, climate.Orbit, snow));
    }
}

public class InsolationTests
{
    private const double Deg = Math.PI / 180.0;

    [Test]
    public void DailyMean_AtTheEquatorEquinox_IsAQuarterOfPiOfTheSolarConstantsFlux()
    {
        // Q = S / pi at latitude 0 and declination 0.
        Assert.That(Insolation.DailyMean(1361.0, 0.0, 0.0), Is.EqualTo(1361.0 / Math.PI).Within(1e-9));
    }

    [Test]
    public void DailyMean_AtThePoleInMidsummer_IsTheSolarConstantTimesSinDeclination()
    {
        // The sun stays up all day at the pole: Q = S sin(dec) = 1361 * 0.3978 = 541.4.
        double q = Insolation.DailyMean(1361.0, 90.0 * Deg, 23.44 * Deg);
        Assert.That(q, Is.EqualTo(1361.0 * Math.Sin(23.44 * Deg)).Within(0.5));
        Assert.That(q, Is.EqualTo(541.4).Within(0.5));
    }

    [Test]
    public void DailyMean_InPolarNight_IsZero()
    {
        Assert.That(Insolation.DailyMean(1361.0, 80.0 * Deg, -23.44 * Deg), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(Insolation.DailyMean(1361.0, -80.0 * Deg, 23.44 * Deg), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void DailyMean_Averaged_OverTheGlobeAndYear_IsAQuarterOfTheSolarConstant()
    {
        SeasonOrbit orbit = Worlds.Orbit(23.44, 0.0, 1.0);
        double sum = 0.0, weight = 0.0;
        for (int k = 0; k < 360; k++)
            for (int i = 0; i < 180; i++)
            {
                double lat = (-90.0 + (i + 0.5)) * Deg, w = Math.Cos(lat);
                sum += Insolation.DailyMean(1361.0, lat, orbit.DeclinationAt((k + 0.5) / 360.0)) * w;
                weight += w;
            }
        Assert.That(sum / weight, Is.EqualTo(1361.0 / 4.0).Within(1361.0 / 4.0 * 0.003));
    }

    [Test]
    public void TheTropics_GetMoreSunThanThePoles_OverAYear()
    {
        SeasonOrbit orbit = Worlds.EarthOrbit();
        double Annual(double lat)
        {
            double s = 0;
            for (int k = 0; k < 365; k++) s += Insolation.DailyMean(1361.0 * orbit.DistanceFactorAt((k + 0.5) / 365.0), lat * Deg, orbit.DeclinationAt((k + 0.5) / 365.0));
            return s / 365.0;
        }
        Assert.That(Annual(0), Is.EqualTo(417).Within(8), "Earth's equator: about 417 W/m^2 (Hartmann fig. 2.7)");
        Assert.That(Annual(90), Is.EqualTo(176).Within(8), "the pole: about 176 W/m^2");
        Assert.That(Annual(45), Is.GreaterThan(Annual(75)));
    }

    [TestCase(0.0, 0.0)]
    [TestCase(1.0, 0.1)]
    [TestCase(2.5, 0.5)]
    [TestCase(5.0, 0.9)]
    public void Kepler_TheTrueAnomalyFitsTheEccentricAnomaly(double mean, double e)
    {
        double nu = Insolation.TrueAnomaly(mean, e);
        double big = 2.0 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
        double m = big - e * Math.Sin(big);
        double wrapped = mean - 2 * Math.PI * Math.Floor(mean / (2 * Math.PI));
        double diff = Math.Abs(((m - wrapped + Math.PI) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) - Math.PI);
        Assert.That(diff, Is.LessThan(1e-9));
    }

    [Test]
    public void ACircularOrbit_Has_TrueAnomalyEqualToMean_AndNoDistanceChange()
    {
        Assert.That(Insolation.TrueAnomaly(1.234, 0.0), Is.EqualTo(1.234).Within(1e-12));
        Assert.That(Insolation.DistanceFactor(2.0, 0.0), Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void EarthsPerihelionIsAboutSevenPercentMoreSun_ThanItsAphelion()
    {
        // (1 + e)^2 / (1 - e)^2 = 1.069 for e = 0.0167
        double ratio = Insolation.DistanceFactor(0.0, 0.0167) / Insolation.DistanceFactor(Math.PI, 0.0167);
        Assert.That(ratio, Is.EqualTo(1.069).Within(0.002));
    }
}

public class SeasonOrbitTests
{
    private const double Deg = Math.PI / 180.0;

    [Test]
    public void TheAxialTilt_IsRecoveredFromTheVectors()
    {
        SeasonOrbit orbit = Worlds.Orbit(25.2, 0.09, 1e7);
        Assert.That(orbit.RealObliquity / Deg, Is.EqualTo(25.2).Within(1e-6));
        double max = double.MinValue, min = double.MaxValue;
        for (int k = 0; k < 1000; k++)
        {
            double d = orbit.DeclinationAt(k / 1000.0) / Deg;
            max = Math.Max(max, d);
            min = Math.Min(min, d);
        }
        Assert.That(max, Is.EqualTo(25.2).Within(0.2));
        Assert.That(min, Is.EqualTo(-25.2).Within(0.2));
    }

    [Test]
    public void AnUntiltedPlanet_HasNoSeasons_AndAPretendTiltGivesSome()
    {
        SeasonOrbit flat = Worlds.Orbit(0.0, 0.0, 9.2e6);
        for (int k = 0; k < 20; k++) Assert.That(flat.DeclinationAt(k / 20.0), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(flat.SeasonAt(0.3, true), Is.EqualTo(SeasonName.None));
        SeasonOrbit pretend = flat.WithObliquity(23.44 * Deg);
        double max = 0;
        for (int k = 0; k < 100; k++) max = Math.Max(max, pretend.DeclinationAt(k / 100.0));
        Assert.That(max / Deg, Is.EqualTo(23.44).Within(0.3));
        Assert.That(pretend.RealObliquity, Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void TheHemispheres_HaveOppositeSeasons()
    {
        SeasonOrbit orbit = Worlds.EarthOrbit();
        for (int k = 0; k < 40; k++)
        {
            double f = (k + 0.5) / 40.0;
            SeasonName n = orbit.SeasonAt(f, true), s = orbit.SeasonAt(f, false);
            Assert.That(((int)n - 1 + 2) % 4, Is.EqualTo((int)s - 1), "northern summer is southern winter");
        }
    }

    [Test]
    public void ARetrogradeAxis_PastNinetyDegrees_StillGivesTheSunItsRange()
    {
        SeasonOrbit uranus = Worlds.Orbit(97.8, 0.046, 2.65e9);
        Assert.That(uranus.RealObliquity / Deg, Is.EqualTo(97.8).Within(1e-6));
        double max = 0;
        for (int k = 0; k < 400; k++) max = Math.Max(max, uranus.DeclinationAt(k / 400.0));
        Assert.That(max / Deg, Is.EqualTo(82.2).Within(1.0), "sin(97.8) = sin(82.2)");
    }

    [Test]
    public void TheSolarLongitude_RunsRoundOnceAYear()
    {
        SeasonOrbit orbit = Worlds.EarthOrbit();
        double previous = orbit.SolarLongitudeAt(0.0);
        double travelled = 0.0;
        for (int k = 1; k <= 400; k++)
        {
            double l = orbit.SolarLongitudeAt(k / 400.0);
            double step = l - previous;
            if (step < -Math.PI) step += 2 * Math.PI;
            Assert.That(step, Is.GreaterThan(0));
            travelled += step;
            previous = l;
        }
        Assert.That(travelled, Is.EqualTo(2 * Math.PI).Within(1e-6));
    }
}

public class EnergyBalanceTests
{
    private const double Deg = Math.PI / 180.0;

    [Test]
    public void EarthLike_HoldsTheGivenGlobalMean_AndHasAnEarthLikeBandShape()
    {
        var model = new EnergyBalance(Worlds.Earth(), Worlds.EarthOrbit());
        Assert.That(model.GlobalMean, Is.EqualTo(288.15).Within(0.3));
        double Annual(double lat)
        {
            double s = 0;
            for (int k = 0; k < 48; k++) s += model.Temperature(lat * Deg, (k + 0.5) / 48.0);
            return s / 48.0 - 273.15;
        }
        Assert.That(Annual(0), Is.InRange(22.0, 31.0), "the equator band: about 26 C");
        Assert.That(Annual(45), Is.InRange(5.0, 15.0), "mid latitudes: about 10 C");
        Assert.That(Annual(85), Is.InRange(-45.0, -12.0), "the pole: well below freezing");
        Assert.That(Annual(0) - Annual(85), Is.InRange(35.0, 65.0), "equator to pole contrast");
        Assert.That(Annual(10), Is.GreaterThan(Annual(40)));
        Assert.That(Annual(40), Is.GreaterThan(Annual(70)));
    }

    [Test]
    public void TheSeasons_AreOppositeInTheTwoHemispheres()
    {
        var model = new EnergyBalance(Worlds.Earth(), Worlds.EarthOrbit());
        // Find the warmest and coldest times of the year at 50 N and 50 S.
        double WarmestOf(double lat)
        {
            double best = -1, at = 0;
            for (int k = 0; k < 192; k++)
            {
                double t = model.Temperature(lat * Deg, (k + 0.5) / 192.0);
                if (t > best) { best = t; at = (k + 0.5) / 192.0; }
            }
            return at;
        }
        double north = WarmestOf(50), south = WarmestOf(-50);
        double apart = Math.Abs(north - south);
        if (apart > 0.5) apart = 1.0 - apart;
        Assert.That(apart, Is.EqualTo(0.5).Within(0.07), "summers half a year apart");
        // And the warmest time of the north is about a month after its solstice (solar longitude 90): fraction of year.
        double lambda = Worlds.EarthOrbit().SolarLongitudeAt(north) / Deg;
        Assert.That(lambda, Is.InRange(100.0, 170.0), "the temperature lags the sun by one to two and a half months (an ocean world: the long end)");
    }

    [Test]
    public void TheNorthernMidLatitudes_SwingMoreThanTheSouthern_WhenMoreOfThemIsLand()
    {
        var land = new double[EnergyBalance.Bands];
        for (int i = 0; i < land.Length; i++) land[i] = EnergyBalance.Latitude(i) > 0 ? 0.4 : 0.95;
        var parameters = Worlds.Earth();
        parameters.OceanFraction = land;
        var model = new EnergyBalance(parameters, Worlds.EarthOrbit());
        double Range(double lat)
        {
            double lo = 1e9, hi = -1e9;
            for (int k = 0; k < 96; k++)
            {
                double t = model.Temperature(lat * Deg, (k + 0.5) / 96.0);
                lo = Math.Min(lo, t);
                hi = Math.Max(hi, t);
            }
            return hi - lo;
        }
        Assert.That(Range(50), Is.GreaterThan(1.5 * Range(-50)));
        Assert.That(Range(50), Is.InRange(8.0, 40.0), "a land hemisphere's yearly range at 50 degrees: ~ 25 K");
    }

    [Test]
    public void AnUntiltedCircularPlanet_HasTheSameClimateAllYear()
    {
        var model = new EnergyBalance(Worlds.Earth(), Worlds.Orbit(0.0, 0.0, 3e7));
        for (double lat = -80; lat <= 80; lat += 20)
            Assert.That(model.Temperature(lat * Deg, 0.1), Is.EqualTo(model.Temperature(lat * Deg, 0.6)).Within(0.01));
    }

    [Test]
    public void TheSameWorld_AlwaysGivesTheSameTable()
    {
        var a = new EnergyBalance(Worlds.Earth(), Worlds.EarthOrbit());
        var b = new EnergyBalance(Worlds.Earth(), Worlds.EarthOrbit());
        for (double lat = -85; lat <= 85; lat += 17)
            for (double f = 0.0; f < 1.0; f += 0.13)
                Assert.That(a.Temperature(lat * Deg, f), Is.EqualTo(b.Temperature(lat * Deg, f)));
        Assert.That(a.Opacity, Is.EqualTo(b.Opacity));
    }

    [Test]
    public void AHotterTarget_NeedsMoreGreenhouse_AndTheMeanFollowsIt()
    {
        var cold = Worlds.Earth();
        cold.MeanTemperature = 260.0;
        var warm = Worlds.Earth();
        warm.MeanTemperature = 300.0;
        var c = new EnergyBalance(cold, Worlds.EarthOrbit());
        var w = new EnergyBalance(warm, Worlds.EarthOrbit());
        Assert.That(w.Opacity, Is.GreaterThan(c.Opacity));
        Assert.That(c.GlobalMean, Is.EqualTo(260.0).Within(0.5));
        Assert.That(w.GlobalMean, Is.EqualTo(300.0).Within(0.5));
    }

    [Test]
    public void ThePolesWarm_ByTheHeatTheAtmosphereCarries()
    {
        var thin = Worlds.Earth();
        thin.PressureAtm = 0.1;
        var thick = Worlds.Earth();
        thick.PressureAtm = 4.0;
        var a = new EnergyBalance(thin, Worlds.EarthOrbit());
        var b = new EnergyBalance(thick, Worlds.EarthOrbit());
        double ContrastOf(EnergyBalance m) => m.Temperature(0.0, 0.25) - m.Temperature(80 * Deg, 0.25);
        Assert.That(ContrastOf(b), Is.LessThan(ContrastOf(a)));
    }


    [TestCase(700.0, 5.0, 0.7, 2600.0)]
    [TestCase(210.0, 0.07, 0.3, 585.0)]
    public void OtherWorlds_HoldTheirGivenMean_AndAreFinite(double mean, double pressure, double gravity, double flux)
    {
        var p = new ClimateParameters { MeanTemperature = mean, PressureAtm = pressure, GravityRatio = gravity, SolarConstant = flux, OceanShare = 0.0 };
        var model = new EnergyBalance(p, Worlds.Orbit(25.0, 0.09, 5.9e7));
        Assert.That(model.GlobalMean, Is.EqualTo(mean).Within(2.0));
        for (double lat = -85; lat <= 85; lat += 10)
            for (double f = 0; f < 1; f += 0.1)
            {
                double t = model.Temperature(lat * Deg, f);
                Assert.That(double.IsNaN(t) || double.IsInfinity(t), Is.False);
                Assert.That(t, Is.GreaterThan(3.0));
            }
    }

    [Test]
    public void AirlessMoon_HasTheRadiativeEquilibriumTemperature()
    {
        var p = new ClimateParameters { PressureAtm = 0.0, Albedo = 0.12, IceFeedback = false, OceanShare = 0.0, SolarDay = 2.36e6, GravityRatio = 0.165 };
        var model = new EnergyBalance(p, Worlds.Orbit(1.5, 0.0, 3e7));
        // Equator, daily mean: (S (1 - a) / pi / sigma)^(1/4) is the cos-weighted figure; a rough 270 K.
        double t = model.Temperature(0.0, 0.25);
        Assert.That(t, Is.InRange(250.0, 290.0));
        Assert.That(model.Temperature(80 * Deg, 0.0), Is.LessThan(t - 40.0));
    }
}

public class PlanetClimateTests
{
    private const double Deg = Math.PI / 180.0;

    [Test]
    public void TheAirCools_AtTheMoistLapseRate_UpToTheTropopause()
    {
        PlanetClimate c = Worlds.EarthClimate();
        double t0 = c.Temperature(0.0, 0.3, 0.0), t5 = c.Temperature(0.0, 0.3, 5000.0);
        Assert.That(t0 - t5, Is.EqualTo(5.0 * 6.4).Within(1.0));
        Assert.That(c.LapseRate * 1000.0, Is.EqualTo(6.35).Within(0.2));
        Assert.That(c.Temperature(0.0, 0.3, 30000.0), Is.GreaterThanOrEqualTo(Math.Min(c.SkinTemperature, t0) - 1e-9));
        Assert.That(c.SkinTemperature, Is.InRange(190.0, 235.0), "Earth's tropopause is at about 217 K");
    }

    [Test]
    public void TheGamesOwnProfile_IsFollowed_WhenGiven()
    {
        var profile = new AltitudeProfile(new[] { 0.0, 10000.0, 20000.0 }, new[] { 300.0, 250.0, 240.0 });
        var model = new EnergyBalance(Worlds.Earth(), Worlds.EarthOrbit());
        var c = new PlanetClimate(model, 9.81, profile);
        double t0 = c.Temperature(0.0, 0.3, 0.0);
        Assert.That(c.Temperature(0.0, 0.3, 10000.0), Is.EqualTo(t0 - 50.0).Within(1e-9));
        Assert.That(c.Temperature(0.0, 0.3, 15000.0), Is.EqualTo(t0 - 55.0).Within(1e-9));
    }

    [Test]
    public void TheFreezingLevel_IsHighInTheTropics_AndAtTheGroundNearThePole()
    {
        PlanetClimate c = Worlds.EarthClimate();
        double tropics = c.FreezingAltitude(0.0, 0.5);
        Assert.That(tropics, Is.InRange(3500.0, 6500.0), "the tropical freezing level is at about 4.5 to 5 km");
        Assert.That(c.FreezingAltitude(85 * Deg, 0.5), Is.LessThan(0.0).Or.LessThan(500.0));
    }

    [Test]
    public void TheDay_IsWarmestInTheAfternoon_AndSwingsAFewKelvinOverLand()
    {
        PlanetClimate c = Worlds.EarthClimate();
        double mean = c.Temperature(20 * Deg, 0.25, 0.0);
        double best = -1e9, at = 0, worst = 1e9;
        for (int k = 0; k < 96; k++)
        {
            double h = -Math.PI + 2 * Math.PI * (k + 0.5) / 96.0;
            double t = c.Temperature(20 * Deg, 0.25, 0.0, h);
            if (t > best) { best = t; at = h; }
            worst = Math.Min(worst, t);
        }
        Assert.That(at / Math.PI * 12.0, Is.InRange(0.5, 4.5), "the peak is 1 to 4 hours after local noon");
        Assert.That(best - worst, Is.InRange(2.0, 20.0));
        Assert.That(0.5 * (best + worst), Is.EqualTo(mean).Within(2.0));
    }

    [Test]
    public void ASlowAirlessWorld_HasAHugeSwing()
    {
        var p = new ClimateParameters { PressureAtm = 0.0, Albedo = 0.12, IceFeedback = false, OceanShare = 0.0, SolarDay = 2.36e6 };
        var c = new PlanetClimate(new EnergyBalance(p, Worlds.Orbit(1.5, 0.0, 3e7)), 1.62, null);
        double day = c.Temperature(0.0, 0.25, 0.0, 0.0), night = c.Temperature(0.0, 0.25, 0.0, Math.PI);
        Assert.That(day - night, Is.GreaterThan(150.0), "the Moon's equator: 390 K by day, 95 K by night");
    }
}

public class SnowModelTests
{
    private const double Deg = Math.PI / 180.0;

    private static double Swe(SnowClimatology s, double lat, double year, double altitude)
    {
        var profile = new float[s.AltitudeBands];
        s.Profile(lat * Deg, year, profile);
        return s.Interpolate(profile, altitude);
    }

    [Test]
    public void SnowFalls_BelowFreezing_AndRainAbove()
    {
        Assert.That(SnowClimatology.SnowFraction(-5.0), Is.EqualTo(1.0));
        Assert.That(SnowClimatology.SnowFraction(1.0), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(SnowClimatology.SnowFraction(4.0), Is.EqualTo(0.0));
    }

    [Test]
    public void SnowAccumulatesInWinter_AndMeltsInSpring_AtMidLatitudes()
    {
        SnowClimatology s = Worlds.EarthSnow();
        // At 60 N: it deepens through the winter to a peak in spring, then melts away by late summer.
        double peak = 0.0, peakAt = 0.0;
        for (int k = 0; k < 96; k++)
        {
            double y = (k + 0.5) / 96.0, d = Swe(s, 60, y, 0.0);
            if (d > peak) { peak = d; peakAt = y; }
        }
        Assert.That(peak, Is.GreaterThan(0.05));
        Assert.That(peakAt, Is.InRange(0.1, 0.5), "the deepest snow between February and June");
        Assert.That(Swe(s, 60, 0.97, 0.0), Is.GreaterThan(Swe(s, 60, 0.87, 0.0)), "it is still piling up in December");
        Assert.That(Swe(s, 60, peakAt + 0.12, 0.0), Is.LessThan(peak), "melting after the peak");
        Assert.That(Swe(s, 60, 0.7, 0.0), Is.LessThan(0.005), "gone by September");
    }

    [Test]
    public void TheSouthernHemisphere_IsSnowyInItsOwnWinter()
    {
        SnowClimatology s = Worlds.EarthSnow();
        double north = Swe(s, 55, 0.2, 0.0), southAtThatTime = Swe(s, -55, 0.2, 0.0);
        double southWinter = Swe(s, -55, 0.7, 0.0);
        Assert.That(south(southWinter, southAtThatTime), Is.True);
        Assert.That(north, Is.GreaterThan(0.0));
        static bool south(double winter, double summer) => winter > summer;
    }

    [Test]
    public void NoSnowAtTheEquator_ButAMountainTopHasSome()
    {
        SnowClimatology s = Worlds.EarthSnow();
        Assert.That(Swe(s, 0, 0.3, 0.0), Is.LessThan(0.001));
        Assert.That(Swe(s, 0, 0.3, 2500.0), Is.LessThan(0.01));
        var profile = new float[s.AltitudeBands];
        s.Profile(0.0, 0.3, profile);
        double line = s.LineOf(profile, s.Parameters.LineDepth);
        Assert.That(line, Is.InRange(3500.0, 7500.0), "the tropical snow line (Earth's is at about 4.7 to 5.5 km)");
    }

    [Test]
    public void TheSnowLine_FallsTowardThePoles()
    {
        SnowClimatology s = Worlds.EarthSnow();
        double Line(double lat)
        {
            var profile = new float[s.AltitudeBands];
            s.Profile(lat * Deg, 0.55, profile);
            return s.LineOf(profile, s.Parameters.LineDepth);
        }
        Assert.That(Line(30), Is.GreaterThan(Line(50)));
        Assert.That(Line(50), Is.GreaterThan(Line(70)));
        Assert.That(Line(85), Is.LessThan(600.0), "snow almost to the sea at the pole even in summer");
    }

    [Test]
    public void IceCaps_StayAtThePole_AndNotInTheTemperateZone()
    {
        SnowClimatology s = Worlds.EarthSnow();
        var polar = new float[s.AltitudeBands];
        var temperate = new float[s.AltitudeBands];
        s.IceProfile(88 * Deg, polar);
        s.IceProfile(40 * Deg, temperate);
        Assert.That(polar[0], Is.GreaterThan(0.8), "an ice cap on the pole");
        Assert.That(temperate[0], Is.LessThan(0.05));
        Assert.That(temperate[s.AltitudeBands - 1], Is.GreaterThan(0.5), "glaciers high in the mountains");
    }

    [Test]
    public void ADryWorld_HasLittleSnow()
    {
        PlanetClimate c = Worlds.EarthClimate();
        var weather = new Weather(KSPVE.Pure.Clouds.Climate.For(6371000.0, 86164.0, 3));
        var dry = new SnowParameters { Moisture = 0.02 };
        var s = new SnowClimatology(c, dry, PrecipitationClimate.From(weather, c.Orbit, dry));
        Assert.That(Swe(s, 60, 0.2, 0.0), Is.LessThan(Swe(Worlds.EarthSnow(c), 60, 0.2, 0.0) * 0.2));
    }

    [Test]
    public void Wet_AndDry_BeltsOfTheWeather_GiveTheRainfallOfEarth()
    {
        PlanetClimate c = Worlds.EarthClimate();
        var weather = new Weather(KSPVE.Pure.Clouds.Climate.For(6371000.0, 86164.0, 3));
        var p = new SnowParameters();
        var rain = PrecipitationClimate.From(weather, c.Orbit, p);
        double Annual(double lat) { double s = 0; for (int k = 0; k < 24; k++) s += rain(lat * Deg, (k + 0.5) / 24.0); return s / 24.0 * SnowParameters.EarthYear; }
        Assert.That(Annual(5), Is.InRange(0.8, 2.4), "the wet tropics: 1 to 2 m a year on average over the belt's swing");
        Assert.That(Annual(25), Is.LessThan(Annual(5) * 0.5), "dry subtropics");
        Assert.That(Annual(88), Is.LessThan(0.4), "polar desert");
    }

    [Test]
    public void TheSnowField_IsSnowyAtThePole_BareAtTheEquator_AndFreezesTheSea()
    {
        SnowClimatology s = Worlds.EarthSnow();
        var field = new SnowField(s, null, true, 72, 36);
        field.Update(0.2, null);
        float At(KSPVE.Pure.Textures.GreyMap m, double lat) => (float)m.Sample(0.5, (lat + 90.0) / 180.0);
        Assert.That(At(field.Snow, 0), Is.LessThan(0.01));
        Assert.That(At(field.SeaIce, 0), Is.EqualTo(0.0));
        // Flat world at sea level: it is all ocean, so the pole's sea freezes and carries no land snow.
        Assert.That(At(field.SeaIce, 85), Is.GreaterThan(0.9));
        field.Update(0.7, null);
        Assert.That(At(field.SeaIce, -85), Is.GreaterThan(0.9), "the southern winter");
    }

    [Test]
    public void TheSnowField_OnLand_FollowsTheSeasons()
    {
        SnowClimatology s = Worlds.EarthSnow();
        var land = new KSPVE.Pure.Textures.GreyMap(2, 2, new[] { 100f, 100f, 100f, 100f }, false);
        var field = new SnowField(s, land, true, 72, 36);
        float Cover(double year, double lat) { field.Update(year, null); return (float)field.Snow.Sample(0.5, (lat + 90.0) / 180.0); }
        float winter = Cover(0.2, 55), summer = Cover(0.7, 55);
        Assert.That(winter, Is.GreaterThan(0.5));
        Assert.That(summer, Is.LessThan(0.05));
        Assert.That(Cover(0.7, -55), Is.GreaterThan(Cover(0.2, -55)), "southern winter is northern summer");
    }

    [Test]
    public void FreshSnowfall_ExtendsTheSnowDownTheMountain()
    {
        SnowClimatology s = Worlds.EarthSnow();
        var land = new KSPVE.Pure.Textures.GreyMap(2, 2, new[] { 100f, 100f, 100f, 100f }, false);
        var field = new SnowField(s, land, false, 72, 36);
        field.Update(0.45, null);
        float before = (float)field.Snow.Sample(0.5, (45.0 + 90.0) / 180.0);
        var fresh = new KSPVE.Pure.Textures.GreyMap(2, 2, new[] { 0.05f, 0.05f, 0.05f, 0.05f }, false);
        field.Update(0.45, fresh);
        float after = (float)field.Snow.Sample(0.5, (45.0 + 90.0) / 180.0);
        Assert.That(after, Is.GreaterThanOrEqualTo(before));
    }


    [Test]
    public void RecentSnowfall_IsDeterministic_AndNeverNegative()
    {
        KSPVE.Pure.Textures.GreyMap Run()
        {
            var weather = new Weather(KSPVE.Pure.Clouds.Climate.For(6371000.0, 86164.0, 3));
            var recent = new RecentPrecipitation(weather, 32, 16, 4);
            recent.Begin(5e6, 8 * 86400.0, 1.0, t => 0.3 - 2 * Math.PI * (t - 5e6) / 86400.0, t => 0.1);
            int guard = 0;
            while (!recent.Step(100) && guard++ < 10000) { }
            Assert.That(recent.Busy, Is.False);
            return recent.Map;
        }
        var a = Run();
        var b = Run();
        double total = 0.0;
        for (int i = 0; i < a.Values.Length; i++)
        {
            Assert.That(a.Values[i], Is.GreaterThanOrEqualTo(0f));
            Assert.That(a.Values[i], Is.EqualTo(b.Values[i]));
            total += a.Values[i];
        }
        Assert.That(total, Is.GreaterThan(0.0), "it snowed somewhere");
    }

    [Test]
    public void TheSnowClimatology_IsDeterministic()
    {
        SnowClimatology a = Worlds.EarthSnow(), b = Worlds.EarthSnow();
        for (double lat = -80; lat <= 80; lat += 20)
            for (double f = 0; f < 1; f += 0.2)
                Assert.That(Swe(a, lat, f, 1000.0), Is.EqualTo(Swe(b, lat, f, 1000.0)));
    }
}
