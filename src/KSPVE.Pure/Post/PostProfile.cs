using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Post;

public enum LookPreset
{
    Default,
    Cinematic,
    Documentary,
    Vivid,
    Photoreal,
    Broadcast
}

/// <summary>Runtime-only G-buffer visualisations (feature #11).</summary>
public enum DebugView
{
    Off,
    Depth,
    Normals,
    Albedo,
    Specular,
    Smoothness,
    Occlusion
}

/// <summary>
/// Live, editable post-processing parameters. Built from the KSPVE_POST settings, edited by the
/// in-game window, read by the renderer every frame, and written back with <see cref="ToCfg"/>.
/// </summary>
public sealed class PostProfile
{
    private const double NeutralKelvin = 6504.0;

    public bool Enabled;
    public TonemapperMode Tonemapper;
    public double Exposure;

    public bool AutoExposure;
    public double AutoExposureKey;
    public double AutoExposureMinEv;
    public double AutoExposureMaxEv;
    public double AutoExposureDarkSpeed;
    public double AutoExposureBrightSpeed;

    public bool Bloom;
    public double BloomIntensity;
    public double BloomThreshold;
    public double BloomKnee;
    public double BloomScatter;

    public double Temperature;
    public double Tint;
    public double Contrast;
    public double Saturation;
    public double Vibrance;

    public double Vignette;
    public double Grain;
    public double ChromaticAberration;
    public double Sharpen;
    public double LensDirt;
    public double Spikes;
    public double SensorSmear;
    public double NightVision;
    public double Broadcast;

    public bool MotionBlur;
    public double MotionBlurStrength;
    public bool DepthOfField;
    public double DofFocusDistance;
    public double DofStrength;
    public bool AmbientOcclusion;
    public double AoIntensity;
    public double AoRadius;
    public bool ContactShadows;
    public double ContactShadowLength;
    public bool BlackHoles;
    public double BlackHoleBrightness;
    public double BlackHoleResolution;
    public bool OrbitPaths;

    /// <summary>Runtime only (not saved): show the unprocessed image on the left half.</summary>
    public bool CompareSplit;

    /// <summary>Runtime only (not saved).</summary>
    public DebugView DebugView;

    /// <summary>Linear multiplier: 2^Exposure, with the conventional 1/0.6 pre-scale for the ACES fit.</summary>
    public double ExposureMultiplier => Math.Pow(2.0, Exposure) * (Tonemapper == TonemapperMode.Aces ? 1.0 / 0.6 : 1.0);

    public static PostProfile Defaults() => From(PostSettings.Schema.Resolve(null, new List<ConfigIssue>()));

    public static PostProfile From(SettingsValues v) => new PostProfile
    {
        Enabled = v.Get(PostSettings.Enabled),
        Tonemapper = v.Get(PostSettings.Tonemapper),
        Exposure = v.Get(PostSettings.Exposure),
        AutoExposure = v.Get(PostSettings.AutoExposure),
        AutoExposureKey = v.Get(PostSettings.AutoExposureKey),
        AutoExposureMinEv = v.Get(PostSettings.AutoExposureMinEv),
        AutoExposureMaxEv = v.Get(PostSettings.AutoExposureMaxEv),
        AutoExposureDarkSpeed = v.Get(PostSettings.AutoExposureDarkSpeed),
        AutoExposureBrightSpeed = v.Get(PostSettings.AutoExposureBrightSpeed),
        Bloom = v.Get(PostSettings.Bloom),
        BloomIntensity = v.Get(PostSettings.BloomIntensity),
        BloomThreshold = v.Get(PostSettings.BloomThreshold),
        BloomKnee = v.Get(PostSettings.BloomKnee),
        BloomScatter = v.Get(PostSettings.BloomScatter),
        Temperature = v.Get(PostSettings.Temperature),
        Tint = v.Get(PostSettings.Tint),
        Contrast = v.Get(PostSettings.Contrast),
        Saturation = v.Get(PostSettings.Saturation),
        Vibrance = v.Get(PostSettings.Vibrance),
        Vignette = v.Get(PostSettings.Vignette),
        Grain = v.Get(PostSettings.Grain),
        ChromaticAberration = v.Get(PostSettings.ChromaticAberration),
        Sharpen = v.Get(PostSettings.Sharpen),
        LensDirt = v.Get(PostSettings.LensDirt),
        Spikes = v.Get(PostSettings.Spikes),
        SensorSmear = v.Get(PostSettings.SensorSmear),
        NightVision = v.Get(PostSettings.NightVision),
        Broadcast = v.Get(PostSettings.Broadcast),
        MotionBlur = v.Get(PostSettings.MotionBlur),
        MotionBlurStrength = v.Get(PostSettings.MotionBlurStrength),
        DepthOfField = v.Get(PostSettings.DepthOfField),
        DofFocusDistance = v.Get(PostSettings.DofFocusDistance),
        DofStrength = v.Get(PostSettings.DofStrength),
        AmbientOcclusion = v.Get(PostSettings.AmbientOcclusion),
        AoIntensity = v.Get(PostSettings.AoIntensity),
        AoRadius = v.Get(PostSettings.AoRadius),
        ContactShadows = v.Get(PostSettings.ContactShadows),
        ContactShadowLength = v.Get(PostSettings.ContactShadowLength),
        BlackHoles = v.Get(PostSettings.BlackHoles),
        BlackHoleBrightness = v.Get(PostSettings.BlackHoleBrightness),
        BlackHoleResolution = v.Get(PostSettings.BlackHoleResolution),
        OrbitPaths = v.Get(PostSettings.OrbitPaths)
    };

    public CfgNode ToCfg()
    {
        var n = new CfgNode(PostSettings.Schema.NodeName);
        n.Add(PostSettings.Enabled.Key, Bool(Enabled));
        n.Add(PostSettings.Tonemapper.Key, Tonemapper.ToString());
        n.Add(PostSettings.Exposure.Key, Num(Exposure));
        n.Add(PostSettings.AutoExposure.Key, Bool(AutoExposure));
        n.Add(PostSettings.AutoExposureKey.Key, Num(AutoExposureKey));
        n.Add(PostSettings.AutoExposureMinEv.Key, Num(AutoExposureMinEv));
        n.Add(PostSettings.AutoExposureMaxEv.Key, Num(AutoExposureMaxEv));
        n.Add(PostSettings.AutoExposureDarkSpeed.Key, Num(AutoExposureDarkSpeed));
        n.Add(PostSettings.AutoExposureBrightSpeed.Key, Num(AutoExposureBrightSpeed));
        n.Add(PostSettings.Bloom.Key, Bool(Bloom));
        n.Add(PostSettings.BloomIntensity.Key, Num(BloomIntensity));
        n.Add(PostSettings.BloomThreshold.Key, Num(BloomThreshold));
        n.Add(PostSettings.BloomKnee.Key, Num(BloomKnee));
        n.Add(PostSettings.BloomScatter.Key, Num(BloomScatter));
        n.Add(PostSettings.Temperature.Key, Num(Temperature));
        n.Add(PostSettings.Tint.Key, Num(Tint));
        n.Add(PostSettings.Contrast.Key, Num(Contrast));
        n.Add(PostSettings.Saturation.Key, Num(Saturation));
        n.Add(PostSettings.Vibrance.Key, Num(Vibrance));
        n.Add(PostSettings.Vignette.Key, Num(Vignette));
        n.Add(PostSettings.Grain.Key, Num(Grain));
        n.Add(PostSettings.ChromaticAberration.Key, Num(ChromaticAberration));
        n.Add(PostSettings.Sharpen.Key, Num(Sharpen));
        n.Add(PostSettings.LensDirt.Key, Num(LensDirt));
        n.Add(PostSettings.Spikes.Key, Num(Spikes));
        n.Add(PostSettings.SensorSmear.Key, Num(SensorSmear));
        n.Add(PostSettings.NightVision.Key, Num(NightVision));
        n.Add(PostSettings.Broadcast.Key, Num(Broadcast));
        n.Add(PostSettings.MotionBlur.Key, Bool(MotionBlur));
        n.Add(PostSettings.MotionBlurStrength.Key, Num(MotionBlurStrength));
        n.Add(PostSettings.DepthOfField.Key, Bool(DepthOfField));
        n.Add(PostSettings.DofFocusDistance.Key, Num(DofFocusDistance));
        n.Add(PostSettings.DofStrength.Key, Num(DofStrength));
        n.Add(PostSettings.AmbientOcclusion.Key, Bool(AmbientOcclusion));
        n.Add(PostSettings.AoIntensity.Key, Num(AoIntensity));
        n.Add(PostSettings.AoRadius.Key, Num(AoRadius));
        n.Add(PostSettings.ContactShadows.Key, Bool(ContactShadows));
        n.Add(PostSettings.ContactShadowLength.Key, Num(ContactShadowLength));
        n.Add(PostSettings.BlackHoles.Key, Bool(BlackHoles));
        n.Add(PostSettings.BlackHoleBrightness.Key, Num(BlackHoleBrightness));
        n.Add(PostSettings.BlackHoleResolution.Key, Num(BlackHoleResolution));
        n.Add(PostSettings.OrbitPaths.Key, Bool(OrbitPaths));
        return n;
    }

    /// <summary>
    /// Sets the look (tonemapper, grading and lens character). Leaves the master switch, exposure
    /// automation and the heavier effects (AO, contact shadows, motion blur, depth of field) alone.
    /// </summary>
    public void ApplyPreset(LookPreset preset)
    {
        PostProfile d = Defaults();
        Tonemapper = d.Tonemapper;
        Exposure = d.Exposure;
        BloomIntensity = d.BloomIntensity;
        Temperature = d.Temperature;
        Tint = d.Tint;
        Contrast = d.Contrast;
        Saturation = d.Saturation;
        Vibrance = d.Vibrance;
        Vignette = d.Vignette;
        Grain = d.Grain;
        ChromaticAberration = d.ChromaticAberration;
        Sharpen = d.Sharpen;
        LensDirt = d.LensDirt;
        Spikes = d.Spikes;
        SensorSmear = d.SensorSmear;
        Broadcast = d.Broadcast;

        switch (preset)
        {
            case LookPreset.Cinematic:
                Tonemapper = TonemapperMode.Aces;
                Contrast = 1.1; Vibrance = 0.1; Temperature = 0.05;
                Vignette = 0.35; Grain = 0.08; BloomIntensity = 0.35; LensDirt = 0.3;
                ChromaticAberration = 0.15; Sharpen = 0.2;
                break;
            case LookPreset.Documentary:
                Contrast = 0.95; Saturation = 0.85; Temperature = 0.15;
                Vignette = 0.4; Grain = 0.35; BloomIntensity = 0.3;
                ChromaticAberration = 0.1; Sharpen = 0.1;
                break;
            case LookPreset.Vivid:
                Tonemapper = TonemapperMode.Neutral;
                Contrast = 1.1; Saturation = 1.2; Vibrance = 0.3;
                Vignette = 0.15; BloomIntensity = 0.3; Sharpen = 0.4;
                break;
            case LookPreset.Photoreal:
                Tonemapper = TonemapperMode.AgX;
                Exposure = 0.4; Contrast = 1.05; Saturation = 1.05; Vibrance = 0.1;
                Vignette = 0.2; Grain = 0.04; BloomIntensity = 0.25; Spikes = 0.2; Sharpen = 0.3;
                break;
            case LookPreset.Broadcast:
                Saturation = 0.9; Grain = 0.2; Broadcast = 0.8; Sharpen = 0.1;
                break;
        }
    }

    /// <summary>
    /// Row-major 3x3 linear-Rec.709 white-balance matrix. Positive temperature treats the scene as lit
    /// by a cooler illuminant (up to ~8,300 K) and adapts it to D65-ish 6504 K, warming the image;
    /// tint scales green.
    /// </summary>
    public double[,] WhiteBalanceMatrix()
    {
        double sourceKelvin = NeutralKelvin * Math.Pow(2.0, Temperature * 0.35);
        var m = new double[3, 3];
        Rgb[] basis = { new Rgb(1, 0, 0), new Rgb(0, 1, 0), new Rgb(0, 0, 1) };
        for (int c = 0; c < 3; c++)
        {
            Rgb column = ChromaticAdaptation.WhiteBalance(basis[c], sourceKelvin, NeutralKelvin);
            m[0, c] = column.R;
            m[1, c] = column.G;
            m[2, c] = column.B;
        }
        double greenGain = 1.0 - 0.1 * Tint;
        for (int c = 0; c < 3; c++) m[1, c] *= greenGain;
        return m;
    }

    private static string Bool(bool b) => b ? "True" : "False";
    private static string Num(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
}
