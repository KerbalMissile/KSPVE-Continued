using System;
using System.Globalization;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.Config;

namespace KSPVE.Aurora;

/// <summary>
/// The aurora feature's settings: which kinds are drawn, and the display settings (every one exists only to make the sky
/// visible or cheap, and its default is the realistic value). Defaults come from the KSPVE_AURORA node's own keys, the
/// player's choices from KSPVE_AURORA_USER in user.cfg.
/// </summary>
internal static class AuroraSettings
{
    public const string UserNodeName = "KSPVE_AURORA_USER";

    public static bool Enabled = true;
    // Kinds.
    public static bool Arcs = true, Diffuse = true, Pulsating = true, Black = true, Proton = true, PolarCap = true, Sar = true, Steve = true;
    public static bool Global = true, Crustal = true, Footprints = true, Airglow = true;
    // Display.
    /// <summary>Multiplies every aurora's brightness; 1 is the realistic brightness of the model (IBC classes in kilorayleighs).</summary>
    public static double AuroraBoost = 1.0;
    /// <summary>The same for airglow and nightglow (about 250 R of green at Earth).</summary>
    public static double AirglowBoost = 1.0;
    /// <summary>
    /// The camera the dark-adapted view is exposed like: ISO, f-number and shutter time. A photographer's aurora shot is
    /// ISO 3200-6400, f/1.4-2.8, 5-15 s, some 5 million times the light of a bright day's exposure (see NightExposure).
    /// </summary>
    public static double NightIso = 1600.0, NightAperture = 2.0, NightShutter = 2.0;
    /// <summary>Size of the march's buffer as a fraction of the screen (it is smooth, and accumulated over frames).</summary>
    public static double Resolution = 0.5;
    /// <summary>How much of the last frames is kept (temporal accumulation, 0 = none).</summary>
    public static double Smoothing = 0.75;

    public static double NightGain => NightExposure.Gain(Math.Max(NightAperture, 0.5), Math.Max(NightShutter, 1e-4), Math.Max(NightIso, 1.0));

    private static readonly string[] BoolKeys =
    {
        "enabled", "arcs", "diffuse", "pulsating", "blackAurora", "proton", "polarCap", "sar", "steve", "global", "crustal", "footprints", "airglow",
    };

    private static ref bool BoolRef(int i)
    {
        switch (i)
        {
            case 0: return ref Enabled;
            case 1: return ref Arcs;
            case 2: return ref Diffuse;
            case 3: return ref Pulsating;
            case 4: return ref Black;
            case 5: return ref Proton;
            case 6: return ref PolarCap;
            case 7: return ref Sar;
            case 8: return ref Steve;
            case 9: return ref Global;
            case 10: return ref Crustal;
            case 11: return ref Footprints;
            default: return ref Airglow;
        }
    }

    /// <summary>Reads a node (the config's own keys, then the user's node on top).</summary>
    public static void Apply(CfgNode node)
    {
        if (node == null) return;
        for (int i = 0; i < BoolKeys.Length; i++)
        {
            string v = node.GetLast(BoolKeys[i]);
            if (v != null && bool.TryParse(v.Trim(), out bool b)) BoolRef(i) = b;
        }
        AuroraBoost = Number(node, "auroraBoost", AuroraBoost);
        AirglowBoost = Number(node, "airglowBoost", AirglowBoost);
        NightIso = Number(node, "nightIso", NightIso);
        NightAperture = Number(node, "nightAperture", NightAperture);
        NightShutter = Number(node, "nightShutter", NightShutter);
        Resolution = Number(node, "resolution", Resolution);
        Smoothing = Number(node, "smoothing", Smoothing);
    }

    public static CfgNode ToNode()
    {
        var n = new CfgNode(UserNodeName);
        for (int i = 0; i < BoolKeys.Length; i++) n.Add(BoolKeys[i], BoolRef(i) ? "True" : "False");
        n.Add("auroraBoost", Fmt(AuroraBoost));
        n.Add("airglowBoost", Fmt(AirglowBoost));
        n.Add("nightIso", Fmt(NightIso));
        n.Add("nightAperture", Fmt(NightAperture));
        n.Add("nightShutter", Fmt(NightShutter));
        n.Add("resolution", Fmt(Resolution));
        n.Add("smoothing", Fmt(Smoothing));
        return n;
    }

    public static void Reset()
    {
        Enabled = Arcs = Diffuse = Pulsating = Black = Proton = PolarCap = Sar = Steve = Global = Crustal = Footprints = Airglow = true;
        AuroraBoost = 1.0;
        AirglowBoost = 1.0;
        NightIso = 1600.0; NightAperture = 2.0; NightShutter = 2.0;
        Resolution = 0.5; Smoothing = 0.75;
    }

    private static double Number(CfgNode node, string key, double fallback)
    {
        string v = node.GetLast(key);
        return v != null && double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
    }

    private static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
