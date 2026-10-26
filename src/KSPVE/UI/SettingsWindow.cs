using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using KSPVE.Space;
using UnityEngine;

namespace KSPVE.UI;

/// <summary>
/// In-game settings window. Ctrl+Alt+V toggles it.
/// Changes apply live; Save writes them to GameData/KSPVE/PluginData/user.cfg.
/// </summary>
internal sealed class SettingsWindow : MonoBehaviour
{
    private const string LockId = "KSPVE_SettingsWindow";
    private const int WindowId = 0x4B535056;


    private bool _visible;
    private bool _hiddenForScreenshot;
    private Rect _rect = new Rect(120, 120, 700, 760);
    private Vector2 _scroll;
    private string _status = string.Empty;
    private int _openHole = -1; // black hole (or other object) whose panel is open
    private int _addBody;        // the body and kind the "add an object" row offers
    private int _addKind = (int)SpaceObjectKind.AsteroidBelt;
    private static bool _advanced;  // the open object's finer settings shown too

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
        SpaceRenderSettings p = engine.SpaceRender;

        // Everything inside stays as wide as the window (long lines wrap, grids share the width out) and scrolls only
        // up and down, so nothing is ever cut off at the right edge.
        bool wrap = GUI.skin.label.wordWrap;
        GUI.skin.label.wordWrap = true;
        _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(_rect.height - 110));
        GUILayout.BeginVertical(GUILayout.Width(_rect.width - 44));

        Header("Space");
        p.BlackHoles = Toggle($"Black holes, neutron stars, disks, belts, nebulae & comets ({KSPVE.Space.BlackHoles.Definitions.Count} configured)", p.BlackHoles);
        if (p.BlackHoles)
        {
            p.BlackHoleBrightness = Slider("  Brightness (all of them)", p.BlackHoleBrightness, 0, 20);
            p.BlackHoleResolution = Math.Min(Math.Max(Slider("  Render scale (lower = faster, above 1 supersampled)", p.BlackHoleResolution, 0.05, 4), 0.05), 4.0);
            p.BlackHoleBrightness = Math.Max(p.BlackHoleBrightness, 0.0);
            for (int i = 0; i < BlackHoles.Definitions.Count; i++) DrawBlackHole(i);
            DrawAddObject();
            DrawProcedural();
        }
        p.OrbitPaths = Toggle("Orbit paths in the map (every body's next lap)", p.OrbitPaths);

        foreach (Feature f in Features.All)
        {
            if (f.SettingsTitle == null) continue;
            Header(f.SettingsTitle);
            Features.Guard(f, "DrawSettings", f.DrawSettings);
        }

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.skin.label.wordWrap = wrap;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save"))
        {
            _status = engine.SaveUserSettings() ? "Saved to GameData/KSPVE/PluginData/user.cfg" : "Save failed; see KSP.log";
        }
        if (GUILayout.Button("Defaults"))
        {
            engine.SpaceRender.Apply(SpaceRenderSettings.Schema.Resolve(null, new List<ConfigIssue>()));
            Features.ResetToDefaults();
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

    /// <summary>
    /// Live controls for one object, every setting its kind has (Save keeps them in user.cfg): what it is, its look, its
    /// size and orientation, and, for a black hole, its pull on ships.
    /// </summary>
    private void DrawBlackHole(int index)
    {
        BlackHoleDefinition h = BlackHoles.Definitions[index];
        bool open = _openHole == index;
        string title = $"  {h.Title}{(h.Procedural ? " (procedural)" : string.Empty)}{(h.Enabled ? string.Empty : " (off)")}{(h.Edited ? " (edited)" : string.Empty)}";
        if (Toggle(title, open) != open)
        {
            _openHole = open ? -1 : index;
            open = !open;
        }
        if (!open) return;

        GUI.changed = false;
        h.Enabled = Toggle("    Drawn", h.Enabled);
        GUILayout.Label("    What it is (switching starts from that kind's settings):");
        int kind = GUILayout.SelectionGrid((int)h.Kind, SpaceObjects.KindNames, 4);
        if (kind != (int)h.Kind)
        {
            BlackHoles.ChangeKind(index, (SpaceObjectKind)kind);
            _status = $"{h.BodyName} is now a {SpaceObjects.KindName((SpaceObjectKind)kind)} (Save to keep)";
            return;
        }

        switch (h.Kind)
        {
            case SpaceObjectKind.BlackHole: DrawHole(h); break;
            case SpaceObjectKind.NeutronStar: DrawNeutronStar(h); break;
            case SpaceObjectKind.Nebula: DrawNebula(h); break;
            case SpaceObjectKind.Comet: DrawComet(h); break;
            case SpaceObjectKind.Galaxy: DrawGalaxy(h); break;
            default: DrawDustyDisk(h); break;
        }
        DrawPlacement(h);
        if (GUI.changed)
        {
            h.Edited = true;
            h.KeepPhysical();
        }

        GUILayout.BeginHorizontal();
        if (h.ConfigNode != null && GUILayout.Button($"Reset {h.Title} to its config"))
        {
            BlackHoles.ResetToConfig(index);
            _status = $"{h.Title} reset to its config (Save to keep)";
        }
        if (h.Procedural && GUILayout.Button($"Keep {h.Title} (save it, edit it)"))
        {
            BlackHoles.Keep(index);
            _status = $"{h.Title} kept (Save to keep it for good)";
        }
        if (h.AddedInGame && GUILayout.Button($"Remove {h.Title}"))
        {
            BlackHoles.Remove(index);
            _openHole = -1;
            _status = $"{h.Title} removed (Save to keep)";
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>A black hole: its disk's colour, size and streaks, its jets, its light and drag on ships.</summary>
    private static void DrawHole(BlackHoleDefinition h)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("    Colour:", GUILayout.Width(80));
        foreach (ColorPreset preset in BlackHoleSettings.ColorPresets)
        {
            if (!GUILayout.Button(preset.Name)) continue;
            h.PeakTemperature = preset.PeakTemperature;
            h.ColorSaturation = preset.ColorSaturation;
            h.Brightness = preset.Brightness;
            GUI.changed = true;
        }
        GUILayout.EndHorizontal();
        h.PeakTemperature = Slider("    Temperature (K): red > gold > blue", h.PeakTemperature, 1000, 200000, "0");
        h.ColorSaturation = Slider("    Colour strength", h.ColorSaturation, 0, 4);
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        h.Glow = Slider("    Glow around the hole", h.Glow, 0, 5);
        h.PhotonRing = Slider("    Photon ring", h.PhotonRing, 0, 5);
        h.PhotonRingColor = ColorSliders("    Photon ring colour", h.PhotonRingColor);
        DrawDiskParts(h);

        h.RotationSpeed = Slider("    Spin speed (1 = a turn per ~8 s)", h.RotationSpeed, 0, 5);
        h.Spin = Toggle("    Reverse spin", h.Spin < 0) ? -1.0 : 1.0;
        h.BeamPeriod = Slider("    Jet sweep: seconds per turn, like a pulsar (0 = along the axis)", h.BeamPeriod, 0, 5);
        if (h.BeamPeriod > 0) h.BeamTilt = Slider("    Jet sweep: tilt from the spin axis (deg)", h.BeamTilt, 0, 90, "0");
        h.HorizonRadiusMeters = Slider("    Horizon radius (m; 0 = from the body)", h.HorizonRadiusMeters, 0, 1e11, "0");
        h.BodyIsShadow = Toggle("    Draw it the body's size (the body is the shadow, not the horizon)", h.BodyIsShadow);
        h.DiskInner = Slider("    Disk inner edge (horizon radii; 3 = innermost stable orbit)", h.DiskInner, 1, 50);
        h.DiskOuter = Slider("    Disk size (outer edge, horizon radii)", h.DiskOuter, h.DiskInner + 1, 500);
        h.DiskThickness = Slider("    Thickness", h.DiskThickness, 0.01, 2);
        h.Turbulence = Slider("    Puffiness / turbulence", h.Turbulence, 0, 1);
        h.DiskStreaks = Slider("    Swirling streaks seen from above", h.DiskStreaks, 0, 1);
        if (h.DiskStreaks > 0)
        {
            h.StreaksFrom = Slider("      Showing from this high above the disk (deg)", h.StreaksFrom, 0, 89, "0");
            h.StreaksFull = Slider("      All there from (deg)", h.StreaksFull, 1, 90, "0");
        }
        h.OuterGlow = Slider("    Outer disk glow (big disks)", h.OuterGlow, 0, 3);
        h.DiskEdge = Slider("    Outer edge: wispy (0) to crisp (1)", h.DiskEdge, 0, 1);
        h.DiskEvenness = Slider("    Display: evenly bright out to the edge (0 = dims as it cools)", h.DiskEvenness, 0, 1);
        h.Rings = Slider("    Rings (gaps in the disk)", h.Rings, 0, 16, "0");
        if (h.Rings > 0 || !string.IsNullOrEmpty(h.Gaps))
        {
            h.RingDepth = Slider("      Gap depth", h.RingDepth, 0, 1);
            h.RingWidth = Slider("      Gap width (fraction of outer edge)", h.RingWidth, 0.001, 0.2, "0.000");
        }
        h.Gaps = TextRow("    Gaps by hand: radii (fractions of the outer edge)", h.Gaps);
        h.Particles = Slider("    Sparks", h.Particles, 0, 2);
        h.Debris = Slider("    Debris", h.Debris, 0, 1);
        h.Jets = Slider("    Jets (0 = off)", h.Jets, 0, 5);
        if (h.Jets > 0)
        {
            h.JetLength = Slider("      Jet length (horizon radii)", h.JetLength, 5, 2000, "0");
            h.JetWidth = Slider("      Jet width", h.JetWidth, 0.1, 3);
            h.JetSpeed = Slider("      Jet speed (fraction of c)", h.JetSpeed, 0, 0.99);
            h.JetSpiral = Slider("      Spiral (0 = plain column)", h.JetSpiral, 0, 1);
            if (h.JetSpiral > 0)
            {
                h.JetTurns = Slider("      Swirl turns", h.JetTurns, 0, 20, "0.0");
                h.JetStrands = Slider("      Strands round it", h.JetStrands, 1, 60, "0");
                h.JetStrandSharpness = Slider("      Strand thinness (1 = soft bands)", h.JetStrandSharpness, 0.5, 12);
            }
            h.JetFlow = Slider("      Flow (horizon radii/s)", h.JetFlow, 0, 200, "0");
            h.JetAbsorb = Slider("      Volume (absorption)", h.JetAbsorb, 0, 3);
            h.JetKnots = Slider("      Breaks into blobs (0 = continuous)", h.JetKnots, 0, 1);
            if (h.JetKnots > 0)
            {
                h.JetKnotStart = Slider("        Where it breaks (fraction of length)", h.JetKnotStart, 0, 1);
                h.JetKnotSpacing = Slider("        Blob spacing", h.JetKnotSpacing, 0.02, 0.5, "0.000");
                h.JetKnotGrowth = Slider("        Blobs swell by", h.JetKnotGrowth, 0, 4);
            }
            h.JetColor = ColorSliders("      Jet colour", h.JetColor);
        }
        h.Pulsar = Toggle("    Pulsar: no shadow, lensing or disk, only the jets as beams", h.Pulsar);
        DrawCompanion(h);
        DrawTexture(h);
        h.DiskLight = Slider("    Light on ships", h.DiskLight, 0, 5);
        h.DiskDrag = Toggle("    Disk drag", h.DiskDrag);
        if (h.DiskDrag)
        {
            h.DiskDragMaxAccel = Slider("      Pull at the edge (m/s^2)", h.DiskDragMaxAccel, 0, 5000, "0");
            h.DiskDragTimescale = Slider("      Grip time (s)", h.DiskDragTimescale, 0.1, 30);
            h.DiskBuffeting = Slider("      Buffeting", h.DiskBuffeting, 0, 5);
        }
        DrawDiskAdvanced(h);
    }

    /// <summary>A black hole's (or neutron star's) disk by parts: colours inside and out, brightness inside and out.</summary>
    private static void DrawDiskParts(BlackHoleDefinition h)
    {
        h.DiskTint = Slider("    Disk colours by hand (0 = physical)", h.DiskTint, 0, 1);
        if (h.DiskTint > 0)
        {
            h.InnerColor = ColorSliders("      Inner disk colour", h.InnerColor);
            h.OuterColor = ColorSliders("      Outer disk colour", h.OuterColor);
        }
        h.InnerBrightness = Slider("    Inner disk brightness", h.InnerBrightness, 0, 5);
        h.OuterBrightness = Slider("    Outer disk brightness", h.OuterBrightness, 0, 5);
    }

    /// <summary>The disk's finer settings, as it was tuned (BlackHole.shader's constants).</summary>
    private static void DrawDiskAdvanced(BlackHoleDefinition h)
    {
        _advanced = Toggle("    Advanced: the disk's gas", _advanced);
        if (!_advanced) return;
        h.DiskDensity = Slider("      Density (tuned 45)", h.DiskDensity, 0, 200);
        h.DiskOpacity = Slider("      Opacity (0.35)", h.DiskOpacity, 0, 3);
        h.Emission = Slider("      Glow of the hot gas (3.6)", h.Emission, 0, 20);
        h.CloudSize = Slider("      Lumps per horizon radius (0.45)", h.CloudSize, 0.05, 3);
        h.SelfShadow = Slider("      Self-shadowing (0.9)", h.SelfShadow, 0, 5);
        h.InnerLight = Slider("      Light from the inner disk (3)", h.InnerLight, 0, 20);
        h.StreakWinding = Slider("      Streaks: winding (5)", h.StreakWinding, -20, 20);
        h.StreakStretch = Slider("      Streaks: stretch along the orbit (30)", h.StreakStretch, 0, 200, "0");
        h.StreakLanes = Slider("      Streaks: darkness between them (0.85)", h.StreakLanes, 0, 1);
        h.ParticleSpacing = Slider("      Sparks & debris spacing (0.5; less = more)", h.ParticleSpacing, 0.05, 5);
    }

    /// <summary>A neutron star: its disk (as a black hole's), its surface, field lines, accretion columns and beams.</summary>
    private static void DrawNeutronStar(BlackHoleDefinition h)
    {
        h.StarRadius = Slider("    Star radius (horizon radii; 2.9 = 12 km, 1.4 Suns)", h.StarRadius, 1.01, 10);
        h.SurfaceTemperature = Slider("    Surface temperature (K)", h.SurfaceTemperature, 1000, 1e7, "0");
        h.PeakTemperature = Slider("    Disk temperature (K)", h.PeakTemperature, 1000, 200000, "0");
        h.ColorSaturation = Slider("    Disk colour strength", h.ColorSaturation, 0, 4);
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        h.DiskInner = Slider("    Disk inner edge = magnetosphere (horizon radii)", h.DiskInner, h.StarRadius, 100);
        h.DiskOuter = Slider("    Disk outer edge (horizon radii)", h.DiskOuter, h.DiskInner + 1, 500);
        h.DiskThickness = Slider("    Disk thickness", h.DiskThickness, 0.01, 2);
        h.Turbulence = Slider("    Puffiness / turbulence", h.Turbulence, 0, 1);
        h.DiskStreaks = Slider("    Swirling streaks seen from above", h.DiskStreaks, 0, 1);
        if (h.DiskStreaks > 0)
        {
            h.StreaksFrom = Slider("      Showing from this high above the disk (deg)", h.StreaksFrom, 0, 89, "0");
            h.StreaksFull = Slider("      All there from (deg)", h.StreaksFull, 1, 90, "0");
        }
        h.Particles = Slider("    Sparks", h.Particles, 0, 2);
        h.Debris = Slider("    Debris", h.Debris, 0, 1);
        h.RotationSpeed = Slider("    Disk spin speed", h.RotationSpeed, 0, 5);
        h.Spin = Toggle("    Reverse spin", h.Spin < 0) ? -1.0 : 1.0;
        DrawDiskParts(h);
        h.AccretionColumns = Slider("    Accretion columns onto the poles", h.AccretionColumns, 0, 5);
        if (h.AccretionColumns > 0) h.ColumnWidth = Slider("      Column width (fraction of the inner edge)", h.ColumnWidth, 0.01, 0.5, "0.000");
        h.Hotspots = Slider("    Hot spots at the poles", h.Hotspots, 0, 20);
        h.Beams = Slider("    Beams", h.Beams, 0, 5);
        if (h.Beams > 0)
        {
            h.JetLength = Slider("      Beam length (horizon radii)", h.JetLength, 5, 2000, "0");
            h.BeamWidth = Slider("      Beam widening", h.BeamWidth, 0, 0.5, "0.000");
            h.JetColor = ColorSliders("      Beam colour", h.JetColor);
        }
        DrawFieldLines(h, "    Magnetic field lines");
        DrawMagneticAxis(h);
        DrawCompanion(h);
        DrawDiskAdvanced(h);
    }

    private static readonly string[] GasNames = { "Emission", "Reflection", "Dark", "Supernova remnant", "Pillars" };
    private static readonly string[] RockCollisionNames = { "Off (drawn only)", "As drawn (hit what you see)", "Realistic (as rare as reality)" };
    private static readonly string[] TextureModeNames = { "Top-down", "Ramp" };

    /// <summary>Mass transfer from a companion star: its name, how full of its Roche lobe it is, and the stream's look.</summary>
    private static void DrawCompanion(BlackHoleDefinition h)
    {
        if (GUILayout.Button("    Star torn apart (like the reference): a star feeding its disk, the disk its stream's windings"))
        {
            // A light star (a twentieth of the hole's mass) filling its Roche lobe, drawn out to a point at L1, feeding a disk
            // round the hole: its gas is pulled off that point in a broad band and curves smoothly into the disk's edge, where
            // the tides stop it spreading (Paczynski's limit), just short of the star, then winds on in, its windings the
            // disk's rings, to a small hot core.
            h.Companion = string.Empty;
            h.CompanionMassRatio = 0.05;
            h.CompanionDistance = 167.0;
            h.StreamRing = RocheStream.TidalRadius(h.CompanionMassRatio) * h.CompanionDistance;
            h.StreamTurns = 1.6;
            h.StreamSpiralTo = 0.35;
            h.StreamCurve = 1.0;
            h.StreamShape = string.Empty;
            h.DiskOuter = h.StreamRing * h.StreamSpiralTo;   // the core it winds in to
            h.CompanionRadius = 0.0;
            h.CompanionTemperature = 9000.0;
            h.CompanionBrightness = 1.2;
            h.StreamWidth = 0.12;
            h.StreamFlatness = 3.0;
            h.StreamOpacity = 2.0;
            h.StreamFlowLines = 1.0;
            h.StreamFlowScale = 1.0;
            h.StreamFray = 0.35;
            h.StreamTaper = 0.6;
            h.StreamBridge = 0.3;
            h.StreamHeat = 0.6;
            h.CompanionPull = 1.0;
            h.StreamBrightness = 0.55;   // pale blue-white, its flow lines showing, not burnt out
            h.HotSpot = 0.3;
            h.DiskEdge = 0.0;
            h.DiskEvenness = 1.0;
            h.Turbulence = 0.0;   // smooth, not clumpy
            h.Particles = 0.0;
            h.Debris = 0.0;
            h.StreamColor = new Double3(0.62, 0.78, 1.0);
            h.CompanionSpots = 0.6;
            h.CompanionGlare = 3.0;
            // The core: white, bright, smooth.
            h.DiskTint = 1.0;
            h.InnerColor = new Double3(0.7, 0.82, 1.0);
            h.OuterColor = new Double3(0.75, 0.85, 1.0);
            h.InnerBrightness = 1.5;
            h.OuterBrightness = 1.0;
            h.Gaps = string.Empty;   // the gaps are between the stream's windings
            h.GapWidths = string.Empty;
            h.GapDepths = string.Empty;
            h.DiskStreaks = 0.0;
            h.Glow = 4.0;
            // Jets of a few twisted filaments, slow enough that both show.
            h.Jets = 0.35;
            h.JetLength = 300.0;
            h.JetWidth = 1.6;
            h.JetSpeed = 0.4;
            h.JetSpiral = 1.0;
            h.JetTurns = 1.5;
            h.JetStrands = 8.0;
            h.JetStrandSharpness = 8.0;
            h.JetAbsorb = 0.05;
            h.JetColor = new Double3(0.7, 0.82, 1.0);
            GUI.changed = true;
        }
        h.Companion = TextRow("    Companion feeding the disk: a body's name...", h.Companion);
        if (string.IsNullOrEmpty(h.Companion))
        {
            // ...or a star the shader draws, placed and sized by hand.
            h.CompanionDistance = Slider("    ...or a drawn companion this far out (0 = none)", h.CompanionDistance, 0, 500, "0.0");
            if (h.CompanionDistance <= 0) return;
            h.CompanionAngle = Slider("      Where round the disk (deg)", h.CompanionAngle, -180, 180, "0");
            h.CompanionHeight = Slider("      Above the disk (deg)", h.CompanionHeight, -90, 90, "0");
            h.CompanionRadius = Slider("      Size (0 = just fills its Roche lobe)", h.CompanionRadius, 0, 200, "0.0");
            h.CompanionTemperature = Slider("      Temperature (K)", h.CompanionTemperature, 2000, 50000, "0");
            h.CompanionBrightness = Slider("      Brightness", h.CompanionBrightness, 0, 5);
            h.CompanionSpots = Slider("      Starspots", h.CompanionSpots, 0, 1);
            h.CompanionGlare = Slider("      Glare round it", h.CompanionGlare, 0, 5);
            h.CompanionMassRatio = Slider("      Mass over the hole's", h.CompanionMassRatio, 0.01, 5, "0.000");
            h.CompanionPeriod = Slider("      Seconds per orbit (0 = stays put)", h.CompanionPeriod, 0, 600, "0");
            h.CompanionPull = Slider("      Its surface pulled towards the stream", h.CompanionPull, 0, 1);
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "      Its Roche lobe: {0:0.#} of the same units across.",
                2.0 * RocheStream.LobeRadius(h.CompanionMassRatio) * h.CompanionDistance));
            GUILayout.BeginHorizontal();
            GUILayout.Label("      The stream strikes at:");
            if (GUILayout.Button("its first ring (a disk forming)"))
            {
                h.StreamRing = RocheStream.CircularizationRadius(h.CompanionMassRatio) * h.CompanionDistance;
                GUI.changed = true;
            }
            if (GUILayout.Button("the tides' limit (a grown disk)"))
            {
                h.StreamRing = RocheStream.TidalRadius(h.CompanionMassRatio) * h.CompanionDistance;
                GUI.changed = true;
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            CelestialBody body = h.Body, companion = BlackHoles.BodyNamed(h.Companion);
            if (companion == null || body == null)
            {
                GUILayout.Label("      No body by that name.");
                return;
            }
            double a = (companion.position - body.position).magnitude;
            double q = body.gravParameter > 0.0 ? companion.gravParameter / body.gravParameter : 0.0;
            double fill = a > 0.0 ? companion.Radius / (RocheStream.LobeRadius(q) * a) : 0.0;
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "      Mass ratio {0:0.###}; the companion fills {1:0.#}% of its Roche lobe{2}.", q, fill * 100.0,
                fill >= 1.0 ? " (overflowing: a real stream)" : " (a real one would need it full)"));
        }
        h.StreamRing = Slider("      Where it strikes and winds in (0 = the disk's edge)", h.StreamRing, 0, 1000, "0.0");
        h.StreamTurns = Slider("      Stream winds on round the hole (turns)", h.StreamTurns, 0, 16, "0.0");
        h.StreamSpiralTo = Slider("      Winding in to (fraction; 0.85 = a ring at the edge)", h.StreamSpiralTo, 0, 1);
        h.StreamCurve = Slider("      How it closes in (1 = steadily, more = fast then settling, 0 = as fast as it fell)", h.StreamCurve, 0, RocheStream.MaxSpiralCurve);
        DrawStreamPad(h);
        h.StreamBrightness = Slider("      Stream brightness", h.StreamBrightness, 0, 10);
        h.StreamWidth = Slider("      Stream width (fraction of the separation)", h.StreamWidth, 0.001, 0.3, "0.000");
        h.StreamTaper = Slider("      Width at the end of its winding (fraction)", h.StreamTaper, 0.05, 3);
        h.StreamBridge = Slider("      Width leaving the star (fraction of the star)", h.StreamBridge, 0, 2);
        h.StreamFlatness = Slider("      Flat band (width over thickness)", h.StreamFlatness, 1, 20);
        h.StreamOpacity = Slider("      Opacity", h.StreamOpacity, 0, 50);
        h.StreamFlowLines = Slider("      Flow lines along it", h.StreamFlowLines, 0, 1);
        h.StreamFlowScale = Slider("      Flow lines' fineness", h.StreamFlowScale, 0.05, 20);
        h.StreamFray = Slider("      Ragged edge", h.StreamFray, 0, 2);
        h.StreamHeat = Slider("      Brightening as it falls", h.StreamHeat, 0, 10);
        h.HotSpot = Slider("      Hot spot where it strikes the disk", h.HotSpot, 0, 10);
        h.StreamColor = ColorSliders("      Stream colour", h.StreamColor);
    }

    // The drawing pad: the stroke being drawn (in the pad's units: the radius where the stream strikes is 1), and the
    // worked-out fall last looked up for its marks.
    private static readonly List<Double3> Stroke = new List<Double3>();
    private static bool _stroking;
    private static double _padQ = -1.0, _padStop = -1.0;
    private static List<Double3> _padFall;
    private const float PadSize = 260f, PadReach = 1.4f;

    /// <summary>
    /// The stream's winding drawn by hand (streamShape): a pad seen from above the binary's plane, the hole in the middle,
    /// the star off to the right, the circle the stream strikes and a square where it does; click and drag to draw, the
    /// stream easing from its fall into the line.
    /// </summary>
    private static void DrawStreamPad(BlackHoleDefinition h)
    {
        GUILayout.Label("      Draw its winding, seen from above (the hole in the middle, the star off to the right, it arrives at the square): click and drag.");
        Rect pad = GUILayoutUtility.GetRect(PadSize, PadSize, GUILayout.Width(PadSize), GUILayout.Height(PadSize));
        float scale = pad.width * 0.5f / PadReach;
        Vector2 ToPad(Double3 p) => new Vector2(pad.center.x + (float)p.X * scale, pad.center.y - (float)p.Y * scale);
        Double3 FromPad(Vector2 m) => new Double3((m.x - pad.center.x) / scale, (pad.center.y - m.y) / scale, 0.0);

        // Where the stream arrives (a drawn companion's worked-out fall; a body's is placed by the game, shown at the right).
        Double3 arrives = Double3.UnitX;
        RocheStream.StreamSpiral? spiral = null;
        if (string.IsNullOrEmpty(h.Companion) && h.CompanionDistance > 0.0)
        {
            double stop = (h.StreamRing > 0.0 ? h.StreamRing : h.DiskOuter) / h.CompanionDistance;
            if (_padFall == null || _padQ != h.CompanionMassRatio || _padStop != stop)
            {
                _padQ = h.CompanionMassRatio;
                _padStop = stop;
                _padFall = RocheStream.Path(_padQ, stop, 24);
            }
            Double3 end = _padFall[_padFall.Count - 1];
            if (end.Length > 0.0) arrives = end / end.Length;
            spiral = RocheStream.SpiralOf(_padFall, h.StreamTurns, h.StreamSpiralTo, h.StreamCurve);
        }

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && pad.Contains(e.mousePosition))
        {
            Stroke.Clear();
            Stroke.Add(FromPad(e.mousePosition));
            _stroking = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && _stroking)
        {
            Vector2 m = new Vector2(Mathf.Clamp(e.mousePosition.x, pad.xMin, pad.xMax), Mathf.Clamp(e.mousePosition.y, pad.yMin, pad.yMax));
            if ((ToPad(Stroke[Stroke.Count - 1]) - m).magnitude >= 3f) Stroke.Add(FromPad(m));
            e.Use();
        }
        else if (e.type == EventType.MouseUp && _stroking)
        {
            _stroking = false;
            if (Stroke.Count >= 2)
            {
                h.StreamShape = StreamShape.Format(StreamShape.Resample(Stroke, StreamShape.MaxPoints));
                GUI.changed = true;
            }
            e.Use();
        }

        if (e.type == EventType.Repaint)
        {
            GUI.Box(pad, GUIContent.none);
            Color was = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            for (int k = 0; k < 60; k++)
            {
                double angle = k * Math.PI / 30.0;
                Dot(ToPad(new Double3(Math.Cos(angle), Math.Sin(angle), 0.0)), 2f);
            }
            GUI.color = Color.white;
            Dot(pad.center, 6f);                       // the hole
            Dot(ToPad(arrives), 8f, true);             // where it arrives
            GUI.Label(new Rect(pad.xMax - 46f, pad.center.y - 10f, 46f, 20f), "star >");
            // The winding: being drawn, as drawn, or (faint) as worked out.
            List<Double3> line = _stroking ? Stroke : StreamShape.Parse(h.StreamShape);
            bool worked = line.Count < 2 && spiral.HasValue && spiral.Value.Turns > 0.0;
            if (worked) line = StreamShape.FromSpiral(spiral.Value, 96);
            GUI.color = worked ? new Color(0.7f, 0.85f, 1f, 0.45f) : new Color(0.75f, 0.88f, 1f, 1f);
            if (line.Count >= 2 && !worked) DrawLine(ToPad(arrives), ToPad(line[0]));
            for (int i = 1; i < line.Count; i++) DrawLine(ToPad(line[i - 1]), ToPad(line[i]));
            GUI.color = was;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear (the worked-out winding)"))
        {
            h.StreamShape = string.Empty;
            GUI.changed = true;
        }
        if (spiral.HasValue && GUILayout.Button("Start from the worked-out one"))
        {
            h.StreamShape = StreamShape.Format(StreamShape.FromSpiral(spiral.Value, StreamShape.MaxPoints));
            GUI.changed = true;
        }
        if (!string.IsNullOrEmpty(h.StreamShape) && GUILayout.Button("Smooth"))
        {
            h.StreamShape = StreamShape.Format(StreamShape.Smooth(StreamShape.Parse(h.StreamShape), 2));
            GUI.changed = true;
        }
        GUILayout.EndHorizontal();
        h.StreamShape = TextRow("      As points (x towards the star, y the way it orbits):", h.StreamShape);
    }

    /// <summary>A square dot centred on <paramref name="at"/>, or its outline.</summary>
    private static void Dot(Vector2 at, float size, bool hollow = false)
    {
        float half = size * 0.5f;
        if (!hollow)
        {
            GUI.DrawTexture(new Rect(at.x - half, at.y - half, size, size), Texture2D.whiteTexture);
            return;
        }
        GUI.DrawTexture(new Rect(at.x - half, at.y - half, size, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(at.x - half, at.y + half - 1f, size, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(at.x - half, at.y - half, 1f, size), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(at.x + half - 1f, at.y - half, 1f, size), Texture2D.whiteTexture);
    }

    /// <summary>A line of dots from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static void DrawLine(Vector2 a, Vector2 b)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 1.5f));
        for (int k = 0; k <= n; k++) Dot(Vector2.Lerp(a, b, k / (float)n), 2.5f);
    }

    /// <summary>A custom texture: any image in GameData, laid on from above or as a ramp, over colour and shape.</summary>
    private static void DrawTexture(BlackHoleDefinition h)
    {
        h.Texture = TextRow("    Texture (GameData path, no extension):", h.Texture);
        if (string.IsNullOrEmpty(h.Texture)) return;
        GUILayout.Label(BlackHoleRenderer.TextureOf(h.Texture) != null ? "      Found." : "      Not in GameData.");
        int mode = GUILayout.SelectionGrid((int)h.TextureMapping - 1, TextureModeNames, 2) + 1;
        if (mode != (int)h.TextureMapping)
        {
            h.TextureMapping = (TextureMode)mode;
            GUI.changed = true;
        }
        h.TextureStrength = Slider("      Its colours", h.TextureStrength, 0, 1);
        h.TextureShape = Slider("      Its brightness shapes it", h.TextureShape, 0, 1);
    }

    /// <summary>A star's dipole field lines: how bright, how many, how far, how thick, and their colour.</summary>
    private static void DrawFieldLines(BlackHoleDefinition h, string label)
    {
        h.FieldLines = Slider(label, h.FieldLines, 0, 5);
        if (h.FieldLines <= 0) return;
        h.FieldLineCount = Slider("      Loops round the axis", h.FieldLineCount, 1, 64, "0");
        h.FieldShells = Slider("      Nested shells", h.FieldShells, 1, 16, "0");
        h.FieldShellRatio = Slider("      Each shell bigger by", h.FieldShellRatio, 1.05, 4);
        h.FieldLineWidth = Slider("      Thickness", h.FieldLineWidth, 0.001, 0.1, "0.000");
        h.ColorA = ColorSliders("      Field line colour", h.ColorA);
    }

    /// <summary>A text box for a list of numbers or a name; marks the panel changed when edited.</summary>
    private static string TextRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(300));
        string entered = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(170));
        GUILayout.EndHorizontal();
        if (entered == (value ?? string.Empty)) return value;
        GUI.changed = true;
        return entered.Trim();
    }

    /// <summary>A protoplanetary disk, debris disk or asteroid belt: its dust, gaps, rocks and the star that lights them.</summary>
    private static void DrawDustyDisk(BlackHoleDefinition h)
    {
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        h.StarTemperature = Slider("    Star temperature (K)", h.StarTemperature, 1000, 50000, "0");
        h.LightFalloff = Slider("    Light falloff (2 = physical; lower lights the outer parts)", h.LightFalloff, 0, 3);
        h.DiskInner = Slider("    Inner edge (length units)", h.DiskInner, 0, 1000, "0.0");
        h.DiskOuter = Slider("    Outer edge (length units)", h.DiskOuter, h.DiskInner + 0.1, 5000, "0.0");
        h.DiskThickness = Slider("    Thickness (fraction of radius)", h.DiskThickness, 0.001, 0.5, "0.000");
        h.Flaring = Slider("    Flaring (thicker outward)", h.Flaring, 0, 2);
        h.Dust = Slider("    Dust", h.Dust, 0, 2, "0.000");
        h.DustColor = ColorSliders("    Dust colour (inner)", h.DustColor);
        h.OuterDustColor = ColorSliders("    Dust colour (outer edge)", h.OuterDustColor);
        h.DensityFalloff = Slider("    Thinning outward (density ~ r^-this)", h.DensityFalloff, 0, 3);
        h.EdgeSoftness = Slider("    Edge softness", h.EdgeSoftness, 0.1, 5);
        GUILayout.Label("    Rings: gaps spread over the disk, or placed one by one:");
        h.Rings = Slider("      Number of gaps (rings between them)", h.Rings, 0, 16, "0");
        h.RingDepth = Slider("      Gap depth", h.RingDepth, 0, 1);
        h.RingWidth = Slider("      Gap width (fraction of outer edge)", h.RingWidth, 0.001, 0.1, "0.000");
        h.Gaps = TextRow("      Gaps by hand: radii (fractions of outer edge)", h.Gaps);
        if (!string.IsNullOrEmpty(h.Gaps))
        {
            h.GapWidths = TextRow("      ...their widths (fractions of outer edge)", h.GapWidths);
            h.GapDepths = TextRow("      ...their depths (0..1)", h.GapDepths);
        }
        h.SpiralArms = Slider("    Spiral arms", h.SpiralArms, 0, 16, "0");
        if (h.SpiralArms > 0)
        {
            h.SpiralStrength = Slider("      Arm contrast", h.SpiralStrength, 0, 1);
            h.SpiralWinding = Slider("      Winding", h.SpiralWinding, -20, 20);
        }
        h.Clumps = Slider("    Clumpiness", h.Clumps, 0, 2);
        h.ClumpSize = Slider("    Clump size (more = smaller)", h.ClumpSize, 0.1, 10);
        h.StarGlow = Slider("    Star's glow in the gas (fraction of outer edge)", h.StarGlow, 0, 0.5, "0.000");
        h.ScatterAsymmetry = Slider("    Forward scattering of the grains (0.45 = dust)", h.ScatterAsymmetry, -0.9, 0.9);
        h.RimGlow = Slider("    Inner rim glow", h.RimGlow, 0, 10);
        if (h.RimGlow > 0) h.RimTemperature = Slider("      Rim temperature (K)", h.RimTemperature, 500, 5000, "0");
        h.Rocks = Slider("    Rocks (share of places holding one)", h.Rocks, 0, 1, "0.000");
        if (h.Rocks > 0)
        {
            h.RockSize = Slider("      Rock size", h.RockSize, 0, 5);
            h.RockSizeSpread = Slider("      Size variety", h.RockSizeSpread, 0, 3);
            h.RockSpacing = Slider("      Rock spacing (fraction of outer edge)", h.RockSpacing, 0.001, 0.05, "0.0000");
            h.RockSpread = Slider("      Spread above and below (x thickness)", h.RockSpread, 0, 5);
            h.RockAlbedo = Slider("      Rock brightness (albedo)", h.RockAlbedo, 0, 2);
            h.RockMaxAngle = Slider("      Largest a rock looks up close (deg; 0 = no limit)", h.RockMaxAngle, 0, 5);
            h.RockColor = ColorSliders("      Rock colour", h.RockColor);
            GUILayout.Label("      Rocks a craft can hit:");
            var collisions = (RockCollisions)GUILayout.SelectionGrid((int)h.RockCollisions, RockCollisionNames, 3);
            if (collisions != h.RockCollisions)
            {
                h.RockCollisions = collisions;
                GUI.changed = true;
            }
            if (h.RockCollisions == RockCollisions.AsDrawn)
            {
                h.SolidRockSpacing = Slider("        Spacing of the solid rocks (m)", h.SolidRockSpacing, 50, 20000, "0");
                h.SolidRockSize = Slider("        Largest solid rock (radius, m)", h.SolidRockSize, 1, 2000, "0");
            }
            if (h.RockCollisions != RockCollisions.Off)
            {
                h.SolidRockRange = Slider("        How far round the craft they are solid (m)", h.SolidRockRange, 1000, 50000, "0");
                GUILayout.Label(h.RockCollisions == RockCollisions.Realistic
                    ? "        As many as a real belt: rocks millions of km apart, so a hit is as rare as in reality."
                    : "        Solid rocks round your craft, drawn as they are: what you see you can hit. None in on-rails time warp.");
            }
        }
        h.RotationSpeed = Slider("    Spin speed", h.RotationSpeed, 0, 1, "0.000");
        h.Spin = Toggle("    Reverse spin", h.Spin < 0) ? -1.0 : 1.0;
        DrawTexture(h);
        DrawFieldLines(h, "    Star's magnetic field lines");
        if (h.FieldLines > 0) DrawMagneticAxis(h);
    }

    /// <summary>A nebula: its size, gas in two colours, filaments, dust lanes and shape.</summary>
    private static void DrawNebula(BlackHoleDefinition h)
    {
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        GUILayout.Label("    Gas:");
        int gas = GUILayout.SelectionGrid((int)h.GasType, GasNames, 3);
        if (gas != (int)h.GasType)
        {
            h.GasType = (NebulaGas)gas;
            GUI.changed = true;
        }
        h.DiskOuter = Slider("    Radius (length units)", h.DiskOuter, 1, 100000, "0");
        h.Dust = Slider("    Gas", h.Dust, 0, 5);
        h.ColorA = ColorSliders("    Gas colour (hydrogen)", h.ColorA);
        h.ColorB = ColorSliders("    Other gas colour (oxygen)", h.ColorB);
        h.Filaments = Slider("    Filaments", h.Filaments, 0, 5);
        h.DustLanes = Slider("    Dark dust lanes", h.DustLanes, 0, 5);
        h.CloudScale = Slider("    Billow size (more = smaller)", h.CloudScale, 0.1, 5);
        h.CloudWarp = Slider("    Swirl", h.CloudWarp, 0, 3);
        h.Shell = Slider("    Shell (a planetary nebula)", h.Shell, 0, 1);
        h.CloudStretch = Slider("    Stretch along the normal (1 = round)", h.CloudStretch, 0.2, 5);
        h.CloudRagged = Slider("    Ragged outline", h.CloudRagged, 0, 4);
        h.CloudFill = Slider("    Emptiness between clouds (lower fills it)", h.CloudFill, -1, 2);
        DrawTexture(h);
        h.StarGlow = Slider("    A star inside: its glow (fraction of the radius)", h.StarGlow, 0, 1, "0.000");
        if (h.StarGlow > 0) h.StarTemperature = Slider("      Its temperature (K)", h.StarTemperature, 1000, 100000, "0");
        h.RotationSpeed = Slider("    Turning speed", h.RotationSpeed, 0, 0.1, "0.0000");
        DrawFieldLines(h, "    Central star's magnetic field lines");
    }

    /// <summary>A galaxy: its size, bulge and bar, spiral arms, dust lanes and star-forming knots, and their colours.</summary>
    private static void DrawGalaxy(BlackHoleDefinition h)
    {
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        h.DiskOuter = Slider("    Radius (length units)", h.DiskOuter, 1, 200000, "0");
        h.DiskThickness = Slider("    Disk thickness (fraction of radius)", h.DiskThickness, 0.002, 0.3, "0.000");
        h.BulgeSize = Slider("    Bulge size (fraction of radius)", h.BulgeSize, 0.01, 1, "0.000");
        h.BulgeBrightness = Slider("    Bulge brightness", h.BulgeBrightness, 0, 5);
        h.InnerColor = ColorSliders("    Bulge colour (old stars)", h.InnerColor);
        h.BarLength = Slider("    Bar length (fraction of radius; 0 = none)", h.BarLength, 0, 0.6, "0.000");
        h.SpiralArms = Slider("    Spiral arms (0 = an elliptical or lenticular galaxy)", h.SpiralArms, 0, 8, "0");
        if (h.SpiralArms > 0)
        {
            h.SpiralWinding = Slider("      Winding (tighter = more)", h.SpiralWinding, -15, 15);
            h.SpiralStrength = Slider("      Arm contrast", h.SpiralStrength, 0, 1);
            h.ColorA = ColorSliders("      Arm colour (young stars)", h.ColorA);
        }
        h.OuterColor = ColorSliders("    Disk colour", h.OuterColor);
        h.StarClusters = Slider("    Star-forming knots", h.StarClusters, 0, 5);
        if (h.StarClusters > 0) h.ColorB = ColorSliders("      Knot colour", h.ColorB);
        h.DustLanes = Slider("    Dust lanes", h.DustLanes, 0, 5);
        h.Clumps = Slider("    Star clouds (mottling)", h.Clumps, 0, 2);
        h.CloudWarp = Slider("    Irregularity", h.CloudWarp, 0, 2);
        DrawTexture(h);
        h.RotationSpeed = Slider("    Turning speed", h.RotationSpeed, 0, 0.1, "0.0000");
    }

    /// <summary>A comet: its coma, ion tail and dust tail, and the star that drives them.</summary>
    private static void DrawComet(BlackHoleDefinition h)
    {
        h.Brightness = Slider("    Brightness", h.Brightness, 0, 50);
        h.Activity = Slider("    Activity (0 = dormant)", h.Activity, 0, 5);
        h.ComaSize = Slider("    Coma size (length units)", h.ComaSize, 0.01, 50);
        h.ComaColor = ColorSliders("    Coma colour", h.ComaColor);
        h.ComaBrightness = Slider("    Coma brightness", h.ComaBrightness, 0, 5);
        h.NucleusBrightness = Slider("    Nucleus brightness", h.NucleusBrightness, 0, 5);
        h.IonTailLength = Slider("    Ion tail length (length units)", h.IonTailLength, 1, 1000, "0");
        h.IonTailWidth = Slider("    Ion tail width", h.IonTailWidth, 0.1, 5);
        h.IonBrightness = Slider("    Ion tail brightness", h.IonBrightness, 0, 5);
        h.Streamers = Slider("    Ion tail streamers & knots", h.Streamers, 0, 3);
        h.ColorA = ColorSliders("    Ion tail colour", h.ColorA);
        h.DustTailLength = Slider("    Dust tail length (length units)", h.DustTailLength, 1, 1000, "0");
        h.DustTailCurve = Slider("    Dust tail curve", h.DustTailCurve, 0, 3);
        h.DustTailWidth = Slider("    Dust tail spread", h.DustTailWidth, 0, 5);
        h.DustBrightness = Slider("    Dust tail brightness", h.DustBrightness, 0, 5);
        h.Striae = Slider("    Dust tail bands (striae)", h.Striae, 0, 3);
        h.ColorB = ColorSliders("    Dust tail colour", h.ColorB);
        h.LightSource = TextRow("    Driven by (a star; empty = the one it orbits):", h.LightSource);
    }

    /// <summary>A star's magnetic axis: along the normal, or swept round it like a pulsar's beams.</summary>
    private static void DrawMagneticAxis(BlackHoleDefinition h)
    {
        h.BeamPeriod = Slider("    Magnetic axis: seconds per sweep (0 = along the normal)", h.BeamPeriod, 0, 30);
        if (h.BeamPeriod > 0) h.BeamTilt = Slider("    Magnetic axis: tilt from the spin axis (deg)", h.BeamTilt, 0, 90, "0");
    }

    /// <summary>Size unit, orientation, quality and extent: every kind has them.</summary>
    private static void DrawPlacement(BlackHoleDefinition h)
    {
        if (!h.IsBlackHole)
        {
            CelestialBody body = h.Body;
            string unit = body != null ? $" = {FormatLength(h.HorizonMeters(body))}" : string.Empty;
            h.HorizonRadiusMeters = Slider($"    Length unit (m; 0 = automatic){unit}", h.HorizonRadiusMeters, 0, 1e11, "0");
        }
        // The normal's direction (configs may give it any length, e.g. 50, -125, 1); only a change of one sets it.
        Vector3d n = h.Normal.sqrMagnitude > 1e-24 ? h.Normal.normalized : Vector3d.up;
        double nx = Slider("    Orientation: normal X", n.x, -1, 1, "0.000");
        double ny = Slider("    Orientation: normal Y", n.y, -1, 1, "0.000");
        double nz = Slider("    Orientation: normal Z", n.z, -1, 1, "0.000");
        if ((nx != n.x || ny != n.y || nz != n.z) && nx * nx + ny * ny + nz * nz > 1e-12) h.Normal = new Vector3d(nx, ny, nz);
        h.Steps = (int)Math.Max(1.0, Math.Min(Slider("    Quality (ray steps; lower = faster)", h.Steps, 16, 5000, "0"), 1000000.0));
        h.BoundRadius = Slider("    Drawn out to (units; 0 = automatic)", h.BoundRadius, 0, 5000, "0");
    }

    private static string FormatLength(double meters) =>
        meters >= 1.495978707e10 ? (meters / 1.495978707e11).ToString("0.###", CultureInfo.InvariantCulture) + " AU"
        : meters >= 1e4 ? (meters / 1e3).ToString("#,0", CultureInfo.InvariantCulture) + " km"
        : meters.ToString("0.#", CultureInfo.InvariantCulture) + " m";

    /// <summary>Three sliders (red, green, blue) for a colour; 1 is full strength, more is brighter.</summary>
    private static Double3 ColorSliders(string label, Double3 c)
    {
        GUILayout.Label(label + ":");
        double r = Slider(label + "  red", c.X, 0, 2);
        double g = Slider(label + "  green", c.Y, 0, 2);
        double b = Slider(label + "  blue", c.Z, 0, 2);
        return new Double3(r, g, b);
    }

    /// <summary>The procedural rules: objects placed round every star of any planet pack (ProceduralObjects).</summary>
    private void DrawProcedural()
    {
        SettingsValues r = BlackHoles.ProceduralRules;
        GUILayout.Label("  Procedural objects: belts, Kuiper belts and comet tails placed round every star by rule:");
        RuleToggle("    On", ProceduralObjects.Enabled, r);
        if (!r.Get(ProceduralObjects.Enabled)) return;
        int seed = r.Get(ProceduralObjects.Seed);
        double newSeed = Slider("    Seed (varies them all)", seed, 0, 1000, "0");
        if ((int)newSeed != seed) BlackHoles.SetProceduralRule(ProceduralObjects.Seed.Key, ((int)newSeed).ToString(CultureInfo.InvariantCulture));
        RuleToggle("    Asteroid belts in the widest gap", ProceduralObjects.Belts, r);
        RuleSlider("      Share of stars with one", ProceduralObjects.BeltChance, r, 0, 1);
        RuleSlider("      Least gap (ratio of orbits)", ProceduralObjects.BeltMinGap, r, 1.1, 5);
        RuleSlider("      Planets are at least (radius, m)", ProceduralObjects.PlanetMinRadius, r, 0, 1000000, "0");
        RuleToggle("    Kuiper belts beyond the outermost planet", ProceduralObjects.OuterDisks, r);
        RuleSlider("      Share of stars with one", ProceduralObjects.OuterDiskChance, r, 0, 1);
        RuleToggle("    Comet tails", ProceduralObjects.Comets, r);
        RuleSlider("      On bodies up to (radius, m)", ProceduralObjects.CometMaxRadius, r, 0, 200000, "0");
        RuleSlider("      With eccentricity at least", ProceduralObjects.CometMinEccentricity, r, 0, 1);
        RuleSlider("    Variety", ProceduralObjects.Variety, r, 0, 1);
        RuleSlider("    Stars' temperature (K)", ProceduralObjects.StarTemperature, r, 1000, 40000, "0");
        int count = BlackHoles.Definitions.FindAll(d => d.Procedural).Count;
        GUILayout.BeginHorizontal();
        GUILayout.Label($"    {count} placed.", GUILayout.Width(200));
        if (GUILayout.Button("Place them again")) BlackHoles.PlanProcedural();
        GUILayout.EndHorizontal();
    }

    private static void RuleToggle(string label, BoolSetting setting, SettingsValues r)
    {
        bool value = r.Get(setting);
        if (Toggle(label, value) != value) BlackHoles.SetProceduralRule(setting.Key, value ? "False" : "True");
    }

    private static void RuleSlider(string label, DoubleSetting setting, SettingsValues r, double min, double max, string format = "0.00")
    {
        double value = r.Get(setting);
        double changed = Slider(label, value, min, max, format);
        if (changed != value)
            BlackHoles.SetProceduralRule(setting.Key, Math.Min(Math.Max(changed, setting.Min), setting.Max).ToString("R", CultureInfo.InvariantCulture));
    }

    /// <summary>Adds an object of any kind round any body (kept in user.cfg once saved).</summary>
    private void DrawAddObject()
    {
        var bodies = FlightGlobals.fetch != null ? FlightGlobals.Bodies : null;
        if (bodies == null || bodies.Count == 0) return;
        GUILayout.Label("  Add an object round a body (no vessels; drawn by the shader):");
        _addBody = Math.Min(Math.Max(_addBody, 0), bodies.Count - 1);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("<", GUILayout.Width(30))) _addBody = (_addBody + bodies.Count - 1) % bodies.Count;
        CelestialBody pick = bodies[_addBody];
        GUILayout.Label("    " + (pick != null ? pick.bodyName : "?"), GUILayout.Width(200));
        if (GUILayout.Button(">", GUILayout.Width(30))) _addBody = (_addBody + 1) % bodies.Count;
        GUILayout.EndHorizontal();
        _addKind = GUILayout.SelectionGrid(_addKind, SpaceObjects.KindNames, 3);
        if (pick != null && GUILayout.Button($"Add a {SpaceObjects.KindName((SpaceObjectKind)_addKind)} round {pick.bodyName}"))
        {
            BlackHoles.Add(pick.bodyName, (SpaceObjectKind)_addKind);
            _openHole = BlackHoles.Definitions.Count - 1;
            _status = $"Added; set it up below its name and Save to keep it";
        }
    }

    private static void Header(string text) => Gui.Header(text);

    private static bool Toggle(string label, bool value) => Gui.Toggle(label, value);

    private static double Slider(string label, double value, double min, double max, string format = "0.00") => Gui.Slider(label, value, min, max, format);
}
