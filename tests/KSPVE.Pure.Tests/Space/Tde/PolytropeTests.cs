using KSPVE.Pure.Space.Tde;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Tde;

public class PolytropeTests
{
    // First zeros and central-to-mean densities (Chandrasekhar 1939, table 4).
    [TestCase(0.0, 2.44949, 1.0)]
    [TestCase(1.0, 3.14159, 3.28987)]
    [TestCase(1.5, 3.65375, 5.99071)]
    [TestCase(3.0, 6.89685, 54.1825)]
    public void MatchesTheLaneEmdenTables(double n, double xi1, double centralToMean)
    {
        var star = new Polytrope(n);
        Assert.That(star.FirstZero, Is.EqualTo(xi1).Within(0.05).Percent);
        Assert.That(star.CentralToMeanDensity, Is.EqualTo(centralToMean).Within(0.3).Percent);
    }

    [Test]
    public void SunLikeStar_KeepsMostOfItsMassInItsInnerHalf()
    {
        var star = new Polytrope(3.0);
        Assert.That(star.EnclosedMass(0.0), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(star.EnclosedMass(1.0), Is.EqualTo(1.0));
        Assert.That(star.EnclosedMass(0.5), Is.GreaterThan(0.85), "an n = 3 star is centrally condensed");
        Assert.That(star.Density(0.0), Is.EqualTo(1.0).Within(1e-6));
        Assert.That(star.Density(1.0), Is.EqualTo(0.0));
    }
}

public class DisruptionFitsTests
{
    [Test]
    public void Gamma43AtBetaTwo_MatchesThePaper()
    {
        Assert.That(DisruptionFits.PeakRateCoefficient(2.0, 3.0), Is.EqualTo(3.034).Within(0.5).Percent);
        Assert.That(DisruptionFits.PeakTimeCoefficient(2.0, 3.0), Is.EqualTo(0.07725).Within(0.5).Percent);
        Assert.That(DisruptionFits.MassLost(2.0, 3.0), Is.EqualTo(1.0));
        Assert.That(DisruptionFits.FullDisruptionBeta(1.5), Is.EqualTo(0.9));
    }

    // The family spans (0.12, 1/3]: from concentrated (w -> infinity) to flat (w = 0).
    [TestCase(0.13)]
    [TestCase(0.2)]
    [TestCase(0.3)]
    public void EnergyShape_InvertsThePeakProduct(double product)
    {
        double w = DisruptionFits.EnergyShape(product);
        Assert.That(DisruptionFits.PeakProduct(w), Is.EqualTo(product).Within(1e-6).Percent);
    }

    [Test]
    public void FlatDistribution_IsTheWidestPossible()
    {
        Assert.That(DisruptionFits.PeakProduct(0.0), Is.EqualTo(1.0 / 3.0).Within(1e-3).Percent);
        Assert.That(DisruptionFits.EnergyShape(0.5), Is.EqualTo(0.0));
    }
}
