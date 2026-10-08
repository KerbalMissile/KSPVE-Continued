using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSPVE.Pure;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Dev;

/// <summary>
/// Records every rendering hook visible at a moment in time: cameras (including disabled ones that
/// mods render manually), the command buffers attached to each camera and light event in execution
/// order, camera components with image-effect callbacks, reflection probes and the built-in shader
/// overrides in GraphicsSettings. Answers spikes S1 (injection order) and S2 (deferred reflections).
/// </summary>
internal static class HookCensus
{
    private static readonly BuiltinShaderType[] BuiltinShaders =
    {
        BuiltinShaderType.DeferredShading,
        BuiltinShaderType.DeferredReflections,
        BuiltinShaderType.LegacyDeferredLighting,
        BuiltinShaderType.ScreenSpaceShadows,
        BuiltinShaderType.DepthNormals,
        BuiltinShaderType.MotionVectors,
        BuiltinShaderType.LightHalo,
        BuiltinShaderType.LensFlare
    };

    public static void Write(JsonWriter w, string label)
    {
        w.BeginObject()
            .Property("label", label)
            .Property("scene", HighLogic.LoadedScene.ToString())
            .Property("frame", Time.frameCount);
        WriteGraphicsSettings(w);
        WriteQuality(w);
        WriteRenderSettings(w);

        w.Name("cameras").BeginArray();
        foreach (Camera cam in SceneObjects<Camera>().OrderBy(c => c.depth).ThenBy(c => c.name)) WriteCamera(w, cam);
        w.EndArray();

        w.Name("lights").BeginArray();
        foreach (Light light in SceneObjects<Light>().OrderBy(l => l.name)) WriteLight(w, light);
        w.EndArray();

        w.Name("reflectionProbes").BeginArray();
        foreach (ReflectionProbe probe in SceneObjects<ReflectionProbe>().OrderBy(p => p.name)) WriteProbe(w, probe);
        w.EndArray();

        w.EndObject();
    }

    /// <summary>Scene instances including inactive ones; excludes prefab assets, which have no valid scene.</summary>
    private static IEnumerable<T> SceneObjects<T>() where T : Component
    {
        return Resources.FindObjectsOfTypeAll<T>().Where(c => c != null && c.gameObject.scene.IsValid());
    }

    private static void WriteGraphicsSettings(JsonWriter w)
    {
        w.Name("graphicsSettings").BeginObject()
            .Property("renderPipelineAsset", GraphicsSettings.renderPipelineAsset != null ? GraphicsSettings.renderPipelineAsset.name : null)
            .Property("graphicsDeviceVersion", SystemInfo.graphicsDeviceVersion)
            .Property("usesReversedZBuffer", SystemInfo.usesReversedZBuffer)
            .Property("activeColorSpace", QualitySettings.activeColorSpace.ToString());
        w.Name("builtinShaders").BeginArray();
        foreach (BuiltinShaderType type in BuiltinShaders)
        {
            Shader custom = GraphicsSettings.GetCustomShader(type);
            w.BeginObject()
                .Property("type", type.ToString())
                .Property("mode", GraphicsSettings.GetShaderMode(type).ToString())
                .Property("customShader", custom != null ? custom.name : null)
                .EndObject();
        }
        w.EndArray();
        w.EndObject();
    }

    private static void WriteQuality(JsonWriter w)
    {
        Vector3 split = QualitySettings.shadowCascade4Split;
        w.Name("quality").BeginObject()
            .Property("level", QualitySettings.names[QualitySettings.GetQualityLevel()])
            .Property("shadows", QualitySettings.shadows.ToString())
            .Property("shadowResolution", QualitySettings.shadowResolution.ToString())
            .Property("shadowProjection", QualitySettings.shadowProjection.ToString())
            .Property("shadowCascades", QualitySettings.shadowCascades)
            .Property("shadowDistance", QualitySettings.shadowDistance)
            .Property("shadowCascade4Split", $"{split.x},{split.y},{split.z}")
            .Property("antiAliasing", QualitySettings.antiAliasing)
            .Property("pixelLightCount", QualitySettings.pixelLightCount)
            .Property("vSyncCount", QualitySettings.vSyncCount)
            .Property("anisotropicFiltering", QualitySettings.anisotropicFiltering.ToString())
            .Property("lodBias", QualitySettings.lodBias)
            .Property("targetFrameRate", Application.targetFrameRate)
            .EndObject();
    }

    private static void WriteRenderSettings(JsonWriter w)
    {
        w.Name("renderSettings").BeginObject()
            .Property("ambientMode", RenderSettings.ambientMode.ToString())
            .Property("ambientLight", RenderSettings.ambientLight.ToString())
            .Property("ambientIntensity", RenderSettings.ambientIntensity)
            .Property("reflectionIntensity", RenderSettings.reflectionIntensity)
            .Property("defaultReflectionMode", RenderSettings.defaultReflectionMode.ToString())
            .Property("fog", RenderSettings.fog)
            .Property("skybox", RenderSettings.skybox != null ? RenderSettings.skybox.name : null)
            .EndObject();
    }

    private static void WriteCamera(JsonWriter w, Camera cam)
    {
        RenderTexture rt = cam.targetTexture;
        w.BeginObject()
            .Property("name", cam.name)
            .Property("path", HierarchyPath(cam.transform))
            .Property("enabled", cam.enabled)
            .Property("activeInHierarchy", cam.gameObject.activeInHierarchy)
            .Property("depth", cam.depth)
            .Property("renderingPath", cam.renderingPath.ToString())
            .Property("actualRenderingPath", cam.actualRenderingPath.ToString())
            .Property("allowHDR", cam.allowHDR)
            .Property("allowMSAA", cam.allowMSAA)
            .Property("clearFlags", cam.clearFlags.ToString())
            .Property("near", cam.nearClipPlane)
            .Property("far", cam.farClipPlane)
            .Property("fieldOfView", cam.fieldOfView)
            .Property("cullingMask", cam.cullingMask)
            .Property("depthTextureMode", cam.depthTextureMode.ToString())
            .Property("forceIntoRenderTexture", cam.forceIntoRenderTexture)
            .Property("targetTexture", rt != null ? $"{rt.name} {rt.width}x{rt.height} {rt.format}" : null);

        w.Name("commandBuffers").BeginArray();
        foreach (CameraEvent evt in Enum.GetValues(typeof(CameraEvent)).Cast<CameraEvent>().Distinct())
        {
            CommandBuffer[] buffers = cam.GetCommandBuffers(evt);
            for (int i = 0; i < buffers.Length; i++)
            {
                w.BeginObject()
                    .Property("event", evt.ToString())
                    .Property("order", i)
                    .Property("name", buffers[i].name)
                    .Property("sizeInBytes", buffers[i].sizeInBytes)
                    .EndObject();
            }
        }
        w.EndArray();

        WriteComponents(w, cam.gameObject);
        w.EndObject();
    }

    private static void WriteComponents(JsonWriter w, GameObject go)
    {
        w.Name("components").BeginArray();
        foreach (Component c in go.GetComponents<Component>())
        {
            if (c == null)
            {
                w.BeginObject().Property("type", "(missing script)").EndObject();
                continue;
            }
            Type t = c.GetType();
            w.BeginObject()
                .Property("type", t.FullName)
                .Property("assembly", t.Assembly.GetName().Name)
                .Property("enabled", !(c is Behaviour b) || b.enabled)
                .Property("onRenderImage", HasMethod(t, "OnRenderImage"))
                .Property("onPreRender", HasMethod(t, "OnPreRender"))
                .Property("onPostRender", HasMethod(t, "OnPostRender"))
                .EndObject();
        }
        w.EndArray();
    }

    private static void WriteLight(JsonWriter w, Light light)
    {
        w.BeginObject()
            .Property("name", light.name)
            .Property("path", HierarchyPath(light.transform))
            .Property("enabled", light.enabled && light.gameObject.activeInHierarchy)
            .Property("type", light.type.ToString())
            .Property("intensity", light.intensity)
            .Property("color", light.color.ToString())
            .Property("shadows", light.shadows.ToString())
            .Property("cullingMask", light.cullingMask)
            .Property("renderMode", light.renderMode.ToString());
        w.Name("commandBuffers").BeginArray();
        foreach (LightEvent evt in Enum.GetValues(typeof(LightEvent)).Cast<LightEvent>().Distinct())
        {
            CommandBuffer[] buffers = light.GetCommandBuffers(evt);
            for (int i = 0; i < buffers.Length; i++)
            {
                w.BeginObject()
                    .Property("event", evt.ToString())
                    .Property("order", i)
                    .Property("name", buffers[i].name)
                    .EndObject();
            }
        }
        w.EndArray();
        w.EndObject();
    }

    private static void WriteProbe(JsonWriter w, ReflectionProbe probe)
    {
        w.BeginObject()
            .Property("name", probe.name)
            .Property("path", HierarchyPath(probe.transform))
            .Property("enabled", probe.enabled && probe.gameObject.activeInHierarchy)
            .Property("mode", probe.mode.ToString())
            .Property("refreshMode", probe.refreshMode.ToString())
            .Property("timeSlicingMode", probe.timeSlicingMode.ToString())
            .Property("resolution", probe.resolution)
            .Property("hdr", probe.hdr)
            .Property("importance", probe.importance)
            .Property("size", probe.size.ToString())
            .EndObject();
    }

    private static bool HasMethod(Type t, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (Type cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
        {
            if (cur.GetMethod(name, flags | BindingFlags.DeclaredOnly) != null) return true;
        }
        return false;
    }

    private static string HierarchyPath(Transform t)
    {
        var parts = new List<string>();
        for (Transform cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}
