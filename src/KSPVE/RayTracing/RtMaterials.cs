using System;
using System.Collections.Generic;
using System.Text;
using KSPVE.Core;
using UnityEngine;

namespace KSPVE.RayTracing;

/// <summary>
/// The surfaces the tracer shades: a table of materials (albedo tint, atlas slice, emission, flags) and an atlas of their
/// albedo textures, each copied on the GPU (no CPU copy of the game's textures is needed) at 128x128 into one slice of a
/// texture array. A renderer's materials sit together in the table, so a triangle's submesh picks its own.
/// </summary>
internal sealed class RtMaterials : IDisposable
{
    public const int AtlasSize = 128;

    /// <summary>Flag: multiply the albedo by the vertex colours (terrain carries its colour map in them).</summary>
    public const float VertexColours = 1f;

    private readonly List<RtMaterial> _table = new List<RtMaterial>();
    private readonly Dictionary<Material, int> _index = new Dictionary<Material, int>();
    private readonly Dictionary<string, int> _blocks = new Dictionary<string, int>();
    private readonly Dictionary<Texture, int> _slices = new Dictionary<Texture, int>();
    private readonly StringBuilder _key = new StringBuilder();
    private Texture2DArray _atlas;
    private RenderTexture _scratch;
    private int _slicesUsed;
    private bool _dirty = true;

    public ComputeBuffer Table { get; private set; }
    public Texture2DArray Atlas => _atlas;
    public int Count => _table.Count;
    public int Slices => _slicesUsed;

    /// <summary>The scene renders in linear colour (sRGB textures decoded, vertex colours converted), not gamma.</summary>
    public static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

    /// <summary>Where a renderer's materials start in the table (consecutive, one per submesh).</summary>
    public int BlockFor(Material[] materials)
    {
        _key.Length = 0;
        foreach (Material m in materials) _key.Append(m != null ? m.GetInstanceID() : 0).Append(',');
        string key = _key.ToString();
        if (_blocks.TryGetValue(key, out int start)) return start;
        start = _table.Count;
        foreach (Material m in materials) _table.Add(Describe(m));
        if (materials.Length == 0) _table.Add(Describe(null));
        _blocks[key] = start;
        _dirty = true;
        return start;
    }

    /// <summary>True for materials drawn in the transparent queue (glass, plumes, particles): not surfaces light bounces off.</summary>
    public static bool IsTransparent(Material m) => m != null && m.renderQueue >= 2450;

    private RtMaterial Describe(Material m)
    {
        if (m == null) return new RtMaterial { Albedo = new Vector4(0.5f, 0.5f, 0.5f, -1f) };
        string shader = m.shader != null ? m.shader.name : string.Empty;
        bool terrain = shader.IndexOf("PQS", StringComparison.OrdinalIgnoreCase) >= 0
                       || shader.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0
                       || shader.IndexOf("Parallax", StringComparison.OrdinalIgnoreCase) >= 0;
        Color tint = Color.white;
        if (!terrain && m.HasProperty("_Color")) tint = m.GetColor("_Color");
        if (Linear) tint = tint.linear;
        float slice = -1f;
        if (!terrain && m.HasProperty("_MainTex"))
        {
            Texture texture = m.GetTexture("_MainTex");
            if (texture != null && texture.dimension == UnityEngine.Rendering.TextureDimension.Tex2D) slice = SliceFor(texture);
        }
        Color emission = Color.black;
        if (m.HasProperty("_EmissiveColor"))
        {
            emission = m.GetColor("_EmissiveColor");
            if (Linear) emission = emission.linear;
        }
        return new RtMaterial
        {
            Albedo = new Vector4(tint.r, tint.g, tint.b, slice),
            Emission = new Vector4(emission.r, emission.g, emission.b, terrain ? VertexColours : 0f),
        };
    }

    private int SliceFor(Texture texture)
    {
        if (_slices.TryGetValue(texture, out int slice)) return slice;
        if (_atlas == null || _slicesUsed >= _atlas.depth) GrowAtlas();
        if (_atlas == null) return -1;
        slice = _slicesUsed++;
        try
        {
            if (_scratch == null)
            {
                _scratch = new RenderTexture(AtlasSize, AtlasSize, 0, RenderTextureFormat.ARGB32, Linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
                {
                    name = "KSPVE RT atlas scratch",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _scratch.Create();
            }
            Graphics.Blit(texture, _scratch);
            Graphics.CopyTexture(_scratch, 0, 0, _atlas, slice, 0);
        }
        catch (Exception ex)
        {
            Log.Warning($"Copying {texture.name} into the ray tracing atlas failed: {ex.Message}");
            slice = -1;
        }
        _slices[texture] = slice;
        return slice;
    }

    private void GrowAtlas()
    {
        int depth = _atlas == null ? 256 : Math.Min(_atlas.depth * 2, 2048);
        if (_atlas != null && depth == _atlas.depth) return;   // full: further textures go without
        var atlas = new Texture2DArray(AtlasSize, AtlasSize, depth, TextureFormat.RGBA32, false, !Linear)
        {
            name = "KSPVE RT albedo atlas",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        atlas.Apply(false, true);
        if (_atlas != null)
        {
            for (int i = 0; i < _slicesUsed; i++) Graphics.CopyTexture(_atlas, i, 0, atlas, i, 0);
            UnityEngine.Object.Destroy(_atlas);
        }
        _atlas = atlas;
    }

    public void Upload()
    {
        if (!_dirty && Table != null) return;
        if (Table == null || Table.count < Math.Max(_table.Count, 1))
        {
            Table?.Release();
            Table = new ComputeBuffer(Math.Max(64, NextPow2(_table.Count)), 32, ComputeBufferType.Structured);
        }
        if (_table.Count > 0) Table.SetData(_table);
        _dirty = false;
    }

    private static int NextPow2(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>Forgets everything (when the scene changes: KSP replaces its materials and textures).</summary>
    public void Clear()
    {
        _table.Clear();
        _index.Clear();
        _blocks.Clear();
        _slices.Clear();
        _slicesUsed = 0;
        _dirty = true;
    }

    public void Dispose()
    {
        Table?.Release();
        Table = null;
        if (_atlas != null) UnityEngine.Object.Destroy(_atlas);
        if (_scratch != null)
        {
            _scratch.Release();
            UnityEngine.Object.Destroy(_scratch);
        }
        _atlas = null;
        _scratch = null;
    }
}
