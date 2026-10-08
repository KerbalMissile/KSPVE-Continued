using NUnit.Framework;
using KSPVE.Pure.Atmospherics;

namespace KSPVE.Pure.Tests.Atmospherics;

[TestFixture]
public class EclipseTests
{
    [Test] public void NoOverlap_IsOne()
    {
        Assert.That(Eclipse.VisibleFractionUniform(1, 1, 2.5), Is.EqualTo(1.0));
        Assert.That(Eclipse.VisibleFractionLimbDarkened(1, 1, 2.5, 0.6, 100), Is.EqualTo(1.0));
    }

    [Test] public void Total_IsZero()
    {
        Assert.That(Eclipse.VisibleFractionUniform(1, 1.05, 0.02), Is.EqualTo(0.0).Within(1e-12));
        Assert.That(Eclipse.VisibleFractionLimbDarkened(1, 1.05, 0.02, 0.6, 200), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test] public void Annular_Uniform()
        => Assert.That(Eclipse.VisibleFractionUniform(1, 0.9, 0.0), Is.EqualTo(1 - 0.81).Within(1e-12));

    // Two equal unit circles at separation 1: lens area = 2*pi/3 - sqrt(3)/2.
    [Test] public void EqualCircles_LensArea()
    {
        double lens = 2 * System.Math.PI / 3 - System.Math.Sqrt(3) / 2;
        Assert.That(Eclipse.VisibleFractionUniform(1, 1, 1), Is.EqualTo(1 - lens / System.Math.PI).Within(1e-12));
    }

    [Test] public void LimbDarkened_BelowUniform_ForCentralOcclusion()
    {
        double uni = Eclipse.VisibleFractionUniform(1, 0.8, 0.1);
        double ld = Eclipse.VisibleFractionLimbDarkened(1, 0.8, 0.1, 0.6, 2000);
        Assert.That(ld, Is.LessThan(uni));
    }

    [Test] public void LimbDarkened_WithZeroU_MatchesUniform()
        => Assert.That(Eclipse.VisibleFractionLimbDarkened(1, 0.8, 0.5, 0.0, 4000),
            Is.EqualTo(Eclipse.VisibleFractionUniform(1, 0.8, 0.5)).Within(1e-3));

    [Test] public void LimbDarkened_Converges()
    {
        double r1000 = Eclipse.VisibleFractionLimbDarkened(1, 0.9, 0.4, 0.6, 1000);
        double r4000 = Eclipse.VisibleFractionLimbDarkened(1, 0.9, 0.4, 0.6, 4000);
        double r16000 = Eclipse.VisibleFractionLimbDarkened(1, 0.9, 0.4, 0.6, 16000);
        Assert.That(System.Math.Abs(r4000 - r16000), Is.LessThan(System.Math.Abs(r1000 - r16000)));
        Assert.That(r4000, Is.EqualTo(r16000).Within(1e-3));
    }
}
