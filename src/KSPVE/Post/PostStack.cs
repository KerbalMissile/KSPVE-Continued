using System;
using KSPVE.Core;
using KSPVE.Pure.Post;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Post;

/// <summary>
/// KSPVE's camera effects on one scene camera. Ambient occlusion is injected after deferred lighting
/// (so transparent clouds, plumes and particles are not darkened); everything else runs as an image
/// effect at the end of the camera: linearize â†’ motion blur â†’ depth of field â†’ bloom â†’ grade and
/// tonemap â†’ sharpen, split-compare and dither. Added by <see cref="PostAttacher"/> as the camera's
/// last component; any failure disables the stack for that camera and falls back to a plain copy.
/// </summary>
[DisallowMultipleComponent]
internal sealed class PostStack : MonoBehaviour
{
    private const int MaxBloomLevels = 8;

    private static readonly int FrameId = Shader.PropertyToID("_KSPVE_Frame");
    private static readonly int ExposureMulId = Shader.PropertyToID("_KSPVE_ExposureMul");
    private static readonly int UseAutoExposureId = Shader.PropertyToID("_KSPVE_UseAutoExposure");
    private static readonly int AutoExposureTexId = Shader.PropertyToID("_KSPVE_AutoExposureTex");
    private static readonly int PrevExposureId = Shader.PropertyToID("_KSPVE_PrevExposure");
    private static readonly int AeParamsId = Shader.PropertyToID("_KSPVE_AEParams");
    private static readonly int AeSpeedId = Shader.PropertyToID("_KSPVE_AESpeed");
    private static readonly int BloomTexId = Shader.PropertyToID("_KSPVE_BloomTex");
    private static readonly int BloomBaseId = Shader.PropertyToID("_KSPVE_BloomBase");
    private static readonly int BloomThresholdId = Shader.PropertyToID("_KSPVE_BloomThreshold");
    private static readonly int BloomScatterId = Shader.PropertyToID("_KSPVE_BloomScatter");
    private static readonly int BloomIntensityId = Shader.PropertyToID("_KSPVE_BloomIntensity");
    private static readonly int LensDirtTexId = Shader.PropertyToID("_KSPVE_LensDirtTex");
    private static readonly int LensDirtId = Shader.PropertyToID("_KSPVE_LensDirt");
    private static readonly int SpikesId = Shader.PropertyToID("_KSPVE_Spikes");
    private static readonly int SensorSmearId = Shader.PropertyToID("_KSPVE_SensorSmear");
    private static readonly int Wb0Id = Shader.PropertyToID("_KSPVE_WB0");
    private static readonly int Wb1Id = Shader.PropertyToID("_KSPVE_WB1");
    private static readonly int Wb2Id = Shader.PropertyToID("_KSPVE_WB2");
    private static readonly int ContrastId = Shader.PropertyToID("_KSPVE_Contrast");
    private static readonly int SaturationId = Shader.PropertyToID("_KSPVE_Saturation");
    private static readonly int VibranceId = Shader.PropertyToID("_KSPVE_Vibrance");
    private static readonly int NightVisionId = Shader.PropertyToID("_KSPVE_NightVision");
    private static readonly int TonemapperId = Shader.PropertyToID("_KSPVE_Tonemapper");
    private static readonly int VignetteId = Shader.PropertyToID("_KSPVE_Vignette");
    private static readonly int GrainId = Shader.PropertyToID("_KSPVE_Grain");
    private static readonly int ChromaticAberrationId = Shader.PropertyToID("_KSPVE_ChromaticAberration");
    private static readonly int OriginalTexId = Shader.PropertyToID("_KSPVE_OriginalTex");
    private static readonly int SharpenId = Shader.PropertyToID("_KSPVE_Sharpen");
    private static readonly int SplitId = Shader.PropertyToID("_KSPVE_Split");
    private static readonly int BroadcastId = Shader.PropertyToID("_KSPVE_Broadcast");
    private static readonly int MotionBlurId = Shader.PropertyToID("_KSPVE_MotionBlur");
    private static readonly int DofId = Shader.PropertyToID("_KSPVE_Dof");
    private static readonly int DofTexId = Shader.PropertyToID("_KSPVE_DofTex");
    private static readonly int DebugModeId = Shader.PropertyToID("_KSPVE_DebugMode");
    private static readonly int WorldToViewId = Shader.PropertyToID("_KSPVE_WorldToView");
    private static readonly int TanHalfFovId = Shader.PropertyToID("_KSPVE_TanHalfFov");
    private static readonly int AoId = Shader.PropertyToID("_KSPVE_AO");
    private static readonly int BlurDirectionId = Shader.PropertyToID("_KSPVE_BlurDirection");
    private static readonly int SunDirectionViewId = Shader.PropertyToID("_KSPVE_SunDirectionView");
    private static readonly int ContactShadowId = Shader.PropertyToID("_KSPVE_ContactShadow");
    private static readonly int AoTempA = Shader.PropertyToID("_KSPVE_AOTempA");
    private static readonly int AoTempB = Shader.PropertyToID("_KSPVE_AOTempB");

    private readonly RenderTexture[] _down = new RenderTexture[MaxBloomLevels];
    private readonly RenderTexture[] _up = new RenderTexture[MaxBloomLevels];

    private Camera _camera;
    private Material _uber, _final, _bloom, _exposure, _motionBlur, _dof, _ao, _debug;
    private bool _materialsTried, _materialsOk, _failed;
    private RenderTexture _logLuminance, _exposureA, _exposureB;
    private bool _exposureReset = true;
    private CommandBuffer _aoBuffer;
    private bool _aoAttached;
    private int _aoWidth, _aoHeight;
    private double _wbTemperature = double.NaN, _wbTint = double.NaN;
    private Vector4 _wb0, _wb1, _wb2;

    /// <summary>True on the local scene camera ("Camera 00"), which has depth, G-buffer and motion vectors.</summary>
    public bool IsLocal { get; set; }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        Engine engine = Engine.Instance;
        if (engine == null || _failed || !IsLocal) return;
        PostProfile p = engine.Post;
        try
        {
            SetViewGlobals(p);
            if (p.AmbientOcclusion || p.DepthOfField || p.ContactShadows) _camera.depthTextureMode |= DepthTextureMode.Depth;
            if (p.MotionBlur) _camera.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            if (p.AmbientOcclusion && p.AoIntensity > 0 && EnsureMaterials()) AttachAmbientOcclusion();
        }
        catch (Exception ex)
        {
            Fail("preparing the frame", ex);
        }
    }

    private void OnPostRender()
    {
        DetachAmbientOcclusion();
    }

    private void OnDisable()
    {
        DetachAmbientOcclusion();
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        Engine engine = Engine.Instance;
        if (_failed || engine == null || !EnsureMaterials())
        {
            Graphics.Blit(src, dst);
            return;
        }
        try
        {
            Render(src, dst, engine.Post);
        }
        catch (Exception ex)
        {
            Fail("rendering", ex);
            Graphics.Blit(src, dst);
        }
    }

    private void OnDestroy()
    {
        DetachAmbientOcclusion();
        _aoBuffer?.Release();
        DestroyIfAlive(_uber); DestroyIfAlive(_final); DestroyIfAlive(_bloom); DestroyIfAlive(_exposure);
        DestroyIfAlive(_motionBlur); DestroyIfAlive(_dof); DestroyIfAlive(_ao); DestroyIfAlive(_debug);
        DestroyIfAlive(_logLuminance); DestroyIfAlive(_exposureA); DestroyIfAlive(_exposureB);
    }

    private void Fail(string what, Exception ex)
    {
        _failed = true;
        DetachAmbientOcclusion();
        Log.Exception($"Post-processing failed while {what} on {name}; disabled for this camera", ex);
    }

    private bool EnsureMaterials()
    {
        if (_materialsTried) return _materialsOk;
        _materialsTried = true;
        _uber = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/Uber");
        _final = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/Final");
        _bloom = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/Bloom");
        _exposure = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/Exposure");
        _motionBlur = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/MotionBlur");
        _dof = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Post/DepthOfField");
        _ao = ShaderLibrary.CreateMaterial("Hidden/KSPVE/AmbientOcclusion");
        _debug = ShaderLibrary.CreateMaterial("Hidden/KSPVE/Debug/GBufferView");
        _materialsOk = _uber != null && _final != null && _bloom != null && _exposure != null
                       && _motionBlur != null && _dof != null && _ao != null && _debug != null;
        if (!_materialsOk) Log.Error("KSPVE post-processing shaders are missing from the shader bundle; post-processing is off.");
        return _materialsOk;
    }

    // ---------------------------------------------------------------------------------------------
    // Per-frame globals used by AO and contact shadows (both run inside the camera's render).

    private void SetViewGlobals(PostProfile p)
    {
        float frame = Time.frameCount % 4096;
        Shader.SetGlobalMatrix(WorldToViewId, _camera.worldToCameraMatrix);
        float tanY = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Shader.SetGlobalVector(TanHalfFovId, new Vector4(tanY * _camera.aspect, tanY, 0f, 0f));
        Shader.SetGlobalVector(AoId, new Vector4((float)p.AoRadius, (float)p.AoIntensity, 1000f, frame));
        Shader.SetGlobalVector(ContactShadowId, new Vector4((float)p.ContactShadowLength, 0.5f, 300f, frame));

        Light sun = PostAttacher.SunLight;
        if (sun != null)
        {
            Vector3 toSun = _camera.worldToCameraMatrix.MultiplyVector(-sun.transform.forward);
            Shader.SetGlobalVector(SunDirectionViewId, new Vector4(toSun.x, toSun.y, -toSun.z, 0f).normalized);
        }
    }

    private void AttachAmbientOcclusion()
    {
        int w = _camera.pixelWidth, h = _camera.pixelHeight;
        if (_aoBuffer == null) _aoBuffer = new CommandBuffer { name = "KSPVE Ambient Occlusion" };
        if (w != _aoWidth || h != _aoHeight) BuildAmbientOcclusionBuffer(w, h);
        if (_aoAttached) return;
        _camera.AddCommandBuffer(CameraEvent.AfterLighting, _aoBuffer);
        _aoAttached = true;
    }

    private void BuildAmbientOcclusionBuffer(int w, int h)
    {
        _aoWidth = w;
        _aoHeight = h;
        int hw = Mathf.Max(1, w / 2), hh = Mathf.Max(1, h / 2);
        _aoBuffer.Clear();
        _aoBuffer.GetTemporaryRT(AoTempA, hw, hh, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
        _aoBuffer.GetTemporaryRT(AoTempB, hw, hh, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
        _aoBuffer.Blit(Texture2D.whiteTexture, AoTempA, _ao, 0);
        _aoBuffer.SetGlobalVector(BlurDirectionId, new Vector4(1f / hw, 0f, 0f, 0f));
        _aoBuffer.Blit(AoTempA, AoTempB, _ao, 1);
        _aoBuffer.SetGlobalVector(BlurDirectionId, new Vector4(0f, 1f / hh, 0f, 0f));
        _aoBuffer.Blit(AoTempB, AoTempA, _ao, 2);
        _aoBuffer.Blit(AoTempA, BuiltinRenderTextureType.CameraTarget, _ao, 3);
        _aoBuffer.ReleaseTemporaryRT(AoTempA);
        _aoBuffer.ReleaseTemporaryRT(AoTempB);
    }

    private void DetachAmbientOcclusion()
    {
        if (!_aoAttached || _camera == null) return;
        _camera.RemoveCommandBuffer(CameraEvent.AfterLighting, _aoBuffer);
        _aoAttached = false;
    }

    // ---------------------------------------------------------------------------------------------
    // Image-effect chain.

    private void Render(RenderTexture src, RenderTexture dst, PostProfile p)
    {
        int w = src.width, h = src.height;
        Shader.SetGlobalFloat(FrameId, Time.frameCount % 4096);

        if (p.DebugView != DebugView.Off && IsLocal)
        {
            _debug.SetInt(DebugModeId, (int)p.DebugView - 1);
            Graphics.SetRenderTarget(dst);
            _debug.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            return;
        }

        Shader.SetGlobalFloat(ExposureMulId, (float)p.ExposureMultiplier);
        if (p.AutoExposure)
        {
            UpdateAutoExposure(src, p);
            Shader.SetGlobalFloat(UseAutoExposureId, 1f);
            Shader.SetGlobalTexture(AutoExposureTexId, _exposureA);
        }
        else
        {
            _exposureReset = true;
            Shader.SetGlobalFloat(UseAutoExposureId, 0f);
            Shader.SetGlobalTexture(AutoExposureTexId, Texture2D.blackTexture);
        }

        RenderTexture work = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(src, work, _uber, 0);

        if (p.MotionBlur && p.MotionBlurStrength > 0 && IsLocal)
        {
            RenderTexture blurred = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
            _motionBlur.SetVector(MotionBlurId, new Vector4((float)p.MotionBlurStrength, 0.05f, 0f, Time.frameCount % 4096));
            Graphics.Blit(work, blurred, _motionBlur, 0);
            RenderTexture.ReleaseTemporary(work);
            work = blurred;
        }

        if (p.DepthOfField && p.DofStrength > 0 && IsLocal) work = RenderDepthOfField(work, p);

        int bloomLevels = 0;
        RenderTexture bloom = null;
        bool bloomNeeded = p.Bloom && (p.BloomIntensity > 0 || p.LensDirt > 0 || p.Spikes > 0 || p.SensorSmear > 0);
        if (bloomNeeded) bloom = RenderBloom(work, p, out bloomLevels);

        SetGradingParameters(p, bloom != null);
        _uber.SetTexture(BloomTexId, bloom != null ? (Texture)bloom : Texture2D.blackTexture);

        RenderTextureFormat ldrFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB2101010)
            ? RenderTextureFormat.ARGB2101010
            : RenderTextureFormat.ARGB32;
        RenderTexture graded = RenderTexture.GetTemporary(w, h, 0, ldrFormat);
        Graphics.Blit(work, graded, _uber, 1);

        _final.SetTexture(OriginalTexId, src);
        _final.SetFloat(SharpenId, (float)p.Sharpen);
        _final.SetFloat(SplitId, p.CompareSplit ? 0.5f : -1f);
        _final.SetFloat(BroadcastId, (float)p.Broadcast);
        Graphics.Blit(graded, dst, _final, 0);

        RenderTexture.ReleaseTemporary(graded);
        RenderTexture.ReleaseTemporary(work);
        for (int i = 0; i < bloomLevels; i++) RenderTexture.ReleaseTemporary(_down[i]);
        for (int i = 0; i < bloomLevels - 1; i++) RenderTexture.ReleaseTemporary(_up[i]);
    }

    private void SetGradingParameters(PostProfile p, bool hasBloom)
    {
        if (p.Temperature != _wbTemperature || p.Tint != _wbTint)
        {
            double[,] m = p.WhiteBalanceMatrix();
            _wb0 = new Vector4((float)m[0, 0], (float)m[0, 1], (float)m[0, 2], 0f);
            _wb1 = new Vector4((float)m[1, 0], (float)m[1, 1], (float)m[1, 2], 0f);
            _wb2 = new Vector4((float)m[2, 0], (float)m[2, 1], (float)m[2, 2], 0f);
            _wbTemperature = p.Temperature;
            _wbTint = p.Tint;
        }
        _uber.SetVector(Wb0Id, _wb0);
        _uber.SetVector(Wb1Id, _wb1);
        _uber.SetVector(Wb2Id, _wb2);
        _uber.SetFloat(BloomIntensityId, hasBloom ? (float)p.BloomIntensity : 0f);
        _uber.SetFloat(LensDirtId, hasBloom ? (float)p.LensDirt : 0f);
        _uber.SetFloat(SpikesId, hasBloom ? (float)p.Spikes : 0f);
        _uber.SetFloat(SensorSmearId, hasBloom ? (float)p.SensorSmear : 0f);
        _uber.SetTexture(LensDirtTexId, LensDirt.Texture);
        _uber.SetFloat(ContrastId, (float)p.Contrast);
        _uber.SetFloat(SaturationId, (float)p.Saturation);
        _uber.SetFloat(VibranceId, (float)p.Vibrance);
        _uber.SetFloat(NightVisionId, (float)p.NightVision);
        _uber.SetFloat(TonemapperId, (int)p.Tonemapper);
        _uber.SetFloat(VignetteId, (float)p.Vignette);
        _uber.SetFloat(GrainId, (float)p.Grain);
        _uber.SetFloat(ChromaticAberrationId, (float)p.ChromaticAberration);
    }

    private RenderTexture RenderBloom(RenderTexture work, PostProfile p, out int levels)
    {
        RenderTextureFormat format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float)
            ? RenderTextureFormat.RGB111110Float
            : RenderTextureFormat.ARGBHalf;
        float threshold = (float)p.BloomThreshold;
        float knee = threshold * (float)p.BloomKnee + 1e-5f;
        _bloom.SetVector(BloomThresholdId, new Vector4(threshold, threshold - knee, knee * 2f, 0.25f / knee));
        _bloom.SetFloat(BloomScatterId, (float)p.BloomScatter);

        int w = Mathf.Max(1, work.width / 2), h = Mathf.Max(1, work.height / 2);
        levels = 0;
        RenderTexture previous = work;
        while (levels < MaxBloomLevels)
        {
            RenderTexture level = RenderTexture.GetTemporary(w, h, 0, format);
            level.filterMode = FilterMode.Bilinear;
            Graphics.Blit(previous, level, _bloom, levels == 0 ? 0 : 1);
            _down[levels++] = level;
            previous = level;
            if (w < 16 || h < 16) break;
            w /= 2;
            h /= 2;
        }

        RenderTexture low = _down[levels - 1];
        for (int i = levels - 2; i >= 0; i--)
        {
            RenderTexture up = RenderTexture.GetTemporary(_down[i].width, _down[i].height, 0, format);
            up.filterMode = FilterMode.Bilinear;
            _bloom.SetTexture(BloomBaseId, _down[i]);
            Graphics.Blit(low, up, _bloom, 2);
            _up[i] = up;
            low = up;
        }
        return low;
    }

    private RenderTexture RenderDepthOfField(RenderTexture work, PostProfile p)
    {
        int hw = Mathf.Max(1, work.width / 2), hh = Mathf.Max(1, work.height / 2);
        _dof.SetVector(DofId, new Vector4((float)p.DofFocusDistance, (float)p.DofStrength, 16f, 0f));

        RenderTexture half = RenderTexture.GetTemporary(hw, hh, 0, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(work, half, _dof, 0);
        RenderTexture bokeh = RenderTexture.GetTemporary(hw, hh, 0, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(half, bokeh, _dof, 1);
        _dof.SetTexture(DofTexId, bokeh);
        RenderTexture result = RenderTexture.GetTemporary(work.width, work.height, 0, RenderTextureFormat.ARGBHalf);
        Graphics.Blit(work, result, _dof, 2);

        RenderTexture.ReleaseTemporary(half);
        RenderTexture.ReleaseTemporary(bokeh);
        RenderTexture.ReleaseTemporary(work);
        return result;
    }

    private void UpdateAutoExposure(RenderTexture src, PostProfile p)
    {
        if (_logLuminance == null)
        {
            _logLuminance = new RenderTexture(256, 256, 0, RenderTextureFormat.RGHalf)
            {
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
                name = "KSPVE Log Luminance"
            };
            _logLuminance.Create();
            _exposureA = CreateExposureTarget("KSPVE Exposure A");
            _exposureB = CreateExposureTarget("KSPVE Exposure B");
        }

        Graphics.Blit(src, _logLuminance, _exposure, 0);
        _exposure.SetTexture(PrevExposureId, _exposureA);
        _exposure.SetVector(AeParamsId, new Vector4((float)p.AutoExposureKey, (float)p.AutoExposureMinEv, (float)p.AutoExposureMaxEv, 0f));
        _exposure.SetVector(AeSpeedId, new Vector4((float)p.AutoExposureDarkSpeed, (float)p.AutoExposureBrightSpeed, Time.unscaledDeltaTime, _exposureReset ? 1f : 0f));
        Graphics.Blit(_logLuminance, _exposureB, _exposure, 1);

        RenderTexture swap = _exposureA;
        _exposureA = _exposureB;
        _exposureB = swap;
        _exposureReset = false;
    }

    private static RenderTexture CreateExposureTarget(string targetName)
    {
        var rt = new RenderTexture(1, 1, 0, RenderTextureFormat.RFloat)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave,
            name = targetName
        };
        rt.Create();
        return rt;
    }

    private static void DestroyIfAlive(UnityEngine.Object o)
    {
        if (o != null) Destroy(o);
    }
}
