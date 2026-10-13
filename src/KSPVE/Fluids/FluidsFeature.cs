using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Fluids;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Fluids;

/// <summary>
/// Fluid-dynamics atmospheres for brown dwarfs, and optionally gas giants and hot Jupiters: a barotropic vorticity model on
/// the rotating sphere makes the zonal jets, bands and vortices (<see cref="BarotropicModel"/>), cloud decks of the right
/// species ride on it and clear or thicken with its temperature anomalies (<see cref="TracerGrid"/>), and the shader shows
/// each deck at its own temperature as a blackbody seen through the clouds above it. It is spun up once on a worker thread and
/// then follows the game clock; see <see cref="FluidWorker"/> for how the work is capped.
/// </summary>
internal sealed class FluidsFeature : Feature
{
    private const string ScaledCameraName = "Camera ScaledSpace";
    private static Camera[] _cameras = new Camera[32];

    private float _readoutAt = -100f;
    private string[] _readout = new string[0];

    public override string Name => "Fluids";
    public override int Order => 60;
    public override string SettingsTitle => "Brown dwarfs and fluid atmospheres";

    public override void Load(List<ConfigIssue> issues)
    {
        FluidWorld.Load(issues);
    }

    public override void Update(bool allowed)
    {
        bool enabled = allowed && FluidSettings.Enabled;
        if (Planetarium.fetch != null && FluidSettings.Enabled) FluidWorld.Update(Planetarium.GetUniversalTime());
        if (enabled) UploadTextures();
        AttachRenderer(enabled);
    }

    public override void Shutdown()
    {
        FluidWorld.Shutdown();
    }

    // ---------------------------------------------------------------- textures

    private static void UploadTextures()
    {
        foreach (FluidBody fb in FluidWorld.All)
        {
            if (fb.State != FluidState.Ready || fb.Sim == null) continue;
            AtmosphereSimulation sim = fb.Sim;
            try
            {
                if (fb.FieldTexture == null)
                {
                    int w = sim.Tracers.NLon, h = sim.Tracers.NLat;
                    fb.FieldTexture = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true)
                    {
                        name = "KSPVE fluid fields " + fb.Body.bodyName,
                        wrapModeU = TextureWrapMode.Repeat,
                        wrapModeV = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    fb.UploadFloats = new float[w * h * 4];
                    fb.UploadBytes = new byte[w * h * 16];
                    fb.EmissionTexture = LutTexture(sim.Lut, "KSPVE fluid emission " + fb.Body.bodyName);
                    fb.EmissionInfraredTexture = LutTexture(sim.LutThermalInfrared, "KSPVE fluid emission (infrared) " + fb.Body.bodyName);
                    fb.Uploaded = -1;
                }
                if (fb.Version != fb.Uploaded && Time.realtimeSinceStartup - fb.LastUpload > 0.2f)
                {
                    lock (fb.Lock)
                    {
                        Array.Copy(fb.Snapshot, fb.UploadFloats, fb.UploadFloats.Length);
                        fb.Uploaded = fb.Version;
                    }
                    Buffer.BlockCopy(fb.UploadFloats, 0, fb.UploadBytes, 0, fb.UploadBytes.Length);
                    fb.FieldTexture.LoadRawTextureData(fb.UploadBytes);
                    fb.FieldTexture.Apply(false);
                    fb.LastUpload = Time.realtimeSinceStartup;
                }
            }
            catch (Exception ex)
            {
                fb.State = FluidState.Failed;
                fb.Status = "failed: " + ex.Message;
                Log.Exception("Uploading the fluid fields of " + fb.Body.bodyName + " failed", ex);
            }
        }
    }

    private static Texture2D LutTexture(EmissionLut lut, string name)
    {
        var tex = new Texture2D(EmissionLut.Columns, lut.Rows, TextureFormat.RGBAFloat, false, true)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var bytes = new byte[lut.Data.Length * 4];
        Buffer.BlockCopy(lut.Data, 0, bytes, 0, bytes.Length);
        tex.LoadRawTextureData(bytes);
        tex.Apply(false);
        return tex;
    }

    // ---------------------------------------------------------------- the renderer

    private static void AttachRenderer(bool enabled)
    {
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || cam.name != ScaledCameraName) continue;
            var renderer = cam.GetComponent<FluidRenderer>();
            if (renderer == null)
            {
                if (!enabled) return;
                renderer = cam.gameObject.AddComponent<FluidRenderer>();
                Log.Info("Fluid atmosphere renderer attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
            return;
        }
    }

    // ---------------------------------------------------------------- settings

    public override CfgNode UserNode() => FluidSettings.ToNode();

    public override void ResetToDefaults()
    {
        FluidSettings.Reset();
        if (GameDatabase.Instance != null)
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(FluidWorld.NodeName)) FluidSettings.Read(KspConfig.ToCfg(node));
        FluidWorld.Rescan();
    }

    private static readonly string[] ViewNames = { "Eye (realistic)", "Thermal infrared (5 micron, false colour)" };

    public override void DrawSettings()
    {
        FluidSettings.Enabled = Gui.Toggle("Fluid-dynamics atmospheres", FluidSettings.Enabled);
        if (!FluidSettings.Enabled) return;

        // The boost is a display setting: a 1500 K L dwarf is some four millionths as bright as the Sun's disc in visible light.
        double stops = Math.Log(Math.Max(FluidSettings.Boost, 1e-3), 2.0);
        double newStops = Gui.Slider("Brown dwarf brightness boost (stops; 0 = realistic)", stops, 0.0, 24.0, "0.0");
        if (newStops != stops) FluidSettings.Boost = Math.Pow(2.0, newStops);
        Gui.Note($"Multiplies the radiance of brown dwarfs and hot planets by {FluidSettings.Boost:0.###}. At 1 they are as dark as they really are in visible light.");

        int view = Gui.Choice("View", (int)FluidSettings.View, ViewNames, 2);
        if (view >= 0 && view < ViewNames.Length) FluidSettings.View = (FluidView)view;
        Gui.Note(FluidSettings.View == FluidView.Eye
            ? "The visible light of each cloud deck through the sodium and potassium absorption above it: deep red for L dwarfs, dull and dark for T dwarfs."
            : "A 5 micrometre thermal camera, as Gemini and Juno image Jupiter: the hot deep layers glow yellow-white through gaps and thin spots, thick clouds are dark. False colour; not what the eye sees.");

        bool dwarfs = Gui.Toggle("Brown dwarfs (stars with kind = browndwarf or under 2500 K)", FluidSettings.BrownDwarfs);
        bool giants = Gui.Toggle("Gas giants without EVE clouds", FluidSettings.GasGiants);
        bool hot = Gui.Toggle("Hot Jupiters named in KSPVE_FLUIDS", FluidSettings.HotJupiters);
        if (dwarfs != FluidSettings.BrownDwarfs || giants != FluidSettings.GasGiants || hot != FluidSettings.HotJupiters)
        {
            FluidSettings.BrownDwarfs = dwarfs;
            FluidSettings.GasGiants = giants;
            FluidSettings.HotJupiters = hot;
            FluidWorld.Rescan();
        }
        Gui.Note("A body gets its atmosphere when the game loads it (a few seconds' spin-up in the background); changes here apply to bodies not yet set up.");

        FluidSettings.WorkShare = Gui.Slider("Share of a core the simulation may use", FluidSettings.WorkShare, 0.05, 1.0);
        FluidSettings.MaxLagDays = Gui.Slider("Longest the weather may lag the game clock (days)", FluidSettings.MaxLagDays, 0.5, 200.0, "0.0");
        Gui.Note("The weather runs at the real rate. Under time warp it takes as many steps as the share above allows, up to 6 per slice; if it falls further behind than the lag it jumps ahead.");

        Gui.Header("The atmospheres now");
        if (Time.realtimeSinceStartup - _readoutAt > 0.5f)
        {
            _readoutAt = Time.realtimeSinceStartup;
            try { _readout = Readout(); }
            catch (Exception ex) { _readout = new[] { "Could not read the atmospheres: " + ex.Message }; }
        }
        foreach (string line in _readout) GUILayout.Label(line);
    }

    private static string[] Readout()
    {
        var lines = new List<string>();
        foreach (FluidBody fb in FluidWorld.All)
        {
            if (fb.Body == null) continue;
            string text = $"{fb.Body.bodyName}: {fb.Profile.Kind} {fb.Profile.Temperature:0} K, {fb.RotationPeriod / 3600.0:0.0} h, {fb.Status}";
            if (fb.State == FluidState.Ready && fb.Sim != null)
                text += $", {fb.JetCount} eastward jets, weather {fb.Lag / 86400.0:0.0} days behind";
            lines.Add(text);
        }
        if (lines.Count == 0) lines.Add("No body has an atmosphere of this kind (name one in KSPVE_FLUIDS, or mark a star kind = browndwarf in KSPVE_STARS).");
        return lines.ToArray();
    }
}
