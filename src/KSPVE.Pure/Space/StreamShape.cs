using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>
/// A companion's stream wound by hand (streamShape): the path it takes on from where it strikes, drawn from above in the
/// binary's plane, in units of that radius, x towards the companion and y the way it orbits; written "x y; x y; ...".
/// </summary>
public static class StreamShape
{
    /// <summary>Points a drawn winding is resampled to (BlackHole.shader's STREAM_DRAWN).</summary>
    public const int MaxPoints = 64;

    /// <summary>The points in <paramref name="text"/>, skipping any that are not two numbers.</summary>
    public static List<Double3> Parse(string text)
    {
        var points = new List<Double3>();
        if (string.IsNullOrWhiteSpace(text)) return points;
        foreach (string pair in text.Split(';'))
        {
            string[] parts = pair.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                && !double.IsNaN(x) && !double.IsInfinity(x) && !double.IsNaN(y) && !double.IsInfinity(y))
                points.Add(new Double3(x, y, 0.0));
        }
        return points;
    }

    /// <summary>The points as text, to three decimals.</summary>
    public static string Format(IReadOnlyList<Double3> points)
    {
        var text = new StringBuilder();
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0) text.Append("; ");
            text.Append(points[i].X.ToString("0.###", CultureInfo.InvariantCulture)).Append(' ')
                .Append(points[i].Y.ToString("0.###", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }

    /// <summary><paramref name="n"/> points evenly spaced along the drawn line (fewer than two: as they are).</summary>
    public static List<Double3> Resample(IReadOnlyList<Double3> points, int n)
    {
        var track = new List<Double3>(points);
        if (track.Count < 2 || n < 2) return track;
        return RocheStream.Resample(track, n);
    }

    /// <summary>The drawing smoothed: each point moved halfway to the middle of its neighbours, <paramref name="passes"/>
    /// times; the ends stay put.</summary>
    public static List<Double3> Smooth(IReadOnlyList<Double3> points, int passes)
    {
        var current = new List<Double3>(points);
        for (int pass = 0; pass < passes && current.Count > 2; pass++)
        {
            var next = new List<Double3>(current);
            for (int i = 1; i + 1 < current.Count; i++)
                next[i] = current[i] * 0.5 + (current[i - 1] + current[i + 1]) * 0.25;
            current = next;
        }
        return current;
    }

    /// <summary>A computed winding (<see cref="RocheStream.StreamSpiral"/>) as a drawing to start from: <paramref name="n"/>
    /// points in units of the radius where it strikes.</summary>
    public static List<Double3> FromSpiral(RocheStream.StreamSpiral spiral, int n)
    {
        var points = new List<Double3>(n);
        double scale = spiral.R0 > 0.0 ? 1.0 / spiral.R0 : 1.0;
        for (int k = 0; k < n; k++) points.Add(spiral.At((double)k / Math.Max(n - 1, 1)) * scale);
        return points;
    }
}
