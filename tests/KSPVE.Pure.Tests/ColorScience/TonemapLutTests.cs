using KSPVE.Pure.ColorScience;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

[TestFixture]
public sealed class TonemapLutTests
{
    [Test]
    public void ShaperRoundTrip()
    {
        var s = new TonemapLut.Shaper(-8, 8);
        Assert.That(s.Encode(System.Math.Pow(2, -8)), Is.EqualTo(0).Within(1e-12));
        Assert.That(s.Encode(1e-9), Is.EqualTo(0));
        Assert.That(s.Encode(0), Is.EqualTo(0));
        Assert.That(s.Encode(256), Is.EqualTo(1).Within(1e-12));
        Assert.That(s.Encode(1e9), Is.EqualTo(1));
        Assert.That(s.Encode(1), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(s.Decode(0.5), Is.EqualTo(1).Within(1e-12));
        Assert.That(s.Decode(s.Encode(3.7)), Is.EqualTo(3.7).Within(1e-9));
    }

    [Test]
    public void LayoutIsRedFastest()
    {
        const int n = 4;
        var s = new TonemapLut.Shaper(-6, 6);
        float[] lut = TonemapLut.Bake(n, c => c, -6, 6);
        Assert.That(lut.Length, Is.EqualTo(n * n * n * 4));
        int r = 1, g = 2, b = 3;
        int idx = ((b * n + g) * n + r) * 4;
        Assert.That(lut[idx + 0], Is.EqualTo((float)s.Decode(r / 3.0)).Within(1e-6));
        Assert.That(lut[idx + 1], Is.EqualTo((float)s.Decode(g / 3.0)).Within(1e-6));
        Assert.That(lut[idx + 2], Is.EqualTo((float)s.Decode(b / 3.0)).Within(1e-6));
        for (int i = 3; i < lut.Length; i += 4) Assert.That(lut[i], Is.EqualTo(1f));
    }

    [Test]
    public void BakedAgXMatchesDirect()
    {
        const int n = 9;
        var s = new TonemapLut.Shaper(-10, 6);
        float[] lut = TonemapLut.Bake(n, AgX.Apply, -10, 6);
        for (int b = 0; b < n; b++)
            for (int g = 0; g < n; g += 2)
                for (int r = 0; r < n; r += 3)
                {
                    Rgb expected = AgX.Apply(new Rgb(s.Decode(r / 8.0), s.Decode(g / 8.0), s.Decode(b / 8.0)));
                    int i = ((b * n + g) * n + r) * 4;
                    Assert.That(lut[i], Is.EqualTo((float)expected.R).Within(1e-6));
                    Assert.That(lut[i + 1], Is.EqualTo((float)expected.G).Within(1e-6));
                    Assert.That(lut[i + 2], Is.EqualTo((float)expected.B).Within(1e-6));
                }
    }

    [Test]
    public void RejectsBadSize() =>
        Assert.Throws<System.ArgumentOutOfRangeException>(() => TonemapLut.Bake(1, c => c, -4, 4));
}
