using System;
using System.Linq;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Orbits;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Orbits;

public class OrbitPathsTests
{
    private const double StarMu = 1.3e20;

    private static PathBody Root(double mu) => new PathBody(-1, mu, Double3.Zero, Double3.Zero);

    /// <summary>A satellite at periapsis on the +X axis, moving along +Y.</summary>
    private static PathBody Orbiting(int parent, double mu, double parentMu, double a, double e = 0.0)
    {
        double r = a * (1.0 - e);
        double speed = Math.Sqrt((parentMu + mu) * (2.0 / r - 1.0 / a));
        return new PathBody(parent, mu, new Double3(r, 0, 0), new Double3(0, speed, 0));
    }

    private static OrbitPath PathOf(OrbitPath[] paths, int body) => paths.Single(p => p.Body == body);

    [Test]
    public void CircularOrbit_OneLapAtConstantRadius_EndsWhereItBegan()
    {
        const double a = 1.5e11;
        OrbitPath path = PathOf(OrbitPaths.Compute(new[] { Root(StarMu), Orbiting(0, 4e14, StarMu, a) }), 1);

        Assert.That(path.FullLap, Is.True);
        Assert.That(path.CentreBodies, Is.EqualTo(new[] { 0 }));
        Assert.That(path.Points.Length, Is.InRange(OrbitPaths.SamplesPerLap / 2, OrbitPaths.SamplesPerLap * 2));
        foreach (Double3 p in path.Points) Assert.That(p.Length, Is.EqualTo(a).Within(a * 1e-3));
        Assert.That((path.Points.Last() - path.Points.First()).Length, Is.LessThan(a * 0.15), "the lap closes");
        Assert.That(path.Seconds, Is.EqualTo(OrbitMath.PeriodSeconds(StarMu + 4e14, a)).Within(path.Seconds * 0.03));
    }

    [Test]
    public void EccentricOrbit_ReachesItsApoapsisAndTakesItsKeplerPeriod()
    {
        const double a = 1e11, e = 0.5;
        OrbitPath path = PathOf(OrbitPaths.Compute(new[] { Root(StarMu), Orbiting(0, 1e13, StarMu, a, e) }), 1);

        Assert.That(path.FullLap, Is.True);
        Assert.That(path.Points.Max(p => p.Length), Is.EqualTo(a * (1 + e)).Within(a * 0.01));
        Assert.That(path.Points.Min(p => p.Length), Is.EqualTo(a * (1 - e)).Within(a * 0.01));
        Assert.That(path.Seconds, Is.EqualTo(OrbitMath.PeriodSeconds(StarMu, a)).Within(path.Seconds * 0.03));
    }

    [Test]
    public void Moon_IsDrawnAroundItsPlanet_ThePlanetAroundItsStar()
    {
        const double planetMu = 4e14, planetA = 1.5e11, moonA = 1e8;
        var bodies = new[]
        {
            Root(StarMu),
            Orbiting(0, planetMu, StarMu, planetA),
            Orbiting(1, 5e12, planetMu, moonA)
        };
        OrbitPath[] paths = OrbitPaths.Compute(bodies);

        OrbitPath moon = PathOf(paths, 2);
        Assert.That(moon.CentreBodies, Is.EqualTo(new[] { 1 }));
        Assert.That(moon.CentreWholeSystems, Is.EqualTo(new[] { false }), "around the planet itself");
        Assert.That(moon.FullLap, Is.True);
        foreach (Double3 p in moon.Points) Assert.That(p.Length, Is.EqualTo(moonA).Within(moonA * 0.02));

        OrbitPath planet = PathOf(paths, 1);
        Assert.That(planet.FullLap, Is.True);
        foreach (Double3 p in planet.Points) Assert.That(p.Length, Is.EqualTo(planetA).Within(planetA * 0.01));
    }

    [Test]
    public void BodyOutsideAHeavySibling_CirclesTheCentreOfMassOfBoth()
    {
        // A binary (the root and a companion of half its mass) with a distant planet: about the pair's centre of mass
        // the planet keeps its distance; about the root alone it would swing by a third of the binary's separation.
        const double companionMu = 0.5 * StarMu, separation = 1e10, planetA = 2e11;
        PathBody companion = Orbiting(0, companionMu, StarMu, separation);
        // The planet on a circular orbit about the pair's centre of mass, given (as KSP holds it) relative to the root.
        Double3 centre = companion.Position / 3.0, centreVelocity = companion.Velocity / 3.0;
        var bodies = new[]
        {
            Root(StarMu),
            companion,
            new PathBody(0, 1e12, centre + new Double3(0, planetA, 0),
                centreVelocity + new Double3(-Math.Sqrt((StarMu + companionMu) / planetA), 0, 0))
        };
        OrbitPath planet = PathOf(OrbitPaths.Compute(bodies), 2);

        Assert.That(planet.CentreBodies, Is.EquivalentTo(new[] { 0, 1 }));
        Assert.That(planet.FullLap, Is.True);
        double min = planet.Points.Min(p => p.Length), max = planet.Points.Max(p => p.Length);
        Assert.That((max - min) / planetA, Is.LessThan(0.03), "steady about the pair's centre of mass");
    }

    [Test]
    public void RootWithAHeavierCompanion_GetsAPathOfItsOwn()
    {
        // Kcalbeloh and Kerr: the companion is four times the root, so the root swings about their centre of mass.
        const double rootMu = 7.9e20, companionMu = 4 * rootMu, separation = 3e12;
        OrbitPath[] paths = OrbitPaths.Compute(new[] { Root(rootMu), Orbiting(0, companionMu, rootMu, separation) });

        OrbitPath root = PathOf(paths, 0);
        Assert.That(root.CentreBodies, Is.EquivalentTo(new[] { 0, 1 }));
        Assert.That(root.FullLap, Is.True);
        foreach (Double3 p in root.Points) Assert.That(p.Length, Is.EqualTo(0.8 * separation).Within(separation * 0.01));
        Assert.That(PathOf(paths, 1).FullLap, Is.True, "and the companion about the root");
    }

    [Test]
    public void LightSatellites_LeaveTheRootWithoutAPath()
    {
        OrbitPath[] paths = OrbitPaths.Compute(new[] { Root(StarMu), Orbiting(0, 4e14, StarMu, 1.5e11) });
        Assert.That(paths.Any(p => p.Body == 0), Is.False);
    }

    [Test]
    public void HoursAndCenturiesTogether_EveryBodyCompletesALap()
    {
        // An 8-hour moon's planet, and a star 100 AU out with a 1000-year orbit, in the same system.
        const double planetMu = 4e14;
        var bodies = new[]
        {
            Root(StarMu),
            Orbiting(0, planetMu, StarMu, 1.5e11),
            Orbiting(1, 1e10, planetMu, 9.4e6),
            Orbiting(0, 0.01 * StarMu, StarMu, 1.5e13)
        };
        OrbitPath[] paths = OrbitPaths.Compute(bodies);

        Assert.That(paths.Select(p => p.Body), Is.EquivalentTo(new[] { 1, 2, 3 }));
        foreach (OrbitPath p in paths)
        {
            Assert.That(p.FullLap, Is.True, "body " + p.Body);
            Assert.That(p.Points.Length, Is.LessThan(OrbitPaths.SamplesPerLap * 2), "body " + p.Body);
        }
        Assert.That(PathOf(paths, 2).Seconds, Is.LessThan(12 * 3600.0));
        Assert.That(PathOf(paths, 3).Seconds, Is.GreaterThan(900 * 365.25 * 86400.0));
    }

    [Test]
    public void EscapingBody_IsFollowedButNotCalledALap()
    {
        const double r = 1e11;
        var bodies = new[]
        {
            Root(StarMu),
            new PathBody(0, 1e10, new Double3(r, 0, 0), new Double3(0, 2.0 * Math.Sqrt(StarMu / r), 0))
        };
        OrbitPath path = PathOf(OrbitPaths.Compute(bodies), 1);

        Assert.That(path.FullLap, Is.False);
        Assert.That(path.Points.Length, Is.GreaterThan(2));
        Assert.That(path.Points.Last().Length, Is.GreaterThan(r), "it has moved away");
    }
}
