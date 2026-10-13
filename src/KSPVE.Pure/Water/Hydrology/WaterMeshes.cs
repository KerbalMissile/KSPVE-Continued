using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>
/// Vertices of a water surface in a window's own coordinates (metres east and north of its south-west corner, altitude above
/// sea level), with what the shader needs of each.
/// </summary>
public sealed class WaterMeshData
{
    public readonly List<float> East = new List<float>(), North = new List<float>(), Altitude = new List<float>();
    /// <summary>u across the water (0 and 1 at the banks), v along it (m of river, or the north-east position over a lake).</summary>
    public readonly List<float> U = new List<float>(), V = new List<float>();
    /// <summary>Depth below this vertex (m), flow speed (m/s), white-water amount and waterfall amount (0 to 1), and the flow direction (unit east, north; zero for a lake).</summary>
    public readonly List<float> Depth = new List<float>(), Speed = new List<float>(), Rapids = new List<float>(), Fall = new List<float>();
    /// <summary>The width (m) of the river at this vertex, 0 on a lake.</summary>
    public readonly List<float> Width = new List<float>();
    public readonly List<float> FlowEast = new List<float>(), FlowNorth = new List<float>();
    public readonly List<int> Indices = new List<int>();
    public int VertexCount => East.Count;

    public int Add(double east, double north, double altitude, double u, double v, double depth, double speed, double rapids, double fall, double flowEast, double flowNorth, double width = 0.0)
    {
        East.Add((float)east); North.Add((float)north); Altitude.Add((float)altitude);
        U.Add((float)u); V.Add((float)v);
        Depth.Add((float)depth); Speed.Add((float)speed); Rapids.Add((float)rapids); Fall.Add((float)fall);
        FlowEast.Add((float)flowEast); FlowNorth.Add((float)flowNorth); Width.Add((float)width);
        return East.Count - 1;
    }
}

/// <summary>Builds the surfaces of a solved window: ribbons for its rivers and flat sheets for its lakes.</summary>
public static class WaterMeshes
{
    /// <summary>
    /// A ribbon along every reach. Three vertices across (the banks and the middle, deepest there), as wide as the river,
    /// <paramref name="lift"/> metres above the bed so it is not buried in the ground's roughness, and at least
    /// <paramref name="minWidth"/> wide to be seen from afar.
    /// </summary>
    public static WaterMeshData Rivers(HydroResult r, double lift, double minWidth = 1.0)
    {
        var m = new WaterMeshData();
        foreach (RiverReach reach in r.Reaches)
        {
            List<RiverPoint> p = reach.Points;
            double along = 0.0;
            int previous = -1;
            for (int k = 0; k < p.Count; k++)
            {
                RiverPoint a = p[Math.Max(k - 1, 0)], b = p[Math.Min(k + 1, p.Count - 1)];
                double tx = b.X - a.X, ty = b.Y - a.Y, len = Math.Sqrt(tx * tx + ty * ty);
                if (len < 1e-9) continue;
                tx /= len; ty /= len;
                double nx = -ty, ny = tx;   // to the left of the flow
                if (k > 0)
                {
                    double dx = p[k].X - p[k - 1].X, dy = p[k].Y - p[k - 1].Y;
                    along += Math.Sqrt(dx * dx + dy * dy);
                }
                RiverPoint c = p[k];
                double half = 0.5 * Math.Max(c.Channel.Width, minWidth);
                double z = c.Elevation + lift;
                int left = m.Add(c.X + nx * half, c.Y + ny * half, z, 0.0, along, 0.0, c.Channel.Speed, c.Rapids, c.Fall, tx, ty, half * 2.0);
                int mid = m.Add(c.X, c.Y, z, 0.5, along, c.Channel.Depth, c.Channel.Speed, c.Rapids, c.Fall, tx, ty, half * 2.0);
                int right = m.Add(c.X - nx * half, c.Y - ny * half, z, 1.0, along, 0.0, c.Channel.Speed, c.Rapids, c.Fall, tx, ty, half * 2.0);
                if (previous >= 0)
                {
                    int pl = previous, pm = previous + 1, pr = previous + 2;
                    Quad(m, pl, pm, left, mid);
                    Quad(m, pm, pr, mid, right);
                }
                previous = left;
            }
        }
        return m;
    }

    private static void Quad(WaterMeshData m, int a0, int a1, int b0, int b1)
    {
        m.Indices.Add(a0); m.Indices.Add(b0); m.Indices.Add(a1);
        m.Indices.Add(a1); m.Indices.Add(b0); m.Indices.Add(b1);
    }

    /// <summary>
    /// A flat sheet at each lake's spill level over its cells, each cell grown by half a cell so the sheet reaches the shore
    /// and is cut off by the ground itself where that rises above the water. Depth is the filling at the vertex.
    /// </summary>
    public static WaterMeshData Lakes(HydroResult r)
    {
        var m = new WaterMeshData();
        int w = r.Width;
        double cell = r.CellSize;
        var vertexOf = new Dictionary<long, int>();
        foreach (Lake lake in r.Lakes)
        {
            for (int y = lake.MinY; y <= lake.MaxY; y++)
            {
                for (int x = lake.MinX; x <= lake.MaxX; x++)
                {
                    int c = y * w + x;
                    if (r.LakeIndex[c] != lake.Id) continue;
                    int v00 = Corner(m, vertexOf, r, lake, x, y, cell), v10 = Corner(m, vertexOf, r, lake, x + 1, y, cell);
                    int v01 = Corner(m, vertexOf, r, lake, x, y + 1, cell), v11 = Corner(m, vertexOf, r, lake, x + 1, y + 1, cell);
                    m.Indices.Add(v00); m.Indices.Add(v01); m.Indices.Add(v10);
                    m.Indices.Add(v10); m.Indices.Add(v01); m.Indices.Add(v11);
                }
            }
        }
        return m;
    }

    private static int Corner(WaterMeshData m, Dictionary<long, int> vertexOf, HydroResult r, Lake lake, int cx, int cy, double cell)
    {
        long key = ((long)lake.Id << 40) | ((long)cy << 20) | (long)cx;
        if (vertexOf.TryGetValue(key, out int existing)) return existing;
        // The depth at a corner: the deepest of the four cells meeting there that belong to the lake.
        double depth = 0.0;
        for (int dy = -1; dy <= 0; dy++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= r.Width || y >= r.Height) continue;
                int c = y * r.Width + x;
                if (r.LakeIndex[c] == lake.Id) depth = Math.Max(depth, r.LakeDepth[c]);
            }
        }
        int v = m.Add(cx * cell, cy * cell, lake.Level, cx * cell, cy * cell, depth, 0.0, 0.0, 0.0, 0.0, 0.0);
        vertexOf[key] = v;
        return v;
    }
}
