using System;
using System.Linq;
using KSPVE.Pure.Space.Orbits;
using KSPVE.Pure.Space.Procedural;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Procedural;

[TestFixture]
public sealed class AsteroidBeltGeneratorTests
{
    private const double Au = 1.495978707e11;

    private static BeltDefinition Belt(double p = -1.0, double q = 3.0, BeltClump[] clumps = null) =>
        new BeltDefinition(2.1 * Au, 3.3 * Au, p, 0.08, 0.1, 20000, 1234UL, 1.0, 10.0, q, clumps);

    [Test]
    public void IndexIndependenceAndDeterminism()
    {
        var def = Belt();
        var full = AsteroidBeltGenerator.GenerateRange(def, 0, 100).ToArray();
        var single = AsteroidBeltGenerator.Generate(def, 57);
        Assert.That(full[57].Elements.SemiMajorAxisMeters, Is.EqualTo(single.Elements.SemiMajorAxisMeters));
        Assert.That(full[57].RadiusMeters, Is.EqualTo(single.RadiusMeters));
        Assert.That(full[57].SpinAxis, Is.EqualTo(single.SpinAxis));
        Assert.That(full[57].ShapeVariant, Is.EqualTo(single.ShapeVariant));
        Assert.That(full[57].Index, Is.EqualTo(57));
        var again = AsteroidBeltGenerator.GenerateRange(def, 50, 10).ToArray();
        Assert.That(again[7].Elements.MeanAnomalyAtEpochRadians, Is.EqualTo(full[57].Elements.MeanAnomalyAtEpochRadians));
    }

    [Test]
    public void DifferentSeedsDiffer()
    {
        var a = AsteroidBeltGenerator.Generate(Belt(), 3);
        var b = AsteroidBeltGenerator.Generate(new BeltDefinition(2.1 * Au, 3.3 * Au, -1, 0.08, 0.1, 20000, 999UL, 1, 10, 3), 3);
        Assert.That(a.Elements.SemiMajorAxisMeters, Is.Not.EqualTo(b.Elements.SemiMajorAxisMeters));
    }

    [Test]
    public void ParametersWithinBounds()
    {
        var def = Belt();
        foreach (var r in AsteroidBeltGenerator.GenerateRange(def, 0, 5000))
        {
            Assert.That(r.Elements.SemiMajorAxisMeters, Is.InRange(def.InnerRadiusMeters, def.OuterRadiusMeters));
            Assert.That(r.Elements.Eccentricity, Is.InRange(0.0, 0.95));
            Assert.That(r.RadiusMeters, Is.InRange(1.0, 10.0));
            Assert.That(r.ShapeVariant, Is.InRange(0, 15));
            Assert.That(r.SpinAxis.Length, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(r.SpinRateRadiansPerSecond, Is.GreaterThan(0));
        }
    }

    [Test]
    public void MeanSemiMajorAxisMatchesPowerLaw()
    {
        double p = -1.0;
        var def = Belt(p);
        double lo = def.InnerRadiusMeters, hi = def.OuterRadiusMeters;
        // pdf(a) ~ a^(p+1)
        double analytic = (p + 2) / (p + 3) * (Math.Pow(hi, p + 3) - Math.Pow(lo, p + 3)) / (Math.Pow(hi, p + 2) - Math.Pow(lo, p + 2));
        double mean = AsteroidBeltGenerator.GenerateRange(def, 0, 20000).Average(r => r.Elements.SemiMajorAxisMeters);
        Assert.That(mean, Is.EqualTo(analytic).Within(analytic * 0.03));
    }

    [Test]
    public void MeanRockRadiusMatchesSizePowerLaw()
    {
        double q = 3.0, lo = 1.0, hi = 10.0;
        double analytic = (q - 1) / (q - 2) * (Math.Pow(lo, 2 - q) - Math.Pow(hi, 2 - q)) / (Math.Pow(lo, 1 - q) - Math.Pow(hi, 1 - q));
        double mean = AsteroidBeltGenerator.GenerateRange(Belt(-1, q), 0, 20000).Average(r => r.RadiusMeters);
        Assert.That(mean, Is.EqualTo(analytic).Within(analytic * 0.03));
    }

    [Test]
    public void ClumpsRaiseDensityNearCentre()
    {
        double centre = 1.0, width = 0.1;
        double Frac(BeltClump[] clumps)
        {
            int near = 0;
            foreach (var r in AsteroidBeltGenerator.GenerateRange(Belt(-1, 3, clumps), 0, 20000))
            {
                var e = r.Elements;
                double l = e.MeanAnomalyAtEpochRadians + e.ArgumentOfPeriapsisRadians + e.LongitudeOfAscendingNodeRadians;
                double d = Math.IEEERemainder(l - centre, 2 * Math.PI);
                if (Math.Abs(d) < width) near++;
            }
            return near / 20000.0;
        }
        double uniform = Frac(null);
        double clumped = Frac(new[] { new BeltClump(centre, width, 1.0) });
        Assert.That(uniform, Is.EqualTo(2 * width / (2 * Math.PI)).Within(0.01));
        Assert.That(clumped, Is.GreaterThan(uniform * 4));
    }
}
