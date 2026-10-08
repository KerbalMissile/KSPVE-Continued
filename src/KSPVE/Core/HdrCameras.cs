using System.Collections.Generic;
using UnityEngine;

namespace KSPVE.Core;

/// <summary>
/// Switches KSP's scene cameras to HDR (floating-point) rendering so values above 1 (the sun,
/// plumes, sunlit clouds) survive until KSPVE's tonemapping instead of being clipped. KSP ships
/// these cameras LDR (P0a census). Each camera's original setting is restored when disabled.
/// </summary>
internal sealed class HdrCameras
{
    private static readonly HashSet<string> SceneCameraNames = new HashSet<string>
    {
        "Camera ScaledSpace",
        "Camera 00",
        "Camera 01" // only present in non-unified camera mode
    };

    private readonly Dictionary<Camera, bool> _original = new Dictionary<Camera, bool>();
    private Camera[] _cameras = new Camera[32];
    private GameScenes _lastScene;
    private int _lastLoggedCount = -1;

    public bool Enabled { get; set; }

    /// <summary>Scene cameras rendering in HDR after the last <see cref="Apply"/>.</summary>
    public int HdrCameraCount { get; private set; }

    /// <summary>Called every frame before rendering; cheap and allocation-free.</summary>
    public void Apply()
    {
        if (HighLogic.LoadedScene != _lastScene)
        {
            _lastScene = HighLogic.LoadedScene;
            ForgetDestroyedCameras();
        }

        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);

        int hdr = 0;
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || !SceneCameraNames.Contains(cam.name)) continue;
            if (Enabled)
            {
                if (!_original.ContainsKey(cam)) _original[cam] = cam.allowHDR;
                if (!cam.allowHDR) cam.allowHDR = true;
            }
            else if (_original.TryGetValue(cam, out bool original) && cam.allowHDR != original)
            {
                cam.allowHDR = original;
            }
            if (cam.allowHDR) hdr++;
        }
        HdrCameraCount = hdr;

        if (hdr != _lastLoggedCount)
        {
            _lastLoggedCount = hdr;
            Log.Info($"HDR scene cameras: {hdr} (HDR {(Enabled ? "enabled" : "disabled")}, scene {HighLogic.LoadedScene}).");
        }
    }

    private void ForgetDestroyedCameras()
    {
        var dead = new List<Camera>();
        foreach (Camera cam in _original.Keys)
        {
            if (cam == null) dead.Add(cam);
        }
        foreach (Camera cam in dead) _original.Remove(cam);
    }
}
