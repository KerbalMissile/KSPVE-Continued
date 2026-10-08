using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using KSPVE.Core;
using KSPVE.Pure;
using KSPVE.Pure.Config;
using KSPVE.Pure.Post;
using UnityEngine;

namespace KSPVE.Dev;

/// <summary>
/// Runs when KSP is started with -kspve-selftest: captures the hook census and scene context at
/// the main menu, the space center and on the launch pad in flight, checks the flight context,
/// writes PluginData/selftest/report.json and quits KSP.
/// </summary>
internal sealed class SelfTestRunner : MonoBehaviour
{
    private const string BenchSaveFolder = "KSPVE_Bench";
    private const string CraftRelativePath = "Ships/VAB/Kerbal X.craft";
    private const string FlagUrl = "Squad/Flags/default";
    private const string LaunchSite = "LaunchPad";
    private const float TimeoutSeconds = 900f;
    private const int MaxRecordedErrors = 50;

    private readonly JsonWriter _json = new JsonWriter();
    private readonly List<string> _failures = new List<string>();
    private readonly List<string> _kspveErrors = new List<string>();
    private readonly List<string> _screenshots = new List<string>();
    private float _startTime;
    private string _stage = "starting";
    private bool _finished;

    private void Awake()
    {
        Application.logMessageReceived += OnLogMessage;
    }

    private IEnumerator Start()
    {
        _startTime = Time.realtimeSinceStartup;
        Engine engine = Engine.Instance;

        _json.BeginObject()
            .Property("kspveVersion", Engine.Version)
            .Property("unityVersion", Application.unityVersion)
            .Property("graphicsDevice", SystemInfo.graphicsDeviceVersion)
            .Property("gpu", SystemInfo.graphicsDeviceName)
            .Property("supportsComputeShaders", SystemInfo.supportsComputeShaders)
            .Property("supportsAsyncGPUReadback", SystemInfo.supportsAsyncGPUReadback)
            .Property("safeMode", engine.SafeMode.ToString());
        _json.Name("configIssues").BeginArray();
        foreach (ConfigIssue issue in engine.ConfigIssues) _json.Value(issue.ToString());
        _json.EndArray();
        _json.Name("stages").BeginArray();

        _stage = "settling in MAINMENU";
        yield return WaitFrames(120);
        CaptureStage("MAINMENU");

        _stage = "loading benchmark save";
        if (!LoadBenchSave()) { Finish(); yield break; }
        _stage = "waiting for SPACECENTER";
        while (HighLogic.LoadedScene != GameScenes.SPACECENTER) yield return null;
        yield return WaitFrames(240);
        CaptureStage("SPACECENTER");

        _stage = "launching craft";
        if (!LaunchCraft()) { Finish(); yield break; }
        _stage = "waiting for FLIGHT";
        while (HighLogic.LoadedScene != GameScenes.FLIGHT || !FlightGlobals.ready || FlightGlobals.ActiveVessel == null) yield return null;
        _stage = "settling in FLIGHT";
        yield return WaitFrames(engine.Settings.Get(CoreSettings.SelfTestFlightFrames));

        // A/B the same view: stock KSP, HDR cameras only, KSPVE defaults, KSPVE with experimental effects.
        if (!ShaderLibrary.Loaded) _failures.Add("shader bundle did not load");
        bool hdrSetting = engine.Hdr.Enabled;
        PostProfile saved = engine.Post;
        PostProfile profile = PostProfile.Defaults();
        engine.Post = profile;

        _stage = "capturing stock screenshot";
        engine.Hdr.Enabled = false;
        profile.Enabled = false;
        yield return WaitFrames(60);
        yield return CaptureScreenshot("flight-stock");

        _stage = "capturing HDR screenshot";
        engine.Hdr.Enabled = true;
        yield return WaitFrames(120);
        yield return CaptureScreenshot("flight-hdr");
        if (engine.Hdr.HdrCameraCount < 2) _failures.Add($"expected 2 HDR scene cameras in flight, found {engine.Hdr.HdrCameraCount}");

        _stage = "capturing KSPVE screenshot";
        profile.Enabled = true;
        yield return WaitFrames(120);
        yield return CaptureScreenshot("flight-kspve");

        _stage = "capturing experimental screenshot";
        profile.AmbientOcclusion = true;
        profile.ContactShadows = true;
        profile.Spikes = 0.3;
        profile.LensDirt = 0.3;
        yield return WaitFrames(120);
        yield return CaptureScreenshot("flight-experimental");

        CaptureStage("FLIGHT");
        CheckFlightContext(engine.Context);
        engine.Hdr.Enabled = hdrSetting;
        engine.Post = saved;

        Finish();
    }

    private void Update()
    {
        if (_finished || Time.realtimeSinceStartup - _startTime <= TimeoutSeconds) return;
        _failures.Add($"timed out after {TimeoutSeconds:F0}s while {_stage}");
        Finish();
    }

    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private IEnumerator CaptureScreenshot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            string path = Path.Combine(Path.GetDirectoryName(Paths.SelfTestReportPath), name + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, texture.EncodeToJPG(92));
            _screenshots.Add(path);
            Log.Info("Self-test screenshot " + path);
        }
        catch (Exception ex)
        {
            _failures.Add($"screenshot {name} failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (texture != null) Destroy(texture);
        }
    }

    private bool LoadBenchSave()
    {
        string sfs = Path.Combine(Path.Combine(Paths.SavesDir, BenchSaveFolder), "persistent.sfs");
        if (!File.Exists(sfs))
        {
            _failures.Add($"benchmark save missing: {sfs} (tools/selftest.ps1 creates it)");
            return false;
        }
        Game game = GamePersistence.LoadGame("persistent", BenchSaveFolder, true, false);
        if (game == null)
        {
            _failures.Add("GamePersistence.LoadGame returned null for " + BenchSaveFolder);
            return false;
        }
        HighLogic.SaveFolder = BenchSaveFolder;
        HighLogic.CurrentGame = game;
        game.startScene = GameScenes.SPACECENTER;
        game.Start();
        return true;
    }

    private bool LaunchCraft()
    {
        string craft = Path.Combine(Paths.KspRoot, CraftRelativePath);
        if (!File.Exists(craft))
        {
            _failures.Add("stock craft missing: " + craft);
            return false;
        }
        VesselCrewManifest manifest = VesselCrewManifest.FromConfigNode(ConfigNode.Load(craft));
        FlightDriver.StartWithNewLaunch(craft, FlagUrl, LaunchSite, manifest);
        return true;
    }

    private void CaptureStage(string label)
    {
        // Each stage is serialised into its own writer so an exception cannot corrupt the report.
        string stageJson;
        try
        {
            var w = new JsonWriter();
            w.BeginObject()
                .Property("stage", label)
                .Property("elapsedSeconds", Time.realtimeSinceStartup - _startTime);
            w.Name("census");
            HookCensus.Write(w, label);
            w.Name("sceneContext");
            SceneContextJson.Write(w, Engine.Instance.Context);
            w.EndObject();
            stageJson = w.ToString();
        }
        catch (Exception ex)
        {
            _failures.Add($"capturing stage {label} threw {ex.GetType().Name}: {ex.Message}");
            stageJson = new JsonWriter().BeginObject().Property("stage", label).Property("error", ex.ToString()).EndObject().ToString();
        }
        _json.RawValue(stageJson);
        Log.Info("Self-test captured stage " + label);
    }

    private void CheckFlightContext(SceneContext ctx)
    {
        if (ctx.Scene != GameScenes.FLIGHT) _failures.Add("scene context reports scene " + ctx.Scene + " in flight");
        if (ctx.View != ViewMode.Flight) _failures.Add("scene context reports view " + ctx.View + " on the launch pad");
        string home = Planetarium.fetch != null && Planetarium.fetch.Home != null ? Planetarium.fetch.Home.bodyName : null;
        if (ctx.MainBodyName != home) _failures.Add($"main body is '{ctx.MainBodyName}', expected home body '{home}'");
        if (!ctx.InAtmosphere) _failures.Add("vessel on the launch pad is not reported in atmosphere");
        if (ctx.Stars.Count == 0)
        {
            _failures.Add("no stars in scene context");
        }
        else
        {
            double lux = ctx.Stars[0].IlluminanceLux;
            if (!(lux > 50_000.0 && lux < 200_000.0)) _failures.Add($"home-star illuminance {lux} lux is outside 50k-200k");
        }
        if (ctx.Bodies.Count == 0) _failures.Add("no bodies in scene context");
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        bool ours = (condition != null && condition.Contains("[KSPVE]")) || (stackTrace != null && stackTrace.Contains("KSPVE."));
        if (ours && _kspveErrors.Count < MaxRecordedErrors) _kspveErrors.Add(condition);
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        StopAllCoroutines();
        Application.logMessageReceived -= OnLogMessage;

        _json.EndArray(); // stages
        _json.Name("screenshots").BeginArray();
        foreach (string s in _screenshots) _json.Value(s);
        _json.EndArray();
        _json.Name("kspveErrors").BeginArray();
        foreach (string e in _kspveErrors) _json.Value(e);
        _json.EndArray();
        _json.Name("failures").BeginArray();
        foreach (string f in _failures) _json.Value(f);
        _json.EndArray();
        _json.Property("passed", _failures.Count == 0 && _kspveErrors.Count == 0);
        _json.EndObject();

        string path = Paths.SelfTestReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + ".tmp";
        File.WriteAllText(temp, _json.ToString());
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);

        Log.Info("Self-test finished; report written to " + path);
        Application.Quit();
    }
}
