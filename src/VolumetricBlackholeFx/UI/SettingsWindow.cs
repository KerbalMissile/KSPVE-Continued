using System;
using System.Globalization;
using VolumetricBlackholeFx.Core;
using VolumetricBlackholeFx.Pure.Space;
using VolumetricBlackholeFx.Space;
using UnityEngine;

namespace VolumetricBlackholeFx.UI;

/// <summary>
/// In-game black hole editor. Ctrl+Alt+V toggles it. Every characteristic of every configured hole (colour, disk, jets,
/// what it does to ships) changes live; Save keeps the changes in GameData/VolumetricBlackholeFx/PluginData/user.cfg.
/// </summary>
internal sealed class SettingsWindow : MonoBehaviour
{
    private const string LockId = "VolumetricBlackholeFx_SettingsWindow";
    private const int WindowId = 0x56424658;
    private const double Rad2Deg = 180.0 / Math.PI, Deg2Rad = Math.PI / 180.0;

    private static readonly string[] Tabs = { "Colour", "Disk", "Jets", "Ships & quality" };

    private bool _visible;
    private Rect _rect = new Rect(120, 100, 560, 720);
    private Vector2 _scroll;
    private string _status = string.Empty;
    private int _openHole = -1;   // the hole whose controls are open
    private int _tab;

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (!ctrl || !alt || !Input.GetKeyDown(KeyCode.V)) return;
        _visible = !_visible;
        if (!_visible) ReleaseLock();
    }

    private void OnGUI()
    {
        if (!_visible || Engine.Instance == null) return;
        GUI.skin = HighLogic.Skin;
        _rect = GUILayout.Window(WindowId, _rect, Draw, "Volumetric Blackhole Fx " + Engine.Version + "   (Ctrl+Alt+V)");
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
        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(_rect.height - 100));

        Header("All black holes");
        engine.Enabled = Toggle("Draw black holes", engine.Enabled);
        engine.Brightness = Slider("Brightness (every disk)", engine.Brightness, 0, 5);
        engine.RenderScale = Slider("Render scale (lower = faster)", engine.RenderScale, 0.25, 1);
        if (!ShaderLibrary.Loaded) GUILayout.Label("The shader bundle did not load (it needs DirectX 11); see KSP.log.");

        Header($"Black holes ({BlackHoles.Definitions.Count} configured)");
        if (BlackHoles.Definitions.Count == 0)
            GUILayout.Label("None: add a VOLUMETRIC_BLACKHOLE node naming a body (see the configs in GameData/VolumetricBlackholeFx/Config).");
        for (int i = 0; i < BlackHoles.Definitions.Count; i++) DrawHole(i);

        GUILayout.EndScrollView();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
            _status = engine.SaveUserSettings() ? "Saved to GameData/VolumetricBlackholeFx/PluginData/user.cfg" : "Save failed; see KSP.log";
        if (GUILayout.Button("Reset all to the configs"))
        {
            engine.ResetGlobals();
            for (int i = 0; i < BlackHoles.Definitions.Count; i++) BlackHoles.ResetToConfig(i);
            _status = "Everything back to the configs (Save to keep)";
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

    /// <summary>One hole: a fold-out with its characteristics in tabs.</summary>
    private void DrawHole(int index)
    {
        BlackHoleDefinition h = BlackHoles.Definitions[index];
        bool open = _openHole == index;
        if (Toggle($"{h.BodyName}{(h.Edited ? "  (edited)" : string.Empty)}", open) != open)
        {
            _openHole = open ? -1 : index;
            open = !open;
        }
        if (!open) return;

        _tab = GUILayout.SelectionGrid(_tab, Tabs, Tabs.Length);
        GUI.changed = false;
        switch (_tab)
        {
            case 0: DrawColour(h); break;
            case 1: DrawDisk(h); break;
            case 2: DrawJets(h); break;
            default: DrawShips(h); break;
        }
        if (GUI.changed) h.Edited = true;

        GUILayout.Space(4);
        if (GUILayout.Button($"Reset {h.BodyName} to its config"))
        {
            BlackHoles.ResetToConfig(index);
            _status = $"{h.BodyName} back to its config (Save to keep)";
        }
    }

    private static void DrawColour(BlackHoleDefinition h)
    {
        GUILayout.Label("Presets:");
        ColorPreset[] presets = BlackHoleSettings.ColorPresets;
        for (int row = 0; row < presets.Length; row += 4)
        {
            GUILayout.BeginHorizontal();
            for (int i = row; i < Math.Min(row + 4, presets.Length); i++)
            {
                if (!GUILayout.Button(presets[i].Name)) continue;
                ColorPreset p = presets[i];
                h.PeakTemperature = p.PeakTemperature;
                h.ColorSaturation = p.ColorSaturation;
                h.Brightness = p.Brightness;
                h.Tint = p.Tint;
                if (p.Tint > 0.0)
                {
                    h.TintRed = p.TintRed;
                    h.TintGreen = p.TintGreen;
                    h.TintBlue = p.TintBlue;
                }
                GUI.changed = true;
            }
            GUILayout.EndHorizontal();
        }

        h.PeakTemperature = Slider("Temperature (K): red > gold > white > blue", h.PeakTemperature, 1500, 60000, "0");
        h.ColorSaturation = Slider("Colour strength", h.ColorSaturation, 0, 4);
        h.Brightness = Slider("Brightness", h.Brightness, 0, 10);
        h.Glow = Slider("Glow around the hole", h.Glow, 0, 5);
        h.PhotonRing = Slider("Photon ring", h.PhotonRing, 0, 5);

        GUILayout.Space(6);
        h.Tint = Slider("Custom colour (0 = the real blackbody colour)", h.Tint, 0, 1);
        if (h.Tint > 0.0)
        {
            ColourSliders("Disk colour", ref h.TintRed, ref h.TintGreen, ref h.TintBlue);
        }
    }

    private static void DrawDisk(BlackHoleDefinition h)
    {
        h.DiskInner = Slider("Inner edge (horizon radii; 3 = innermost stable orbit)", h.DiskInner, 1.5, Math.Max(h.DiskOuter - 1.0, 1.6));
        h.DiskOuter = Slider("Outer edge (horizon radii)", h.DiskOuter, h.DiskInner + 1.0, 60);
        h.DiskThickness = Slider("Thickness", h.DiskThickness, 0.02, 0.4);
        h.Turbulence = Slider("Puffiness / turbulence", h.Turbulence, 0, 1);
        h.Particles = Slider("Sparks", h.Particles, 0, 2);
        h.Debris = Slider("Debris", h.Debris, 0, 1);
        h.RotationSpeed = Slider("Spin speed (1 = a turn every ~8 s)", h.RotationSpeed, 0, 5);
        h.Spin = Toggle("Reverse spin", h.Spin < 0) ? -1.0 : 1.0;

        // The disk's orientation, as a tilt from the plane of the planets' orbits (the inertial frame's Y axis is its pole).
        Vector3d n = h.Normal.sqrMagnitude > 0.0 ? h.Normal.normalized : new Vector3d(0, 1, 0);
        double tilt = Math.Acos(Math.Max(-1.0, Math.Min(1.0, n.y))) * Rad2Deg;
        double direction = Math.Atan2(n.z, n.x) * Rad2Deg;
        if (direction < 0.0) direction += 360.0;
        double newTilt = Slider("Tilt (deg; 0 = in the plane of the planets)", tilt, 0, 180, "0.0");
        double newDirection = Slider("Tilt direction (deg)", direction, 0, 360, "0");
        if (newTilt != tilt || newDirection != direction)
        {
            double t = newTilt * Deg2Rad, d = newDirection * Deg2Rad;
            h.Normal = new Vector3d(Math.Sin(t) * Math.Cos(d), Math.Cos(t), Math.Sin(t) * Math.Sin(d));
        }

        CelestialBody body = h.Body;
        if (body != null && body.Radius > 0.0)
        {
            double size = h.HorizonRadiusMeters > 0.0 ? h.HorizonRadiusMeters / body.Radius : 1.0;
            double newSize = Slider("Hole size (x the body's radius)", size, 0.1, 5);
            if (newSize != size) h.HorizonRadiusMeters = Math.Abs(newSize - 1.0) < 1e-3 ? 0.0 : newSize * body.Radius;
        }
    }

    private static void DrawJets(BlackHoleDefinition h)
    {
        h.Jets = Slider("Jets (0 = off)", h.Jets, 0, 5);
        if (h.Jets <= 0.0) return;
        h.JetLength = Slider("Length (horizon radii)", h.JetLength, 5, 200, "0");
        h.JetWidth = Slider("Width", h.JetWidth, 0.1, 3);
        h.JetSpeed = Slider("Speed (fraction of light speed)", h.JetSpeed, 0, 0.99);
        h.JetSpiral = Slider("Spiral (0 = plain column)", h.JetSpiral, 0, 1);
        if (h.JetSpiral > 0) h.JetTurns = Slider("Swirl turns", h.JetTurns, 0, 20, "0.0");
        h.JetFlow = Slider("Flow (horizon radii per second)", h.JetFlow, 0, 200, "0");
        h.JetAbsorb = Slider("Volume (absorption)", h.JetAbsorb, 0, 3);
        ColourSliders("Jet colour", ref h.JetRed, ref h.JetGreen, ref h.JetBlue);
        h.BeamPeriod = Slider("Sweep like a pulsar: seconds per turn (0 = still)", h.BeamPeriod, 0, 5);
        if (h.BeamPeriod > 0) h.BeamTilt = Slider("Sweep tilt from the spin axis (deg)", h.BeamTilt, 0, 90, "0");
    }

    private static void DrawShips(BlackHoleDefinition h)
    {
        h.DiskLight = Slider("Light on ships", h.DiskLight, 0, 5);
        h.DiskDrag = Toggle("Disk drag: the gas pulls ships along with it", h.DiskDrag);
        if (h.DiskDrag)
        {
            h.DiskDragMaxAccel = Slider("Pull at the outer edge (m/s^2)", h.DiskDragMaxAccel, 0, 5000, "0");
            h.DiskDragTimescale = Slider("Grip time (s)", h.DiskDragTimescale, 0.1, 30);
            h.DiskBuffeting = Slider("Buffeting", h.DiskBuffeting, 0, 5);
        }
        h.Steps = (int)Slider("Quality (ray steps; lower = faster)", h.Steps, 64, 600, "0");
    }

    /// <summary>Red, green and blue sliders (on-screen colour, 0..1) with a swatch of the colour.</summary>
    private static void ColourSliders(string label, ref double r, ref double g, ref double b)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(250));
        Rect swatch = GUILayoutUtility.GetRect(60, 18, GUILayout.Width(60), GUILayout.Height(18));
        Color old = GUI.color;
        GUI.color = new Color((float)r, (float)g, (float)b, 1f);
        GUI.DrawTexture(swatch, Texture2D.whiteTexture);
        GUI.color = old;
        GUILayout.EndHorizontal();
        r = Slider("  Red", r, 0, 1);
        g = Slider("  Green", g, 0, 1);
        b = Slider("  Blue", b, 0, 1);
    }

    private static void Header(string text)
    {
        GUILayout.Space(8);
        GUILayout.Label("<b>" + text + "</b>");
    }

    private static bool Toggle(string label, bool value) => GUILayout.Toggle(value, " " + label);

    /// <summary>A labelled slider; returns <paramref name="value"/> untouched unless the player moves it.</summary>
    private static double Slider(string label, double value, double min, double max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(250));
        float shown = (float)value;
        float v = GUILayout.HorizontalSlider(shown, (float)min, (float)max, GUILayout.Width(200));
        GUILayout.Label(v.ToString(format, CultureInfo.InvariantCulture), GUILayout.Width(55));
        GUILayout.EndHorizontal();
        // The slider clamps to its range: a config value beyond it only changes when the player drags it.
        return v == Mathf.Clamp(shown, (float)min, (float)max) ? value : Math.Round(v, 3);
    }
}
