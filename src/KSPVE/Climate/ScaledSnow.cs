using System;
using System.Collections.Generic;
using KSPVE.Core;
using UnityEngine;

namespace KSPVE.Seasonal;

/// <summary>
/// Snow, glaciers and sea ice on the planets seen from afar. The scaled planet's colour texture is blended with the snow
/// maps (SnowBlend.shader) into a render texture the scaled material uses instead; KSP's own planet shaders still do the
/// lighting, so the snow is lit as native ground. The nearest few planets have one at a time (a full resolution texture is
/// tens of megabytes), the original is put back when a planet goes far, when the feature is turned off and at quit, and
/// the texture is blended again whenever the maps change (as the seasons turn) or another mod swaps the original.
/// </summary>
internal static class ScaledSnow
{
    private const string TextureName = "_MainTex";
    private static readonly int MapsId = Shader.PropertyToID("_KSPVE_SnowMaps");
    private static readonly int BumpId = Shader.PropertyToID("_BumpMap");
    private static readonly int BlendId = Shader.PropertyToID("_KSPVE_SnowBlend");
    private static readonly int ColorsId = Shader.PropertyToID("_KSPVE_SnowColors");

    private sealed class Entry
    {
        public CelestialBody Body;
        public Material Material;
        public Texture Original;
        public RenderTexture Blended;
        public int Version = -1;
        public double Albedo;
        public int Size;
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static Material _blend;
    private static bool _blendTried;
    private static float _lastScan;
    private static Camera _scaledCamera;
    private static readonly List<CelestialBody> Candidates = new List<CelestialBody>();
    private static readonly HashSet<string> Warned = new HashSet<string>();

    public static int Active => Entries.Count;

    public static void Update(bool allowed)
    {
        if (!allowed || !ClimateConfig.ScaledSnow || ClimateConfig.ScaledBodies <= 0 || FlightGlobals.Bodies == null)
        {
            RestoreAll();
            return;
        }
        if (!_blendTried)
        {
            _blendTried = true;
            _blend = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Climate/SnowBlend");
            if (_blend == null) Log.Warning("Snow: the scaled space snow shader is missing from the shader bundle; scaled snow stays off.");
        }
        if (_blend == null) return;

        if (Time.realtimeSinceStartup - _lastScan > 1.0f)
        {
            _lastScan = Time.realtimeSinceStartup;
            Choose();
        }
        foreach (string name in new List<string>(Entries.Keys))
        {
            Entry e = Entries[name];
            if (e.Body == null || e.Material == null) { Release(e, false); Entries.Remove(name); }
        }
        foreach (CelestialBody body in Candidates) Apply(body);
        // Bodies no longer chosen give their texture back.
        foreach (string name in new List<string>(Entries.Keys))
        {
            Entry e = Entries[name];
            if (!Candidates.Contains(e.Body)) { Release(e, true); Entries.Remove(name); }
        }
    }

    /// <summary>The bodies with snow to show, nearest the scaled camera first (the maps of every candidate are asked for).</summary>
    private static void Choose()
    {
        if (_scaledCamera == null)
        {
            foreach (Camera c in Camera.allCameras)
                if (c != null && c.name == "Camera ScaledSpace") { _scaledCamera = c; break; }
        }
        Vector3 view = _scaledCamera != null ? _scaledCamera.transform.position : Vector3.zero;
        var found = new List<KeyValuePair<float, CelestialBody>>();
        foreach (CelestialBody b in FlightGlobals.Bodies)
        {
            if (b == null || b.scaledBody == null || !b.atmosphere || !b.hasSolidSurface || b.pqsController == null) continue;
            SnowCover.Request(b);
            BodyClimateState s = SnowCover.StateOf(b);
            if (s == null || s.Field == null) continue;
            found.Add(new KeyValuePair<float, CelestialBody>((b.scaledBody.transform.position - view).sqrMagnitude, b));
        }
        found.Sort((a, b) => a.Key.CompareTo(b.Key));
        Candidates.Clear();
        for (int i = 0; i < found.Count && i < ClimateConfig.ScaledBodies; i++) Candidates.Add(found[i].Value);
    }

    private static void Apply(CelestialBody body)
    {
        SnowTextures textures = SnowCover.TexturesOf(body);
        if (textures == null || textures.Maps == null) return;
        if (!Entries.TryGetValue(body.bodyName, out Entry e) || e.Body != body)
        {
            Renderer renderer = body.scaledBody.GetComponent<Renderer>();
            if (renderer == null) renderer = body.scaledBody.GetComponentInChildren<Renderer>();
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null || !material.HasProperty(TextureName))
            {
                if (Warned.Add(body.bodyName)) Log.Warning($"Snow: {body.bodyName}'s scaled material has no {TextureName}; no snow is blended into it.");
                return;
            }
            e = new Entry { Body = body, Material = material };
            Entries[body.bodyName] = e;
        }
        Texture current = e.Material.GetTexture(TextureName);
        bool replaced = false;
        if (current != null && current != e.Blended)
        {
            // The planet's own texture (first time, or another mod swapped it, e.g. on-demand loading).
            e.Original = current;
            replaced = true;
        }
        if (e.Original == null) return;
        int version = SnowCover.VersionOf(body);
        double albedo = ClimateConfig.SnowAlbedo;
        int size = Math.Min(Math.Max(e.Original.width, 256), Math.Max(ClimateConfig.ScaledResolution, 256));
        if (!replaced && e.Blended != null && e.Version == version && e.Albedo == albedo && e.Size == size) return;

        if (e.Blended == null || e.Blended.width != size || e.Blended.height != Math.Max(size * e.Original.height / Math.Max(e.Original.width, 1), 1))
        {
            if (e.Blended != null) { e.Blended.Release(); UnityEngine.Object.Destroy(e.Blended); }
            int height = Math.Max(size * e.Original.height / Math.Max(e.Original.width, 1), 1);
            e.Blended = new RenderTexture(size, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "KSPVE snow " + body.bodyName,
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };
            e.Blended.Create();
        }
        Texture bump = e.Material.HasProperty("_BumpMap") ? e.Material.GetTexture("_BumpMap") : null;
        _blend.SetTexture(MapsId, textures.Maps);
        _blend.SetTexture(BumpId, bump != null ? bump : Texture2D.blackTexture);
        // Noise cells round the planet: about 4 km across (a body of 600 km radius has 940 round the equator).
        float cells = Mathf.Clamp((float)(2.0 * Math.PI * body.Radius / 4000.0), 64f, 4096f);
        _blend.SetVector(BlendId, new Vector4((float)albedo, bump != null ? 1f : 0f, cells, 0.9f));
        _blend.SetVector(ColorsId, new Vector4(1f, 0.95f, 0f, 0f));
        Graphics.Blit(e.Original, e.Blended, _blend, 0);
        e.Material.SetTexture(TextureName, e.Blended);
        e.Version = version;
        e.Albedo = albedo;
        e.Size = size;
    }

    private static void Release(Entry e, bool restore)
    {
        if (e.Material != null && restore && e.Original != null && e.Material.GetTexture(TextureName) == e.Blended)
            e.Material.SetTexture(TextureName, e.Original);
        if (e.Blended != null)
        {
            e.Blended.Release();
            UnityEngine.Object.Destroy(e.Blended);
            e.Blended = null;
        }
    }

    /// <summary>Puts every planet's own texture back.</summary>
    public static void RestoreAll()
    {
        if (Entries.Count == 0) return;
        foreach (Entry e in Entries.Values) Release(e, true);
        Entries.Clear();
        Candidates.Clear();
    }
}
