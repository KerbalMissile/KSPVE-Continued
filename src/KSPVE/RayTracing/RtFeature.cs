using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.RayTracing;

/// <summary>
/// Ray-traced global illumination: light bouncing off craft, terrain and buildings (and, from orbit, off the planet),
/// traced through the real 3D scene in KSPVE's own BVHs on DirectX 11 (no screen-space guessing), accumulated and
/// filtered, replacing KSP's flat ambient light. Lives on "Camera 00".
/// </summary>
internal sealed class RtFeature : Feature
{
    private const string LocalCameraName = "Camera 00";

    private readonly RtSettings _settings = new RtSettings();
    private Camera[] _cameras = new Camera[8];
    private RtGiRenderer _renderer;

    public override string Name => "Ray tracing";
    public override int Order => 50;
    public override string SettingsTitle => "Ray-traced global illumination";

    public override void Load(List<ConfigIssue> issues)
    {
        _settings.From(KspConfig.LoadUserNodes().Find(n => n.Name == RtSettings.UserNodeName));
    }

    public override void Update(bool allowed)
    {
        bool wanted = allowed && _settings.Enabled;
        if (_renderer == null)
        {
            if (!wanted) return;
            Camera camera = FindLocalCamera();
            if (camera == null) return;
            _renderer = camera.GetComponent<RtGiRenderer>() ?? camera.gameObject.AddComponent<RtGiRenderer>();
            _renderer.Settings = _settings;
            Log.Info("Ray-traced lighting attached to " + camera.name + ".");
        }
        if (_renderer.enabled != wanted) _renderer.enabled = wanted;
    }

    private Camera FindLocalCamera()
    {
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
            if (_cameras[i] != null && _cameras[i].name == LocalCameraName) return _cameras[i];
        return null;
    }

    public override void DrawSettings()
    {
        RtSettings s = _settings;
        s.Enabled = Gui.Toggle("Ray-traced bounce light (experimental; DirectX 11, world space)", s.Enabled);
        if (!s.Enabled) return;
        s.Resolution = Gui.Slider("  Trace resolution (share of the screen)", s.Resolution, 0.25, 1.0);
        s.Reach = Gui.Slider("  Ray reach (m)", s.Reach, 100, 10000, "0");
        s.SunShadows = Gui.Toggle("  Shadow rays where the light lands (realistic)", s.SunShadows);
        s.PlanetBounce = Gui.Toggle("  Light off the planet beyond the scene (ground bounce, planetshine)", s.PlanetBounce);
        s.ReplaceAmbient = Gui.Toggle("  Replace KSP's flat ambient light (realistic)", s.ReplaceAmbient);
        s.History = Gui.IntSlider("  Frames averaged (smoother vs quicker)", s.History, 1, 64);
        s.FilterPasses = Gui.IntSlider("  Denoise passes", s.FilterPasses, 0, 6);
        s.Strength = Gui.Slider("  Strength (display; 1 = as traced)", s.Strength, 0, 4);
        s.Debug = Gui.Choice("  Show", s.Debug, 2);
        if (_renderer != null && !string.IsNullOrEmpty(_renderer.Status)) Gui.Note("  " + _renderer.Status);
    }

    public override CfgNode UserNode() => _settings.ToCfg();

    public override void ResetToDefaults()
    {
        var defaults = new RtSettings();
        _settings.From(defaults.ToCfg());
        _settings.Debug = RtSettings.DebugView.Off;
    }

    public override void Shutdown()
    {
        RtGiRenderer.Scene.Dispose();
    }
}
