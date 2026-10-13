using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSPVE.Pure.Clouds;

/// <summary>
/// A curve through keys, as KSP's FloatCurve and Unity's AnimationCurve: "key = time value [inTangent outTangent]",
/// cubic Hermite between neighbouring keys, held flat beyond the first and last. Keys given without tangents get smooth
/// ones (the slope between their neighbours; one-sided at the ends).
/// </summary>
public sealed class FloatCurve
{
    public readonly struct Key
    {
        public readonly double Time, Value, InTangent, OutTangent;
        public Key(double time, double value, double inTangent, double outTangent) { Time = time; Value = value; InTangent = inTangent; OutTangent = outTangent; }
    }

    private readonly List<Key> _keys;

    public FloatCurve(IEnumerable<Key> keys)
    {
        _keys = new List<Key>(keys);
        _keys.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public IReadOnlyList<Key> Keys => _keys;

    /// <summary>A curve that is <paramref name="value"/> everywhere.</summary>
    public static FloatCurve Constant(double value) => new FloatCurve(new[] { new Key(0.0, value, 0.0, 0.0) });

    /// <summary>
    /// A curve from "key" lines (each "time value" or "time value in out"); null when none parse. Lines that do not parse
    /// are reported through <paramref name="bad"/>.
    /// </summary>
    public static FloatCurve FromKeys(IEnumerable<string> keyTexts, Action<string> bad = null)
    {
        var parsed = new List<double[]>();
        foreach (string text in keyTexts)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new double[parts.Length];
            bool ok = parts.Length == 2 || parts.Length == 4;
            for (int i = 0; ok && i < parts.Length; i++)
                ok = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]);
            if (ok) parsed.Add(numbers);
            else bad?.Invoke(text);
        }
        if (parsed.Count == 0) return null;
        parsed.Sort((a, b) => a[0].CompareTo(b[0]));
        var keys = new List<Key>(parsed.Count);
        for (int i = 0; i < parsed.Count; i++)
        {
            double[] k = parsed[i];
            if (k.Length == 4)
            {
                keys.Add(new Key(k[0], k[1], k[2], k[3]));
                continue;
            }
            double[] before = parsed[Math.Max(i - 1, 0)], after = parsed[Math.Min(i + 1, parsed.Count - 1)];
            double span = after[0] - before[0];
            double slope = span > 0.0 ? (after[1] - before[1]) / span : 0.0;
            keys.Add(new Key(k[0], k[1], slope, slope));
        }
        return new FloatCurve(keys);
    }

    /// <summary>The curve's value at <paramref name="time"/>.</summary>
    public double Evaluate(double time)
    {
        if (_keys.Count == 0) return 0.0;
        if (time <= _keys[0].Time) return _keys[0].Value;
        Key last = _keys[_keys.Count - 1];
        if (time >= last.Time) return last.Value;
        int i = 1;
        while (_keys[i].Time < time) i++;
        Key a = _keys[i - 1], b = _keys[i];
        double dt = b.Time - a.Time;
        if (dt <= 0.0) return b.Value;
        double s = (time - a.Time) / dt, s2 = s * s, s3 = s2 * s;
        return (2.0 * s3 - 3.0 * s2 + 1.0) * a.Value + (s3 - 2.0 * s2 + s) * dt * a.OutTangent
             + (-2.0 * s3 + 3.0 * s2) * b.Value + (s3 - s2) * dt * b.InTangent;
    }
}

/// <summary>
/// Which cloud types a cloud type map's value means: with n types, type i (from 0) is stored at i / (n - 1), and values
/// between blend the two types either side.
/// </summary>
public static class CloudTypeBlend
{
    /// <summary>The lower of the two types and how far towards the next one (0..1) <paramref name="mapValue"/> (0..1) lies.</summary>
    public static void Pick(double mapValue, int typeCount, out int lower, out double towardsNext)
    {
        if (typeCount <= 1)
        {
            lower = 0;
            towardsNext = 0.0;
            return;
        }
        double position = Math.Min(Math.Max(mapValue, 0.0), 1.0) * (typeCount - 1);
        lower = Math.Min((int)Math.Floor(position), typeCount - 2);
        towardsNext = position - lower;
    }
}
