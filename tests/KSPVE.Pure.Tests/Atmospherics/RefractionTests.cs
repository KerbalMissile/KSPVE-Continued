using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class RefractionTests
{
    // Bennett 1982 / Meeus, Astronomical Algorithms ch.16: ~34.5' at the horizon, ~1' at 45 deg.
    [Test] public void Bennett_HorizonAndMidAltitude()
    {
        Assert.That(Refraction.BennettArcminutes(0), Is.EqualTo(34.5).Within(0.5));
        Assert.That(Refraction.BennettArcminutes(45), Is.EqualTo(1.0).Within(0.05));
    }

    // Saemundsson 1986 (true altitude): ~29' at true altitude 0 (apparent 0.48 deg), ~1' at 45 deg.
    [Test] public void Saemundsson_HorizonAndMidAltitude()
    {
        Assert.That(Refraction.SaemundssonArcminutes(0), Is.EqualTo(28.9).Within(1.0));
        Assert.That(Refraction.SaemundssonArcminutes(45), Is.EqualTo(1.0).Within(0.05));
    }

    [Test] public void PressureTemperatureScale_IsUnityAtStandardConditions()
    {
        Assert.That(Refraction.PressureTemperatureScale(101000, 283), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(Refraction.PressureTemperatureScale(50500, 283), Is.EqualTo(0.5).Within(1e-12));
    }

    [Test] public void SettingSun_IsFlattened()
    {
        double ratio = Refraction.SunVerticalFlattening(0.0, 0.2666, 1.0);
        Assert.That(ratio, Is.InRange(0.8, 0.9));
    }

    [Test] public void HighSun_IsNotFlattenedAndZeroScaleIsRound()
    {
        Assert.That(Refraction.SunVerticalFlattening(60, 0.2666, 1.0), Is.EqualTo(1.0).Within(0.005));
        Assert.That(Refraction.SunVerticalFlattening(0, 0.2666, 0.0), Is.EqualTo(1.0).Within(1e-12));
    }
}
