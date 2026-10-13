using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Water;
using KSPVE.UI;

namespace KSPVE.Water;

/// <summary>
/// KSPVE's water: oceans with FFT waves (a swap of the look of KSP's ocean quads, plus a finely displaced mesh round the camera),
/// the sun's glint on a sea from space, and rivers and lakes at their real elevation. Settings live in the KSPVE_WATER node
/// (gamedata/Config/KSPVE_WATER.cfg) and the window's own section.
/// </summary>
internal sealed class WaterFeature : Feature
{
    private readonly OceanSystem _ocean = new OceanSystem();
    private readonly OceanGlintRenderer _glint = new OceanGlintRenderer();
    private readonly RiverSystem _rivers = new RiverSystem();

    public static WaterFeature Instance { get; private set; }

    public override string Name => "Water";
    public override int Order => 50;
    public override string SettingsTitle => "Water: oceans, rivers and lakes";

    /// <summary>The rivers and lakes being drawn, for the buoyancy module.</summary>
    public RiverSystem Rivers => _rivers;

    public WaterFeature() { Instance = this; }

    public override void Load(List<ConfigIssue> issues)
    {
        WaterConfig.Load(issues);
        // The climate's sea ice, when it has one for the body (a hook for anything else to replace).
        OceanSystem.SeaIceMap = body => KSPVE.Seasonal.SnowCover.SeaIceOf(body);
    }

    public override void Update(bool allowed)
    {
        TerrainSampler.Update(WaterConfig.SamplingBudgetMs);
        _ocean.Update(allowed);
        _glint.Update(allowed);
        _rivers.Update(allowed);
    }

    public override void Shutdown()
    {
        _ocean.Shutdown(false);
        _glint.Shutdown();
        _rivers.Shutdown();
    }

    public override CfgNode UserNode() => WaterConfig.UserNode();

    public override void ResetToDefaults() => WaterConfig.ResetToConfigs();

    public override void DrawSettings()
    {
        WaterConfig.Enabled = Gui.Toggle("Water on", WaterConfig.Enabled);
        if (!WaterConfig.Enabled) return;

        Gui.Header("Oceans");
        WaterConfig.Oceans = Gui.Toggle("Replace the look of the ocean (FFT waves; KSP's ocean physics is not touched)", WaterConfig.Oceans);
        WaterConfig.Quality = Gui.Choice("Ocean quality (cascades and resolution of the waves)", WaterConfig.Quality, 4);
        WaterConfig.WaveScale = Gui.Slider("Wave height scale (1 is the real sea)", WaterConfig.WaveScale, 0.0, 4.0);
        WaterConfig.Choppiness = Gui.Slider("Choppiness (1 is physical)", WaterConfig.Choppiness, 0.0, 2.0);
        WaterConfig.WindOverride = Gui.Slider("Wind speed (m/s at 10 m; below 0 follows the weather)", WaterConfig.WindOverride, -1.0, 40.0, "0.0");
        WaterConfig.Fetch = Gui.Slider("Fetch (km of open water the wind has blown over)", WaterConfig.Fetch / 1000.0, 1.0, 1000.0, "0") * 1000.0;
        WaterConfig.NearRadius = Gui.Slider("Fine mesh radius (m)", WaterConfig.NearRadius, 300.0, 8000.0, "0");
        WaterConfig.Chlorophyll = Gui.Slider("Chlorophyll (mg/m3: 0.03 clear, 1 coastal green)", WaterConfig.Chlorophyll, 0.01, 10.0);
        WaterConfig.Foam = Gui.Toggle("Whitecaps and shore foam", WaterConfig.Foam);
        WaterConfig.Refraction = Gui.Toggle("Refraction of the sea floor", WaterConfig.Refraction);
        WaterConfig.OrbitalGlint = Gui.Toggle("The sun's glint on the sea from orbit", WaterConfig.OrbitalGlint);
        WaterConfig.Brightness = Gui.Slider("Water brightness (display; 1 is physical)", WaterConfig.Brightness, 0.0, 4.0);
        Gui.Note("From the weather's wind and the fetch, by the JONSWAP spectrum; whitecaps follow Monahan's coverage, the glint Cox and Munk's roughness.");

        Gui.Header("Rivers and lakes");
        WaterConfig.Rivers = Gui.Toggle("Rivers and lakes at their real elevation", WaterConfig.Rivers);
        if (WaterConfig.Rivers)
        {
            WaterConfig.Lakes = Gui.Toggle("Lakes (basins filled to their spill level)", WaterConfig.Lakes);
            WaterConfig.RiverMinDischarge = Math.Max(0.001, Math.Pow(10.0, Gui.Slider("River density: least discharge drawn (log10 m3/s; lower = more streams)", Math.Log10(WaterConfig.RiverMinDischarge), -2.0, 3.0)));
            WaterConfig.LakeMinDepth = Gui.Slider("Smallest lake depth (m)", WaterConfig.LakeMinDepth, 0.5, 50.0, "0.0");
            WaterConfig.LakeMinArea = Gui.Slider("Smallest lake area (km2)", WaterConfig.LakeMinArea / 1e6, 0.005, 10.0, "0.000") * 1e6;
            WaterConfig.Runoff = Gui.Slider("Rain that runs off (display; 1 is the climate's)", WaterConfig.Runoff, 0.0, 5.0);
            WaterConfig.WindowKm = Gui.Slider("Window width (km)", WaterConfig.WindowKm, 8.0, 96.0, "0");
            WaterConfig.GridSize = Gui.IntSlider("Window resolution (cells across)", WaterConfig.GridSize, 64, 512);
            WaterConfig.SamplingBudgetMs = Gui.Slider("Terrain reading per frame (ms)", WaterConfig.SamplingBudgetMs, 0.2, 12.0, "0.0");
            WaterConfig.DiskCache = Gui.Toggle("Keep the terrain heights between sessions", WaterConfig.DiskCache);
            WaterConfig.FlowAnimation = Gui.Slider("Flow animation speed (1 is the real speed)", WaterConfig.FlowAnimation, 0.0, 5.0);
            HydroTile t = _rivers.Current;
            if (t != null)
                Gui.Note($"Window: {t.State}, {t.WindowMeters / 1000.0:0} km at {t.CellSize:0} m; terrain reads queued: {TerrainSampler.Pending}"
                         + (t.Result != null ? $"; {t.Result.Reaches.Count} reaches, {t.Result.Lakes.Count} lakes" : string.Empty));
        }

        Gui.Header("Gameplay (off by default)");
        WaterConfig.Buoyancy = Gui.Toggle("Vessels float on lakes and rivers (KSP's own buoyancy only knows the sea)", WaterConfig.Buoyancy);
    }
}
