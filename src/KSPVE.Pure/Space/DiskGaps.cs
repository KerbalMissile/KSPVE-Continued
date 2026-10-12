using System;
using System.Collections.Generic;
using System.Globalization;

namespace KSPVE.Pure.Space;

/// <summary>A gap in a dusty disk: its radius and width (length units) and how empty it is (0..1).</summary>
public readonly struct DiskGap
{
    public readonly double Radius, Width, Depth;

    public DiskGap(double radius, double width, double depth)
    {
        Radius = radius;
        Width = width;
        Depth = depth;
    }
}

/// <summary>
/// The gaps planets clear in a protoplanetary (or any dusty) disk: spread over the disk by count, a little closer together
/// inward (HL Tau's rings), or placed one by one (the gaps, gapWidths and gapDepths lists). BlackHole.shader cuts them.
/// </summary>
public static class DiskGaps
{
    /// <summary>The most gaps the shader cuts (BlackHole.shader's _KSPVE_ObjGaps).</summary>
    public const int Max = 16;

    /// <summary>
    /// Fills <paramref name="into"/> (at least <see cref="Max"/> long) and returns how many gaps there are. With
    /// <paramref name="radii"/> (fractions of the outer radius) they go exactly there, each with its own width and depth
    /// from <paramref name="widths"/> and <paramref name="depths"/> where given (else <paramref name="width"/>, a fraction of
    /// the outer radius, and <paramref name="depth"/>); otherwise <paramref name="rings"/> of them are spread over the disk.
    /// </summary>
    public static int Fill(double inner, double outer, double rings, double depth, double width,
        string radii, string widths, string depths, DiskGap[] into)
    {
        int n = 0;
        List<double> custom = ParseList(radii);
        if (custom.Count > 0)
        {
            List<double> w = ParseList(widths), d = ParseList(depths);
            for (int i = 0; i < custom.Count && n < Max; i++)
            {
                double wi = i < w.Count ? w[i] : width;
                double di = i < d.Count ? d[i] : depth;
                into[n++] = new DiskGap(custom[i] * outer, Math.Max(wi, 1e-6) * outer, Math.Min(Math.Max(di, 0.0), 1.0));
            }
            return n;
        }
        for (int k = 1; k <= Max && k <= rings; k++)
        {
            double fk = Math.Pow((k + 0.5 * (Hash(k * 7.31) - 0.5)) / (rings + 0.6), 1.25);
            double rk = inner * 2.0 + (outer - inner * 2.0) * fk;
            double wk = outer * Math.Max(width, 1e-6) * (0.6 + 0.8 * Hash(k * 3.17));
            into[n++] = new DiskGap(rk, wk, Math.Min(Math.Max(depth, 0.0), 1.0) * (0.65 + 0.35 * Hash(k * 5.9)));
        }
        return n;
    }

    /// <summary>Numbers separated by commas or spaces ("0.2, 0.35, 0.6"); anything not a finite number is skipped.</summary>
    public static List<double> ParseList(string text)
    {
        var list = new List<double>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (string part in text.Split(new[] { ',', ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v))
                list.Add(v);
        return list;
    }

    private static double Hash(double n)
    {
        double v = Math.Sin(n) * 43758.5453;
        return v - Math.Floor(v);
    }
}
