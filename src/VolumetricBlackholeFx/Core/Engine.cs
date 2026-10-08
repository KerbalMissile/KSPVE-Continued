using System;
using System.Collections.Generic;
using VolumetricBlackholeFx.Pure;
using VolumetricBlackholeFx.Pure.Config;
using VolumetricBlackholeFx.Space;
using VolumetricBlackholeFx.UI;
using UnityEngine;

namespace VolumetricBlackholeFx.Core;

/// <summary>Process-lifetime root of the mod. Created once at the main menu and kept across scenes.</summary>
public sealed class Engine : MonoBehaviour
{
    private readonly List<ConfigIssue> _configIssues = new List<ConfigIssue>();
    private bool _updateErrorLogged;

    public static Engine Instance { get; private set; }
    public static string Version => typeof(Engine).Assembly.GetName().Version.ToString(3);

    public SettingsValues Settings { get; private set; }

    /// <summary>Master switch (the settings window's "Draw black holes").</summary>
    public bool Enabled { get; internal set; }

    /// <summary>Brightness multiplier for every accretion disk.</summary>
    public double Brightness { get; internal set; }

    /// <summary>Render scale of the black holes (0.25..1).</summary>
    public double RenderScale { get; internal set; }

    /// <summary>True when the black holes may be drawn (switched on and the shaders loaded).</summary>
    public bool RenderingAllowed => Enabled && ShaderLibrary.Loaded;

    internal static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("VolumetricBlackholeFx.Engine");
        DontDestroyOnLoad(go);
        go.AddComponent<Engine>();
    }

    private void Awake()
    {
        Instance = this;
        LoadSettings();

        try
        {
            ShaderLibrary.Load();
        }
        catch (Exception ex)
        {
            Log.Exception("Loading the shader bundle failed; black holes stay off", ex);
        }

        try
        {
            BlackHoles.Load(_configIssues);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading VOLUMETRIC_BLACKHOLE nodes failed; black holes are off", ex);
        }

        gameObject.AddComponent<SettingsWindow>();
        Log.Info($"Volumetric Blackhole Fx {Version} started (shaders: {ShaderLibrary.Loaded}). Settings: Ctrl+Alt+V.");
    }

    private void FixedUpdate()
    {
        DiskDragDriver.FixedUpdate();
    }

    private void LateUpdate()
    {
        try
        {
            BlackHoles.Update(RenderingAllowed);
            DiskLight.Update(RenderingAllowed);
        }
        catch (Exception ex)
        {
            if (!_updateErrorLogged)
            {
                _updateErrorLogged = true;
                Log.Exception("Updating the black holes failed (logged once)", ex);
            }
        }
    }

    /// <summary>Writes the mod-wide settings and every hole's tweaks to PluginData/user.cfg, keeping any other user nodes.</summary>
    internal bool SaveUserSettings()
    {
        try
        {
            CfgNode core = KspConfig.LoadUserNodes().Find(n => n.Name == CoreSettings.Schema.NodeName)
                           ?? new CfgNode(CoreSettings.Schema.NodeName);
            core.SetValue(CoreSettings.Enabled.Key, Enabled ? "True" : "False");
            core.SetValue(CoreSettings.Brightness.Key, Brightness.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            core.SetValue(CoreSettings.RenderScale.Key, RenderScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            KspConfig.SaveUserNodes(new[] { core, BlackHoles.UserNode() });
            Log.Info("Saved settings to " + Paths.UserConfigPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception("Saving settings failed", ex);
            return false;
        }
    }

    /// <summary>The mod-wide settings back to their config values (not saved until Save).</summary>
    internal void ResetGlobals()
    {
        Enabled = Settings.Get(CoreSettings.Enabled);
        Brightness = Settings.Get(CoreSettings.Brightness);
        RenderScale = Settings.Get(CoreSettings.RenderScale);
    }

    private void LoadSettings()
    {
        _configIssues.Clear();
        CfgNode core = null;
        try
        {
            core = KspConfig.LoadMerged(CoreSettings.Schema.NodeName);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading the mod's config failed; using defaults", ex);
        }
        Settings = CoreSettings.Schema.Resolve(core, _configIssues);
        Log.MinLevel = Settings.Get(CoreSettings.MinLogLevel);
        ResetGlobals();
        foreach (ConfigIssue issue in _configIssues) Log.Warning("Config " + issue);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
