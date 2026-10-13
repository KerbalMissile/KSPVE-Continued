using System;
using System.Collections.Generic;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Aurora;

public class MagnetosphereTests
{
    private static SolarWindState Wind(double speedKms, double densityCm3, double btNt, double bzNt, double particles = 1.0)
    {
        SolarWindState w = SolarWindState.Quiet(SpaceWeather.SpaceWeather.ReferenceDistance, SpaceWeather.SpaceWeather.ReferenceDistance);
        w.Speed = speedKms * 1e3; w.Density = densityCm3 * 1e6; w.FieldStrength = btNt * 1e-9; w.FieldNorth = bzNt * 1e-9; w.EnergeticParticles = particles;
        return w;
    }

    private static Dipole Earth => new Dipole { EquatorialField = 31e-6, Tilt = 9.3 * Math.PI / 180.0, Polarity = -1.0 };

    [Test]
    public void StandOff_OfEarthInTheQuietWind_IsAboutTenRadii()
    {
        double r = Magnetopause.StandOff(31e-6, SolarWindState.Quiet(1.0, 1.0).DynamicPressure);
        Assert.That(r, Is.InRange(9.5, 11.5), "the observed subsolar magnetopause is at 10-11 Earth radii");
    }

    [Test]
    public void StandOff_FallsAsThePressureRisesToTheSixthRoot_AndRisesWithTheFieldToTheThird()
    {
        double p = SolarWindState.Quiet(1.0, 1.0).DynamicPressure;
        double r1 = Magnetopause.StandOff(31e-6, p), r2 = Magnetopause.StandOff(31e-6, 64.0 * p), r3 = Magnetopause.StandOff(8.0 * 31e-6, p);
        Assert.That(r2 / r1, Is.EqualTo(0.5).Within(1e-9), "64 times the pressure halves it (p^-1/6)");
        Assert.That(r3 / r1, Is.EqualTo(2.0).Within(1e-9), "8 times the field doubles it (B^1/3)");
    }

    [Test]
    public void SouthwardField_ErodesTheDayside()
    {
        Assert.That(Magnetopause.Erosion(0.0), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(Magnetopause.Erosion(-15e-9), Is.LessThan(0.9));
        Assert.That(Magnetopause.Erosion(+15e-9), Is.GreaterThan(1.0).And.LessThan(1.15));
    }

    [Test]
    public void Kp_IsLowInTheQuietWind_AndHighInAStrongSouthwardStorm()
    {
        double quiet = Activity.Kp(t => Wind(400, 5, 5, 0), 1e6, -1.0);
        double storm = Activity.Kp(t => Wind(800, 15, 30, -25), 1e6, -1.0);
        double northward = Activity.Kp(t => Wind(400, 5, 5, 5), 1e6, -1.0);
        Assert.That(quiet, Is.InRange(0.8, 2.5));
        Assert.That(storm, Is.GreaterThanOrEqualTo(7.0));
        Assert.That(northward, Is.LessThan(quiet), "a northward interplanetary field does not drive the magnetosphere");
    }

    [Test]
    public void Newell_CouplingPeaksForASouthwardField()
    {
        double north = Activity.Newell(500e3, 10e-9, 0.0), east = Activity.Newell(500e3, 10e-9, Math.PI / 2), south = Activity.Newell(500e3, 10e-9, Math.PI);
        Assert.That(north, Is.EqualTo(0.0).Within(1e-9));
        Assert.That(south, Is.GreaterThan(east).And.GreaterThan(1e4), "about 1e4-1e5 (km/s)^(4/3) nT^(2/3) in storms");
    }

    [Test]
    public void TheOval_IsAt65To70Degrees_WhenQuiet_AndMovesToAbout50ForAStrongStorm()
    {
        SolarWindState quietWind = Wind(400, 5, 5, 0);
        double kpQuiet = Activity.Kp(t => quietWind, 1e6, -1.0);
        OvalState quiet = OvalModel.SolarDriven(Earth, quietWind, kpQuiet);
        SolarWindState stormWind = Wind(700, 15, 20, -15);
        double kpStorm = Activity.Kp(t => stormWind, 1e6, -1.0);
        OvalState storm = OvalModel.SolarDriven(Earth, stormWind, kpStorm);
        Assert.That(quiet.NightLatitudeDegrees, Is.InRange(64.0, 70.0));
        Assert.That(storm.NightLatitudeDegrees, Is.InRange(44.0, 54.0));
        Assert.That(storm.StandOff, Is.LessThan(quiet.StandOff), "the magnetosphere is compressed in a storm");
        Assert.That(storm.NightLatitudeDegrees, Is.LessThan(quiet.NightLatitudeDegrees - 10.0));
    }

    [Test]
    public void TheOval_IsNearerThePoleByDay_ThanByNight()
    {
        OvalState oval = OvalModel.SolarDriven(Earth, Wind(400, 5, 5, 0), 2.0);
        Assert.That(oval.ColatitudeAt(Math.PI), Is.LessThan(oval.ColatitudeAt(0.0)), "noon (nu = pi) is nearer the pole: the oval is shifted to the night side");
        Assert.That(oval.ColatitudeAt(0.0), Is.EqualTo(oval.NightColatitude).Within(1e-12));
    }

    [Test]
    public void FieldLines_AreDipolar()
    {
        Assert.That(Dipole.InvariantLatitude(1.0), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(Dipole.InvariantLatitude(6.6) * 180.0 / Math.PI, Is.EqualTo(67.0).Within(0.2), "L = 6.6 comes down at 67 degrees");
        Assert.That(Dipole.LShell(Dipole.InvariantLatitude(4.0)), Is.EqualTo(4.0).Within(1e-9));
        // The footprint of a point on the surface is itself; of one higher up on the same line is the same.
        double colat = 0.4;
        Assert.That(Dipole.FootprintColatitude(1.0, colat), Is.EqualTo(colat).Within(1e-12));
        double r = 1.02;
        double lat = Math.PI / 2 - colat;
        double L = 1.0 / Math.Pow(Math.Sin(colat), 2.0);
        double colatAtR = Math.Asin(Math.Sqrt(r / L));
        Assert.That(Dipole.FootprintColatitude(r, colatAtR), Is.EqualTo(colat).Within(1e-9), "the same field line");
        _ = lat;
    }

    [Test]
    public void ACorotatingOval_BarelyMoves()
    {
        var jupiter = new Dipole { EquatorialField = 417e-6, Polarity = 1.0 };
        OvalState a = OvalModel.Corotating(jupiter, Wind(400, 0.2, 1, 0), 16.0, 0.8), b = OvalModel.Corotating(jupiter, Wind(800, 1.0, 2, -1), 16.0, 0.8);
        Assert.That(b.NightColatitude, Is.EqualTo(a.NightColatitude).Within(1e-12));
        Assert.That(a.StandOff, Is.GreaterThan(40.0), "Jupiter's magnetosphere is some 50-100 radii across");
    }
}

public class SubstormTests
{
    [Test]
    public void Substorms_AreDeterministic()
    {
        for (double ut = 0; ut < 3e5; ut += 913.0)
        {
            SubstormState a = Substorms.At(ut, 3.0, 42UL), b = Substorms.At(ut, 3.0, 42UL);
            Assert.That(b.Phase, Is.EqualTo(a.Phase));
            Assert.That(b.Progress, Is.EqualTo(a.Progress));
            Assert.That(b.OnsetUt, Is.EqualTo(a.OnsetUt));
        }
    }

    [Test]
    public void ASubstorm_RunsThroughGrowthExpansionAndRecovery_InOrder()
    {
        SubstormPhase last = SubstormPhase.None;
        bool seenGrowth = false, seenExpansion = false, seenRecovery = false;
        double onset = double.NaN;
        for (double ut = 0; ut < 86400.0 * 3; ut += 20.0)
        {
            SubstormState s = Substorms.At(ut, 4.0, 7UL);
            if (s.Phase == SubstormPhase.Growth) seenGrowth = true;
            if (s.Phase == SubstormPhase.Expansion) { seenExpansion = true; if (double.IsNaN(onset)) onset = s.OnsetUt; }
            if (s.Phase == SubstormPhase.Recovery) seenRecovery = true;
            if (s.Phase == SubstormPhase.Expansion && last == SubstormPhase.Growth) Assert.That(Math.Abs(ut - s.OnsetUt), Is.LessThan(40.0), "the expansion starts at the onset");
            last = s.Phase;
        }
        Assert.That(seenGrowth && seenExpansion && seenRecovery, Is.True);
    }

    [Test]
    public void StormsBringMoreSubstorms()
    {
        int Onsets(double kp)
        {
            int n = 0;
            SubstormPhase last = SubstormPhase.None;
            for (double ut = 0; ut < 86400.0 * 60; ut += 60.0)
            {
                SubstormState s = Substorms.At(ut, kp, 99UL);
                if (s.Phase == SubstormPhase.Expansion && last != SubstormPhase.Expansion) n++;
                last = s.Phase;
            }
            return n;
        }
        int quiet = Onsets(0.5), storm = Onsets(8.0);
        Assert.That(storm, Is.GreaterThan(quiet * 2));
        Assert.That(quiet, Is.InRange(60 * 24 / 14, 60 * 24 / 4), "one every few hours when quiet");
    }

    [Test]
    public void TheOnset_IsNearMidnight_AndTheSurgeTravelsTowardDusk()
    {
        for (double ut = 0; ut < 86400.0 * 10; ut += 600.0)
        {
            SubstormState s = Substorms.At(ut, 4.0, 5UL);
            if (s.Phase == SubstormPhase.None) continue;
            Assert.That(Math.Abs(s.OnsetAngle), Is.LessThan(0.7), "within an hour or two of midnight");
            Assert.That(s.SurgeAngle, Is.LessThanOrEqualTo(s.OnsetAngle + 1e-12), "westward is towards dusk, negative");
            Assert.That(s.EastAngle, Is.GreaterThanOrEqualTo(s.OnsetAngle - 1e-12));
        }
    }
}

public class EmissionTests
{
    private static readonly EmissionLut EarthLut = EmissionLut.Build(AtmosphereProfile.Earth(), Composition.N2O2);

    [Test]
    public void OnEarth_EmissionHeightsAreTheMeasuredOnes()
    {
        double Km(EmissionRow r) => EarthLut.PeakHeight[(int)r] / 1000.0;
        Assert.That(Km(EmissionRow.Arc), Is.InRange(98.0, 118.0), "557.7 nm arcs peak near 110 km");
        Assert.That(Km(EmissionRow.ArcHard), Is.InRange(82.0, 100.0), "energetic electrons reach lower");
        Assert.That(Km(EmissionRow.SoftArc), Is.InRange(170.0, 300.0), "630.0 nm: 200-400 km, red tops");
        Assert.That(Km(EmissionRow.GlowGreen), Is.InRange(93.0, 99.0));
        Assert.That(Km(EmissionRow.GlowSodium), Is.InRange(89.0, 95.0));
        Assert.That(Km(EmissionRow.GlowHydroxyl), Is.InRange(84.0, 90.0));
        Assert.That(Km(EmissionRow.GlowRed), Is.InRange(250.0, 290.0));
        Assert.That(Km(EmissionRow.Sar), Is.InRange(380.0, 450.0));
        Assert.That(EarthLut.LowerEdge[(int)EmissionRow.Arc] / 1000.0, Is.InRange(90.0, 108.0), "arcs have a sharp lower border near 100 km");
    }

    [Test]
    public void EmissionHeightsFollowThePressureLevel_OnAnyAtmosphere()
    {
        // Kerbin-like: 1 atm, but a scale height of 5.6 km on a 600 km world. The glow sits at the same pressures.
        AtmosphereProfile kerbin = AtmosphereProfile.Isothermal(101325.0, 5600.0, 600000.0, 9.81, 0.029, 1000.0);
        EmissionLut lut = EmissionLut.Build(kerbin, Composition.N2O2);
        AtmosphereProfile earth = AtmosphereProfile.Earth();
        foreach (EmissionRow row in new[] { EmissionRow.GlowGreen, EmissionRow.GlowHydroxyl, EmissionRow.GlowRed, EmissionRow.Sar })
        {
            double pEarth = earth.PressureAt(EarthLut.PeakHeight[(int)row]), pKerbin = kerbin.PressureAt(lut.PeakHeight[(int)row]);
            Assert.That(Math.Log(pKerbin / pEarth), Is.InRange(-0.5, 0.5), row + " is at the same pressure level");
        }
        Assert.That(lut.PeakHeight[(int)EmissionRow.Arc], Is.Not.EqualTo(EarthLut.PeakHeight[(int)EmissionRow.Arc]).Within(5000.0), "and so at a different height");
    }

    [Test]
    public void Quenching_RemovesTheRedLineFromThickAir_ButNotTheGreenHigh()
    {
        var red = new EmissionLine(630.0, 1.0, 2.0e-11, 107.0);
        var green = new EmissionLine(557.7, 1.0, 1.0e-13, 0.76);
        Assert.That(EmissionModel.QuenchEfficiency(red, 0.03, 190.0), Is.LessThan(1e-3));
        Assert.That(EmissionModel.QuenchEfficiency(red, 1e-7, 1000.0), Is.GreaterThan(0.9));
        Assert.That(EmissionModel.QuenchEfficiency(green, 0.03, 190.0), Is.InRange(0.3, 0.9), "557.7 is half quenched near 100 km");
        Assert.That(EmissionModel.QuenchEfficiency(green, 1.0, 190.0), Is.LessThan(0.1));
    }

    private static Rgb Peak(EmissionRow row)
    {
        int s = (int)((EarthLut.PeakHeight[(int)row] - EarthLut.HeightLow) / (EarthLut.HeightHigh - EarthLut.HeightLow) * EarthLut.Samples);
        return new Rgb(EarthLut.Rgb[(int)row, s, 0], EarthLut.Rgb[(int)row, s, 1], EarthLut.Rgb[(int)row, s, 2]);
    }

    [Test]
    public void TheColoursAreTheLinesOnes()
    {
        Rgb arc = Peak(EmissionRow.Arc), green = Peak(EmissionRow.GlowGreen), red = Peak(EmissionRow.SoftArc), sodium = Peak(EmissionRow.GlowSodium);
        Rgb proton = Peak(EmissionRow.Proton), steve = Peak(EmissionRow.Steve), sar = Peak(EmissionRow.Sar), hard = Peak(EmissionRow.ArcHard);
        Assert.That(arc.G, Is.GreaterThan(arc.R).And.GreaterThan(arc.B * 3.0), "green");
        Assert.That(green.G, Is.GreaterThan(green.R).And.GreaterThan(green.B * 5.0));
        Assert.That(red.R, Is.GreaterThan(red.G * 2.0).And.GreaterThan(red.B * 2.0), "630 nm is red");
        Assert.That(sar.R, Is.GreaterThan(sar.G * 5.0), "SAR arcs are pure red");
        Assert.That(sodium.R, Is.GreaterThan(sodium.G), "589 nm is yellow-orange");
        Assert.That(proton.R, Is.GreaterThan(proton.G * 2.0), "H-alpha is red");
        Assert.That(steve.R, Is.GreaterThan(steve.G).And.LessThan(steve.R * 2.0));
        Assert.That(steve.B, Is.GreaterThan(steve.G), "mauve: red and blue above green");
        Assert.That(hard.B / hard.G, Is.GreaterThan(arc.B / arc.G), "energetic arcs are bluer (N2+) than soft ones");
    }

    [Test]
    public void ARayleigh_IsConvertedToRadianceAndLuminanceByTheDefinition()
    {
        Assert.That(AuroraPhotometry.PhotonRadiancePerRayleigh, Is.EqualTo(7.9577e8).Within(1e5));
        // 250 R of 557.7 nm (airglow): 250 x 7.96e8 photons s^-1 m^-2 sr^-1 x 3.56e-19 J = 7.1e-8 W m^-2 sr^-1, 4.7e-5 cd/m^2.
        double radiance = AuroraPhotometry.EnergyRadiance(250.0, 557.7);
        Assert.That(radiance, Is.EqualTo(7.09e-8).Within(1e-9));
        double luminance = AuroraPhotometry.Luminance(250.0, 557.7);
        Assert.That(luminance, Is.EqualTo(4.7e-5).Within(3e-6));
        // 100 kR (IBC III) gives some 0.02 cd/m^2.
        Assert.That(AuroraPhotometry.Luminance(100000.0, 557.7), Is.EqualTo(0.019).Within(0.003));
        // The RGB of a line has its luminance.
        Rgb rgb = AuroraPhotometry.LineLuminanceRgb(250.0, 557.7);
        Assert.That(rgb.Luminance, Is.EqualTo(luminance).Within(luminance * 0.08));
        Assert.That(rgb.R, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(rgb.B, Is.GreaterThanOrEqualTo(0.0));
    }

    [Test]
    public void TheSceneUnit_IsASunlitWhiteSurface()
    {
        Assert.That(AuroraPhotometry.SceneUnitLuminance, Is.EqualTo(126573.0 / Math.PI).Within(500.0), "about 40,000 cd/m^2");
        double scene = AuroraPhotometry.LineSceneRgb(10000.0, 557.7).G;
        Assert.That(scene, Is.GreaterThan(0.0).And.LessThan(1e-6), "an aurora is far below a lit surface before the night exposure");
    }

    [Test]
    public void NightExposure_OfAnAuroraPhotograph_IsAboutFiveMillionTimesDaylight()
    {
        double gain = NightExposure.Gain(1.8, 10.0, 6400.0);
        Assert.That(gain, Is.InRange(3e6, 8e6));
        Assert.That(NightExposure.Gain(16.0, 0.01, 100.0), Is.EqualTo(1.0).Within(1e-9), "sunny 16 is the reference");
    }

    [Test]
    public void TheColourMatchingFunctions_AreTheStandardObservers()
    {
        Assert.That(CieColor.LuminousEfficiency(555.0), Is.EqualTo(1.0).Within(0.02));
        Assert.That(CieColor.LuminousEfficiency(630.0), Is.EqualTo(0.265).Within(0.03));
        Assert.That(CieColor.LuminousEfficiency(450.0), Is.EqualTo(0.038).Within(0.02));
    }

    [Test]
    public void EnergyIsConserved_ByTheDepositionProfile()
    {
        // The integral of the deposition over ln p (ds / s) is 1: all the beam's energy lands.
        double sum = 0.0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
        {
            double s = (i + 0.5) / n;
            sum += EmissionModel.DepositionPerLnP(s) / s / n;
        }
        Assert.That(sum, Is.EqualTo(1.0).Within(0.01));
        Assert.That(EmissionModel.RangeColumnMass(3.0), Is.EqualTo(3.1e-3).Within(3e-4), "a 3 keV electron stops in about 3e-4 g/cm^2");
    }
}

public class AuroraBodyTests
{
    private const string Config = @"
KSPVE_AURORA
{
    BODY
    {
        name = Earth
        fieldStrength = 31
        tilt = 9.3
        tiltLongitude = 287.3
        polarity = -1
        composition = N2O2
    }
    BODY
    {
        name = Mars
        composition = CO2
        CRUSTAL
        {
            latitude = -53
            longitude = -178
            radius = 500
            strength = 1
        }
    }
    BODY
    {
        name = Jupiter
        fieldStrength = 417
        polarity = 1
        oval = corotation
        ovalColatitude = 16
        composition = H2He
        MOON
        {
            name = Io
            brightness = 8
        }
    }
}";

    private static AuroraBodyModel Model(string name)
    {
        foreach (CfgNode root in CfgParser.Parse(Config))
            foreach (CfgNode child in root.Children)
                if (child.GetLast("name") == name) return AuroraBodyModel.FromConfig(child, new List<ConfigIssue>());
        throw new InvalidOperationException(name);
    }

    private static SolarWindState Quiet => SolarWindState.Quiet(SpaceWeather.SpaceWeather.ReferenceDistance, SpaceWeather.SpaceWeather.ReferenceDistance);

    [Test]
    public void TheConfigIsRead()
    {
        AuroraBodyModel earth = Model("Earth"), mars = Model("Mars"), jupiter = Model("Jupiter");
        Assert.That(earth.Field.EquatorialField, Is.EqualTo(31e-6).Within(1e-12));
        Assert.That(earth.Oval, Is.EqualTo(OvalMode.Solar));
        Assert.That(mars.HasField, Is.False);
        Assert.That(mars.Crustal.Count, Is.EqualTo(1));
        Assert.That(mars.Composition, Is.EqualTo(Composition.CO2));
        Assert.That(jupiter.Oval, Is.EqualTo(OvalMode.Corotation));
        Assert.That(jupiter.Moons[0].Name, Is.EqualTo("Io"));
        Assert.That(jupiter.Field.Polarity, Is.EqualTo(1.0));
    }

    [Test]
    public void TheSolution_IsDeterministic()
    {
        AuroraBodyModel earth = Model("Earth");
        Double3 sun = new Double3(0.3, 0.2, -0.93).Normalized();
        AuroraFrame a = AuroraSolver.Solve(earth, 6371e3, t => Quiet, 1.234e7, sun), b = AuroraSolver.Solve(earth, 6371e3, t => Quiet, 1.234e7, sun);
        Assert.That(b.Arc, Is.EqualTo(a.Arc));
        Assert.That(b.Oval.NightColatitude, Is.EqualTo(a.Oval.NightColatitude));
        Assert.That(b.Storm.Phase, Is.EqualTo(a.Storm.Phase));
        Assert.That(a.Oval.StandOff, Is.InRange(9.5, 11.5));
        Assert.That(a.OvalLatitudeDegrees, Is.InRange(63.0, 70.0));
    }

    [Test]
    public void AMagnetisedBody_ShowsTheKindsItsActivityCalls_AndAFieldlessOneOnlyInParticleEvents()
    {
        AuroraBodyModel earth = Model("Earth"), mars = Model("Mars");
        Double3 sun = Double3.UnitX;
        SolarWindState storm = Quiet; storm.Speed = 800e3; storm.Density = 15e6; storm.FieldStrength = 30e-9; storm.FieldNorth = -25e-9;
        AuroraFrame quietEarth = AuroraSolver.Solve(earth, 6371e3, t => Quiet, 1e7, sun), stormEarth = AuroraSolver.Solve(earth, 6371e3, t => storm, 1e7, sun);
        Assert.That(stormEarth.Oval.Kp, Is.GreaterThan(quietEarth.Oval.Kp + 4.0));
        Assert.That(quietEarth.Sar, Is.EqualTo(0.0), "no SAR arcs in a quiet night");
        Assert.That(stormEarth.Sar, Is.GreaterThan(0.0));
        Assert.That(stormEarth.Arc, Is.GreaterThan(quietEarth.Arc));
        Assert.That(stormEarth.InStorm, Is.True);

        SolarWindState northward = Quiet; northward.FieldStrength = 8e-9; northward.FieldNorth = 8e-9;
        Assert.That(AuroraSolver.Solve(earth, 6371e3, t => northward, 1e7, sun).ThetaPresent, Is.True, "northward field: polar cap arcs");

        AuroraFrame quietMars = AuroraSolver.Solve(mars, 3375.8e3, t => Quiet, 1e7, sun);
        SolarWindState event1 = Quiet; event1.EnergeticParticles = 1e4;
        AuroraFrame eventMars = AuroraSolver.Solve(mars, 3375.8e3, t => event1, 1e7, sun);
        Assert.That(quietMars.Global, Is.EqualTo(0.0));
        Assert.That(eventMars.Global, Is.GreaterThan(0.1));
        Assert.That(eventMars.Crustal, Is.GreaterThan(quietMars.Crustal));
        Assert.That(eventMars.CrustalCount, Is.EqualTo(1));
        Assert.That(quietMars.GlowGreen, Is.EqualTo(250.0), "airglow is always there");
    }

    [Test]
    public void AMoonInsideAGiantsMagnetosphere_MakesAFootprintAtTheMappedLatitude()
    {
        AuroraBodyModel jupiter = Model("Jupiter");
        double radius = 66.316e6;
        var moons = new List<AuroraSolver.MoonPosition> { new AuroraSolver.MoonPosition { Name = "Io", Position = new Double3(5.9 * radius, 0.0, 0.0) } };
        AuroraFrame f = AuroraSolver.Solve(jupiter, radius, t => Quiet, 1e7, Double3.UnitX, moons);
        Assert.That(f.SpotCount, Is.EqualTo(1));
        // Io at 5.9 radii on the magnetic equator: sin^2(theta) = 1 / 5.9, a colatitude of about 24 degrees.
        Assert.That(f.Spots[0].Colatitude * 180.0 / Math.PI, Is.EqualTo(Math.Asin(Math.Sqrt(1.0 / 5.9)) * 180.0 / Math.PI).Within(3.0));
        Assert.That(f.Giant, Is.GreaterThan(0.0));
        Assert.That(f.Oval.NightColatitude * 180.0 / Math.PI, Is.EqualTo(16.0).Within(1e-9));
    }

    [Test]
    public void ABodyWithoutANode_GetsAGuessFromItsAir_AndTheSameOneEachTime()
    {
        var thin = new AuroraBodyModel.Facts { Name = "Kerbolia", Radius = 4.0e6, MolarMass = 0.043, SurfacePressure = 500.0, SurfaceGravity = 8.0, RotationPeriod = 40000.0, HasAtmosphere = true };
        AuroraBodyModel a = AuroraBodyModel.Guess(thin), b = AuroraBodyModel.Guess(thin);
        Assert.That(a.Composition, Is.EqualTo(Composition.CO2));
        Assert.That(b.Field.EquatorialField, Is.EqualTo(a.Field.EquatorialField));
        Assert.That(b.Field.Tilt, Is.EqualTo(a.Field.Tilt));
        var giant = new AuroraBodyModel.Facts { Name = "Gasbag", Radius = 5.0e7, MolarMass = 0.0022, SurfacePressure = 1e5, SurfaceGravity = 20.0, RotationPeriod = 36000.0, HasAtmosphere = true };
        AuroraBodyModel g = AuroraBodyModel.Guess(giant);
        Assert.That(g.Composition, Is.EqualTo(Composition.H2He));
        Assert.That(g.HasField, Is.True);
        Assert.That(g.Oval, Is.EqualTo(OvalMode.Corotation));
        AuroraBodyModel none = AuroraBodyModel.Guess(new AuroraBodyModel.Facts { Name = "Rock", HasAtmosphere = false });
        Assert.That(none.Aurora, Is.False);
        Assert.That(none.Airglow, Is.False);
    }
}

public class TracerTests
{
    private static SolarWindState Quiet => SolarWindState.Quiet(SpaceWeather.SpaceWeather.ReferenceDistance, SpaceWeather.SpaceWeather.ReferenceDistance);

    private static AuroraTracer EarthTracer(double ut, Double3 sun, out Double3 up)
    {
        var model = new AuroraBodyModel
        {
            Name = "Earth", Seed = AuroraBodyModel.SeedOf("Earth"),
            Field = new Dipole { EquatorialField = 31e-6, Tilt = 9.3 * Math.PI / 180.0, TiltLongitude = 287.3 * Math.PI / 180.0, Polarity = -1.0 },
            Oval = OvalMode.Solar,
        };
        AuroraFrame frame = AuroraSolver.Solve(model, 6371010.0, t => Quiet, ut, sun);
        var tracer = new AuroraTracer { F = frame, Lut = EmissionLut.Build(AtmosphereProfile.Earth(), Composition.N2O2), NightGain = 5e6 };
        tracer.Prepare();
        // Straight over the oval's midnight point.
        Double3 axis = frame.Axis;
        double colat = frame.Oval.NightColatitude;
        Double3 midnight = (-frame.Noon);
        up = (axis * Math.Cos(colat) + midnight * Math.Sin(colat)).Normalized();
        return tracer;
    }

    private static Rgb LookUp(AuroraTracer t, Double3 up, double pitchDegrees)
    {
        Double3 east = Double3.Cross(Double3.UnitY, up).Normalized(), north = Double3.Cross(up, east).Normalized();
        double p = pitchDegrees * Math.PI / 180.0;
        Double3 dir = (north * Math.Cos(p) + up * Math.Sin(p)).Normalized();
        Rgb sum = new Rgb(0, 0, 0);
        const int samples = 64;
        for (int i = 0; i < samples; i++) sum = sum + t.Trace(up * (6371010.0 + 100.0), dir, 1e9, (i + 0.5) / samples, 0.002);
        return sum / samples;
    }

    [Test]
    public void UnderTheOvalAtNight_TheSkyGlows_AndInDaylightItDoesNot()
    {
        Double3 sunBelow = new Double3(0.2, 0.1, -0.97).Normalized();
        AuroraTracer night = EarthTracer(1.0e7, sunBelow, out Double3 up);
        // Put the sun on the observer's side of the planet for the day case.
        AuroraTracer day = EarthTracer(1.0e7, up, out Double3 up2);
        Rgb n = LookUp(night, up, 60.0), d = LookUp(day, up2, 60.0);
        Assert.That(n.G, Is.GreaterThan(0.0));
        Assert.That(n.G / Math.Max(d.G, 1e-30), Is.GreaterThan(1e4), "the night exposure applies only in the dark");
    }

    [Test]
    public void TheTracer_IsDeterministic_AndFinite()
    {
        AuroraTracer t = EarthTracer(1.0e7, new Double3(0.2, 0.1, -0.97).Normalized(), out Double3 up);
        for (double pitch = 5; pitch < 90; pitch += 17)
        {
            Rgb a = LookUp(t, up, pitch), b = LookUp(t, up, pitch);
            Assert.That(b.G, Is.EqualTo(a.G));
            Assert.That(double.IsNaN(a.R + a.G + a.B) || double.IsInfinity(a.R + a.G + a.B), Is.False);
            Assert.That(Math.Min(a.R, Math.Min(a.G, a.B)), Is.GreaterThanOrEqualTo(0.0));
        }
    }

    [Test]
    public void ARayThroughTheBody_SeesNothingBeyondIt()
    {
        AuroraTracer t = EarthTracer(1.0e7, new Double3(0.2, 0.1, -0.97).Normalized(), out Double3 up);
        Rgb below = t.Trace(up * (6371010.0 + 100.0), -up, 1e9, 0.5, 0.002);
        Assert.That(below.R + below.G + below.B, Is.EqualTo(0.0), "looking down at the ground");
    }

    [Test]
    public void TheNightExposureGain_ScalesTheNightSky_Linearly()
    {
        Double3 sun = new Double3(0.2, 0.1, -0.97).Normalized();
        AuroraTracer a = EarthTracer(1.0e7, sun, out Double3 up), b = EarthTracer(1.0e7, sun, out _);
        b.NightGain = 2.0 * a.NightGain;
        b.Prepare();
        Rgb ra = LookUp(a, up, 50.0), rb = LookUp(b, up, 50.0);
        Assert.That(rb.G / ra.G, Is.EqualTo(2.0).Within(0.01));
    }
}
