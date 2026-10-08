using System;
using KSPVE.Pure.Materials;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Materials;

[TestFixture]
public class DfgAndFurnaceTests
{
    private const int Samples = 2048;

    [Test]
    public void Dfg_Values_InRange()
    {
        foreach (double nv in new[] { 0.05, 0.3, 0.7, 1.0 })
        foreach (double r in new[] { 0.05, 0.3, 0.6, 1.0 })
        {
            var (a, b) = DfgLut.Integrate(nv, r, Samples);
            Assert.That(a, Is.InRange(0.0, 1.0), $"A nv={nv} r={r}");
            Assert.That(b, Is.InRange(0.0, 1.0), $"B nv={nv} r={r}");
            Assert.That(a + b, Is.LessThanOrEqualTo(1.0 + 0.01), $"A+B nv={nv} r={r}");
        }
    }

    [TestCase(0.2)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public void Dfg_Smooth_SumsToOne(double nDotV)
    {
        var (a, b) = DfgLut.Integrate(nDotV, 0.02, 4096);
        Assert.That(a + b, Is.EqualTo(1.0).Within(0.02));
    }

    [Test]
    public void Dfg_Albedo_DecreasesWithRoughness_AtGrazing()
    {
        double prev = double.MaxValue;
        foreach (double r in new[] { 0.05, 0.15, 0.3 })
        {
            var (a, b) = DfgLut.Integrate(0.1, r, 4096);
            Assert.That(a + b, Is.LessThan(prev), $"r={r} value={a + b}");
            prev = a + b;
        }
        // Beyond r ~ 0.4 the height-correlated GGX albedo at grazing is not monotonic, but stays below the smooth value.
        var (a1, b1) = DfgLut.Integrate(0.1, 1.0, 4096);
        var (a0, b0) = DfgLut.Integrate(0.1, 0.05, 4096);
        Assert.That(a1 + b1, Is.LessThan(a0 + b0));
    }

    [Test]
    public void Bake_LayoutAndContents()
    {
        float[] lut = DfgLut.Bake(8, 256);
        Assert.That(lut.Length, Is.EqualTo(8 * 8 * 2));
        var (a, b) = DfgLut.Integrate(3.5 / 8, 5.5 / 8, 256);
        int idx = (5 * 8 + 3) * 2;
        Assert.That(lut[idx], Is.EqualTo((float)a).Within(1e-6));
        Assert.That(lut[idx + 1], Is.EqualTo((float)b).Within(1e-6));
    }

    [Test]
    public void EnergyCompensation_WhiteFurnace_F0One()
    {
        foreach (double nv in new[] { 0.1, 0.4, 0.8, 1.0 })
        foreach (double r in new[] { 0.05, 0.25, 0.5, 0.75, 1.0 })
        {
            double e = EnergyCompensation.DirectionalAlbedo(nv, r, Samples);
            double factor = EnergyCompensation.MultiScatterFactor(1.0, e);
            Assert.That(e * factor, Is.EqualTo(1.0).Within(0.02), $"nv={nv} r={r}");
        }
    }

    [Test]
    public void EnergyCompensation_Factor_Properties()
    {
        Assert.That(EnergyCompensation.MultiScatterFactor(0.0, 0.6), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(EnergyCompensation.MultiScatterFactor(0.5, 1.0), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(EnergyCompensation.MultiScatterFactor(0.04, 0.5), Is.EqualTo(1.04).Within(1e-12));
    }

    [TestCase(0.3, 0.6)]
    [TestCase(0.5, 0.6)]
    [TestCase(0.7, 0.9)]
    [TestCase(1.0, 0.5)]
    public void GgxSmith_WhiteFurnace_MatchesDirectionalAlbedo(double perceptualRoughness, double nDotV)
    {
        double alpha = perceptualRoughness * perceptualRoughness;
        double sinV = Math.Sqrt(1 - nDotV * nDotV);
        int nt = 500, np = 200;
        double sum = 0;
        for (int i = 0; i < nt; i++)
        {
            double theta = (i + 0.5) * (Math.PI / 2) / nt;
            double nDotL = Math.Cos(theta), sinL = Math.Sin(theta);
            for (int j = 0; j < np; j++)
            {
                double phi = (j + 0.5) * 2 * Math.PI / np;
                double lx = sinL * Math.Cos(phi), ly = sinL * Math.Sin(phi);
                // H = normalize(V + L), V = (sinV, 0, nDotV)
                double hx = sinV + lx, hy = ly, hz = nDotV + nDotL;
                double len = Math.Sqrt(hx * hx + hy * hy + hz * hz);
                double nDotH = hz / len;
                double d = Ggx.D(nDotH, alpha);
                double vis = Smith.VisibilityHeightCorrelated(nDotV, nDotL, alpha);
                sum += d * vis * nDotL * sinL * (Math.PI / 2 / nt) * (2 * Math.PI / np);
            }
        }
        Assert.That(sum, Is.LessThanOrEqualTo(1.0 + 0.005));
        double e = EnergyCompensation.DirectionalAlbedo(nDotV, perceptualRoughness, 8192);
        Assert.That(sum, Is.EqualTo(e).Within(0.02));
    }
}
