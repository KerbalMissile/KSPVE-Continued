using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class ProceduralObjectsTests
{
    // Stock Kerbol's planets (semi-major axes, radii) and a comet-like body.
    private static List<SystemBody> Kerbol() => new List<SystemBody>
    {
        new SystemBody { Name = "Sun", IsStar = true, Radius = 261600000 },
        P("Moho", 5263138304, 250000), P("Eve", 9832684544, 700000), P("Kerbin", 13599840256, 600000),
        P("Duna", 20726155264, 320000), P("Dres", 40839348203, 138000), P("Jool", 68773560320, 6000000),
        P("Eeloo", 90118820000, 210000),
        new SystemBody { Name = "Mun", Parent = "Kerbin", Radius = 200000, SemiMajorAxis = 12000000 },
        new SystemBody { Name = "Wanderer", Parent = "Sun", Radius = 3000, SemiMajorAxis = 30e9, Eccentricity = 0.6 },
    };

    private static SystemBody P(string name, double sma, double radius) =>
        new SystemBody { Name = name, Parent = "Sun", Radius = radius, SemiMajorAxis = sma, OrbitNormal = Double3.UnitY };

    private static SettingsValues Rules(params string[] keyValues)
    {
        var node = new CfgNode("KSPVE_PROCEDURAL").Add("enabled", "True");
        for (int i = 0; i + 1 < keyValues.Length; i += 2) node.Add(keyValues[i], keyValues[i + 1]);
        var issues = new List<ConfigIssue>();
        SettingsValues v = ProceduralObjects.Schema.Resolve(node, issues);
        Assert.That(issues, Is.Empty);
        return v;
    }

    private static SettingsValues Resolved(CfgNode node)
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.ResolveWithKind(node, issues);
        Assert.That(issues, Is.Empty, "the planner writes only known settings");
        return v;
    }

    [Test]
    public void Off_PlacesNothing()
    {
        var off = ProceduralObjects.Schema.Resolve(new CfgNode("KSPVE_PROCEDURAL"), new List<ConfigIssue>());
        Assert.That(ProceduralObjects.Plan(Kerbol(), off, new HashSet<string>()), Is.Empty);
    }

    [Test]
    public void TheBelt_GoesInTheWidestGap_ClearOfBothPlanets()
    {
        List<CfgNode> plan = ProceduralObjects.Plan(Kerbol(), Rules(), new HashSet<string>());
        CfgNode belt = plan.Single(n => n.GetLast("kind") == "AsteroidBelt");
        SettingsValues v = Resolved(belt);
        Assert.That(belt.GetLast("body"), Is.EqualTo("Sun"));
        // Duna to Dres (ratio 1.97) is the widest gap: 1.3x Duna's orbit to 0.75x Dres', in the Sun's radii.
        Assert.That(v.Get(BlackHoleSettings.DiskInner), Is.EqualTo(20726155264 * 1.3 / 261600000).Within(0.01));
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(40839348203 * 0.75 / 261600000).Within(0.01));
        Assert.That(v.Get(BlackHoleSettings.NormalY), Is.EqualTo(1.0).Within(1e-9), "in the planets' plane");
    }

    [Test]
    public void NoGapWideEnough_NoBelt()
    {
        Assert.That(ProceduralObjects.Plan(Kerbol(), Rules("beltMinGap", "2.5"), new HashSet<string>()).Any(n => n.GetLast("kind") == "AsteroidBelt"), Is.False);
    }

    [Test]
    public void AKuiperBelt_LiesBeyondTheOutermostPlanet()
    {
        CfgNode disk = ProceduralObjects.Plan(Kerbol(), Rules("outerDiskChance", "1"), new HashSet<string>()).Single(n => n.GetLast("kind") == "DebrisDisk");
        SettingsValues v = Resolved(disk);
        Assert.That(v.Get(BlackHoleSettings.DiskInner), Is.EqualTo(90118820000 * 1.4 / 261600000).Within(0.01));
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(90118820000 * 2.6 / 261600000).Within(0.01));
    }

    [Test]
    public void SmallEccentricBodiesRoundAStar_BecomeComets_AndDoNotCountAsPlanets()
    {
        List<CfgNode> plan = ProceduralObjects.Plan(Kerbol(), Rules(), new HashSet<string>());
        CfgNode comet = plan.Single(n => n.GetLast("kind") == "Comet");
        Assert.That(comet.GetLast("body"), Is.EqualTo("Wanderer"));
        Assert.That(Resolved(comet).Get(BlackHoleSettings.HorizonRadius), Is.EqualTo(30e9 / 1500.0).Within(1.0));
        Assert.That(plan.Any(n => n.GetLast("body") == "Mun"), Is.False, "moons are left alone");
    }

    [Test]
    public void DwarfPlanets_SitInTheBelt_RatherThanSplittingIt()
    {
        List<SystemBody> bodies = Kerbol();
        bodies.Add(P("Ceres", 30e9, 40000));   // smaller than planetMinRadius
        CfgNode belt = ProceduralObjects.Plan(bodies, Rules(), new HashSet<string>()).Single(n => n.GetLast("kind") == "AsteroidBelt");
        Assert.That(Resolved(belt).Get(BlackHoleSettings.DiskInner), Is.EqualTo(20726155264 * 1.3 / 261600000).Within(0.01));
    }

    [Test]
    public void BodiesThatHaveThatKindAlready_AreLeftAlone()
    {
        var taken = new HashSet<string> { "Sun|AsteroidBelt", "Wanderer|Comet" };
        List<CfgNode> plan = ProceduralObjects.Plan(Kerbol(), Rules("outerDiskChance", "1"), taken);
        Assert.That(plan.Select(n => n.GetLast("kind")), Is.EquivalentTo(new[] { "DebrisDisk" }));
    }

    [Test]
    public void TheSameSeed_GivesTheSameObjects_AnotherSeedAnotherLook()
    {
        string Text(List<CfgNode> plan) => string.Join(";", plan.SelectMany(n => n.Values.Select(v => v.Key + "=" + v.Value)));
        string a = Text(ProceduralObjects.Plan(Kerbol(), Rules("seed", "7", "outerDiskChance", "1"), new HashSet<string>()));
        string b = Text(ProceduralObjects.Plan(Kerbol(), Rules("seed", "7", "outerDiskChance", "1"), new HashSet<string>()));
        string c = Text(ProceduralObjects.Plan(Kerbol(), Rules("seed", "8", "outerDiskChance", "1"), new HashSet<string>()));
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
    }

    [Test]
    public void Hash_IsEvenlySpread()
    {
        int low = 0;
        for (int i = 0; i < 2000; i++)
        {
            double h = ProceduralObjects.Hash(3, "Body" + i, "belt");
            Assert.That(h, Is.InRange(0.0, 1.0));
            if (h < 0.5) low++;
        }
        Assert.That(low, Is.InRange(900, 1100));
    }

    [Test]
    public void AGalaxy_IsSizedToItsRadius()
    {
        SettingsValues v = Resolved(new CfgNode("KSPVE_BLACKHOLE").Add("body", "Kerr").Add("kind", "Galaxy"));
        Assert.That(v.Get(BlackHoleSettings.DiskOuter), Is.EqualTo(50000.0));
        double unit = SpaceObjects.UnitMeters(SpaceObjectKind.Galaxy, v.Get(BlackHoleSettings.HorizonRadius), 1e9, false, 3.0);
        Assert.That(unit, Is.EqualTo(9460730472580800.0), "a light year");
        Assert.That(SpaceObjects.ShaderUnitMeters(SpaceObjectKind.Galaxy, unit, 50000.0) * SpaceObjects.NebulaShaderRadius, Is.EqualTo(unit * 50000.0).Within(1e6));
    }
}
