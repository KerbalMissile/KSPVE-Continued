using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Pipeline;

/// <summary>A quality knob the governor may lower or raise. Lower Priority is lowered first.</summary>
public sealed class GovernorKnob
{
    public GovernorKnob(string id, int level, int minLevel, int maxLevel, int priority)
    {
        Id = id ?? string.Empty;
        MinLevel = minLevel;
        MaxLevel = Math.Max(minLevel, maxLevel);
        Level = Math.Max(MinLevel, Math.Min(MaxLevel, level));
        Priority = priority;
    }

    public string Id { get; }
    public int Level { get; internal set; }
    public int MinLevel { get; }
    public int MaxLevel { get; }
    public int Priority { get; }
}

public enum GovernorAction
{
    Hold,
    Lowered,
    Raised,
    AtFloor,
    AtCeiling,
}

public readonly struct GovernorDecision
{
    public GovernorDecision(GovernorAction action, string knobId, int level, double smoothedMs)
    {
        Action = action;
        KnobId = knobId;
        Level = level;
        SmoothedMs = smoothedMs;
    }

    public GovernorAction Action { get; }
    public string KnobId { get; }
    /// <summary>New level of <see cref="KnobId"/>, or -1 when no knob changed.</summary>
    public int Level { get; }
    public double SmoothedMs { get; }
}

/// <summary>Adjusts quality knobs to keep smoothed GPU frame time near a target, with hysteresis and dwell.</summary>
public sealed class FrameBudgetGovernor
{
    private readonly List<GovernorKnob> _knobs;
    private readonly List<GovernorKnob> _lowered = new();
    private bool _hasSample;
    private int _framesSinceChange;

    public FrameBudgetGovernor(double targetMs, IEnumerable<GovernorKnob> knobs)
    {
        TargetMs = targetMs;
        _knobs = knobs == null ? new List<GovernorKnob>() : new List<GovernorKnob>(knobs);
        _knobs.RemoveAll(k => k == null);
    }

    public double TargetMs { get; set; }
    public double SmoothingAlpha { get; set; } = 0.1;
    public double HighMargin { get; set; } = 0.05;
    public double LowMargin { get; set; } = 0.15;
    public int MinDwellFrames { get; set; } = 60;
    public double SmoothedMs { get; private set; }
    public IReadOnlyList<GovernorKnob> Knobs => _knobs;

    public GovernorDecision Update(double gpuFrameMs)
    {
        if (double.IsNaN(gpuFrameMs) || double.IsInfinity(gpuFrameMs) || gpuFrameMs < 0)
            return new GovernorDecision(GovernorAction.Hold, null, -1, SmoothedMs);

        if (!_hasSample) { SmoothedMs = gpuFrameMs; _hasSample = true; }
        else
        {
            double a = Math.Max(0.0, Math.Min(1.0, SmoothingAlpha));
            SmoothedMs += a * (gpuFrameMs - SmoothedMs);
        }

        if (_framesSinceChange < int.MaxValue) _framesSinceChange++;
        if (_framesSinceChange < MinDwellFrames)
            return new GovernorDecision(GovernorAction.Hold, null, -1, SmoothedMs);

        if (SmoothedMs > TargetMs * (1.0 + HighMargin))
        {
            GovernorKnob best = null;
            foreach (var k in _knobs)
            {
                if (k.Level <= k.MinLevel) continue;
                if (best == null || k.Priority < best.Priority ||
                    (k.Priority == best.Priority && string.CompareOrdinal(k.Id, best.Id) < 0))
                    best = k;
            }
            if (best == null)
                return new GovernorDecision(GovernorAction.AtFloor, null, -1, SmoothedMs);
            best.Level--;
            _lowered.Add(best);
            _framesSinceChange = 0;
            return new GovernorDecision(GovernorAction.Lowered, best.Id, best.Level, SmoothedMs);
        }

        if (SmoothedMs < TargetMs * (1.0 - LowMargin))
        {
            while (_lowered.Count > 0)
            {
                var k = _lowered[_lowered.Count - 1];
                _lowered.RemoveAt(_lowered.Count - 1);
                if (k.Level >= k.MaxLevel) continue;
                k.Level++;
                _framesSinceChange = 0;
                return new GovernorDecision(GovernorAction.Raised, k.Id, k.Level, SmoothedMs);
            }
            return new GovernorDecision(GovernorAction.AtCeiling, null, -1, SmoothedMs);
        }

        return new GovernorDecision(GovernorAction.Hold, null, -1, SmoothedMs);
    }
}
