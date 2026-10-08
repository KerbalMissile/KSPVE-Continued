using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VolumetricBlackholeFx.Build
{
    public static class BuildBundles
    {
        const string BundleName = "volumetricblackholefx.shaderbundle";
        const string ShaderRoot = "Assets/Shaders";

        public static void BuildAll()
        {
            try
            {
                string outDir = GetArg("-out");
                if (string.IsNullOrEmpty(outDir))
                    throw new Exception("Missing -out <dir> argument.");
                outDir = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);

                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var assets = new List<string>();
                foreach (string guid in AssetDatabase.FindAssets("t:Shader t:ComputeShader", new[] { ShaderRoot }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                        p.EndsWith(".compute", StringComparison.OrdinalIgnoreCase))
                        assets.Add(p);
                }
                assets.Sort(StringComparer.Ordinal);
                if (assets.Count == 0)
                    throw new Exception("No shader/compute assets found under " + ShaderRoot);

                var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() };
                var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression |
                    BuildAssetBundleOptions.DeterministicAssetBundle |
                    BuildAssetBundleOptions.StrictMode,
                    BuildTarget.StandaloneWindows64);

                string bundlePath = Path.Combine(outDir, BundleName);
                if (manifest == null || !File.Exists(bundlePath))
                    throw new Exception("BuildAssetBundles failed (no manifest/bundle produced); see shader errors above.");

                bool anyError = false;
                var sb = new StringBuilder();
                foreach (string path in assets)
                {
                    if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase))
                    {
                        var s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                        bool err = s == null || ShaderUtil.ShaderHasError(s);
                        anyError |= err;
                        sb.AppendLine("shader: " + (s != null ? s.name : "<null>") + " | file=" + path + " | hasError=" + err);
                    }
                    else
                    {
                        var c = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                        if (c == null) { anyError = true; sb.AppendLine("compute: <null> | file=" + path); continue; }
                        var kernels = new List<string>();
                        foreach (string k in KernelNames(path)) if (c.HasKernel(k)) kernels.Add(k);
                        sb.AppendLine("compute: " + c.name + " | file=" + path + " | kernels=" + string.Join(",", kernels.ToArray()));
                    }
                }
                File.WriteAllText(Path.Combine(outDir, BundleName + ".contents.txt"), sb.ToString());

                if (anyError) throw new Exception("One or more shaders have compile errors.");

                Debug.Log("[VolumetricBlackholeFx build] SUCCESS: " + bundlePath + " (" + assets.Count + " assets)");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[VolumetricBlackholeFx build] FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        // Kernel names are parsed from '#pragma kernel X' lines (no public kernel enumeration API).
        static IEnumerable<string> KernelNames(string path)
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string t = line.Trim();
                if (t.StartsWith("#pragma kernel"))
                {
                    string[] parts = t.Substring("#pragma kernel".Length).Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0) yield return parts[0];
                }
            }
        }

        static string GetArg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == name) return a[i + 1];
            return null;
        }
    }
}
