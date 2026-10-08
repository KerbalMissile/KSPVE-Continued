using System;
using KSPVE.Pure.Space.Tde;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Tde;

public class TidalDisruptionTests
{
    // Kcalbeloh as Sol + Kcalbeloh + KSS2 draws it: horizon 9.6e9 m, so 3.25 million Suns.
    private const double Horizon = 9.6e9;

    // A Sun-like star at beta = 2, tilted: the textbook case the simulation fits are checked against.
    private static TdeParameters SunLike() => new TdeParameters
    {
        StarMassSuns = 1.2, StarRadiusSuns = 1.3, StarTemperature = 6300.0, Periapsis = 0.0, Beta = 2.0, StartDistance = 10.0,
        InclinationDeg = 35.0
    };

    private static readonly Lazy<TidalDisruption> Sun = new Lazy<TidalDisruption>(() => new TidalDisruption(SunLike(), Horizon, 42));
    // The default: a big O star on a deep, coplanar orbit that builds Kcalbeloh's disk.
    private static readonly Lazy<TidalDisruption> Default = new Lazy<TidalDisruption>(() => new TidalDisruption(new TdeParameters(), Horizon, 42));

    [Test]
    public void TheHoleIsTheOneTheRendererDraws()
    {
        TidalDisruption tde = Sun.Value;
        Assert.That(tde.HoleMassSuns, Is.EqualTo(3.25e6).Within(1.0).Percent);
        Assert.That(tde.TimeUnit, Is.EqualTo(Horizon / 299792458.0).Within(1e-9).Percent);
    }

    [Test]
    public void TidalRadius_IsRStarTimesTheCubeRootOfTheMassRatio()
    {
        TidalDisruption tde = Sun.Value;
        double expected = 1.3 * 6.957e8 * Math.Pow(3.25e6 / 1.2, 1.0 / 3.0) / Horizon;
        Assert.That(tde.TidalRadius, Is.EqualTo(expected).Within(1.0).Percent);
        Assert.That(tde.Periapsis, Is.EqualTo(tde.TidalRadius / 2.0).Within(1e-9).Percent);
    }

    [Test]
    public void SunLikeStarAtBetaTwo_IsDisruptedFully_AndHalfOfItStaysBound()
    {
        TidalDisruption tde = Sun.Value;
        Assert.That(tde.CoreRadius, Is.EqualTo(0.0));
        Assert.That(tde.StrippedFraction, Is.EqualTo(1.0));
        Assert.That(tde.BoundFraction, Is.EqualTo(0.5).Within(0.05));
    }

    [Test]
    public void SunLikeStarAtBetaOne_LosesWhatTheSimulationsSay()
    {
        // Guillochon and Ramirez-Ruiz 2013, eq. A10: C_4/3(1) = exp(-5.288 / 2.103) = 0.081.
        TdeParameters p = SunLike();
        p.Beta = 1.0;
        var tde = new TidalDisruption(p, Horizon, 42);
        Assert.That(tde.StrippedFraction, Is.EqualTo(0.081).Within(3.0).Percent);
        Assert.That(tde.CoreRadius, Is.GreaterThan(0.0).And.LessThan(1.0));
    }

    [Test]
    public void Fallback_PeaksWhenAndAsHighAsTheSimulationsSay()
    {
        // eqs. A6, A8 at beta = 2: A = 3.03 Suns/yr, B = 0.0773 yr, scaled to M6 = 3.25, m* = 1.2, r* = 1.3.
        TidalDisruption tde = Sun.Value;
        Assert.That(tde.TargetPeakSeconds / 86400.0, Is.EqualTo(62.8).Within(2.0).Percent);
        Assert.That(tde.TargetPeakRate, Is.EqualTo(1.635 * 1.98847e30 / (365.25 * 86400.0)).Within(2.0).Percent);
        var frame = new TdeFrame();
        double best = 0.0, bestTime = 0.0;
        for (double days = 20.0; days < 400.0; days += 1.0)
        {
            tde.Evaluate(tde.ApproachSeconds + days * 86400.0, frame);
            if (frame.FallbackEddington > best) { best = frame.FallbackEddington; bestTime = days; }
        }
        Assert.That(bestTime, Is.EqualTo(tde.TargetPeakSeconds / 86400.0).Within(10.0).Percent);
        Assert.That(best * tde.EddingtonAccretionRate, Is.EqualTo(tde.TargetPeakRate).Within(30.0).Percent);
        Assert.That(tde.MostBoundPeriodSeconds, Is.LessThan(tde.TargetPeakSeconds), "the first debris is back before the peak");
    }

    [Test]
    public void ThePeriapsisAdvance_IsStrongSoCloseToTheHole()
    {
        TidalDisruption tde = Sun.Value;
        double weakField = 6.0 * Math.PI * 0.5 / (2.0 * tde.Periapsis);
        Assert.That(tde.PeriapsisAdvance, Is.GreaterThan(weakField).And.LessThan(3.0 * weakField));
    }

    [Test]
    public void Fallback_DeclinesAsTimeToTheMinusFiveThirds()
    {
        TidalDisruption tde = Sun.Value;
        double tMin = tde.MostBoundPeriodSeconds;
        var frame = new TdeFrame();
        tde.Evaluate(tde.ApproachSeconds + 6.0 * tMin, frame);
        double early = frame.FallbackEddington;
        tde.Evaluate(tde.ApproachSeconds + 12.0 * tMin, frame);
        double late = frame.FallbackEddington;
        Assert.That(late / early, Is.EqualTo(Math.Pow(2.0, -5.0 / 3.0)).Within(20.0).Percent);
    }

    [Test]
    public void TheEventRunsFromApproachToFlare()
    {
        TidalDisruption tde = Sun.Value;
        var frame = new TdeFrame();

        tde.Evaluate(0.0, frame);
        Assert.That(frame.Active && frame.StarVisible, Is.True);
        Assert.That(frame.Phase, Is.EqualTo(TdePhase.Approach));
        Assert.That(frame.StarCenter.Length, Is.EqualTo(10.0 * tde.TidalRadius).Within(1.0).Percent);

        tde.Evaluate(tde.ApproachSeconds * 0.999, frame);
        Assert.That(frame.StarLong, Is.GreaterThan(frame.StarShort), "tidally stretched");
        Assert.That(frame.StarTail, Is.GreaterThan(0.9), "pouring gas off its tips at the tidal radius");

        tde.Evaluate(tde.ApproachSeconds + 3600.0, frame);
        Assert.That(frame.StarVisible, Is.False, "fully disrupted: no core, and the star has dissolved into the stream");
        Assert.That(frame.VertexCount, Is.InRange(2, TdeFrame.MaxVertices));
        Assert.That(frame.Opacity[frame.VertexCount / 2], Is.GreaterThan(1.0), "the stream is optically thick");

        tde.Evaluate(tde.ApproachSeconds + 0.5 * tde.MostBoundPeriodSeconds, frame);
        Assert.That(frame.Phase, Is.EqualTo(TdePhase.Stream));
        Assert.That(frame.ShockVisible, Is.False);
        Assert.That(frame.DiskFormation, Is.EqualTo(0.0), "no disk before the debris comes back");
        Assert.That(frame.Extent, Is.GreaterThan(tde.TidalRadius * 10.0), "the bound debris swings far out");

        tde.Evaluate(tde.ApproachSeconds + 1.6 * tde.MostBoundPeriodSeconds, frame);
        Assert.That(frame.Phase, Is.EqualTo(TdePhase.Flare));
        Assert.That(frame.ShockVisible, Is.True, "the returning stream crosses itself");
        Assert.That(frame.DiskFormation, Is.GreaterThan(0.0));
        Assert.That(frame.Luminosity, Is.GreaterThan(0.1), "a super-Eddington-ish flare");
        Assert.That(frame.FallbackEddington, Is.GreaterThan(1.0));
    }

    [Test]
    public void TheDefaultStar_IsBigAndSettlesInsideKcalbelohsDisk()
    {
        TidalDisruption tde = Default.Value;
        Assert.That(tde.Periapsis, Is.EqualTo(5.0));
        Assert.That(tde.TidalRadius, Is.GreaterThan(40.0), "a big star comes apart far out");
        Assert.That(tde.StarRadius, Is.GreaterThan(1.0), "as big as the hole's shadow");
        Assert.That(tde.CircularizationRadius, Is.InRange(3.0, 14.0), "the debris settles where Kcalbeloh's disk is");
        Assert.That(tde.StrippedFraction, Is.EqualTo(1.0));
    }

    [Test]
    public void TheDisk_FormsOverTwoToThreeMonths_AndStays()
    {
        TidalDisruption tde = Default.Value;
        var frame = new TdeFrame();
        double firstReturn = tde.FirstReturnSeconds / 86400.0;
        tde.Evaluate(tde.ApproachSeconds + (firstReturn - 1.0) * 86400.0, frame);
        Assert.That(frame.DiskFormation, Is.EqualTo(0.0));

        double previous = 0.0, whole = double.NaN;
        for (double day = firstReturn; day < firstReturn + 200.0; day += 1.0)
        {
            tde.Evaluate(tde.ApproachSeconds + day * 86400.0, frame);
            Assert.That(frame.DiskFormation, Is.GreaterThanOrEqualTo(previous), "it only grows");
            previous = frame.DiskFormation;
            if (frame.DiskFormation >= 1.0) { whole = day; break; }
        }
        Assert.That(whole - firstReturn, Is.InRange(45.0, 100.0), "from the first return to a whole disk: about 2-3 months");

        tde.Evaluate(tde.EndSeconds * 2.0, frame);
        Assert.That(frame.Active, Is.True);
        Assert.That(frame.DiskFormation, Is.EqualTo(1.0), "once built, the disk stays");
    }

    [Test]
    public void TheJets_LaunchOnlyOnceTheDiskIsHalfBuilt()
    {
        TidalDisruption tde = Default.Value;
        var frame = new TdeFrame();
        for (double day = -1.0; day < 200.0; day += 0.5)
        {
            tde.Evaluate(tde.ApproachSeconds + day * 86400.0, frame);
            if (frame.JetAge > 0.0) Assert.That(frame.DiskFormation, Is.GreaterThanOrEqualTo(0.5), $"day {day}");
            else Assert.That(frame.DiskFormation, Is.LessThan(0.55), $"day {day}");
        }
        Assert.That(frame.JetAge, Is.GreaterThan(0.0), "launched by the end of it");
    }

    [Test]
    public void ADeepEncounter_FlashesBrieflyAtPeriapsis()
    {
        TidalDisruption tde = Default.Value;   // beta 9.4
        var frame = new TdeFrame();
        double best = 0.0, bestDay = 0.0;
        for (double day = 0.0; day < 0.3; day += 0.0005)
        {
            tde.Evaluate(tde.ApproachSeconds + day * 86400.0, frame);
            if (frame.FlashStrength > best) { best = frame.FlashStrength; bestDay = day; }
        }
        Assert.That(best, Is.GreaterThan(3.5));
        tde.Evaluate(tde.ApproachSeconds + bestDay * 86400.0, frame);
        Assert.That(frame.FlashPosition.Length, Is.EqualTo(tde.Periapsis).Within(5).Percent, "at periapsis");
        Assert.That(frame.FlashRadius, Is.EqualTo(tde.StarRadius));
        tde.Evaluate(tde.ApproachSeconds + (bestDay + 0.05) * 86400.0, frame);
        Assert.That(frame.FlashVisible, Is.False, "the burst lasts about the periapsis crossing time");
    }

    [Test]
    public void AGrazingEncounter_HasNoFlash()
    {
        TidalDisruption tde = Sun.Value;   // beta 2
        var frame = new TdeFrame();
        for (double day = 0.0; day < 2.0; day += 0.01)
        {
            tde.Evaluate(tde.ApproachSeconds + day * 86400.0, frame);
            Assert.That(frame.FlashVisible, Is.False, $"day {day}");
        }
    }

    [Test]
    public void TheSameSeed_GivesTheSameEvent()
    {
        var a = new TidalDisruption(new TdeParameters(), Horizon, 7);
        var b = new TidalDisruption(new TdeParameters(), Horizon, 7);
        var fa = new TdeFrame();
        var fb = new TdeFrame();
        a.Evaluate(a.ApproachSeconds + 86400.0, fa);
        b.Evaluate(b.ApproachSeconds + 86400.0, fb);
        Assert.That(fa.VertexCount, Is.EqualTo(fb.VertexCount));
        Assert.That(fa.Position[5], Is.EqualTo(fb.Position[5]));
    }

    [Test]
    public void BeforeTheStart_NothingIsDrawn()
    {
        var frame = new TdeFrame();
        Default.Value.Evaluate(-10.0, frame);
        Assert.That(frame.Active, Is.False);
    }
}

public class DiskStateTests
{
    [Test]
    public void WithoutAnEvent_ThereIsNoDiskAndNoJets()
    {
        DiskState s = DiskState.Forming(null, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(s.Presence, Is.EqualTo(0.0));
        Assert.That(s.Jets, Is.EqualTo(0.0));
    }

    [Test]
    public void ItStartsAsARingAtTheCircularisationRadius_AndSpreadsToTheConfiguredDisk()
    {
        var frame = new TdeFrame { Active = true, DiskFormRadius = 10.8, DiskFormation = 0.05 };
        DiskState young = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(young.Inner, Is.GreaterThan(7.0));
        Assert.That(young.Outer, Is.LessThan(14.0));
        Assert.That(young.Presence, Is.GreaterThan(0.0).And.LessThan(1.0));

        frame.DiskFormation = 1.0;
        DiskState whole = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(whole.Inner, Is.EqualTo(3.0).Within(1e-9));
        Assert.That(whole.Outer, Is.EqualTo(14.0).Within(1e-9));
        Assert.That(whole.Presence, Is.EqualTo(1.0));
    }

    [Test]
    public void ItIsDrawnAsTheConfiguredDisk_SeenOnlyWhereTheGasHasReached()
    {
        var frame = new TdeFrame { Active = true, DiskFormRadius = 10.8, DiskFormation = 0.0 };
        DiskState ring = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(ring.ShapeInner, Is.EqualTo(3.0));
        Assert.That(ring.ShapeOuter, Is.EqualTo(14.0));
        Assert.That(ring.MaskOuter - ring.MaskInner, Is.LessThan(2.0), "a narrow ring at first");
        Assert.That(ring.MaskSoftness, Is.GreaterThan(0.0));

        frame.DiskFormation = 0.5;
        DiskState half = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(half.MaskInner, Is.LessThan(ring.MaskInner - 2.0).And.GreaterThan(3.0), "spreading inward");

        // Once built, the band reaches past the disk's own soft edges: nothing of the configured disk is cut off.
        frame.DiskFormation = 1.0;
        DiskState whole = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(whole.MaskInner + whole.MaskSoftness, Is.LessThanOrEqualTo(0.8 * 3.0));
        Assert.That(whole.MaskOuter - whole.MaskSoftness, Is.GreaterThanOrEqualTo(1.1 * 14.0));
    }

    [Test]
    public void TheJets_GrowOutAtHalfTheSpeedOfLight()
    {
        var frame = new TdeFrame { Active = true, DiskFormRadius = 10.8, DiskFormation = 0.6, JetAge = -5.0 };
        Assert.That(DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0).Jets, Is.EqualTo(0.0));
        frame.JetAge = 60.0; // r_s / c: the heads are 30 r_s out, half way
        DiskState half = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(half.JetLength, Is.EqualTo(30.0).Within(1e-9));
        Assert.That(half.Jets, Is.EqualTo(1.3).Within(1e-9));
        frame.JetAge = 1000.0;
        Assert.That(DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0).JetLength, Is.EqualTo(60.0));
    }

    [Test]
    public void SpiralShocks_WindThroughTheDisk_OnlyWhileItForms()
    {
        var frame = new TdeFrame { Active = true, DiskFormRadius = 10.8, DiskFormation = 0.0 };
        Assert.That(DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0).Spiral, Is.EqualTo(0.0));
        frame.DiskFormation = 0.3;
        Assert.That(DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0).Spiral, Is.GreaterThan(0.5));
        frame.DiskFormation = 1.0;
        Assert.That(DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0).Spiral, Is.EqualTo(0.0));
    }

    [Test]
    public void TheJetHeads_GlowOnTheWayOut_AndTheHoleFlaresAsTheyLaunch()
    {
        var frame = new TdeFrame { Active = true, DiskFormRadius = 10.8, DiskFormation = 0.6, JetAge = -5.0 };
        DiskState before = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(before.JetHeads, Is.EqualTo(0.0));
        Assert.That(before.LaunchFlash, Is.EqualTo(0.0));
        frame.JetAge = 10.0;
        DiskState launching = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(launching.JetHeads, Is.EqualTo(1.0));
        Assert.That(launching.LaunchFlash, Is.GreaterThan(0.7));
        frame.JetAge = 1000.0;
        DiskState settled = DiskState.Forming(frame, 3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(settled.JetHeads, Is.EqualTo(0.0), "gone once the jets reach full length");
        Assert.That(settled.LaunchFlash, Is.LessThan(1e-6));
    }

    [Test]
    public void AHoleWithItsOwnDisk_KeepsItAsConfigured()
    {
        DiskState s = DiskState.Configured(3.0, 14.0, 0.12, 1.3, 60.0);
        Assert.That(s.Presence, Is.EqualTo(1.0));
        Assert.That(s.Jets, Is.EqualTo(1.3));
        Assert.That(s.JetLength, Is.EqualTo(60.0));
    }
}
