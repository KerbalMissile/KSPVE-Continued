using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.Numerics;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Aurora;

/// <summary>A body to draw this frame, with the camera's place relative to it (world axes, metres from the body's centre).</summary>
internal struct AuroraDraw
{
    public AuroraBody Body;
    public Vector3d CameraFromBody;
    /// <summary>Angular radius of the body's glowing layers as seen (rad), for ordering.</summary>
    public double AngularSize;
}

/// <summary>
/// Draws aurorae and airglow on one camera. On "Camera 00" (the local camera, for views from the ground and the air) the
/// march is added after the sky, limited by the camera's depth texture, so terrain hides it and the volumetric clouds drawn
/// later cover it; on "Camera ScaledSpace" (views from orbit, the map) it is added after transparents, the planet an exact sphere.
/// The march runs into a reduced-resolution buffer which is blended with the reprojected last frame (temporal accumulation) and
/// added to the camera's target. All the maths is in Aurora.shader (a port of KSPVE.Pure.Aurora.AuroraTracer).
/// </summary>
[DisallowMultipleComponent]
internal sealed class AuroraRenderer : MonoBehaviour
{
    private const string ShaderName = "Hidden/KSPVE/Aurora/Aurora";
    private const int MarchPass = 0, ResolvePass = 1, CompositePass = 2;

    private static readonly int LowId = Shader.PropertyToID("_KSPVE_AuLowRT");
    private static readonly int LowTexId = Shader.PropertyToID("_KSPVE_AuLow");
    private static readonly int HistoryTexId = Shader.PropertyToID("_KSPVE_AuHistory");
    private static readonly int LutId = Shader.PropertyToID("_KSPVE_AuLut");

    private static readonly int Au0 = Shader.PropertyToID("_KSPVE_Au0"), AuAxis = Shader.PropertyToID("_KSPVE_AuAxis"), AuNoon = Shader.PropertyToID("_KSPVE_AuNoon"),
        AuDawn = Shader.PropertyToID("_KSPVE_AuDawn"), AuSun = Shader.PropertyToID("_KSPVE_AuSun"), AuStorm0 = Shader.PropertyToID("_KSPVE_AuStorm0"),
        AuStorm1 = Shader.PropertyToID("_KSPVE_AuStorm1"), AuStorm2 = Shader.PropertyToID("_KSPVE_AuStorm2"), AuKinds0 = Shader.PropertyToID("_KSPVE_AuKinds0"),
        AuKinds1 = Shader.PropertyToID("_KSPVE_AuKinds1"), AuKinds2 = Shader.PropertyToID("_KSPVE_AuKinds2"), AuGlow = Shader.PropertyToID("_KSPVE_AuGlow"),
        AuTime = Shader.PropertyToID("_KSPVE_AuTime"), AuSlabs0 = Shader.PropertyToID("_KSPVE_AuSlabs0"), AuSlabs1 = Shader.PropertyToID("_KSPVE_AuSlabs1"),
        AuGlowLow = Shader.PropertyToID("_KSPVE_AuGlowLow"), AuGlowHigh = Shader.PropertyToID("_KSPVE_AuGlowHigh"), AuLutInfo = Shader.PropertyToID("_KSPVE_AuLutInfo"),
        AuAtmo = Shader.PropertyToID("_KSPVE_AuAtmo"), AuView = Shader.PropertyToID("_KSPVE_AuView"), AuCam = Shader.PropertyToID("_KSPVE_AuCam"),
        AuRight = Shader.PropertyToID("_KSPVE_AuRight"), AuUp = Shader.PropertyToID("_KSPVE_AuUp"), AuForward = Shader.PropertyToID("_KSPVE_AuForward"),
        AuPrevRight = Shader.PropertyToID("_KSPVE_AuPrevRight"), AuPrevUp = Shader.PropertyToID("_KSPVE_AuPrevUp"), AuPrevForward = Shader.PropertyToID("_KSPVE_AuPrevForward"),
        AuSpots = Shader.PropertyToID("_KSPVE_AuSpots"), AuCrust = Shader.PropertyToID("_KSPVE_AuCrust"), AuCrust2 = Shader.PropertyToID("_KSPVE_AuCrust2");

    /// <summary>True on "Camera 00", false on the scaled camera.</summary>
    public bool Local;

    private Camera _camera;
    private Material _material;
    private CommandBuffer _buffer;
    private bool _attached, _failed, _depthAdded;
    private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
    private readonly List<AuroraDraw> _draws = new List<AuroraDraw>();
    private readonly Vector4[] _spots = new Vector4[AuroraFrame.MaxSpots];
    private readonly Vector4[] _crust = new Vector4[AuroraFrame.MaxCrustal];
    private readonly Dictionary<CelestialBody, History> _history = new Dictionary<CelestialBody, History>();
    private long _frame;

    /// <summary>The accumulated image of a body and the camera it was made with.</summary>
    private sealed class History
    {
        public RenderTexture A, B;
        public Vector3 Right, Up, Forward;
        public int LastFrame = -100;
        public int Width, Height;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        if (_failed) return;
        AuroraFeature feature = AuroraFeature.Instance;
        Engine engine = Engine.Instance;
        if (feature == null || engine == null || !AuroraSettings.Enabled) { ClearBuffer(); return; }
        try
        {
            if (_material == null)
            {
                _material = ShaderLibrary.CreateMaterial(ShaderName);
                if (_material == null)
                {
                    _failed = true;
                    Log.Error("Aurora shader missing from the shader bundle; aurorae are off.");
                    return;
                }
            }
            if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Aurora" };
            _buffer.Clear();
            _frame++;

            feature.Select(_camera, Local, _draws);
            int drawn = 0;
            bool needDepth = false;
            foreach (AuroraDraw draw in _draws)
            {
                if (drawn >= 2) break;
                if (Render(draw, drawn, ref needDepth)) drawn++;
            }
            SetDepthTexture(needDepth && Local);
            if (!_attached)
            {
                _camera.AddCommandBuffer(Local ? CameraEvent.AfterSkybox : CameraEvent.AfterForwardAlpha, _buffer);
                _attached = true;
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            Detach();
            Log.Exception("Aurora rendering failed; disabled", ex);
        }
    }

    private void ClearBuffer()
    {
        if (_buffer != null) _buffer.Clear();
        SetDepthTexture(false);
    }

    private bool Render(AuroraDraw draw, int index, ref bool needDepth)
    {
        AuroraBody body = draw.Body;
        AuroraFrame f = body.Solve();
        if (f == null || !f.Valid) return false;
        body.Basis(out Vector3d bx, out Vector3d by, out Vector3d bz);
        Vector3d rel = draw.CameraFromBody;
        var camBody = new Vector3((float)Vector3d.Dot(rel, bx), (float)Vector3d.Dot(rel, by), (float)Vector3d.Dot(rel, bz));
        Transform view = _camera.transform;
        Vector3 right = ToBody(view.right, bx, by, bz), up = ToBody(view.up, bx, by, bz), forward = ToBody(view.forward, bx, by, bz);
        Matrix4x4 proj = _camera.projectionMatrix;
        float tanX = 1f / Mathf.Max(Mathf.Abs(proj.m00), 1e-4f), tanY = 1f / Mathf.Max(Mathf.Abs(proj.m11), 1e-4f);

        // What is drawn: the frame, with the player's choices applied.
        double arc = AuroraSettings.Arcs ? f.Arc : 0.0, steve = AuroraSettings.Steve ? f.Steve : 0.0;
        double diffuse = AuroraSettings.Diffuse ? f.Diffuse : 0.0, pulsating = AuroraSettings.Pulsating ? f.Pulsating : 0.0;
        double black = AuroraSettings.Black ? f.Black : 0.0, proton = AuroraSettings.Proton ? f.Proton : 0.0;
        double theta = AuroraSettings.PolarCap ? f.Theta : 0.0, sar = AuroraSettings.Sar ? f.Sar : 0.0;
        double global = AuroraSettings.Global ? f.Global : 0.0, crustal = AuroraSettings.Crustal && f.CrustalCount > 0 ? f.Crustal : 0.0;
        double giant = AuroraSettings.Arcs ? f.Giant : 0.0;
        int spotCount = AuroraSettings.Footprints && giant > 0.0 ? f.SpotCount : 0;
        bool airglow = AuroraSettings.Airglow;
        double glowScale = airglow ? 1.0 : 0.0;
        bool anything = arc > 0 || steve > 0 || diffuse > 0 || proton > 0 || theta > 0 || sar > 0 || global > 0 || crustal > 0 || giant > 0
            || (airglow && f.GlowGreen + f.GlowSodium + f.GlowHydroxyl + f.GlowRed > 0.0);
        if (!anything) return false;

        // Where it sits on the screen: nothing to draw when the glowing shell is wholly off the view.
        if (!InView(draw, body)) return false;

        double ut = f.Ut;
        SubstormState s = f.Storm;
        bool active = s.Phase == SubstormPhase.Expansion || s.Phase == SubstormPhase.Recovery;
        while (_blocks.Count <= index) _blocks.Add(new MaterialPropertyBlock());
        MaterialPropertyBlock b = _blocks[index];
        b.Clear();
        b.SetVector(Au0, new Vector4((float)f.Radius, (float)f.Centre.X, (float)f.Centre.Y, (float)f.Centre.Z));
        b.SetVector(AuAxis, new Vector4((float)f.Axis.X, (float)f.Axis.Y, (float)f.Axis.Z, (float)f.Oval.Kp));
        b.SetVector(AuNoon, new Vector4((float)f.Noon.X, (float)f.Noon.Y, (float)f.Noon.Z, (float)(f.HasField ? f.Oval.NightColatitude : 0.0)));
        b.SetVector(AuDawn, new Vector4((float)f.Dawn.X, (float)f.Dawn.Y, (float)f.Dawn.Z, (float)f.Oval.DayShift));
        b.SetVector(AuSun, new Vector4((float)f.Sun.X, (float)f.Sun.Y, (float)f.Sun.Z, (float)f.Oval.HalfWidth));
        b.SetVector(AuStorm0, new Vector4((float)s.OnsetAngle, (float)s.SurgeAngle, (float)s.EastAngle, (float)s.Strength));
        b.SetVector(AuStorm1, new Vector4((float)s.Growth, (float)s.Breakup, (float)s.Bulge, (float)pulsating));
        b.SetVector(AuStorm2, new Vector4(active ? 1f : 0f, 0f, 0f, 0f));
        b.SetVector(AuKinds0, new Vector4((float)arc, (float)f.HardShare, (float)f.SoftShare, (float)diffuse));
        b.SetVector(AuKinds1, new Vector4((float)proton, (float)black, (float)sar, (float)steve));
        b.SetVector(AuKinds2, new Vector4((float)theta, (float)global, (float)crustal, (float)giant));
        b.SetVector(AuGlow, new Vector4((float)(f.GlowGreen * glowScale), (float)(f.GlowSodium * glowScale), (float)(f.GlowHydroxyl * glowScale), (float)(f.GlowRed * glowScale)));
        b.SetVector(AuTime, new Vector4((float)Fract(ut / 600.0), (float)Fract(ut / 300.0), (float)Fract(ut / 3600.0), (float)f.Seed24));
        b.SetVector(AuSlabs0, new Vector4(body.SlabArc.x, body.SlabArc.y, body.SlabSmooth.x, body.SlabSmooth.y));
        b.SetVector(AuSlabs1, new Vector4(body.SlabSar.x, body.SlabSar.y, spotCount, AuroraSettings.Crustal ? f.CrustalCount : 0));
        b.SetVector(AuGlowLow, body.GlowLow);
        b.SetVector(AuGlowHigh, body.GlowHigh);
        b.SetVector(AuLutInfo, new Vector4((float)body.Lut.HeightLow, (float)body.Lut.HeightHigh, body.Lut.Samples, EmissionLut.Rows));
        b.SetVector(AuAtmo, new Vector4((float)body.Tau0.R, (float)body.Tau0.G, (float)body.Tau0.B, (float)body.ScaleHeight));
        b.SetVector(AuView, new Vector4((float)AuroraSettings.NightGain, (float)AuroraSettings.AuroraBoost, (float)AuroraSettings.AirglowBoost, _frame % 4096));
        b.SetVector(AuCam, new Vector4(camBody.x, camBody.y, camBody.z, 2f * tanY / Mathf.Max(_camera.pixelHeight, 1)));
        b.SetVector(AuRight, new Vector4(right.x, right.y, right.z, tanX));
        b.SetVector(AuUp, new Vector4(up.x, up.y, up.z, tanY));
        b.SetVector(AuForward, new Vector4(forward.x, forward.y, forward.z, Local ? 1f : 0f));
        for (int i = 0; i < AuroraFrame.MaxSpots; i++)
            _spots[i] = i < spotCount ? new Vector4((float)f.Spots[i].Colatitude, (float)f.Spots[i].Angle, (float)f.Spots[i].Brightness, (float)f.Spots[i].Tail) : Vector4.zero;
        b.SetVectorArray(AuSpots, _spots);
        var strengths = Vector4.zero;
        for (int i = 0; i < AuroraFrame.MaxCrustal; i++)
        {
            if (i < f.CrustalCount)
            {
                CrustalSpot c = f.CrustalSpots[i];
                _crust[i] = new Vector4((float)c.Direction.X, (float)c.Direction.Y, (float)c.Direction.Z, (float)c.Radius);
                strengths[i] = (float)c.Strength;
            }
            else _crust[i] = Vector4.zero;
        }
        b.SetVectorArray(AuCrust, _crust);
        b.SetVector(AuCrust2, strengths);

        // The buffers: a march at a fraction of the screen, blended with the last frame's.
        float scale = Mathf.Clamp((float)AuroraSettings.Resolution, 0.1f, 1f);
        int width = Mathf.Max(8, (int)(_camera.pixelWidth * scale)), height = Mathf.Max(8, (int)(_camera.pixelHeight * scale));
        _history.TryGetValue(body.Body, out History h);
        if (h == null) _history[body.Body] = h = new History();
        bool accumulate = AuroraSettings.Smoothing > 0.01;
        float weight = 0f;
        if (accumulate)
        {
            if (h.A == null || h.Width != width || h.Height != height) { ReleaseHistory(h); h.A = NewTarget(width, height); h.B = NewTarget(width, height); h.Width = width; h.Height = height; h.LastFrame = -100; }
            // The history only counts when it was made last frame, from nearly the same view.
            bool fresh = Time.frameCount - h.LastFrame <= 2 && Vector3.Dot(h.Forward, forward) > 0.995f;
            weight = fresh ? (float)Mathf.Clamp01((float)AuroraSettings.Smoothing) : 0f;
            b.SetVector(AuPrevRight, new Vector4(h.Right.x, h.Right.y, h.Right.z, weight));
            b.SetVector(AuPrevUp, new Vector4(h.Up.x, h.Up.y, h.Up.z, 0f));
            b.SetVector(AuPrevForward, new Vector4(h.Forward.x, h.Forward.y, h.Forward.z, 0f));
        }
        else
        {
            b.SetVector(AuPrevRight, Vector4.zero); b.SetVector(AuPrevUp, Vector4.zero); b.SetVector(AuPrevForward, Vector4.zero);
        }
        _buffer.SetGlobalTexture(LutId, body.LutTexture);
        _buffer.GetTemporaryRT(LowId, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        _buffer.SetRenderTarget(LowId);
        _buffer.DrawProcedural(Matrix4x4.identity, _material, MarchPass, MeshTopology.Triangles, 3, 1, b);
        _buffer.SetGlobalTexture(LowTexId, LowId);
        if (accumulate)
        {
            _buffer.SetGlobalTexture(HistoryTexId, h.A);
            _buffer.SetRenderTarget(h.B);
            _buffer.DrawProcedural(Matrix4x4.identity, _material, ResolvePass, MeshTopology.Triangles, 3, 1, b);
            _buffer.SetGlobalTexture(LowTexId, h.B);
        }
        _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        _buffer.DrawProcedural(Matrix4x4.identity, _material, CompositePass, MeshTopology.Triangles, 3, 1, b);
        _buffer.ReleaseTemporaryRT(LowId);
        if (accumulate)
        {
            RenderTexture swap = h.A; h.A = h.B; h.B = swap;
            h.Right = right; h.Up = up; h.Forward = forward; h.LastFrame = Time.frameCount;
        }
        needDepth = true;
        return true;
    }

    /// <summary>Whether any of the body's glowing shell can be in the camera's view (a cheap test on the shell's sphere).</summary>
    private bool InView(AuroraDraw draw, AuroraBody body)
    {
        double shell = body.Body.Radius + Math.Max(body.SlabSmooth.y, Math.Max(body.SlabArc.y, body.GlowHigh.w));
        double distance = draw.CameraFromBody.magnitude;
        if (distance < shell) return true;   // inside the shell (or below it): the rays are what matters
        Vector3d toBody = -draw.CameraFromBody / distance;
        double halfAngle = Math.Asin(Math.Min(1.0, shell / distance));
        double cosToAxis = Vector3d.Dot((Vector3d)_camera.transform.forward, toBody);
        double fovDiag = Math.Atan(Math.Sqrt(Math.Pow(Math.Tan(_camera.fieldOfView * 0.5 * Mathf.Deg2Rad), 2.0) * (1.0 + _camera.aspect * _camera.aspect)));
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosToAxis))) < fovDiag + halfAngle + 0.05;
    }

    private static Vector3 ToBody(Vector3 v, Vector3d x, Vector3d y, Vector3d z) =>
        new Vector3((float)Vector3d.Dot(v, x), (float)Vector3d.Dot(v, y), (float)Vector3d.Dot(v, z));

    private static double Fract(double x) => x - Math.Floor(x);

    private static RenderTexture NewTarget(int w, int h)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, name = "KSPVE Aurora history",
        };
        rt.Create();
        return rt;
    }

    private static void ReleaseHistory(History h)
    {
        if (h.A != null) { h.A.Release(); Destroy(h.A); h.A = null; }
        if (h.B != null) { h.B.Release(); Destroy(h.B); h.B = null; }
    }

    private void SetDepthTexture(bool on)
    {
        if (_camera == null) return;
        bool has = (_camera.depthTextureMode & DepthTextureMode.Depth) != 0;
        if (on && !has) { _camera.depthTextureMode |= DepthTextureMode.Depth; _depthAdded = true; }
        else if (!on && has && _depthAdded) { _camera.depthTextureMode &= ~DepthTextureMode.Depth; _depthAdded = false; }
    }

    private void OnDisable()
    {
        if (_buffer != null) _buffer.Clear();
        SetDepthTexture(false);
    }

    private void OnDestroy()
    {
        Detach();
        _buffer?.Release();
        if (_material != null) Destroy(_material);
        foreach (History h in _history.Values) ReleaseHistory(h);
        _history.Clear();
    }

    private void Detach()
    {
        if (!_attached || _camera == null) return;
        _camera.RemoveCommandBuffer(Local ? CameraEvent.AfterSkybox : CameraEvent.AfterForwardAlpha, _buffer);
        _attached = false;
        SetDepthTexture(false);
    }
}
