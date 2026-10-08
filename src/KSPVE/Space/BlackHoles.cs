using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Post;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using KSP.Localization;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>A black hole configured by a KSPVE_BLACKHOLE node.</summary>
internal sealed class BlackHoleDefinition
{
    private CelestialBody _body;

    public string BodyName;
    public bool FindByMass;
    public double MinMass;
    public double HorizonRadiusMeters;
    public double DiskInner, DiskOuter, DiskThickness;
    public double PeakTemperature, Brightness, RotationSpeed, Turbulence, Spin;
    public double ColorSaturation, Glow, PhotonRing, Particles, Debris;
    public double Jets, JetLength, JetWidth, JetSpeed, JetSpiral, JetTurns, JetFlow, JetAbsorb;
    public Vector3d Normal;
    public int Steps;
    public double BoundRadius;
    public bool DiskDrag;
    public double DiskDragTimescale, DiskDragMaxAccel, DiskBuffeting, DiskLight;
    public bool Pulsar;
    public double BeamPeriod, BeamTilt;
    /// <summary>No disk or jets until a tidal disruption builds them (TidalDisruptions).</summary>
    public bool DiskFromTde;

    /// <summary>The GameData config node, without the player's tweaks (for "reset to config").</summary>
    public CfgNode ConfigNode;
    /// <summary>The player has tweaked this hole in the settings window (or did in an earlier session).</summary>
    public bool Edited;

    public static BlackHoleDefinition From(SettingsValues v) => new BlackHoleDefinition
    {
        BodyName = v.Get(BlackHoleSettings.Body),
        FindByMass = v.Get(BlackHoleSettings.FindByMass),
        MinMass = v.Get(BlackHoleSettings.MinMass),
        HorizonRadiusMeters = v.Get(BlackHoleSettings.HorizonRadius),
        DiskInner = v.Get(BlackHoleSettings.DiskInner),
        DiskOuter = v.Get(BlackHoleSettings.DiskOuter),
        DiskThickness = v.Get(BlackHoleSettings.DiskThickness),
        PeakTemperature = v.Get(BlackHoleSettings.PeakTemperature),
        Brightness = v.Get(BlackHoleSettings.Brightness),
        RotationSpeed = v.Get(BlackHoleSettings.RotationSpeed),
        Turbulence = v.Get(BlackHoleSettings.Turbulence),
        Spin = v.Get(BlackHoleSettings.Spin),
        ColorSaturation = v.Get(BlackHoleSettings.ColorSaturation),
        Glow = v.Get(BlackHoleSettings.Glow),
        PhotonRing = v.Get(BlackHoleSettings.PhotonRing),
        Normal = new Vector3d(v.Get(BlackHoleSettings.NormalX), v.Get(BlackHoleSettings.NormalY), v.Get(BlackHoleSettings.NormalZ)),
        Steps = v.Get(BlackHoleSettings.Steps),
        BoundRadius = v.Get(BlackHoleSettings.BoundRadius),
        Particles = v.Get(BlackHoleSettings.Particles),
        Jets = v.Get(BlackHoleSettings.Jets),
        JetLength = v.Get(BlackHoleSettings.JetLength),
        JetWidth = v.Get(BlackHoleSettings.JetWidth),
        JetSpeed = v.Get(BlackHoleSettings.JetSpeed),
        JetSpiral = v.Get(BlackHoleSettings.JetSpiral),
        JetTurns = v.Get(BlackHoleSettings.JetTurns),
        JetFlow = v.Get(BlackHoleSettings.JetFlow),
        JetAbsorb = v.Get(BlackHoleSettings.JetAbsorb),
        Debris = v.Get(BlackHoleSettings.Debris),
        DiskDrag = v.Get(BlackHoleSettings.DiskDrag),
        DiskDragTimescale = v.Get(BlackHoleSettings.DiskDragTimescale),
        DiskDragMaxAccel = v.Get(BlackHoleSettings.DiskDragMaxAccel),
        DiskBuffeting = v.Get(BlackHoleSettings.DiskBuffeting),
        DiskLight = v.Get(BlackHoleSettings.DiskLight),
        Pulsar = v.Get(BlackHoleSettings.Pulsar),
        BeamPeriod = v.Get(BlackHoleSettings.BeamPeriod),
        BeamTilt = v.Get(BlackHoleSettings.BeamTilt),
        DiskFromTde = v.Get(BlackHoleSettings.DiskFromTde)
    };

    /// <summary>The values the settings window can change, as a KSPVE_BLACKHOLE node for PluginData/user.cfg.</summary>
    public CfgNode ToUserCfg() => new CfgNode(BlackHoleSettings.Schema.NodeName)
        .Add(BlackHoleSettings.Body.Key, BodyName)
        .Add(BlackHoleSettings.PeakTemperature.Key, Num(PeakTemperature))
        .Add(BlackHoleSettings.ColorSaturation.Key, Num(ColorSaturation))
        .Add(BlackHoleSettings.Brightness.Key, Num(Brightness))
        .Add(BlackHoleSettings.Glow.Key, Num(Glow))
        .Add(BlackHoleSettings.PhotonRing.Key, Num(PhotonRing))
        .Add(BlackHoleSettings.RotationSpeed.Key, Num(RotationSpeed))
        .Add(BlackHoleSettings.Spin.Key, Num(Spin))
        .Add(BlackHoleSettings.Turbulence.Key, Num(Turbulence))
        .Add(BlackHoleSettings.DiskThickness.Key, Num(DiskThickness))
        .Add(BlackHoleSettings.DiskOuter.Key, Num(DiskOuter))
        .Add(BlackHoleSettings.Particles.Key, Num(Particles))
        .Add(BlackHoleSettings.Debris.Key, Num(Debris))
        .Add(BlackHoleSettings.Jets.Key, Num(Jets))
        .Add(BlackHoleSettings.JetLength.Key, Num(JetLength))
        .Add(BlackHoleSettings.JetWidth.Key, Num(JetWidth))
        .Add(BlackHoleSettings.JetSpeed.Key, Num(JetSpeed))
        .Add(BlackHoleSettings.JetSpiral.Key, Num(JetSpiral))
        .Add(BlackHoleSettings.JetTurns.Key, Num(JetTurns))
        .Add(BlackHoleSettings.JetFlow.Key, Num(JetFlow))
        .Add(BlackHoleSettings.JetAbsorb.Key, Num(JetAbsorb))
        .Add(BlackHoleSettings.DiskLight.Key, Num(DiskLight))
        .Add(BlackHoleSettings.DiskDrag.Key, DiskDrag ? "True" : "False")
        .Add(BlackHoleSettings.DiskDragMaxAccel.Key, Num(DiskDragMaxAccel))
        .Add(BlackHoleSettings.DiskDragTimescale.Key, Num(DiskDragTimescale))
        .Add(BlackHoleSettings.DiskBuffeting.Key, Num(DiskBuffeting))
        .Add(BlackHoleSettings.BeamPeriod.Key, Num(BeamPeriod))
        .Add(BlackHoleSettings.BeamTilt.Key, Num(BeamTilt))
        .Add(BlackHoleSettings.Steps.Key, Steps.ToString(CultureInfo.InvariantCulture));

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// The body, looked up once the planetary system exists (null at the main menu). With findByMass it is the most
    /// massive body of at least minMass, whatever planet packs call it (Kcalbeloh with DistanceFactor 0 or Principia
    /// becomes the root "Sun", and under Real Solar System or Sol the home star is "Sun" too). Otherwise, or when no
    /// body is that heavy, it is found by internal name, then by display name.
    /// </summary>
    public CelestialBody Body
    {
        get
        {
            if (_body != null) return _body;
            if (FlightGlobals.fetch == null) return null;
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (FindByMass)
            {
                var masses = new List<double>(bodies.Count);
                foreach (CelestialBody b in bodies) masses.Add(b != null ? b.Mass : 0.0);
                int heaviest = BlackHoleSettings.HeaviestAbove(masses, MinMass);
                if (heaviest >= 0)
                {
                    _body = bodies[heaviest];
                    Log.Info($"Black hole {BodyName} is body '{_body.bodyName}' ({_body.Mass:0.###e+0} kg, the most massive body).");
                    return _body;
                }
            }
            _body = bodies.Find(b => b != null && b.bodyName == BodyName);
            if (_body == null)
            {
                _body = bodies.Find(b => b != null && DisplayName(b) == BodyName);
                if (_body != null) Log.Info($"Black hole {BodyName} found as body '{_body.bodyName}' (by its display name).");
            }
            return _body;
        }
    }

    private static string DisplayName(CelestialBody body) =>
        string.IsNullOrEmpty(body.bodyDisplayName) ? string.Empty : Localizer.Format(body.bodyDisplayName).LocalizeRemoveGender().Trim();
}

/// <summary>Loads KSPVE_BLACKHOLE nodes and keeps a <see cref="BlackHoleRenderer"/> on the ScaledSpace camera.</summary>
internal static class BlackHoles
{
    private const string ScaledCameraName = "Camera ScaledSpace";
    private static Camera[] _cameras = new Camera[32];

    public static readonly List<BlackHoleDefinition> Definitions = new List<BlackHoleDefinition>();

    public static void Load(List<ConfigIssue> issues)
    {
        Definitions.Clear();
        CfgNode user = null;
        try
        {
            user = KspConfig.LoadUserNodes().Find(n => n.Name == BlackHoleSettings.UserNodeName);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading black hole tweaks from user.cfg failed; using the configs as they are", ex);
        }

        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(BlackHoleSettings.Schema.NodeName))
        {
            CfgNode config = KspConfig.ToCfg(node);
            CfgNode merged = BlackHoleSettings.ApplyUserOverrides(config, user);
            BlackHoleDefinition definition = BlackHoleDefinition.From(BlackHoleSettings.Schema.Resolve(merged, issues));
            if (string.IsNullOrEmpty(definition.BodyName)) continue;
            definition.ConfigNode = config;
            definition.Edited = !ReferenceEquals(merged, config);
            Definitions.Add(definition);
        }
        if (Definitions.Count > 0)
            Log.Info($"Black holes configured: {string.Join(", ", Definitions.ConvertAll(d => d.BodyName).ToArray())}.");
    }

    /// <summary>The player's tweaks for user.cfg: one child per edited hole (an empty node clears old tweaks).</summary>
    public static CfgNode UserNode()
    {
        var container = new CfgNode(BlackHoleSettings.UserNodeName);
        foreach (BlackHoleDefinition hole in Definitions)
            if (hole.Edited) container.AddChild(hole.ToUserCfg());
        return container;
    }

    /// <summary>Throws away the player's tweaks to one hole, back to its GameData config (not saved until Save).</summary>
    public static void ResetToConfig(int index)
    {
        BlackHoleDefinition hole = Definitions[index];
        if (hole.ConfigNode == null) return;
        BlackHoleDefinition reset = BlackHoleDefinition.From(BlackHoleSettings.Schema.Resolve(hole.ConfigNode, new List<ConfigIssue>()));
        reset.ConfigNode = hole.ConfigNode;
        Definitions[index] = reset;
    }

    /// <summary>
    /// The hole's disk and jets as they are now: as configured, or, with diskFromTde, as much of them as a tidal
    /// disruption has built (none without one).
    /// </summary>
    public static DiskState StateOf(BlackHoleDefinition hole) => hole.DiskFromTde
        ? DiskState.Forming(TidalDisruptions.FrameFor(hole), hole.DiskInner, hole.DiskOuter, hole.DiskThickness, hole.Jets, hole.JetLength)
        : DiskState.Configured(hole.DiskInner, hole.DiskOuter, hole.DiskThickness, hole.Jets, hole.JetLength);

    public static void Update(PostProfile profile, bool allowed)
    {
        if (Definitions.Count == 0) return;
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || cam.name != ScaledCameraName) continue;
            bool enabled = allowed && profile.BlackHoles;
            var renderer = cam.GetComponent<BlackHoleRenderer>();
            if (renderer == null)
            {
                if (!enabled) return;
                renderer = cam.gameObject.AddComponent<BlackHoleRenderer>();
                Log.Info("Black hole renderer attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
            return;
        }
    }
}
