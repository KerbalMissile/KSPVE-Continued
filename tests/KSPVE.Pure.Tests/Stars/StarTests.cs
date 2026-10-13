using System;
using System.Linq;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using KSPVE.Pure.Stars;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Stars;

public class StarTests
{
    private const double Deg = Math.PI / 180.0, Au = 1.495978707e11, Day = 86400.0;

    [SetUp, TearDown]
    public void ResetStatics()
    {
        SpaceWeather.SpaceWeather.Source = null;
        SpaceWeather.SpaceWeather.ReferenceDistance = Au;
    }

    private static StarProfile Sun(ulong seed = 1, double phase = 0.30) => new StarProfile { Body = "Sun", Seed = seed, CyclePhase = phase };

    /// <summary>A time (s) in the cycle at the given phase of the cycle after the first.</summary>
    private static double At(StarProfile p, double phase, int cycle = 3) => (cycle + phase - p.CyclePhase) * p.CyclePeriod;

    // ---------------------------------------------------------------- determinism

    [Test]
    public void TheSameStar_HasTheSameActivity_EveryTimeItIsAskedFor()
    {
        var a = new StarActivity(Sun());
        var b = new StarActivity(Sun());
        double t = At(a.Profile, 0.36);
        Assert.That(a.RegionsAt(t).Select(r => (r.Id, r.Latitude, r.AreaMsh)), Is.EqualTo(b.RegionsAt(t).Select(r => (r.Id, r.Latitude, r.AreaMsh))));
        Assert.That(a.RegionsAt(t).Count, Is.GreaterThan(3));
        Assert.That(a.CmesAt(t, 30 * Day).Select(c => (c.Id, c.LaunchTime, c.LaunchSpeed)), Is.EqualTo(b.CmesAt(t, 30 * Day).Select(c => (c.Id, c.LaunchTime, c.LaunchSpeed))));
        // Asking in a different order (a time warp, a reload) changes nothing.
        var c2 = new StarActivity(Sun());
        c2.CmesAt(t + 200 * Day, 30 * Day);
        Assert.That(c2.CmesAt(t, 30 * Day).Select(c => c.Id), Is.EqualTo(a.CmesAt(t, 30 * Day).Select(c => c.Id)));
        var other = new StarActivity(Sun(seed: 2));
        Assert.That(other.RegionsAt(t).Select(r => r.Longitude0), Is.Not.EqualTo(a.RegionsAt(t).Select(r => r.Longitude0)));
        StarScene s1 = a.Snapshot(t, Double3.UnitZ), s2 = b.Snapshot(t, Double3.UnitZ);
        Assert.That(s1.Spots.Count, Is.EqualTo(s2.Spots.Count));
        Assert.That(s1.Structures.Select(s => s.Seed), Is.EqualTo(s2.Structures.Select(s => s.Seed)));
    }

    // ---------------------------------------------------------------- the cycle

    [Test]
    public void TheCycle_PeaksAtAboutAThirdOfTheWay_AndNeverFullyStops()
    {
        double best = 0, at = 0;
        for (double ph = 0.001; ph < 1.0; ph += 0.001)
            if (StarActivity.Shape(ph) > best) { best = StarActivity.Shape(ph); at = ph; }
        Assert.That(at, Is.EqualTo(0.36).Within(0.02));
        Assert.That(best, Is.EqualTo(1.0).Within(1e-3));
        Assert.That(StarActivity.Shape(0.0), Is.EqualTo(0.0));
        Assert.That(StarActivity.Shape(1.0), Is.EqualTo(0.0));
        var star = new StarActivity(Sun());
        Assert.That(star.Level(At(star.Profile, 0.0)), Is.GreaterThan(0.0));
        Assert.That(star.Level(At(star.Profile, 0.36)), Is.GreaterThan(10.0 * star.Level(At(star.Profile, 0.0))));
    }

    [Test]
    public void TheCycleAdvancesWithTheConfiguredPeriod()
    {
        var p = Sun();
        p.CyclePeriod = 22.0 * StarProfile.Year;
        var star = new StarActivity(p);
        Assert.That(star.Phase(0.0), Is.EqualTo(p.CyclePhase).Within(1e-9));
        Assert.That(star.Phase(11.0 * StarProfile.Year), Is.EqualTo(p.CyclePhase + 0.5).Within(1e-9));
        Assert.That(star.CycleIndex(23.0 * StarProfile.Year), Is.EqualTo(1));
    }

    [Test]
    public void SporersLaw_RegionsStartHighAndDriftToTheEquator()
    {
        Assert.That(StarActivity.MeanLatitude(0.0) / Deg, Is.EqualTo(30.0).Within(1.0));
        Assert.That(StarActivity.MeanLatitude(1.0) / Deg, Is.EqualTo(8.0).Within(1.0));
        var star = new StarActivity(Sun());
        double early = MeanLatitudeOver(star, 0.12, 0.25), late = MeanLatitudeOver(star, 0.65, 0.85);
        Assert.That(early, Is.GreaterThan(late + 5.0 * Deg), "regions emerge nearer the equator as the cycle goes on");
        Assert.That(early / Deg, Is.InRange(18.0, 35.0));
        Assert.That(late / Deg, Is.InRange(5.0, 18.0));
    }

    private static double MeanLatitudeOver(StarActivity star, double from, double to)
    {
        double sum = 0; int n = 0;
        var seen = new System.Collections.Generic.HashSet<long>();
        for (int cycle = 1; cycle < 6; cycle++)
            for (double ph = from; ph < to; ph += 0.004)
                foreach (ActiveRegion r in star.RegionsAt(At(star.Profile, ph, cycle)))
                    if (seen.Add(r.Id)) { sum += Math.Abs(r.Latitude); n++; }
        Assert.That(n, Is.GreaterThan(30));
        return sum / n;
    }

    [Test]
    public void JoysLaw_TiltsGrowWithLatitude_AndHalesLawFlipsPolarityAcrossTheEquatorAndEachCycle()
    {
        var star = new StarActivity(Sun());
        var regions = new System.Collections.Generic.List<ActiveRegion>();
        for (int cycle = 2; cycle < 6; cycle++)
            for (double ph = 0.05; ph < 0.95; ph += 0.01) regions.AddRange(star.RegionsAt(At(star.Profile, ph, cycle)));
        regions = regions.GroupBy(r => r.Id).Select(g => g.First()).ToList();
        double highTilt = regions.Where(r => Math.Abs(r.Latitude) > 20 * Deg).Average(r => r.Tilt);
        double lowTilt = regions.Where(r => Math.Abs(r.Latitude) < 10 * Deg).Average(r => r.Tilt);
        Assert.That(highTilt, Is.GreaterThan(lowTilt + 2.0 * Deg));
        Assert.That(regions.Where(r => Math.Abs(r.Latitude) > 5 * Deg).Average(r => r.Tilt) / Deg, Is.InRange(2.0, 15.0));
        foreach (var group in regions.GroupBy(r => r.Cycle))
        {
            Assert.That(group.Where(r => r.Latitude > 0).All(r => r.Polarity == group.First(x => x.Latitude > 0).Polarity));
            Assert.That(group.Where(r => r.Latitude < 0).All(r => r.Polarity == -group.First(x => x.Latitude > 0).Polarity));
        }
        var cycles = regions.GroupBy(r => r.Cycle).OrderBy(g => g.Key).ToList();
        for (int i = 1; i < cycles.Count; i++)
            Assert.That(cycles[i].First(r => r.Latitude > 0).Polarity, Is.EqualTo(-cycles[i - 1].First(r => r.Latitude > 0).Polarity));
    }

    [Test]
    public void TheSurfaceTurnsFasterAtTheEquator()
    {
        double equator = StellarRotation.Snodgrass(0.0), mid = StellarRotation.Snodgrass(45 * Deg), pole = StellarRotation.Snodgrass(80 * Deg);
        Assert.That(equator, Is.GreaterThan(mid));
        Assert.That(mid, Is.GreaterThan(pole));
        Assert.That(2.0 * Math.PI / equator / Day, Is.EqualTo(24.47).Within(0.05));      // the Sun's equatorial sidereal period
        Assert.That(2.0 * Math.PI / StellarRotation.Snodgrass(90 * Deg) / Day, Is.EqualTo(360.0 / (14.713 - 2.396 - 1.787)).Within(0.1));
        Assert.That(StellarRotation.Howard(0.0), Is.EqualTo(14.522 * Deg / Day).Within(1e-9));
        Assert.That(StellarRotation.Snodgrass(0.0, 12.0 * Day), Is.EqualTo(2.0 * Math.PI / (12.0 * Day)).Within(1e-12), "a star turning once in 12 days at its equator");
    }

    [Test]
    public void SunspotsMove_GrowAndDecay_DeterministicallyWithTime()
    {
        var star = new StarActivity(Sun());
        double t = At(star.Profile, 0.36);
        StarScene a = star.Snapshot(t, Double3.UnitZ), b = star.Snapshot(t + 2 * Day, Double3.UnitZ);
        SpotGroup g = a.Spots.OrderByDescending(x => x.AreaMsh).First();
        SpotGroup h = b.Spots.OrderBy(x => (x.Dir - g.Dir).Length).First();
        Assert.That((h.Dir - g.Dir).Length, Is.GreaterThan(0.1), "two days of rotation carry a group far across the disc");
        Assert.That(star.Snapshot(t, Double3.UnitZ).Spots.Select(x => x.Seed), Is.EqualTo(a.Spots.Select(x => x.Seed)));
        ActiveRegion r = star.RegionsAt(t).First(x => x.AreaMsh > 100);
        double young = ActiveRegion.Envelope(0.05), mature = ActiveRegion.Envelope(0.2), old = ActiveRegion.Envelope(0.9);
        Assert.That(mature, Is.GreaterThan(young));
        Assert.That(mature, Is.GreaterThan(old));
        Assert.That(r.Life, Is.GreaterThan(5 * Day), "a big region lasts weeks");
    }

    [Test]
    public void ARegionIsCarriedRoundByTheRotation()
    {
        var star = new StarActivity(Sun());
        ActiveRegion r = star.RegionsAt(At(star.Profile, 0.36)).First();
        double turn = star.RegionLongitude(r, r.Emerge + 10 * Day) - star.RegionLongitude(r, r.Emerge);
        double expected = StellarRotation.Howard(r.Latitude) * 10 * Day;
        Assert.That(turn, Is.EqualTo(expected).Within(1e-9));
        Assert.That(turn / Deg, Is.InRange(120.0, 150.0));
    }

    // ---------------------------------------------------------------- flares and CMEs

    [Test]
    public void FlaresFollowAPowerLaw_AndAreRareAtMinimum()
    {
        var star = new StarActivity(Sun());
        double max = At(star.Profile, 0.36), min = At(star.Profile, 0.0);
        var flaresMax = star.FlaresBetween(max, max + 60 * Day);
        var flaresMin = star.FlaresBetween(min, min + 60 * Day);
        Assert.That(flaresMax.Count, Is.GreaterThan(5 * Math.Max(flaresMin.Count, 1)));
        int c = flaresMax.Count(f => f.Flux >= 1e-6), m = flaresMax.Count(f => f.Flux >= 1e-5), x = flaresMax.Count(f => f.Flux >= 1e-4);
        Assert.That(c, Is.GreaterThan(m));
        Assert.That(m, Is.GreaterThan(x));
        Assert.That(c / (double)Math.Max(m, 1), Is.InRange(3.0, 12.0), "each tenfold in flux about 6 times rarer");
        Assert.That(flaresMax.First().Class, Does.Match("^[BCMX][0-9.]+$"));
    }

    [Test]
    public void CmesAreWithinObservedRanges_AndMoreNumerousAtMaximum()
    {
        var star = new StarActivity(Sun());
        double max = At(star.Profile, 0.36), min = At(star.Profile, 0.0);
        var atMax = star.CmesBetween(max, max + 120 * Day);
        var atMin = star.CmesBetween(min, min + 120 * Day);
        Assert.That(atMax.Count / 120.0, Is.InRange(0.3, 8.0), "per day at maximum");
        Assert.That(atMax.Count, Is.GreaterThan(3 * atMin.Count));
        Assert.That(atMax.All(c => c.LaunchSpeed >= 2.5e5 && c.LaunchSpeed <= 3.2e6));
        Assert.That(atMax.All(c => 2 * c.HalfWidth / Deg >= 30.0 - 1e-6 && 2 * c.HalfWidth / Deg <= 120.0 + 1e-6));
        double median = atMax.Select(c => c.LaunchSpeed).OrderBy(v => v).ElementAt(atMax.Count / 2);
        Assert.That(median, Is.InRange(3.0e5, 7.0e5));
        Assert.That(atMax.Max(c => c.LaunchSpeed), Is.GreaterThan(9.0e5), "some are fast");
        Assert.That(atMax.All(c => Math.Abs(c.Latitude) < 80 * Deg));
    }

    [Test]
    public void ACmeReportedByCmesAt_IsTheOneCmesBetweenFinds()
    {
        var star = new StarActivity(Sun());
        double t = At(star.Profile, 0.36);
        var between = star.CmesBetween(t - 20 * Day, t).Select(c => c.Id).ToList();
        var at = star.CmesAt(t, 20 * Day).Select(c => c.Id).ToList();
        Assert.That(at, Is.EqualTo(between));
    }

    // ---------------------------------------------------------------- the drag-based model

    private static Cme Test(double speed, double drag = 2.0e-11, double wind = 4.0e5, double halfWidth = 30 * Deg) =>
        new Cme { Id = 7, LaunchTime = 0.0, Latitude = 0.0, Longitude = 0.0, LaunchSpeed = speed, HalfWidth = halfWidth, Drag = drag, WindSpeed = wind, CloudField = 1.5e-8, Helicity = 1.0 }.Prepare();

    [Test]
    public void AFastCmeSlowsTowardsTheWind_AndASlowOneSpeedsUp()
    {
        Cme fast = Test(1.5e6), slow = Test(2.5e5);
        double w = 4.0e5;
        double vFast = fast.Speed(30 * Day), vSlow = slow.Speed(30 * Day);
        Assert.That(vFast, Is.GreaterThan(w).And.LessThan(1.5e6));
        Assert.That(vSlow, Is.GreaterThan(2.5e5).And.LessThan(w));
        Assert.That(Math.Abs(vFast - w), Is.LessThan(0.2 * 1.1e6), "close to the wind after a month");
        // Monotone approach.
        double last = fast.Speed(10 * 3600.0);
        for (int h = 11; h < 400; h += 5)
        {
            double v = fast.Speed(h * 3600.0);
            Assert.That(v, Is.LessThanOrEqualTo(last + 1e-6));
            last = v;
        }
        // Distance only ever grows.
        double d = 0;
        for (int h = 0; h < 600; h += 3)
        {
            double r = fast.Distance(h * 3600.0);
            Assert.That(r, Is.GreaterThanOrEqualTo(d));
            d = r;
        }
    }

    [Test]
    public void TheClosedFormMatchesNumericalIntegrationOfTheDragEquation()
    {
        var c = Test(1.2e6, drag: 3.0e-11);
        double t0 = c.LaunchTime + c.HandOffTime;
        double r = c.Distance(t0), v = c.Speed(t0), w = c.WindSpeed;
        const double dt = 10.0;
        double t = t0;
        for (int i = 0; i < 20000; i++)   // 55 hours
        {
            // Heun's method on dr/dt = v, dv/dt = -gamma (v - w) |v - w|.
            double a1 = -c.Drag * (v - w) * Math.Abs(v - w);
            double v1 = v + a1 * dt;
            double a2 = -c.Drag * (v1 - w) * Math.Abs(v1 - w);
            double vNew = v + 0.5 * (a1 + a2) * dt;
            r += 0.5 * (v + vNew) * dt;
            v = vNew;
            t += dt;
        }
        Assert.That(c.Distance(t), Is.EqualTo(r).Within(r * 1e-4));
        Assert.That(c.Speed(t), Is.EqualTo(v).Within(1.0e3));
    }

    [Test]
    public void ATypicalFastCmeTakesDaysToReachOneAu()
    {
        Cme c = Test(1.0e6);
        double hours = c.TimeToReach(Au) / 3600.0;
        Assert.That(hours, Is.InRange(40.0, 75.0), "a 1000 km/s CME reaches Earth in 2-3 days");
        Cme extreme = Test(2.8e6);
        Assert.That(extreme.TimeToReach(Au) / 3600.0, Is.InRange(18.0, 36.0), "the fastest in a day or a little more");
        Cme slow = Test(3.0e5);
        Assert.That(slow.TimeToReach(Au) / 3600.0, Is.InRange(80.0, 140.0));
        Assert.That(extreme.TimeToReach(Au), Is.LessThan(c.TimeToReach(Au)));
    }

    [Test]
    public void TimeToReach_InvertsDistance()
    {
        Cme c = Test(9.0e5);
        foreach (double d in new[] { 3e9, 1.4e10, 5e10, Au, 3 * Au })
            Assert.That(c.Distance(c.LaunchTime + c.TimeToReach(d)), Is.EqualTo(d).Within(d * 1e-6));
    }

    [Test]
    public void ArrivalTimes_DependOnWhereInTheConeAPlaceIs()
    {
        Cme c = Test(1.0e6, halfWidth: 40 * Deg);
        var onAxis = new Double3(Au, 0, 0);
        var flank = new Double3(Math.Cos(30 * Deg), 0, Math.Sin(30 * Deg)) * Au;
        var outside = new Double3(Math.Cos(60 * Deg), 0, Math.Sin(60 * Deg)) * Au;
        var behind = new Double3(-Au, 0, 0);
        double t0 = c.ArrivalTime(onAxis), t1 = c.ArrivalTime(flank);
        Assert.That(t0, Is.EqualTo(c.TimeToReach(Au)).Within(1.0));
        Assert.That(t1, Is.GreaterThan(t0), "the flank is reached later");
        Assert.That(c.ArrivalTime(outside), Is.NaN);
        Assert.That(c.ArrivalTime(behind), Is.NaN);
        Assert.That(c.DepartureTime(onAxis), Is.GreaterThan(t0 + 10 * 3600.0), "the sheath and cloud take hours to pass");
        Assert.That(c.EdgeFactor(0.0), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(c.EdgeFactor(40 * Deg), Is.GreaterThan(0.0).And.LessThan(1.0));
        Assert.That(c.EdgeFactor(41 * Deg), Is.EqualTo(0.0));
    }

    // ---------------------------------------------------------------- space weather

    private static StarWind Wind(StarProfile p = null, double reference = Au)
    {
        p ??= Sun();
        p.CmeRate = 0.0;   // nothing but what a test injects
        return new StarWind(new StarActivity(p), reference);
    }

    [Test]
    public void TheQuietWind_IsTheSunsAtTheReferenceDistance_AndFallsOffWithDistance()
    {
        StarWind wind = Wind();
        double t = At(wind.Activity.Profile, 0.36) ;
        SolarWindState home = wind.At(new Double3(Au, 0, 0), t);
        Assert.That(home.Speed, Is.EqualTo(4.0e5));
        Assert.That(home.Density, Is.EqualTo(5.0e6).Within(1e3));
        Assert.That(home.FieldStrength, Is.EqualTo(5.0e-9).Within(1.5e-9));
        Assert.That(home.DynamicPressure, Is.EqualTo(1.3e-9).Within(0.3e-9));
        Assert.That(home.InCme, Is.EqualTo(0.0));
        Assert.That(home.EnergeticParticles, Is.EqualTo(1.0));
        SolarWindState far = wind.At(new Double3(0, 4 * Au, 0), t);
        Assert.That(far.Density, Is.EqualTo(home.Density / 16.0).Within(1.0));
        Assert.That(far.Speed, Is.EqualTo(home.Speed));
        Assert.That(far.FieldStrength, Is.EqualTo(home.FieldStrength / 4.0).Within(home.FieldStrength * 0.01));
        // A planet at half the reference distance (the reference being the home planet's orbit) sees four times the density.
        StarWind small = Wind(reference: 0.5 * Au);
        Assert.That(small.At(new Double3(0.5 * Au, 0, 0), t).Density, Is.EqualTo(5.0e6).Within(1e3));
        Assert.That(small.At(new Double3(Au, 0, 0), t).Density, Is.EqualTo(5.0e6 / 4.0).Within(1e3));
        // With no source, the space weather is the same quiet wind.
        SpaceWeather.SpaceWeather.Source = null;
        Assert.That(SpaceWeather.SpaceWeather.At("x", new Double3(Au, 0, 0), 0.0).Density, Is.EqualTo(5.0e6).Within(1e3));
    }

    [Test]
    public void ACmeWithAShock_CompressesTheWindThenBringsAFluxRope()
    {
        StarWind wind = Wind();
        Cme c = Test(1.2e6, halfWidth: 45 * Deg);
        wind.Activity.Inject(c);
        var home = new Double3(Au, 0, 0);
        double arrival = c.ArrivalTime(home), departure = c.DepartureTime(home);
        SolarWindState before = wind.At(home, arrival - 3600.0);
        SolarWindState shock = wind.At(home, arrival + 120.0);
        SolarWindState sheath = wind.At(home, arrival + 0.5 * (departure - arrival) * 0.2);
        SolarWindState cloud = wind.At(home, arrival + 0.65 * (departure - arrival));
        SolarWindState after = wind.At(home, departure + 3600.0);
        Assert.That(before.InCme, Is.EqualTo(0.0));
        Assert.That(before.Speed, Is.EqualTo(4.0e5));
        Assert.That(shock.InCme, Is.EqualTo(1.0));
        Assert.That(shock.Density, Is.GreaterThan(3.0 * before.Density), "a strong shock compresses about 4 times");
        Assert.That(shock.Speed, Is.GreaterThan(before.Speed + 1.0e5), "and the speed jumps");
        Assert.That(shock.FieldStrength, Is.GreaterThan(1.5 * before.FieldStrength));
        Assert.That(sheath.Density, Is.GreaterThan(1.5 * before.Density));
        Assert.That(cloud.InCme, Is.EqualTo(1.0));
        Assert.That(cloud.Density, Is.LessThan(before.Density), "a magnetic cloud is thin");
        Assert.That(cloud.FieldStrength, Is.GreaterThan(1.5e-8 * 0.2).And.LessThan(5e-8));
        Assert.That(after.InCme, Is.EqualTo(0.0));
        Assert.That(after.Speed, Is.EqualTo(4.0e5));
        Assert.That((departure - arrival) / 3600.0, Is.InRange(15.0, 60.0), "a day or two to pass at 1 AU");
        Assert.That(shock.EnergeticParticles, Is.GreaterThan(30.0), "a fast CME brings energetic particles");
    }

    [Test]
    public void TheMagneticClouds_NorthSouthFieldTurnsThroughThePassage()
    {
        // A rope whose axis is east-west with a path through the middle: Bz goes one way, through zero, to the other.
        var c = Test(8.0e5);
        c.AxisAngle = 0.0;
        c.Impact = 0.0;
        c.Helicity = 1.0;
        var home = new Double3(Au, 0, 0);
        double first = StarWind.FluxRope(c, home, 0.05, 1e-8).Y, mid = StarWind.FluxRope(c, home, 0.5, 1e-8).Y, last = StarWind.FluxRope(c, home, 0.95, 1e-8).Y;
        Assert.That(first * last, Is.LessThan(0.0), "it swings from one sign to the other");
        Assert.That(Math.Abs(mid), Is.LessThan(Math.Abs(first)), "through the middle the field lies along the axis");
        Assert.That(StarWind.FluxRope(c, home, 0.5, 1e-8).Length, Is.EqualTo(1e-8).Within(1e-10), "the axial field is the peak one on the axis");
        c.Helicity = -1.0;
        Assert.That(StarWind.FluxRope(c, home, 0.05, 1e-8).Y * first, Is.LessThan(0.0), "the other handedness swings the other way");
        StarWind.Bessel01(2.405, out double j0, out double j1);
        Assert.That(j0, Is.EqualTo(0.0).Within(1e-3));
        Assert.That(j1, Is.EqualTo(0.5191).Within(1e-3));
    }

    [Test]
    public void ACloudsFieldFallsWithDistance()
    {
        StarWind wind = Wind();
        Cme c = Test(7.0e5, halfWidth: 50 * Deg);
        wind.Activity.Inject(c);
        double Peak(double distance)
        {
            var p = new Double3(distance, 0, 0);
            double a = c.ArrivalTime(p), d = c.DepartureTime(p), best = 0;
            for (int i = 1; i < 60; i++) best = Math.Max(best, wind.At(p, a + (d - a) * i / 60.0).FieldStrength);
            return best;
        }
        Assert.That(Peak(0.5 * Au), Is.GreaterThan(Peak(Au) * 2.0));
    }

    [Test]
    public void TheForecast_AgreesWithWhatTheWindDoes()
    {
        StarWind wind = Wind();
        Cme c = Test(9.0e5, halfWidth: 45 * Deg);
        c.LaunchTime = 1.0e6;
        wind.Activity.Inject(c);
        var home = new Double3(Au * Math.Cos(10 * Deg), 0, Au * Math.Sin(10 * Deg));
        var list = wind.Incoming(home, 1.0e6 + 3600.0, 10 * Day);
        Assert.That(list.Count, Is.EqualTo(1));
        CmeForecast f = list[0];
        Assert.That(f.Cme, Is.SameAs(c));
        Assert.That(f.Arrival, Is.GreaterThan(1.0e6 + 24 * 3600.0));
        Assert.That(wind.At(home, f.Arrival - 600.0).InCme, Is.EqualTo(0.0));
        Assert.That(wind.At(home, f.Arrival + 600.0).InCme, Is.EqualTo(1.0));
        Assert.That(wind.At(home, f.Departure + 600.0).InCme, Is.EqualTo(0.0));
        Assert.That(f.Speed, Is.InRange(4.0e5, 9.0e5));
        Assert.That(f.StormClass, Is.Not.Empty);
        Assert.That(wind.Incoming(new Double3(-Au, 0, 0), 1.0e6 + 3600.0, 10 * Day), Is.Empty, "the far side is missed");
        Assert.That(wind.Incoming(home, f.Departure + 3600.0, 10 * Day), Is.Empty, "gone by");
    }

    [Test]
    public void EnergeticParticles_RiseThenDecay_AndAreWeakerFarOut()
    {
        StarWind wind = Wind();
        Cme c = Test(2.0e6, halfWidth: 60 * Deg);
        c.FlareFlux = 3e-4;
        c.Longitude = 0.0;
        var well = new Double3(Au * Math.Cos(-50 * Deg), 0, Au * Math.Sin(-50 * Deg));   // connected: the spiral leads to the eruption
        double early = wind.EnergeticParticles(c, well, 600.0);
        double peak = wind.EnergeticParticles(c, well, 3.0 * 3600.0);
        double late = wind.EnergeticParticles(c, well, 4 * Day);
        Assert.That(early, Is.EqualTo(0.0).Or.LessThan(peak));
        Assert.That(peak, Is.GreaterThan(1e3));
        Assert.That(late, Is.LessThan(0.1 * peak));
        Assert.That(wind.EnergeticParticles(c, new Double3(0, 0, 4 * Au), 3.0 * 3600.0 + 4 * 3600.0), Is.LessThan(peak));
        var slow = Test(3e5);
        Assert.That(wind.EnergeticParticles(slow, well, 3.0 * 3600.0), Is.EqualTo(0.0));
    }

    [Test]
    public void TheWindSource_FallsBackToQuietForAnUnknownStar()
    {
        var source = new StarWindSource();
        source.Set("Sun", Wind());
        SpaceWeather.SpaceWeather.ReferenceDistance = Au;
        Assert.That(source.At("Other", new Double3(2 * Au, 0, 0), 0.0).Density, Is.EqualTo(5.0e6 / 4.0).Within(1.0));
        Assert.That(source.At("Sun", new Double3(Au, 0, 0), 0.0).Speed, Is.EqualTo(4.0e5));
    }

    // ---------------------------------------------------------------- optics

    [Test]
    public void TheLimbIsDarkerAndRedder()
    {
        double green = StarOptics.LimbRatio(0.1, 5772, StarOptics.Green);
        Assert.That(green, Is.InRange(0.25, 0.45), "the Sun's limb at 550 nm");
        Assert.That(StarOptics.LimbRatio(0.1, 5772, StarOptics.Blue), Is.LessThan(green));
        Assert.That(StarOptics.LimbRatio(0.1, 5772, StarOptics.Red), Is.GreaterThan(green));
        Assert.That(StarOptics.LimbRatio(1.0, 5772, StarOptics.Green), Is.EqualTo(1.0).Within(1e-12));
        double mean = StarOptics.MeanOverCentre(5772, StarOptics.Green);
        Assert.That(mean, Is.InRange(0.7, 0.9));
        Assert.That(StarOptics.LimbRatio(0.1, 3500, StarOptics.Green), Is.GreaterThan(0.0));
    }

    [Test]
    public void TheCorona_IsAMillionthOfTheDisc_AndOnlyShowsInTotality()
    {
        Assert.That(StarOptics.KCorona(1.03), Is.InRange(1e-6, 5e-6));
        Assert.That(StarOptics.KCorona(2.0), Is.InRange(5e-9, 1e-7));
        Assert.That(StarOptics.KCorona(1.5), Is.GreaterThan(StarOptics.KCorona(2.5)));
        Assert.That(StarOptics.KCorona(1.2, polar: 1.0), Is.LessThan(StarOptics.KCorona(1.2)));
        Assert.That(StarOptics.CoronaVisibility(1.0), Is.EqualTo(0.0));
        Assert.That(StarOptics.CoronaVisibility(0.5), Is.EqualTo(0.0));
        Assert.That(StarOptics.CoronaVisibility(0.0), Is.EqualTo(1.0));
        Assert.That(StarOptics.CoronaVisibility(1e-3), Is.InRange(0.01, 0.99));
        // The Moon from the Earth: the same angular size as the Sun, so a central eclipse is total.
        double total = StarOptics.DiscVisibleFraction(0.00465, 0.00471, 0.0, 5772);
        double partial = StarOptics.DiscVisibleFraction(0.00465, 0.00471, 0.004, 5772);
        Assert.That(total, Is.EqualTo(0.0).Within(1e-6));
        Assert.That(partial, Is.GreaterThan(0.1));
        Assert.That(StarOptics.CoronaVisibility(total), Is.EqualTo(1.0));
        Assert.That(StarOptics.CoronaVisibility(partial), Is.EqualTo(0.0));
    }

    // ---------------------------------------------------------------- what the renderer gets

    [Test]
    public void TheSnapshot_HasSpotsAndLoopsAtMaximumAndLittleAtMinimum()
    {
        var star = new StarActivity(Sun());
        StarScene max = star.Snapshot(At(star.Profile, 0.36), Double3.UnitZ);
        StarScene min = star.Snapshot(At(star.Profile, 0.0), Double3.UnitZ);
        Assert.That(max.Spots.Count, Is.GreaterThan(min.Spots.Count));
        Assert.That(max.Spots.Count, Is.InRange(3, 24));
        Assert.That(max.Structures.Count(s => s.Kind == StructureKind.Arch), Is.GreaterThan(0));
        Assert.That(max.Activity, Is.GreaterThan(min.Activity));
        foreach (SpotGroup g in max.Spots)
        {
            Assert.That(g.Dir.Length, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(Math.Abs(Double3.Dot(g.Dir, g.Axis)), Is.LessThan(1e-6), "the axis lies in the surface");
            Assert.That(g.UmbraRadius, Is.InRange(0.0005, 0.08));
        }
        foreach (StarStructure s in max.Structures)
            Assert.That(s.Height, Is.GreaterThan(0.0).And.LessThan(2.0));
    }

    [Test]
    public void ALaunchedCme_ShowsAsAnEruptionThenAShell()
    {
        var star = new StarActivity(Sun(phase: 0.0));
        Cme c = Test(1.0e6);
        c.Latitude = 20 * Deg;
        c.Longitude = 40 * Deg;
        c.LaunchTime = 1e7;
        star.Inject(c);
        StarScene early = star.Snapshot(1e7 + 1800.0, new Double3(Math.Cos(40 * Deg), 0.3, Math.Sin(40 * Deg)));
        Assert.That(early.Structures.Any(s => s.Label == "Eruption" && s.Erupt > 0.0));
        StarScene later = star.Snapshot(1e7 + 2.5 * 3600.0, new Double3(Math.Cos(40 * Deg), 0.3, Math.Sin(40 * Deg)));
        Assert.That(later.Shells.Count, Is.EqualTo(1));
        Assert.That(later.Shells[0].Apex, Is.GreaterThan(3.0));
        StarScene gone = star.Snapshot(1e7 + 5 * Day, Double3.UnitZ);
        Assert.That(gone.Shells, Is.Empty, "beyond 14 radii the renderer stops drawing it");
        star.ClearInjected();
        Assert.That(star.Snapshot(1e7 + 2.5 * 3600.0, Double3.UnitZ).Shells, Is.Empty);
    }

    [Test]
    public void TheProfile_ReadsItsConfig()
    {
        var node = new KSPVE.Pure.Config.CfgNode("STAR")
            .Add("body", "Kerbol").Add("temperature", "6100").Add("cyclePeriodYears", "5").Add("cyclePhase", "1.25").Add("seed", "42")
            .Add("cmes", "False").Add("activity", "2.5");
        StarProfile p = StarProfile.From(node, new StarProfile());
        Assert.That(p.Body, Is.EqualTo("Kerbol"));
        Assert.That(p.Temperature, Is.EqualTo(6100.0));
        Assert.That(p.CyclePeriod, Is.EqualTo(5.0 * StarProfile.Year));
        Assert.That(p.CyclePhase, Is.EqualTo(0.25).Within(1e-12));
        Assert.That(p.Seed, Is.EqualTo(42UL));
        Assert.That(p.Cmes, Is.False);
        Assert.That(p.Corona, Is.True, "unnamed keys keep the baseline");
        Assert.That(new StarActivity(p).CmesBetween(0, 100 * Day), Is.Empty);
    }
}
