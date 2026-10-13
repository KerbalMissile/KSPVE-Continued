using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;

namespace KSPVE.Seasonal;

/// <summary>One body's overrides: a pretend axial tilt (a display setting), and the climate's inputs when the game's guess is wrong.</summary>
internal sealed class BodyClimateOverride
{
    public string Name;
    /// <summary>Pretend the axis is tilted by this many degrees (stock planets have none); null: the real tilt.</summary>
    public double? PretendTilt;
    /// <summary>How wet the world is compared with Earth (null: from whether it has an ocean and clouds).</summary>
    public double? Moisture;
    /// <summary>The global mean surface temperature (K) to hold to (null: the game's own sea level temperature).</summary>
    public double? MeanTemperature;
    /// <summary>False: no snow, ice or seasons on this body.</summary>
    public bool Enabled = true;

    public BodyClimateOverride Clone() => (BodyClimateOverride)MemberwiseClone();
}

/// <summary>
/// The climate feature's settings: from GameData/.../KSPVE_CLIMATE.cfg (the defaults and per body overrides), then the
/// player's changes from PluginData/user.cfg (KSPVE_CLIMATE_USER).
/// </summary>
internal static class ClimateConfig
{
    public const string Node = "KSPVE_CLIMATE", UserNode = "KSPVE_CLIMATE_USER", BodyNode = "BODY";

    /// <summary>Snow is blended into the scaled planets' textures.</summary>
    public static bool ScaledSnow = true;
    /// <summary>Snow is put on the local terrain in the deferred G-buffer.</summary>
    public static bool LocalSnow = true;
    /// <summary>Largest side (texels) of a scaled planet's blended texture, and how many bodies have one at a time.</summary>
    public static int ScaledResolution = 4096, ScaledBodies = 3;
    /// <summary>Display settings: snow's albedo (fresh snow is 0.8 to 0.9 in visible light), sparkle (0 off), keep craft clear of snow.</summary>
    public static double SnowAlbedo = 0.86, SnowSparkle = 0.0;
    public static bool KeepVehiclesClear = true;
    /// <summary>Wind pushes craft (through Ferram Aerospace Research's wind hook; gameplay, off by default).</summary>
    public static bool WindPush = false;
    /// <summary>The weather is looked at this many days (of the body) back for recent snowfall; 0 turns it off.</summary>
    public static double RecentWindowDays = 8.0;

    private static readonly Dictionary<string, BodyClimateOverride> Defaults = new Dictionary<string, BodyClimateOverride>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BodyClimateOverride> Bodies = new Dictionary<string, BodyClimateOverride>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bumps when anything that changes the climate changes (the tables are built again).</summary>
    public static int Revision { get; private set; } = 1;

    public static BodyClimateOverride For(string body)
    {
        return body != null && Bodies.TryGetValue(body, out BodyClimateOverride o) ? o : null;
    }

    public static bool TryGetTilt(string body, out double degrees)
    {
        BodyClimateOverride o = For(body);
        degrees = o != null && o.PretendTilt.HasValue ? o.PretendTilt.Value : 0.0;
        return o != null && o.PretendTilt.HasValue;
    }

    public static void SetPretendTilt(string body, double? degrees)
    {
        BodyClimateOverride o = For(body);
        if (o == null) Bodies[body] = o = new BodyClimateOverride { Name = body };
        if (o.PretendTilt == degrees) return;
        o.PretendTilt = degrees;
        Revision++;
    }

    public static void Load(List<ConfigIssue> issues)
    {
        Defaults.Clear();
        Bodies.Clear();
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(Node)) Apply(KspConfig.ToCfg(node), issues);
        foreach (KeyValuePair<string, BodyClimateOverride> pair in Bodies) Defaults[pair.Key] = pair.Value.Clone();
        foreach (CfgNode user in KspConfig.LoadUserNodes())
            if (user.Name == UserNode) Apply(user, issues);
        Revision++;
    }

    public static void ResetToDefaults()
    {
        ScaledSnow = true;
        LocalSnow = true;
        ScaledResolution = 4096;
        ScaledBodies = 3;
        SnowAlbedo = 0.86;
        SnowSparkle = 0.0;
        KeepVehiclesClear = true;
        WindPush = false;
        Bodies.Clear();
        foreach (KeyValuePair<string, BodyClimateOverride> pair in Defaults) Bodies[pair.Key] = pair.Value.Clone();
        Revision++;
    }

    private static void Apply(CfgNode node, List<ConfigIssue> issues)
    {
        ReadBool(node, "scaledSnow", ref ScaledSnow);
        ReadBool(node, "localSnow", ref LocalSnow);
        ReadBool(node, "keepVehiclesClear", ref KeepVehiclesClear);
        ReadBool(node, "windPush", ref WindPush);
        ReadDouble(node, "snowAlbedo", ref SnowAlbedo);
        ReadDouble(node, "snowSparkle", ref SnowSparkle);
        ReadDouble(node, "recentWindowDays", ref RecentWindowDays);
        double v = ScaledResolution;
        if (ReadDouble(node, "scaledResolution", ref v)) ScaledResolution = Math.Max(256, (int)v);
        v = ScaledBodies;
        if (ReadDouble(node, "scaledBodies", ref v)) ScaledBodies = Math.Max(0, (int)v);
        foreach (CfgNode child in node.Children)
        {
            if (!string.Equals(child.Name, BodyNode, StringComparison.OrdinalIgnoreCase)) continue;
            string name = child.GetLast("name");
            if (string.IsNullOrEmpty(name)) { issues.Add(new ConfigIssue(IssueSeverity.Warning, Node + "/" + BodyNode, "a BODY needs a name")); continue; }
            if (!Bodies.TryGetValue(name, out BodyClimateOverride o)) Bodies[name] = o = new BodyClimateOverride { Name = name };
            double d = 0.0;
            if (child.GetLast("pretendTilt") is string t) o.PretendTilt = string.Equals(t, "off", StringComparison.OrdinalIgnoreCase) || t.Length == 0 ? (double?)null : Parse(t, out d) ? d : o.PretendTilt;
            if (ReadDouble(child, "moisture", ref d)) o.Moisture = d;
            if (ReadDouble(child, "meanTemperature", ref d)) o.MeanTemperature = d;
            bool enabled = o.Enabled;
            if (ReadBool(child, "enabled", ref enabled)) o.Enabled = enabled;
        }
    }

    /// <summary>The settings as the user node (only what differs from the configured defaults is needed; the whole is small).</summary>
    public static CfgNode ToUserNode()
    {
        var node = new CfgNode(UserNode)
            .Add("scaledSnow", ScaledSnow ? "true" : "false")
            .Add("localSnow", LocalSnow ? "true" : "false")
            .Add("keepVehiclesClear", KeepVehiclesClear ? "true" : "false")
            .Add("windPush", WindPush ? "true" : "false")
            .Add("snowAlbedo", SnowAlbedo.ToString("R", CultureInfo.InvariantCulture))
            .Add("snowSparkle", SnowSparkle.ToString("R", CultureInfo.InvariantCulture))
            .Add("scaledResolution", ScaledResolution.ToString(CultureInfo.InvariantCulture))
            .Add("scaledBodies", ScaledBodies.ToString(CultureInfo.InvariantCulture));
        foreach (BodyClimateOverride o in Bodies.Values)
        {
            var child = new CfgNode(BodyNode).Add("name", o.Name);
            child.Add("pretendTilt", o.PretendTilt.HasValue ? o.PretendTilt.Value.ToString("R", CultureInfo.InvariantCulture) : "off");
            if (o.Moisture.HasValue) child.Add("moisture", o.Moisture.Value.ToString("R", CultureInfo.InvariantCulture));
            if (o.MeanTemperature.HasValue) child.Add("meanTemperature", o.MeanTemperature.Value.ToString("R", CultureInfo.InvariantCulture));
            child.Add("enabled", o.Enabled ? "true" : "false");
            node.AddChild(child);
        }
        return node;
    }

    private static bool Parse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool ReadDouble(CfgNode node, string key, ref double target)
    {
        string text = node.GetLast(key);
        if (text == null || !Parse(text, out double v)) return false;
        target = v;
        return true;
    }

    private static bool ReadBool(CfgNode node, string key, ref bool target)
    {
        string text = node.GetLast(key);
        if (text == null) return false;
        if (bool.TryParse(text, out bool b)) { target = b; return true; }
        return false;
    }
}
