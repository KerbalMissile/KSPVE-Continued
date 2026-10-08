using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KSPVE.Core;

/// <summary>Loads KSPVE's shaders from GameData/KSPVE/Shaders/kspve.shaderbundle once, at startup.</summary>
internal static class ShaderLibrary
{
    public const string BundleFileName = "kspve.shaderbundle";

    private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
    private static readonly Dictionary<string, ComputeShader> ComputeShaders = new Dictionary<string, ComputeShader>();

    public static bool Loaded { get; private set; }
    public static IEnumerable<string> ShaderNames => Shaders.Keys;

    public static void Load()
    {
        if (Loaded) return;
        string path = Path.Combine(Path.Combine(Paths.ModDir, "Shaders"), BundleFileName);
        if (!File.Exists(path))
        {
            Log.Warning($"Shader bundle not found at {path}; KSPVE rendering stays off.");
            return;
        }

        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null)
        {
            Log.Error("Could not load shader bundle " + path);
            return;
        }

        foreach (Shader shader in bundle.LoadAllAssets<Shader>())
        {
            Shaders[shader.name] = shader;
            if (!shader.isSupported) Log.Warning($"Shader {shader.name} is not supported on this GPU; effects using it stay off.");
        }
        foreach (ComputeShader compute in bundle.LoadAllAssets<ComputeShader>()) ComputeShaders[compute.name] = compute;
        bundle.Unload(false);

        Loaded = true;
        Log.Info($"Loaded {Shaders.Count} shaders and {ComputeShaders.Count} compute shaders from {BundleFileName}.");
    }

    public static Shader Find(string name) => Shaders.TryGetValue(name, out Shader s) && s.isSupported ? s : null;

    public static ComputeShader FindCompute(string name) => ComputeShaders.TryGetValue(name, out ComputeShader c) ? c : null;

    /// <summary>A new hidden material for the named shader, or null when it is missing or unsupported.</summary>
    public static Material CreateMaterial(string name)
    {
        Shader shader = Find(name);
        return shader == null ? null : new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = name };
    }
}
