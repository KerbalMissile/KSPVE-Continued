using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Water;

namespace KSPVE.Water;

/// <summary>
/// The water's settings as the game holds them: KSPVE_WATER nodes from the configs (the defaults are
/// gamedata/Config/KSPVE_WATER.cfg), then the player's tweaks from user.cfg's KSPVE_WATER_USER node on top. The window edits the
/// fields; Save writes only what differs from the configs.
/// </summary>
internal static class WaterConfig
{
    public const string UserNodeName = "KSPVE_WATER_USER";

    public static bool Enabled, Oceans, Foam, Refraction, OrbitalGlint, Rivers, Lakes, DiskCache, Buoyancy;
    public static OceanQuality Quality;
    public static double WaveScale, Choppiness, WindOverride, Fetch, NearRadius, Brightness, Chlorophyll, BreakerIndex;
    public static double RiverMinDischarge, LakeMinDepth, LakeMinArea, Runoff, WindowKm, SamplingBudgetMs, FlowAnimation;
    public static int GridSize;

    /// <summary>What the configs alone say (so Save can write the differences).</summary>
    private static SettingsValues _baseline;
    private static SettingsValues _loaded;

    public static readonly List<LiquidDef> Liquids = new List<LiquidDef>();

    public static void Load(List<ConfigIssue> issues)
    {
        CfgNode configs = new CfgNode(WaterSettings.Schema.NodeName);
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(WaterSettings.Schema.NodeName))
            configs = CfgNode.Overlay(configs, KspConfig.ToCfg(node));
        _baseline = WaterSettings.Schema.Resolve(WithoutLiquids(configs), issues);
        CfgNode merged = configs;
        foreach (CfgNode user in KspConfig.LoadUserNodes())
            if (user.Name == UserNodeName) merged = CfgNode.Overlay(merged, user);
        _loaded = WaterSettings.Schema.Resolve(WithoutLiquids(merged), issues);
        Liquids.Clear();
        foreach (CfgNode child in merged.Children)
            if (string.Equals(child.Name, "LIQUID", StringComparison.OrdinalIgnoreCase)) Liquids.Add(ReadLiquid(child, issues));
        Apply(_loaded);
    }

    private static CfgNode WithoutLiquids(CfgNode node)
    {
        var copy = new CfgNode(node.Name);
        copy.Values.AddRange(node.Values);
        return copy;
    }

    private static LiquidDef ReadLiquid(CfgNode node, List<ConfigIssue> issues)
    {
        var liquid = new LiquidDef { Body = node.GetLast("body") ?? string.Empty };
        liquid.RefractiveIndex = ParseDouble(node.GetLast("refractiveIndex"), liquid.RefractiveIndex);
        liquid.SurfaceTensionOverDensity = ParseDouble(node.GetLast("surfaceTension"), liquid.SurfaceTensionOverDensity);
        liquid.DeepColor = ParseColor(node.GetLast("color"), liquid.DeepColor);
        liquid.Absorption = ParseColor(node.GetLast("absorption"), liquid.Absorption);
        if (string.IsNullOrEmpty(liquid.Body)) issues.Add(new ConfigIssue(IssueSeverity.Warning, "KSPVE_WATER/LIQUID", "has no body; ignored"));
        return liquid;
    }

    private static double ParseDouble(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    private static Double3 ParseColor(string s, Double3 fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        string[] p = s.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return fallback;
        return new Double3(ParseDouble(p[0], 0), ParseDouble(p[1], 0), ParseDouble(p[2], 0));
    }

    /// <summary>The liquid a body's ocean is made of, or null for water.</summary>
    public static LiquidDef LiquidOf(string body)
    {
        foreach (LiquidDef l in Liquids) if (string.Equals(l.Body, body, StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }

    private static void Apply(SettingsValues v)
    {
        Enabled = v.Get(WaterSettings.Enabled);
        Oceans = v.Get(WaterSettings.Oceans);
        Quality = v.Get(WaterSettings.Quality);
        WaveScale = v.Get(WaterSettings.WaveScale);
        Choppiness = v.Get(WaterSettings.Choppiness);
        WindOverride = v.Get(WaterSettings.WindOverride);
        Fetch = v.Get(WaterSettings.Fetch);
        NearRadius = v.Get(WaterSettings.NearGridRadius);
        Brightness = v.Get(WaterSettings.Brightness);
        Chlorophyll = v.Get(WaterSettings.Turbidity);
        Foam = v.Get(WaterSettings.Foam);
        Refraction = v.Get(WaterSettings.Refraction);
        OrbitalGlint = v.Get(WaterSettings.OrbitalGlint);
        BreakerIndex = v.Get(WaterSettings.ShoalingFoam);
        Rivers = v.Get(WaterSettings.Rivers);
        Lakes = v.Get(WaterSettings.Lakes);
        RiverMinDischarge = v.Get(WaterSettings.RiverThreshold);
        LakeMinDepth = v.Get(WaterSettings.LakeMinDepth);
        LakeMinArea = v.Get(WaterSettings.LakeMinArea);
        Runoff = v.Get(WaterSettings.RunoffCoefficient);
        WindowKm = v.Get(WaterSettings.WindowKm);
        GridSize = v.Get(WaterSettings.GridSize);
        SamplingBudgetMs = v.Get(WaterSettings.SamplingBudgetMs);
        DiskCache = v.Get(WaterSettings.DiskCache);
        FlowAnimation = v.Get(WaterSettings.FlowSpeedScale);
        Buoyancy = v.Get(WaterSettings.Buoyancy);
    }

    public static void ResetToConfigs() => Apply(_baseline ?? WaterSettings.Schema.Resolve(null, new List<ConfigIssue>()));

    /// <summary>A key that changes whenever a setting that needs the hydrology worked out again does.</summary>
    public static string HydrologyKey =>
        string.Join("|", WindowKm.ToString("R", CultureInfo.InvariantCulture), GridSize, RiverMinDischarge.ToString("R", CultureInfo.InvariantCulture),
            LakeMinDepth.ToString("R", CultureInfo.InvariantCulture), LakeMinArea.ToString("R", CultureInfo.InvariantCulture), Runoff.ToString("R", CultureInfo.InvariantCulture), Lakes);

    public static CfgNode UserNode()
    {
        var node = new CfgNode(UserNodeName);
        SettingsValues b = _baseline ?? WaterSettings.Schema.Resolve(null, new List<ConfigIssue>());
        Put(node, WaterSettings.Enabled, Enabled, b);
        Put(node, WaterSettings.Oceans, Oceans, b);
        PutEnum(node, WaterSettings.Quality, Quality, b);
        Put(node, WaterSettings.WaveScale, WaveScale, b);
        Put(node, WaterSettings.Choppiness, Choppiness, b);
        Put(node, WaterSettings.WindOverride, WindOverride, b);
        Put(node, WaterSettings.Fetch, Fetch, b);
        Put(node, WaterSettings.NearGridRadius, NearRadius, b);
        Put(node, WaterSettings.Brightness, Brightness, b);
        Put(node, WaterSettings.Turbidity, Chlorophyll, b);
        Put(node, WaterSettings.Foam, Foam, b);
        Put(node, WaterSettings.Refraction, Refraction, b);
        Put(node, WaterSettings.OrbitalGlint, OrbitalGlint, b);
        Put(node, WaterSettings.ShoalingFoam, BreakerIndex, b);
        Put(node, WaterSettings.Rivers, Rivers, b);
        Put(node, WaterSettings.Lakes, Lakes, b);
        Put(node, WaterSettings.RiverThreshold, RiverMinDischarge, b);
        Put(node, WaterSettings.LakeMinDepth, LakeMinDepth, b);
        Put(node, WaterSettings.LakeMinArea, LakeMinArea, b);
        Put(node, WaterSettings.RunoffCoefficient, Runoff, b);
        Put(node, WaterSettings.WindowKm, WindowKm, b);
        Put(node, WaterSettings.GridSize, GridSize, b);
        Put(node, WaterSettings.SamplingBudgetMs, SamplingBudgetMs, b);
        Put(node, WaterSettings.DiskCache, DiskCache, b);
        Put(node, WaterSettings.FlowSpeedScale, FlowAnimation, b);
        Put(node, WaterSettings.Buoyancy, Buoyancy, b);
        return node;
    }

    private static void Put(CfgNode node, SettingDef<bool> def, bool value, SettingsValues baseline)
    {
        if (baseline.Get(def) != value) node.Add(def.Key, value ? "True" : "False");
    }

    private static void Put(CfgNode node, SettingDef<double> def, double value, SettingsValues baseline)
    {
        if (Math.Abs(baseline.Get(def) - value) > 1e-12) node.Add(def.Key, value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static void Put(CfgNode node, SettingDef<int> def, int value, SettingsValues baseline)
    {
        if (baseline.Get(def) != value) node.Add(def.Key, value.ToString(CultureInfo.InvariantCulture));
    }

    private static void PutEnum(CfgNode node, SettingDef<OceanQuality> def, OceanQuality value, SettingsValues baseline)
    {
        if (baseline.Get(def) != value) node.Add(def.Key, value.ToString());
    }
}
