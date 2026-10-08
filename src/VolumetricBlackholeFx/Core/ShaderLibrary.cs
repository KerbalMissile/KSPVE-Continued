using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VolumetricBlackholeFx.Core;

/// <summary>Loads the mod's shaders from GameData/VolumetricBlackholeFx/Shaders/volumetricblackholefx.shaderbundle once, at startup.</summary>
internal static class ShaderLibrary
{
    public const string BundleFileName = "volumetricblackholefx.shaderbundle";

    private static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();

    public static bool Loaded { get; private set; }

    public static void Load()
    {
        if (Loaded) return;
        string path = Path.Combine(Path.Combine(Paths.ModDir, "Shaders"), BundleFileName);
        if (!File.Exists(path))
        {
            Log.Warning($"Shader bundle not found at {path}; black holes stay off.");
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
            if (!shader.isSupported) Log.Warning($"Shader {shader.name} is not supported on this GPU (it needs DirectX 11).");
        }
        bundle.Unload(false);

        Loaded = true;
        Log.Info($"Loaded {Shaders.Count} shader(s) from {BundleFileName}.");
    }

    public static Shader Find(string name) => Shaders.TryGetValue(name, out Shader s) && s.isSupported ? s : null;

    /// <summary>A new hidden material for the named shader, or null when it is missing or unsupported.</summary>
    public static Material CreateMaterial(string name)
    {
        Shader shader = Find(name);
        return shader == null ? null : new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = name };
    }
}
