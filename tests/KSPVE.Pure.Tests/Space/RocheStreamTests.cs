using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class RocheStreamTests
{
    [Test]
    public void L1_IsHalfwayForEqualMasses_AndAtTheHillRadiusForALightCompanion()
    {
        Assert.That(RocheStream.L1FromCompact(1.0), Is.EqualTo(0.5).Within(1e-9));
        double q = 1e-3;
        Assert.That(1.0 - RocheStream.L1FromCompact(q), Is.EqualTo(Math.Pow(q / 3.0, 1.0 / 3.0)).Within(2e-3), "the companion's Hill radius");
    }

    [Test]
    public void LobeRadius_IsEggletons()
    {
        Assert.That(RocheStream.LobeRadius(1.0), Is.EqualTo(0.3789).Within(1e-4));
        Assert.That(RocheStream.LobeRadius(0.1), Is.LessThan(RocheStream.LobeRadius(1.0)), "a lighter companion has a smaller lobe");
    }

    [Test]
    public void ThePotential_IsFlatAtL1_AndTheLobeItBoundsHasEggletonsVolume()
    {
        foreach (double q in new[] { 0.15, 0.5, 2.0 })
        {
            double xl1 = RocheStream.L1FromCompact(q), phiL1 = RocheStream.LobePotential(q);
            RocheStream.Potential(q, new Double3(xl1, 0.0, 0.0), out Double3 g);
            Assert.That(g.Length, Is.LessThan(1e-6), "L1 is a saddle of the potential");
            // The lobe: below the L1 potential on the companion's side of L1, its volume as a sphere's.
            const int n = 120;
            double half = 1.0 - xl1, cell = 2.0 * half / n, volume = 0.0;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            for (int k = 0; k < n; k++)
            {
                var p = new Double3(1.0 - half + (i + 0.5) * cell, -half + (j + 0.5) * cell, -half + (k + 0.5) * cell);
                if (p.X > xl1 && RocheStream.Potential(q, p, out _) < phiL1) volume += cell * cell * cell;
            }
            double radius = Math.Pow(volume * 3.0 / (4.0 * Math.PI), 1.0 / 3.0);
            Assert.That(radius, Is.EqualTo(RocheStream.LobeRadius(q)).Within(0.015 * radius), $"q = {q}: Eggleton's fit is good to 1%");
        }
    }

    [Test]
    public void TheFirstRing_HasTheAngularMomentumTheGasHadAtL1()
    {
        foreach (double q in new[] { 0.05, 0.3, 1.0 })
        {
            double b = RocheStream.L1FromCompact(q), r = RocheStream.CircularizationRadius(q);
            // In units G (M1 + M2) = a = Omega = 1: the gas at L1 turns with the binary, angular momentum b^2 about the compact
            // object; a circular orbit round it (mass 1 / (1 + q)) at r has sqrt(r / (1 + q)).
            Assert.That(Math.Sqrt(r / (1.0 + q)), Is.EqualTo(b * b).Within(1e-12), $"q = {q}");
            Assert.That(r, Is.LessThan(b), "inside L1");
        }
        Assert.That(RocheStream.CircularizationRadius(0.05), Is.GreaterThan(RocheStream.CircularizationRadius(0.3)), "a lighter companion's gas settles further out");
        foreach (double q in new[] { 0.05, 0.3, 1.0 })
        {
            Assert.That(RocheStream.TidalRadius(q), Is.EqualTo(0.6 / (1.0 + q)).Within(1e-12));
            Assert.That(RocheStream.TidalRadius(q), Is.GreaterThan(RocheStream.CircularizationRadius(q)), "the disk spreads out from its first ring");
            Assert.That(RocheStream.TidalRadius(q), Is.LessThan(RocheStream.L1FromCompact(q)), "but not past L1");
        }
    }

    [Test]
    public void TheWinding_ClosesInAtFirstAsFastAsTheGasWasFalling_SoThereIsNoKink()
    {
        // Falling in 0.1 for every 0.1 radians round as it strikes at radius 1: dr/dtheta = -1.
        var fall = new List<Double3> { new Double3(1.1 * Math.Cos(-0.1), 1.1 * Math.Sin(-0.1), 0.0), new Double3(1.0, 0.0, 0.0) };
        RocheStream.StreamSpiral spiral = RocheStream.SpiralOf(fall, 1.0, 0.1, 0.0);
        Assert.That(spiral.Curve, Is.EqualTo(2.0 * Math.PI / 0.9).Within(1e-9));
        const double t = 1e-6;
        double slope = (spiral.At(t).Length - spiral.At(0.0).Length) / (2.0 * Math.PI * t);
        Assert.That(slope, Is.EqualTo(-1.0).Within(1e-4), "the same slope either side of where it strikes");
        Assert.That(spiral.At(1.0).Length, Is.EqualTo(0.1).Within(1e-9), "and still in to where it was asked");
        Assert.That(RocheStream.SpiralOf(fall, 1.0, 0.1, 2.0).Curve, Is.EqualTo(2.0), "or curved as set");
    }

    [Test]
    public void TheFall_EasesIntoItsWinding_WithoutAKink()
    {
        List<Double3> fall = RocheStream.Path(0.05, 0.57, 24);
        RocheStream.StreamSpiral spiral = RocheStream.SpiralOf(fall, 1.6, 0.35);
        double t0 = RocheStream.EaseAngle / (2.0 * Math.PI * spiral.Turns);
        List<Double3> eased = RocheStream.EaseInto(fall, 16, spiral.At(t0), spiral.DirectionAt(t0), 8);
        Assert.That(eased, Has.Count.EqualTo(24));
        Assert.That((eased[23] - spiral.At(t0)).Length, Is.LessThan(1e-12), "it lands on the winding");
        // No corner anywhere: neighbouring segments turn by little, and the last runs on along the winding.
        for (int i = 2; i < eased.Count; i++)
        {
            Double3 a = (eased[i - 1] - eased[i - 2]).Normalized(), b = (eased[i] - eased[i - 1]).Normalized();
            Assert.That(Double3.Dot(a, b), Is.GreaterThan(Math.Cos(25.0 * Math.PI / 180.0)), $"no kink at point {i - 1}");
        }
        Double3 last = (eased[23] - eased[22]).Normalized();
        Assert.That(Double3.Dot(last, spiral.DirectionAt(t0)), Is.GreaterThan(0.98));
        // And the rest of the winding carries on from there on the same curve.
        RocheStream.StreamSpiral rest = spiral.From(t0);
        Assert.That((rest.At(0.0) - spiral.At(t0)).Length, Is.LessThan(1e-9));
        Assert.That((rest.At(0.5) - spiral.At(t0 + 0.5 * (1.0 - t0))).Length, Is.LessThan(1e-9));
    }

    [Test]
    public void TheSpiral_WindsInFromWhereItStrikes_TheWayItWasGoing()
    {
        List<Double3> fall = RocheStream.Path(0.15, 0.6, 24);
        RocheStream.StreamSpiral spiral = RocheStream.SpiralOf(fall, 2.5, 0.3);
        Assert.That((spiral.At(0.0) - fall[23]).Length, Is.LessThan(1e-9), "it carries on from where it struck");
        Assert.That(spiral.At(1.0).Length, Is.EqualTo(0.3 * fall[23].Length).Within(1e-9), "in to 30% of that radius");
        Assert.That(spiral.At(0.5).Length, Is.EqualTo(0.65 * fall[23].Length).Within(1e-9), "closing in steadily");
        Double3 a = spiral.At(0.0), b = spiral.At(0.01), f0 = fall[22], f1 = fall[23];
        double spin = a.X * (b.Y - a.Y) - a.Y * (b.X - a.X), fallSpin = f1.X * (f1.Y - f0.Y) - f1.Y * (f1.X - f0.X);
        Assert.That(Math.Sign(spin), Is.EqualTo(Math.Sign(fallSpin)), "round the way the falling gas was going");
    }

    [Test]
    public void TheStream_FallsFromL1_CurvingWithTheOrbit_ToTheDisk()
    {
        const double q = 0.5;
        List<Double3> path = RocheStream.Path(q, 0.15, 24);
        Assert.That(path, Has.Count.EqualTo(24));
        Assert.That(path[0].X, Is.EqualTo(RocheStream.L1FromCompact(q)).Within(1e-3), "starts at L1");
        Assert.That(path[23].Length, Is.LessThanOrEqualTo(0.15 + 1e-3), "ends at the disk's edge");
        Assert.That(path[12].Y, Is.GreaterThan(0.0), "the Coriolis force turns it towards the companion's motion");
        for (int i = 1; i < path.Count; i++) Assert.That(path[i].Length, Is.LessThan(path[i - 1].Length + 1e-6), "always falling inward");
    }

    [Test]
    public void AStreamThatCannotReachATinyDisk_EndsAtItsClosestApproach()
    {
        List<Double3> path = RocheStream.Path(0.5, 1e-6, 24);
        double end = path[23].Length;
        Assert.That(end, Is.InRange(0.02, 0.3), "it swings round at its circularisation radius");
    }
}

public class RocheStreamTurnsTests
{
    [Test]
    public void Turns_WindOnRoundTheDisk_SpirallingIn()
    {
        var path = RocheStream.PathWithTurns(0.5, 0.15, 24, 2.0, out int impact);
        Assert.That(impact, Is.EqualTo(23));
        Assert.That(path, Has.Count.EqualTo(48));
        Assert.That(path[47].Length, Is.EqualTo(path[23].Length * 0.85).Within(1e-9), "in to 85% of where it struck: the disk's outer ring");
        var none = RocheStream.PathWithTurns(0.5, 0.15, 24, 0.0, out int impact0);
        Assert.That(none, Has.Count.EqualTo(24));
        Assert.That(impact0, Is.EqualTo(23));
    }
}
