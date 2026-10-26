using System;
using System.Collections.Generic;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class SpaceObjectsTests
{
    [Test]
    public void EveryKindsDefaults_AreKnownSettings_WithoutIssues()
    {
        foreach (SpaceObjectKind kind in Enum.GetValues(typeof(SpaceObjectKind)))
        {
            var node = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Star").Add("kind", kind.ToString());
            var issues = new List<ConfigIssue>();
            SettingsValues v = BlackHoleSettings.ResolveWithKind(node, issues);
            Assert.That(issues, Is.Empty, kind.ToString());
            Assert.That(v.Get(BlackHoleSettings.Kind), Is.EqualTo(kind));
            Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.GreaterThan(v.Get(BlackHoleSettings.DiskInner)), kind.ToString());
        }
    }

    [Test]
    public void OtherKinds_NeitherDragNorLightShips_NorWaitForADisruption()
    {
        foreach (SpaceObjectKind kind in Enum.GetValues(typeof(SpaceObjectKind)))
        {
            if (kind == SpaceObjectKind.BlackHole) continue;
            SettingsValues v = BlackHoleSettings.ResolveWithKind(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Star").Add("kind", kind.ToString()), new List<ConfigIssue>());
            Assert.That(v.Get(BlackHoleSettings.DiskDrag), Is.False, kind.ToString());
            Assert.That(v.Get(BlackHoleSettings.DiskLight), Is.EqualTo(0.0), kind.ToString());
            Assert.That(v.Get(BlackHoleSettings.Jets), Is.EqualTo(0.0), kind.ToString());
        }
    }

    [Test]
    public void ABlackHoleNode_IsUnchangedByKindDefaults()
    {
        var node = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr");
        SettingsValues withKind = BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>());
        SettingsValues plain = BlackHoleSettings.Schema.Resolve(node, new List<ConfigIssue>());
        Assert.That(withKind.Get(BlackHoleSettings.Kind), Is.EqualTo(SpaceObjectKind.BlackHole));
        Assert.That(withKind.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(plain.Get(BlackHoleSettings.DiskOuter)));
        Assert.That(withKind.Get(BlackHoleSettings.DiskDrag), Is.EqualTo(plain.Get(BlackHoleSettings.DiskDrag)));
        Assert.That(withKind.Get(BlackHoleSettings.FieldLines), Is.EqualTo(0.0), "no field lines on a black hole");
    }

    [Test]
    public void TheNodesOwnValues_WinOverItsKindsDefaults()
    {
        var node = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerbol").Add("kind", "AsteroidBelt").Add("diskOuter", "300").Add("rocks", "0.5");
        SettingsValues v = BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>());
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(300.0));
        Assert.That(v.Get(BlackHoleSettings.Rocks), Is.EqualTo(0.5));
        Assert.That(v.Get(BlackHoleSettings.DiskInner), Is.EqualTo(100.0), "the rest from the belt's defaults");
    }

    [Test]
    public void AnUnknownKind_IsReported_AndDrawsABlackHole()
    {
        var node = new CfgNode("KSPVE_BLACKHOLE").Add("body", "X").Add("kind", "Quasar");
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.ResolveWithKind(node, issues);
        Assert.That(BlackHoleSettings.KindOf(node), Is.EqualTo(SpaceObjectKind.BlackHole));
        Assert.That(v.Get(BlackHoleSettings.Kind), Is.EqualTo(SpaceObjectKind.BlackHole));
        Assert.That(issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void Colors_ParseThreeNumbers_AndRejectAnythingElse()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.Schema.Resolve(new CfgNode("KSPVE_BLACKHOLE").Add("dustColor", " 1, 0.5 0.25 "), issues);
        Assert.That(v.Get(BlackHoleSettings.DustColor), Is.EqualTo(new Double3(1.0, 0.5, 0.25)));
        Assert.That(issues, Is.Empty);

        foreach (string bad in new[] { "1, 0.5", "red", "1, -1, 0", "1, 2, 3, 4" })
        {
            issues.Clear();
            v = BlackHoleSettings.Schema.Resolve(new CfgNode("KSPVE_BLACKHOLE").Add("dustColor", bad), issues);
            Assert.That(v.Get(BlackHoleSettings.DustColor), Is.EqualTo(BlackHoleSettings.DustColor.Default), bad);
            Assert.That(issues, Has.Count.EqualTo(1), bad);
        }
        Assert.That(ColorSetting.Write(new Double3(1.0, 0.5, 0.25)), Is.EqualTo("1, 0.5, 0.25"));
    }

    [Test]
    public void UserTweaks_MatchTheBodysObjectBySlot()
    {
        CfgNode hole = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr").Add("brightness", "1");
        CfgNode belt = new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr").Add("kind", "AsteroidBelt").Add("brightness", "1");
        CfgNode user = new CfgNode(BlackHoleSettings.UserNodeName)
            .AddChild(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr").Add("brightness", "7"))                    // no slot: the first
            .AddChild(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr").Add("slot", "1").Add("brightness", "3"));
        Assert.That(Brightness(BlackHoleSettings.ApplyUserOverrides(hole, 0, user)), Is.EqualTo(7.0));
        Assert.That(Brightness(BlackHoleSettings.ApplyUserOverrides(belt, 1, user)), Is.EqualTo(3.0));
        Assert.That(BlackHoleSettings.UserNodeFor("Kerr", 2, user), Is.Null);
        Assert.That(BlackHoleSettings.SlotOf(new CfgNode("x").Add("slot", "nope")), Is.EqualTo(0));
    }

    private static double Brightness(CfgNode node) =>
        BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>()).Get(BlackHoleSettings.Brightness);

    [Test]
    public void LengthUnit_PerKind()
    {
        Assert.That(SpaceObjects.UnitMeters(SpaceObjectKind.BlackHole, 0.0, 2.6e6, true, 3.0), Is.EqualTo(2.6e6 / BlackHoleSettings.ShadowToHorizon).Within(1e-6));
        Assert.That(SpaceObjects.UnitMeters(SpaceObjectKind.NeutronStar, 0.0, 12000.0, false, 3.0), Is.EqualTo(4000.0), "the body is the neutron star");
        Assert.That(SpaceObjects.UnitMeters(SpaceObjectKind.AsteroidBelt, 0.0, 2.6e8, false, 3.0), Is.EqualTo(2.6e8), "star radii");
        Assert.That(SpaceObjects.UnitMeters(SpaceObjectKind.Comet, 1e7, 1800.0, false, 3.0), Is.EqualTo(1e7), "set by horizonRadius");
        Assert.That(SpaceObjects.ShaderUnitMeters(SpaceObjectKind.Nebula, 1e9, 1000.0), Is.EqualTo(1e9 * 1000.0 / SpaceObjects.NebulaShaderRadius));
        Assert.That(SpaceObjects.ShaderUnitMeters(SpaceObjectKind.DebrisDisk, 1e9, 1000.0), Is.EqualTo(1e9));
    }

    [Test]
    public void Bound_HoldsTheWholeObject()
    {
        // A belt: past its outer edge, and its field lines when it has them.
        double belt = SpaceObjects.Bound(SpaceObjectKind.AsteroidBelt, 155, 0.06, 0, 1, 1.0, 0, 7, 1.6, 0, 0, 0, 0, 0, 0);
        Assert.That(belt, Is.GreaterThan(155 * 1.1));
        double withField = SpaceObjects.Bound(SpaceObjectKind.AsteroidBelt, 5, 0.06, 0, 1, 1.0, 1, 7, 1.6, 0, 0, 0, 0, 0, 0);
        Assert.That(withField, Is.EqualTo(SpaceObjects.FieldExtent(1.0, 7, 1.6)).Within(1e-9));
        Assert.That(SpaceObjects.FieldExtent(1.0, 7, 1.6), Is.EqualTo(1.6 * Math.Pow(1.6, 6) * 1.1).Within(1e-9));
        // A neutron star's beams, faded by e^-2.5.
        Assert.That(SpaceObjects.Bound(SpaceObjectKind.NeutronStar, 70, 0.08, 0, 1, 3, 1, 7, 1.6, 1, 120, 0, 0, 0, 0), Is.EqualTo(300.0).Within(1e-9));
        // A comet: three tail lengths, the dust tail's curve included.
        Assert.That(SpaceObjects.Bound(SpaceObjectKind.Comet, 0, 0, 0, 1, 0, 0, 7, 1.6, 0, 0, 2, 60, 35, 0.5), Is.EqualTo(3 * 35 * 2.5).Within(1e-9));
        Assert.That(SpaceObjects.Bound(SpaceObjectKind.Nebula, 1000, 0, 0, 1, 0, 0, 7, 1.6, 0, 0, 0, 0, 0, 0), Is.EqualTo(13.0).Within(1e-9));
    }

    [Test]
    public void Gaps_SpreadOverTheDisk_ByCount()
    {
        var gaps = new DiskGap[DiskGaps.Max];
        int n = DiskGaps.Fill(8, 215, 6, 0.97, 0.02, "", "", "", gaps);
        Assert.That(n, Is.EqualTo(6));
        for (int i = 0; i < n; i++)
        {
            Assert.That(gaps[i].Radius, Is.InRange(8.0, 215.0), "inside the disk");
            Assert.That(gaps[i].Depth, Is.InRange(0.97 * 0.65, 0.97));
            if (i > 0) Assert.That(gaps[i].Radius, Is.GreaterThan(gaps[i - 1].Radius), "outward in order");
        }
        Assert.That(DiskGaps.Fill(8, 215, 40, 0.97, 0.02, "", "", "", gaps), Is.EqualTo(DiskGaps.Max));
        Assert.That(DiskGaps.Fill(8, 215, 0, 0.97, 0.02, "", "", "", gaps), Is.EqualTo(0));
    }

    [Test]
    public void Gaps_PlacedByHand_TakeTheirOwnWidthsAndDepths()
    {
        var gaps = new DiskGap[DiskGaps.Max];
        int n = DiskGaps.Fill(8, 200, 6, 0.9, 0.02, "0.25, 0.5 0.75", "0.05", "1, 2", gaps);
        Assert.That(n, Is.EqualTo(3), "the list wins over rings");
        Assert.That(gaps[0].Radius, Is.EqualTo(50.0).Within(1e-9));
        Assert.That(gaps[0].Width, Is.EqualTo(10.0).Within(1e-9));
        Assert.That(gaps[1].Width, Is.EqualTo(4.0).Within(1e-9), "missing widths take ringWidth");
        Assert.That(gaps[1].Depth, Is.EqualTo(1.0), "depths kept within 0..1");
        Assert.That(gaps[2].Depth, Is.EqualTo(0.9), "missing depths take ringDepth");
        Assert.That(DiskGaps.ParseList("0.1; x, 0.3"), Is.EqualTo(new List<double> { 0.1, 0.3 }));
    }
}
