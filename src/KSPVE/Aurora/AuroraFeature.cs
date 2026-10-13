using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.Config;
using KSPVE.Pure.SpaceWeather;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Aurora;

/// <summary>
/// Aurorae and airglow: every kind (discrete arcs and curtains with rays, folds and curls, diffuse and pulsating aurora, black
/// aurora, the corona overhead, polar cap arcs, proton aurora, SAR arcs and STEVE, global and crustal aurora on bodies with no
/// field, a giant's main oval and its moons' footprints) and the airglow layers, each at the heights and colours its emission
/// physics gives, from a magnetosphere model driven by the solar wind (KSPVE.Pure.Aurora). The bodies and their fields are
/// configured in KSPVE_AURORA; a body without a node is given a model by a rule.
/// </summary>
internal sealed class AuroraFeature : Feature
{
    private const string LocalCameraName = "Camera 00", ScaledCameraName = "Camera ScaledSpace";
    /// <summary>The scaled camera draws bodies whose glowing shell is at least this wide on the sky (rad).</summary>
    private const double MinAngularSize = 0.004;

    public static AuroraFeature Instance { get; private set; }

    private CfgNode _defaults;
    private Camera[] _cameras = new Camera[16];
    private Camera _local, _scaled;

    public override string Name => "Aurora";
    public override int Order => 20;
    public override string SettingsTitle => "Aurorae & airglow";

    public override void Load(List<ConfigIssue> issues)
    {
        Instance = this;
        AuroraSettings.Reset();
        CfgNode root = KspConfig.LoadMerged("KSPVE_AURORA");
        _defaults = root;
        AuroraBodies.Load(root, issues);
        AuroraSettings.Apply(root);
        foreach (CfgNode user in KspConfig.LoadUserNodes())
            if (user.Name == AuroraSettings.UserNodeName) AuroraSettings.Apply(user);
        Log.Info($"Aurora: {System.Linq.Enumerable.Count(AuroraBodies.ConfiguredModels)} configured bodies; night exposure gain {AuroraSettings.NightGain:0.0e0}.");
    }

    public override void ResetToDefaults()
    {
        AuroraSettings.Reset();
        if (_defaults != null) AuroraSettings.Apply(_defaults);
    }

    public override CfgNode UserNode() => AuroraSettings.ToNode();

    public override void Shutdown()
    {
        AuroraBodies.Reset();
        Instance = null;
    }

    public override void Update(bool allowed)
    {
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        _local = _scaled = null;
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null) continue;
            if (cam.name == LocalCameraName) { _local = cam; Attach(cam, true, allowed); }
            else if (cam.name == ScaledCameraName) { _scaled = cam; Attach(cam, false, allowed); }
        }
    }

    private static void Attach(Camera cam, bool local, bool allowed)
    {
        bool enabled = allowed && AuroraSettings.Enabled;
        var renderer = cam.GetComponent<AuroraRenderer>();
        if (renderer == null)
        {
            if (!enabled) return;
            renderer = cam.gameObject.AddComponent<AuroraRenderer>();
            renderer.Local = local;
            Log.Info("Aurora renderer attached to " + cam.name + ".");
        }
        if (renderer.enabled != enabled) renderer.enabled = enabled;
    }

    /// <summary>
    /// The bodies a camera draws this frame, largest first. The local camera draws the body the player is in the glowing layers
    /// of (so terrain and the volumetric clouds hide the aurora behind them); the scaled camera draws every other body in view
    /// (and all of them in the map), the planet an exact sphere.
    /// </summary>
    public void Select(Camera cam, bool local, List<AuroraDraw> into)
    {
        into.Clear();
        if (FlightGlobals.fetch == null || FlightGlobals.Bodies == null) return;
        bool flight = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.currentMainBody != null;
        bool map = flight && MapView.MapIsEnabled;
        CelestialBody main = flight ? FlightGlobals.currentMainBody : null;
        Vector3d camPos = cam.transform.position;

        // Does the local camera draw the main body? (When it is inside its glowing layers, in the flight view.)
        AuroraBody mainBody = null;
        bool localDrawsMain = false;
        if (main != null && !map && main.atmosphere)
        {
            mainBody = AuroraBodies.Of(main);
            if (mainBody != null)
            {
                Vector3d at = _local != null ? (Vector3d)_local.transform.position : camPos;
                localDrawsMain = (at - main.position).magnitude - main.Radius < mainBody.LocalLimit;
            }
        }

        if (local)
        {
            if (localDrawsMain)
                into.Add(new AuroraDraw { Body = mainBody, CameraFromBody = camPos - main.position, AngularSize = Math.PI });
            return;
        }

        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody body = bodies[i];
            if (body == null || body.isStar || !body.atmosphere || body.scaledBody == null) continue;
            if (localDrawsMain && body == main) continue;
            Vector3d rel = (camPos - (Vector3d)body.scaledBody.transform.position) * ScaledSpace.ScaleFactor;
            double distance = rel.magnitude;
            double shell = body.Radius * 1.1 + 300000.0;
            double angular = Math.Asin(Math.Min(1.0, shell / Math.Max(distance, 1.0)));
            if (angular < MinAngularSize) continue;
            AuroraBody a = AuroraBodies.Of(body);
            if (a == null) continue;
            into.Add(new AuroraDraw { Body = a, CameraFromBody = rel, AngularSize = angular });
        }
        if (into.Count > 1) into.Sort((p, q) => q.AngularSize.CompareTo(p.AngularSize));
    }

    // ---- the settings window ----

    public override void DrawSettings()
    {
        AuroraSettings.Enabled = Gui.Toggle("Aurorae and airglow", AuroraSettings.Enabled);
        if (!AuroraSettings.Enabled) return;
        Gui.Note("Kinds: each is drawn where and at the height its physics puts it (by pressure level, so every atmosphere works).");
        AuroraSettings.Arcs = Gui.Toggle("Discrete arcs and curtains (rays, folds, curls) and giant planets' ovals", AuroraSettings.Arcs);
        AuroraSettings.Diffuse = Gui.Toggle("Diffuse aurora", AuroraSettings.Diffuse);
        AuroraSettings.Pulsating = Gui.Toggle("Pulsating aurora (patches blinking every 2-20 s, after substorms)", AuroraSettings.Pulsating);
        AuroraSettings.Black = Gui.Toggle("Black aurora (dark voids in the diffuse glow)", AuroraSettings.Black);
        AuroraSettings.Proton = Gui.Toggle("Proton aurora (diffuse hydrogen-alpha red)", AuroraSettings.Proton);
        AuroraSettings.PolarCap = Gui.Toggle("Polar cap arcs (theta aurora, northward interplanetary field)", AuroraSettings.PolarCap);
        AuroraSettings.Sar = Gui.Toggle("SAR arcs (red, in strong storms)", AuroraSettings.Sar);
        AuroraSettings.Steve = Gui.Toggle("STEVE (mauve ribbon with a green picket fence, in strong storms)", AuroraSettings.Steve);
        AuroraSettings.Global = Gui.Toggle("Global aurora on bodies with no field (energetic particle events)", AuroraSettings.Global);
        AuroraSettings.Crustal = Gui.Toggle("Aurora over crustal magnetic fields (Mars-like)", AuroraSettings.Crustal);
        AuroraSettings.Footprints = Gui.Toggle("Moon footprints on gas giants", AuroraSettings.Footprints);
        AuroraSettings.Airglow = Gui.Toggle("Airglow and nightglow (oxygen 557.7 and 630 nm, sodium, hydroxyl)", AuroraSettings.Airglow);

        Gui.Header("Aurora display");
        Gui.Note("Realistic is 1. The brightness is in kilorayleighs (IBC I-IV are 1, 10, 100 and 1000 kR of 557.7 nm).");
        AuroraSettings.AuroraBoost = Gui.Slider("Aurora brightness boost", AuroraSettings.AuroraBoost, 0.0, 10.0);
        AuroraSettings.AirglowBoost = Gui.Slider("Airglow brightness boost", AuroraSettings.AirglowBoost, 0.0, 20.0);
        Gui.Note("Night exposure: the dark-adapted view's camera. A photographer's aurora shot, not a daylight exposure; it fades to daylight's by dawn.");
        AuroraSettings.NightIso = Gui.Slider("  ISO", AuroraSettings.NightIso, 100, 51200, "0");
        AuroraSettings.NightAperture = Gui.Slider("  f-number", AuroraSettings.NightAperture, 1.0, 5.6, "0.0");
        AuroraSettings.NightShutter = Gui.Slider("  Exposure time (s)", AuroraSettings.NightShutter, 0.5, 30.0, "0.0");
        Gui.Note($"  gain over a bright day's exposure: {AuroraSettings.NightGain:0.0e0} ({Math.Log(AuroraSettings.NightGain, 2.0):0.0} stops)");
        AuroraSettings.Resolution = Gui.Slider("Resolution (fraction of the screen)", AuroraSettings.Resolution, 0.2, 1.0);
        AuroraSettings.Smoothing = Gui.Slider("Temporal smoothing", AuroraSettings.Smoothing, 0.0, 0.95);

        Gui.Header("Aurora now");
        DrawReadout();
    }

    private static void DrawReadout()
    {
        if (FlightGlobals.fetch == null || FlightGlobals.currentMainBody == null) { Gui.Note("Not in flight."); return; }
        CelestialBody body = FlightGlobals.currentMainBody;
        AuroraBody a = AuroraBodies.Of(body);
        if (a == null) { Gui.Note(body.bodyName + " has no atmosphere to glow."); return; }
        AuroraFrame f = a.Solve();
        CultureInfo c = CultureInfo.InvariantCulture;
        GUILayout.Label($"{body.bodyName}: {(a.Model.HasField ? "dipole " + (a.Model.Field.EquatorialField * 1e6).ToString("0.0", c) + " uT" : "no global field")}, {a.Model.Composition} ({a.Model.Source})");
        SolarWindState w = f.Wind;
        GUILayout.Label($"Solar wind: {w.Speed / 1000.0:0} km/s, {w.Density / 1e6:0.0} /cm3, B {w.FieldStrength * 1e9:0.0} nT (north {w.FieldNorth * 1e9:0.0}), particles x{w.EnergeticParticles:0}");
        if (a.Model.HasField)
        {
            GUILayout.Label($"Activity index (Kp-like): {f.Oval.Kp:0.0}");
            GUILayout.Label($"Oval: {f.OvalLatitudeDegrees:0.0} deg at midnight; magnetopause stand-off {f.Oval.StandOff:0.0} radii ({f.Oval.StandOff * body.Radius / 1000.0:0} km)");
            GUILayout.Label("Substorm: " + SubstormText(f.Storm, f.Ut));
            GUILayout.Label($"Arcs: {f.Arc:0.#} kR of 557.7 nm at their brightest ({IbcClass(f.Arc)}), diffuse {f.Diffuse:0.0} kR");
            string extras = (f.StevePresent ? "STEVE  " : "") + (f.ThetaPresent ? "polar cap arcs  " : "") + (f.Sar > 0 ? "SAR arcs  " : "") + (f.Pulsating > 0.05 ? "pulsating  " : "");
            if (extras.Length > 0) GUILayout.Label("Showing: " + extras);
        }
        else
        {
            GUILayout.Label($"Energetic particles: {(f.Global > 0 || f.Crustal > 0 ? "an event, global aurora on the night side" : "quiet")}");
        }
        GUILayout.Label("Storm: " + (w.InCme > 0.5 ? "inside a CME" : f.InStorm ? "geomagnetic storm" : "quiet"));
        GUILayout.Label($"Airglow: {f.GlowGreen:0} R green, {f.GlowRed:0} R red");
    }

    /// <summary>The International Brightness Coefficient of an aurora of the given kilorayleighs (I-IV are 1, 10, 100 and 1000 kR).</summary>
    private static string IbcClass(double kiloRayleighs)
    {
        if (kiloRayleighs < 1.0) return "below IBC I";
        string[] names = { "IBC I", "IBC II", "IBC III", "IBC IV" };
        return names[Math.Min(3, (int)Math.Floor(Math.Log10(kiloRayleighs)))];
    }

    private static string SubstormText(SubstormState s, double ut)
    {
        switch (s.Phase)
        {
            case SubstormPhase.Growth: return $"growth phase, onset in {(s.OnsetUt - ut) / 60.0:0} min";
            case SubstormPhase.Expansion: return $"expansion, {s.PhaseTime / 60.0:0} min after onset";
            case SubstormPhase.Recovery: return $"recovery, {(ut - s.OnsetUt) / 60.0:0} min after onset";
            default: return "none";
        }
    }
}
