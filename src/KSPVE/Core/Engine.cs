using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using KSPVE.Post;
using KSPVE.Pure;
using KSPVE.Pure.Config;
using KSPVE.Pure.Post;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Core;

/// <summary>Process-lifetime root of KSPVE. Created once at the main menu and kept across scenes.</summary>
public sealed class Engine : MonoBehaviour
{
    private readonly List<ConfigIssue> _configIssues = new List<ConfigIssue>();
    private SessionMarker _marker;
    private long _frameIndex;
    private bool _contextErrorLogged;
    private bool _hdrErrorLogged;
    private bool _postErrorLogged;
    private SettingsWindow _window;

    public static Engine Instance { get; private set; }
    public static string Version => typeof(Engine).Assembly.GetName().Version.ToString();

    public LaunchOptions Options { get; private set; }
    public SettingsValues Settings { get; private set; }
    public IReadOnlyList<ConfigIssue> ConfigIssues => _configIssues;
    public SafeModeReason SafeMode { get; private set; }
    public FrameOfReference Frame { get; private set; }
    public SceneContext Context { get; } = new SceneContext();
    internal HdrCameras Hdr { get; } = new HdrCameras();

    /// <summary>Live post-processing parameters, edited by the settings window.</summary>
    public PostProfile Post { get; internal set; } = PostProfile.Defaults();

    /// <summary>True when KSPVE may render at all (not in safe mode, master switch on, shaders loaded).</summary>
    public bool RenderingAllowed => SafeMode == SafeModeReason.None && Settings.Get(CoreSettings.Enabled) && ShaderLibrary.Loaded;

    internal static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("KSPVE.Engine");
        DontDestroyOnLoad(go);
        go.AddComponent<Engine>();
    }

    private void Awake()
    {
        Instance = this;
        Options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        LoadSettings();

        _marker = new SessionMarker(Paths.SessionMarkerPath);
        SafeMode = SafeModePolicy.Decide(Options.ForceSafeMode, _marker.PreviousSessionUnclean, _marker.PreviousSessionErrorCount);
        _marker.BeginSession();
        Log.ErrorLogged += _marker.RecordError;

        Frame = new FrameOfReference();
        Hdr.Enabled = SafeMode == SafeModeReason.None
            && Settings.Get(CoreSettings.Enabled)
            && Settings.Get(CoreSettings.HdrCameras);

        try
        {
            ShaderLibrary.Load();
        }
        catch (Exception ex)
        {
            Log.Exception("Loading the shader bundle failed; KSPVE rendering stays off", ex);
        }

        try
        {
            KSPVE.Space.BlackHoles.Load(_configIssues);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading KSPVE_BLACKHOLE nodes failed; black holes are off", ex);
        }

        _window = gameObject.AddComponent<SettingsWindow>();
        if (Options.SelfTest) gameObject.AddComponent<KSPVE.Dev.SelfTestRunner>();

        Log.Info($"KSPVE {Version} started (safe mode: {SafeMode}, self-test: {Options.SelfTest}, shaders: {ShaderLibrary.Loaded}). Settings: Ctrl+Alt+V.");
    }

    private void FixedUpdate()
    {
        KSPVE.Space.DiskDragDriver.FixedUpdate();
        KSPVE.Space.SolidRockDriver.FixedUpdate();
    }

    private void LateUpdate()
    {
        KSPVE.Dev.BodyDump.TryWrite(Options);
        Frame.BeginFrame();
        try
        {
            SceneContextBuilder.Fill(Context, ++_frameIndex, Frame);
        }
        catch (Exception ex)
        {
            if (!_contextErrorLogged)
            {
                _contextErrorLogged = true;
                Log.Exception("Building the scene context failed (logged once)", ex);
            }
        }

        try
        {
            Hdr.Apply();
        }
        catch (Exception ex)
        {
            if (!_hdrErrorLogged)
            {
                _hdrErrorLogged = true;
                Log.Exception("Applying HDR to scene cameras failed (logged once)", ex);
            }
        }

        try
        {
            PostAttacher.Update(Post, RenderingAllowed);
            KSPVE.Space.TidalDisruptions.Update(RenderingAllowed && Post.BlackHoles);
            KSPVE.Space.BlackHoles.Update(Post, RenderingAllowed);
            KSPVE.Space.DiskLight.Update(RenderingAllowed && Post.BlackHoles);
            KSPVE.Space.OrbitPathOverlay.Update(SafeMode == SafeModeReason.None && Settings.Get(CoreSettings.Enabled) && Post.OrbitPaths);
        }
        catch (Exception ex)
        {
            if (!_postErrorLogged)
            {
                _postErrorLogged = true;
                Log.Exception("Attaching post-processing failed (logged once)", ex);
            }
        }
    }

    /// <summary>Writes KSPVE_POST, the HDR switch and the black hole tweaks to PluginData/user.cfg, keeping any other user nodes.</summary>
    internal bool SaveUserSettings()
    {
        try
        {
            CfgNode core = KspConfig.LoadUserNodes().Find(n => n.Name == CoreSettings.Schema.NodeName)
                           ?? new CfgNode(CoreSettings.Schema.NodeName);
            core.SetValue(CoreSettings.HdrCameras.Key, Hdr.Enabled ? "True" : "False");
            KspConfig.SaveUserNodes(new[] { Post.ToCfg(), core, KSPVE.Space.BlackHoles.UserNode() });
            Log.Info("Saved settings to " + Paths.UserConfigPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception("Saving settings failed", ex);
            return false;
        }
    }

    internal void ResetPost()
    {
        Post = PostProfile.Defaults();
    }

    /// <summary>Supersampled screenshot to the KSP Screenshots folder, with the KSPVE window hidden.</summary>
    internal void TakeScreenshot(int superSize)
    {
        StartCoroutine(CaptureScreenshot(superSize));
    }

    private IEnumerator CaptureScreenshot(int superSize)
    {
        _window.HiddenForScreenshot = true;
        yield return null;
        string folder = Path.Combine(Paths.KspRoot, "Screenshots");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"KSPVE_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        ScreenCapture.CaptureScreenshot(path, superSize);
        Log.Info($"Screenshot ({superSize}x) queued: {path}");
        yield return null;
        yield return null;
        _window.HiddenForScreenshot = false;
    }

    private void LoadSettings()
    {
        _configIssues.Clear();
        CfgNode core = null, post = null;
        try
        {
            core = KspConfig.LoadMerged(CoreSettings.Schema.NodeName);
            post = KspConfig.LoadMerged(PostSettings.Schema.NodeName);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading KSPVE config failed; using defaults", ex);
        }
        Settings = CoreSettings.Schema.Resolve(core, _configIssues);
        Post = PostProfile.From(PostSettings.Schema.Resolve(post, _configIssues));
        Log.MinLevel = Settings.Get(CoreSettings.MinLogLevel);
        foreach (ConfigIssue issue in _configIssues) Log.Warning("Config " + issue);
    }

    private void OnApplicationQuit()
    {
        _marker?.EndSession();
    }

    private void OnDestroy()
    {
        if (_marker != null) Log.ErrorLogged -= _marker.RecordError;
        Frame?.Dispose();
        if (Instance == this) Instance = null;
    }
}
