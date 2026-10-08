using System;
using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class PhaseFunctionsTests
{
    static double SphereIntegral(Func<double, double> f)
    {
        const int n = 200000;
        double sum = 0, dc = 2.0 / n;
        for (int i = 0; i < n; i++) sum += f(-1 + (i + 0.5) * dc) * dc;
        return 2 * Math.PI * sum;
    }

    [Test] public void Rayleigh_Normalised() => Assert.That(SphereIntegral(PhaseFunctions.Rayleigh), Is.EqualTo(1.0).Within(0.005));

    [TestCase(0.0)] [TestCase(0.5)] [TestCase(-0.4)] [TestCase(0.76)]
    public void HenyeyGreenstein_Normalised(double g)
        => Assert.That(SphereIntegral(c => PhaseFunctions.HenyeyGreenstein(c, g)), Is.EqualTo(1.0).Within(0.005));

    [TestCase(0.0)] [TestCase(0.5)] [TestCase(-0.4)] [TestCase(0.76)]
    public void CornetteShanks_Normalised(double g)
        => Assert.That(SphereIntegral(c => PhaseFunctions.CornetteShanks(c, g)), Is.EqualTo(1.0).Within(0.005));

    [Test] public void HenyeyGreenstein_ZeroG_IsIsotropic()
        => Assert.That(PhaseFunctions.HenyeyGreenstein(0.3, 0), Is.EqualTo(1 / (4 * Math.PI)).Within(1e-12));

    [Test] public void CornetteShanks_ZeroG_EqualsRayleigh()
        => Assert.That(PhaseFunctions.CornetteShanks(0.3, 0), Is.EqualTo(PhaseFunctions.Rayleigh(0.3)).Within(1e-12));

    [Test] public void ForwardScatteringPeaks()
        => Assert.That(PhaseFunctions.HenyeyGreenstein(1, 0.7), Is.GreaterThan(PhaseFunctions.HenyeyGreenstein(-1, 0.7)));

    // Lambert sphere phase function (Russell 1916): Phi(0)=1, Phi(pi/2)=1/pi, Phi(pi)=0.
    [Test] public void LambertSphere_KnownValues()
    {
        Assert.That(PhaseFunctions.LambertSphere(0), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(PhaseFunctions.LambertSphere(Math.PI / 2), Is.EqualTo(1 / Math.PI).Within(1e-12));
        Assert.That(PhaseFunctions.LambertSphere(Math.PI), Is.EqualTo(0.0).Within(1e-12));
    }
}
