using KSPVE.Pure.Config;

namespace KSPVE.Pure.Post;

public enum TonemapperMode
{
    None,
    Rolloff,
    Neutral,
    AgX,
    Aces
}

/// <summary>The KSPVE_POST settings node: camera post-processing (phase P1).</summary>
public static class PostSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE_POST");

    public static readonly BoolSetting Enabled = Schema.Add(new BoolSetting("enabled", true, "Master switch for KSPVE post-processing."));
    public static readonly EnumSetting<TonemapperMode> Tonemapper = Schema.Add(new EnumSetting<TonemapperMode>("tonemapper", TonemapperMode.Rolloff, "Rolloff keeps KSP's look and only compresses HDR highlights; Neutral, AgX and ACES are alternative looks."));
    public static readonly DoubleSetting Exposure = Schema.Add(new DoubleSetting("exposure", 0.0, -4.0, 4.0, "Exposure compensation in EV."));

    public static readonly BoolSetting AutoExposure = Schema.Add(new BoolSetting("autoExposure", false, "Eye adaptation from centre-weighted average luminance (experimental)."));
    public static readonly DoubleSetting AutoExposureKey = Schema.Add(new DoubleSetting("autoExposureKey", 0.18, 0.05, 0.5, "Target average scene luminance."));
    public static readonly DoubleSetting AutoExposureMinEv = Schema.Add(new DoubleSetting("autoExposureMinEv", -2.0, -6.0, 0.0, "Lowest automatic exposure, EV."));
    public static readonly DoubleSetting AutoExposureMaxEv = Schema.Add(new DoubleSetting("autoExposureMaxEv", 1.5, 0.0, 6.0, "Highest automatic exposure, EV."));
    public static readonly DoubleSetting AutoExposureDarkSpeed = Schema.Add(new DoubleSetting("autoExposureDarkSpeed", 1.0, 0.1, 10.0, "Adaptation speed when the scene gets darker."));
    public static readonly DoubleSetting AutoExposureBrightSpeed = Schema.Add(new DoubleSetting("autoExposureBrightSpeed", 3.0, 0.1, 10.0, "Adaptation speed when the scene gets brighter."));

    public static readonly BoolSetting Bloom = Schema.Add(new BoolSetting("bloom", true, "Energy-conserving bloom from bright highlights."));
    public static readonly DoubleSetting BloomIntensity = Schema.Add(new DoubleSetting("bloomIntensity", 0.25, 0.0, 2.0, "Bloom strength."));
    public static readonly DoubleSetting BloomThreshold = Schema.Add(new DoubleSetting("bloomThreshold", 0.8, 0.0, 4.0, "Linear brightness where bloom starts."));
    public static readonly DoubleSetting BloomKnee = Schema.Add(new DoubleSetting("bloomKnee", 0.5, 0.0, 1.0, "Softness of the bloom threshold."));
    public static readonly DoubleSetting BloomScatter = Schema.Add(new DoubleSetting("bloomScatter", 0.7, 0.0, 1.0, "How far bloom spreads."));

    public static readonly DoubleSetting Temperature = Schema.Add(new DoubleSetting("temperature", 0.0, -1.0, 1.0, "White balance: negative cooler, positive warmer."));
    public static readonly DoubleSetting Tint = Schema.Add(new DoubleSetting("tint", 0.0, -1.0, 1.0, "White balance: negative green, positive magenta."));
    public static readonly DoubleSetting Contrast = Schema.Add(new DoubleSetting("contrast", 1.0, 0.5, 1.5, "Contrast around mid-grey."));
    public static readonly DoubleSetting Saturation = Schema.Add(new DoubleSetting("saturation", 1.0, 0.0, 2.0, "Colour saturation."));
    public static readonly DoubleSetting Vibrance = Schema.Add(new DoubleSetting("vibrance", 0.0, -1.0, 1.0, "Saturation that favours muted colours."));

    public static readonly DoubleSetting Vignette = Schema.Add(new DoubleSetting("vignette", 0.2, 0.0, 1.0, "Natural lens darkening towards the corners."));
    public static readonly DoubleSetting Grain = Schema.Add(new DoubleSetting("grain", 0.05, 0.0, 1.0, "Film grain."));
    public static readonly DoubleSetting ChromaticAberration = Schema.Add(new DoubleSetting("chromaticAberration", 0.0, 0.0, 1.0, "Colour fringing towards the edges."));
    public static readonly DoubleSetting Sharpen = Schema.Add(new DoubleSetting("sharpen", 0.3, 0.0, 1.0, "Contrast-adaptive sharpening (AMD CAS)."));

    public static readonly DoubleSetting LensDirt = Schema.Add(new DoubleSetting("lensDirt", 0.0, 0.0, 2.0, "Dirt on the lens lit up by bright light."));
    public static readonly DoubleSetting Spikes = Schema.Add(new DoubleSetting("spikes", 0.0, 0.0, 2.0, "Diffraction spikes on bright lights."));
    public static readonly DoubleSetting SensorSmear = Schema.Add(new DoubleSetting("sensorSmear", 0.0, 0.0, 1.0, "CCD-style vertical smear from very bright sources."));
    public static readonly DoubleSetting NightVision = Schema.Add(new DoubleSetting("nightVision", 0.0, 0.0, 1.0, "Human night vision: dark areas lose colour and turn blue (Purkinje shift)."));
    public static readonly DoubleSetting Broadcast = Schema.Add(new DoubleSetting("broadcast", 0.0, 0.0, 1.0, "Mission-control TV look."));

    public static readonly BoolSetting MotionBlur = Schema.Add(new BoolSetting("motionBlur", false, "Motion blur from per-pixel motion vectors."));
    public static readonly DoubleSetting MotionBlurStrength = Schema.Add(new DoubleSetting("motionBlurStrength", 0.5, 0.0, 1.0, "Motion blur shutter."));
    public static readonly BoolSetting DepthOfField = Schema.Add(new BoolSetting("depthOfField", false, "Lens depth of field."));
    public static readonly DoubleSetting DofFocusDistance = Schema.Add(new DoubleSetting("dofFocusDistance", 0.0, 0.0, 100000.0, "Focus distance in metres; 0 auto-focuses on the screen centre."));
    public static readonly DoubleSetting DofStrength = Schema.Add(new DoubleSetting("dofStrength", 0.5, 0.0, 1.0, "Depth-of-field blur amount."));
    public static readonly BoolSetting AmbientOcclusion = Schema.Add(new BoolSetting("ambientOcclusion", false, "Screen-space ambient occlusion on opaque geometry."));
    public static readonly DoubleSetting AoIntensity = Schema.Add(new DoubleSetting("aoIntensity", 0.8, 0.0, 1.0, "Ambient occlusion strength."));
    public static readonly DoubleSetting AoRadius = Schema.Add(new DoubleSetting("aoRadius", 1.5, 0.1, 10.0, "Ambient occlusion radius in metres."));
    public static readonly BoolSetting ContactShadows = Schema.Add(new BoolSetting("contactShadows", false, "Screen-space contact shadows from the sun."));
    public static readonly DoubleSetting ContactShadowLength = Schema.Add(new DoubleSetting("contactShadowLength", 1.0, 0.1, 5.0, "Contact shadow ray length in metres."));

    public static readonly BoolSetting BlackHoles = Schema.Add(new BoolSetting("blackHoles", true, "Relativistic black holes with volumetric accretion disks (KSPVE_BLACKHOLE nodes)."));
    public static readonly DoubleSetting BlackHoleBrightness = Schema.Add(new DoubleSetting("blackHoleBrightness", 1.0, 0.0, 5.0, "Brightness multiplier for all accretion disks."));
    public static readonly DoubleSetting BlackHoleResolution = Schema.Add(new DoubleSetting("blackHoleResolution", 0.5, 0.25, 1.0, "Render scale of black holes: 0.5 draws them at half resolution (about 4x faster), 1 at full resolution."));
    public static readonly BoolSetting OrbitPaths = Schema.Add(new BoolSetting("orbitPaths", true, "In the map: the next lap of every body's real path, under all the bodies' gravity."));
}
