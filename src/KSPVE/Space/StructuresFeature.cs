using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Particles;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>
/// Structures made of particles on real orbits: evaporating and chthonian planets with their tails, giant-impact debris,
/// synestias, super-puff planets, moons torn into rings, rubble piles breaking apart, and a speculative toroidal planet. Each
/// is a KSPVE_STRUCTURE node on a body (<see cref="StructureSettings"/>); this feature loads them, keeps their particles up
/// to the game's time (<see cref="StructureWorld"/>), attaches the renderer and draws their section of the settings window.
/// </summary>
internal sealed class StructuresFeature : Feature
{
    private const string ScaledCameraName = "Camera ScaledSpace";
    private static Camera[] _cameras = new Camera[32];

    private int _open = -1;
    private string _newBody = string.Empty;
    private StructureKind _newKind = StructureKind.EvaporatingPlanet;
    private string _status = string.Empty;

    public override string Name => "Structures";
    public override int Order => 55;
    public override string SettingsTitle => "Particle structures: evaporating planets, impact debris, synestias, super-puffs, ringed moons, rubble piles";

    public override void Load(List<ConfigIssue> issues) => StructureWorld.Load(issues);

    public override void Update(bool allowed)
    {
        StructureWorld.Update();
        bool enabled = allowed && StructureWorld.Enabled && StructureWorld.Instances.Count > 0;
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || cam.name != ScaledCameraName) continue;
            var renderer = cam.GetComponent<StructureRenderer>();
            if (renderer == null)
            {
                if (!enabled) return;
                renderer = cam.gameObject.AddComponent<StructureRenderer>();
                Log.Info("Structure renderer attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
            return;
        }
    }

    public override CfgNode UserNode() => StructureWorld.UserNode();

    public override void ResetToDefaults() => StructureWorld.ResetAll();

    // -------------------------------------------------------------------------------------------------- settings window

    public override void DrawSettings()
    {
        StructureWorld.Enabled = Gui.Toggle($"Particle structures ({StructureWorld.Instances.Count} configured)", StructureWorld.Enabled);
        if (!StructureWorld.Enabled) return;
        Gui.Note("Clumps, puffs and grains on real orbits about their body (and its star): their places follow from the game's time, so the shapes are exact. Brightness is physical unless a setting says it is a display aid.");

        for (int i = 0; i < StructureWorld.Instances.Count; i++)
        {
            if (DrawInstance(i)) break;
        }

        Gui.Header("Add a structure");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Body:", GUILayout.Width(50));
        if (string.IsNullOrEmpty(_newBody) && FlightGlobals.fetch != null)
            _newBody = (FlightGlobals.currentMainBody ?? FlightGlobals.GetHomeBody())?.bodyName ?? string.Empty;
        _newBody = GUILayout.TextField(_newBody, GUILayout.Width(180));
        GUILayout.EndHorizontal();
        _newKind = (StructureKind)Gui.Choice("What", (int)_newKind, StructureSettings.KindNames, 2);
        if (GUILayout.Button("Add") && !string.IsNullOrEmpty(_newBody.Trim()))
        {
            StructureInstance added = StructureWorld.Add(_newBody.Trim(), _newKind);
            _open = StructureWorld.Instances.IndexOf(added);
            _status = $"{added.Title} added (Save to keep)";
        }
        if (!string.IsNullOrEmpty(_status)) Gui.Note(_status);
    }

    /// <summary>One structure's row and, when open, its controls. True when the list changed.</summary>
    private bool DrawInstance(int index)
    {
        StructureInstance s = StructureWorld.Instances[index];
        StructureConfig c = s.Config;
        bool open = _open == index;
        string title = $"  {s.Title}{(c.Enabled ? string.Empty : " (off)")}{(s.Edited ? " (edited)" : string.Empty)}";
        if (Gui.Toggle(title, open) != open)
        {
            _open = open ? -1 : index;
            open = !open;
        }
        if (!open) return false;

        GUI.changed = false;
        bool enabled = Gui.Toggle("    Drawn", c.Enabled);
        if (enabled != c.Enabled) { c.Enabled = enabled; s.Changed(); }
        GUILayout.Label("    What it is (switching keeps the settings they share):");
        int kind = GUILayout.SelectionGrid((int)c.Kind, StructureSettings.KindNames, 2);
        if (kind != (int)c.Kind)
        {
            c.Kind = (StructureKind)kind;
            s.Changed();
        }
        if (c.Kind == StructureKind.ToroidalPlanet)
            Gui.Note("SPECULATIVE: no such planet is known, and a torus is unstable (Liu 2012). It is drawn only with the switch below on.");

        // Presets.
        GUILayout.Label("    Presets:");
        foreach (StructureSettings.Preset preset in StructureSettings.Presets)
        {
            if (preset.Kind != c.Kind) continue;
            if (GUILayout.Button(new GUIContent("    " + preset.Name, preset.Description)))
            {
                StructureSettings.Apply(preset, c);
                s.Changed();
                _status = $"{s.Title}: {preset.Name} (Save to keep)";
            }
        }

        string section = null;
        foreach (StructureParam p in StructureSettings.Params)
        {
            if (!p.UsedBy(c.Kind)) continue;
            if (p.Section != section)
            {
                section = p.Section;
                Gui.Header(section);
            }
            double value = c.N(p.Key);
            double next;
            if (p.Toggle) next = Gui.Toggle("    " + p.Label, value > 0.5) ? 1.0 : 0.0;
            else
            {
                double lo = p.Min, hi = p.Max;
                if (p.Log) { lo = Math.Max(p.Min, p.Default / 100.0); hi = Math.Min(p.Max, Math.Max(p.Default, 1e-12) * 100.0); }
                if (hi <= lo) { lo = p.Min; hi = p.Max; }
                next = Gui.Slider("    " + p.Label, value, lo, hi, p.Max >= 1000.0 || p.Min < 0.01 ? "0.####" : "0.##");
            }
            if (next != value) { c.Set(p.Key, Math.Min(Math.Max(next, p.Min), p.Max)); s.Changed(); }
        }
        foreach (StructureColor col in StructureSettings.Colors)
        {
            if (!col.UsedBy(c.Kind)) continue;
            Double3 v = c.C(col.Key);
            Double3 n = ColorSliders("    " + col.Label, v);
            if (n != v) { c.Set(col.Key, n); s.Changed(); }
        }
        string light = TextRow("    Star lighting it (empty = the nearest star above it)", c.LightSource);
        if (light != c.LightSource) { c.LightSource = light; s.Changed(); }

        DrawReadout(s);

        GUILayout.BeginHorizontal();
        if (s.FromConfig != null && GUILayout.Button($"Reset {s.Title} to its config"))
        {
            StructureWorld.ResetToConfig(s);
            _status = $"{s.Title} reset to its config (Save to keep)";
        }
        bool removed = false;
        if (s.AddedInGame && GUILayout.Button($"Remove {s.Title}"))
        {
            StructureWorld.Remove(s);
            _open = -1;
            _status = $"{s.Title} removed (Save to keep)";
            removed = true;
        }
        GUILayout.EndHorizontal();
        return removed;
    }

    private static Double3 ColorSliders(string label, Double3 c)
    {
        GUILayout.Label(label);
        double r = Gui.Slider("      red", c.X, 0, 2), g = Gui.Slider("      green", c.Y, 0, 2), b = Gui.Slider("      blue", c.Z, 0, 2);
        return new Double3(Math.Max(r, 0.0), Math.Max(g, 0.0), Math.Max(b, 0.0));
    }

    private static string TextRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(300));
        string text = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(160));
        GUILayout.EndHorizontal();
        return text;
    }

    /// <summary>What the physics says about it now (the numbers behind the picture).</summary>
    private static void DrawReadout(StructureInstance s)
    {
        if (s.Context == null || s.Body == null) return;
        StructureConfig c = s.Config;
        StructureContext x = s.Context;
        Gui.Header("What the physics says");
        try
        {
            switch (c.Kind)
            {
                case StructureKind.EvaporatingPlanet:
                {
                    EvaporationState st = Structures.EvaporationNow(c, x);
                    GUILayout.Label($"    Atmosphere left {st.AtmosphereLeft * 100.0:0.#}%; mass {st.Mass:0.###e+0} kg, radius {st.Radius / 1000.0:0} km, {st.Distance / 1e9:0.###} million km from its star");
                    GUILayout.Label($"    Star's XUV flux at the planet {st.XuvFlux:0.###e+0} W/m^2; Roche lobe {st.LobeRadius / 1000.0:0} km, planet fills {st.FillFactor * 100.0:0}% of it (Hill radius, where L1 and L2 are: {st.HillRadius / 1000.0:0} km)");
                    GUILayout.Label($"    Loss: energy-limited {st.EnergyLimited:0.###e+0} kg/s (Erkaev K {st.ErkaevK:0.##}); Roche overflow {st.RocheOverflow:0.###e+0} kg/s; dust sublimation {st.DustSublimation:0.###e+0} kg/s at {st.SubstellarTemperature:0} K; in use {st.Total:0.###e+0} kg/s");
                    break;
                }
                case StructureKind.SuperPuff:
                {
                    double rc = x.BodyRadius, rv = c.N("puffRadius") * rc;
                    GUILayout.Label($"    Mean density {Structures.MeanDensity(x.BodyMass, rv):0.#} kg/m^3 over its visible radius ({(Structures.MeanDensity(x.BodyMass, rv) < 100.0 ? "a super-puff" : "denser than a super-puff")}); lambda = {Structures.EnvelopeLambda(x.BodyMass, rc, c.N("atmTemperature"), c.N("envelopeMu")):0.#}");
                    break;
                }
                case StructureKind.RingingMoon:
                {
                    double rho = x.BodyMass / (4.0 / 3.0 * Math.PI * x.BodyRadius * x.BodyRadius * x.BodyRadius);
                    double limit = Structures.RocheLimit(x.BodyRadius, rho, c.N("moonDensity"));
                    GUILayout.Label($"    Fluid Roche limit {limit / 1000.0:0} km from the centre; the moon orbits at {c.N("moonDistanceRoche") * limit / 1000.0:0} km ({(c.N("moonDistanceRoche") < 1.0 ? "inside it: it comes apart" : "outside it: it holds together")})");
                    break;
                }
                case StructureKind.Synestia:
                {
                    double rcor = c.N("corotation") * x.BodyRadius;
                    GUILayout.Label($"    Corotation limit {rcor / 1000.0:0} km: the inner structure turns once in {2.0 * Math.PI / Math.Sqrt(x.BodyMu / (rcor * rcor * rcor)) / 3600.0:0.#} hours");
                    break;
                }
                case StructureKind.RubblePile:
                    GUILayout.Label($"    Break-up spin: one turn in {2.0 * Math.PI / Math.Sqrt(x.BodyMu / (x.BodyRadius * x.BodyRadius * x.BodyRadius)) / 3600.0:0.##} hours at the equator");
                    break;
                case StructureKind.ImpactDebris:
                    GUILayout.Label($"    Escape speed {Math.Sqrt(2.0 * x.BodyMu / x.BodyRadius) / 1000.0:0.##} km/s; {s.Set.Count} blobs still flying (the rest have fallen back)");
                    break;
            }
        }
        catch (Exception ex)
        {
            GUILayout.Label("    (" + ex.Message + ")");
        }
    }
}
