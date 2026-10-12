using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Space.Procedural;
using KSPVE.Pure.Space.Tde;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>
/// Runs the tidal disruption event a save asks for. Each save gets a KSPVE file,
/// GameData/KSPVE/PluginData/Saves/&lt;save&gt;.cfg (created with tde = off the first time the save is played), reread
/// whenever it changes, even while the game runs. When it says tde = on, the event starts: the game time is written back
/// as tdeStart, and from then on the event is a function of game time alone, so time warp, saves, reverts and restarts
/// all agree. The physics (<see cref="TidalDisruption"/>) is built off the main thread; each frame it is evaluated
/// for the current game time and handed to <see cref="BlackHoleRenderer"/>.
/// </summary>
internal static class TidalDisruptions
{
    private const float PollSeconds = 1f;

    private static string _save, _path;
    private static DateTime _stamp;
    private static float _nextPoll;
    private static TdeSaveState _state;
    private static TdeParameters _parameters = new TdeParameters();
    private static string _key;
    private static Task<TidalDisruption> _job;
    private static TidalDisruption _model;
    private static BlackHoleDefinition _hole;
    private static readonly TdeFrame _frame = new TdeFrame();
    private static double _frameUt = double.NaN, _frameScale = double.NaN;
    private static bool _errorLogged;

    /// <summary>The save's KSPVE file, or null outside a game.</summary>
    public static string FilePath => _path;
    public static TdeSaveState State => _state;
    public static TdeParameters Parameters => _parameters;
    public static TidalDisruption Model => _model;
    public static bool Building => _job != null;
    public static BlackHoleDefinition Hole => _hole;

    /// <summary>This hole's event as it is now, or null when it has none on screen.</summary>
    public static TdeFrame FrameFor(BlackHoleDefinition hole) => hole != null && hole == _hole && _model != null && _frame.Active ? _frame : null;

    /// <summary>The event's frame for whatever hole runs one (null when none does).</summary>
    public static TdeFrame Current => _model != null && _frame.Active ? _frame : null;

    /// <summary>Game time (s) since the event started, or NaN.</summary>
    public static double GameSecondsSinceStart =>
        _state?.StartUt is double start && FlightGlobals.fetch != null ? Planetarium.GetUniversalTime() - start : double.NaN;

    public static void Update(bool allowed)
    {
        try
        {
            Step(allowed);
        }
        catch (Exception ex)
        {
            _frame.Clear();
            if (_errorLogged) return;
            _errorLogged = true;
            Log.Exception("Running the tidal disruption event failed (logged once)", ex);
        }
    }

    private static void Step(bool allowed)
    {
        bool inGame = HighLogic.CurrentGame != null && !string.IsNullOrEmpty(HighLogic.SaveFolder)
                      && HighLogic.LoadedSceneHasPlanetarium && FlightGlobals.fetch != null;
        if (!inGame)
        {
            _frame.Clear();
            return;
        }

        string save = HighLogic.SaveFolder;
        if (save != _save)
        {
            _save = save;
            _path = Path.Combine(Path.Combine(Paths.PluginDataDir, "Saves"), save + ".cfg");
            _stamp = default;
            _nextPoll = 0f;
            _state = null;
            _key = null;
            _model = null;
            _job = null;
            _hole = null;
        }
        if (Time.realtimeSinceStartup >= _nextPoll)
        {
            _nextPoll = Time.realtimeSinceStartup + PollSeconds;
            Poll();
        }

        if (_job != null && _job.IsCompleted)
        {
            if (_job.IsFaulted) Log.Exception("Building the tidal disruption model failed", _job.Exception?.GetBaseException() ?? new Exception("unknown"));
            else
            {
                _model = _job.Result;
                Log.Info(Describe(_model));
            }
            _job = null;
        }

        if (!allowed || _state == null || !_state.On || !_state.StartUt.HasValue)
        {
            _frame.Clear();
            return;
        }
        BlackHoleDefinition hole = FindHole(_state.Body);
        CelestialBody body = hole?.Body;
        if (body == null)
        {
            _frame.Clear();
            return;
        }

        double horizon = hole.HorizonMeters(body);
        string key = string.Join("|", save, hole.BodyName, horizon.ToString("R", CultureInfo.InvariantCulture),
            _state.StartUt.Value.ToString("R", CultureInfo.InvariantCulture), _parameters.PhysicsKey());
        if (key != _key)
        {
            _key = key;
            _model = null;
            _hole = hole;
            TdeParameters p = _parameters.Clone();
            // Inclination is measured from the disk's plane and its turning sense, so a coplanar star comes in prograde.
            if (p.InclinationDeg >= 0.0 && hole.Spin < 0.0) p.InclinationDeg = 180.0 - p.InclinationDeg;
            ulong seed = StableHash.Combine(StableHash.Fnv1a64(save), (ulong)BitConverter.DoubleToInt64Bits(_state.StartUt.Value));
            _job = Task.Run(() => new TidalDisruption(p, horizon, seed));
            _frameUt = double.NaN;
        }
        if (_model == null)
        {
            _frame.Clear();
            return;
        }

        double ut = Planetarium.GetUniversalTime();
        if (ut != _frameUt || _parameters.TimeScale != _frameScale)
        {
            _frameUt = ut;
            _frameScale = _parameters.TimeScale;
            _model.Evaluate((ut - _state.StartUt.Value) * _parameters.TimeScale, _frame);
        }
    }

    /// <summary>Rereads the save's file when it changes; starts or resets the event as it says.</summary>
    private static void Poll()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            if (!File.Exists(_path))
            {
                File.WriteAllText(_path, new TdeSaveState().Write(_save));
                Log.Info($"Created {_path} (tde = off). Set tde = on there to start a tidal disruption event.");
            }
            DateTime stamp = File.GetLastWriteTimeUtc(_path);
            if (_state != null && stamp == _stamp) return;
            _stamp = stamp;

            ConfigNode file = ConfigNode.Load(_path);
            ConfigNode node = file?.GetNode(TdeSaveState.NodeName);
            bool wasOn = _state != null && _state.On;
            _state = TdeSaveState.Parse(node != null ? KspConfig.ToCfg(node) : null);

            // GameData's KSPVE_TDE (and user.cfg's), then this save's own settings on top.
            CfgNode merged = CfgNode.Overlay(KspConfig.LoadMerged(TdeSettings.Schema.NodeName), _state.Overrides);
            var issues = new List<ConfigIssue>();
            _parameters = TdeParameters.From(TdeSettings.Schema.Resolve(merged, issues));
            foreach (ConfigIssue issue in issues) Log.Warning("Config " + issue);

            bool changed = false;
            if (_state.On && !_state.StartUt.HasValue)
            {
                _state.StartUt = Planetarium.GetUniversalTime();
                changed = true;
                BlackHoleDefinition hole = FindHole(_state.Body);
                string where = hole?.Body != null ? hole.BodyName : "the black hole";
                Log.Info($"Tidal disruption event started at UT {_state.StartUt.Value:0} ({_save}).");
                ScreenMessages.PostScreenMessage($"KSPVE: a star is falling toward {where}", 6f, ScreenMessageStyle.UPPER_CENTER);
            }
            else if (!_state.On && _state.StartUt.HasValue)
            {
                _state.StartUt = null;
                changed = true;
                Log.Info($"Tidal disruption event switched off ({_save}); switching it on again starts a new one.");
            }
            else if (_state.On && !wasOn)
            {
                Log.Info($"Tidal disruption event resumes from UT {_state.StartUt.Value:0} ({_save}).");
            }
            if (changed)
            {
                File.WriteAllText(_path, _state.Write(_save));
                _stamp = File.GetLastWriteTimeUtc(_path);
            }
        }
        catch (Exception ex)
        {
            Log.Exception($"Reading {_path} failed; the tidal disruption event stays as it was", ex);
        }
    }

    /// <summary>Switches the event on or off from the settings window, through the same file.</summary>
    public static void SetSwitch(bool on)
    {
        if (_path == null || _state == null) return;
        _state.On = on;
        File.WriteAllText(_path, _state.Write(_save));
        _stamp = default;
        _nextPoll = 0f;
    }

    private static BlackHoleDefinition FindHole(string name)
    {
        foreach (BlackHoleDefinition hole in BlackHoles.Definitions)
        {
            if (hole.Pulsar || !hole.IsBlackHole || !hole.Enabled || hole.Body == null) continue;
            if (string.IsNullOrEmpty(name) || hole.BodyName == name || hole.Body.bodyName == name) return hole;
        }
        return null;
    }

    private static string Describe(TidalDisruption m) => string.Format(CultureInfo.InvariantCulture,
        "Tidal disruption model: hole {0:0.00}e6 Suns, tidal radius {1:0.0} r_s, periapsis {2:0.0} r_s (beta {3:0.0}), {4:0}% stripped; " +
        "disruption {5:0.0} h after the start, first return {6:0} d, fallback peak {7:0} d at {8:0} x Eddington, peak {9:0.0} L_Edd " +
        "(physical time; the game runs it {10:0.###}x).",
        m.HoleMassSuns / 1e6, m.TidalRadius, m.Periapsis, m.Beta, m.StrippedFraction * 100.0, m.ApproachSeconds / 3600.0,
        m.FirstReturnSeconds / 86400.0, m.TargetPeakSeconds / 86400.0, m.PeakFallbackEddington, m.PeakLuminosityWatts / m.EddingtonLuminosity,
        _parameters.TimeScale);
}
