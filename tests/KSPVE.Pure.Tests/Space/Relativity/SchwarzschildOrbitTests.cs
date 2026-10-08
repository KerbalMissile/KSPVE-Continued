using System;
using KSPVE.Pure.Space.Relativity;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Relativity;

public class SchwarzschildOrbitTests
{
    [Test]
    public void NearlyCircularOrbit_KeepsKeplersPeriodInCoordinateTime()
    {
        // Circular orbits in Schwarzschild coordinate time obey Omega^2 = m / r^3 exactly.
        var orbit = new SchwarzschildOrbit(20.0, 1e-6, 1.0, 0.0);
        double azimuthalPeriod = orbit.RadialPeriod * 2.0 * Math.PI / orbit.AzimuthPerOrbit;
        Assert.That(azimuthalPeriod, Is.EqualTo(2.0 * Math.PI * Math.Sqrt(8000.0)).Within(1e-6).Percent);
        Assert.That(orbit.AzimuthPerOrbit, Is.EqualTo(2.0 * Math.PI * Math.Sqrt(20.0 / 14.0)).Within(1e-7).Percent,
            "circular orbits advance by 2 pi (sqrt(p/(p-6)) - 1) per radial period");
    }

    [Test]
    public void WeakField_PeriapsisAdvanceIsSixPiMOverP()
    {
        var orbit = new SchwarzschildOrbit(2000.0, 0.5, 1.0, 0.0);
        double advance = orbit.AzimuthPerOrbit - 2.0 * Math.PI;
        Assert.That(advance, Is.EqualTo(6.0 * Math.PI / 2000.0).Within(1.0).Percent);
    }

    [Test]
    public void WeakField_RadialPeriodIsKeplers()
    {
        var orbit = new SchwarzschildOrbit(5000.0, 0.5, 1.0, 0.0);
        double a = 5000.0 / (1.0 - 0.25);
        Assert.That(orbit.RadialPeriod, Is.EqualTo(2.0 * Math.PI * Math.Sqrt(a * a * a)).Within(0.5).Percent);
    }

    [TestCase(30.0, 0.7)]
    [TestCase(25.0, 1.0)]
    [TestCase(40.0, 1.2)]
    [TestCase(12.0, 0.95)]
    public void EnergyAndAngularMomentum_GiveBackPAndE(double p, double e)
    {
        double energy = SchwarzschildOrbit.Energy(p, e);
        double momentum = SchwarzschildOrbit.AngularMomentum(p, e, 0.5);
        Assert.That(SchwarzschildOrbit.TryElements(energy, momentum, 0.5, out double p2, out double e2), Is.True);
        Assert.That(p2, Is.EqualTo(p).Within(1e-6).Percent);
        Assert.That(e2, Is.EqualTo(e).Within(1e-6));
    }

    [Test]
    public void ParabolicOrbit_HasUnitEnergy()
    {
        Assert.That(SchwarzschildOrbit.Energy(25.0, 1.0), Is.EqualTo(1.0).Within(1e-12));
    }

    [Test]
    public void PlungingOrbit_HasNoElements()
    {
        // Angular momentum below the ISCO's sqrt(12) m: nothing stops the fall.
        Assert.That(SchwarzschildOrbit.TryElements(0.97, 3.0, 1.0, out _, out _), Is.False);
    }

    [TestCase(30.0, 0.7)]
    [TestCase(40.0, 1.2)]
    public void TimeAndChi_InvertEachOther(double p, double e)
    {
        var orbit = new SchwarzschildOrbit(p, e, 0.5, 5000.0);
        foreach (double chi in new[] { -2.5, -1.0, -0.1, 0.0, 0.3, 1.7, 2.6 })
        {
            if (Math.Abs(chi) >= orbit.ChiEnd) continue;
            double t = orbit.TimeAt(chi);
            Assert.That(orbit.ChiAtTime(t, out bool beyond), Is.EqualTo(chi).Within(1e-3), $"chi {chi}");
            Assert.That(beyond, Is.False);
        }
    }

    [Test]
    public void BoundOrbit_RepeatsEveryRadialPeriod()
    {
        var orbit = new SchwarzschildOrbit(30.0, 0.7, 0.5, 0.0);
        double t = orbit.TimeAt(0.8);
        Assert.That(orbit.TimeAt(0.8 + 2.0 * Math.PI), Is.EqualTo(t + orbit.RadialPeriod).Within(1e-9).Percent);
        Assert.That(orbit.AzimuthAt(0.8 + 2.0 * Math.PI), Is.EqualTo(orbit.AzimuthAt(0.8) + orbit.AzimuthPerOrbit).Within(1e-9));
        Assert.That(orbit.ChiAtTime(t + 3.0 * orbit.RadialPeriod, out _), Is.EqualTo(0.8 + 6.0 * Math.PI).Within(1e-3));
    }

    [Test]
    public void UnboundOrbit_StopsAtTheEndOfItsTable()
    {
        var orbit = new SchwarzschildOrbit(40.0, 1.2, 0.5, 1000.0);
        Assert.That(orbit.Radius(orbit.ChiEnd), Is.EqualTo(1000.0).Within(1e-6).Percent);
        orbit.ChiAtTime(orbit.Time[SchwarzschildOrbit.Cells] * 2.0, out bool beyond);
        Assert.That(beyond, Is.True);
    }

    [Test]
    public void StaticVelocity_AtPeriapsisOfAParabola_IsEscapeSpeed()
    {
        // E = 1: a static observer at r measures exactly the local escape speed sqrt(2m/r), all of it azimuthal.
        var orbit = new SchwarzschildOrbit(40.0, 1.0, 0.5, 1000.0);
        orbit.StaticVelocity(0.0, out double radial, out double azimuthal);
        double r = orbit.Radius(0.0);
        Assert.That(radial, Is.EqualTo(0.0).Within(1e-12));
        Assert.That(azimuthal, Is.EqualTo(Math.Sqrt(1.0 / r)).Within(1e-9));
    }
}
