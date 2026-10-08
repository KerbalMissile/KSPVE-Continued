using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Numerics;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

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

[TestFixture]
public sealed class ChromaticAdaptationTests
{
    [Test]
    public void BradfordIdentityWhenSame()
    {
        var w = new Double3(0.95047, 1.0, 1.08883);
        double[,] m = ChromaticAdaptation.BradfordMatrix(w, w);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.That(m[i, j], Is.EqualTo(i == j ? 1.0 : 0.0).Within(1e-12));
    }

    [Test]
    public void BradfordD50ToD65()
    {
        // Lindbloom (brucelindbloom.com) Bradford D50->D65 row 0: 0.9555766 -0.0230393 0.0631636
        var d50 = new Double3(0.96422, 1.0, 0.82521);
        var d65 = new Double3(0.95047, 1.0, 1.08883);
        double[,] m = ChromaticAdaptation.BradfordMatrix(d50, d65);
        Assert.That(m[0, 0], Is.EqualTo(0.9555766).Within(2e-4));
        Assert.That(m[0, 1], Is.EqualTo(-0.0230393).Within(2e-4));
        Assert.That(m[0, 2], Is.EqualTo(0.0631636).Within(2e-4));
        double x = m[0, 0] * d50.X + m[0, 1] * d50.Y + m[0, 2] * d50.Z;
        double z = m[2, 0] * d50.X + m[2, 1] * d50.Y + m[2, 2] * d50.Z;
        Assert.That(x, Is.EqualTo(d65.X).Within(1e-9));
        Assert.That(z, Is.EqualTo(d65.Z).Within(1e-9));
    }

    [Test]
    public void WhiteBalanceIdentity()
    {
        var c = new Rgb(0.2, 0.5, 0.7);
        Rgb o = ChromaticAdaptation.WhiteBalance(c, 5000, 5000);
        Assert.That(o.R, Is.EqualTo(c.R).Within(1e-6));
        Assert.That(o.G, Is.EqualTo(c.G).Within(1e-6));
        Assert.That(o.B, Is.EqualTo(c.B).Within(1e-6));
    }

    [Test]
    public void WarmLitWhiteBecomesNeutral()
    {
        Rgb lit = ColorTemperature.ToLinearRec709(3000);
        Rgb o = ChromaticAdaptation.WhiteBalance(lit, 3000);
        // Becomes the target illuminant's own colour (6504 K Planckian, itself ~2-6% off D65 in Rec.709).
        Rgb n = o / o.MaxComponent;
        Rgb expected = ColorTemperature.ToLinearRec709(6504);
        Assert.That(n.R, Is.EqualTo(expected.R).Within(0.005));
        Assert.That(n.G, Is.EqualTo(expected.G).Within(0.005));
        Assert.That(n.B, Is.EqualTo(expected.B).Within(0.005));
    }
}
