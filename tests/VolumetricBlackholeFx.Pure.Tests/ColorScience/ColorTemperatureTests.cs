using VolumetricBlackholeFx.Pure.ColorScience;
using VolumetricBlackholeFx.Pure.Numerics;
using NUnit.Framework;

namespace VolumetricBlackholeFx.Pure.Tests.ColorScience;

[TestFixture]
public sealed class ColorTemperatureTests
{
    [Test]
    public void D65IsNearNeutral()
    {
        Rgb c = ColorTemperature.ToLinearRec709(6504);
        Assert.That(c.MaxComponent, Is.EqualTo(1.0).Within(1e-12));
        // The Planckian locus at 6504 K sits ~0.005 in y above D65 (Duv ~ +0.003), so it is slightly green: ~6% tolerance.
        Assert.That(c.R, Is.EqualTo(1.0).Within(0.06));
        Assert.That(c.G, Is.EqualTo(1.0).Within(0.06));
        Assert.That(c.B, Is.EqualTo(1.0).Within(0.06));
    }

    [Test]
    public void WarmAndCool()
    {
        Rgb w = ColorTemperature.ToLinearRec709(3000);
        Assert.That(w.R, Is.GreaterThan(w.G));
        Assert.That(w.G, Is.GreaterThan(w.B));
        Rgb c = ColorTemperature.ToLinearRec709(10000);
        Assert.That(c.B, Is.GreaterThan(c.R));
    }

    [Test]
    public void ClampsOutsideRange()
    {
        Assert.That(ColorTemperature.ToLinearRec709(500), Is.EqualTo(ColorTemperature.ToLinearRec709(1667)));
        Assert.That(ColorTemperature.ToLinearRec709(1e6), Is.EqualTo(ColorTemperature.ToLinearRec709(25000)));
    }

    [Test]
    public void PlanckMatchesFormulaAndWien()
    {
        double p = ColorTemperature.PlanckSpectralRadiance(502e-9, 5772);
        Assert.That(p, Is.GreaterThan(ColorTemperature.PlanckSpectralRadiance(400e-9, 5772)));
        Assert.That(p, Is.GreaterThan(ColorTemperature.PlanckSpectralRadiance(900e-9, 5772)));
        double h = 6.62607015e-34, c = 299792458, k = 1.380649e-23, l = 500e-9;
        double expected = 2 * h * c * c / System.Math.Pow(l, 5) / (System.Math.Exp(h * c / (l * k * 5772)) - 1);
        Assert.That(ColorTemperature.PlanckSpectralRadiance(l, 5772), Is.EqualTo(expected).Within(expected * 1e-6));
    }
}
