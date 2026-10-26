using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using KSPVE.Pure.Stars;
using UnityEngine;

namespace KSPVE.Stars;

/// <summary>One star in the game: its body, profile, activity model and wind.</summary>
internal sealed class StarEntry
{
    public CelestialBody Body;
    public StarProfile Profile;
    public StarActivity Activity;
    public StarWind Wind;
}

/// <summary>
/// The player's display choices for stellar prominences and loops.
/// </summary>
internal static class StarSettings
{
    public static bool Enabled = true;
    public static StarView View = StarView.HAlpha;
    /// <summary>
    /// Multiplies the brightness of prominences and loops in the chosen view. 1 is the view's own strength. Display.
    /// </summary>
    public static double Visibility = 1.0;
    /// <summary>
    /// Scales the height and breadth of prominences and loops (display). Real active-region loops reach a few hundredths of the star's
    /// radius and big prominences a tenth to a third; 1 is real. The default 2.5 shows them near the size in the reference picture.
    /// </summary>
    public static double LoopSize = 2.5;
    public static bool Prominences = true;

    public const string UserNode = "KSPVE_STARS_USER";

    public static void Reset()
    {
        Enabled = true;
        View = StarView.HAlpha;
        Visibility = 1.0;
        LoopSize = 2.5;
        Prominences = true;
    }

    /// <summary>Global keys of a KSPVE_STARS or KSPVE_STARS_USER node.</summary>
    public static void Read(CfgNode node)
    {
        string v;
        if ((v = node.GetLast("enabled")) != null) Enabled = Flag(v, Enabled);
        if ((v = node.GetLast("view")) != null) View = ParseView(v, View);
        if ((v = node.GetLast("visibility")) != null) Visibility = Num(v, Visibility);
        if ((v = node.GetLast("loopSize")) != null) LoopSize = Num(v, LoopSize);
        if ((v = node.GetLast("prominences")) != null) Prominences = Flag(v, Prominences);
        Visibility = Math.Max(Visibility, 0.0);
        LoopSize = Math.Max(LoopSize, 0.0);
    }

    public static CfgNode ToNode() => new CfgNode(UserNode)
        .Add("enabled", Enabled ? "True" : "False")
        .Add("view", View.ToString())
        .Add("visibility", Visibility.ToString("0.###", CultureInfo.InvariantCulture))
        .Add("loopSize", LoopSize.ToString("0.###", CultureInfo.InvariantCulture))
        .Add("prominences", Prominences ? "True" : "False");

    private static StarView ParseView(string text, StarView fallback)
    {
        text = text.Trim().Replace("-", "").Replace(" ", "");
        foreach (StarView v in Enum.GetValues(typeof(StarView)))
            if (string.Equals(v.ToString(), text, StringComparison.OrdinalIgnoreCase)) return v;
        if (text.Equals("halpha", StringComparison.OrdinalIgnoreCase)) return StarView.HAlpha;
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

/// <summary>
/// The stars of the loaded game and what they are doing: reads KSPVE_STARS, makes an activity model per star on first use,
/// keeps the solar wind source up to date, and gives the model's frame in the game's world.
/// </summary>
internal static class StarWorld
{
    public const string NodeName = "KSPVE_STARS";

    private static readonly Dictionary<string, CfgNode> Nodes = new Dictionary<string, CfgNode>(StringComparer.Ordinal);
    private static readonly Dictionary<string, StarEntry> Entries = new Dictionary<string, StarEntry>(StringComparer.Ordinal);
    private static StarProfile _default = new StarProfile();
    private static double _reference = 1.495978707e11;

    public static readonly StarWindSource Source = new StarWindSource();

    public static double ReferenceDistance => _reference;

    public static void Load(List<ConfigIssue> issues)
    {
        Nodes.Clear();
        Entries.Clear();
        Source.Clear();
        _default = new StarProfile();
        StarSettings.Reset();
        try
        {
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(NodeName))
            {
                CfgNode cfg = KspConfig.ToCfg(node);
                StarSettings.Read(cfg);
                foreach (CfgNode child in cfg.Children)
                {
                    if (child.Name == "DEFAULT") _default = StarProfile.From(child, _default);
                    else if (child.Name == "STAR")
                    {
                        string body = child.GetLast("body");
                        if (string.IsNullOrWhiteSpace(body)) { issues?.Add(new ConfigIssue(IssueSeverity.Warning, NodeName + "/STAR", "a STAR needs a body")); continue; }
                        body = body.Trim();
                        Nodes[body] = Nodes.TryGetValue(body, out CfgNode old) ? CfgNode.Overlay(old, child) : child;
                    }
                }
            }
            foreach (CfgNode user in KspConfig.LoadUserNodes())
                if (user.Name == StarSettings.UserNode) StarSettings.Read(user);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading " + NodeName + " failed", ex);
        }
        SpaceWeather.Source = Source;
    }

    /// <summary>A star: KSP's flag, or a scaled body drawn with the star shader (what Kopernicus makes).</summary>
    public static bool IsStar(CelestialBody b) =>
        b != null && (b.isStar || (b.scaledBody != null && b.scaledBody.GetComponentInChildren<SunShaderController>() != null));

    /// <summary>The nearest star above a body in the system (the body itself when it is one).</summary>
    public static CelestialBody StarOf(CelestialBody body)
    {
        CelestialBody b = body;
        for (int guard = 0; b != null && guard < 64; guard++)
        {
            if (IsStar(b)) return b;
            if (b.referenceBody == null || b.referenceBody == b) break;
            b = b.referenceBody;
        }
        return Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
    }

    /// <summary>
    /// The star's entry (created on first use); null for something that is not a star, or a star that other effects own
    /// (a body drawn as a black hole, neutron star or other object is not a star to draw).
    /// </summary>
    public static StarEntry Entry(CelestialBody star)
    {
        if (star == null || !IsStar(star)) return null;
        if (Entries.TryGetValue(star.bodyName, out StarEntry entry))
        {
            entry.Body = star;
            return entry;
        }
        StarProfile p = _default.Clone();
        p.Body = star.bodyName;
        CfgNode node;
        bool explicitSeed = false, explicitPhase = false;
        if (Nodes.TryGetValue(star.bodyName, out node))
        {
            p = StarProfile.From(node, p);
            explicitSeed = node.GetLast("seed") != null;
            explicitPhase = node.GetLast("cyclePhase") != null;
        }
        // The surface turns at the body's own rotation period unless the config says otherwise (its sunspots drift with it).
        if ((node == null || node.GetLast("rotationPeriodDays") == null) && star.rotates && Math.Abs(star.rotationPeriod) > 60.0)
        {
            p.EquatorialRotationPeriod = Math.Abs(star.rotationPeriod);
        }
        // A star the config does not describe still gets a character of its own, fixed by its name.
        if (!explicitSeed) p.Seed = StableHash(star.bodyName);
        if (!explicitPhase && node == null) p.CyclePhase = (StableHash(star.bodyName + "#phase") % 1000UL) / 1000.0;
        var created = new StarEntry { Body = star, Profile = p, Activity = new StarActivity(p) };
        created.Wind = new StarWind(created.Activity, _reference);
        Entries[star.bodyName] = created;
        Source.Set(star.bodyName, created.Wind);
        Log.Info($"Star {star.bodyName}: {p.Temperature:0} K, activity {p.Activity:0.##}, cycle {p.CyclePeriod / StarProfile.Year:0.#} years, phase {p.CyclePhase:0.00}, seed {p.Seed}.");
        return created;
    }

    /// <summary>The profile a star would get from the configs (no game body needed), for the catalogue.</summary>
    public static StarProfile Configured(string bodyName)
    {
        StarProfile p = _default.Clone();
        p.Body = bodyName;
        return Nodes.TryGetValue(bodyName, out CfgNode node) ? StarProfile.From(node, p) : p;
    }

    public static IEnumerable<StarEntry> All => Entries.Values;

    /// <summary>The wind reference distance: the home planet's orbit (KSP calibrates sunlight the same way).</summary>
    public static void SetReference(double distance)
    {
        if (!(distance > 1e6) || Math.Abs(distance - _reference) < 1.0) return;
        _reference = distance;
        SpaceWeather.ReferenceDistance = distance;
        foreach (StarEntry e in Entries.Values)
        {
            e.Wind = new StarWind(e.Activity, _reference);
            Source.Set(e.Body.bodyName, e.Wind);
        }
    }

    /// <summary>Updates the reference distance from the game's home planet when it is known.</summary>
    public static void UpdateReference()
    {
        if (FlightGlobals.fetch == null) return;
        CelestialBody home = FlightGlobals.GetHomeBody();
        if (home == null || home.orbit == null) return;
        // The home planet's own orbit round its star (a moon's home would be the planet's).
        CelestialBody b = home;
        for (int guard = 0; b != null && b.referenceBody != null && b.referenceBody != b && guard < 64; guard++)
        {
            if (IsStar(b.referenceBody)) { SetReference(b.orbit.semiMajorAxis); return; }
            b = b.referenceBody;
        }
    }

    // ---------------------------------------------------------------- the model frame

    /// <summary>
    /// The model frame of a star in the game's world: y the star's spin axis (north), x a fixed direction across it, z so that
    /// longitude increases the way the star turns. Fixed in inertial space (the activity model's regions carry their own
    /// rotation); expressed in the current world frame through Planetarium.Rotation.
    /// </summary>
    public static void Basis(CelestialBody star, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        Vector3d axis = star.RotationAxis;
        if (axis.sqrMagnitude < 1e-12) axis = Vector3d.up;
        Vector3d yc = axis.normalized;
        Vector3d reference = Math.Abs(Vector3d.Dot(yc, Vector3d.forward)) > 0.95 ? Vector3d.right : Vector3d.forward;
        Vector3d xc = (reference - yc * Vector3d.Dot(reference, yc)).normalized;
        Vector3d zc = Vector3d.Cross(yc, xc).normalized;
        if (star.rotationPeriod < 0.0) zc = -zc;   // a retrograde star turns the other way round its axis
        QuaternionD rotation = Planetarium.Rotation;
        x = (Vector3)(rotation * xc);
        y = (Vector3)(rotation * yc);
        z = (Vector3)(rotation * zc);
    }

    /// <summary>A world-space offset from the star as model-frame coordinates (m), the frame SpaceWeather.At expects.</summary>
    public static Double3 ToModel(CelestialBody star, Vector3d worldOffset)
    {
        Basis(star, out Vector3 x, out Vector3 y, out Vector3 z);
        return new Double3(
            worldOffset.x * x.x + worldOffset.y * x.y + worldOffset.z * x.z,
            worldOffset.x * y.x + worldOffset.y * y.y + worldOffset.z * y.z,
            worldOffset.x * z.x + worldOffset.y * z.y + worldOffset.z * z.z);
    }

    /// <summary>The solar wind at a body now (the aurora feature's input), and the star it comes from.</summary>
    public static SolarWindState WindAt(CelestialBody body, double ut, out CelestialBody star)
    {
        star = StarOf(body);
        if (star == null || body == null) return SolarWindState.Quiet(SpaceWeather.ReferenceDistance, SpaceWeather.ReferenceDistance);
        Double3 p = ToModel(star, body.position - star.position);
        if (p.Length < 1.0) p = new Double3(SpaceWeather.ReferenceDistance, 0.0, 0.0);
        return SpaceWeather.At(star.bodyName, p, ut);
    }

    private static ulong StableHash(string s)
    {
        ulong h = 1469598103934665603UL;
        unchecked
        {
            foreach (char c in s) h = (h ^ c) * 1099511628211UL;
        }
        return h % 1000000007UL + 1UL;
    }
}
