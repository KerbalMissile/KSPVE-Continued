using KSPVE.Pure.ColorScience;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

[TestFixture]
public sealed class LuminanceHistogramTests
{
    [Test]
    public void BinMapping()
    {
        var h = new LuminanceHistogram(16, -8, 8); // 1 log2 unit per bin
        Assert.That(h.BinFor(0), Is.EqualTo(0));
        Assert.That(h.BinFor(-5), Is.EqualTo(0));
        Assert.That(h.BinFor(System.Math.Pow(2, -8)), Is.EqualTo(0));
        Assert.That(h.BinFor(System.Math.Pow(2, -7.5)), Is.EqualTo(0));
        Assert.That(h.BinFor(System.Math.Pow(2, -6.5)), Is.EqualTo(1));
        Assert.That(h.BinFor(1.0), Is.EqualTo(8));
        Assert.That(h.BinFor(1e12), Is.EqualTo(15));
        Assert.That(h.BinCenterLog2(0), Is.EqualTo(-7.5).Within(1e-12));
        Assert.That(h.BinCenterLog2(15), Is.EqualTo(7.5).Within(1e-12));
    }

    [Test]
    public void SingleValueAverage()
    {
        var h = new LuminanceHistogram(16, -8, 8);
        h.Add(1.0, 5);
        Assert.That(h.AverageLog2InPercentileRange(0, 100), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(h.AverageLog2InPercentileRange(40, 60), Is.EqualTo(0.5).Within(1e-12));
    }

    [Test]
    public void OutliersAreIgnored()
    {
        var h = new LuminanceHistogram(16, -8, 8);
        h.Add(System.Math.Pow(2, -7.5), 10);  // dark 10%
        h.Add(System.Math.Pow(2, 0.5), 80);   // bin 8, centre 0.5
        h.Add(System.Math.Pow(2, 7.5), 10);   // bright 10%
        Assert.That(h.AverageLog2InPercentileRange(10, 90), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(h.AverageLog2InPercentileRange(0, 90), Is.EqualTo((10 * -7.5 + 80 * 0.5) / 90.0).Within(1e-12));
    }

    [Test]
    public void PartialBinsAreFractional()
    {
        var h = new LuminanceHistogram(16, -8, 8);
        h.Add(System.Math.Pow(2, -7.5), 10);
        h.Add(System.Math.Pow(2, 0.5), 10);
        // total 20; skip lowest 25% (5 units) -> 5 of dark bin + 10 of bright bin
        double expected = (5 * -7.5 + 10 * 0.5) / 15.0;
        Assert.That(h.AverageLog2InPercentileRange(25, 100), Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void ClearAndWeights()
    {
        var h = new LuminanceHistogram(8, -4, 4);
        h.Add(1.0);
        h.Add(1.0, 2);
        h.Clear();
        h.Add(4.0, 1);
        Assert.That(h.AverageLog2InPercentileRange(0, 100), Is.EqualTo(h.BinCenterLog2(h.BinFor(4.0))).Within(1e-12));
    }
}
