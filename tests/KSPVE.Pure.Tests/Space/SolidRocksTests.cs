using System;
using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class SolidRocksTests
{
    // A belt from 3e11 to 5e11 m round a Sun-like star, thick enough that its middle is full.
    private const double Gm = 1.32712440018e20;
    private static RockFieldShape Belt() => new RockFieldShape { Kind = SpaceObjectKind.AsteroidBelt, Inner = 3e11, Outer = 5e11, Thickness = 0.05 };
    private static readonly Double3 Middle = new Double3(4e11, 0.0, 0.0);

    private static List<SolidRock> Near(RockFieldShape shape, Double3 at, double range, double ut = 1e7, RockCollisions mode = RockCollisions.AsDrawn,
        double share = 1.0, double spacing = 2000.0, ulong seed = 7)
    {
        var rocks = new List<SolidRock>();
        SolidRocks.Near(shape, mode, share, Gm, 1.0, spacing, 150.0, 1.0, seed, ut, at, range, rocks);
        return rocks;
    }

    [Test]
    public void TheSameRocksAreThere_EveryTimeTheyAreLookedUp()
    {
        List<SolidRock> a = Near(Belt(), Middle, 10000.0), b = Near(Belt(), Middle, 10000.0);
        Assert.That(a, Is.Not.Empty);
        Assert.That(a.Select(r => r.Id), Is.EqualTo(b.Select(r => r.Id)));
        Assert.That(a.Select(r => r.Position), Is.EqualTo(b.Select(r => r.Position)));
        Assert.That(Near(Belt(), Middle, 10000.0, seed: 8).Select(r => r.Id), Is.Not.EqualTo(a.Select(r => r.Id)), "another object's field differs");
    }

    [Test]
    public void AFullField_HoldsOneRockPerCell_AndShareThinsIt()
    {
        const double range = 20000.0, spacing = 2000.0;
        double cellsInRange = 4.0 / 3.0 * Math.PI * Math.Pow(range / spacing, 3);
        int full = Near(Belt(), Middle, range).Count;
        Assert.That(full, Is.EqualTo(cellsInRange).Within(0.06 * cellsInRange), "one rock a cell where the field is full");
        int quarter = Near(Belt(), Middle, range, share: 0.25).Count;
        Assert.That(quarter, Is.EqualTo(0.25 * full).Within(0.1 * 0.25 * full));
        foreach (SolidRock r in Near(Belt(), Middle, range)) Assert.That((r.Position - Middle).Length, Is.LessThanOrEqualTo(range + r.Radius));
    }

    [Test]
    public void NoRocks_OutsideTheBelt_OrInAFullGap()
    {
        Assert.That(Near(Belt(), new Double3(1e11, 0.0, 0.0), 20000.0), Is.Empty, "inside the inner edge");
        Assert.That(Near(Belt(), new Double3(7e11, 0.0, 0.0), 20000.0), Is.Empty, "beyond the outer edge");
        Assert.That(Near(Belt(), new Double3(4e11, 0.0, 4e11 * 0.05 * 8.0), 20000.0), Is.Empty, "far above the plane");
        RockFieldShape gapped = Belt();
        gapped.Gaps = new[] { new DiskGap(4e11, 1e9, 1.0) };
        gapped.GapCount = 1;
        Assert.That(Near(gapped, Middle, 20000.0), Is.Empty, "in a gap a planet cleared");
    }

    [Test]
    public void Rocks_OrbitAtTheirCircularSpeed_AndMoveSmoothly()
    {
        List<SolidRock> now = Near(Belt(), Middle, 20000.0, ut: 1e7);
        Assert.That(now, Is.Not.Empty);
        foreach (SolidRock r in now)
        {
            double radius = Math.Sqrt(r.Position.X * r.Position.X + r.Position.Y * r.Position.Y);
            Assert.That(r.Velocity.Length, Is.EqualTo(Math.Sqrt(Gm / radius)).Within(1e-5 * Math.Sqrt(Gm / radius)), "circular orbital speed");
            Assert.That(Double3.Dot(r.Velocity, r.Position), Is.EqualTo(0.0).Within(1e-6 * r.Velocity.Length * radius), "along the orbit");
        }
        // A second later the same rocks are where their velocity carried them (looked up afresh, not extrapolated).
        const double dt = 1.0;
        Dictionary<long, SolidRock> later = Near(Belt(), Middle, 60000.0, ut: 1e7 + dt).ToDictionary(r => r.Id);
        foreach (SolidRock r in now)
        {
            Assert.That(later.ContainsKey(r.Id), Is.True, "still there");
            Assert.That((later[r.Id].Position - r.PositionAfter(dt)).Length, Is.LessThan(1e-3), "where its orbit took it");
            Assert.That((later[r.Id].Position - (r.Position + r.Velocity * dt)).Length, Is.LessThan(1.0), "its velocity is its motion");
        }
    }

    [Test]
    public void ARealisticField_IsAsEmptyAsARealBelt()
    {
        int found = 0;
        for (int i = 0; i < 200; i++)
        {
            double a = i * 0.0314;
            found += Near(Belt(), new Double3(4e11 * Math.Cos(a), 4e11 * Math.Sin(a), 0.0), 20000.0, mode: RockCollisions.Realistic).Count;
        }
        Assert.That(found, Is.Zero, "no rock within 20 km of 200 places in the belt");
        List<SolidRock> wide = Near(Belt(), Middle, 3e10, mode: RockCollisions.Realistic);
        Assert.That(wide, Is.Not.Empty, "but they are there, millions of km apart");
        Assert.That(wide.All(r => r.Radius >= SolidRocks.RealMinRadius && r.Radius <= SolidRocks.RealMaxRadius), Is.True);
        Assert.That(Near(Belt(), Middle, 20000.0, mode: RockCollisions.Off), Is.Empty);
    }
}
