using System;
using KSPVE.Clouds;
using KSPVE.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Seasonal;

/// <summary>
/// Snow on the terrain around the craft: a command buffer on the local camera, before the deferred lighting pass, that
/// copies the three G-buffers and writes them again with snow in (SnowGBuffer.shader: albedo, smoothness, specular, the
/// normal for glints). It is independent of any other pass on the G-buffer (it reads and writes the whole of it, in turn).
/// Where the snow goes comes from the snow maps (the snow line, now), the slope and noise; craft are told from the ground by a
/// hidden copy of the camera that sees only the terrain layer and records its depth.
/// </summary>
[DisallowMultipleComponent]
internal sealed class LocalSnowRenderer : MonoBehaviour
{
    /// <summary>KSP's "Local Scenery" layer, where the planet's terrain quads and the space centre's buildings are.</summary>
    private const int TerrainLayer = 15;
    /// <summary>Above this height (m) the terrain is too far to be worth it.</summary>
    private const double MaxCameraAltitude = 120000.0;

    private static readonly int G0 = Shader.PropertyToID("_KSPVE_SnowG0"), G1 = Shader.PropertyToID("_KSPVE_SnowG1"), G2 = Shader.PropertyToID("_KSPVE_SnowG2");
    private static readonly int SceneDepth = Shader.PropertyToID("_KSPVE_SnowSceneDepth"), TerrainDepth = Shader.PropertyToID("_KSPVE_TerrainDepth");
    private static readonly int MapsId = Shader.PropertyToID("_KSPVE_SnowMaps"), AuxId = Shader.PropertyToID("_KSPVE_SnowAux");
    private static readonly int RightId = Shader.PropertyToID("_KSPVE_SnowRight"), UpId = Shader.PropertyToID("_KSPVE_SnowUp"), ForwardId = Shader.PropertyToID("_KSPVE_SnowForward");
    private static readonly int BodyId = Shader.PropertyToID("_KSPVE_SnowBody");
    private static readonly int AxisXId = Shader.PropertyToID("_KSPVE_SnowAxisX"), AxisYId = Shader.PropertyToID("_KSPVE_SnowAxisY"), AxisZId = Shader.PropertyToID("_KSPVE_SnowAxisZ");
    private static readonly int LookId = Shader.PropertyToID("_KSPVE_SnowLook"), Look2Id = Shader.PropertyToID("_KSPVE_SnowLook2");

    private Camera _camera;
    private CommandBuffer _buffer;
    private bool _attached;
    private Material _material;
    private Shader _depthShader;
    private Camera _mask;
    private RenderTexture _maskTexture;
    private int _width, _height;
    private bool _tried;

    public void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private bool Prepare()
    {
        if (_tried) return _material != null && _depthShader != null;
        _tried = true;
        _material = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Climate/SnowGBuffer");
        _depthShader = ShaderLibrary.Find("Hidden/KSPVE/Climate/TerrainDepth");
        if (_material == null || _depthShader == null)
            Log.Warning("Snow: the terrain snow shaders are missing from the shader bundle; snow on the local terrain stays off.");
        return _material != null && _depthShader != null;
    }

    public void OnPreRender()
    {
        if (_camera == null || !Prepare()) return;
        CelestialBody body = FlightGlobals.currentMainBody;
        bool wanted = ClimateConfig.LocalSnow && body != null && body.atmosphere && body.hasSolidSurface && HighLogic.LoadedSceneIsFlight
                      && _camera.actualRenderingPath == RenderingPath.DeferredShading;
        SnowTextures textures = null;
        BodyClimateState state = null;
        if (wanted)
        {
            Vector3d from = (Vector3d)_camera.transform.position - body.position;
            wanted = from.magnitude - body.Radius < MaxCameraAltitude;
            if (wanted)
            {
                state = SnowCover.StateOf(body);
                textures = SnowCover.TexturesOf(body);
                wanted = state != null && textures != null && textures.Maps != null && textures.Aux != null;
            }
        }
        if (!wanted)
        {
            Detach();
            return;
        }

        int w = _camera.pixelWidth, h = _camera.pixelHeight;
        if (_buffer == null || w != _width || h != _height) Build(w, h);
        Attach();

        // Uniforms (read when the buffer runs, later this frame).
        Vector3d offset = (Vector3d)_camera.transform.position - body.position;
        CloudWorld.Basis(body, out Vector3d x, out Vector3d y, out Vector3d z);
        Shader.SetGlobalVector(BodyId, new Vector4((float)offset.x, (float)offset.y, (float)offset.z, (float)body.Radius));
        Shader.SetGlobalVector(AxisXId, new Vector4((float)x.x, (float)x.y, (float)x.z, 0f));
        Shader.SetGlobalVector(AxisYId, new Vector4((float)y.x, (float)y.y, (float)y.z, 0f));
        Shader.SetGlobalVector(AxisZId, new Vector4((float)z.x, (float)z.y, (float)z.z, 0f));
        Matrix4x4 projection = _camera.projectionMatrix;
        float tanX = 1f / projection.m00, tanY = 1f / projection.m11;
        Transform t = _camera.transform;
        Shader.SetGlobalVector(RightId, t.right * tanX);
        Shader.SetGlobalVector(UpId, t.up * tanY);
        Shader.SetGlobalVector(ForwardId, t.forward);
        Shader.SetGlobalVector(LookId, new Vector4((float)ClimateConfig.SnowAlbedo, (float)ClimateConfig.SnowSparkle, ClimateConfig.KeepVehiclesClear ? 1f : 0f, 1f));
        // Snow lies on slopes up to about 40 degrees from level (cos 0.77), smoothness 0.25 (powder), glacier ice a little duller.
        Shader.SetGlobalVector(Look2Id, new Vector4(0.95f, 0.25f, 6.0f, 0.77f));
        Shader.SetGlobalTexture(MapsId, textures.Maps);
        Shader.SetGlobalTexture(AuxId, textures.Aux);
        _camera.depthTextureMode |= DepthTextureMode.Depth;
        if (ClimateConfig.KeepVehiclesClear) RenderTerrainDepth(w, h);
    }

    private void Build(int w, int h)
    {
        bool wasAttached = _attached;
        if (wasAttached) Detach();
        _width = w;
        _height = h;
        if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Snow" };
        _buffer.Clear();
        RenderTextureFormat normalFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010) ? RenderTextureFormat.ARGB2101010 : RenderTextureFormat.ARGB32;
        _buffer.GetTemporaryRT(G0, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        _buffer.GetTemporaryRT(G1, -1, -1, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        _buffer.GetTemporaryRT(G2, -1, -1, 0, FilterMode.Point, normalFormat, RenderTextureReadWrite.Default);
        _buffer.GetTemporaryRT(SceneDepth, -1, -1, 0, FilterMode.Point, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        _buffer.Blit(BuiltinRenderTextureType.GBuffer0, G0);
        _buffer.Blit(BuiltinRenderTextureType.GBuffer1, G1);
        _buffer.Blit(BuiltinRenderTextureType.GBuffer2, G2);
        _buffer.Blit(BuiltinRenderTextureType.Depth, SceneDepth);
        _buffer.SetRenderTarget(new RenderTargetIdentifier[] { BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1, BuiltinRenderTextureType.GBuffer2 }, BuiltinRenderTextureType.CameraTarget);
        _buffer.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
        _buffer.ReleaseTemporaryRT(G0);
        _buffer.ReleaseTemporaryRT(G1);
        _buffer.ReleaseTemporaryRT(G2);
        _buffer.ReleaseTemporaryRT(SceneDepth);
        if (wasAttached) Attach();
    }

    private void Attach()
    {
        if (_attached || _buffer == null) return;
        _camera.AddCommandBuffer(CameraEvent.BeforeLighting, _buffer);
        _attached = true;
    }

    private void Detach()
    {
        if (!_attached || _camera == null) return;
        _camera.RemoveCommandBuffer(CameraEvent.BeforeLighting, _buffer);
        _attached = false;
    }

    private void RenderTerrainDepth(int w, int h)
    {
        if (_mask == null)
        {
            var go = new GameObject("KSPVE Terrain Mask") { hideFlags = HideFlags.HideAndDontSave };
            _mask = go.AddComponent<Camera>();
            _mask.enabled = false;
        }
        if (_maskTexture == null || _maskTexture.width != w || _maskTexture.height != h)
        {
            if (_maskTexture != null) { _maskTexture.Release(); Destroy(_maskTexture); }
            _maskTexture = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) { name = "KSPVE terrain depth", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            _maskTexture.Create();
        }
        _mask.CopyFrom(_camera);
        _mask.cullingMask = 1 << TerrainLayer;
        _mask.clearFlags = CameraClearFlags.SolidColor;
        _mask.backgroundColor = new Color(1e6f, 1e6f, 1e6f, 1e6f);
        _mask.renderingPath = RenderingPath.Forward;
        _mask.allowHDR = false;
        _mask.allowMSAA = false;
        _mask.depthTextureMode = DepthTextureMode.None;
        _mask.targetTexture = _maskTexture;
        _mask.SetReplacementShader(_depthShader, string.Empty);
        _mask.Render();
        _mask.targetTexture = null;
        Shader.SetGlobalTexture(TerrainDepth, _maskTexture);
    }

    public void OnDisable()
    {
        Detach();
    }

    public void OnDestroy()
    {
        Detach();
        if (_buffer != null) { _buffer.Release(); _buffer = null; }
        if (_maskTexture != null) { _maskTexture.Release(); Destroy(_maskTexture); }
        if (_mask != null) Destroy(_mask.gameObject);
        if (_material != null) Destroy(_material);
    }
}

/// <summary>Puts a <see cref="LocalSnowRenderer"/> on the local cameras.</summary>
internal static class LocalSnow
{
    private static readonly string[] CameraNames = { "Camera 00", "Camera 01" };
    private static Camera[] _cameras = new Camera[32];

    public static void Update(bool allowed)
    {
        bool enabled = allowed && ClimateConfig.LocalSnow;
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || Array.IndexOf(CameraNames, cam.name) < 0) continue;
            var renderer = cam.GetComponent<LocalSnowRenderer>();
            if (renderer == null)
            {
                if (!enabled) continue;
                renderer = cam.gameObject.AddComponent<LocalSnowRenderer>();
                Log.Info("Terrain snow attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
        }
    }
}
