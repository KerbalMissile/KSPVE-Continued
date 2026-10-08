using KSPVE.Pure.Space.Procedural;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Procedural;

[TestFixture]
public sealed class Pcg32Tests
{
    [Test]
    public void MatchesPublishedDemoOutput()
    {
        // pcg-c "pcg32-demo" output for pcg32_srandom_r(&rng, 42u, 54u) (pcg-random.org).
        var rng = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (uint e in expected) Assert.That(rng.NextUInt(), Is.EqualTo(e));
    }

    [Test]
    public void DoublesAreInUnitInterval()
    {
        var rng = new Pcg32(1, 2);
        for (int i = 0; i < 10000; i++)
        {
            double d = rng.NextDouble();
            Assert.That(d, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
        }
    }

    [Test]
    public void GaussianHasUnitMoments()
    {
        var rng = new Pcg32(7, 9);
        double sum = 0, sq = 0;
        const int n = 50000;
        for (int i = 0; i < n; i++) { double g = rng.NextGaussian(); sum += g; sq += g * g; }
        Assert.That(sum / n, Is.EqualTo(0).Within(0.03));
        Assert.That(sq / n, Is.EqualTo(1).Within(0.04));
    }

    [Test]
    public void SameSeedSameSequence()
    {
        var a = new Pcg32(5, 6); var b = new Pcg32(5, 6);
        for (int i = 0; i < 100; i++) Assert.That(a.NextGaussian(), Is.EqualTo(b.NextGaussian()));
    }
}
