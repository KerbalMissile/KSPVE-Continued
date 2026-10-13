using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>A square of terrain and the rain that runs off it.</summary>
public sealed class HydroInput
{
    public int Width, Height;
    /// <summary>Metres between cell centres.</summary>
    public double CellSize;
    /// <summary>Altitude above sea level of each cell (m), x east fastest, y north.</summary>
    public float[] Terrain;
    /// <summary>Cells at or below this altitude are the sea (NaN: no sea in the window).</summary>
    public float SeaLevel = float.NaN;
    /// <summary>The water each cell adds (m3/s): the rain that runs off its area.</summary>
    public float[] Runoff;
    /// <summary>More water entering at the window's edge from upstream (m3/s per cell), or null.</summary>
    public float[] Inflow;
    public double Gravity = 9.80665;
    /// <summary>Least discharge (m3/s) drawn as a river.</summary>
    public double MinDischarge = 0.5;
    /// <summary>A basin is a lake if the filling is at least this deep (m) and its surface at least this wide (m2).</summary>
    public double LakeMinDepth = 1.0, LakeMinArea = 20000.0;
    /// <summary>
    /// What a lake loses over what rain gives it (mm/year, evaporation minus precipitation). A basin whose catchment cannot
    /// make up the loss over its full area shrinks to the area it can keep wet. 0 fills every basin to its spill level.
    /// </summary>
    public double LakeNetEvaporation;
}

/// <summary>A point along a river (x east and y north, metres from the window's south-west corner).</summary>
public struct RiverPoint
{
    public double X, Y, Elevation;
    public ChannelState Channel;
    /// <summary>0 to 1: how much of a waterfall (a drop steeper than a quarter in a cell).</summary>
    public double Fall;
    /// <summary>0 to 1: white water, from the Froude number.</summary>
    public double Rapids;
}

/// <summary>A river from a source to where it joins another, a lake, the sea or the window's edge.</summary>
public sealed class RiverReach
{
    public readonly List<RiverPoint> Points = new List<RiverPoint>();
    /// <summary>The reach ends in this reach's cell (a confluence), in a lake or at the sea / edge.</summary>
    public bool EndsInLake, EndsAtSea;
}

/// <summary>A lake: a basin filled to its spill level.</summary>
public sealed class Lake
{
    public int Id;
    /// <summary>Altitude of its surface (m): the level it spills over.</summary>
    public float Level;
    public int CellCount;
    public double Area, MaxDepth;
    /// <summary>Its bounding box in cells.</summary>
    public int MinX, MinY, MaxX, MaxY;
}

/// <summary>Everything worked out for a window of terrain.</summary>
public sealed class HydroResult
{
    public int Width, Height;
    public double CellSize;
    public FloodResult Flood;
    /// <summary>The discharge passing each cell (m3/s).</summary>
    public float[] Discharge;
    /// <summary>The depth of water over each cell that is in a lake (m), 0 elsewhere.</summary>
    public float[] LakeDepth;
    /// <summary>The lake each cell belongs to (index into <see cref="Lakes"/>), or -1.</summary>
    public int[] LakeIndex;
    public readonly List<Lake> Lakes = new List<Lake>();
    public readonly List<RiverReach> Reaches = new List<RiverReach>();
}

/// <summary>
/// Works out where the water goes on a window of terrain: fills its basins (<see cref="PriorityFlood"/>), adds up the rain
/// that runs off down the flow (<see cref="FlowRouting"/>), keeps the basins that make lakes, and follows the channels whose
/// discharge reaches the threshold from their sources down, with the width, depth and speed of their water. Deterministic: the
/// same terrain and rain always give the same rivers.
/// </summary>
public static class HydroSolver
{
    public static HydroResult Solve(HydroInput input)
    {
        int w = input.Width, h = input.Height, count = w * h;
        FloodResult flood = PriorityFlood.Run(input.Terrain, w, h, input.SeaLevel);

        // Water entering over the edge needs a way in: the edge cell drains inwards, to its lowest lower neighbour.
        var source = new float[count];
        Array.Copy(input.Runoff, source, count);
        if (input.Inflow != null)
        {
            for (int i = 0; i < count; i++)
            {
                if (input.Inflow[i] <= 0f) continue;
                source[i] += input.Inflow[i];
                if (flood.Direction[i] >= 0) continue;
                int best = -1;
                float bestZ = flood.Filled[i];
                int x = i % w, y = i / w;
                for (int n = 0; n < 8; n++)
                {
                    int nx = x + Neighbours.Dx[n], ny = y + Neighbours.Dy[n];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (flood.Filled[ny * w + nx] < bestZ) { bestZ = flood.Filled[ny * w + nx]; best = n; }
                }
                if (best >= 0) flood.Direction[i] = (sbyte)best;
            }
        }
        float[] q = FlowRouting.AccumulateD8(flood, source);

        var result = new HydroResult { Width = w, Height = h, CellSize = input.CellSize, Flood = flood, Discharge = q };
        FindLakes(input, result);
        TraceRivers(input, result);
        return result;
    }

    private static void FindLakes(HydroInput input, HydroResult r)
    {
        int w = r.Width, h = r.Height, count = w * h;
        r.LakeDepth = new float[count];
        r.LakeIndex = new int[count];
        for (int i = 0; i < count; i++) r.LakeIndex[i] = -1;
        var candidate = new bool[count];
        for (int i = 0; i < count; i++) candidate[i] = r.Flood.Filled[i] - input.Terrain[i] > 0.05f && !float.IsNaN(input.Terrain[i]);

        var stack = new Stack<int>();
        var visited = new bool[count];
        for (int s = 0; s < count; s++)
        {
            if (!candidate[s] || visited[s]) continue;
            // One lake: the connected cells filled to the same level.
            float level = r.Flood.Filled[s];
            var cells = new List<int>();
            visited[s] = true;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                cells.Add(c);
                int cx = c % w, cy = c / w;
                for (int n = 0; n < 8; n += 2)
                {
                    int nx = cx + Neighbours.Dx[n], ny = cy + Neighbours.Dy[n];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (visited[ni] || !candidate[ni] || Math.Abs(r.Flood.Filled[ni] - level) > 0.01f) continue;
                    visited[ni] = true;
                    stack.Push(ni);
                }
            }
            level = ShrinkToWaterBalance(input, r, cells, level);
            if (cells.Count == 0) continue;
            double area = cells.Count * input.CellSize * input.CellSize, maxDepth = 0.0;
            foreach (int c in cells) maxDepth = Math.Max(maxDepth, level - input.Terrain[c]);
            if (maxDepth < input.LakeMinDepth || area < input.LakeMinArea) continue;
            var lake = new Lake { Id = r.Lakes.Count, Level = level, CellCount = cells.Count, Area = area, MaxDepth = maxDepth, MinX = w, MinY = h };
            foreach (int c in cells)
            {
                int cx = c % w, cy = c / w;
                r.LakeIndex[c] = lake.Id;
                r.LakeDepth[c] = level - input.Terrain[c];
                lake.MinX = Math.Min(lake.MinX, cx); lake.MaxX = Math.Max(lake.MaxX, cx);
                lake.MinY = Math.Min(lake.MinY, cy); lake.MaxY = Math.Max(lake.MaxY, cy);
            }
            r.Lakes.Add(lake);
        }
    }

    /// <summary>
    /// A lake keeps its full area only if the water reaching it (the discharge where it spills, less the rain on its own cells)
    /// covers what evaporates from that area. Otherwise it settles at the area the inflow can keep (area x net evaporation =
    /// inflow), the lowest cells connected to the deepest one, and <paramref name="cells"/> and the level are cut to it.
    /// Returns the level.
    /// </summary>
    private static float ShrinkToWaterBalance(HydroInput input, HydroResult r, List<int> cells, float level)
    {
        if (input.LakeNetEvaporation <= 0.0) return level;
        int w = r.Width, h = r.Height;
        double inflow = 0.0;
        foreach (int c in cells) inflow = Math.Max(inflow, r.Discharge[c] - input.Runoff[c]);
        double loss = input.LakeNetEvaporation * 1e-3 / (365.25 * 86400.0);   // m/s over the surface
        double keptArea = inflow / loss;
        double cellArea = input.CellSize * input.CellSize;
        if (keptArea >= cells.Count * cellArea) return level;
        int keep = (int)(keptArea / cellArea);
        if (keep < 1) { cells.Clear(); return level; }
        cells.Sort((a, b) => input.Terrain[a] != input.Terrain[b] ? input.Terrain[a].CompareTo(input.Terrain[b]) : a.CompareTo(b));
        float newLevel = input.Terrain[cells[Math.Min(keep, cells.Count) - 1]];
        var allowed = new HashSet<int>();
        foreach (int c in cells) if (input.Terrain[c] <= newLevel) allowed.Add(c);
        var kept = new List<int>();
        var seen = new HashSet<int> { cells[0] };
        var stack = new Stack<int>();
        stack.Push(cells[0]);
        while (stack.Count > 0)
        {
            int c = stack.Pop();
            kept.Add(c);
            for (int n = 0; n < 8; n += 2)
            {
                int nx = c % w + Neighbours.Dx[n], ny = c / w + Neighbours.Dy[n];
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int ni = ny * w + nx;
                if (allowed.Contains(ni) && seen.Add(ni)) stack.Push(ni);
            }
        }
        cells.Clear();
        cells.AddRange(kept);
        return newLevel;
    }

    private static void TraceRivers(HydroInput input, HydroResult r)
    {
        int w = r.Width, h = r.Height, count = w * h;
        float min = (float)input.MinDischarge;
        float[] q = r.Discharge;
        bool IsRiver(int i) => q[i] >= min && r.LakeIndex[i] < 0 && !(!float.IsNaN(input.SeaLevel) && input.Terrain[i] <= input.SeaLevel);

        // A source is a river cell nothing river-like drains into.
        var donors = new int[count];
        for (int i = 0; i < count; i++)
        {
            int d = r.Flood.Direction[i];
            if (d < 0 || !IsRiver(i)) continue;
            int n = (i / w + Neighbours.Dy[d]) * w + i % w + Neighbours.Dx[d];
            donors[n]++;
        }
        var traced = new bool[count];
        for (int s = 0; s < count; s++)
        {
            if (!IsRiver(s) || donors[s] > 0 || traced[s]) continue;
            var reach = new RiverReach();
            var path = new List<int>();
            int c = s;
            while (true)
            {
                path.Add(c);
                traced[c] = true;
                int d = r.Flood.Direction[c];
                if (d < 0) { reach.EndsAtSea = true; break; }
                int n = (c / w + Neighbours.Dy[d]) * w + c % w + Neighbours.Dx[d];
                if (r.LakeIndex[n] >= 0) { reach.EndsInLake = true; path.Add(n); break; }
                if (!IsRiver(n))
                {
                    // Reached the sea (or fell below the threshold, which a discharge that only grows downstream does not do).
                    reach.EndsAtSea = true;
                    path.Add(n);
                    break;
                }
                if (traced[n]) { path.Add(n); break; }   // joins a river already followed
                c = n;
            }
            BuildPoints(input, r, path, reach);
            if (reach.Points.Count >= 2) r.Reaches.Add(reach);
        }
    }

    /// <summary>
    /// Turns a chain of cells into a smooth river: Chaikin's corner cutting twice (the cells' centres are the control
    /// polygon), then each point's width, depth and speed from its discharge and slope. The bed never rises downstream.
    /// </summary>
    private static void BuildPoints(HydroInput input, HydroResult r, List<int> path, RiverReach reach)
    {
        int w = r.Width;
        double cell = input.CellSize;
        var pts = new List<RiverPoint>();
        for (int k = 0; k < path.Count; k++)
        {
            int c = path[k];
            pts.Add(new RiverPoint { X = (c % w + 0.5) * cell, Y = (c / w + 0.5) * cell, Elevation = r.Flood.Filled[c], Channel = new ChannelState { Discharge = r.Discharge[c] } });
        }
        for (int pass = 0; pass < 2 && pts.Count > 2; pass++)
        {
            var cut = new List<RiverPoint> { pts[0] };
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                cut.Add(Lerp(pts[k], pts[k + 1], 0.25));
                cut.Add(Lerp(pts[k], pts[k + 1], 0.75));
            }
            cut.Add(pts[pts.Count - 1]);
            pts = cut;
        }
        // Monotone bed, and the slope over about a cell either side.
        for (int k = 1; k < pts.Count; k++)
        {
            RiverPoint p = pts[k];
            p.Elevation = Math.Min(p.Elevation, pts[k - 1].Elevation);
            pts[k] = p;
        }
        for (int k = 0; k < pts.Count; k++)
        {
            int a = k, b = k;
            double run = 0.0;
            // Widen the window to at least one cell's length of river.
            while (run < cell && (a > 0 || b < pts.Count - 1))
            {
                if (a > 0) a--;
                if (b < pts.Count - 1) b++;
                double dx = pts[b].X - pts[a].X, dy = pts[b].Y - pts[a].Y;
                run = Math.Sqrt(dx * dx + dy * dy);
            }
            double slope = run > 1e-9 ? Math.Max((pts[a].Elevation - pts[b].Elevation) / run, 0.0) : 0.0;
            RiverPoint p = pts[k];
            p.Channel = HydraulicGeometry.At(p.Channel.Discharge, slope, input.Gravity);
            p.Fall = Clamp01((slope - 0.18) / 0.12);
            p.Rapids = Math.Max(p.Fall, Clamp01((p.Channel.Froude - 0.6) / 0.8));
            pts[k] = p;
        }
        reach.Points.AddRange(pts);
    }

    private static RiverPoint Lerp(RiverPoint a, RiverPoint b, double t)
    {
        double Mix(double x, double y) => x + (y - x) * t;
        return new RiverPoint
        {
            X = Mix(a.X, b.X), Y = Mix(a.Y, b.Y), Elevation = Mix(a.Elevation, b.Elevation),
            Channel = new ChannelState { Discharge = Mix(a.Channel.Discharge, b.Channel.Discharge) }
        };
    }

    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    /// <summary>
    /// Water entering a window across its edge from a coarser map of the region around it: where the coarse discharge
    /// <paramref name="coarse"/>(x, y) (the largest in the coarse cell there; x and y in the window's metres) is much more than the
    /// window's own on an edge cell (the highest of its neighbours along the edge), the difference is added there so rivers
    /// that start far outside the window arrive in it. Returns null when nothing enters.
    /// </summary>
    public static float[] RegionalInflow(HydroInput input, float[] ownDischarge, Func<double, double, double> coarse)
    {
        int w = input.Width, h = input.Height;
        float[] inflow = null;
        void Consider(int x, int y, int ax, int ay)
        {
            int i = y * w + x;
            double own = ownDischarge[i], c = coarse((x + 0.5) * input.CellSize, (y + 0.5) * input.CellSize);
            if (c < input.MinDischarge || c <= 1.5 * own) return;
            // The channel enters where the ground is lowest along the edge (a valley floor): the lowest of the cells nearby that
            // the coarse map also puts a river in, the first of them on a tie.
            for (int s = -3; s <= 3; s++)
            {
                int nx = x + ax * s, ny = y + ay * s;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || s == 0) continue;
                int ni = ny * w + nx;
                double cn = coarse((nx + 0.5) * input.CellSize, (ny + 0.5) * input.CellSize);
                if (cn < input.MinDischarge) continue;
                if (input.Terrain[ni] < input.Terrain[i] || (input.Terrain[ni] == input.Terrain[i] && s < 0)) return;
            }
            if (inflow == null) inflow = new float[w * h];
            inflow[i] = (float)(c - own);
        }
        for (int x = 0; x < w; x++) { Consider(x, 0, 1, 0); Consider(x, h - 1, 1, 0); }
        for (int y = 1; y < h - 1; y++) { Consider(0, y, 0, 1); Consider(w - 1, y, 0, 1); }
        return inflow;
    }
}
