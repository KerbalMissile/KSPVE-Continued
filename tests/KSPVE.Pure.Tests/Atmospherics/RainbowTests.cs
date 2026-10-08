using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class RainbowTests
{
    // Primary bow ~42 deg, secondary ~51 deg (Descartes 1637; Nussenzveig, Sci. Am. 1977).
    [Test] public void PrimaryAndSecondary_Angles()
    {
        Assert.That(Rainbow.RainbowAngleDegrees(1.333, 1), Is.EqualTo(42.0).Within(0.5));
        Assert.That(Rainbow.RainbowAngleDegrees(1.333, 2), Is.EqualTo(51.0).Within(0.5));
    }

    [Test] public void MinimumDeviation_Primary()
        => Assert.That(Rainbow.MinimumDeviationDegrees(1.333, 1), Is.EqualTo(138.0).Within(0.5));

    [Test] public void RedIsOutsideVioletOnPrimary()
    {
        double red = Rainbow.RainbowAngleDegrees(Rainbow.WaterRefractiveIndex(650), 1);
        double violet = Rainbow.RainbowAngleDegrees(Rainbow.WaterRefractiveIndex(420), 1);
        Assert.That(red, Is.GreaterThan(violet));
        Assert.That(red, Is.EqualTo(42.4).Within(0.3));
        Assert.That(violet, Is.EqualTo(40.6).Within(0.4));
    }

    [Test] public void SecondaryIsReversed_VioletOutside()
    {
        double red = Rainbow.RainbowAngleDegrees(Rainbow.WaterRefractiveIndex(650), 2);
        double violet = Rainbow.RainbowAngleDegrees(Rainbow.WaterRefractiveIndex(420), 2);
        Assert.That(violet, Is.GreaterThan(red));
    }

    // Water at 20 C: n(589 nm) = 1.333, n(400) ~ 1.343, n(700) ~ 1.331 (Daimon & Masumura 2007).
    [Test] public void WaterIndex_Values()
    {
        Assert.That(Rainbow.WaterRefractiveIndex(589), Is.EqualTo(1.333).Within(0.001));
        Assert.That(Rainbow.WaterRefractiveIndex(400), Is.EqualTo(1.343).Within(0.002));
        Assert.That(Rainbow.WaterRefractiveIndex(700), Is.EqualTo(1.331).Within(0.002));
    }

    [Test] public void InvalidReflectionCount_Throws()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => Rainbow.MinimumDeviationDegrees(1.333, 0));
}
