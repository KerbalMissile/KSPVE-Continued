using System;
using System.Globalization;
using KSPVE.Pure.Config;

namespace KSPVE.RayTracing;

/// <summary>What the player can set for ray-traced global illumination (kept in user.cfg as KSPVE_RAYTRACING_USER).</summary>
internal sealed class RtSettings
{
    public const string UserNodeName = "KSPVE_RAYTRACING_USER";

    public enum DebugView { Off, LightOnly }

    /// <summary>Ray-traced bounce light on the local camera (off by default while it is new).</summary>
    public bool Enabled;

    /// <summary>The share of the screen's width and height the rays are traced at (0.5 = a quarter of the pixels).</summary>
    public double Resolution = 0.5;

    /// <summary>How far rays look for geometry before they see the planet or the sky (m).</summary>
    public double Reach = 2000.0;

    /// <summary>Shadow rays where bounce rays land, so only sunlit surfaces bounce sunlight (realistic; off is faster).</summary>
    public bool SunShadows = true;

    /// <summary>Rays that leave the loaded scene meet the planet (its colour map, lit by the sun): ground bounce from orbit.</summary>
    public bool PlanetBounce = true;

    /// <summary>Replace KSP's flat ambient light with the traced light (the realistic choice; off keeps both).</summary>
    public bool ReplaceAmbient = true;

    /// <summary>Frames the temporal accumulation averages over at most.</summary>
    public int History = 32;

    /// <summary>A-trous filter passes (each doubles the reach of the smoothing).</summary>
    public int FilterPasses = 4;

    /// <summary>Display: how much of the bounce light is added (1 = as traced).</summary>
    public double Strength = 1.0;

    public DebugView Debug = DebugView.Off;

    public CfgNode ToCfg()
    {
        var node = new CfgNode(UserNodeName);
        node.Add("enabled", Enabled ? "True" : "False");
        node.Add("resolution", F(Resolution));
        node.Add("reach", F(Reach));
        node.Add("sunShadows", SunShadows ? "True" : "False");
        node.Add("planetBounce", PlanetBounce ? "True" : "False");
        node.Add("replaceAmbient", ReplaceAmbient ? "True" : "False");
        node.Add("history", History.ToString(CultureInfo.InvariantCulture));
        node.Add("filterPasses", FilterPasses.ToString(CultureInfo.InvariantCulture));
        node.Add("strength", F(Strength));
        return node;
    }

    public void From(CfgNode node)
    {
        if (node == null) return;
        Enabled = Bool(node, "enabled", Enabled);
        Resolution = Clamp(Number(node, "resolution", Resolution), 0.1, 1.0);
        Reach = Clamp(Number(node, "reach", Reach), 50.0, 100000.0);
        SunShadows = Bool(node, "sunShadows", SunShadows);
        PlanetBounce = Bool(node, "planetBounce", PlanetBounce);
        ReplaceAmbient = Bool(node, "replaceAmbient", ReplaceAmbient);
        History = (int)Clamp(Number(node, "history", History), 1, 256);
        FilterPasses = (int)Clamp(Number(node, "filterPasses", FilterPasses), 0, 6);
        Strength = Clamp(Number(node, "strength", Strength), 0.0, 100.0);
    }

    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

    private static double Number(CfgNode node, string key, double fallback) =>
        double.TryParse(node.GetLast(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) ? v : fallback;

    private static bool Bool(CfgNode node, string key, bool fallback) =>
        bool.TryParse(node.GetLast(key), out bool v) ? v : fallback;
}
