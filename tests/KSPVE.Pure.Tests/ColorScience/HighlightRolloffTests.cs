using KSPVE.Pure.ColorScience;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

public class HighlightRolloffTests
{
    [Test]
    public void IsIdentityBelowTheKnee()
    {
        var c = new Rgb(0.02, 0.3, 0.7);
        Assert.That(HighlightRolloff.Apply(c), Is.EqualTo(c));
    }

    [Test]
    public void CompressesHdrHighlightsBelowOne_AndIsMonotonic()
    {
        double previous = 0;
        for (double v = 0.8; v < 1000; v *= 1.5)
        {
            double o = HighlightRolloff.Apply(new Rgb(v, v, v)).R;
            Assert.That(o, Is.LessThan(1.0));
            Assert.That(o, Is.GreaterThan(previous));
            previous = o;
        }
    }

    [Test]
    public void DesaturatesTowardWhiteAsHighlightsCompress()
    {
        Rgb o = HighlightRolloff.Apply(new Rgb(8.0, 2.0, 1.0));
        Assert.That(o.MaxComponent, Is.LessThan(1.0));
        // blue rises relative to red compared with the input ratio of 1/8
        Assert.That(o.B / o.R, Is.GreaterThan(1.0 / 8.0));
    }
}
