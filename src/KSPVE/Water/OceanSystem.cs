using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Textures;
using KSPVE.Pure.Water;
using UnityEngine;
using D3 = KSPVE.Pure.Numerics.Double3;

namespace KSPVE.Water;

/// <summary>
/// The sea as the player sees it near a body: the look of KSP's own ocean (its PQS quads) is replaced by KSPVE's by swapping the
/// shader of the materials those quads use (and putting the originals back whenever it is switched off); KSP's ocean physics, the
/// altitude 0 surface boats float on, is not touched. Near the camera a polar mesh is drawn on top, displaced by the FFT waves;
/// beyond it the swapped quads show the same waves' normals, foam and glitter on a flat surface.
/// </summary>
internal sealed class OceanSystem
{
    private const string ShaderName = "Hidden/KSPVE/Water/Ocean";
    private const string NearCameraName = "Camera 00";
    private const int TerrainLayer = 15;
    /// <summary>The near mesh is drawn while the camera is lower than this many near radii above the sea.</summary>
    private const double NearAltitudeRadii = 6.0;

    // ---- the swapped materials of the current body's ocean
    private sealed class Swap { public Material Material; public Shader Shader; public int Queue; }
    private readonly List<Swap> _swapped = new List<Swap>();
    private readonly HashSet<Material> _seen = new HashSet<Material>();
    private CelestialBody _body;
    private float _nextScan;
    private Color _stockColor = Color.clear;

    // ---- waves
    private Shader _shader;
    private Material _gridMaterial;
    private Mesh _grid;
    private OceanFftGpu _fft;
    private OceanQuality _builtQuality;
    private Camera _nearCamera;

    // The spectrum's sea state, to tell when it needs building again, and the job making it.
    private double _spectrumWind = -1, _spectrumFetch, _spectrumGravity, _spectrumChop;
    private Vector3d _spectrumWindWorld;
    private float _spectrumTime = -100f;
    private Task<float[]> _spectrumJob;
    private float[] _roughnessForJob;
    private double _jobWind;
    private Vector3d _jobWindWorld;
    private bool _wasActive;
    private double[] _foamThresholds = { 2, 2, 2, 2 };

    // The waves' coordinates: axes in the body's turning frame, and the offsets that keep the pattern continuous when they change.
    private Double3 _anchorUp, _axisE, _axisN;   // in the turning frame
    private double _offU, _offV;
    private bool _anchored;
    private string _anchoredBody;

    private static readonly int PropGrid = Shader.PropertyToID("_KSPVE_Grid");

    /// <summary>The sea-ice map (SnowCover.SeaIceOf by default): share of the sea covered, by latitude and longitude; null for none.</summary>
    public static Func<CelestialBody, GreyMap> SeaIceMap = body => null;

    public bool Active => _wasActive;
    public CelestialBody Body => _body;
    public OceanFftGpu Fft => _fft;

    public void Update(bool allowed)
    {
        bool want = allowed && WaterConfig.Enabled && WaterConfig.Oceans && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready
                    && SystemInfo.supportsComputeShaders && ShaderLibrary.Loaded;
        CelestialBody body = want ? FlightGlobals.currentMainBody : null;
        if (body != null && (!body.ocean || body.pqsController == null)) body = null;

        if (body != _body)
        {
            Restore();
            _body = body;
            _anchored = false;
            _spectrumWind = -1;
            _spectrumJob = null;
        }
        if (_body == null)
        {
            Shutdown(false);
            _wasActive = false;
            return;
        }

        if (_shader == null) _shader = ShaderLibrary.Find(ShaderName);
        if (_shader == null) { _wasActive = false; return; }
        if (_fft == null || _builtQuality != WaterConfig.Quality)
        {
            Shutdown(true);
            _fft = OceanFftGpu.TryCreate(OceanLayout.For(WaterConfig.Quality));
            _builtQuality = WaterConfig.Quality;
            _spectrumWind = -1;
            if (_fft == null) { Log.Warning("Ocean: the FFT compute shader is missing or unsupported; the ocean keeps its stock look."); _wasActive = false; return; }
        }
        if (_nearCamera == null) _nearCamera = FindCamera(NearCameraName);
        if (_nearCamera == null) { _wasActive = false; return; }
        _nearCamera.depthTextureMode |= DepthTextureMode.Depth;

        if (Time.realtimeSinceStartup >= _nextScan)
        {
            _nextScan = Time.realtimeSinceStartup + 1.5f;
            ScanMaterials(_body);
        }

        UpdateSea(_body);
        _wasActive = true;
    }

    // ---------------------------------------------------------------- materials

    private void ScanMaterials(CelestialBody body)
    {
        PQS ocean = OceanOf(body);
        if (ocean == null) return;
        var found = new List<Material>();
        void Add(Material m) { if (m != null && m.shader != _shader) found.Add(m); }
        Add(ocean.surfaceMaterial); Add(ocean.fallbackMaterial);
        Add(ocean.lowQualitySurfaceMaterial); Add(ocean.mediumQualitySurfaceMaterial);
        Add(ocean.highQualitySurfaceMaterial); Add(ocean.ultraQualitySurfaceMaterial);
        foreach (PQSMod_OceanFX fx in ocean.GetComponentsInChildren<PQSMod_OceanFX>(true)) Add(fx.waterMat);
        if (ocean.quads != null) foreach (PQ q in ocean.quads) Collect(q, found, 0);
        foreach (Material m in found)
        {
            if (m == null || _seen.Contains(m)) continue;
            _seen.Add(m);
            if (_stockColor.a == 0f && m.HasProperty("_Color")) _stockColor = m.GetColor("_Color");
            _swapped.Add(new Swap { Material = m, Shader = m.shader, Queue = m.renderQueue });
            m.shader = _shader;
            m.renderQueue = -1;
            m.SetFloat(PropGrid, 0f);
        }
        if (_stockColor.a == 0f && body.pqsController != null) _stockColor = body.pqsController.mapOceanColor;
    }

    private static void Collect(PQ quad, List<Material> into, int depth)
    {
        if (quad == null || depth > 24) return;
        if (quad.meshRenderer != null && quad.meshRenderer.sharedMaterial != null) into.Add(quad.meshRenderer.sharedMaterial);
        if (quad.subNodes != null) foreach (PQ sub in quad.subNodes) Collect(sub, into, depth + 1);
    }

    private static PQS OceanOf(CelestialBody body)
    {
        PQS pqs = body.pqsController;
        if (pqs == null) return null;
        PQS[] children = pqs.ChildSpheres;
        if (children == null) return null;
        foreach (PQS child in children)
            if (child != null && child.GetComponentInChildren<PQSMod_OceanFX>(true) != null) return child;
        return null;
    }

    /// <summary>Puts the original shaders back on the ocean's materials.</summary>
    public void Restore()
    {
        foreach (Swap s in _swapped)
        {
            if (s.Material == null) continue;
            s.Material.shader = s.Shader;
            s.Material.renderQueue = s.Queue;
        }
        _swapped.Clear();
        _seen.Clear();
        _stockColor = Color.clear;
    }

    public void Shutdown(bool keepMaterials)
    {
        if (!keepMaterials) Restore();
        if (_fft != null) { _fft.Dispose(); _fft = null; }
        if (!keepMaterials && _gridMaterial != null) { UnityEngine.Object.Destroy(_gridMaterial); _gridMaterial = null; }
        if (!keepMaterials && _grid != null) { UnityEngine.Object.Destroy(_grid); _grid = null; }
    }

    // ---------------------------------------------------------------- the sea

    private static Camera FindCamera(string name)
    {
        int count = Camera.allCamerasCount;
        var cams = new Camera[count];
        Camera.GetAllCameras(cams);
        foreach (Camera c in cams) if (c != null && c.name == name) return c;
        return null;
    }

    private void UpdateSea(CelestialBody body)
    {
        _clock += Time.deltaTime;
        Vector3d camPos = _nearCamera.transform.position;
        Vector3d rel = camPos - body.position;
        double r = rel.magnitude;
        if (r < 1.0) return;
        Vector3d up = rel / r;
        double altitude = r - body.Radius;
        CloudWorld.Basis(body, out Vector3d bx, out Vector3d by, out Vector3d bz);
        if (!_anchored || _anchoredBody != body.bodyName || D3.Dot(_anchorUp, ToFrame(up, bx, by, bz)) < Math.Cos(1.5 * Math.PI / 180.0))
            Anchor(body, up, rel, bx, by, bz);
        Vector3d e = FromFrame(_axisE, bx, by, bz), n = FromFrame(_axisN, bx, by, bz);
        double u = Vector3d.Dot(rel, e) + _offU, v = Vector3d.Dot(rel, n) + _offV;

        // The sea state: the wind there (or the setting's), the fetch, the body's gravity.
        double gravity = body.GeeASL * 9.80665;
        Vector3d windWorld = Vector3d.zero;
        double wind;
        try
        {
            BodyClouds clouds = CloudWorld.Of(body);
            windWorld = CloudWorld.WindWorld(clouds, body.position + up * (body.Radius + 10.0));
        }
        catch (Exception) { }
        wind = WaterConfig.WindOverride >= 0.0 ? WaterConfig.WindOverride : windWorld.magnitude;
        if (windWorld.magnitude < 0.05) windWorld = e * 0.05;
        Vector3d windDir = windWorld.normalized;

        LiquidDef liquid = WaterConfig.LiquidOf(body.bodyName);
        double tension = liquid != null ? liquid.SurfaceTensionOverDensity : OceanSpectrum.WaterSurfaceTension;
        OceanLayout layout = _fft.Layout;
        double chop = WaterConfig.Choppiness;

        // A new spectrum when the sea has changed enough (building takes a moment, off the main thread).
        bool changed = _spectrumWind < 0 || Math.Abs(wind - _spectrumWind) > 0.12 * Math.Max(_spectrumWind, 2.0)
                       || Math.Abs(WaterConfig.Fetch - _spectrumFetch) > 1.0 || Math.Abs(gravity - _spectrumGravity) > 1e-6
                       || Vector3d.Angle(windDir, _spectrumWindWorld) > 14.0 || Math.Abs(chop - _spectrumChop) > 1e-9;
        if (_spectrumJob == null && changed && Time.realtimeSinceStartup - _spectrumTime > 8f)
        {
            double windAngle = Math.Atan2(Vector3d.Dot(windDir, n), Vector3d.Dot(windDir, e));
            ulong seed = 0x5EA;
            foreach (char ch in body.bodyName) seed = seed * 1099511628211UL ^ ch;
            double w = wind, fetch = WaterConfig.Fetch, scale = WaterConfig.WaveScale, ch2 = chop;
            _jobWind = w; _jobWindWorld = windDir;
            _roughnessForJob = null;
            _spectrumJob = Task.Run(() => layout.BuildSpectra(w, windAngle, fetch, gravity, seed));
            _spectrumFetch = fetch; _spectrumGravity = gravity; _spectrumChop = ch2;
            _foamThresholds = layout.FoamThresholds(w, fetch, gravity, ch2);
            _roughnessForJob = layout.RoughnessTable(w, fetch, gravity, scale);
            _roughScale = scale;
            _spectrumTime = Time.realtimeSinceStartup;
        }
        if (_spectrumJob != null && _spectrumJob.IsCompleted)
        {
            if (_spectrumJob.IsFaulted) Log.Exception("Building the ocean's spectrum failed", _spectrumJob.Exception?.GetBaseException() ?? new Exception("unknown"));
            else
            {
                _fft.SetSpectrum(_spectrumJob.Result, _roughnessForJob);
                _spectrumWind = _jobWind; _spectrumWindWorld = _jobWindWorld;
            }
            _spectrumJob = null;
        }
        // The roughness table follows the display wave scale without a new spectrum.
        if (_fft.HasSpectrum && _spectrumJob == null && Math.Abs(WaterConfig.WaveScale - _roughScale) > 1e-6)
        {
            _roughScale = WaterConfig.WaveScale;
            _fft.SetRoughnessOnly(layout.RoughnessTable(Math.Max(_spectrumWind, 0.0), WaterConfig.Fetch, gravity, _roughScale));
        }

        SeaState sea = SeaState.From(Math.Max(_spectrumWind, 0.0), WaterConfig.Fetch, gravity);
        // Sea state used by the shaders: wind, the peak frequency (for shoaling), the (display-scaled) significant height.
        double hs = sea.SignificantHeight * WaterConfig.WaveScale;

        if (_fft.HasSpectrum)
            _fft.Step(_clock, gravity, tension, WaterConfig.WaveScale, chop, _foamThresholds, Time.deltaTime, 4.0, ToVector4(layout.Lengths));

        // Uniforms.
        bool nearOn = altitude < NearAltitudeRadii * WaterConfig.NearRadius;
        double nearRadius = WaterConfig.NearRadius;
        Vector3 up3 = (Vector3)up;
        Vector3 center = (Vector3)(camPos - up * altitude);   // the point on the sea under the camera
        // Tangent axes at that point.
        Vector3 gridE = ((Vector3)e - up3 * Vector3.Dot((Vector3)e, up3)).normalized;
        Vector3 gridN = Vector3.Cross(gridE, up3);
        if (Vector3.Dot(gridN, (Vector3)n) < 0f) gridN = -gridN;

        double largest = layout.Lengths[0];
        double camU = u - Math.Floor(u / largest) * largest, camV = v - Math.Floor(v / largest) * largest;

        WaterLight.Apply(body, up3);

        Optics(body, liquid, out Vector4 deep, out Vector4 absorb, out Vector4 backscatter);

        Shader.SetGlobalVector("_KSPVE_Lengths", ToVector4(layout.Lengths));
        var minWave = new Vector4(1e3f, 1e3f, 1e3f, 1e3f);
        for (int c = 0; c < layout.Cascades; c++) minWave[c] = (float)layout.MinWavelength(c);
        Shader.SetGlobalVector("_KSPVE_MinWavelength", minWave);
        Shader.SetGlobalVector("_KSPVE_OceanFrame", new Vector4((float)camU, (float)camV, layout.Cascades, 0f));
        Shader.SetGlobalVector("_KSPVE_OceanE", new Vector4((float)e.x, (float)e.y, (float)e.z, 0f));
        Shader.SetGlobalVector("_KSPVE_OceanN", new Vector4((float)n.x, (float)n.y, (float)n.z, 0f));
        Shader.SetGlobalVector("_KSPVE_GridCenter", new Vector4(center.x, center.y, center.z, nearOn ? (float)nearRadius : 0f));
        Shader.SetGlobalVector("_KSPVE_GridUp", new Vector4(up3.x, up3.y, up3.z, (float)body.Radius));
        Shader.SetGlobalVector("_KSPVE_GridE", new Vector4(gridE.x, gridE.y, gridE.z, 0f));
        Shader.SetGlobalVector("_KSPVE_GridN", new Vector4(gridN.x, gridN.y, gridN.z, 0f));
        Shader.SetGlobalVector("_KSPVE_Sea", new Vector4((float)_spectrumWind, (float)Math.Max(sea.PeakFrequency, 0.1), (float)hs, (float)gravity));
        Shader.SetGlobalVector("_KSPVE_SeaLook", new Vector4((float)WaterConfig.BreakerIndex, WaterConfig.Foam ? 1f : 0f, WaterConfig.Refraction ? 1f : 0f, (float)WaterConfig.Brightness));
        Shader.SetGlobalVector("_KSPVE_Deep", deep);
        Shader.SetGlobalVector("_KSPVE_Absorb", absorb);
        Shader.SetGlobalVector("_KSPVE_Backscatter", backscatter);
        Shader.SetGlobalVector("_KSPVE_SeaIce", new Vector4(0f, (float)SeaIceHere(body, rel), 0f, 0f));
        if (_fft.HasSpectrum)
        {
            Shader.SetGlobalTexture("_KSPVE_Disp", _fft.Displacement);
            Shader.SetGlobalTexture("_KSPVE_Deriv", _fft.Derivatives);
            Shader.SetGlobalTexture("_KSPVE_RoughLut", _fft.Roughness);
        }
        else
        {
            // Until the first spectrum is ready the shaders would sample empty textures: no waves, and the stock look stays.
            Shader.SetGlobalVector("_KSPVE_OceanFrame", new Vector4(0f, 0f, 0f, 0f));
        }

        if (nearOn && _fft.HasSpectrum) DrawNear();
    }

    private double _roughScale = -1;
    private double _clock;

    private double SeaIceHere(CelestialBody body, Vector3d rel)
    {
        GreyMap map = null;
        try { map = SeaIceMap(body); } catch (Exception) { }
        if (map == null) return 0.0;
        Double3 p = CloudWorld.ToBody(body, body.position + rel);
        double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, p.Y / Math.Max(p.Length, 1.0)))), lon = Math.Atan2(p.Z, p.X);
        return map.Sample((lon + Math.PI) / (2.0 * Math.PI), (lat + 0.5 * Math.PI) / Math.PI);
    }

    private static Vector4 ToVector4(double[] values)
    {
        var v = new Vector4(1e3f, 1e3f, 1e3f, 1e3f);
        for (int i = 0; i < Math.Min(4, values.Length); i++) v[i] = (float)values[i];
        return v;
    }

    private static Vector4 Mul(Vector3 light, D3 c) => new Vector4(light.x * (float)c.X, light.y * (float)c.Y, light.z * (float)c.Z, 0f);

    private static D3 ToFrame(Vector3d v, Vector3d x, Vector3d y, Vector3d z) => new D3(Vector3d.Dot(v, x), Vector3d.Dot(v, y), Vector3d.Dot(v, z));

    private static Vector3d FromFrame(D3 f, Vector3d x, Vector3d y, Vector3d z) => x * f.X + y * f.Y + z * f.Z;

    /// <summary>
    /// Fixes the waves' axes at the point under the camera (east and north there), in the body's turning frame so the waves stay
    /// with the ground as it turns. The offsets are chosen so the pattern does not jump where the camera is.
    /// </summary>
    private void Anchor(CelestialBody body, Vector3d up, Vector3d rel, Vector3d bx, Vector3d by, Vector3d bz)
    {
        Vector3d north = by;
        Vector3d east = Vector3d.Cross(up, north);
        if (east.magnitude < 1e-6) east = bz;   // at a pole east is undefined: any horizontal axis will do
        east = east.normalized;
        Vector3d tangentNorth = Vector3d.Cross(east, up).normalized;
        double oldU = 0.0, oldV = 0.0;
        if (_anchored && _anchoredBody == body.bodyName)
        {
            Vector3d oe = FromFrame(_axisE, bx, by, bz), on = FromFrame(_axisN, bx, by, bz);
            oldU = Vector3d.Dot(rel, oe) + _offU;
            oldV = Vector3d.Dot(rel, on) + _offV;
        }
        _anchorUp = ToFrame(up, bx, by, bz);
        _axisE = ToFrame(east, bx, by, bz);
        _axisN = ToFrame(tangentNorth, bx, by, bz);
        _offU = oldU - Vector3d.Dot(rel, east);
        _offV = oldV - Vector3d.Dot(rel, tangentNorth);
        _anchored = true;
        _anchoredBody = body.bodyName;
    }

    // ---------------------------------------------------------------- the near mesh

    private void DrawNear()
    {
        if (_grid == null) _grid = BuildGrid();
        if (_gridMaterial == null)
        {
            _gridMaterial = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, name = "KSPVE Ocean (near)" };
            _gridMaterial.SetFloat(PropGrid, 1f);
        }
        Graphics.DrawMesh(_grid, Matrix4x4.identity, _gridMaterial, TerrainLayer, _nearCamera, 0, null, false, false, false);
    }

    /// <summary>
    /// A polar mesh: 288 rings from the centre to the rim, spaced logarithmically (fine close up, 3.3 percent of the radius far
    /// out), 384 spokes. u is the ring's place from 0 (the centre) to 1 (the rim) and v the angle in turns; the vertex shader
    /// puts it on the sea under the camera.
    /// </summary>
    private static Mesh BuildGrid()
    {
        const int rings = 288, spokes = 384;   // Water/Ocean.shader: KSPVE_GRID_RINGS
        var uv = new Vector2[1 + rings * spokes];
        var vertices = new Vector3[uv.Length];
        uv[0] = new Vector2(-100f, 0f);
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < spokes; s++)
                uv[1 + r * spokes + s] = new Vector2((r + 1) / (float)rings, s / (float)spokes);
        var tris = new List<int>(spokes * 3 + (rings - 1) * spokes * 6);
        for (int s = 0; s < spokes; s++)
        {
            int a = 1 + s, b = 1 + (s + 1) % spokes;
            tris.Add(0); tris.Add(a); tris.Add(b);
        }
        for (int r = 0; r + 1 < rings; r++)
        {
            for (int s = 0; s < spokes; s++)
            {
                int s1 = (s + 1) % spokes;
                int a = 1 + r * spokes + s, b = 1 + r * spokes + s1, c = 1 + (r + 1) * spokes + s, d = 1 + (r + 1) * spokes + s1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }
        var mesh = new Mesh { name = "KSPVE Ocean grid", hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e7f, 1e7f, 1e7f));
        return mesh;
    }

    // ---------------------------------------------------------------- the water's own colour

    /// <summary>
    /// The optics of the body's sea: pure water and plankton (Pope and Fry, Morel) for water; a configured liquid's own; and for a
    /// pack's ocean that is plainly not water (the stock ocean colour has no blue to it: Eve's purple) that colour, as a
    /// reflecting liquid that absorbs fast.
    /// </summary>
    private void Optics(CelestialBody body, LiquidDef liquid, out Vector4 deep, out Vector4 absorb, out Vector4 backscatter)
    {
        double chl = WaterConfig.Chlorophyll;
        D3 a = WaterOptics.Absorption(chl), bb = WaterOptics.Backscattering(chl), r = WaterOptics.DeepReflectance(chl);
        double index = WaterOptics.SeawaterIndex;
        if (liquid != null)
        {
            index = liquid.RefractiveIndex;
            if (liquid.Absorption.X >= 0.0) { a = liquid.Absorption; bb = new D3(0.01, 0.01, 0.01); r = WaterOptics.DeepReflectance(a, bb); }
            if (liquid.HasColor) r = liquid.DeepColor;
        }
        else if (_stockColor.a > 0f && !LooksLikeWater(_stockColor))
        {
            // A liquid that is not water: its stock ocean colour, as a reflectance 30 percent of it, absorbing within a few metres.
            r = new D3(_stockColor.r * 0.3, _stockColor.g * 0.3, _stockColor.b * 0.3);
            a = new D3(0.35, 0.35, 0.35);
            bb = new D3(0.05, 0.05, 0.05);
        }
        deep = new Vector4((float)r.X, (float)r.Y, (float)r.Z, (float)index);
        absorb = new Vector4((float)a.X, (float)a.Y, (float)a.Z, 0f);
        backscatter = new Vector4((float)bb.X, (float)bb.Y, (float)bb.Z, 0f);
    }

    /// <summary>Blue-green rather than anything else: seawater's colours (Kerbin's stock ocean is 0.27, 0.35, 0.38).</summary>
    private static bool LooksLikeWater(Color c) => c.b >= c.r * 1.1f && c.g >= c.r * 1.05f;
}
