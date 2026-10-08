using System;
using KSPVE.Pure.Space.Procedural;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Procedural;

[TestFixture]
public sealed class Noise3Tests
{
    [Test]
    public void BoundedDeterministicAndVarying()
    {
        double min = 1, max = -1;
        for (int i = 0; i < 4000; i++)
        {
            double x = i * 0.137, y = i * 0.291 - 3, z = i * 0.053 + 1;
            double v = Noise3.Gradient(x, y, z, 5);
            Assert.That(v, Is.InRange(-1.0, 1.0));
            Assert.That(v, Is.EqualTo(Noise3.Gradient(x, y, z, 5)));
            min = Math.Min(min, v); max = Math.Max(max, v);
        }
        Assert.That(max - min, Is.GreaterThan(0.8));
    }

    [Test]
    public void ZeroAtLatticePointsAndSeedChangesField()
    {
        Assert.That(Noise3.Gradient(3, -2, 7, 1), Is.EqualTo(0).Within(1e-12));
        Assert.That(Noise3.Gradient(0.4, 0.5, 0.6, 1), Is.Not.EqualTo(Noise3.Gradient(0.4, 0.5, 0.6, 2)));
    }

    [Test]
    public void IsContinuous()
    {
        double a = Noise3.Gradient(1.3, 2.2, 0.7, 3);
        double b = Noise3.Gradient(1.3 + 1e-6, 2.2, 0.7, 3);
        Assert.That(Math.Abs(a - b), Is.LessThan(1e-4));
    }

    [Test]
    public void FbmIsBoundedAndDeterministic()
    {
        for (int i = 0; i < 500; i++)
        {
            double v = Noise3.Fbm(i * 0.31, i * 0.17, i * 0.07, 4, 2.0, 0.5, 9);
            Assert.That(v, Is.InRange(-1.0, 1.0));
            Assert.That(v, Is.EqualTo(Noise3.Fbm(i * 0.31, i * 0.17, i * 0.07, 4, 2.0, 0.5, 9)));
        }
    }
}
