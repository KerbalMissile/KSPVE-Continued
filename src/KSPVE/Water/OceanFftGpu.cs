using System;
using KSPVE.Core;
using KSPVE.Pure.Water;
using UnityEngine;

namespace KSPVE.Water;

/// <summary>
/// The ocean's waves on the graphics card (Water/OceanFft.compute): the sea's initial spectra uploaded once per sea state, then each
/// frame the spectra are advanced to the time, transformed (a radix-2 FFT in groupshared memory) and unpacked into textures
/// the ocean shaders read: height with the choppy displacement, slopes and the whitecaps' foam, a layer for each cascade.
/// </summary>
internal sealed class OceanFftGpu : IDisposable
{
    private readonly ComputeShader _cs;
    private readonly int _spectrum, _rows, _cols, _assemble;
    private Texture2DArray _h0;
    private RenderTexture _specA, _specB, _pingA, _pingB;
    private Texture2D _roughness;

    public OceanLayout Layout { get; }
    public RenderTexture Displacement { get; private set; }
    public RenderTexture Derivatives { get; private set; }
    public Texture2D Roughness => _roughness;
    public bool HasSpectrum { get; private set; }

    public static OceanFftGpu TryCreate(OceanLayout layout)
    {
        ComputeShader cs = ShaderLibrary.FindCompute("OceanFft");
        if (cs == null) return null;
        string suffix = layout.N.ToString();
        if (!cs.HasKernel("Spectrum") || !cs.HasKernel("Rows" + suffix) || !cs.HasKernel("Cols" + suffix) || !cs.HasKernel("Assemble")) return null;
        return new OceanFftGpu(cs, layout);
    }

    private OceanFftGpu(ComputeShader cs, OceanLayout layout)
    {
        _cs = cs;
        Layout = layout;
        string suffix = layout.N.ToString();
        _spectrum = cs.FindKernel("Spectrum");
        _rows = cs.FindKernel("Rows" + suffix);
        _cols = cs.FindKernel("Cols" + suffix);
        _assemble = cs.FindKernel("Assemble");
        int n = layout.N, c = layout.Cascades;
        _h0 = new Texture2DArray(n, n, c, TextureFormat.RGBAFloat, false, true) { name = "KSPVE Ocean h0", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        _specA = Array(n, c, RenderTextureFormat.ARGBFloat, false, "KSPVE Ocean spectrum A");
        _specB = Array(n, c, RenderTextureFormat.ARGBFloat, false, "KSPVE Ocean spectrum B");
        _pingA = Array(n, c, RenderTextureFormat.ARGBFloat, false, "KSPVE Ocean ping A");
        _pingB = Array(n, c, RenderTextureFormat.ARGBFloat, false, "KSPVE Ocean ping B");
        Displacement = Array(n, c, RenderTextureFormat.ARGBHalf, true, "KSPVE Ocean displacement");
        Derivatives = Array(n, c, RenderTextureFormat.ARGBHalf, true, "KSPVE Ocean derivatives");
        _roughness = new Texture2D(64, 1, TextureFormat.RFloat, false, true) { name = "KSPVE Ocean roughness", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
    }

    private static RenderTexture Array(int n, int slices, RenderTextureFormat format, bool mips, string name)
    {
        var rt = new RenderTexture(n, n, 0, format, RenderTextureReadWrite.Linear)
        {
            dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray,
            volumeDepth = slices,
            enableRandomWrite = true,
            useMipMap = mips,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = mips ? FilterMode.Trilinear : FilterMode.Point,
            anisoLevel = mips ? 4 : 0,
            name = name,
            hideFlags = HideFlags.HideAndDontSave
        };
        rt.Create();
        return rt;
    }

    /// <summary>Uploads a new sea (the spectra from <see cref="OceanLayout.BuildSpectra"/>) and its roughness table.</summary>
    public void SetSpectrum(float[] h0, float[] roughness)
    {
        int n = Layout.N, c = Layout.Cascades, per = n * n * 4;
        var slice = new float[per];
        for (int i = 0; i < c; i++)
        {
            Buffer.BlockCopy(h0, i * per * sizeof(float), slice, 0, per * sizeof(float));
            _h0.SetPixelData(slice, 0, i);
        }
        _h0.Apply(false, false);
        var colours = new Color[64];
        for (int i = 0; i < 64; i++) colours[i] = new Color(roughness[i], 0f, 0f, 0f);
        _roughness.SetPixels(colours);
        _roughness.Apply(false, false);
        HasSpectrum = true;
    }

    /// <summary>Replaces the roughness table alone (the wave scale setting changed).</summary>
    public void SetRoughnessOnly(float[] roughness)
    {
        var colours = new Color[64];
        for (int i = 0; i < 64; i++) colours[i] = new Color(roughness[i], 0f, 0f, 0f);
        _roughness.SetPixels(colours);
        _roughness.Apply(false, false);
    }

    /// <summary>Advances the waves to <paramref name="time"/> (s, any value: it is taken modulo the sea's period).</summary>
    public void Step(double time, double gravity, double surfaceTension, double heightScale, double chop, double[] foamThresholds, double dt, double foamLifetime, Vector4 lengths)
    {
        if (!HasSpectrum) return;
        int n = Layout.N, c = Layout.Cascades;
        _cs.SetInt("_KSPVE_N", n);
        _cs.SetVector("_KSPVE_Lengths", lengths);
        _cs.SetFloat("_KSPVE_Time", (float)(time - Math.Floor(time / OceanCascade.Period) * OceanCascade.Period));
        _cs.SetFloat("_KSPVE_Period", (float)OceanCascade.Period);
        _cs.SetFloat("_KSPVE_Gravity", (float)gravity);
        _cs.SetFloat("_KSPVE_SurfaceTension", (float)surfaceTension);
        _cs.SetFloat("_KSPVE_HeightScale", (float)heightScale);
        _cs.SetFloat("_KSPVE_Chop", (float)chop);
        var th = new Vector4(2f, 2f, 2f, 2f);
        for (int i = 0; i < Math.Min(4, foamThresholds.Length); i++) th[i] = (float)foamThresholds[i];
        _cs.SetVector("_KSPVE_FoamThreshold", th);
        _cs.SetFloat("_KSPVE_FoamDecay", (float)Math.Exp(-Math.Min(dt, 0.5) / Math.Max(foamLifetime, 0.1)));
        _cs.SetFloat("_KSPVE_FoamGain", 4f);

        _cs.SetTexture(_spectrum, "_KSPVE_H0", _h0);
        _cs.SetTexture(_spectrum, "_KSPVE_SpecA", _specA);
        _cs.SetTexture(_spectrum, "_KSPVE_SpecB", _specB);
        int groups = Mathf.CeilToInt(n / 8f);
        _cs.Dispatch(_spectrum, groups, groups, c);

        _cs.SetTexture(_rows, "_KSPVE_SpecA", _specA); _cs.SetTexture(_rows, "_KSPVE_SpecB", _specB);
        _cs.SetTexture(_rows, "_KSPVE_PingA", _pingA); _cs.SetTexture(_rows, "_KSPVE_PingB", _pingB);
        _cs.Dispatch(_rows, n, c, 1);
        _cs.SetTexture(_cols, "_KSPVE_SpecA", _specA); _cs.SetTexture(_cols, "_KSPVE_SpecB", _specB);
        _cs.SetTexture(_cols, "_KSPVE_PingA", _pingA); _cs.SetTexture(_cols, "_KSPVE_PingB", _pingB);
        _cs.Dispatch(_cols, n, c, 1);

        _cs.SetTexture(_assemble, "_KSPVE_SpecA", _specA);
        _cs.SetTexture(_assemble, "_KSPVE_SpecB", _specB);
        _cs.SetTexture(_assemble, "_KSPVE_Disp", Displacement);
        _cs.SetTexture(_assemble, "_KSPVE_Deriv", Derivatives);
        _cs.Dispatch(_assemble, groups, groups, c);
        Displacement.GenerateMips();
        Derivatives.GenerateMips();
    }

    public void Dispose()
    {
        foreach (UnityEngine.Object o in new UnityEngine.Object[] { _h0, _specA, _specB, _pingA, _pingB, Displacement, Derivatives, _roughness })
            if (o != null) UnityEngine.Object.Destroy(o);
        _h0 = null; _specA = _specB = _pingA = _pingB = null; Displacement = Derivatives = null; _roughness = null;
        HasSpectrum = false;
    }
}
