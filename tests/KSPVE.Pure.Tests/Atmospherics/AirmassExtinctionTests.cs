using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class AirmassExtinctionTests
{
    // Kasten & Young 1989, Applied Optics 28(22): m(0 deg) ~ 1, m(60 deg) ~ 2, m(90 deg) ~ 38.
    [Test] public void KastenYoung_ZenithAndHorizon()
    {
        Assert.That(Airmass.KastenYoung(0), Is.EqualTo(1.0).Within(1e-3));
        Assert.That(Airmass.KastenYoung(90), Is.EqualTo(38.0).Within(0.3));
        Assert.That(Airmass.KastenYoung(60), Is.EqualTo(2.0).Within(0.01));
    }

    [Test] public void Transmittance_BeerLambert()
    {
        Assert.That(Extinction.Transmittance(0, 0.3), Is.EqualTo(1.0));
        Assert.That(Extinction.Transmittance(1, 1), Is.EqualTo(System.Math.Exp(-1)).Within(1e-12));
        Assert.That(Extinction.Transmittance(38, 0.1), Is.EqualTo(System.Math.Exp(-3.8)).Within(1e-12));
    }
}
