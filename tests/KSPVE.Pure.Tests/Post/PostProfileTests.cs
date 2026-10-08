using System.Collections.Generic;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;
using KSPVE.Pure.Post;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Post;

public class PostProfileTests
{
    [Test]
    public void Defaults_AreSubtleAndPreserveKspLook()
    {
        var issues = new List<ConfigIssue>();
        PostProfile p = PostProfile.From(PostSettings.Schema.Resolve(null, issues));
        Assert.That(issues, Is.Empty);
        Assert.That(PostSettings.Schema.NodeName, Is.EqualTo("KSPVE_POST"));
        Assert.That(p.Enabled, Is.True);
        Assert.That(p.Tonemapper, Is.EqualTo(TonemapperMode.Rolloff));
        Assert.That(p.Exposure, Is.EqualTo(0.0));
        Assert.That(p.AutoExposure, Is.False);
        Assert.That(p.Contrast, Is.EqualTo(1.0));
        Assert.That(p.Saturation, Is.EqualTo(1.0));
        Assert.That(p.ChromaticAberration, Is.EqualTo(0.0));
        // Untested-in-game effects ship off.
        Assert.That(p.MotionBlur, Is.False);
        Assert.That(p.DepthOfField, Is.False);
        Assert.That(p.AmbientOcclusion, Is.False);
        Assert.That(p.ContactShadows, Is.False);
        Assert.That(p.NightVision, Is.EqualTo(0.0));
        Assert.That(p.Broadcast, Is.EqualTo(0.0));
    }

    [Test]
    public void Presets_ChangeTheLook_AndDefaultRestoresIt_WithoutTouchingHeavyEffects()
    {
        PostProfile p = PostProfile.Defaults();
        p.AmbientOcclusion = true;
        p.ApplyPreset(LookPreset.Cinematic);
        Assert.That(p.Tonemapper, Is.EqualTo(TonemapperMode.Aces));
        Assert.That(p.Vignette, Is.GreaterThan(PostProfile.Defaults().Vignette));
        Assert.That(p.AmbientOcclusion, Is.True);

        p.ApplyPreset(LookPreset.Default);
        PostProfile d = PostProfile.Defaults();
        Assert.That(p.Tonemapper, Is.EqualTo(d.Tonemapper));
        Assert.That(p.Vignette, Is.EqualTo(d.Vignette));
        Assert.That(p.Grain, Is.EqualTo(d.Grain));
        Assert.That(p.AmbientOcclusion, Is.True);
    }

    [Test]
    public void ToCfg_RoundTripsThroughTheSchema()
    {
        var original = PostProfile.From(PostSettings.Schema.Resolve(null, new List<ConfigIssue>()));
        original.Enabled = false;
        original.Tonemapper = TonemapperMode.AgX;
        original.Exposure = -1.25;
        original.AutoExposure = true;
        original.BloomIntensity = 0.6;
        original.Temperature = 0.3;
        original.Vibrance = -0.2;
        original.Sharpen = 0.75;
        original.MotionBlur = true;
        original.AoRadius = 2.5;
        original.Spikes = 0.4;

        var issues = new List<ConfigIssue>();
        PostProfile copy = PostProfile.From(PostSettings.Schema.Resolve(original.ToCfg(), issues));

        Assert.That(issues, Is.Empty);
        Assert.That(copy.Enabled, Is.False);
        Assert.That(copy.Tonemapper, Is.EqualTo(TonemapperMode.AgX));
        Assert.That(copy.Exposure, Is.EqualTo(-1.25));
        Assert.That(copy.AutoExposure, Is.True);
        Assert.That(copy.BloomIntensity, Is.EqualTo(0.6));
        Assert.That(copy.Temperature, Is.EqualTo(0.3));
        Assert.That(copy.Vibrance, Is.EqualTo(-0.2));
        Assert.That(copy.Sharpen, Is.EqualTo(0.75));
        Assert.That(copy.MotionBlur, Is.True);
        Assert.That(copy.AoRadius, Is.EqualTo(2.5));
        Assert.That(copy.Spikes, Is.EqualTo(0.4));
    }

    [Test]
    public void ExposureMultiplier_CombinesEvAndAcesPreScale()
    {
        var p = PostProfile.From(PostSettings.Schema.Resolve(null, new List<ConfigIssue>()));
        p.Exposure = 1.0;
        p.Tonemapper = TonemapperMode.Rolloff;
        Assert.That(p.ExposureMultiplier, Is.EqualTo(2.0).Within(1e-12));
        p.Tonemapper = TonemapperMode.Aces;
        Assert.That(p.ExposureMultiplier, Is.EqualTo(2.0 / 0.6).Within(1e-12));
    }

    [Test]
    public void WhiteBalanceMatrix_IsIdentityAtNeutral_AndWarmsForPositiveTemperature()
    {
        var p = PostProfile.From(PostSettings.Schema.Resolve(null, new List<ConfigIssue>()));
        double[,] m = p.WhiteBalanceMatrix();
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Assert.That(m[r, c], Is.EqualTo(r == c ? 1.0 : 0.0).Within(1e-6), $"[{r},{c}]");

        p.Temperature = 1.0;
        double[,] warm = p.WhiteBalanceMatrix();
        Rgb white = new Rgb(
            warm[0, 0] + warm[0, 1] + warm[0, 2],
            warm[1, 0] + warm[1, 1] + warm[1, 2],
            warm[2, 0] + warm[2, 1] + warm[2, 2]);
        Assert.That(white.R, Is.GreaterThan(white.B), "positive temperature should warm neutral white");
    }
}
