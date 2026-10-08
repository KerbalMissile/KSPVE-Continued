using System;
using KSPVE.Pure.Pipeline;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Pipeline;

[TestFixture]
public sealed class QualityLadderTests
{
    private static QualityLadder Make(int n)
    {
        var l = new QualityLevel[n];
        for (int i = 0; i < n; i++) l[i] = new QualityLevel("L" + i, i + 1.0);
        return new QualityLadder(l);
    }

    [TestCase(1, 0, 0), TestCase(1, 3, 0)]
    [TestCase(2, 0, 0), TestCase(2, 1, 0), TestCase(2, 2, 1), TestCase(2, 3, 1)]
    [TestCase(4, 0, 0), TestCase(4, 1, 1), TestCase(4, 2, 2), TestCase(4, 3, 3)]
    [TestCase(5, 1, 1), TestCase(5, 2, 3), TestCase(5, 3, 4)]
    public void LevelForMapsPresets(int n, int preset, int expected)
        => Assert.That(Make(n).LevelFor((QualityPreset)preset), Is.EqualTo(expected));

    [Test]
    public void ClampBounds()
    {
        var l = Make(3);
        Assert.That(l.Clamp(-4), Is.EqualTo(0));
        Assert.That(l.Clamp(1), Is.EqualTo(1));
        Assert.That(l.Clamp(9), Is.EqualTo(2));
        Assert.That(l.Count, Is.EqualTo(3));
        Assert.That(l[2].Name, Is.EqualTo("L2"));
    }

    [Test]
    public void ValidationThrows()
    {
        Assert.Throws<ArgumentException>(() => new QualityLadder(new QualityLevel[0]));
        Assert.Throws<ArgumentNullException>(() => new QualityLadder(null!));
        Assert.Throws<ArgumentException>(() => new QualityLadder(new[] { new QualityLevel("a", 2), new QualityLevel("b", 1) }));
        Assert.Throws<ArgumentException>(() => new QualityLadder(new[] { new QualityLevel("a", double.NaN) }));
        Assert.Throws<ArgumentException>(() => new QualityLadder(new[] { new QualityLevel("a", -1) }));
    }
}
