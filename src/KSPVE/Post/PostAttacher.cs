using KSPVE.Core;
using KSPVE.Pure.Post;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Post;

/// <summary>
/// Keeps a <see cref="PostStack"/> on the last scene camera each frame: "Camera 00" in flight and at
/// the space center, "Camera ScaledSpace" when the local camera is off (map view, tracking station).
/// Also owns the contact-shadow command buffer on the sun light's screen-space shadow mask.
/// </summary>
internal static class PostAttacher
{
    private const string LocalCameraName = "Camera 00";
    private const string ScaledCameraName = "Camera ScaledSpace";
    private const string SunLightName = "SunLight";

    private static Camera[] _cameras = new Camera[32];
    private static CommandBuffer _contactBuffer;
    private static Material _contactMaterial;
    private static Light _contactLight;
    private static int _nextSunSearchFrame;

    public static Light SunLight { get; private set; }

    public static void Update(PostProfile profile, bool allowed)
    {
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);

        Camera local = null, scaled = null;
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null) continue;
            if (cam.name == LocalCameraName) local = cam;
            else if (cam.name == ScaledCameraName) scaled = cam;
        }

        bool on = allowed && profile.Enabled;
        Configure(local, on, isLocal: true);
        Configure(scaled, on && local == null, isLocal: false);
        UpdateContactShadows(on && profile.ContactShadows && local != null);
    }

    private static void Configure(Camera cam, bool enabled, bool isLocal)
    {
        if (cam == null) return;
        var stack = cam.GetComponent<PostStack>();
        if (stack == null)
        {
            if (!enabled) return;
            stack = cam.gameObject.AddComponent<PostStack>();
            Log.Info($"Post-processing attached to {cam.name}.");
        }
        stack.IsLocal = isLocal;
        if (stack.enabled != enabled) stack.enabled = enabled;
    }

    private static void UpdateContactShadows(bool wanted)
    {
        if (SunLight == null && Time.frameCount >= _nextSunSearchFrame)
        {
            _nextSunSearchFrame = Time.frameCount + 120;
            SunLight = FindSun();
        }

        if (!wanted || SunLight == null)
        {
            DetachContactShadows();
            return;
        }
        if (_contactLight == SunLight) return;

        DetachContactShadows();
        if (_contactMaterial == null) _contactMaterial = ShaderLibrary.CreateMaterial("Hidden/KSPVE/ContactShadows");
        if (_contactMaterial == null) return;
        if (_contactBuffer == null)
        {
            _contactBuffer = new CommandBuffer { name = "KSPVE Contact Shadows" };
            _contactBuffer.Blit(Texture2D.whiteTexture, BuiltinRenderTextureType.CurrentActive, _contactMaterial, 0);
        }
        SunLight.AddCommandBuffer(LightEvent.AfterScreenspaceMask, _contactBuffer);
        _contactLight = SunLight;
        Log.Info("Contact shadows attached to " + SunLight.name + ".");
    }

    private static void DetachContactShadows()
    {
        if (_contactLight != null && _contactBuffer != null)
            _contactLight.RemoveCommandBuffer(LightEvent.AfterScreenspaceMask, _contactBuffer);
        _contactLight = null;
    }

    private static Light FindSun()
    {
        foreach (Light light in Object.FindObjectsOfType<Light>())
        {
            if (light.name == SunLightName && light.type == LightType.Directional) return light;
        }
        return null;
    }
}
