using KSPVE.Pure.Numerics;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Numerics;

public class Double3Tests
{
    [Test]
    public void DotAndCross_FollowRightHandedConvention()
    {
        Assert.That(Double3.Dot(new Double3(1, 2, 3), new Double3(4, -5, 6)), Is.EqualTo(12.0));
        Assert.That(Double3.Cross(Double3.UnitX, Double3.UnitY), Is.EqualTo(Double3.UnitZ));
    }

    [Test]
    public void Normalized_HasUnitLength_AndZeroStaysZero()
    {
        Assert.That(new Double3(3, 4, 12).Normalized().Length, Is.EqualTo(1.0).Within(1e-15));
        Assert.That(Double3.Zero.Normalized(), Is.EqualTo(Double3.Zero));
    }

    [Test]
    public void Operators_AreComponentWise()
    {
        var a = new Double3(1, 2, 3);
        var b = new Double3(4, 5, 6);
        Assert.That(a + b, Is.EqualTo(new Double3(5, 7, 9)));
        Assert.That(b - a, Is.EqualTo(new Double3(3, 3, 3)));
        Assert.That(2.0 * a, Is.EqualTo(new Double3(2, 4, 6)));
        Assert.That(b / 2.0, Is.EqualTo(new Double3(2, 2.5, 3)));
        Assert.That(Double3.Lerp(a, b, 0.5), Is.EqualTo(new Double3(2.5, 3.5, 4.5)));
    }
}
