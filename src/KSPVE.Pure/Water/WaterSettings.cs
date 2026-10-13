using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Water;

/// <summary>How finely the ocean's waves are resolved.</summary>
public enum OceanQuality
{
    /// <summary>Two 128x128 cascades: cheap, for weak graphics cards.</summary>
    Low,
    /// <summary>Three 256x256 cascades.</summary>
    Medium,
    /// <summary>Four 256x256 cascades (waves from 1 cm to 1 km).</summary>
    High,
    /// <summary>Four 512x512 cascades.</summary>
    Ultra
}

/// <summary>
/// The KSPVE_WATER node: the ocean, rivers and lakes. Every look setting defaults to the realistic value (a named display
/// setting is only there to exaggerate, never to be needed for the real thing); the options that change the game (floating on
/// lakes and rivers) are off.
/// </summary>
public static class WaterSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE_WATER");

    public static readonly BoolSetting Enabled = Schema.Add(new BoolSetting("enabled", true, "Draw KSPVE's water at all."));

    // ---- oceans
    public static readonly BoolSetting Oceans = Schema.Add(new BoolSetting("oceans", true, "Replace the look of each body's ocean (KSP's ocean physics is not touched)."));
    public static readonly EnumSetting<OceanQuality> Quality = Schema.Add(new EnumSetting<OceanQuality>("oceanQuality", OceanQuality.High, "Low, Medium, High or Ultra: how many wave cascades and how finely each is resolved."));
    public static readonly DoubleSetting WaveScale = Schema.Add(new DoubleSetting("waveScale", 1.0, 0.0, 10.0, "Display setting: multiplies every wave's height. 1 is the real sea state for the local wind and fetch."));
    public static readonly DoubleSetting Choppiness = Schema.Add(new DoubleSetting("choppiness", 1.0, 0.0, 3.0, "Horizontal displacement of the waves (Tessendorf's lambda; 1 is the physical Gerstner-like motion)."));
    public static readonly DoubleSetting WindOverride = Schema.Add(new DoubleSetting("windSpeed", -1.0, -1.0, 80.0, "Wind speed at 10 m (m/s) for the sea state. Negative: follow the local weather (dynamic weather's wind)."));
    public static readonly DoubleSetting Fetch = Schema.Add(new DoubleSetting("fetch", 200000.0, 1000.0, 5e7, "How far the wind has blown over the sea (m). 200 km makes a nearly fully developed sea at moderate winds."));
    public static readonly DoubleSetting NearGridRadius = Schema.Add(new DoubleSetting("nearRadius", 2500.0, 200.0, 20000.0, "Radius (m) round the camera drawn as a finely displaced mesh; farther out the sea uses KSP's own ocean quads, shaded from the same waves."));
    public static readonly DoubleSetting Brightness = Schema.Add(new DoubleSetting("brightness", 1.0, 0.0, 10.0, "Display setting: scales the sunlit water's radiance. 1 is the physical value."));
    public static readonly DoubleSetting Turbidity = Schema.Add(new DoubleSetting("chlorophyll", 0.1, 0.0, 50.0, "Chlorophyll concentration (mg/m3) of the sea: 0.03 is the clearest ocean, 0.1 the open ocean, 1 to 10 coastal and green."));
    public static readonly BoolSetting Foam = Schema.Add(new BoolSetting("foam", true, "Whitecaps (from the waves' Jacobian, to Monahan's coverage) and foam on the shore."));
    public static readonly BoolSetting Refraction = Schema.Add(new BoolSetting("refraction", true, "Show the sea floor through the water, tinted by the depth."));
    public static readonly BoolSetting OrbitalGlint = Schema.Add(new BoolSetting("orbitalGlint", true, "From orbit, the sun's glint on the ocean with Cox and Munk's roughness."));
    public static readonly DoubleSetting ShoalingFoam = Schema.Add(new DoubleSetting("breakerIndex", 0.78, 0.3, 1.5, "Wave height over depth at which a wave breaks in shallow water (McCowan's 0.78)."));

    // ---- rivers and lakes
    public static readonly BoolSetting Rivers = Schema.Add(new BoolSetting("rivers", true, "Rivers and lakes at their real elevation, running down the terrain."));
    public static readonly BoolSetting Lakes = Schema.Add(new BoolSetting("lakes", true, "Fill basins to their spill level."));
    public static readonly DoubleSetting RiverThreshold = Schema.Add(new DoubleSetting("riverMinDischarge", 0.5, 0.001, 1e6, "River density: the least discharge (m3/s) at which a channel is drawn. Lower draws more, smaller streams; 0.5 is a brook a couple of metres wide."));
    public static readonly DoubleSetting LakeMinDepth = Schema.Add(new DoubleSetting("lakeMinDepth", 1.0, 0.1, 1000.0, "The shallowest basin (m of filling) drawn as a lake."));
    public static readonly DoubleSetting LakeMinArea = Schema.Add(new DoubleSetting("lakeMinArea", 20000.0, 100.0, 1e10, "The smallest lake drawn (m2)."));
    public static readonly DoubleSetting RunoffCoefficient = Schema.Add(new DoubleSetting("runoff", 1.0, 0.0, 5.0, "Display setting: multiplies the rain that runs off. 1 is the climate's own."));
    public static readonly DoubleSetting WindowKm = Schema.Add(new DoubleSetting("windowKm", 32.0, 4.0, 128.0, "Side (km) of the square of terrain round the camera whose water is worked out."));
    public static readonly IntSetting GridSize = Schema.Add(new IntSetting("gridSize", 256, 64, 1024, "Cells along the window's side (cell size = window / this)."));
    public static readonly DoubleSetting SamplingBudgetMs = Schema.Add(new DoubleSetting("samplingBudgetMs", 2.0, 0.1, 30.0, "Milliseconds per frame spent reading the terrain's heights (on the main thread; the planet's terrain code is not safe from other threads)."));
    public static readonly BoolSetting DiskCache = Schema.Add(new BoolSetting("diskCache", true, "Keep the sampled terrain heights between sessions (PluginData/WaterCache)."));
    public static readonly DoubleSetting FlowSpeedScale = Schema.Add(new DoubleSetting("flowAnimation", 1.0, 0.0, 10.0, "Display setting: speeds up the animated flow on the water. 1 is the real speed."));

    // ---- gameplay (off)
    public static readonly BoolSetting Buoyancy = Schema.Add(new BoolSetting("lakeBuoyancy", false, "Gameplay: vessels float on lakes and rivers (KSP only lets them float on the sea)."));
}

/// <summary>Seawater and other liquids' optical properties (a LIQUID node of KSPVE_WATER names a body).</summary>
public sealed class LiquidDef
{
    public string Body = string.Empty;
    /// <summary>Real refractive index (water 1.34; liquid methane 1.29).</summary>
    public double RefractiveIndex = WaterOptics.SeawaterIndex;
    /// <summary>The colour of deep water seen from above (linear), or negative: from the water optics.</summary>
    public Double3 DeepColor = new Double3(-1, -1, -1);
    /// <summary>Absorption (1/m) at red, green and blue; negative: pure water's.</summary>
    public Double3 Absorption = new Double3(-1, -1, -1);
    /// <summary>Surface tension / density (m3/s2), which shifts the capillary waves.</summary>
    public double SurfaceTensionOverDensity = 7.28e-5;

    public bool HasColor => DeepColor.X >= 0.0;
    public bool IsWater => RefractiveIndex > 1.32 && !HasColor && Absorption.X < 0.0;
}
