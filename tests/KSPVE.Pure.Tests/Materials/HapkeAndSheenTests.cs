using System;
using KSPVE.Pure.Materials;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Materials;

[TestFixture]
public class HapkeAndSheenTests
{
    // Typical lunar parameters, Hapke 1993 / Helfenstein & Veverka 1987.
    private const double W = 0.25, B0 = 1.0, H = 0.06, G = -0.25;

    [Test]
    public void Hapke_OppositionSurge()
    {
        double at0 = Hapke.Reflectance(0.7, 0.8, 0.0, W, B0, H, G);
        double at10 = Hapke.Reflectance(0.7, 0.8, 10 * Math.PI / 180, W, B0, H, G);
        Assert.That(at0, Is.GreaterThan(at10));
    }

    [Test]
    public void Hapke_Positive()
    {
        foreach (double mu0 in new[] { 0.05, 0.5, 1.0 })
        foreach (double mu in new[] { 0.05, 0.5, 1.0 })
        foreach (double gDeg in new[] { 0.0, 30.0, 90.0, 150.0 })
            Assert.That(Hapke.Reflectance(mu0, mu, gDeg * Math.PI / 180, W, B0, H, G), Is.GreaterThan(0));
    }

    [Test]
    public void Hapke_FlatFullMoon()
    {
        // At zero phase mu0 = mu. The Lommel-Seeliger term mu0/(mu0+mu) = 1/2 makes the radiance r
        // (which already includes the mu0 factor) nearly independent of mu: the lunar disc looks flat
        // at full moon (Hapke 1993).
        double min = double.MaxValue, max = double.MinValue;
        for (double mu = 0.2; mu <= 1.0001; mu += 0.05)
        {
            double r = Hapke.Reflectance(mu, mu, 0.0, W, B0, H, G);
            min = Math.Min(min, r);
            max = Math.Max(max, r);
        }
        Assert.That((max - min) / max, Is.LessThan(0.2));
    }

    [Test]
    public void Hapke_DarkerWithLowerAlbedo()
    {
        double bright = Hapke.Reflectance(0.6, 0.6, 0.5, 0.5, B0, H, G);
        double dark = Hapke.Reflectance(0.6, 0.6, 0.5, 0.1, B0, H, G);
        Assert.That(bright, Is.GreaterThan(dark));
    }

    [TestCase(0.3)]
    [TestCase(0.6)]
    [TestCase(1.0)]
    public void Sheen_CharlieD_Normalises(double perceptualRoughness)
    {
        // Estevez & Kulla 2017: D is normalised over the projected hemisphere.
        int steps = 20000;
        double sum = 0, dTheta = Math.PI / 2 / steps;
        for (int i = 0; i < steps; i++)
        {
            double theta = (i + 0.5) * dTheta;
            double c = Math.Cos(theta);
            sum += Sheen.CharlieD(c, perceptualRoughness) * c * Math.Sin(theta) * dTheta;
        }
        Assert.That(sum * 2 * Math.PI, Is.EqualTo(1.0).Within(0.02));
    }

    [Test]
    public void Sheen_NeubeltVisibility_KnownValue()
    {
        Assert.That(Sheen.NeubeltVisibility(0.5, 0.5), Is.EqualTo(1.0 / (4 * 0.75)).Within(1e-12));
    }
}
