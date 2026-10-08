using KSPVE.Pure.ColorScience;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.ColorScience;

[TestFixture]
public sealed class TonemapperTests
{
    private static System.Collections.Generic.IEnumerable<TestCaseData> Mappers()
    {
        yield return new TestCaseData((System.Func<Rgb, Rgb>)AcesFitted.Apply).SetName("AcesFitted");
        yield return new TestCaseData((System.Func<Rgb, Rgb>)KhronosPbrNeutral.Apply).SetName("KhronosPbrNeutral");
        yield return new TestCaseData((System.Func<Rgb, Rgb>)AgX.Apply).SetName("AgX");
    }

    [TestCaseSource(nameof(Mappers))]
    public void GreyRampMonotonicAndBounded(System.Func<Rgb, Rgb> map)
    {
        double prev = -1;
        for (double v = 0.001; v <= 1000; v *= 1.1)
        {
            Rgb o = map(new Rgb(v, v, v));
            foreach (double c in new[] { o.R, o.G, o.B })
                Assert.That(c, Is.InRange(0.0, 1.0));
            Assert.That(o.Luminance, Is.GreaterThanOrEqualTo(prev - 1e-9), "v=" + v);
            prev = o.Luminance;
        }
    }

    [TestCaseSource(nameof(Mappers))]
    public void BlackIsBlack(System.Func<Rgb, Rgb> map)
    {
        Rgb o = map(new Rgb(0, 0, 0));
        Assert.That(o.MaxComponent, Is.LessThan(0.01));
    }

    [TestCaseSource(nameof(Mappers))]
    public void MidGreyPlausible(System.Func<Rgb, Rgb> map)
    {
        double y = map(new Rgb(0.18, 0.18, 0.18)).G;
        Assert.That(y, Is.InRange(0.08, 0.5));
    }

    [Test]
    public void AcesBrightSaturatesToWhite()
    {
        Rgb o = AcesFitted.Apply(new Rgb(1000, 1000, 1000));
        Assert.That(o.R, Is.GreaterThan(0.97));
        Assert.That(o.G, Is.GreaterThan(0.97));
        Assert.That(o.B, Is.GreaterThan(0.97));
    }

    [Test]
    public void AcesFitMatchesHillFormula()
    {
        double v = 1.0;
        double expected = (v * (v + 0.0245786) - 0.000090537) / (v * (0.983729 * v + 0.4329510) + 0.238081);
        Assert.That(AcesFitted.RrtAndOdtFit(1.0), Is.EqualTo(expected).Within(1e-12));
    }

    [Test]
    public void KhronosSpotValues()
    {
        // Khronos reference GLSL: below the compression start the curve is identity minus offset.
        // Grey 0.5: x=0.5 >= 0.08 -> offset 0.04, peak 0.46 < 0.76 -> 0.46.
        Rgb g = KhronosPbrNeutral.Apply(new Rgb(0.5, 0.5, 0.5));
        Assert.That(g.R, Is.EqualTo(0.46).Within(1e-12));
        // x = 0.04 -> offset = 0.04 - 6.25*0.0016 = 0.03 -> 0.01
        Assert.That(KhronosPbrNeutral.Apply(new Rgb(0.04, 0.04, 0.04)).G, Is.EqualTo(0.01).Within(1e-12));
        // Grey 1.0: offset 0.04, peak 0.96, d 0.24, newPeak = 1 - d^2/(peak + d - 0.76)
        double peak = 0.96, d = 0.24, np = 1 - d * d / (peak + d - 0.76);
        Assert.That(KhronosPbrNeutral.Apply(new Rgb(1, 1, 1)).G, Is.EqualTo(np).Within(1e-12));
        Assert.That(KhronosPbrNeutral.Apply(new Rgb(1e6, 1e6, 1e6)).G, Is.GreaterThan(0.99));
    }

    [Test]
    public void KhronosMatchesReferenceGlslHandComputed()
    {
        // Values evaluated from KhronosGroup/ToneMapping pbrNeutral.glsl (independent Python transcription).
        Rgb a = KhronosPbrNeutral.Apply(new Rgb(1.0, 0.5, 0.25));
        Assert.That(a.R, Is.EqualTo(0.8690909090909091).Within(1e-12));
        Assert.That(a.G, Is.EqualTo(0.4225288762060063).Within(1e-12));
        Assert.That(a.B, Is.EqualTo(0.19924785976355486).Within(1e-12));
        Rgb b = KhronosPbrNeutral.Apply(new Rgb(0.05, 0.9, 3.0));
        Assert.That(b.R, Is.EqualTo(0.2283572903276121).Within(1e-12));
        Assert.That(b.G, Is.EqualTo(0.4439087753389689).Within(1e-12));
        Assert.That(b.B, Is.EqualTo(0.9764477383082034).Within(1e-12));
    }

    [Test]
    public void KhronosHasNoFinalClamp()
    {
        // Reference has no saturate: out-of-domain negative input passes through the offset arithmetic unclamped.
        // x = -0.1 -> offset = -0.1 - 6.25*0.01 = -0.1625 -> 0.0625 (grey), 0.6625 (others).
        Assert.That(KhronosPbrNeutral.Apply(new Rgb(-0.1, -0.1, -0.1)).G, Is.EqualTo(0.0625).Within(1e-12));
        Rgb c = KhronosPbrNeutral.Apply(new Rgb(-0.1, 0.5, 0.5));
        Assert.That(c.R, Is.EqualTo(0.0625).Within(1e-12));
        Assert.That(c.G, Is.EqualTo(0.6625).Within(1e-12));
    }

    [Test]
    public void AcesMatchesBakingLabMatrixOrientation()
    {
        // mul(ACESInputMat, float3(1,1,1)) row sums: 1.00000, 0.99999, 1.00000 (rows as written in ACES.hlsl).
        // Full pipeline for white 1: RRTAndODTFit(row sums) then output matrix, saturated.
        double[] rows = { 0.59719 + 0.35458 + 0.04823, 0.07600 + 0.90834 + 0.01566, 0.02840 + 0.13383 + 0.83777 };
        double[] f = new double[3];
        for (int i = 0; i < 3; i++) f[i] = AcesFitted.RrtAndOdtFit(rows[i]);
        double expR = 1.60475 * f[0] - 0.53108 * f[1] - 0.07367 * f[2];
        Assert.That(AcesFitted.Apply(new Rgb(1, 1, 1)).R, Is.EqualTo(System.Math.Min(1.0, System.Math.Max(0.0, expR))).Within(1e-12));
    }

    [Test]
    public void AgXGreyStaysNeutral()
    {
        Rgb o = AgX.Apply(new Rgb(0.18, 0.18, 0.18));
        Assert.That(System.Math.Abs(o.R - o.B), Is.LessThan(0.02));
    }
}
