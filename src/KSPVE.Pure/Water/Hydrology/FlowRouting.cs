using System;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>Moves water down a flooded terrain and adds it up: the discharge in every cell.</summary>
public static class FlowRouting
{
    /// <summary>
    /// D8 flow accumulation: each cell passes everything it holds to the one neighbour it drains into. <paramref name="source"/>
    /// is the water each cell adds itself (m3/s, or 1 for counting cells); the result is the water passing each cell.
    /// </summary>
    public static float[] AccumulateD8(FloodResult flood, float[] source)
    {
        int w = flood.Width;
        var q = new float[source.Length];
        Array.Copy(source, q, source.Length);
        // The flood reached every cell after the one it drains into, so the reverse order is downstream order.
        for (int k = flood.Order.Length - 1; k >= 0; k--)
        {
            int i = flood.Order[k];
            int d = flood.Direction[i];
            if (d < 0) continue;
            int x = i % w + Neighbours.Dx[d], y = i / w + Neighbours.Dy[d];
            q[y * w + x] += q[i];
        }
        return q;
    }

    /// <summary>
    /// Tarboton's D-infinity: the flow direction is the steepest downhill direction over the eight triangular facets round a
    /// cell (not a multiple of 45 degrees), and a cell shares its water between the two neighbours that direction lies
    /// between, in proportion to its angle from each (Tarboton 1997, "A new method for the determination of flow directions and
    /// upslope areas in grid digital elevation models"). Where nothing lies lower (a lake or flat) the D8 route of the flood
    /// carries it. The result spreads over hillslopes instead of running along the grid's axes.
    /// </summary>
    public static float[] AccumulateDInfinity(FloodResult flood, float[] source, double cellSize)
    {
        int w = flood.Width, h = flood.Height;
        float[] z = flood.Filled;
        var q = new float[source.Length];
        Array.Copy(source, q, source.Length);
        for (int k = flood.Order.Length - 1; k >= 0; k--)
        {
            int i = flood.Order[k];
            int x = i % w, y = i / w;
            if (flood.Direction[i] < 0 && (x == 0 || y == 0 || x == w - 1 || y == h - 1)) continue;   // leaves the window
            if (!Facets(z, w, h, x, y, cellSize, out int a, out int b, out double share))
            {
                int d = flood.Direction[i];
                if (d < 0) continue;
                q[(y + Neighbours.Dy[d]) * w + x + Neighbours.Dx[d]] += q[i];
                continue;
            }
            float v = q[i];
            int ib = (y + Neighbours.Dy[b]) * w + x + Neighbours.Dx[b], ia = (y + Neighbours.Dy[a]) * w + x + Neighbours.Dx[a];
            // Water only goes to a cell that is lower, so the downstream order holds.
            if (z[ib] >= z[i]) share = 0.0;
            if (z[ia] >= z[i]) { int d = flood.Direction[i]; if (d < 0) continue; q[(y + Neighbours.Dy[d]) * w + x + Neighbours.Dx[d]] += v; continue; }
            q[ia] += (float)(v * (1.0 - share));
            if (share > 0.0) q[ib] += (float)(v * share);
        }
        return q;
    }

    /// <summary>
    /// The steepest facet of a cell: the neighbour <paramref name="first"/> (a cardinal or diagonal), the next one round
    /// <paramref name="second"/> and the share of the flow that goes to the second. False when no neighbour is lower.
    /// </summary>
    public static bool Facets(float[] z, int w, int h, int x, int y, double cell, out int first, out int second, out double share)
    {
        first = second = -1; share = 0.0;
        double e0 = z[y * w + x], best = 0.0, bestAngle = 0.0;
        for (int f = 0; f < 8; f++)
        {
            int n1 = f, n2 = (f + 1) & 7;
            int x1 = x + Neighbours.Dx[n1], y1 = y + Neighbours.Dy[n1], x2 = x + Neighbours.Dx[n2], y2 = y + Neighbours.Dy[n2];
            if (x1 < 0 || y1 < 0 || x1 >= w || y1 >= h || x2 < 0 || y2 < 0 || x2 >= w || y2 >= h) continue;
            // One of the two is a cardinal neighbour (distance d1), the other diagonal.
            bool firstCardinal = (n1 & 1) == 0;
            double d1 = firstCardinal ? cell : cell * Math.Sqrt(2.0);
            double e1 = z[y1 * w + x1], e2 = z[y2 * w + x2];
            double s1, s2;
            if (firstCardinal) { s1 = (e0 - e1) / cell; s2 = (e1 - e2) / cell; }
            else { s1 = (e0 - e2) / cell; s2 = (e2 - e1) / cell; }   // measured from the cardinal one
            double r = Math.Atan2(s2, s1), s = Math.Sqrt(s1 * s1 + s2 * s2);
            if (r < 0.0) { r = 0.0; s = s1; }
            else if (r > Math.PI / 4.0)
            {
                r = Math.PI / 4.0;
                s = ((firstCardinal ? e0 - e2 : e0 - e1)) / (cell * Math.Sqrt(2.0));
            }
            _ = d1;
            if (s > best)
            {
                best = s; bestAngle = r;
                if (firstCardinal) { first = n1; second = n2; share = r / (Math.PI / 4.0); }
                else { first = n2; second = n1; share = r / (Math.PI / 4.0); }
            }
        }
        _ = bestAngle;
        return best > 0.0 && first >= 0;
    }

    /// <summary>The steepest-descent slope (drop over distance) from a cell to its D8 neighbour; zero in a lake or at an outlet.</summary>
    public static double Slope(FloodResult flood, float[] terrain, int cell, double cellSize)
    {
        int d = flood.Direction[cell];
        if (d < 0) return 0.0;
        int w = flood.Width;
        int n = (cell / w + Neighbours.Dy[d]) * w + cell % w + Neighbours.Dx[d];
        return Math.Max((terrain[cell] - terrain[n]) / (cellSize * Neighbours.Distance[d]), 0.0);
    }
}
