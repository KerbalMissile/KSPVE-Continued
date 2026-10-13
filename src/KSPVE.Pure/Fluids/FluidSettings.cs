using System;
using System.Globalization;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Fluids;

/// <summary>What the player sees of a brown dwarf. Only <see cref="Eye"/> is what an eye would see.</summary>
public enum FluidView
{
    /// <summary>Realistic: the visible light (380 to 780 nm) of each deck's blackbody through the alkali-absorbing gas above it.</summary>
    Eye = 0,
    /// <summary>
    /// A thermal-infrared camera at 5 micrometres, as Gemini North and Juno see Jupiter: heat from the deep, hot layers glows
    /// through gaps and thin spots in the clouds, thick clouds are dark, and the radiance is shown in a false-colour ramp (deep
    /// red-brown, orange, yellow, white). Not what the eye sees: in visible light a gas giant does not glow. A display choice.
    /// </summary>
    ThermalInfrared = 1
}

/// <summary>
/// The player's settings for the fluid-dynamics atmospheres, and the global keys of KSPVE_FLUIDS. Brightness boost is a named
/// display setting: 1 is the realistic brightness (a 1500 K L dwarf shines at about four millionths of the Sun's disc in
/// visible light, so a boost is what makes a dark brown dwarf visible to an eye that has not adapted).
/// </summary>
public static class FluidSettings
{
    public const string UserNode = "KSPVE_FLUIDS_USER";
    public const string ConfigNode = "KSPVE_FLUIDS";

    public static bool Enabled = true;
    /// <summary>Display: multiplies the brown dwarfs' radiance (1 = realistic).</summary>
    public static double Boost = 1.0;
    /// <summary>The view of the dwarf (a display choice).</summary>
    public static FluidView View = FluidView.Eye;
    /// <summary>Brown dwarfs found in the game (stars with kind = browndwarf or a temperature below 2500 K) get an atmosphere.</summary>
    public static bool BrownDwarfs = true;
    /// <summary>Gas giants without EVE clouds get the same simulation (off: they keep what the game draws).</summary>
    public static bool GasGiants;
    /// <summary>Tidally locked hot giants named in the config get day-night contrast and a super-rotating jet.</summary>
    public static bool HotJupiters = true;
    /// <summary>The share of one core's time the simulation may use, 0.05 to 1.</summary>
    public static double WorkShare = 0.35;
    /// <summary>
    /// How far behind the game clock the simulation may fall, days. Under time warp the real-time work cap cannot keep up:
    /// the model lags by up to this much and beyond it jumps ahead without integrating (the flow is statistically steady, so
    /// the jump shows as a new weather state, not a different planet).
    /// </summary>
    public static double MaxLagDays = 20.0;

    public static void Reset()
    {
        Enabled = true;
        Boost = 1.0;
        View = FluidView.Eye;
        BrownDwarfs = true;
        GasGiants = false;
        HotJupiters = true;
        WorkShare = 0.35;
        MaxLagDays = 20.0;
    }

    /// <summary>Reads the global keys of a KSPVE_FLUIDS or KSPVE_FLUIDS_USER node.</summary>
    public static void Read(CfgNode node)
    {
        string v;
        if ((v = node.GetLast("enabled")) != null) Enabled = Flag(v, Enabled);
        if ((v = node.GetLast("brightnessBoost")) != null) Boost = Num(v, Boost);
        if ((v = node.GetLast("view")) != null) View = ParseView(v, View);
        if ((v = node.GetLast("brownDwarfs")) != null) BrownDwarfs = Flag(v, BrownDwarfs);
        if ((v = node.GetLast("gasGiants")) != null) GasGiants = Flag(v, GasGiants);
        if ((v = node.GetLast("hotJupiters")) != null) HotJupiters = Flag(v, HotJupiters);
        if ((v = node.GetLast("workShare")) != null) WorkShare = Num(v, WorkShare);
        if ((v = node.GetLast("maxLagDays")) != null) MaxLagDays = Num(v, MaxLagDays);
        Boost = Math.Max(Math.Min(Boost, 1.0e9), 1.0e-3);
        WorkShare = Math.Max(Math.Min(WorkShare, 1.0), 0.05);
        MaxLagDays = Math.Max(Math.Min(MaxLagDays, 3650.0), 0.1);
    }

    public static CfgNode ToNode() => new CfgNode(UserNode)
        .Add("enabled", Enabled ? "True" : "False")
        .Add("brightnessBoost", Boost.ToString("0.######", CultureInfo.InvariantCulture))
        .Add("view", View.ToString())
        .Add("brownDwarfs", BrownDwarfs ? "True" : "False")
        .Add("gasGiants", GasGiants ? "True" : "False")
        .Add("hotJupiters", HotJupiters ? "True" : "False")
        .Add("workShare", WorkShare.ToString("0.###", CultureInfo.InvariantCulture))
        .Add("maxLagDays", MaxLagDays.ToString("0.###", CultureInfo.InvariantCulture));

    private static FluidView ParseView(string text, FluidView fallback)
    {
        text = text.Trim().Replace("-", "").Replace(" ", "");
        if (text.Equals("thermalinfrared", StringComparison.OrdinalIgnoreCase) || text.Equals("ir", StringComparison.OrdinalIgnoreCase)) return FluidView.ThermalInfrared;
        if (text.Equals("eye", StringComparison.OrdinalIgnoreCase)) return FluidView.Eye;
        return fallback;
    }

    private static bool Flag(string text, bool fallback)
    {
        text = text.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1") return true;
        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0") return false;
        return fallback;
    }

    private static double Num(string text, double fallback) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : fallback;
}
