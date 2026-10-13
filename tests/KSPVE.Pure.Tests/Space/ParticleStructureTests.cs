using System;
using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Particles;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class ParticleStructureTests
{
    const double Au = 1.495978707e11, MSun = 1.98847e30, MEarth = 5.9722e24, REarth = 6.371e6, MJup = 1.8982e27, RJup = 7.1492e7;

    static StructureContext Context(double bodyMass, double bodyRadius, double starAu, double ut, double starMassSuns = 1.0, double starRadius = 6.957e8)
    {
        double muStar = UniversalKepler.G * starMassSuns * MSun, d = starAu * Au;
        return new StructureContext
        {
            Ut = ut, BodyMu = UniversalKepler.G * bodyMass, BodyRadius = bodyRadius,
            HasStar = true, StarMu = muStar, StarRadius = starRadius, StarTemperature = 5800.0,
            BodyFromStar = new Double3(d, 0, 0), BodyVelocity = new Double3(0, Math.Sqrt(muStar / d), 0), Axis = Double3.UnitZ
        };
    }

    static StructureConfig Config(StructureKind kind, params (string, double)[] values)
    {
        var c = new StructureConfig { Kind = kind, Body = "Test" };
        foreach (var (k, v) in values) c.Set(k, v);
        c.Clamp();
        return c;
    }

    // ---------------------------------------------------------------------------------------------------------- kepler

    [Test]
    public void Propagate_ACircularOrbit_ReturnsAfterOnePeriod_AndKeepsItsRadius()
    {
        double mu = 3.986e14, r = 7e6, v = Math.Sqrt(mu / r), period = 2.0 * Math.PI * Math.Sqrt(r * r * r / mu);
        UniversalKepler.Propagate(mu, new Double3(r, 0, 0), new Double3(0, v, 0), period, out Double3 p, out Double3 w);
        Assert.That((p - new Double3(r, 0, 0)).Length, Is.LessThan(1e-3 * r * 1e-3));
        UniversalKepler.Propagate(mu, new Double3(r, 0, 0), new Double3(0, v, 0), period / 4, out Double3 q, out _);
        Assert.That(q.Length, Is.EqualTo(r).Within(1e-6));
        Assert.That(q.Y, Is.EqualTo(r).Within(1.0), "a quarter turn");
    }

    [Test]
    public void Propagate_KeepsEnergyAndAngularMomentum_ForEllipsesAndHyperbolas()
    {
        double mu = 1.327e20;
        foreach (double speedFactor in new[] { 0.6, 1.0, 1.3, 1.6 })
        {
            Double3 r0 = new Double3(1.5e11, 2e10, 1e10), v0 = new Double3(-3000, 0.0, 500).Normalized() * (speedFactor * Math.Sqrt(mu / r0.Length));
            v0 = v0 + new Double3(0, speedFactor * 20000.0, 0);
            double e0 = 0.5 * v0.LengthSquared - mu / r0.Length, h0 = Double3.Cross(r0, v0).Length;
            foreach (double dt in new[] { 1e5, 3e6, -2e6, 1e8 })
            {
                UniversalKepler.Propagate(mu, r0, v0, dt, out Double3 r, out Double3 v);
                Assert.That(0.5 * v.LengthSquared - mu / r.Length, Is.EqualTo(e0).Within(Math.Abs(e0) * 1e-7), $"energy, speed {speedFactor}, dt {dt}");
                Assert.That(Double3.Cross(r, v).Length, Is.EqualTo(h0).Within(h0 * 1e-7), $"angular momentum, speed {speedFactor}, dt {dt}");
            }
        }
    }

    [Test]
    public void Propagate_BackThenForward_ReturnsToTheStart()
    {
        double mu = 1.327e20;
        Double3 r0 = new Double3(1.5e11, 0, 0), v0 = new Double3(0, 30000, 2000);
        UniversalKepler.Propagate(mu, r0, v0, -7.3e6, out Double3 r1, out Double3 v1);
        UniversalKepler.Propagate(mu, r1, v1, 7.3e6, out Double3 r2, out _);
        Assert.That((r2 - r0).Length, Is.LessThan(10.0));
    }

    [Test]
    public void Propagate_WithRepulsion_PushesOutward_AndWithNoGravityGoesStraight()
    {
        Double3 r0 = new Double3(1e11, 0, 0), v0 = new Double3(0, 1000, 0);
        UniversalKepler.Propagate(0.0, r0, v0, 1e5, out Double3 r, out _);
        Assert.That((r - (r0 + v0 * 1e5)).Length, Is.LessThan(1.0));
        UniversalKepler.Propagate(-1e18, r0, v0, 1e6, out Double3 pushed, out _);
        Assert.That(pushed.X, Is.GreaterThan(r0.X + 1e3));
    }

    // ------------------------------------------------------------------------------------------------------ evaporation

    static Evaporation.Inputs HD209458b(double left = 1.0) => new Evaporation.Inputs
    {
        PlanetMass = 0.69 * MJup, PlanetRadius = 1.38 * RJup, StarMass = 1.1 * MSun, StarRadius = 1.16 * 6.957e8, StarTemperature = 6065,
        StarLuminosity = 1.6 * Evaporation.SolarLuminosity, Distance = 0.047 * Au, AtmosphereLeft = left, CoreMassFraction = 0.05, CoreDensity = 5500,
        StarAgeGyr = 4, Efficiency = 0.15, BondAlbedo = 0.1, EnvelopeMu = 1.3
    };

    [Test]
    public void EnergyLimitedLoss_OfHD209458b_IsOfTheOrderOf1e7ToE8KgPerSecond()
    {
        EvaporationState s = Evaporation.Evaluate(HD209458b());
        Assert.That(s.EnergyLimited, Is.InRange(1e7, 1e8), "published estimates: about 1e10 g/s");
        Assert.That(s.XuvFlux, Is.InRange(0.5, 20.0), "W/m^2, 500 to 20000 erg/cm^2/s");
        Assert.That(s.ErkaevK, Is.InRange(0.4, 0.9));
    }

    [Test]
    public void EnergyLimitedLoss_FollowsItsFormula()
    {
        double expected = 0.2 * Math.PI * 3.0 * Math.Pow(1e8, 3) / (UniversalKepler.G * 1e27 * 0.5);
        Assert.That(Evaporation.EnergyLimitedRate(0.2, 3.0, 1e8, 1e27, 0.5), Is.EqualTo(expected).Within(expected * 1e-12));
        Assert.That(Evaporation.EnergyLimitedRate(0.2, 6.0, 1e8, 1e27, 0.5), Is.EqualTo(2.0 * expected).Within(expected * 1e-12), "linear in the flux");
    }

    [Test]
    public void ErkaevFactor_IsOneFarInsideTheLobe_AndFallsToItsFloorAtTheLobeEdge()
    {
        Assert.That(Evaporation.ErkaevFactor(1000.0, 1.0), Is.EqualTo(1.0).Within(2e-3));
        Assert.That(Evaporation.ErkaevFactor(1.0, 1.0), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(Evaporation.ErkaevFactor(3.0, 1.0), Is.EqualTo(1.0 - 0.5 + 0.5 / 27.0).Within(1e-12));
    }

    [Test]
    public void LobeRadius_IsEggletons_AndAPlanetCloseEnoughOverflows()
    {
        Assert.That(Evaporation.LobeFraction(1.0), Is.EqualTo(0.3789).Within(1e-4));
        // A Jupiter at 0.01 AU from a Sun fills its lobe; at 0.05 AU it does not.
        Evaporation.Inputs near = HD209458b(), far = HD209458b();
        near.Distance = 0.012 * Au;
        Assert.That(Evaporation.Evaluate(near).FillFactor, Is.GreaterThan(Evaporation.Evaluate(far).FillFactor));
        Assert.That(Evaporation.Evaluate(near).FillFactor, Is.GreaterThan(0.8));
        Assert.That(Evaporation.Evaluate(far).FillFactor, Is.LessThan(0.5));
        Assert.That(Evaporation.Evaluate(near).RocheOverflow, Is.GreaterThan(Evaporation.Evaluate(far).RocheOverflow));
    }

    [Test]
    public void ABareCore_LosesDust_ByLangmuirSublimation_RisingSteeplyWithTemperature()
    {
        double cool = Evaporation.SublimationRate(1800.0, 3e6), warm = Evaporation.SublimationRate(2200.0, 3e6), hot = Evaporation.SublimationRate(3000.0, 3e6);
        Assert.That(warm, Is.GreaterThan(10.0 * cool));
        Assert.That(hot, Is.GreaterThan(warm));
        Assert.That(warm, Is.InRange(1e6, 1e11), "KIC 12557548 b is losing about an Earth mass per Gyr, ~2e8 kg/s");
        Evaporation.Inputs bare = HD209458b(0.0);
        EvaporationState s = Evaporation.Evaluate(bare);
        Assert.That(s.EnergyLimited, Is.EqualTo(0.0));
        Assert.That(s.Total, Is.EqualTo(s.DustSublimation));
        Assert.That(s.Radius, Is.LessThan(0.4 * 1.38 * RJup), "the bare core is much smaller than the gas giant");
    }

    [Test]
    public void GrainBeta_OfSubmicronSilicateAroundTheSun_IsAboutOneHalf_AndFallsWithSize()
    {
        double beta = Evaporation.GrainBeta(Evaporation.SolarLuminosity, MSun, 3000.0, 0.3e-6);
        Assert.That(beta, Is.InRange(0.4, 0.9));
        Assert.That(Evaporation.GrainBeta(Evaporation.SolarLuminosity, MSun, 3000.0, 3e-6), Is.EqualTo(beta / 10.0).Within(1e-9));
    }

    [Test]
    public void XuvFraction_IsSaturatedWhenYoung_AndTheSunsToday()
    {
        Assert.That(Evaporation.XuvFraction(0.05), Is.EqualTo(Math.Pow(10.0, -3.5)).Within(1e-12));
        Assert.That(Evaporation.XuvFraction(4.6), Is.InRange(1e-6, 1e-5));
    }

    [Test]
    public void TheAtmosphere_IsStrippedOverTime_Deterministically()
    {
        Evaporation.Inputs i = HD209458b();
        i.PlanetMass = 0.05 * MJup;   // a Saturn-mass planet close in loses its envelope in a few Gyr
        i.PlanetRadius = 0.8 * RJup;
        double a = Evaporation.AtmosphereAfter(i, 1.0, 1e15), b = Evaporation.AtmosphereAfter(i, 1.0, 1e15), later = Evaporation.AtmosphereAfter(i, 1.0, 1e17);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.LessThan(1.0));
        Assert.That(later, Is.LessThan(a));
        Assert.That(Evaporation.AtmosphereAfter(i, 1.0, 0.0), Is.EqualTo(1.0));
    }

    // ----------------------------------------------------------------------------------------------------------- tails

    static StructureContext HotJupiter(double ut) => Context(0.69 * MJup, 1.38 * RJup, 0.047, ut, 1.1);

    [Test]
    public void TheTail_IsDeterministic_AndMovesWithGameTime()
    {
        StructureConfig c = Config(StructureKind.EvaporatingPlanet, ("count", 3000));
        var a = new ParticleSet();
        var b = new ParticleSet();
        Structures.Generate(c, HotJupiter(5e5), a);
        Structures.Generate(c, HotJupiter(5e5), b);
        Assert.That(a.Count, Is.EqualTo(b.Count));
        for (int i = 0; i < a.Count; i++) Assert.That(a.Items[i].Position, Is.EqualTo(b.Items[i].Position));
        var later = new ParticleSet();
        Structures.Generate(c, HotJupiter(5e5 + 1.5e5), later);
        int moved = 0;
        for (int i = 0; i < Math.Min(a.Count, later.Count); i++) if ((a.Items[i].Position - later.Items[i].Position).Length > 1e6) moved++;
        Assert.That(moved, Is.GreaterThan(a.Count / 2));
    }

    [Test]
    public void TheGasTail_StreamsAwayFromTheStar_AndTheClumpsFromL1RunAheadOfThePlanetAndFromL2Behind()
    {
        // Gas pushed by beta ends further from the star than gas that is not (the star is at -x here).
        double MeanX(double beta)
        {
            StructureConfig gas = Config(StructureKind.EvaporatingPlanet, ("count", 2000), ("clumpShare", 0), ("gasShare", 1), ("haloShare", 0), ("gasBeta", beta), ("spreadAngle", 40), ("gasTailHours", 3));
            var set = new ParticleSet();
            Structures.Generate(gas, HotJupiter(3e5), set);
            return Enumerable.Range(0, set.Count).Average(i => set.Items[i].Position.X);
        }
        Assert.That(MeanX(8.0), Is.GreaterThan(MeanX(0.0) + 1e8), "away from the star");

        // Clumps from L1 only run ahead (along the planet's motion, +y), from L2 only fall behind.
        StructureConfig l1 = Config(StructureKind.EvaporatingPlanet, ("count", 1500), ("clumpShare", 1), ("gasShare", 0), ("haloShare", 0), ("l1Share", 1), ("l2Share", 0),
            ("clumpBeta", 0), ("spreadAngle", 5), ("speedSpread", 0.1), ("ejectionSpeed", 2000));
        StructureConfig l2 = l1.Clone();
        l2.Set("l1Share", 0.0);
        l2.Set("l2Share", 1.0);
        var s1 = new ParticleSet();
        var s2 = new ParticleSet();
        Structures.Generate(l1, HotJupiter(4e5), s1);
        Structures.Generate(l2, HotJupiter(4e5), s2);
        double y1 = Enumerable.Range(0, s1.Count).Average(i => s1.Items[i].Position.Y);
        double y2 = Enumerable.Range(0, s2.Count).Average(i => s2.Items[i].Position.Y);
        Assert.That(y1, Is.GreaterThan(0.0), "L1 clumps drift ahead");
        Assert.That(y2, Is.LessThan(0.0), "L2 clumps lag");
    }

    [Test]
    public void TheDustTail_IsCurvedByRadiationPressure_SmallerGrainsMoreSo()
    {
        double Curve(double grainUm)
        {
            StructureConfig c = Config(StructureKind.EvaporatingPlanet, ("count", 2500), ("clumpShare", 0), ("gasShare", 0), ("haloShare", 0), ("grainSizeUm", grainUm), ("ejectionSpeed", 500), ("spreadAngle", 10), ("dustTailHours", 200));
            var set = new ParticleSet();
            Structures.Generate(c, HotJupiter(2e5), set);
            // Mean offset along the star direction (anti-solar = +x) of the dust, over its offset along the orbit.
            return Enumerable.Range(0, set.Count).Average(i => set.Items[i].Position.X);
        }
        double fine = Curve(0.15), coarse = Curve(30.0);
        Assert.That(fine, Is.GreaterThan(coarse), "the finer dust is blown further from the star");
    }

    [Test]
    public void EachKindMakesFiniteParticles_AndTheToroidalPlanetNeedsPermission()
    {
        foreach (StructureKind kind in Enum.GetValues(typeof(StructureKind)))
        {
            StructureConfig c = Config(kind, ("count", 800), ("speculative", 1), ("startAgeHours", 30), ("moon", 1));
            StructureContext x = Context(MEarth, REarth, 1.0, 4e5);
            var set = new ParticleSet();
            Structures.Generate(c, x, set);
            Assert.That(set.Count, Is.GreaterThan(100), kind.ToString());
            for (int i = 0; i < set.Count; i++)
            {
                StructureParticle p = set.Items[i];
                Assert.That(double.IsNaN(p.Position.X + p.Position.Y + p.Position.Z + p.Radius), Is.False, $"{kind} #{i}");
                Assert.That(p.Radius, Is.GreaterThan(0.0));
            }
        }
        var gated = new ParticleSet();
        Structures.Generate(Config(StructureKind.ToroidalPlanet, ("count", 500)), Context(MEarth, REarth, 1.0, 0), gated);
        Assert.That(gated.Count, Is.EqualTo(0), "speculative structures are drawn only when allowed");
    }

    // ------------------------------------------------------------------------------------------------------------ others

    [Test]
    public void HitTime_OfANearlyRadialFall_IsHalfAPeriod()
    {
        double mu = 3.986e14, r0 = 4e7;
        double fall = Math.PI / 2.0 * Math.Sqrt(r0 * r0 * r0 / (2.0 * mu));
        double t = Structures.HitTime(mu, 6.371e6, new Double3(r0, 0, 0), new Double3(0, 1e-3, 0));
        Assert.That(t, Is.EqualTo(fall).Within(fall * 0.05));
        Assert.That(Structures.HitTime(mu, 6.371e6, new Double3(r0, 0, 0), new Double3(0, Math.Sqrt(mu / r0), 0)), Is.EqualTo(double.PositiveInfinity), "a circular orbit never hits");
        Assert.That(Structures.HitTime(mu, 6.371e6, new Double3(r0, 0, 0), new Double3(1e4, 3e3, 0)), Is.EqualTo(double.PositiveInfinity), "flying away");
    }

    [Test]
    public void HitTime_AgreesWithPropagation()
    {
        double mu = 3.986e14;
        Double3 r0 = new Double3(2e7, 3e6, 0), v0 = new Double3(-1500, 600, 0);
        double t = Structures.HitTime(mu, 6.371e6, r0, v0);
        Assert.That(double.IsInfinity(t), Is.False);
        UniversalKepler.Propagate(mu, r0, v0, t, out Double3 r, out _);
        Assert.That(r.Length, Is.EqualTo(6.371e6).Within(2e4));
    }

    [Test]
    public void RocheLimit_OfTheMoonAroundTheEarth_IsAbout18000Km()
    {
        Assert.That(Structures.RocheLimit(6.371e6, 5514, 3344), Is.EqualTo(1.84e7).Within(1.5e6));
    }

    [Test]
    public void ImpactDebris_CoolsAndFallsBack()
    {
        StructureConfig c = Config(StructureKind.ImpactDebris, ("count", 4000), ("ejectaSpeed", 0.5), ("initialTemperature", 3500), ("ageRate", 0), ("startAgeHours", 0.01));
        StructureContext x = Context(MEarth, REarth, 1.0, 0);
        var young = new ParticleSet();
        Structures.Generate(c, x, young);
        c.Set("startAgeHours", 400.0);
        var old = new ParticleSet();
        Structures.Generate(c, x, old);
        double Glow(ParticleSet s) => Enumerable.Range(0, s.Count).Average(i => s.Items[i].Emit.Y);
        Assert.That(Glow(old), Is.LessThan(Glow(young)), "cooler");
        Assert.That(old.Count, Is.LessThan(young.Count), "blobs on collision orbits have fallen in");
        for (int i = 0; i < old.Count; i++) Assert.That(old.Items[i].Position.Length, Is.GreaterThanOrEqualTo(REarth * 0.99));
    }

    [Test]
    public void ASuperPuff_HasAMeanDensityBelowTenthOfWater_AndAnExtendedEnvelope()
    {
        // Kepler-51 d: about 5.7 Earth masses, 9.7 Earth radii.
        Assert.That(Structures.MeanDensity(5.7 * MEarth, 9.7 * REarth), Is.LessThan(100.0));
        Assert.That(Structures.MeanDensity(MEarth, REarth), Is.GreaterThan(5000.0));
        StructureConfig c = Config(StructureKind.SuperPuff, ("count", 3000), ("puffRadius", 5), ("atmTemperature", 3500));
        var set = new ParticleSet();
        Structures.Generate(c, Context(5.7 * MEarth, 2.5 * REarth, 0.5, 0), set);
        double far = Enumerable.Range(0, set.Count).Max(i => set.Items[i].Position.Length);
        Assert.That(far, Is.GreaterThan(1.5 * 2.5 * REarth), "the haze reaches well beyond the core");
    }

    [Test]
    public void AMoonInsideTheRocheLimit_IsAnIntactBallBeforeAndARingAfter()
    {
        StructureConfig c = Config(StructureKind.RingingMoon, ("count", 3000), ("disruptStartHours", 10), ("disruptSpanHours", 100), ("ageRate", 0), ("startAgeHours", 0), ("moonSizeKm", 150), ("moonDistanceRoche", 0.7));
        StructureContext x = Context(5.68e26, 5.8e7, 9.5, 0, 1.0);
        var before = new ParticleSet();
        Structures.Generate(c, x, before);
        Double3 mean = Double3.Zero;
        for (int i = 0; i < before.Count; i++) mean = mean + before.Items[i].Position;
        mean = mean / before.Count;
        double radius = Enumerable.Range(0, before.Count).Max(i => (before.Items[i].Position - mean).Length);
        Assert.That(radius, Is.LessThan(1.6e5), "a ball 150 km in radius");
        c.Set("startAgeHours", 3000.0);
        var after = new ParticleSet();
        Structures.Generate(c, x, after);
        double spread = Enumerable.Range(0, after.Count).Select(i => after.Items[i].Position.Length).Max() - Enumerable.Range(0, after.Count).Select(i => after.Items[i].Position.Length).Min();
        Assert.That(spread, Is.GreaterThan(5e6), "stretched round the planet");
    }

    [Test]
    public void ARubblePile_HoldsTogetherBelowItsBreakUpSpin_AndSheds_AboveIt()
    {
        StructureContext x = Context(5e11, 250.0, 1.0, 0);   // a 250 m rubble pile
        StructureConfig calm = Config(StructureKind.RubblePile, ("count", 2000), ("spinFraction", 0.6), ("spinUpYears", 1e9), ("ageRate", 0), ("startAgeHours", 100));
        var held = new ParticleSet();
        Structures.Generate(calm, x, held);
        Assert.That(Enumerable.Range(0, held.Count).Max(i => held.Items[i].Position.Length), Is.LessThan(250.0 * 1.001));
        StructureConfig fast = Config(StructureKind.RubblePile, ("count", 2000), ("spinFraction", 1.15), ("spinUpYears", 1e9), ("ageRate", 0), ("startAgeHours", 100), ("reaccrete", 0));
        var shed = new ParticleSet();
        Structures.Generate(fast, x, shed);
        int outside = Enumerable.Range(0, shed.Count).Count(i => shed.Items[i].Position.Length > 250.0 * 1.2);
        Assert.That(outside, Is.GreaterThan(50), "boulders from the equator have left");
    }

    [Test]
    public void SelfShadowing_DarkensWhatLiesBehindDensePartsAndTheBody()
    {
        var set = new ParticleSet();
        for (int i = 0; i < 300; i++) set.Add(new Double3(100.0 + i * 0.01, 0.0, 0.0), 5.0, new Double3(1, 1, 1), Double3.Zero, 1.0, 0f);   // a thick stack towards the star
        set.Add(new Double3(-100.0, 0.0, 0.0), 5.0, new Double3(1, 1, 1), Double3.Zero, 1.0, 0f);                                          // behind it
        StructureLight.ApplyShadows(set, Double3.UnitX, 1.0, 0.0);
        double front = set.Items[299].Reflect.X, back = set.Items[300].Reflect.X;
        Assert.That(front, Is.GreaterThan(0.9));
        Assert.That(back, Is.LessThan(0.2));
        var eclipsed = new ParticleSet();
        eclipsed.Add(new Double3(-50.0, 1.0, 0.0), 1.0, new Double3(1, 1, 1), Double3.Zero, 1.0, 0f);
        StructureLight.ApplyShadows(eclipsed, Double3.UnitX, 0.0, 20.0);
        Assert.That(eclipsed.Items[0].Reflect.X, Is.EqualTo(0.0), "behind the body, in its shadow");
    }

    [Test]
    public void L2_LiesBeyondThePlanet_AtAboutTheHillRadius()
    {
        double q = 1e-3, hill = Math.Pow(q / 3.0, 1.0 / 3.0);
        Assert.That(Structures.L2Distance(q), Is.EqualTo(hill).Within(0.05 * hill));
    }

    // ---------------------------------------------------------------------------------------------------------- settings

    [Test]
    public void EverySetting_HasAUniqueKey_AValidRange_AndEveryPresetParses()
    {
        var keys = new HashSet<string>();
        foreach (StructureParam p in StructureSettings.Params)
        {
            Assert.That(keys.Add(p.Key), Is.True, "duplicate " + p.Key);
            Assert.That(p.Default, Is.InRange(p.Min, p.Max), p.Key);
            Assert.That(p.Kinds, Is.Not.EqualTo(0), p.Key);
        }
        foreach (StructureSettings.Preset preset in StructureSettings.Presets)
        {
            var issues = new List<ConfigIssue>();
            StructureConfig config = StructureSettings.Parse(preset.Node, issues);
            Assert.That(issues, Is.Empty, preset.Name);
            foreach (CfgNode n in new[] { preset.Node })
                foreach (var kv in n.Values)
                    Assert.That(kv.Key == "kind" || StructureSettings.HasParam(kv.Key) || StructureSettings.Colors.Any(c => c.Key == kv.Key), Is.True, preset.Name + ": " + kv.Key);
            var set = new ParticleSet();
            StructureConfig run = config.Clone();
            run.Set("count", 600.0);
            Structures.Generate(run, Context(MEarth, REarth, 1.0, 1e5), set);
            Assert.That(set.Count, Is.GreaterThan(0), preset.Name);
        }
        Assert.That(StructureSettings.Presets.Any(p => p.Name == "Like the reference"), Is.True);
        Assert.That(StructureSettings.Presets.Any(p => p.Name.StartsWith("Impact debris ball")), Is.True);
    }

    [Test]
    public void ASavedStructure_ReadsBackAsItWas()
    {
        StructureConfig c = Config(StructureKind.Synestia, ("count", 1234), ("extent", 7.5));
        c.Body = "Duna";
        c.Set("clumpColor", new Double3(0.2, 0.4, 0.6));
        StructureConfig back = StructureSettings.Parse(StructureSettings.ToNode(c));
        Assert.That(back.Body, Is.EqualTo("Duna"));
        Assert.That(back.Kind, Is.EqualTo(StructureKind.Synestia));
        Assert.That(back.N("count"), Is.EqualTo(1234));
        Assert.That(back.N("extent"), Is.EqualTo(7.5));
        Assert.That(back.C("clumpColor"), Is.EqualTo(new Double3(0.2, 0.4, 0.6)));
    }
}
