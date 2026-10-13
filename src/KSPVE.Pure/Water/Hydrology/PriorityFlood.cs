using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>The eight neighbours of a grid cell (x east, y north), in a fixed order shared by all the flow code.</summary>
public static class Neighbours
{
    public static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
    public static readonly int[] Dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

    /// <summary>1 along an axis, sqrt 2 along a diagonal (in cells).</summary>
    public static readonly double[] Distance = { 1.0, Math.Sqrt(2.0), 1.0, Math.Sqrt(2.0), 1.0, Math.Sqrt(2.0), 1.0, Math.Sqrt(2.0) };
}

/// <summary>What <see cref="PriorityFlood"/> found.</summary>
public sealed class FloodResult
{
    public int Width, Height;
    /// <summary>The terrain with every depression filled to the level it spills at: lakes are flat.</summary>
    public float[] Filled;
    /// <summary>The way each cell drains (an index into <see cref="Neighbours"/>), or -1 for an outlet (the edge, or the sea).</summary>
    public sbyte[] Direction;
    /// <summary>The cells in the order the flood reached them: every cell comes after the one it drains into.</summary>
    public int[] Order;
}

/// <summary>
/// Fills the depressions of a terrain grid to their spill level with the Priority-Flood algorithm (Barnes, Lehman and Mulla
/// 2014, "Priority-flood: an optimal depression-filling and watershed-labeling algorithm for digital elevation models"): the
/// flood starts at the outlets (the grid's edge and everything at or below the sea) and always advances from the lowest cell
/// it has reached, so a basin is only entered over its lowest rim and is filled to that rim's height. Each cell drains into
/// the cell it was reached from, which routes the flow across lakes and flats to their outlets.
/// </summary>
public static class PriorityFlood
{
    private struct Entry
    {
        public float Elevation;
        public int Serial;
        public int Cell;
    }

    /// <summary>
    /// A binary min-heap on elevation, ties broken by arrival: the flood is the same every time and spreads across a flat
    /// outwards from where it entered (so the flow crosses a lake by the shortest way).
    /// </summary>
    private sealed class Heap
    {
        private Entry[] _items = new Entry[1024];
        public int Count;

        public void Push(Entry e)
        {
            if (Count == _items.Length) Array.Resize(ref _items, Count * 2);
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (Less(_items[parent], e)) break;
                _items[i] = _items[parent];
                i = parent;
            }
            _items[i] = e;
        }

        public Entry Pop()
        {
            Entry top = _items[0];
            Entry last = _items[--Count];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= Count) break;
                if (child + 1 < Count && Less(_items[child + 1], _items[child])) child++;
                if (Less(last, _items[child])) break;
                _items[i] = _items[child];
                i = child;
            }
            if (Count > 0) _items[i] = last;
            return top;
        }

        private static bool Less(Entry a, Entry b) => a.Elevation < b.Elevation || (a.Elevation == b.Elevation && a.Serial < b.Serial);
    }

    /// <summary>
    /// Floods <paramref name="dem"/> (heights in metres, x fastest). Cells on the grid's edge and cells at or below
    /// <paramref name="seaLevel"/> are outlets (pass NaN for no sea).
    /// </summary>
    public static FloodResult Run(float[] dem, int width, int height, float seaLevel = float.NaN)
    {
        int count = width * height;
        var filled = new float[count];
        var direction = new sbyte[count];
        var order = new int[count];
        var closed = new bool[count];
        var heap = new Heap();
        int serial = 0, produced = 0;

        for (int i = 0; i < count; i++) direction[i] = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                bool sea = !float.IsNaN(seaLevel) && dem[i] <= seaLevel;
                if (!edge && !sea) continue;
                filled[i] = dem[i];
                closed[i] = true;
                heap.Push(new Entry { Elevation = dem[i], Serial = serial++, Cell = i });
            }
        }

        while (heap.Count > 0)
        {
            Entry e = heap.Pop();
            order[produced++] = e.Cell;
            int cx = e.Cell % width, cy = e.Cell / width;
            for (int n = 0; n < 8; n++)
            {
                int nx = cx + Neighbours.Dx[n], ny = cy + Neighbours.Dy[n];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int ni = ny * width + nx;
                if (closed[ni]) continue;
                closed[ni] = true;
                filled[ni] = Math.Max(dem[ni], filled[e.Cell]);
                direction[ni] = (sbyte)((n + 4) & 7);   // towards the cell it was reached from
                heap.Push(new Entry { Elevation = filled[ni], Serial = serial++, Cell = ni });
            }
        }

        // NaN heights (no data) never close: they become outlets of their own so nothing is left unvisited.
        if (produced < count)
        {
            var rest = new List<int>();
            for (int i = 0; i < count; i++) if (!closed[i]) rest.Add(i);
            foreach (int i in rest) { filled[i] = dem[i]; order[produced++] = i; }
        }
        Steepen(filled, direction, order, width, height);
        return new FloodResult { Width = width, Height = height, Filled = filled, Direction = direction, Order = order };
    }

    /// <summary>
    /// The flood drains a cell into whichever neighbour reached it first, which on a slope is any of several equally low
    /// cells. Water runs down the steepest way: among the neighbours the flood had reached earlier and that are lower (the
    /// order keeps the flow free of loops), take the steepest, cardinal first on a tie; across a flat the flood's own route stays.
    /// </summary>
    private static void Steepen(float[] filled, sbyte[] direction, int[] order, int width, int height)
    {
        var position = new int[order.Length];
        for (int k = 0; k < order.Length; k++) position[order[k]] = k;
        for (int i = 0; i < filled.Length; i++)
        {
            if (direction[i] < 0) continue;
            int x = i % width, y = i / width;
            int best = direction[i];
            double bestSlope = -1.0;
            {
                int bx = x + Neighbours.Dx[best], by = y + Neighbours.Dy[best];
                bestSlope = (filled[i] - filled[by * width + bx]) / Neighbours.Distance[best];
            }
            for (int n = 0; n < 8; n++)
            {
                int nx = x + Neighbours.Dx[n], ny = y + Neighbours.Dy[n];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int ni = ny * width + nx;
                if (position[ni] >= position[i] || filled[ni] >= filled[i]) continue;
                double slope = (filled[i] - filled[ni]) / Neighbours.Distance[n];
                if (slope > bestSlope + 1e-9) { bestSlope = slope; best = n; }
            }
            direction[i] = (sbyte)best;
        }
    }
}
