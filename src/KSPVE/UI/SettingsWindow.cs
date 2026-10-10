using System;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Post;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using KSPVE.Space;
using UnityEngine;

namespace KSPVE.UI;

/// <summary>
/// In-game settings window. Ctrl+Alt+V toggles it; Ctrl+Alt+K toggles all post-processing (kill switch).
/// Changes apply live; Save writes them to GameData/KSPVE/PluginData/user.cfg.
/// </summary>
internal sealed class SettingsWindow : MonoBehaviour
{
    private const string LockId = "KSPVE_SettingsWindow";
    private const int WindowId = 0x4B535056;

    private static readonly string[] TonemapperNames = { "None", "Rolloff", "Neutral", "AgX", "ACES" };
    private static readonly string[] PresetNames = Enum.GetNames(typeof(LookPreset));
    private static readonly string[] DebugNames = Enum.GetNames(typeof(DebugView));

    private bool _visible;
    private bool _hiddenForScreenshot;
    private Rect _rect = new Rect(120, 120, 520, 760);
    private Vector2 _scroll;
    private string _status = string.Empty;
    private int _openHole = -1; // black hole whose look & physics panel is open

    public bool HiddenForScreenshot
    {
        get => _hiddenForScreenshot;
        set => _hiddenForScreenshot = value;
    }

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (!ctrl || !alt) return;

        if (Input.GetKeyDown(KeyCode.V))
        {
            _visible = !_visible;
            if (!_visible) ReleaseLock();
        }
        if (Input.GetKeyDown(KeyCode.K) && Engine.Instance != null)
        {
            PostProfile p = Engine.Instance.Post;
            p.Enabled = !p.Enabled;
            ScreenMessages.PostScreenMessage("KSPVE post-processing " + (p.Enabled ? "ON" : "OFF"), 2f, ScreenMessageStyle.UPPER_CENTER);
        }
    }

    private void OnGUI()
    {
        if (!_visible || _hiddenForScreenshot || Engine.Instance == null) return;
        GUI.skin = HighLogic.Skin;
        _rect = GUILayout.Window(WindowId, _rect, Draw, "KSPVE " + Engine.Version + "   (Ctrl+Alt+V)");
        if (_rect.Contains(Event.current.mousePosition)) InputLockManager.SetControlLock(ControlTypes.ALLBUTCAMERAS, LockId);
        else ReleaseLock();
    }

    private void OnDisable()
    {
        ReleaseLock();
    }

    private static void ReleaseLock()
    {
        InputLockManager.RemoveControlLock(LockId);
    }

    private void Draw(int id)
    {
        Engine engine = Engine.Instance;
        PostProfile p = engine.Post;

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_rect.height - 110));

        Header("General");
        p.Enabled = Toggle("Post-processing   (kill switch: Ctrl+Alt+K)", p.Enabled);
        bool hdr = Toggle("HDR scene cameras", engine.Hdr.Enabled);
        if (hdr != engine.Hdr.Enabled) engine.Hdr.Enabled = hdr;
        p.CompareSplit = Toggle("Split-screen compare (left half = stock KSP)", p.CompareSplit);
        GUILayout.Label("Look presets:");
        int preset = GUILayout.SelectionGrid(-1, PresetNames, 3);
        if (preset >= 0)
        {
            p.ApplyPreset((LookPreset)preset);
            _status = "Applied preset " + PresetNames[preset];
        }

        Header("Exposure & tone");
        GUILayout.Label("Tonemapper:");
        p.Tonemapper = (TonemapperMode)GUILayout.SelectionGrid((int)p.Tonemapper, TonemapperNames, 5);
        p.Exposure = Slider("Exposure (EV)", p.Exposure, -4, 4);
        p.AutoExposure = Toggle("Auto exposure / eye adaptation (experimental)", p.AutoExposure);
        if (p.AutoExposure)
        {
            p.AutoExposureKey = Slider("  Target brightness", p.AutoExposureKey, 0.05, 0.5);
            p.AutoExposureMinEv = Slider("  Min exposure (EV)", p.AutoExposureMinEv, -6, 0);
            p.AutoExposureMaxEv = Slider("  Max exposure (EV)", p.AutoExposureMaxEv, 0, 6);
            p.AutoExposureDarkSpeed = Slider("  Adapt to dark speed", p.AutoExposureDarkSpeed, 0.1, 10);
            p.AutoExposureBrightSpeed = Slider("  Adapt to bright speed", p.AutoExposureBrightSpeed, 0.1, 10);
        }

        Header("Bloom & lens");
        p.Bloom = Toggle("Bloom", p.Bloom);
        if (p.Bloom)
        {
            p.BloomIntensity = Slider("  Intensity", p.BloomIntensity, 0, 2);
            p.BloomThreshold = Slider("  Threshold", p.BloomThreshold, 0, 4);
            p.BloomKnee = Slider("  Softness", p.BloomKnee, 0, 1);
            p.BloomScatter = Slider("  Spread", p.BloomScatter, 0, 1);
            p.LensDirt = Slider("  Lens dirt", p.LensDirt, 0, 2);
            p.Spikes = Slider("  Diffraction spikes", p.Spikes, 0, 2);
            p.SensorSmear = Slider("  Sensor smear", p.SensorSmear, 0, 1);
        }
        p.ChromaticAberration = Slider("Chromatic aberration", p.ChromaticAberration, 0, 1);
        p.Vignette = Slider("Vignette", p.Vignette, 0, 1);

        Header("Colour");
        p.Temperature = Slider("Temperature (cool / warm)", p.Temperature, -1, 1);
        p.Tint = Slider("Tint (green / magenta)", p.Tint, -1, 1);
        p.Contrast = Slider("Contrast", p.Contrast, 0.5, 1.5);
        p.Saturation = Slider("Saturation", p.Saturation, 0, 2);
        p.Vibrance = Slider("Vibrance", p.Vibrance, -1, 1);
        p.NightVision = Slider("Night vision (Purkinje)", p.NightVision, 0, 1);

        Header("Film & display");
        p.Grain = Slider("Film grain", p.Grain, 0, 1);
        p.Sharpen = Slider("Sharpen (CAS)", p.Sharpen, 0, 1);
        p.Broadcast = Slider("Broadcast TV look", p.Broadcast, 0, 1);

        Header("Lighting (experimental)");
        p.AmbientOcclusion = Toggle("Ambient occlusion", p.AmbientOcclusion);
        if (p.AmbientOcclusion)
        {
            p.AoIntensity = Slider("  Intensity", p.AoIntensity, 0, 1);
            p.AoRadius = Slider("  Radius (m)", p.AoRadius, 0.1, 10);
        }
        p.ContactShadows = Toggle("Contact shadows", p.ContactShadows);
        if (p.ContactShadows) p.ContactShadowLength = Slider("  Ray length (m)", p.ContactShadowLength, 0.1, 5);

        Header("Camera (experimental)");
        p.MotionBlur = Toggle("Motion blur", p.MotionBlur);
        if (p.MotionBlur) p.MotionBlurStrength = Slider("  Shutter", p.MotionBlurStrength, 0, 1);
        p.DepthOfField = Toggle("Depth of field", p.DepthOfField);
        if (p.DepthOfField)
        {
            p.DofFocusDistance = Slider("  Focus distance (m, 0 = auto)", p.DofFocusDistance, 0, 500);
            p.DofStrength = Slider("  Strength", p.DofStrength, 0, 1);
        }

        Header("Space");
        p.BlackHoles = Toggle($"Black holes & accretion disks ({KSPVE.Space.BlackHoles.Definitions.Count} configured)", p.BlackHoles);
        if (p.BlackHoles)
        {
            p.BlackHoleBrightness = Slider("  Disk brightness (all holes)", p.BlackHoleBrightness, 0, 5);
            p.BlackHoleResolution = Slider("  Render scale (lower = faster)", p.BlackHoleResolution, 0.25, 1);
            for (int i = 0; i < BlackHoles.Definitions.Count; i++) DrawBlackHole(i);
            DrawTidalDisruption();
        }
        p.OrbitPaths = Toggle("Orbit paths in the map (every body's next lap)", p.OrbitPaths);

        Header("Debug");
        p.DebugView = (DebugView)GUILayout.SelectionGrid((int)p.DebugView, DebugNames, 4);

        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            _status = engine.SaveUserSettings() ? "Saved to GameData/KSPVE/PluginData/user.cfg" : "Save failed; see KSP.log";
        }
        if (GUILayout.Button("Defaults"))
        {
            engine.ResetPost();
            _status = "Defaults restored (not saved yet)";
        }
        if (GUILayout.Button("Screenshot 2x"))
        {
            engine.TakeScreenshot(2);
            _status = "Screenshot saved to the KSP Screenshots folder";
        }
        if (GUILayout.Button("Close"))
        {
            _visible = false;
            ReleaseLock();
        }
        GUILayout.EndHorizontal();
        if (_status.Length > 0) GUILayout.Label(_status);

        GUI.DragWindow();
    }

    /// <summary>Live look & physics controls for one black hole; Save keeps them in user.cfg.</summary>
    private void DrawBlackHole(int index)
    {
        BlackHoleDefinition h = BlackHoles.Definitions[index];
        bool open = _openHole == index;
        string title = $"  {h.BodyName}: colour, disk & drag{(h.Edited ? " (edited)" : string.Empty)}";
        if (Toggle(title, open) != open)
        {
            _openHole = open ? -1 : index;
            open = !open;
        }
        if (!open) return;

        GUI.changed = false;
        GUILayout.BeginHorizontal();
        GUILayout.Label("    Colour:", GUILayout.Width(80));
        foreach (ColorPreset preset in BlackHoleSettings.ColorPresets)
        {
            if (!GUILayout.Button(preset.Name)) continue;
            h.PeakTemperature = preset.PeakTemperature;
            h.ColorSaturation = preset.ColorSaturation;
            h.Brightness = preset.Brightness;
            h.Edited = true;
        }
        GUILayout.EndHorizontal();
        h.PeakTemperature = Slider("    Temperature (K): red > gold > blue", h.PeakTemperature, 1500, 60000, "0");
        h.ColorSaturation = Slider("    Colour strength", h.ColorSaturation, 0, 4);
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 10);
        h.Glow = Slider("    Glow around the hole", h.Glow, 0, 5);
        h.PhotonRing = Slider("    Photon ring", h.PhotonRing, 0, 5);

        h.RotationSpeed = Slider("    Spin speed (1 = a turn per ~8 s)", h.RotationSpeed, 0, 5);
        h.Spin = Toggle("    Reverse spin", h.Spin < 0) ? -1.0 : 1.0;
        h.BeamPeriod = Slider("    Jet sweep: seconds per turn, like a pulsar (0 = along the axis)", h.BeamPeriod, 0, 5);
        if (h.BeamPeriod > 0) h.BeamTilt = Slider("    Jet sweep: tilt from the spin axis (deg)", h.BeamTilt, 0, 90, "0");
        h.DiskOuter = Slider("    Disk size (outer edge, horizon radii)", h.DiskOuter, h.DiskInner + 1, 40);
        h.DiskThickness = Slider("    Thickness", h.DiskThickness, 0.02, 0.4);
        h.Turbulence = Slider("    Puffiness / turbulence", h.Turbulence, 0, 1);
        h.DiskStreaks = Slider("    Swirling streaks seen from above", h.DiskStreaks, 0, 1);
        h.Particles = Slider("    Sparks", h.Particles, 0, 2);
        h.Debris = Slider("    Debris", h.Debris, 0, 1);
        h.Jets = Slider("    Jets (0 = off)", h.Jets, 0, 5);
        if (h.Jets > 0)
        {
            h.JetLength = Slider("      Jet length (horizon radii)", h.JetLength, 5, 200, "0");
            h.JetWidth = Slider("      Jet width", h.JetWidth, 0.1, 3);
            h.JetSpeed = Slider("      Jet speed (fraction of c)", h.JetSpeed, 0, 0.99);
            h.JetSpiral = Slider("      Spiral (0 = plain column)", h.JetSpiral, 0, 1);
            if (h.JetSpiral > 0) h.JetTurns = Slider("      Swirl turns", h.JetTurns, 0, 20, "0.0");
            h.JetFlow = Slider("      Flow (horizon radii/s)", h.JetFlow, 0, 200, "0");
            h.JetAbsorb = Slider("      Volume (absorption)", h.JetAbsorb, 0, 3);
        }
        h.DiskLight = Slider("    Light on ships", h.DiskLight, 0, 5);
        h.Steps = (int)Slider("    Quality (ray steps; lower = faster)", h.Steps, 64, 600, "0");

        h.DiskDrag = Toggle("    Disk drag", h.DiskDrag);
        if (h.DiskDrag)
        {
            h.DiskDragMaxAccel = Slider("      Pull at the edge (m/s^2)", h.DiskDragMaxAccel, 0, 5000, "0");
            h.DiskDragTimescale = Slider("      Grip time (s)", h.DiskDragTimescale, 0.1, 30);
            h.DiskBuffeting = Slider("      Buffeting", h.DiskBuffeting, 0, 5);
        }
        if (GUI.changed) h.Edited = true;

        if (GUILayout.Button($"Reset {h.BodyName} to its config"))
        {
            BlackHoles.ResetToConfig(index);
            _status = $"{h.BodyName} reset to its config (Save to keep)";
        }
    }

    /// <summary>The save's tidal disruption event: where its switch lives, and what it is doing.</summary>
    private static void DrawTidalDisruption()
    {
        GUILayout.Label("  Tidal disruption event");
        string path = TidalDisruptions.FilePath;
        if (path == null)
        {
            GUILayout.Label("    Load a save: tde = on in its file, GameData/KSPVE/PluginData/Saves/<save>.cfg, starts one.");
            return;
        }
        GUILayout.Label("    Switch: tde = on / off in " + path.Replace(Paths.KspRoot, string.Empty).TrimStart(System.IO.Path.DirectorySeparatorChar, '/'));
        TdeSaveState state = TidalDisruptions.State;
        if (state == null || !state.On)
        {
            GUILayout.Label("    Off.");
            if (GUILayout.Button("    Start one now (writes tde = on)")) TidalDisruptions.SetSwitch(true);
            return;
        }
        TidalDisruption model = TidalDisruptions.Model;
        if (model == null)
        {
            GUILayout.Label(TidalDisruptions.Building ? "    Working out the orbits..." : "    No black hole to run it at.");
        }
        else
        {
            double scale = TidalDisruptions.Parameters.TimeScale;
            double physical = TidalDisruptions.GameSecondsSinceStart * scale;
            double toDisruption = (model.ApproachSeconds - physical) / scale;
            string where = TidalDisruptions.Hole != null ? TidalDisruptions.Hole.BodyName : "the hole";
            if (toDisruption > 0)
                GUILayout.Label($"    A star is falling toward {where}: torn apart in {KSPUtil.PrintTime(toDisruption, 3, false)}.");
            else
            {
                TdeFrame f = TidalDisruptions.Current;
                string phase = f != null ? f.Phase.ToString() : "Over";
                GUILayout.Label($"    {phase}: {KSPUtil.PrintTime(-toDisruption, 3, false)} since the star was torn apart ({-toDisruption * scale / 86400.0:0.#} days of real time).");
                double toReturn = (model.FirstReturnSeconds + model.ApproachSeconds - physical) / scale;
                if (toReturn > 0) GUILayout.Label($"    The first debris returns in {KSPUtil.PrintTime(toReturn, 3, false)}.");
                if (f != null && f.Luminosity > 0.0)
                {
                    GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "    Luminosity {0:0.00} x Eddington; debris falling back at {1:0.0} x Eddington.", f.Luminosity, f.FallbackEddington));
                    if (f.PhotosphereMeters > 0.0)
                        GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "    Photosphere {0:0} horizon radii, {1:0} K.", f.PhotosphereMeters / (model.TimeUnit * 299792458.0), f.PhotosphereTemperature));
                }
            }
            TdeFrame now = TidalDisruptions.Current;
            if (TidalDisruptions.Hole != null && TidalDisruptions.Hole.DiskFromTde && now != null)
            {
                string jets = now.JetAge > 0.0 ? "jets launched" : "jets launch when it is half built";
                GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "    {0}'s disk: {1:0}% built; {2}.", TidalDisruptions.Hole.BodyName, now.DiskFormation * 100.0, jets));
            }
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "    Plays at {0:0.###}x real time; flare peaks {1:0} real days after disruption.", scale, model.PeakSeconds / 86400.0));
        }
        if (GUILayout.Button("    Stop (writes tde = off)")) TidalDisruptions.SetSwitch(false);
    }

    private static void Header(string text)
    {
        GUILayout.Space(8);
        GUILayout.Label("<b>" + text + "</b>");
    }

    private static bool Toggle(string label, bool value) => GUILayout.Toggle(value, " " + label);

    private static double Slider(string label, double value, double min, double max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(230));
        float v = GUILayout.HorizontalSlider((float)value, (float)min, (float)max, GUILayout.Width(180));
        GUILayout.Label(v.ToString(format, CultureInfo.InvariantCulture), GUILayout.Width(50));
        GUILayout.EndHorizontal();
        return Math.Round(v, 3);
    }
}
