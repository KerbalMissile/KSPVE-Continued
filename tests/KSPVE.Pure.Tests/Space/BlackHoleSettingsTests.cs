using System;
using System.Collections.Generic;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class BlackHoleSettingsTests
{
    [Test]
    public void Defaults_PlaceTheDiskFromTheIscoOutward()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.Schema.Resolve(null, issues);
        Assert.That(BlackHoleSettings.Schema.NodeName, Is.EqualTo("KSPVE_BLACKHOLE"));
        Assert.That(v.Get(BlackHoleSettings.Body), Is.EqualTo(string.Empty));
        Assert.That(v.Get(BlackHoleSettings.HorizonRadius), Is.EqualTo(0.0), "0 means the body's own radius");
        Assert.That(v.Get(BlackHoleSettings.DiskInner), Is.EqualTo(3.0), "ISCO = 6M = 3 horizon radii");
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.GreaterThan(v.Get(BlackHoleSettings.DiskInner)));
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ParsesAKcalbelohStyleNode()
    {
        var node = new CfgNode("KSPVE_BLACKHOLE")
            .Add("body", " Kcalbeloh ")
            .Add("diskOuter", "12.5")
            .Add("normalY", "10")
            .Add("steps", "256");
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.Schema.Resolve(node, issues);
        Assert.That(issues, Is.Empty);
        Assert.That(v.Get(BlackHoleSettings.Body), Is.EqualTo("Kcalbeloh"));
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(12.5));
        Assert.That(v.Get(BlackHoleSettings.NormalY), Is.EqualTo(10.0));
        Assert.That(v.Get(BlackHoleSettings.Steps), Is.EqualTo(256));
    }

    [Test]
    public void EmptyBodyName_IsReported()
    {
        var issues = new List<ConfigIssue>();
        BlackHoleSettings.Schema.Resolve(new CfgNode("KSPVE_BLACKHOLE").Add("body", "   "), issues);
        Assert.That(issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void UserOverrides_ApplyOnlyToTheSameBody_AndWin()
    {
        CfgNode baseNode = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kcalbeloh").Add("peakTemperature", "6000").Add("brightness", "0.9");
        CfgNode user = new CfgNode(BlackHoleSettings.UserNodeName)
            .AddChild(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Other").Add("peakTemperature", "1000"))
            .AddChild(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kcalbeloh").Add("peakTemperature", "40000"));
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.Schema.Resolve(BlackHoleSettings.ApplyUserOverrides(baseNode, user), issues);
        Assert.That(v.Get(BlackHoleSettings.PeakTemperature), Is.EqualTo(40000.0), "the player's tweak wins");
        Assert.That(v.Get(BlackHoleSettings.Brightness), Is.EqualTo(0.9), "untouched values come from the config");
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void UserOverrides_NoUserNode_LeavesTheConfigAlone()
    {
        CfgNode baseNode = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kcalbeloh").Add("peakTemperature", "6000");
        Assert.That(BlackHoleSettings.ApplyUserOverrides(baseNode, null), Is.SameAs(baseNode));
    }

    [Test]
    public void ColorPresets_AreWithinTheSettingRanges()
    {
        Assert.That(BlackHoleSettings.ColorPresets, Is.Not.Empty);
        foreach (ColorPreset preset in BlackHoleSettings.ColorPresets)
        {
            Assert.That(preset.PeakTemperature, Is.InRange(1000.0, 100000.0), preset.Name);
            Assert.That(preset.ColorSaturation, Is.InRange(0.0, 4.0), preset.Name);
            Assert.That(preset.Brightness, Is.InRange(0.0, 20.0), preset.Name);
        }
    }

    [Test]
    public void TheHorizon_IsTheBodysRadius_UnlessTheBodyIsTheShadow()
    {
        Assert.That(BlackHoleSettings.HorizonMeters(0.0, 3.2e9, false), Is.EqualTo(3.2e9));
        // The shadow of a Schwarzschild hole is sqrt(27)/2 = 2.598 horizon radii across: the body's size, with bodyIsShadow.
        double horizon = BlackHoleSettings.HorizonMeters(0.0, 3.2e9, true);
        Assert.That(horizon * 3.0 * Math.Sqrt(3.0) / 2.0, Is.EqualTo(3.2e9).Within(1e-6).Percent);
        Assert.That(BlackHoleSettings.HorizonMeters(4.8e9, 3.2e9, true), Is.EqualTo(4.8e9), "an explicit horizonRadius wins");
        SettingsValues v = BlackHoleSettings.Schema.Resolve(null, new List<ConfigIssue>());
        Assert.That(v.Get(BlackHoleSettings.BodyIsShadow), Is.False, "by default the body's radius is the horizon");
    }

    [Test]
    public void Streaks_FadeInSlowlyAsTheViewRisesOverTheDisk()
    {
        Assert.That(BlackHoleSettings.StreakView(1.0, 0.0), Is.EqualTo(0.0), "edge-on: the cloudy disk");
        Assert.That(BlackHoleSettings.StreakView(1.0, 10.0), Is.EqualTo(0.0));
        Assert.That(BlackHoleSettings.StreakView(1.0, 47.5), Is.EqualTo(0.5).Within(1e-9), "half way up, half the look");
        Assert.That(BlackHoleSettings.StreakView(1.0, 90.0), Is.EqualTo(1.0), "straight down: all of it");
        Assert.That(BlackHoleSettings.StreakView(0.4, 90.0), Is.EqualTo(0.4).Within(1e-9));
        Assert.That(BlackHoleSettings.StreakView(1.0, 20.0, 0.0, 40.0), Is.EqualTo(0.5).Within(1e-9), "streaksFrom and streaksFull move the fade");
        Assert.That(BlackHoleSettings.StreakView(1.0, 45.0, 0.0, 40.0), Is.EqualTo(1.0));
        double previous = 0.0;
        for (double e = 0.0; e <= 90.0; e += 1.0)
        {
            double v = BlackHoleSettings.StreakView(1.0, e);
            Assert.That(v, Is.GreaterThanOrEqualTo(previous));
            Assert.That(v - previous, Is.LessThan(0.03), $"no sudden jump at {e} degrees");
            previous = v;
        }
        SettingsValues defaults = BlackHoleSettings.Schema.Resolve(null, new List<ConfigIssue>());
        Assert.That(defaults.Get(BlackHoleSettings.DiskStreaks), Is.EqualTo(0.0), "off unless a config asks for it");
    }

    [Test]
    public void BigDisks_GetMoreSteps_NormalOnesNone()
    {
        Assert.That(BlackHoleSettings.ExtraSteps(14.0), Is.EqualTo(0), "the disks the renderer was tuned for are unchanged");
        Assert.That(BlackHoleSettings.ExtraSteps(BlackHoleSettings.DetailRadius), Is.EqualTo(0));
        int big = BlackHoleSettings.ExtraSteps(2026.0);   // 130 AU round Kcalbeloh
        Assert.That(big, Is.InRange(400, 500));
        Assert.That(BlackHoleSettings.ExtraSteps(20000.0), Is.GreaterThan(big), "more for bigger, but only logarithmically");
        SettingsValues v = BlackHoleSettings.Schema.Resolve(null, new List<ConfigIssue>());
        Assert.That(v.Get(BlackHoleSettings.OuterGlow), Is.EqualTo(0.0), "physical unless a config asks");
    }

    [Test]
    public void HeaviestAbove_PicksTheBlackHole_NotOrdinaryStars()
    {
        // Kerbol, real Sun, Kcalbeloh, a gas giant.
        var masses = new List<double> { 1.7565e28, 1.989e30, 1.178305e31, 4.2e24 };
        Assert.That(BlackHoleSettings.HeaviestAbove(masses, 5e30), Is.EqualTo(2));
        Assert.That(BlackHoleSettings.HeaviestAbove(new List<double> { 1.7565e28, 1.989e30 }, 5e30), Is.EqualTo(-1), "no black hole: no disk on the Sun");
        Assert.That(BlackHoleSettings.HeaviestAbove(new List<double>(), 5e30), Is.EqualTo(-1));
    }

    [Test]
    public void JetTwistRate_GivesTheRequestedTurnsFromBaseToTip()
    {
        double rate = BlackHoleSettings.JetTwistRate(4.0, 60.0);
        double turns = rate * (Math.Pow(60.0, 0.45) - Math.Pow(1.1, 0.45)) / (2.0 * Math.PI);
        Assert.That(turns, Is.EqualTo(4.0).Within(1e-12));
        Assert.That(BlackHoleSettings.JetTwistRate(0.0, 60.0), Is.EqualTo(0.0));
    }

    [Test]
    public void JetFlowRate_MovesThePatternAtTheRequestedSpeedAtMidLength()
    {
        double rate = BlackHoleSettings.JetFlowRate(30.0, 60.0);
        double speedAtMid = rate * Math.Pow(30.0, 0.55) / 0.45;      // dz/dt from zeta = z^0.45
        Assert.That(speedAtMid, Is.EqualTo(30.0).Within(1e-9));
        Assert.That(rate * Math.Pow(60.0, 0.55) / 0.45, Is.GreaterThan(30.0), "the flow speeds up toward the tip");
    }

    [Test]
    public void JetDefaults_AreTheSpiralCurrentStyle()
    {
        SettingsValues v = BlackHoleSettings.Schema.Resolve(null, new List<ConfigIssue>());
        Assert.That(v.Get(BlackHoleSettings.JetSpiral), Is.EqualTo(1.0));
        Assert.That(v.Get(BlackHoleSettings.JetTurns), Is.EqualTo(4.0));
        Assert.That(v.Get(BlackHoleSettings.JetFlow), Is.EqualTo(30.0));
        Assert.That(v.Get(BlackHoleSettings.JetWidth), Is.EqualTo(0.9));
    }

    [Test]
    public void JetBound_HoldsTheWholeJetIncludingItsEdgeAtTheTip()
    {
        double bound = BlackHoleSettings.JetBound(60.0, 0.9, 1.0);
        double tipEdge = 3.0 * 0.9 * Math.Pow(60.0 + BlackHoleSettings.JetBase, 0.55);
        Assert.That(bound, Is.GreaterThan(Math.Sqrt(60.0 * 60.0 + tipEdge * tipEdge)));
        Assert.That(BlackHoleSettings.JetBound(60.0, 0.9, 1.0), Is.GreaterThan(BlackHoleSettings.JetBound(60.0, 0.9, 0.0)), "the spiral's thick base widens it");
    }

    [Test]
    public void PulsarBeam_KeepsItsTiltFromTheSpinAxis_AndSweepsRoundIt()
    {
        var spin = new Double3(0.3, 1.0, -0.2);
        Double3 n = spin.Normalized();
        Double3 first = BlackHoleSettings.PulsarBeamAxis(spin, 35.0, 0.0);
        foreach (double phase in new[] { 0.0, 1.0, 2.5, 4.0 })
        {
            Double3 beam = BlackHoleSettings.PulsarBeamAxis(spin, 35.0, phase);
            Assert.That(beam.Length, Is.EqualTo(1.0).Within(1e-12));
            Assert.That(Math.Acos(Double3.Dot(beam, n)) * 180.0 / Math.PI, Is.EqualTo(35.0).Within(1e-9), "on the cone");
        }
        Double3 half = BlackHoleSettings.PulsarBeamAxis(spin, 35.0, Math.PI);
        Assert.That(Double3.Dot(first - n * Double3.Dot(first, n), half - n * Double3.Dot(half, n)), Is.LessThan(0.0),
            "half a turn later it points the other way round the axis");
        Assert.That((BlackHoleSettings.PulsarBeamAxis(spin, 35.0, 2.0 * Math.PI) - first).Length, Is.LessThan(1e-12), "a full turn comes back");
    }

    [Test]
    public void PulsarBeam_WithoutTilt_IsTheSpinAxis()
    {
        Double3 beam = BlackHoleSettings.PulsarBeamAxis(new Double3(0, 0, 2), 0.0, 1.3);
        Assert.That((beam - Double3.UnitZ).Length, Is.LessThan(1e-12));
    }
}
