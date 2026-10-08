using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Pipeline;

public sealed class QualityLevel
{
    public QualityLevel(string name, double estimatedCostMs)
    {
        Name = name;
        EstimatedCostMs = estimatedCostMs;
    }

    public string Name { get; }
    public double EstimatedCostMs { get; }
}

public enum QualityPreset
{
    Low = 0,
    Medium = 1,
    High = 2,
    Ultra = 3,
}

/// <summary>Ordered quality levels (ascending cost) with preset mapping.</summary>
public sealed class QualityLadder
{
    private readonly QualityLevel[] _levels;

    public QualityLadder(IReadOnlyList<QualityLevel> levels)
    {
        if (levels == null) throw new ArgumentNullException(nameof(levels));
        if (levels.Count < 1) throw new ArgumentException("At least one quality level is required.", nameof(levels));
        _levels = new QualityLevel[levels.Count];
        double prev = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            var l = levels[i];
            if (l == null) throw new ArgumentException("Null quality level at index " + i + ".", nameof(levels));
            if (double.IsNaN(l.EstimatedCostMs) || double.IsInfinity(l.EstimatedCostMs) || l.EstimatedCostMs < 0)
                throw new ArgumentException("Invalid cost for level '" + l.Name + "'.", nameof(levels));
            if (i > 0 && l.EstimatedCostMs < prev)
                throw new ArgumentException("Levels must be in ascending cost order.", nameof(levels));
            prev = l.EstimatedCostMs;
            _levels[i] = l;
        }
    }

    public int Count => _levels.Length;

    public QualityLevel this[int index] => _levels[index];

    public int LevelFor(QualityPreset preset)
    {
        int p = Math.Max(0, Math.Min(3, (int)preset));
        return Clamp((int)Math.Round(p * (_levels.Length - 1) / 3.0, MidpointRounding.AwayFromZero));
    }

    public int Clamp(int level) => level < 0 ? 0 : level >= _levels.Length ? _levels.Length - 1 : level;
}
