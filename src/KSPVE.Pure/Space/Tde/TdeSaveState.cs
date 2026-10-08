using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// A save's KSPVE file (GameData/KSPVE/PluginData/Saves/&lt;save&gt;.cfg), one KSPVE_SAVE node:
/// <c>tde = on</c> starts a tidal disruption event; KSPVE then writes <c>tdeStart</c>, the game time it began, so the
/// event plays out the same way across sessions, reverts and time warp. <c>tde = off</c> (or deleting tdeStart)
/// resets it. <c>tdeBody</c> picks the black hole (default: the first configured). Any KSPVE_TDE key set here
/// overrides the GameData config for this save only.
/// </summary>
public sealed class TdeSaveState
{
    public const string NodeName = "KSPVE_SAVE";
    public const string SwitchKey = "tde";
    public const string StartKey = "tdeStart";
    public const string BodyKey = "tdeBody";

    public bool On;
    public double? StartUt;
    public string Body = string.Empty;
    /// <summary>The KSPVE_TDE keys this save overrides, as a KSPVE_TDE node.</summary>
    public CfgNode Overrides = new CfgNode(TdeSettings.Schema.NodeName);
    /// <summary>Everything else the file held, kept when KSPVE rewrites it.</summary>
    public List<KeyValuePair<string, string>> Other = new List<KeyValuePair<string, string>>();

    /// <summary>on/off, true/false, yes/no, 1/0 (case-insensitive); anything else is off.</summary>
    public static bool ParseSwitch(string raw)
    {
        string s = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        return s == "on" || s == "true" || s == "yes" || s == "1";
    }

    public static TdeSaveState Parse(CfgNode node)
    {
        var state = new TdeSaveState();
        if (node == null) return state;
        foreach (KeyValuePair<string, string> pair in node.Values)
        {
            string key = pair.Key?.Trim() ?? string.Empty;
            if (string.Equals(key, SwitchKey, StringComparison.OrdinalIgnoreCase)) state.On = ParseSwitch(pair.Value);
            else if (string.Equals(key, StartKey, StringComparison.OrdinalIgnoreCase))
                state.StartUt = double.TryParse(pair.Value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double ut)
                                && !double.IsNaN(ut) && !double.IsInfinity(ut) ? ut : (double?)null;
            else if (string.Equals(key, BodyKey, StringComparison.OrdinalIgnoreCase)) state.Body = pair.Value?.Trim() ?? string.Empty;
            else if (IsTdeKey(key)) state.Overrides.SetValue(key, pair.Value);
            else state.Other.Add(new KeyValuePair<string, string>(key, pair.Value));
        }
        return state;
    }

    private static bool IsTdeKey(string key)
    {
        foreach (SettingDef def in TdeSettings.Schema.Definitions)
            if (string.Equals(def.Key, key, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The file's text: an explanatory header, then the KSPVE_SAVE node.</summary>
    public string Write(string saveName)
    {
        var node = new CfgNode(NodeName).Add(SwitchKey, On ? "on" : "off");
        if (StartUt.HasValue) node.Add(StartKey, StartUt.Value.ToString("R", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(Body)) node.Add(BodyKey, Body);
        foreach (KeyValuePair<string, string> pair in Overrides.Values) node.Add(pair.Key, pair.Value);
        foreach (KeyValuePair<string, string> pair in Other) node.Add(pair.Key, pair.Value);

        var sb = new StringBuilder();
        sb.Append("// KSPVE settings for the save \"").Append(saveName).Append("\". KSPVE rereads this file while the game runs.\n");
        sb.Append("//   tde = on        a star falls into the black hole and is torn apart (a tidal disruption event)\n");
        sb.Append("//   tde = off       no event; switching off and on again starts a new one\n");
        sb.Append("//   tdeStart        written by KSPVE: the game time (s) the event began; delete it to restart\n");
        sb.Append("//   tdeBody         optional: which black hole (default: the first configured)\n");
        sb.Append("// Any KSPVE_TDE setting (GameData/KSPVE/Config/KSPVE_TDE.cfg) can be added here for this save only,\n");
        sb.Append("// for example: timeScale = 0.05 (twenty times slower than real), starMass = 1.2, beta = 2\n");
        sb.Append(CfgWriter.Write(new[] { node }));
        return sb.ToString();
    }
}
