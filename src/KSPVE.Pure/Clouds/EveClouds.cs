using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Clouds;

// KSPVE's own model of the cloud and weather configs written for EVE Redux's raymarched volumetrics (EVE_CLOUDS,
// EVE_RAYMARCHED_CLOUDS_QUALITY, EVE_PARTICLE_FIELD_CONFIG, EVE_LIGHTNING_CONFIG, EVE_DROPLETS_CONFIG,
// EVE_WET_SURFACES_CONFIG, EVE_SHADOWS, PQS_MANAGER), so packs written for it work with KSPVE unchanged. Read by
// EveCloudsReader; the keys' meanings follow EVE's public wiki. Colours are kept as written: 0-255 a channel, more for
// brighter than white.

/// <summary>Two numbers, as a config's "x, y".</summary>
public readonly struct Double2 : IEquatable<Double2>
{
    public readonly double X, Y;
    public Double2(double x, double y) { X = x; Y = y; }
    public bool Equals(Double2 other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object obj) => obj is Double2 other && Equals(other);
    public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A colour as written, "r, g, b[, a]" (0-255 a channel; more is brighter than white).</summary>
public readonly struct CfgColor : IEquatable<CfgColor>
{
    public readonly double R, G, B, A;
    public CfgColor(double r, double g, double b, double a = 255.0) { R = r; G = g; B = b; A = a; }
    public static CfgColor White => new CfgColor(255, 255, 255, 255);
    /// <summary>As 0-1 linear fractions (more than 1 for HDR values).</summary>
    public Double3 Rgb01 => new Double3(R / 255.0, G / 255.0, B / 255.0);
    public bool Equals(CfgColor o) => R.Equals(o.R) && G.Equals(o.G) && B.Equals(o.B) && A.Equals(o.A);
    public override bool Equals(object obj) => obj is CfgColor o && Equals(o);
    public override int GetHashCode() => (R.GetHashCode() * 397 ^ G.GetHashCode()) * 397 ^ B.GetHashCode() ^ A.GetHashCode();
    public override string ToString() => $"({R}, {G}, {B}, {A})";
}

/// <summary>Which channel of a texture holds a grey map (EVE's alphaMask).</summary>
public enum AlphaMaskChannel { None, R, G, B, A }

/// <summary>A texture a config points at: a GameData path without extension (PluginData ones loaded on demand).</summary>
public sealed class TextureRef
{
    public string Path = string.Empty;
    /// <summary>EVE's type: RGBA, AlphaMap, or a cube map layout.</summary>
    public string Type = string.Empty;
    public AlphaMaskChannel AlphaMask = AlphaMaskChannel.None;
    public bool IsClamped;
    public string Format = string.Empty;
    public bool IsSet => !string.IsNullOrEmpty(Path);
    /// <summary>The channel a grey map is read from: its alphaMask, else <paramref name="fallback"/> (coverage maps use alpha,
    /// cloud type maps red).</summary>
    public AlphaMaskChannel ChannelOr(AlphaMaskChannel fallback) => AlphaMask != AlphaMaskChannel.None ? AlphaMask : fallback;
}

public enum CloudSpeedMode { Linear, LinearSurface, Angular }
public enum TimeUnit { Seconds, Hours }
public enum TimeFadeMode { Coverage, Density }

/// <summary>
/// A layer that comes and goes (timeSettings): on for <see cref="Duration"/> out of every <see cref="RepeatInterval"/>,
/// starting <see cref="Offset"/> into the cycle, fading in and out over <see cref="FadeTime"/>; in <see cref="Unit"/>s of
/// game time. Fading shrinks the clouds (Coverage) or thins them (Density).
/// </summary>
public sealed class TimeCycle
{
    public TimeUnit Unit = TimeUnit.Seconds;
    public double Duration, Offset, RepeatInterval, FadeTime;
    public TimeFadeMode FadeMode = TimeFadeMode.Coverage;

    /// <summary>Seconds per unit.</summary>
    public double UnitSeconds => Unit == TimeUnit.Hours ? 3600.0 : 1.0;

    /// <summary>How much of the layer is there at game time <paramref name="universalTime"/> (s): 0 off, 1 fully on.</summary>
    public double FactorAt(double universalTime)
    {
        if (RepeatInterval <= 0.0 || Duration >= RepeatInterval) return 1.0;
        if (Duration <= 0.0) return 0.0;
        double t = universalTime / UnitSeconds - Offset;
        double phase = t - Math.Floor(t / RepeatInterval) * RepeatInterval;
        if (phase >= Duration) return 0.0;
        if (FadeTime <= 0.0) return 1.0;
        return Math.Min(1.0, Math.Min(phase, Duration - phase) / FadeTime);
    }
}

/// <summary>The cloud material (settings): the 2D layer's look, and the detail texture a volumetric layer can borrow.</summary>
public sealed class CloudsMaterial
{
    public CfgColor Color = CfgColor.White;
    public TextureRef MainTex = new TextureRef(), BumpMap = new TextureRef(), DetailTex = new TextureRef(), UVNoiseTex = new TextureRef();
    public FlowMapSettings FlowMap;
    public double Lambertian = 1.0, BumpScale = 1.0, DetailScale = 1.0, DetailDist, DistFade, DistFadeVert, UVNoiseScale, UVNoiseStrength;
    public Double2 UVNoiseAnimation;
}

/// <summary>Clouds moving locally (flowMap): directions in a texture's RGB (0.5 = still), looping every 1/speed s.</summary>
public sealed class FlowMapSettings
{
    public TextureRef Texture = new TextureRef();
    public double Speed, Displacement;
}

/// <summary>The flat cloud layer seen from far off (layer2D).</summary>
public sealed class Layer2DSettings
{
    public double FalloffPow, FalloffScale, MinLight, RimDist, RimDistSub, InvFade;
    public bool HasShadow;
    public double ShadowFactor = 1.0;
}

/// <summary>The older particle clouds (layerVolume).</summary>
public sealed class LayerVolumeSettings
{
    public Double2 Size, Area;
    public Double3 MaxTranslation, NoiseScale;
    public double VisibleRange, RotationSpeed;
    public bool FollowDetail;
    public TextureRef ParticleTexture = new TextureRef();
    public double InvFade, MinScatter, Opacity;
}

/// <summary>Octaves of Perlin or Worley noise shaping the clouds.</summary>
public sealed class NoiseOctaves
{
    public double Octaves = 4, Periods = 1, Persistence = 0.5, Lacunarity = 2, Spherical;
}

/// <summary>The 3D noise the clouds are carved from (noise): Worley, Perlin, or both mixed.</summary>
public sealed class CloudNoise
{
    public double ErosionDepth = 0.7;
    public NoiseOctaves Worley, Perlin;
}

/// <summary>Curl noise swirling the clouds' noise into wisps (curlNoise).</summary>
public sealed class CurlNoiseSettings
{
    public double Octaves = 8, Periods = 1, Contrast = 1, Tiling = 1, Strength = 1;
    public bool Smooth;
}

/// <summary>How the clouds are stepped through (raymarchingSettings), in metres.</summary>
public sealed class RaymarchSettings
{
    public double LightMarchSteps = 2, LightMarchDistance = 400, BaseStepSize = 45, AdaptiveStepSizeFactor = 0.0022, MaxStepSize = 180;
    /// <summary>A layer only for its effects (particles, sound): nothing drawn.</summary>
    public bool FxOnlyLayer;
    public double OverlapRenderOrder = 1, ContinuousAccumulationDistance = 50000;
}

/// <summary>One lobe of light scattered by cloud droplets: its eccentricity (+ forward, - back) and its brightness.</summary>
public readonly struct PhaseLobe
{
    public readonly double Eccentricity, Brightness;
    public PhaseLobe(double eccentricity, double brightness) { Eccentricity = eccentricity; Brightness = brightness; }
}

/// <summary>Light scattered by the clouds (phaseFunctions): two lobes for single scattering, two for multiple.</summary>
public sealed class CloudPhase
{
    public PhaseLobe SingleScattering1 = new PhaseLobe(0.95, 0.1), SingleScattering2 = new PhaseLobe(0.8, 0.2);
    public PhaseLobe MultipleScattering1 = new PhaseLobe(0.2, 3.0), MultipleScattering2 = new PhaseLobe(-0.4, 0.3);
}

/// <summary>
/// A kind of cloud in a layer (cloudTypes Item): its height band, density and noise, and how its shape (coverageCurve)
/// and density (densityCurve) vary up through the band (0 its base, 1 its top). The cloud type map picks the type at
/// each place, blending neighbouring types between their values.
/// </summary>
public sealed class CloudType
{
    public string Name = "New cloud type";
    public double MinAltitude, MaxAltitude, Density = 0.1, BaseNoiseTiling = 1000, NoiseEdgeHardness, MultipleScatteringBrightness = 1;
    public double CurlNoiseStrength = 1, ParticleFieldDensity = 1, DropletsDensity = 1, WetSurfacesIntensity = 1, LightningFrequency = 1, AmbientVolume = 1;
    public bool InterpolateCloudHeights = true;
    public FloatCurve CoverageCurve = FloatCurve.Constant(1.0), DensityCurve = FloatCurve.Constant(1.0);
}

/// <summary>A ready-made patch of coverage and cloud type the painter stamps (painterTiles Item).</summary>
public sealed class PainterTile
{
    public string Name = string.Empty;
    public TextureRef CoverageMap = new TextureRef(), CloudTypeMap = new TextureRef();
    public double Size;
    public Double2 RemapCoverage = new Double2(0, 1), RemapType = new Double2(0, 1);
}

/// <summary>A mask limiting where painter tiles go (painterTileMasks Item).</summary>
public sealed class PainterTileMask
{
    public string Name = string.Empty;
    public TextureRef Texture = new TextureRef();
}

/// <summary>A raymarched volumetric cloud layer (layerRaymarchedVolumeV5, or the older layerRaymarchedVolume).</summary>
public sealed class VolumetricLayer
{
    /// <summary>Written as the older layerRaymarchedVolume (EVE's release 1-4), which EVE's release 5 no longer reads.</summary>
    public bool OlderFormat;
    public CloudNoise Noise = new CloudNoise();
    public CurlNoiseSettings CurlNoise;
    public TextureRef CoverageMap = new TextureRef(), CloudTypeMap = new TextureRef(), CloudColorMap = new TextureRef();
    public string SdfMap = string.Empty;
    public FlowMapSettings FlowMap;
    public RaymarchSettings Raymarching = new RaymarchSettings();
    public bool UseLightVolume = true;
    public double MaxLightVolumeRadius = double.PositiveInfinity;
    public CloudPhase Phase = new CloudPhase();
    /// <summary>The names of the effect configs it uses (empty: none).</summary>
    public string ParticleField = string.Empty, Droplets = string.Empty, WetSurfaces = string.Empty, Lightning = string.Empty;
    public string AmbientSound = string.Empty, IvaAmbientSound = string.Empty;
    public bool HasParticleField, HasDroplets, HasWetSurfaces, HasLightning, HasAmbientSound;
    public CfgColor Color = CfgColor.White;
    public double SkylightMultiplier = 1, SkylightTintMultiplier;
    public string ReceiveShadowsFromLayer = string.Empty;
    public double ReceivedShadowsDensity = 100;
    /// <summary>The noise scrolls up through the clouds at this speed (m/s; negative: down, as rain).</summary>
    public double UpwardsCloudSpeed = 11;
    /// <summary>Between these altitudes (m) the volume fades into the flat layer seen from orbit.</summary>
    public double ScaledFadeStartAltitude = 30000, ScaledFadeEndAltitude = 55000;
    public bool UseDetailTex, UseUntilingIfEnabled = true;
    public List<CloudType> CloudTypes = new List<CloudType>();
    public List<PainterTile> PainterTiles = new List<PainterTile>();
    public List<PainterTileMask> PainterTileMasks = new List<PainterTileMask>();

    /// <summary>The lowest and highest altitudes any of its cloud types reach (m above the layer's altitude base).</summary>
    public double MinAltitude { get { double m = double.PositiveInfinity; foreach (CloudType t in CloudTypes) m = Math.Min(m, t.MinAltitude); return CloudTypes.Count > 0 ? m : 0.0; } }
    public double MaxAltitude { get { double m = double.NegativeInfinity; foreach (CloudType t in CloudTypes) m = Math.Max(m, t.MaxAltitude); return CloudTypes.Count > 0 ? m : 0.0; } }
}

/// <summary>A cloud layer round a body (EVE_CLOUDS OBJECT): where it is, how it turns, and its flat, particle and volumetric parts.</summary>
public sealed class CloudLayer
{
    public string Name = string.Empty, Body = string.Empty;
    public double Altitude;
    public bool KillBodyRotation;
    public Double3 Speed, DetailSpeed, Offset;
    public CloudSpeedMode SpeedMode = CloudSpeedMode.Linear;
    public Double3 RotationAxis0 = new Double3(1, 0, 0), RotationAxis1 = new Double3(0, 1, 0), RotationAxis2 = new Double3(0, 0, 1);
    public double Arc = 360;
    public CloudsMaterial Material = new CloudsMaterial();
    public TimeCycle Time;
    public Layer2DSettings Layer2D;
    public LayerVolumeSettings LayerVolume;
    public VolumetricLayer Volume;
}

/// <summary>Raymarching quality for every layer (EVE_RAYMARCHED_CLOUDS_QUALITY).</summary>
public sealed class CloudsQuality
{
    /// <summary>Pixels a frame is built up from over that many frames (x1 .. x32): 9 renders one in nine each frame.</summary>
    public int TemporalUpscaling = 9;
    public bool NonTiling3DNoise = true, RenderCloudsInReflectionProbes = true;
    public double ScreenshotModeDenoisingIterations = 8;
    /// <summary>The light volume: cloud shadows and ambient light round the camera.</summary>
    public double HorizontalResolution = 256, VerticalResolution = 32, StepCount = 50, DirectLightTimeSlicing = 8, AmbientLightTimeSlicing = 32, TimewarpRateMultiplier = 3;

    /// <summary>The upscaling factors a config may name (x1 ... x32).</summary>
    public static readonly int[] UpscalingFactors = { 1, 2, 3, 4, 6, 8, 9, 12, 16, 32 };
}

/// <summary>Rain, snow or dust round the camera (EVE_PARTICLE_FIELD_CONFIG OBJECT).</summary>
public sealed class ParticleFieldConfig
{
    public string Name = string.Empty;
    public double FieldSize, FieldParticleCount, FallSpeed, TangentialSpeed, RandomDirectionStrength, ParticleSize, ParticleStretch;
    public double MinCoverageThreshold, MaxCoverageThreshold = 1;
    public CfgColor Color = CfgColor.White;
    public Double2 ParticleSheetCount = new Double2(1, 1);
    public TextureRef ParticleTexture = new TextureRef();
    public SplashesConfig Splashes;
}

/// <summary>Splashes where the particles hit something (splashes).</summary>
public sealed class SplashesConfig
{
    public double SplashesSpeed;
    public Double2 SplashesSize, SplashesSheetCount = new Double2(1, 1);
    public CfgColor Color = CfgColor.White;
    public TextureRef SplashTexture = new TextureRef();
}

/// <summary>Lightning in a layer (EVE_LIGHTNING_CONFIG OBJECT).</summary>
public sealed class LightningConfig
{
    public string Name = string.Empty;
    public double SpawnChancePerSecond, MinSpawnRange, LifeTime, LightIntensity, LightRange, SpawnAltitude, BoltWidth, BoltHeight;
    public double SoundMinDistance, SoundMaxDistance, SoundFarThreshold, RealisticAudioDelayMultiplier;
    public CfgColor LightColor = CfgColor.White, BoltColor = CfgColor.White;
    public TextureRef BoltTexture = new TextureRef();
    public Double2 LightningSheetCount = new Double2(1, 1);
    public List<string> NearSounds = new List<string>(), FarSounds = new List<string>();
}

/// <summary>Raindrops on the camera and windows (EVE_DROPLETS_CONFIG OBJECT).</summary>
public sealed class DropletsConfig
{
    public string Name = string.Empty;
    public double MinCoverageThreshold, MaxCoverageThreshold = 1, FadeOutStartSpeed, FadeOutEndSpeed, RefractionStrength, Translucency, SpecularStrength;
    public double Scale = 1, TriplanarTransitionSharpness, DryingSpeed, SpeedIncreaseFactor, MaxModulationSpeed;
    public double SideNoiseScale, SideLowSpeedNoiseStrength, SideHighSpeedNoiseStrength, LowSpeedStreakRatio, HighSpeedStreakRatio;
    public double LowSpeedTimeRandomness, HighSpeedTimeRandomness, TopNoiseScale, TopNoiseStrength;
    public Double3 Color = new Double3(1, 1, 1);
    public TextureRef Noise = new TextureRef();
    public List<SideDropletLayer> SideDropletLayers = new List<SideDropletLayer>();
    public List<TopDropletLayer> TopDropletLayers = new List<TopDropletLayer>();
}

public sealed class SideDropletLayer
{
    public string Name = string.Empty;
    public double FallSpeed, Scale = 1, DropletToTrailAspectRatio, StreakRatio;
}

public sealed class TopDropletLayer
{
    public string Name = string.Empty;
    public double Scale = 1, Speed;
}

/// <summary>How fast a kind of surface wets and dries (scenery, terrain, craft).</summary>
public sealed class Wetting
{
    public double WetnessAccumulationSpeed, WetnessDryingSpeed, WetDiffuse = 1, WetSmoothness, PuddleAccumulationSpeed, PuddleDryingSpeed, MaxPuddleAccumulation;
}

/// <summary>Wet ground and puddles under a body's rain (EVE_WET_SURFACES_CONFIG OBJECT).</summary>
public sealed class WetSurfacesConfig
{
    public string Name = string.Empty, Body = string.Empty;
    public double MinCoverageThreshold, MaxCoverageThreshold = 1, PuddleTextureScale = 1, RippleSpeed = 1, RippleScale = 1;
    public TextureRef PuddlesTexture = new TextureRef();
    public Wetting Scenery = new Wetting(), Terrain = new Wetting(), Craft = new Wetting();
}

/// <summary>Bodies that cast eclipse shadows on a body (EVE_SHADOWS OBJECT).</summary>
public sealed class EclipseShadows
{
    public string Body = string.Empty;
    public List<string> Casters = new List<string>();
}

/// <summary>A body whose terrain is let go of from further out, with its kill sphere moved (PQS_MANAGER OBJECT), so a
/// craft can fly down into a gas giant's lower clouds.</summary>
public sealed class PqsOverride
{
    public string Body = string.Empty;
    public double DeactivateDistance;
    public bool OverridesKillSphere;
    public double KillSphereAltitude;
}

/// <summary>Everything read from a set of EVE configs, and what in them could not be used.</summary>
public sealed class EveCloudsSet
{
    public List<CloudLayer> Layers = new List<CloudLayer>();
    public CloudsQuality Quality;
    public Dictionary<string, ParticleFieldConfig> ParticleFields = new Dictionary<string, ParticleFieldConfig>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LightningConfig> Lightning = new Dictionary<string, LightningConfig>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DropletsConfig> Droplets = new Dictionary<string, DropletsConfig>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, WetSurfacesConfig> WetSurfaces = new Dictionary<string, WetSurfacesConfig>(StringComparer.OrdinalIgnoreCase);
    public List<EclipseShadows> Shadows = new List<EclipseShadows>();
    public List<PqsOverride> Pqs = new List<PqsOverride>();
    public List<KSPVE.Pure.Config.ConfigIssue> Issues = new List<KSPVE.Pure.Config.ConfigIssue>();
}
