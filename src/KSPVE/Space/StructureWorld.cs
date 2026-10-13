using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Particles;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>One structure on a body: its settings, and the particles worked out for the game's time.</summary>
internal sealed class StructureInstance
{
    private CelestialBody _body;

    public StructureConfig Config;
    /// <summary>The GameData node's settings, for "reset to config" (null for one the player added).</summary>
    public StructureConfig FromConfig;
    public bool AddedInGame;
    public bool Edited;
    public readonly ParticleSet Set = new ParticleSet();
    public StructureContext Context;
    /// <summary>Game time and real time of the last generation, and a counter the window bumps when a setting changes.</summary>
    public double GeneratedUt = double.NaN;
    public float GeneratedAt = -100f;
    public int Version, GeneratedVersion = -1;

    public string Title => Config.Body + " " + StructureSettings.KindName(Config.Kind) + (Config.Slot > 0 ? " #" + (Config.Slot + 1) : string.Empty);

    /// <summary>The body, found by internal name then display name once the planetary system exists (null at the main menu).</summary>
    public CelestialBody Body
    {
        get
        {
            if (_body != null) return _body;
            if (FlightGlobals.fetch == null || FlightGlobals.Bodies == null) return null;
            _body = FlightGlobals.Bodies.Find(b => b != null && b.bodyName == Config.Body)
                ?? FlightGlobals.Bodies.Find(b => b != null && !string.IsNullOrEmpty(b.bodyDisplayName) && b.bodyDisplayName.Replace("^N", string.Empty).Trim() == Config.Body);
            return _body;
        }
    }

    public void Changed()
    {
        Edited = true;
        Version++;
    }
}

/// <summary>
/// Loads KSPVE_STRUCTURE nodes (and the player's in user.cfg), builds each one's physical context from the game's bodies and
/// keeps its particles up to date (<see cref="Structures.Generate"/>), at most a few times a second.
/// </summary>
internal static class StructureWorld
{
    public const string NodeName = StructureSettings.NodeName;

    public static readonly List<StructureInstance> Instances = new List<StructureInstance>();
    /// <summary>The master switch (kept in user.cfg).</summary>
    public static bool Enabled = true;
    /// <summary>The player has changed which structures exist or the switch (so user.cfg must be written).</summary>
    public static bool ListEdited;

    /// <summary>Seconds of real time between new sets of particles: positions are worked out by Kepler, so a slow refresh does not show.</summary>
    private const float RefreshSeconds = 0.4f;

    public static void Load(List<ConfigIssue> issues)
    {
        Instances.Clear();
        Enabled = true;
        ListEdited = false;
        CfgNode user = null;
        try { user = KspConfig.LoadUserNodes().Find(n => n.Name == StructureSettings.UserNodeName); }
        catch (Exception ex) { Log.Exception("Reading structure tweaks from user.cfg failed; using the configs as they are", ex); }
        if (user != null && bool.TryParse(user.GetLast("enabled")?.Trim(), out bool enabled)) Enabled = enabled;

        var slots = new Dictionary<string, int>();
        var taken = new HashSet<string>();
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(NodeName))
        {
            CfgNode cfg = KspConfig.ToCfg(node);
            string body = cfg.GetLast("body")?.Trim();
            if (string.IsNullOrEmpty(body)) continue;
            int slot = slots.TryGetValue(body, out int n) ? n : 0;
            slots[body] = slot + 1;
            cfg.SetValue("slot", slot.ToString(CultureInfo.InvariantCulture));
            StructureConfig fromConfig = StructureSettings.Parse(cfg, issues);
            var instance = new StructureInstance { Config = fromConfig.Clone(), FromConfig = fromConfig };
            CfgNode tweak = UserChild(user, body, slot);
            if (tweak != null)
            {
                instance.Config = StructureSettings.Parse(tweak, issues);
                instance.Edited = true;
            }
            Instances.Add(instance);
            taken.Add(body + "#" + slot);
        }
        if (user != null)
        {
            foreach (CfgNode child in user.Children)
            {
                if (child.Name != NodeName) continue;
                StructureConfig config = StructureSettings.Parse(child, issues);
                if (string.IsNullOrEmpty(config.Body) || !taken.Add(config.Body + "#" + config.Slot)) continue;
                Instances.Add(new StructureInstance { Config = config, AddedInGame = true, Edited = true });
            }
        }
        if (Instances.Count > 0) Log.Info("Particle structures: " + string.Join(", ", Instances.ConvertAll(i => i.Title).ToArray()) + ".");
    }

    private static CfgNode UserChild(CfgNode user, string body, int slot)
    {
        if (user == null) return null;
        foreach (CfgNode child in user.Children)
        {
            if (child.Name != NodeName || child.GetLast("body")?.Trim() != body) continue;
            if (int.TryParse(child.GetLast("slot")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s) ? s == slot : slot == 0) return child;
        }
        return null;
    }

    /// <summary>What goes in PluginData/user.cfg: every structure the player added or changed, whole.</summary>
    public static CfgNode UserNode()
    {
        if (!ListEdited && !Instances.Exists(i => i.Edited)) return null;
        var container = new CfgNode(StructureSettings.UserNodeName).Add("enabled", Enabled ? "True" : "False");
        foreach (StructureInstance i in Instances)
            if (i.Edited) container.AddChild(StructureSettings.ToNode(i.Config));
        return container;
    }

    public static StructureInstance Add(string body, StructureKind kind)
    {
        int slot = 0;
        foreach (StructureInstance i in Instances)
            if (i.Config.Body == body) slot = Math.Max(slot, i.Config.Slot + 1);
        var config = new StructureConfig { Body = body, Kind = kind, Slot = slot };
        // Start from the kind's first preset: a blank structure of any kind draws something worth seeing.
        foreach (StructureSettings.Preset p in StructureSettings.Presets)
            if (p.Kind == kind) { StructureSettings.Apply(p, config); break; }
        var instance = new StructureInstance { Config = config, AddedInGame = true, Edited = true };
        Instances.Add(instance);
        ListEdited = true;
        return instance;
    }

    public static void Remove(StructureInstance instance)
    {
        if (!instance.AddedInGame) return;
        Instances.Remove(instance);
        ListEdited = true;
    }

    public static void ResetToConfig(StructureInstance instance)
    {
        if (instance.FromConfig == null) return;
        instance.Config = instance.FromConfig.Clone();
        instance.Edited = false;
        instance.Version++;
    }

    /// <summary>Back to the GameData configs (nothing the player changed).</summary>
    public static void ResetAll()
    {
        Instances.RemoveAll(i => i.AddedInGame);
        foreach (StructureInstance i in Instances) ResetToConfig(i);
        Enabled = true;
        ListEdited = false;
    }

    // ------------------------------------------------------------------------------------------------- the scene

    /// <summary>The star lighting a body: the named one, or the nearest star above it in the system, or the sun.</summary>
    public static CelestialBody StarFor(StructureConfig config, CelestialBody body)
    {
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        if (!string.IsNullOrEmpty(config.LightSource))
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] != null && bodies[i].bodyName == config.LightSource) return bodies[i];
        CelestialBody parent = body.referenceBody;
        for (int guard = 0; parent != null && parent != body && guard < 64; guard++)
        {
            if (BlackHoles.IsStar(parent) || parent.referenceBody == parent) return parent;
            parent = parent.referenceBody;
        }
        return Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
    }

    /// <summary>
    /// What the generators need at game time <paramref name="ut"/>: the body, its star, and their relative place and velocity
    /// (world frame; the velocity from the positions a second either side).
    /// </summary>
    public static StructureContext ContextFor(StructureInstance instance, double ut)
    {
        CelestialBody body = instance.Body;
        StructureConfig config = instance.Config;
        var context = new StructureContext { Ut = ut, BodyMu = body.gravParameter, BodyRadius = body.Radius };
        CelestialBody star = StarFor(config, body);
        if (star != null && star != body)
        {
            const double dt = 1.0;
            Vector3d p0 = body.getTruePositionAtUT(ut) - star.getTruePositionAtUT(ut);
            Vector3d pa = body.getTruePositionAtUT(ut - dt) - star.getTruePositionAtUT(ut - dt);
            Vector3d pb = body.getTruePositionAtUT(ut + dt) - star.getTruePositionAtUT(ut + dt);
            Vector3d v = (pb - pa) / (2.0 * dt);
            context.HasStar = true;
            context.StarMu = star.gravParameter;
            context.StarRadius = star.Radius;
            context.StarTemperature = config.N("starTemperature");
            context.BodyFromStar = new Double3(p0.x, p0.y, p0.z);
            context.BodyVelocity = new Double3(v.x, v.y, v.z);
        }
        else
        {
            // No star to follow: lit from +x, as the preview does.
            context.HasStar = false;
        }
        var axis = new Vector3d(config.N("normalX"), config.N("normalY"), config.N("normalZ"));
        if (axis.sqrMagnitude < 1e-12) axis = Vector3d.up;
        Vector3d world = Planetarium.Rotation * axis.normalized;
        context.Axis = new Double3(world.x, world.y, world.z);
        return context;
    }

    /// <summary>Brings every structure's particles up to the game's time, when it has moved or a setting changed.</summary>
    public static void Update()
    {
        if (!Enabled || Instances.Count == 0 || FlightGlobals.fetch == null || Planetarium.fetch == null) return;
        double ut = Planetarium.GetUniversalTime();
        float now = Time.realtimeSinceStartup;
        foreach (StructureInstance i in Instances)
        {
            if (!i.Config.Enabled) continue;
            CelestialBody body = i.Body;
            if (body == null) continue;
            bool changed = i.GeneratedVersion != i.Version;
            if (!changed && (now - i.GeneratedAt < RefreshSeconds || ut == i.GeneratedUt)) continue;
            try
            {
                i.Context = ContextFor(i, ut);
                Structures.Generate(i.Config, i.Context, i.Set);
                i.GeneratedUt = ut;
                i.GeneratedAt = now;
                i.GeneratedVersion = i.Version;
            }
            catch (Exception ex)
            {
                Log.Exception("Making the particles of " + i.Title + " failed; it is switched off", ex);
                i.Config.Enabled = false;
            }
        }
    }
}
