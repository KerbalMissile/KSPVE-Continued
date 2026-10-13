using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>What the water is like at a point.</summary>
public struct WaterAt
{
    public bool InWater;
    public bool IsLake;
    /// <summary>Altitude of the water's surface (m).</summary>
    public double Surface;
    /// <summary>Depth of the water there (m).</summary>
    public double Depth;
    /// <summary>The way the water flows (unit east, north; zero in a lake) and its speed (m/s).</summary>
    public double FlowEast, FlowNorth, Speed;
}

/// <summary>
/// A solved window's water looked up by position: which lake (and its level) or river (and its width, depth and flow) lies at a
/// map point. Used to float things on it and to tell whether a place is wet. Rivers are kept as segments listed by the cells their
/// banks reach, so a query measures its distance to the true centre line, not to a cell.
/// </summary>
public sealed class RiverField
{
    private readonly HydroResult _result;
    private readonly float[] _terrain;
    private readonly RiverPoint[] _points;
    private readonly List<int>[] _cells;   // per cell, the first point of each segment whose banks reach it

    public RiverField(HydroResult result, float[] terrain)
    {
        _result = result;
        _terrain = terrain;
        var points = new List<RiverPoint>();
        _cells = new List<int>[result.Width * result.Height];
        foreach (RiverReach reach in result.Reaches)
        {
            int first = points.Count;
            points.AddRange(reach.Points);
            for (int k = first; k + 1 < points.Count; k++)
            {
                RiverPoint a = points[k], b = points[k + 1];
                double reachRadius = 0.5 * Math.Max(Math.Max(a.Channel.Width, b.Channel.Width), 1.0) + 0.75 * result.CellSize;
                int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - reachRadius) / result.CellSize), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + reachRadius) / result.CellSize);
                int y0 = (int)Math.Floor((Math.Min(a.Y, b.Y) - reachRadius) / result.CellSize), y1 = (int)Math.Floor((Math.Max(a.Y, b.Y) + reachRadius) / result.CellSize);
                for (int y = Math.Max(y0, 0); y <= Math.Min(y1, result.Height - 1); y++)
                {
                    for (int x = Math.Max(x0, 0); x <= Math.Min(x1, result.Width - 1); x++)
                    {
                        List<int> list = _cells[y * result.Width + x];
                        if (list == null) _cells[y * result.Width + x] = list = new List<int>(2);
                        list.Add(k);
                    }
                }
            }
        }
        _points = points.ToArray();
    }

    /// <summary>The water at map position (east, north) in metres from the window's south-west corner, for a point at the given altitude.</summary>
    public WaterAt At(double east, double north, double altitude)
    {
        var r = new WaterAt();
        int cx = (int)Math.Floor(east / _result.CellSize), cy = (int)Math.Floor(north / _result.CellSize);
        if (cx < 0 || cy < 0 || cx >= _result.Width || cy >= _result.Height) return r;
        int cell = cy * _result.Width + cx;
        int lake = _result.LakeIndex[cell];
        if (lake >= 0)
        {
            Lake l = _result.Lakes[lake];
            if (altitude <= l.Level)
            {
                r.InWater = true; r.IsLake = true; r.Surface = l.Level; r.Depth = l.Level - _terrain[cell];
                return r;
            }
        }
        List<int> segments = _cells[cell];
        if (segments == null) return r;
        double bestEdge = double.MaxValue;
        foreach (int k in segments)
        {
            RiverPoint a = _points[k], b = _points[k + 1];
            double sx = b.X - a.X, sy = b.Y - a.Y, len2 = sx * sx + sy * sy;
            double t = len2 > 1e-12 ? Math.Min(Math.Max(((east - a.X) * sx + (north - a.Y) * sy) / len2, 0.0), 1.0) : 0.0;
            double px = a.X + sx * t, py = a.Y + sy * t;
            double lateral = Math.Sqrt((east - px) * (east - px) + (north - py) * (north - py));
            double width = a.Channel.Width + (b.Channel.Width - a.Channel.Width) * t;
            double half = 0.5 * Math.Max(width, 1.0);
            if (lateral > half || lateral / half >= bestEdge) continue;
            bestEdge = lateral / half;
            double across = lateral / half;
            r.InWater = true;
            r.Surface = a.Elevation + (b.Elevation - a.Elevation) * t;
            r.Depth = (a.Channel.Depth + (b.Channel.Depth - a.Channel.Depth) * t) * (1.0 - across * across);
            r.Speed = a.Channel.Speed + (b.Channel.Speed - a.Channel.Speed) * t;
            double len = Math.Sqrt(len2);
            r.FlowEast = len > 1e-9 ? sx / len : 0.0;
            r.FlowNorth = len > 1e-9 ? sy / len : 0.0;
        }
        return r;
    }
}
