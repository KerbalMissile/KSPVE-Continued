using System;
using KSPVE.Pure.Materials;
using KSPVE.Pure.Numerics;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Materials;

[TestFixture]
public class MicrofacetTests
{
    // Midpoint quadrature of an azimuthally symmetric integrand over the hemisphere.
    private static double IntegrateHemisphere(Func<double, double> perCosTheta, int steps = 20000)
    {
        double sum = 0.0;
        double dTheta = Math.PI / 2 / steps;
        for (int i = 0; i < steps; i++)
        {
            double theta = (i + 0.5) * dTheta;
            sum += perCosTheta(Math.Cos(theta)) * Math.Sin(theta) * dTheta;
        }
        return sum * 2.0 * Math.PI;
    }

    [TestCase(0.3)]
    [TestCase(0.5)]
    [TestCase(1.0)]
    public void GgxD_Normalises_ProjectedArea(double alpha)
    {
        // Walter et al. 2007: integral of D(m) (n.m) dω = 1.
        double integral = IntegrateHemisphere(c => Ggx.D(c, alpha) * c);
        Assert.That(integral, Is.EqualTo(1.0).Within(0.005));
    }

    [Test]
    public void GgxD_KnownValue_AtNormal()
    {
        // D(1) = 1/(π α²)
        Assert.That(Ggx.D(1.0, 0.5), Is.EqualTo(1.0 / (Math.PI * 0.25)).Within(1e-12));
    }

    [Test]
    public void GgxAnisotropic_ReducesToIsotropic_WhenAlphasEqual()
    {
        double alpha = 0.4, nDotH = 0.8;
        double sin = Math.Sqrt(1 - nDotH * nDotH);
        double hT = sin * Math.Cos(0.7), hB = sin * Math.Sin(0.7);
        Assert.That(Ggx.DAnisotropic(nDotH, hT, hB, alpha, alpha), Is.EqualTo(Ggx.D(nDotH, alpha)).Within(1e-10));
    }

    [Test]
    public void GgxAnisotropic_Normalises()
    {
        double alphaT = 0.6, alphaB = 0.2;
        int nt = 600, np = 360;
        double sum = 0;
        for (int i = 0; i < nt; i++)
        {
            double theta = (i + 0.5) * (Math.PI / 2) / nt;
            double c = Math.Cos(theta), s = Math.Sin(theta);
            for (int j = 0; j < np; j++)
            {
                double phi = (j + 0.5) * 2 * Math.PI / np;
                sum += Ggx.DAnisotropic(c, s * Math.Cos(phi), s * Math.Sin(phi), alphaT, alphaB)
                       * c * s * (Math.PI / 2 / nt) * (2 * Math.PI / np);
            }
        }
        Assert.That(sum, Is.EqualTo(1.0).Within(0.01));
    }

    [Test]
    public void SampleHalfVector_IsUnitLength_AndInUpperHemisphere()
    {
        for (int i = 0; i < 64; i++)
        {
            var (u, v) = Sampling.Hammersley(i, 64);
            Double3 h = Ggx.SampleHalfVector(u, v, 0.3);
            Assert.That(h.Length, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(h.Z, Is.GreaterThanOrEqualTo(0.0));
        }
    }

    [Test]
    public void SampleHalfVector_MeanCosine_MatchesAnalyticDistribution()
    {
        double alpha = 0.5;
        double expected = IntegrateHemisphere(c => Ggx.D(c, alpha) * c * c);
        int n = 20000;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var (u, v) = Sampling.Hammersley(i, n);
            sum += Ggx.SampleHalfVector(u, v, alpha).Z;
        }
        Assert.That(sum / n, Is.EqualTo(expected).Within(0.005));
    }

    [Test]
    public void SmithVisibility_Smooth_ReducesToOneOverFourNlNv()
    {
        double v = Smith.VisibilityHeightCorrelated(0.7, 0.4, 1e-4);
        Assert.That(v, Is.EqualTo(1.0 / (4 * 0.7 * 0.4)).Within(1e-3));
    }

    [Test]
    public void SmithVisibility_IsSymmetric_AndPositive()
    {
        double a = Smith.VisibilityHeightCorrelated(0.3, 0.8, 0.25);
        double b = Smith.VisibilityHeightCorrelated(0.8, 0.3, 0.25);
        Assert.That(a, Is.EqualTo(b).Within(1e-12));
        Assert.That(a, Is.GreaterThan(0));
    }

    [Test]
    public void Fresnel_Schlick_Endpoints()
    {
        // Schlick 1994
        Assert.That(Fresnel.Schlick(0.04, 1.0), Is.EqualTo(0.04).Within(1e-12));
        Assert.That(Fresnel.Schlick(0.04, 0.0), Is.EqualTo(1.0).Within(1e-12));
        Assert.That(Fresnel.SchlickF90(0.04, 0.5, 0.0), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(Fresnel.SchlickF90(0.04, 0.5, 1.0), Is.EqualTo(0.04).Within(1e-12));
    }

    [Test]
    public void Diffuse_Lambert_IsOneOverPi() =>
        Assert.That(Diffuse.Lambert(), Is.EqualTo(1.0 / Math.PI).Within(1e-15));

    [Test]
    public void Diffuse_Burley_Properties()
    {
        // Burley 2012: at normal incidence/view the factor is 1 (times 1/π).
        Assert.That(Diffuse.Burley(1.0, 1.0, 1.0, 0.5), Is.EqualTo(1.0 / Math.PI).Within(1e-12));
        // Rough surface shows retro-reflection at grazing: brighter than Lambert.
        Assert.That(Diffuse.Burley(0.1, 0.1, 1.0, 1.0), Is.GreaterThan(1.0 / Math.PI));
        // Smooth surface darkens at grazing (fd90 = 0.5).
        Assert.That(Diffuse.Burley(0.1, 0.1, 0.5, 0.0), Is.LessThan(1.0 / Math.PI));
    }

    [Test]
    public void Sampling_RadicalInverse_KnownValues()
    {
        Assert.That(Sampling.RadicalInverseBase2(0), Is.EqualTo(0.0));
        Assert.That(Sampling.RadicalInverseBase2(1), Is.EqualTo(0.5));
        Assert.That(Sampling.RadicalInverseBase2(2), Is.EqualTo(0.25));
        Assert.That(Sampling.RadicalInverseBase2(3), Is.EqualTo(0.75));
        var (u, v) = Sampling.Hammersley(3, 8);
        Assert.That(u, Is.EqualTo(3.0 / 8).Within(1e-15));
        Assert.That(v, Is.EqualTo(0.75).Within(1e-15));
    }

    [Test]
    public void Sampling_CosineHemisphere_MeanCosineIsTwoThirds()
    {
        // E[cosθ] under a cosine-weighted pdf = 2/3.
        int n = 4096;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var (u, v) = Sampling.Hammersley(i, n);
            Double3 d = Sampling.CosineHemisphere(u, v);
            Assert.That(d.Length, Is.EqualTo(1.0).Within(1e-9));
            sum += d.Z;
        }
        Assert.That(sum / n, Is.EqualTo(2.0 / 3.0).Within(0.005));
    }
}
