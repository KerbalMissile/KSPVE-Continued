using System;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;
using KSPVE.Pure.Fluids;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Fluids;

public class AtmosphereTests
{
    private const double A = 7.0e7;

    private static FlowParameters Forced(double hours, ulong seed, double drag = 1.5e6, double power = 0.1, int lmin = 12, int lmax = 16) => new FlowParameters
    {
        Radius = A,
        Omega = 2.0 * Math.PI / (hours * 3600.0),
        DragTime = drag,
        ForcingPower = power,
        ForcingLMin = lmin,
        ForcingLMax = lmax,
        HyperEfoldSteps = 6.0,
        Seed = seed
    };

    // ---------------------------------------------------------------- energy, jets

    [Test]
    public void TheStirring_PutsEnergyInAtTheRequestedPower()
    {
        // From rest, without drag, the energy grows at the stirring power (the nonlinear term only moves it between scales).
        var p = Forced(5.0, 4, drag: 0.0, power: 0.1);
        p.HyperEfoldSteps = 0.0;
        var model = new BarotropicModel(21, p);
        while (model.Time < 4.0e5) model.Step(Math.Min(model.CourantDt(0.5, 2000.0), 2000.0));
        double expected = 0.1 * model.Time;
        Assert.That(model.Energy(), Is.EqualTo(expected).Within(0.3 * expected));
    }

    [Test]
    public void ADampedForcedFlow_ReachesAStatisticallySteadyEnergy()
    {
        var model = new BarotropicModel(21, Forced(5.0, 5, drag: 1.0e6, power: 0.1));
        var early = new System.Collections.Generic.List<double>();
        var late = new System.Collections.Generic.List<double>();
        while (model.Time < 1.2e7)
        {
            model.Step(Math.Min(model.CourantDt(0.5, 3000.0), 3000.0));
            if (model.Steps % 25 != 0) continue;
            if (model.Time > 4.0e6 && model.Time < 8.0e6) early.Add(model.Energy());
            else if (model.Time > 8.0e6) late.Add(model.Energy());
        }
        double a = Mean(early), b = Mean(late);
        Assert.That(b / a, Is.InRange(0.6, 1.6), "mean energy of the two halves of the steady state");
        Assert.That(model.RmsSpeed(), Is.InRange(20.0, 1500.0));
        Assert.That(model.IsFinite());
    }

    private static double Mean(System.Collections.Generic.List<double> v)
    {
        double s = 0.0;
        foreach (double x in v) s += x;
        return v.Count == 0 ? 0.0 : s / v.Count;
    }

    /// <summary>The share of a freely evolving flow's energy that ends up in the zonal mean after 4e6 s, started from small-scale eddies.</summary>
    private static double ZonalShareOfDecayingTurbulence(double hours, ulong seed)
    {
        var p = new FlowParameters { Radius = A, Omega = 2.0 * Math.PI / (hours * 3600.0), DragTime = 0.0, ForcingPower = 0.0, HyperEfoldSteps = 6.0, Seed = seed };
        var model = new BarotropicModel(31, p);
        var rng = new FluidRandom(seed * 17);
        for (int m = 0; m <= 22; m++)
            for (int l = Math.Max(m, 18); l <= 22; l++) model.SetCoefficient(l, m, rng.NextGaussian(), m == 0 ? 0.0 : rng.NextGaussian());
        double scale = 150.0 / model.RmsSpeed();
        for (int i = 0; i < model.ZRe.Length; i++) { model.ZRe[i] *= scale; model.ZIm[i] *= scale; }
        while (model.Time < 4.0e6) model.Step(Math.Min(model.CourantDt(0.5, 3000.0), 3000.0));
        double zonal = 0.0;
        for (int l = 1; l <= model.K; l++)
        {
            double z = model.ZRe[model.Layout.Index(l, 0)];
            zonal += 0.25 * z * z * A * A / (l * (l + 1.0));
        }
        return zonal / model.Energy();
    }

    [Test]
    public void RotationOrganisesTheFlowIntoZonalJets_MoreSoTheFasterItSpins()
    {
        // The beta effect turns small-scale eddies into Rossby waves and an inverse cascade that stops near the Rhines scale,
        // leaving zonal jets: the share of the energy in the zonal mean grows with the spin (Rhines 1975).
        double none = 0.0, slow = 0.0, medium = 0.0, fast = 0.0;
        for (ulong seed = 3; seed <= 5; seed++)
        {
            none += ZonalShareOfDecayingTurbulence(1e9, seed) / 3.0;
            slow += ZonalShareOfDecayingTurbulence(80.0, seed) / 3.0;
            medium += ZonalShareOfDecayingTurbulence(20.0, seed) / 3.0;
            fast += ZonalShareOfDecayingTurbulence(5.0, seed) / 3.0;
        }
        Assert.That(medium, Is.GreaterThan(1.4 * slow), $"20 h ({medium:0.00}) against 80 h ({slow:0.00})");
        Assert.That(fast, Is.GreaterThan(2.0 * slow), $"5 h ({fast:0.00}) against 80 h ({slow:0.00})");
        Assert.That(fast, Is.GreaterThan(2.0 * none), $"5 h ({fast:0.00}) against no rotation ({none:0.00})");
    }

    [Test]
    public void AForcedFlowOnAFastSpinner_FormsSeveralZonalJets()
    {
        var model = new BarotropicModel(21, Forced(5.0, 3));
        double sum = 0.0;
        int n = 0;
        while (model.Time < 6.0e6)
        {
            model.Step(Math.Min(model.CourantDt(0.5, 3000.0), 3000.0));
            if (model.Time > 3.5e6 && model.Steps % 20 == 0) { sum += Jets.Count(model.ZonalMeanWind(), 0.3); n++; }
        }
        Assert.That(sum / n, Is.GreaterThanOrEqualTo(2.0), "eastward jets");
        Assert.That(Jets.ZonalCentroid(model), Is.GreaterThan(3.0));
    }

    [Test]
    public void TheRhinesDegree_GrowsAsTheSquareRootOfTheRotationRate()
    {
        double w = 2.0 * Math.PI / (10.0 * 3600.0);
        double l1 = FluidPhysics.RhinesDegree(A, w, 300.0), l4 = FluidPhysics.RhinesDegree(A, 4.0 * w, 300.0);
        Assert.That(l4 / l1, Is.EqualTo(2.0).Within(1e-9));
        double faster = FluidPhysics.RhinesDegree(A, w, 1200.0);
        Assert.That(faster / l1, Is.EqualTo(0.5).Within(1e-9), "and falls as the square root of the wind");
    }

    [Test]
    public void JetCount_FindsTheMaximaOfAProfile()
    {
        var u = new double[64];
        for (int i = 0; i < u.Length; i++) u[i] = Math.Sin(4.0 * Math.PI * (i + 0.5) / u.Length);   // two eastward and two westward lobes
        Assert.That(Jets.Count(u, 0.3), Is.EqualTo(2));
        Assert.That(Jets.Count(new double[64], 0.3), Is.EqualTo(0));
    }

    // ---------------------------------------------------------------- determinism

    private static AtmosphereSimulation SmallSim(ulong seed, FluidKind kind = FluidKind.L, double kelvin = 1500.0)
    {
        var profile = new FluidProfile { Body = "t", Kind = kind, KindSet = true, Temperature = kelvin, Resolution = 16, TracerLongitudes = 64, Seed = seed };
        return new AtmosphereSimulation(profile, A, 6.0 * 3600.0, 1000.0);
    }

    [Test]
    public void TheSpinUp_IsDeterministicForASeed()
    {
        AtmosphereSimulation a = SmallSim(11), b = SmallSim(11), c = SmallSim(12);
        Assert.That(a.SpinUp(), Is.True);
        Assert.That(b.SpinUp(), Is.True);
        Assert.That(c.SpinUp(), Is.True);
        Assert.That(b.Model.ZRe, Is.EqualTo(a.Model.ZRe));
        Assert.That(b.Tracers.Theta, Is.EqualTo(a.Tracers.Theta));
        Assert.That(b.Tracers.Q[1], Is.EqualTo(a.Tracers.Q[1]));
        Assert.That(c.Model.ZRe, Is.Not.EqualTo(a.Model.ZRe));
        // And advancing is deterministic too.
        a.AdvanceTo(5.0e4, 100);
        b.AdvanceTo(5.0e4, 100);
        Assert.That(b.Tracers.Theta, Is.EqualTo(a.Tracers.Theta));
        Assert.That(b.Time, Is.EqualTo(a.Time));
    }

    [Test]
    public void ASpunUpAtmosphere_HasPatchyCloudsAndBoundedFields()
    {
        AtmosphereSimulation sim = SmallSim(2);
        sim.SpinUp();
        Assert.That(sim.Model.IsFinite());
        for (int d = 0; d < AtmosphereSimulation.DeckCount; d++)
        {
            double min = double.MaxValue, max = double.MinValue, sum = 0.0;
            foreach (double q in sim.Tracers.Q[d]) { min = Math.Min(min, q); max = Math.Max(max, q); sum += q; }
            Assert.That(min, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(max, Is.LessThanOrEqualTo(sim.Forcing.MaxCondensate + 1e-9));
            Assert.That(max - min, Is.GreaterThan(0.1), "deck " + d + " has gaps and thick patches");
            Assert.That(sum / sim.Tracers.Q[d].Length, Is.InRange(0.3, 2.0));
        }
        var fields = new float[sim.Tracers.NLon * sim.Tracers.NLat * 4];
        sim.WriteFields(fields);
        foreach (float f in fields) Assert.That(float.IsNaN(f), Is.False);
    }

    // ---------------------------------------------------------------- tracers

    [Test]
    public void ATracerInASolidBodyRotation_IsCarriedAroundWithoutNewExtremes()
    {
        // A solid rotation (the l = 1, m = 0 harmonic) turns a pattern rigidly; the semi-Lagrangian scheme must carry a
        // smooth blob with it without making new extremes and without losing much of its total.
        var p = new FlowParameters { Radius = A, Omega = 0.0, DragTime = 0.0, ForcingPower = 0.0, HyperEfoldSteps = 0.0 };
        var model = new BarotropicModel(16, p);
        model.SetCoefficient(1, 0, 4.0e-5, 0.0);
        var grid = new TracerGrid(model, 128, 1, new FluidRandom(1), 8, 10);
        for (int i = 0; i < grid.NLat; i++)
            for (int k = 0; k < grid.NLon; k++)
            {
                double dLon = Math.Atan2(Math.Sin(grid.Longitude(k) - 1.0), Math.Cos(grid.Longitude(k) - 1.0));
                double dLat = grid.Latitude(i) - 0.4;
                grid.Q[0][i * grid.NLon + k] = Math.Exp(-(dLon * dLon + dLat * dLat) / (2.0 * 0.35 * 0.35));
            }
        var forcing = new TracerForcing { RadiativeTime = 1e30, HeatingRate = 0.0, DeckKelvin = new[] { 1000.0 }, DeckScale = new[] { 1e30 }, CondensationTime = new[] { 1e30 }, DeckShare = new[] { 0.0 } };
        double Total()
        {
            double s = 0.0;
            for (int i = 0; i < grid.NLat; i++)
                for (int k = 0; k < grid.NLon; k++) s += grid.Q[0][i * grid.NLon + k] * Math.Cos(grid.Latitude(i));
            return s;
        }
        double before = Total();
        for (int s = 0; s < 120; s++) grid.Step(1500.0, forcing);
        double max = 0.0, min = 0.0;
        foreach (double q in grid.Q[0]) { max = Math.Max(max, q); min = Math.Min(min, q); }
        Assert.That(max, Is.LessThanOrEqualTo(1.0 + 1e-9));
        Assert.That(min, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(Total(), Is.EqualTo(before).Within(0.06 * before), "total amount (some numerical diffusion is expected)");
        Assert.That(max, Is.GreaterThan(0.6), "the blob is not smeared away");
    }

    // ---------------------------------------------------------------- the look

    [Test]
    public void ABlackbodyOf1500K_IsRed()
    {
        Rgb c = DwarfSpectrum.Blackbody(1500.0);
        Assert.That(c.R, Is.GreaterThan(c.G * 4.0));
        Assert.That(c.G, Is.GreaterThan(c.B));
        Assert.That(c.B / c.R, Is.LessThan(0.01));
        // Through the alkali wings above the upper deck it stays red.
        Rgb seen = DwarfSpectrum.Emission(1500.0, 0.3, 1.0, 1.0);
        Assert.That(seen.R, Is.GreaterThan(seen.G * 3.0));
    }

    [Test]
    public void TheVisibleLuminanceOfACoolBody_IsTheRealMinuscule_AndRisesSteeplyWithTemperature()
    {
        double l1500 = DwarfSpectrum.LuminanceRelativeToSun(1500.0);
        Assert.That(l1500, Is.InRange(2e-6, 8e-6), "about four millionths of the Sun's disc");
        Assert.That(DwarfSpectrum.LuminanceRelativeToSun(DwarfSpectrum.SolarTemperature), Is.EqualTo(1.0).Within(1e-9));
        double last = 0.0;
        for (double t = 600.0; t <= 3000.0; t += 300.0)
        {
            double l = DwarfSpectrum.LuminanceRelativeToSun(t);
            Assert.That(l, Is.GreaterThan(last));
            last = l;
        }
        Assert.That(DwarfSpectrum.LuminanceRelativeToSun(1800.0) / l1500, Is.GreaterThan(10.0), "more than an order of magnitude from 1500 to 1800 K");
    }

    [Test]
    public void AlkaliWings_RemoveMostOfTheLight_AndAreStrongestAtTheSodiumLine()
    {
        Rgb through = DwarfSpectrum.Emission(1800.0, 1.5, 1.0, 1.0);
        Rgb clear = DwarfSpectrum.Emission(1800.0, 1.5, 0.0, 1.0);
        Assert.That(through.Luminance, Is.LessThan(0.5 * clear.Luminance), "the wings remove most of the light");
        Assert.That(through.R, Is.GreaterThan(through.B * 3.0), "and what is left is still red");
        Assert.That(DwarfSpectrum.GasDepth(589.3, 1.0, 1.0), Is.GreaterThan(DwarfSpectrum.GasDepth(700.0, 1.0, 1.0)), "strongest in the sodium D line");
        Assert.That(DwarfSpectrum.GasDepth(600.0, 1.0, 0.0), Is.LessThan(DwarfSpectrum.GasDepth(600.0, 1.0, 1.0)));
    }

    [Test]
    public void DeeperDecks_AreHotter_AndTheLutDecodes()
    {
        foreach (FluidKind kind in new[] { FluidKind.L, FluidKind.T, FluidKind.Y, FluidKind.Giant, FluidKind.HotJupiter })
        {
            DeckSpec[] decks = Decks.For(kind);
            Assert.That(decks.Length, Is.EqualTo(AtmosphereSimulation.DeckCount));
            for (int i = 1; i < decks.Length; i++) Assert.That(decks[i].TemperatureFactor, Is.GreaterThan(decks[i - 1].TemperatureFactor), kind + " deck " + i);
            Assert.That(Decks.DeepFactorFor(kind), Is.GreaterThan(decks[decks.Length - 1].TemperatureFactor));
        }
        var lut = new EmissionLut(FluidKind.L, Decks.For(FluidKind.L), 2.0);
        Assert.That(lut.Rows, Is.EqualTo(4 * EmissionLut.MuBins));
        // A hotter texel is brighter, and decodes to a red colour for an L dwarf's top deck.
        int cool = EmissionLut.Columns / 3, hot = EmissionLut.Columns * 2 / 3;
        EmissionLut.Decode(lut.Data, (EmissionLut.MuBins - 1) * EmissionLut.Columns + cool, out double r0, out double g0, out _);
        EmissionLut.Decode(lut.Data, (EmissionLut.MuBins - 1) * EmissionLut.Columns + hot, out double r1, out double g1, out _);
        Assert.That(r1, Is.GreaterThan(r0));
        Assert.That(r1, Is.GreaterThan(g1));
        Assert.That(DwarfSpectrum.LuminanceRelativeToSun(EmissionLut.KelvinOf(hot)), Is.GreaterThan(DwarfSpectrum.LuminanceRelativeToSun(EmissionLut.KelvinOf(cool))));
        _ = g0;
    }

    [Test]
    public void TheDeckTemperatureScale_FollowsClausiusClapeyron()
    {
        // Theta = R T^2 / L: water (L/R ~ 6100 K) at 270 K responds over about 12 K; iron (L/R 4.2e4 K) at 1800 K over 77 K.
        var water = Decks.For(FluidKind.Giant)[2];
        Assert.That(water.TemperatureScale(270.0), Is.EqualTo(270.0 * 270.0 / 6100.0).Within(1e-9));
        var iron = Decks.For(FluidKind.L)[1];
        Assert.That(iron.TemperatureScale(1800.0), Is.EqualTo(1800.0 * 1800.0 / 4.2e4).Within(1e-9));
    }

    // ---------------------------------------------------------------- hot Jupiters and kinds

    [Test]
    public void AHotJupiter_HasAHotDaySideAnEastwardEquatorialJetAndAGlowingNightSide()
    {
        var profile = new FluidProfile { Body = "hj", Kind = FluidKind.HotJupiter, KindSet = true, TidallyLocked = true, Temperature = 2000.0, Resolution = 16, TracerLongitudes = 64, Seed = 4 };
        var sim = new AtmosphereSimulation(profile, A, 3.0 * 86400.0, 10.0, 0.0);
        Assert.That(sim.SpinUp(), Is.True);
        TracerGrid t = sim.Tracers;
        double day = 0.0, night = 0.0;
        int nd = 0, nn = 0;
        for (int i = 0; i < t.NLat; i++)
            for (int k = 0; k < t.NLon; k++)
            {
                double c = Math.Cos(t.Latitude(i)) * Math.Cos(t.Longitude(k));
                if (Math.Abs(t.Latitude(i)) > 0.5) continue;
                if (c > 0.6) { day += t.Theta[i * t.NLon + k]; nd++; }
                else if (c < -0.6) { night += t.Theta[i * t.NLon + k]; nn++; }
            }
        Assert.That(day / nd - night / nn, Is.GreaterThan(60.0), "day-night temperature contrast, K");
        double[] u = sim.Model.ZonalMeanWind();
        double equator = u[u.Length / 2];
        Assert.That(equator, Is.GreaterThan(300.0), "eastward super-rotation, m/s");
        // The night side is still hot enough to glow: the deep layer at the night-side temperature is far above a cool dwarf's.
        double nightKelvin = sim.DeepKelvin + night / nn;
        Assert.That(DwarfSpectrum.LuminanceRelativeToSun(nightKelvin), Is.GreaterThan(DwarfSpectrum.LuminanceRelativeToSun(1000.0)));
    }

    [Test]
    public void TheKindFollowsTheTemperature()
    {
        Assert.That(Decks.KindForTemperature(1800.0), Is.EqualTo(FluidKind.L));
        Assert.That(Decks.KindForTemperature(900.0), Is.EqualTo(FluidKind.T));
        Assert.That(Decks.KindForTemperature(350.0), Is.EqualTo(FluidKind.Y));
        Assert.That(Decks.KindForTemperature(124.0), Is.EqualTo(FluidKind.Giant));
    }

    // ---------------------------------------------------------------- config

    [Test]
    public void AProfile_ReadsItsKeys_AndAKindFollowsTheTemperatureUnlessNamed()
    {
        CfgNode node = CfgParser.Parse("BODY { body = X\n type = T\n temperature = 900\n rotationPeriodHours = 4.5\n resolution = 31\n tracerResolution = 200\n seed = 12\n forcing = 1.5 }")[0];
        FluidProfile p = FluidProfile.From(node, new FluidProfile());
        Assert.That(p.Body, Is.EqualTo("X"));
        Assert.That(p.Kind, Is.EqualTo(FluidKind.T));
        Assert.That(p.Temperature, Is.EqualTo(900.0));
        Assert.That(p.RotationPeriodHours, Is.EqualTo(4.5));
        Assert.That(p.Resolution, Is.EqualTo(31));
        Assert.That(p.TracerLongitudes, Is.EqualTo(256), "rounded up to a power of two");
        Assert.That(p.Seed, Is.EqualTo(12UL));
        Assert.That(p.Forcing, Is.EqualTo(1.5));

        CfgNode warm = CfgParser.Parse("BODY { body = Y\n temperature = 1700 }")[0];
        Assert.That(FluidProfile.From(warm, new FluidProfile()).Kind, Is.EqualTo(FluidKind.L));
        CfgNode cool = CfgParser.Parse("BODY { body = Z\n temperature = 700 }")[0];
        Assert.That(FluidProfile.From(cool, new FluidProfile()).Kind, Is.EqualTo(FluidKind.T));
        CfgNode locked = CfgParser.Parse("BODY { body = H\n temperature = 1800\n tidallyLocked = True }")[0];
        Assert.That(FluidProfile.From(locked, new FluidProfile()).Kind, Is.EqualTo(FluidKind.HotJupiter));
    }

    [Test]
    public void TheSettings_RoundTripThroughTheirUserNode()
    {
        FluidSettings.Reset();
        FluidSettings.Boost = 4096.0;
        FluidSettings.View = FluidView.ThermalInfrared;
        FluidSettings.GasGiants = true;
        FluidSettings.WorkShare = 0.6;
        CfgNode node = FluidSettings.ToNode();
        FluidSettings.Reset();
        Assert.That(FluidSettings.Boost, Is.EqualTo(1.0));
        FluidSettings.Read(node);
        Assert.That(FluidSettings.Boost, Is.EqualTo(4096.0));
        Assert.That(FluidSettings.View, Is.EqualTo(FluidView.ThermalInfrared));
        Assert.That(FluidSettings.GasGiants, Is.True);
        Assert.That(FluidSettings.WorkShare, Is.EqualTo(0.6).Within(1e-9));
        FluidSettings.Reset();
    }

    [Test]
    public void TheStirringPowerAndDragTime_AreWhatTheHeatFluxSuggests()
    {
        // 3e-4 of sigma T^4 over the 10 bar layer: about 0.09 W/kg for an L dwarf at 1500 K, a ten-thousandth of that at 400 K.
        double p1500 = FluidPhysics.StirringPower(1500.0, 1000.0);
        Assert.That(p1500, Is.InRange(0.05, 0.15));
        Assert.That(FluidPhysics.StirringPower(750.0, 1000.0) / p1500, Is.EqualTo(1.0 / 16.0).Within(1e-9));
        Assert.That(FluidPhysics.DragTime(1500.0, 1000.0), Is.InRange(1e6, 1e7));
        Assert.That(FluidPhysics.DragTime(800.0, 1000.0), Is.GreaterThan(FluidPhysics.DragTime(1500.0, 1000.0)));
    }
}
