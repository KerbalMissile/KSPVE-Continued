using System;
using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class PlanetshineTests
{
    const double Sun = 100000.0;

    [Test] public void ScalesWithInverseSquare()
    {
        double a = Planetshine.IlluminanceLux(Sun, 0.3, 600e3, 1200e3, 0);
        double b = Planetshine.IlluminanceLux(Sun, 0.3, 600e3, 2400e3, 0);
        Assert.That(a / b, Is.EqualTo(4.0).Within(1e-9));
    }

    [Test] public void ZeroAtNewPhase()
        => Assert.That(Planetshine.IlluminanceLux(Sun, 0.3, 600e3, 1200e3, Math.PI), Is.EqualTo(0.0).Within(1e-9));

    // Bond albedo 0.3 -> p = 0.2; at d = 670 km over R = 600 km: 0.2 * 0.802 = 0.16 of sunlight (~16,000 lux).
    [Test] public void FullPhaseLowOrbit_IsPlausibleFraction()
    {
        double e = Planetshine.IlluminanceLux(Sun, 0.3, 600e3, 670e3, 0);
        Assert.That(e / Sun, Is.EqualTo(0.16).Within(0.01));
    }
}

[TestFixture]
public class CondensationTests
{
    [Test] public void VaporCone_ZeroOutsideTransonicWindow()
    {
        Assert.That(Condensation.VaporConeIntensity(0.5, 0.9, 30000), Is.EqualTo(0.0));
        Assert.That(Condensation.VaporConeIntensity(1.3, 0.9, 30000), Is.EqualTo(0.0));
        Assert.That(Condensation.VaporConeIntensity(0.85, 0.9, 30000), Is.EqualTo(0.0));
        Assert.That(Condensation.VaporConeIntensity(1.15, 0.9, 30000), Is.EqualTo(0.0));
    }

    [Test] public void VaporCone_ZeroInDryAir()
        => Assert.That(Condensation.VaporConeIntensity(0.98, 0.3, 30000), Is.EqualTo(0.0));

    [Test] public void VaporCone_PeaksNearMachOneAndIsBounded()
    {
        double peak = Condensation.VaporConeIntensity(0.98, 1.0, 1e6);
        Assert.That(peak, Is.InRange(0.95, 1.0));
        Assert.That(Condensation.VaporConeIntensity(0.9, 1.0, 1e6), Is.LessThan(peak));
        Assert.That(Condensation.VaporConeIntensity(1.1, 1.0, 1e6), Is.LessThan(peak));
    }

    [Test] public void VaporCone_RisesWithDynamicPressureAndSaturates()
    {
        double lo = Condensation.VaporConeIntensity(0.98, 0.9, 2000);
        double mid = Condensation.VaporConeIntensity(0.98, 0.9, 15000);
        double hi = Condensation.VaporConeIntensity(0.98, 0.9, 500000);
        Assert.That(lo, Is.LessThan(mid));
        Assert.That(mid, Is.LessThan(hi));
        Assert.That(hi, Is.LessThanOrEqualTo(1.0));
    }

    // Schumann 1996: cruise at 250 hPa, eta ~ 0.3 gives T_LM ~ -42 C (about -40..-45 C).
    [Test] public void SchmidtAppleman_At250hPa()
        => Assert.That(Condensation.SchmidtApplemanThresholdCelsius(25000, 0.3), Is.InRange(-45.0, -40.0));

    [Test] public void Threshold_RisesWithPressureAndEfficiency()
    {
        double p = Condensation.SchmidtApplemanThresholdCelsius(25000, 0.3);
        Assert.That(Condensation.SchmidtApplemanThresholdCelsius(35000, 0.3), Is.GreaterThan(p));
        Assert.That(Condensation.SchmidtApplemanThresholdCelsius(25000, 0.4), Is.GreaterThan(p));
    }

    [Test] public void ContrailForms_Classification()
    {
        Assert.That(Condensation.ContrailForms(-30, -42, 1.2), Is.EqualTo(ContrailKind.None));
        Assert.That(Condensation.ContrailForms(-50, -42, 0.6), Is.EqualTo(ContrailKind.ShortLived));
        Assert.That(Condensation.ContrailForms(-50, -42, 1.1), Is.EqualTo(ContrailKind.Persistent));
    }
}
