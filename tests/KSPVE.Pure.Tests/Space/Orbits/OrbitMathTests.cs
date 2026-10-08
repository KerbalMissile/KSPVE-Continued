using System;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Orbits;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Orbits;

[TestFixture]
public sealed class OrbitMathTests
{
    private const double MuSun = 1.32712440018e20; // IAU 2015 nominal GM_sun
    private const double Au = 1.495978707e11;

    [Test]
    public void EarthLikePeriodIsAboutOneYear()
    {
        double days = OrbitMath.PeriodSeconds(MuSun, Au) / 86400.0;
        Assert.That(days, Is.EqualTo(365.25).Within(0.1));
    }

    [Test]
    public void MeanMotionMatchesPeriod()
    {
        Assert.That(OrbitMath.MeanMotion(MuSun, Au) * OrbitMath.PeriodSeconds(MuSun, Au),
            Is.EqualTo(2 * Math.PI).Within(1e-12));
    }

    [Test]
    public void CircularOrbitHasConstantRadiusAndSpeed()
    {
        var el = new OrbitalElements(2 * Au, 0, 0.3, 1.0, 0.5, 0.2, 0);
        for (int k = 0; k < 20; k++)
        {
            var (p, v) = OrbitMath.StateAt(el, MuSun, k * 1.0e6);
            Assert.That(p.Length, Is.EqualTo(2 * Au).Within(2 * Au * 1e-12));
            Assert.That(v.Length, Is.EqualTo(Math.Sqrt(MuSun / (2 * Au))).Within(1e-6));
        }
    }

    [Test]
    public void StateReturnsAfterOnePeriod()
    {
        var el = new OrbitalElements(3 * Au, 0.4, 0.2, 2.0, 1.0, 0.7, 100.0);
        double period = OrbitMath.PeriodSeconds(MuSun, el.SemiMajorAxisMeters);
        var (p0, v0) = OrbitMath.StateAt(el, MuSun, 5.0e6);
        var (p1, v1) = OrbitMath.StateAt(el, MuSun, 5.0e6 + period);
        Assert.That((p1 - p0).Length / p0.Length, Is.LessThan(1e-9));
        Assert.That((v1 - v0).Length / v0.Length, Is.LessThan(1e-9));
    }

    [Test]
    public void VisVivaHolds()
    {
        var el = new OrbitalElements(2.7 * Au, 0.35, 0.1, 0.4, 3.0, 1.1, 0);
        for (int k = 0; k < 15; k++)
        {
            var (p, v) = OrbitMath.StateAt(el, MuSun, k * 3.0e7);
            double expected = MuSun * (2.0 / p.Length - 1.0 / el.SemiMajorAxisMeters);
            Assert.That(v.LengthSquared, Is.EqualTo(expected).Within(expected * 1e-10));
        }
    }

    [Test]
    public void AngularMomentumIsNormalToOrbitPlane()
    {
        double i = 0.5, node = 1.2;
        var el = new OrbitalElements(2.2 * Au, 0.2, i, node, 0.9, 0.3, 0);
        var (p, v) = OrbitMath.StateAt(el, MuSun, 1.0e7);
        Double3 h = Double3.Cross(p, v).Normalized();
        var expected = new Double3(Math.Sin(i) * Math.Sin(node), -Math.Sin(i) * Math.Cos(node), Math.Cos(i));
        Assert.That((h - expected).Length, Is.LessThan(1e-10));
    }

    [Test]
    public void PericentreAtMeanAnomalyZero()
    {
        var el = new OrbitalElements(Au, 0.5, 0, 0, 0, 0, 0);
        var (p, _) = OrbitMath.StateAt(el, MuSun, 0);
        Assert.That(p.X, Is.EqualTo(0.5 * Au).Within(1.0));
        Assert.That(p.Y, Is.EqualTo(0).Within(1.0));
    }
}
