using KSPVE.Pure.ColorScience;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

[TestFixture]
public sealed class RgbTests
{
    [Test]
    public void Arithmetic()
    {
        var a = new Rgb(1, 2, 3);
        var b = new Rgb(4, 5, 6);
        Assert.That(a + b, Is.EqualTo(new Rgb(5, 7, 9)));
        Assert.That(b - a, Is.EqualTo(new Rgb(3, 3, 3)));
        Assert.That(a * 2, Is.EqualTo(new Rgb(2, 4, 6)));
        Assert.That(2 * a, Is.EqualTo(new Rgb(2, 4, 6)));
        Assert.That(a * b, Is.EqualTo(new Rgb(4, 10, 18)));
        Assert.That(b / 2, Is.EqualTo(new Rgb(2, 2.5, 3)));
        Assert.That(b / a, Is.EqualTo(new Rgb(4, 2.5, 2)));
        Assert.That(a == new Rgb(1, 2, 3), Is.True);
        Assert.That(a != b, Is.True);
    }

    [Test]
    public void LuminanceAndMax()
    {
        Assert.That(new Rgb(1, 1, 1).Luminance, Is.EqualTo(1.0).Within(1e-12));
        Assert.That(new Rgb(1, 0, 0).Luminance, Is.EqualTo(0.2126).Within(1e-12));
        Assert.That(new Rgb(0, 1, 0).Luminance, Is.EqualTo(0.7152).Within(1e-12));
        Assert.That(new Rgb(0, 0, 1).Luminance, Is.EqualTo(0.0722).Within(1e-12));
        Assert.That(new Rgb(0.2, 0.9, 0.5).MaxComponent, Is.EqualTo(0.9));
    }
}

[TestFixture]
public sealed class TransferTests
{
    // IEC 61966-2-1: threshold 0.04045 / 0.0031308, slope 12.92.
    [TestCase(0.0, 0.0)]
    [TestCase(1.0, 1.0)]
    [TestCase(0.04045, 0.04045 / 12.92)]
    [TestCase(0.5, 0.2140411404)]   // well-known sRGB 50% -> 0.214041
    [TestCase(0.2, 0.0331047)]
    public void SrgbToLinear(double v, double expected)
        => Assert.That(Transfer.SrgbToLinear(v), Is.EqualTo(expected).Within(1e-7));

    [Test]
    public void SrgbRoundTrip()
    {
        for (int i = 0; i <= 100; i++)
        {
            double v = i / 100.0;
            Assert.That(Transfer.LinearToSrgb(Transfer.SrgbToLinear(v)), Is.EqualTo(v).Within(1e-12));
        }
        Assert.That(Transfer.LinearToSrgb(0.0031308), Is.EqualTo(12.92 * 0.0031308).Within(1e-6));
    }

    [Test]
    public void Gamma()
    {
        Assert.That(Transfer.GammaToLinear(0.5), Is.EqualTo(System.Math.Pow(0.5, 2.2)).Within(1e-12));
        Assert.That(Transfer.LinearToGamma(Transfer.GammaToLinear(0.3, 2.4), 2.4), Is.EqualTo(0.3).Within(1e-12));
    }
}

[TestFixture]
public sealed class ExposureTests
{
    [Test]
    public void Sunny16() =>
        Assert.That(Exposure.Ev100FromCamera(16, 1.0 / 100.0, 100), Is.EqualTo(14.64).Within(0.01));

    [Test]
    public void IsoShiftsEv() =>
        Assert.That(Exposure.Ev100FromCamera(4, 1.0, 400), Is.EqualTo(Exposure.Ev100FromCamera(4, 1.0, 100) - 2).Within(1e-12));

    [Test]
    public void FromLuminance()
    {
        Assert.That(Exposure.Ev100FromAverageLuminance(0.125), Is.EqualTo(0.0).Within(1e-12));
        Assert.That(Exposure.Ev100FromAverageLuminance(12.5), Is.EqualTo(System.Math.Log(100, 2)).Within(1e-12));
    }

    [Test]
    public void MaxLuminanceAndScale()
    {
        Assert.That(Exposure.MaxLuminanceFromEv100(0), Is.EqualTo(1.2).Within(1e-12));
        Assert.That(Exposure.MaxLuminanceFromEv100(3), Is.EqualTo(9.6).Within(1e-12));
        Assert.That(Exposure.ExposureScale(3), Is.EqualTo(1 / 9.6).Within(1e-12));
    }
}

[TestFixture]
public sealed class AdaptationTests
{
    [Test]
    public void ExponentialApproach()
    {
        double r = Adaptation.Step(0, 10, 1.0, 2.0, 1.0);
        Assert.That(r, Is.EqualTo(10 * (1 - System.Math.Exp(-2))).Within(1e-12));
    }

    [Test]
    public void UsesDirectionalSpeed()
    {
        double r = Adaptation.Step(10, 0, 1.0, 2.0, 0.5);
        Assert.That(r, Is.EqualTo(10 - 10 * (1 - System.Math.Exp(-0.5))).Within(1e-12));
    }

    [Test]
    public void NeverOvershootsAndZeroDt()
    {
        double r = Adaptation.Step(0, 1, 1000, 50, 50);
        Assert.That(r, Is.LessThanOrEqualTo(1.0));
        Assert.That(Adaptation.Step(3, 9, 0, 1, 1), Is.EqualTo(3));
        Assert.That(Adaptation.Step(3, 9, -1, 1, 1), Is.EqualTo(3));
    }
}
