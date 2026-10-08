using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class IceHaloTests
{
    // Ice n ~ 1.31: 22 deg halo (60 deg prism) and 46 deg halo (90 deg prism). Tape & Moilanen 2006.
    [Test] public void MinimumDeviation_60And90()
    {
        Assert.That(IceHalo.MinimumDeviationDegrees(1.31, 60), Is.EqualTo(21.8).Within(0.1));
        Assert.That(IceHalo.MinimumDeviationDegrees(1.31, 90), Is.EqualTo(45.7).Within(0.2));
    }

    [Test] public void Parhelion_22AtHorizon_LargerWhenSunHigher()
    {
        double h0 = IceHalo.ParhelionOffsetDegrees(0, 1.31);
        Assert.That(h0, Is.EqualTo(21.8).Within(0.1));
        double h30 = IceHalo.ParhelionOffsetDegrees(30, 1.31);
        double h50 = IceHalo.ParhelionOffsetDegrees(50, 1.31);
        Assert.That(h30, Is.GreaterThan(h0));
        Assert.That(h50, Is.GreaterThan(h30));
    }

    [Test] public void Parhelion_VanishesWhenSunTooHigh()
        => Assert.That(double.IsNaN(IceHalo.ParhelionOffsetDegrees(80, 1.31)));
}

[TestFixture]
public class CoronaTests
{
    [Test] public void TenMicronDroplets_At550nm_AreThreeToFourDegrees()
    {
        double deg = Corona.FirstRingAngularRadiusRadians(10e-6, 550e-9) * 180 / System.Math.PI;
        Assert.That(deg, Is.InRange(3.0, 4.0));
    }

    [Test] public void SmallerDropletsGiveBiggerRings()
        => Assert.That(Corona.FirstRingAngularRadiusRadians(5e-6, 550e-9),
            Is.GreaterThan(Corona.FirstRingAngularRadiusRadians(10e-6, 550e-9)));
}
