using System;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Post;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.RayTracing;

/// <summary>
/// Ray-traced bounce light on the local camera. Just before the camera culls, the scene the rays see is placed around
/// it (<see cref="RtScene"/>) and KSP's flat ambient is taken away (the traced sky and bounces replace it); after the
/// deferred lighting one bounce ray per low-resolution pixel is traced; before the opaque image effects (when the
/// camera's motion vectors exist) the light is accumulated over time, filtered and added to the scene, times each
/// surface's albedo. The ambient is put back once the camera has rendered, so KSP never sees the change.
/// </summary>
[DisallowMultipleComponent]
internal sealed class RtGiRenderer : MonoBehaviour
{
    private const string GiShader = "Hidden/KSPVE/RayTracing/Gi";
    private const int PassTrace = 0, PassAccumulate = 1, PassAtrous = 2, PassComposite = 3, PassPrepare = 4, PassDebug = 5, PassMoments = 6, PassG = 7;

    private static readonly int RtNodes = Shader.PropertyToID("_RtNodes"), RtTriangles = Shader.PropertyToID("_RtTriangles"),
        RtTriangleData = Shader.PropertyToID("_RtTriangleData"), RtInstances = Shader.PropertyToID("_RtInstances"),
        RtTopNodes = Shader.PropertyToID("_RtTopNodes"), RtTopOrder = Shader.PropertyToID("_RtTopOrder"),
        RtMaterialTable = Shader.PropertyToID("_RtMaterials"), RtAtlas = Shader.PropertyToID("_RtAtlas"),
        RtSky = Shader.PropertyToID("_RtSky"), RtPlanetAlbedo = Shader.PropertyToID("_RtPlanetAlbedo"),
        RtInfo = Shader.PropertyToID("_RtInfo"), RtSunDir = Shader.PropertyToID("_RtSunDir"), RtSunColor = Shader.PropertyToID("_RtSunColor"),
        RtSkyInfo = Shader.PropertyToID("_RtSkyInfo"), RtSkyColor = Shader.PropertyToID("_RtSkyColor"),
        RtPlanet = Shader.PropertyToID("_RtPlanet"), RtPlanetInfo = Shader.PropertyToID("_RtPlanetInfo"),
        RtPlanetX = Shader.PropertyToID("_RtPlanetX"), RtPlanetY = Shader.PropertyToID("_RtPlanetY"), RtPlanetZ = Shader.PropertyToID("_RtPlanetZ"),
        RtRight = Shader.PropertyToID("_RtRight"), RtUp = Shader.PropertyToID("_RtUp"), RtForward = Shader.PropertyToID("_RtForward"),
        RtLowRes = Shader.PropertyToID("_RtLowRes"), RtParams = Shader.PropertyToID("_RtParams"), RtParams2 = Shader.PropertyToID("_RtParams2"),
        RtCurrent = Shader.PropertyToID("_RtCurrent"), RtHistory = Shader.PropertyToID("_RtHistory"),
        RtMomentsHistory = Shader.PropertyToID("_RtMomentsHistory"), RtPrevG = Shader.PropertyToID("_RtPrevG"),
        RtG = Shader.PropertyToID("_RtG"), RtAccumulated = Shader.PropertyToID("_RtAccumulated"), RtMoments = Shader.PropertyToID("_RtMoments"),
        RtFilterIn = Shader.PropertyToID("_RtFilterIn"), RtFiltered = Shader.PropertyToID("_RtFiltered");

    public static readonly RtScene Scene = new RtScene();

    public RtSettings Settings;

    private Camera _camera;
    private Material _material;
    private CommandBuffer _trace, _resolve;
    private RenderTexture _current;
    private readonly RenderTexture[] _history = new RenderTexture[2], _moments = new RenderTexture[2], _g = new RenderTexture[2], _filter = new RenderTexture[2];
    private int _width, _height, _flip;
    private bool _historyValid, _attached, _ambientTaken;
    private Color _ambientLight;
    private AmbientMode _ambientMode;
    private SphericalHarmonicsL2 _ambientProbe;
    private float _ambientIntensity;
    private FlightReflectionProbe _probe;
    private float _nextProbeSearch;
    private GameScenes _scene;

    /// <summary>What the last frame did, for the settings window.</summary>
    public string Status { get; private set; } = string.Empty;

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        _material = ShaderLibrary.CreateMaterial(GiShader);
        if (_material == null)
        {
            Status = "the ray tracing shader is missing (rebuild the shader bundle)";
            enabled = false;
            return;
        }
        _trace = new CommandBuffer { name = "KSPVE RT trace" };
        _resolve = new CommandBuffer { name = "KSPVE RT resolve" };
        _camera.AddCommandBuffer(CameraEvent.AfterLighting, _trace);
        _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _resolve);
        _attached = true;
        _historyValid = false;
    }

    private void OnDisable()
    {
        GiveAmbientBack();
        if (_attached && _camera != null)
        {
            _camera.RemoveCommandBuffer(CameraEvent.AfterLighting, _trace);
            _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _resolve);
        }
        _attached = false;
        _trace?.Release();
        _resolve?.Release();
        _trace = _resolve = null;
        ReleaseTargets();
        if (_material != null) Destroy(_material);
        _material = null;
    }

    private void OnPreCull()
    {
        _trace?.Clear();
        _resolve?.Clear();
        if (_material == null || Settings == null) return;
        try
        {
            Prepare();
        }
        catch (Exception ex)
        {
            Status = "failed: " + ex.Message;
            Log.Exception("Ray-traced lighting failed this frame", ex);
            _trace.Clear();
            _resolve.Clear();
            GiveAmbientBack();
        }
    }

    private void OnPostRender()
    {
        GiveAmbientBack();
    }

    private void Prepare()
    {
        if (HighLogic.LoadedScene != _scene)
        {
            _scene = HighLogic.LoadedScene;
            Scene.Reset();
            _historyValid = false;
        }
        int frame = Time.frameCount;
        Scene.Update(_camera, Settings.Reach, frame);
        RtGeometry g = Scene.Geometry;
        Status = $"{Scene.InstanceCount} placed of {Scene.CandidateCount} found; {g.MeshCount} meshes, {g.TriangleCount:N0} triangles"
                 + (g.Building > 0 ? $", {g.Building} building" : string.Empty)
                 + (g.Unreadable > 0 ? $", {g.Unreadable} without a CPU copy (skipped)" : string.Empty)
                 + $"; {Scene.Materials.Count} materials, {Scene.Materials.Slices} textures";
        if (!Scene.Ready) return;

        _camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        int width = Math.Max(1, (int)(_camera.pixelWidth * Settings.Resolution));
        int height = Math.Max(1, (int)(_camera.pixelHeight * Settings.Resolution));
        if (width != _width || height != _height || _current == null || !_current.IsCreated()) Allocate(width, height);
        if (Engine.Instance != null && Engine.Instance.Context.OriginShiftsThisFrame > 0) _historyValid = false;

        if (Settings.ReplaceAmbient) TakeAmbient();
        SetUniforms(frame);
        Record();
        _historyValid = true;
        _flip ^= 1;
    }

    private void SetUniforms(int frame)
    {
        CommandBuffer c = _trace;
        RtGeometry g = Scene.Geometry;
        c.SetGlobalBuffer(RtNodes, g.Nodes);
        c.SetGlobalBuffer(RtTriangles, g.Triangles);
        c.SetGlobalBuffer(RtTriangleData, g.Data);
        c.SetGlobalBuffer(RtInstances, Scene.Instances);
        c.SetGlobalBuffer(RtTopNodes, Scene.TopNodes);
        c.SetGlobalBuffer(RtTopOrder, Scene.TopOrder);
        c.SetGlobalBuffer(RtMaterialTable, Scene.Materials.Table);
        if (Scene.Materials.Atlas != null) c.SetGlobalTexture(RtAtlas, Scene.Materials.Atlas);
        c.SetGlobalVector(RtInfo, new Vector4(Scene.InstanceCount, (float)Settings.Reach, frame % 4096, RtMaterials.Linear ? 1f : 0f));

        // The view, in camera-relative world directions with forward at z = 1.
        Transform t = _camera.transform;
        float tanY = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * _camera.aspect;
        c.SetGlobalVector(RtForward, t.forward);
        c.SetGlobalVector(RtRight, t.right * tanX);
        c.SetGlobalVector(RtUp, t.up * tanY);
        c.SetGlobalVector(RtLowRes, new Vector4(_width, _height, 1f / _width, 1f / _height));
        c.SetGlobalVector(RtParams, new Vector4(Settings.History, 1f, 4f, (float)Settings.Strength));
        c.SetGlobalVector(RtParams2, new Vector4(_historyValid ? 1f : 0f, 0f, 0f, 0f));

        // The sun as the scene lights with it, and its real angular radius.
        Light sun = PostAttacher.SunLight;
        Vector4 sunDir = Vector4.zero, sunColor = Vector4.zero;
        CelestialBody body = FlightGlobals.currentMainBody;
        if (sun != null && sun.isActiveAndEnabled)
        {
            Vector3 d = -sun.transform.forward;
            float radius = 0.00465f;
            CelestialBody star = body != null ? CloudWorld.StarOf(body) : null;
            if (star != null)
            {
                double distance = (star.position - (Vector3d)t.position).magnitude;
                if (distance > star.Radius) radius = (float)Math.Asin(star.Radius / distance);
            }
            sunDir = new Vector4(d.x, d.y, d.z, radius);
            Color col = sun.color;
            if (RtMaterials.Linear) col = col.linear * (GraphicsSettings.lightsUseLinearIntensity ? sun.intensity : Mathf.GammaToLinearSpace(sun.intensity));
            else col *= sun.intensity;
            sunColor = new Vector4(col.r, col.g, col.b, Settings.SunShadows ? 1f : 0f);
        }
        c.SetGlobalVector(RtSunDir, sunDir);
        c.SetGlobalVector(RtSunColor, sunColor);

        // The sky: what KSP's reflection probe sees, or (without one) the ambient KSP would have used.
        if (_probe == null && Time.unscaledTime >= _nextProbeSearch)
        {
            _nextProbeSearch = Time.unscaledTime + 2f;
            _probe = FindObjectOfType<FlightReflectionProbe>();
        }
        Texture cube = null;
        float intensity = 1f;
        if (_probe != null && _probe.probeComponent != null && _probe.probeComponent.isActiveAndEnabled)
        {
            ReflectionProbe rp = _probe.probeComponent;
            cube = rp.realtimeTexture != null ? (Texture)rp.realtimeTexture : rp.texture;
            intensity = rp.intensity;
        }
        if (cube != null && cube.dimension == TextureDimension.Cube)
        {
            int mips = Math.Max(1, (int)Math.Log(Math.Max(cube.width, 1), 2.0));
            c.SetGlobalTexture(RtSky, cube);
            c.SetGlobalVector(RtSkyInfo, new Vector4(1f, 1f, intensity, Math.Max(mips - 2, 0)));
        }
        else c.SetGlobalVector(RtSkyInfo, Vector4.zero);
        Color ambient = _ambientTaken ? _ambientLight : RenderSettings.ambientLight;
        if (RtMaterials.Linear) ambient = ambient.linear;
        c.SetGlobalVector(RtSkyColor, ambient);

        // The planet past the loaded scene.
        if (Settings.PlanetBounce && body != null)
        {
            Vector3d centre = body.position - (Vector3d)t.position;
            CloudWorld.Basis(body, out Vector3d x, out Vector3d y, out Vector3d z);
            Texture map = null;
            var scaled = body.scaledBody != null ? body.scaledBody.GetComponent<Renderer>() : null;
            if (scaled != null && scaled.sharedMaterial != null) map = scaled.sharedMaterial.mainTexture;
            if (map != null) c.SetGlobalTexture(RtPlanetAlbedo, map);
            c.SetGlobalVector(RtPlanet, new Vector4((float)centre.x, (float)centre.y, (float)centre.z, (float)body.Radius));
            c.SetGlobalVector(RtPlanetInfo, new Vector4(1f, (float)Settings.Reach, 0f, map != null ? 1f : 0f));
            c.SetGlobalVector(RtPlanetX, (Vector3)x);
            c.SetGlobalVector(RtPlanetY, (Vector3)y);
            c.SetGlobalVector(RtPlanetZ, (Vector3)z);
        }
        else c.SetGlobalVector(RtPlanetInfo, Vector4.zero);
    }

    private void Record()
    {
        int now = _flip, before = _flip ^ 1;
        Texture black = Texture2D.blackTexture;

        // After lighting: trace, and keep this frame's depth and normal at the trace's resolution.
        _trace.Blit(black, _current, _material, PassTrace);

        // Before the opaque image effects: accumulate, filter, add.
        CommandBuffer r = _resolve;
        r.SetGlobalTexture(RtCurrent, _current);
        r.SetGlobalTexture(RtHistory, _history[before]);
        r.SetGlobalTexture(RtMomentsHistory, _moments[before]);
        r.SetGlobalTexture(RtPrevG, _g[before]);
        r.Blit(black, _history[now], _material, PassAccumulate);
        r.Blit(black, _moments[now], _material, PassMoments);
        r.Blit(black, _g[now], _material, PassG);
        r.SetGlobalTexture(RtAccumulated, _history[now]);
        r.SetGlobalTexture(RtMoments, _moments[now]);
        r.SetGlobalTexture(RtG, _g[now]);
        r.Blit(black, _filter[0], _material, PassPrepare);
        int source = 0;
        for (int i = 0; i < Settings.FilterPasses; i++)
        {
            r.SetGlobalVector(RtParams, new Vector4(Settings.History, 1 << i, 4f, (float)Settings.Strength));
            r.SetGlobalTexture(RtFilterIn, _filter[source]);
            r.Blit(black, _filter[source ^ 1], _material, PassAtrous);
            source ^= 1;
        }
        r.SetGlobalTexture(RtFiltered, _filter[source]);
        r.Blit(black, BuiltinRenderTextureType.CameraTarget, _material, Settings.Debug == RtSettings.DebugView.LightOnly ? PassDebug : PassComposite);
    }

    private void Allocate(int width, int height)
    {
        ReleaseTargets();
        _width = width;
        _height = height;
        _current = Target("KSPVE RT current", RenderTextureFormat.ARGBHalf);
        for (int i = 0; i < 2; i++)
        {
            _history[i] = Target("KSPVE RT history", RenderTextureFormat.ARGBHalf);
            _moments[i] = Target("KSPVE RT moments", RenderTextureFormat.RGHalf);
            _g[i] = Target("KSPVE RT depth and normal", RenderTextureFormat.ARGBFloat);
            _filter[i] = Target("KSPVE RT filter", RenderTextureFormat.ARGBHalf);
        }
        _historyValid = false;
    }

    private RenderTexture Target(string name, RenderTextureFormat format)
    {
        var rt = new RenderTexture(_width, _height, 0, format, RenderTextureReadWrite.Linear)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        rt.Create();
        return rt;
    }

    private void ReleaseTargets()
    {
        void Free(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }
        Free(ref _current);
        for (int i = 0; i < 2; i++)
        {
            Free(ref _history[i]);
            Free(ref _moments[i]);
            Free(ref _g[i]);
            Free(ref _filter[i]);
        }
        _width = _height = 0;
    }

    /// <summary>KSP's flat ambient light off for this camera's render: the traced sky and bounces take its place.</summary>
    private void TakeAmbient()
    {
        if (_ambientTaken) return;
        _ambientLight = RenderSettings.ambientLight;
        _ambientMode = RenderSettings.ambientMode;
        _ambientProbe = RenderSettings.ambientProbe;
        _ambientIntensity = RenderSettings.ambientIntensity;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        _ambientTaken = true;
    }

    private void GiveAmbientBack()
    {
        if (!_ambientTaken) return;
        RenderSettings.ambientMode = _ambientMode;
        RenderSettings.ambientLight = _ambientLight;
        RenderSettings.ambientProbe = _ambientProbe;
        RenderSettings.ambientIntensity = _ambientIntensity;
        _ambientTaken = false;
    }
}
