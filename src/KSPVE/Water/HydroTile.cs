using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Water;
using KSPVE.Pure.Water.Hydrology;
using UnityEngine;
using D3 = KSPVE.Pure.Numerics.Double3;

namespace KSPVE.Water;

/// <summary>A water mesh as plain arrays (built on a worker thread, turned into a Unity mesh on the main one).</summary>
internal sealed class WaterMeshArrays
{
    public Vector3[] Positions;
    public Vector2[] Uv0;
    public Vector4[] Uv1;
    public Vector3[] Uv2;
    public int[] Indices;
    public float MinY = float.MaxValue, MaxY = float.MinValue;
    public bool Empty => Indices == null || Indices.Length == 0;
}

/// <summary>
/// The water of one window of a body's terrain, built in stages that never hold a frame up: the terrain's heights are read from
/// the planet a few at a time (or from the disk cache), the rain that runs off is worked out from the climate, then the rivers and
/// lakes (<see cref="HydroSolver"/>) and their meshes on a worker thread. The same window gives the same water every time: it is
/// keyed by where it is and how it is set.
/// </summary>
internal sealed class HydroTile : ITerrainJob
{
    public enum Stage { Sampling, Solving, Building, Ready, Failed }

    public readonly string Key;
    public readonly CelestialBody Body;
    public readonly WindowFrame Frame;
    public readonly int Grid;
    public readonly double WindowMeters, CellSize;
    /// <summary>The window's centre latitude (degrees); the climate is read there.</summary>
    public readonly double Latitude;
    public readonly bool Regional;
    public Stage State { get; private set; } = Stage.Sampling;
    public float[] Terrain;
    public HydroResult Result;
    public RiverField Field;
    public WaterMeshArrays RiverMesh, LakeMesh;
    public bool MeshUploaded;
    public float LastUsed;
    private int _cursor;
    private Task _task;
    private float[] _runoff;
    private bool _cacheLoaded;
    private double _netEvaporation;
    private Func<double, double, double> _coarse;
    private HydroResult _coarseUsedFor;

    public HydroTile(CelestialBody body, string key, WindowFrame frame, double latitude, int grid, double windowMeters, bool regional)
    {
        Body = body; Key = key; Frame = frame; Latitude = latitude; Grid = grid; WindowMeters = windowMeters; CellSize = windowMeters / grid; Regional = regional;
        Terrain = new float[grid * grid];
        _cacheLoaded = TryLoadCache();
        if (_cacheLoaded) { _cursor = Terrain.Length; BeginSolve(); }
    }

    private string CachePath => Path.Combine(Path.Combine(Paths.PluginDataDir, "WaterCache"), Sanitise(Body.bodyName) + "_" + Key + ".f32");

    private static string Sanitise(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>The altitude (m) at map position (east, north) from the window's centre.</summary>
    private double Sample(double east, double north)
    {
        D3 d = Frame.ToDirection(east, north);
        return TerrainSampler.Altitude(Body, new Vector3d(d.X, d.Y, d.Z));
    }

    // ------------------------------------------------------------ sampling the terrain

    public bool Step(Stopwatch watch, double limitMs)
    {
        if (State != Stage.Sampling) return true;
        double half = 0.5 * WindowMeters;
        while (_cursor < Terrain.Length)
        {
            for (int k = 0; k < 24 && _cursor < Terrain.Length; k++, _cursor++)
            {
                int x = _cursor % Grid, y = _cursor / Grid;
                Terrain[_cursor] = (float)Sample((x + 0.5) * CellSize - half, (y + 0.5) * CellSize - half);
            }
            if (watch.Elapsed.TotalMilliseconds >= limitMs) return false;
        }
        SaveCache();
        BeginSolve();
        return true;
    }

    // ------------------------------------------------------------ the disk cache

    private const int CacheMagic = 0x4B574331;   // "KWC1"

    private double[] Probes()
    {
        double h = 0.5 * WindowMeters;
        return new[] { Sample(0, 0), Sample(0.37 * h, -0.61 * h), Sample(-0.83 * h, 0.29 * h) };
    }

    private bool TryLoadCache()
    {
        if (!WaterConfig.DiskCache) return false;
        try
        {
            string path = CachePath;
            if (!File.Exists(path)) return false;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.ReadInt32() != CacheMagic || r.ReadInt32() != Grid) return false;
                if (Math.Abs(r.ReadDouble() - WindowMeters) > 1e-6 || Math.Abs(r.ReadDouble() - Body.Radius) > 1e-3) return false;
                double[] stored = { r.ReadDouble(), r.ReadDouble(), r.ReadDouble() };
                double[] now = Probes();   // three heights read live: if the planet's terrain changed, the cache is stale
                for (int i = 0; i < 3; i++) if (Math.Abs(stored[i] - now[i]) > 0.5) return false;
                for (int i = 0; i < Terrain.Length; i++) Terrain[i] = r.ReadSingle();
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Water cache not used: " + ex.Message);
            return false;
        }
    }

    private void SaveCache()
    {
        if (!WaterConfig.DiskCache) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
            double[] probes = Probes();
            using (var w = new BinaryWriter(File.Create(CachePath)))
            {
                w.Write(CacheMagic); w.Write(Grid); w.Write(WindowMeters); w.Write(Body.Radius);
                foreach (double p in probes) w.Write(p);
                foreach (float t in Terrain) w.Write(t);
            }
        }
        catch (Exception ex) { Log.Debug("Water cache not written: " + ex.Message); }
    }

    // ------------------------------------------------------------ the water

    /// <summary>
    /// Works out the rain that runs off every cell (main thread: it reads the climate) and sets the tile solving. Rain is the
    /// climate's mean annual precipitation by latitude (HydraulicGeometry: 3000 h^2.2 mm), raised with altitude (orographic lift: up
    /// to +50 percent by 3 km), less the evaporation Turc's formula gives for the temperature there (33 cos^2 lat - 8 C, cooling 6.5 C
    /// a kilometre). The settings' runoff multiplier is a display setting.
    /// </summary>
    private void BeginSolve()
    {
        State = Stage.Solving;
        BodyClouds clouds = null;
        try { clouds = CloudWorld.Of(Body); if (clouds?.Weather != null) CloudWorld.PrepareWeather(clouds); } catch (Exception) { }
        _runoff = new float[Terrain.Length];
        double latitude0 = Latitude * Math.PI / 180.0;
        double meanPrecip = 0.0;
        for (int y = 0; y < Grid; y++)
        {
            double lat = latitude0 + ((y + 0.5) * CellSize - 0.5 * WindowMeters) / Body.Radius;
            double humidity = clouds?.Weather != null ? clouds.Weather.ClimateHumidity(lat) : 0.6;
            double precipitation = HydraulicGeometry.AnnualPrecipitation(humidity);
            meanPrecip += precipitation / Grid;
            double temperature = HydraulicGeometry.MeanTemperature(lat);
            for (int x = 0; x < Grid; x++)
            {
                double altitude = Math.Max(Terrain[y * Grid + x], 0.0);
                double p = precipitation * (1.0 + 0.5 * Math.Min(altitude / 3000.0, 1.0));
                double runoff = HydraulicGeometry.AnnualRunoff(p, temperature - 0.0065 * altitude) * WaterConfig.Runoff;
                _runoff[y * Grid + x] = (float)HydraulicGeometry.DischargeFromRunoff(runoff, CellSize * CellSize);
            }
        }
        _netEvaporation = Math.Max(HydraulicGeometry.LakeEvaporation(latitude0) - meanPrecip, 0.0);
        StartTask(null);
    }

    /// <summary>Gives the tile a coarser regional solution to take inflow from (rivers that start outside the window); solves again with it.</summary>
    public void UseRegional(HydroTile coarse)
    {
        if (coarse == null || coarse.Result == null || ReferenceEquals(coarse.Result, _coarseUsedFor)) return;
        if (State != Stage.Ready && State != Stage.Building && State != Stage.Solving) return;
        if (_task != null && !_task.IsCompleted) return;   // after the current job
        _coarseUsedFor = coarse.Result;
        HydroResult cr = coarse.Result;
        WindowFrame cf = coarse.Frame;
        double chalf = 0.5 * coarse.WindowMeters, fhalf = 0.5 * WindowMeters;
        WindowFrame ff = Frame;
        _coarse = (x, y) =>
        {
            D3 dir = ff.ToDirection(x - fhalf, y - fhalf);
            cf.FromDirection(dir, out double e, out double n);
            int cx = (int)Math.Floor((e + chalf) / cr.CellSize), cy = (int)Math.Floor((n + chalf) / cr.CellSize);
            double best = 0.0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = cx + dx, py = cy + dy;
                    if (px < 0 || py < 0 || px >= cr.Width || py >= cr.Height) continue;
                    best = Math.Max(best, cr.Discharge[py * cr.Width + px]);
                }
            return best;
        };
        State = Stage.Solving;
        StartTask(_coarse);
    }

    private void StartTask(Func<double, double, double> coarse)
    {
        float[] terrain = Terrain, runoff = _runoff;
        int grid = Grid;
        double cell = CellSize, gravity = Body.GeeASL * 9.80665, net = _netEvaporation;
        double minQ = WaterConfig.RiverMinDischarge, lakeDepth = WaterConfig.LakeMinDepth, lakeArea = WaterConfig.LakeMinArea;
        bool lakes = WaterConfig.Lakes, regional = Regional;
        double radius = Body.Radius;
        WindowFrame frame = Frame;
        double windowMeters = WindowMeters;
        _task = Task.Run(() =>
        {
            var input = new HydroInput
            {
                Width = grid, Height = grid, CellSize = cell, Terrain = terrain, Runoff = runoff, SeaLevel = 0f, Gravity = gravity,
                MinDischarge = regional ? Math.Max(minQ * 20.0, 5.0) : minQ,
                LakeMinDepth = lakeDepth, LakeMinArea = regional ? double.MaxValue : lakeArea, LakeNetEvaporation = net
            };
            if (!lakes) input.LakeMinArea = double.MaxValue;
            HydroResult result = HydroSolver.Solve(input);
            if (coarse != null && !regional)
            {
                float[] inflow = HydroSolver.RegionalInflow(input, result.Discharge, coarse);
                if (inflow != null)
                {
                    input.Inflow = inflow;
                    result = HydroSolver.Solve(input);
                }
            }
            RiverField field = regional ? null : new RiverField(result, terrain);
            WaterMeshArrays rivers = null, lakeMesh = null;
            if (!regional)
            {
                rivers = ToArrays(WaterMeshes.Rivers(result, 0.25), frame, radius, windowMeters, true);
                lakeMesh = ToArrays(WaterMeshes.Lakes(result), frame, radius, windowMeters, false);
            }
            return new Built { Result = result, Field = field, Rivers = rivers, Lakes = lakeMesh };
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) { Log.Exception("Working out the water of a window failed", t.Exception?.GetBaseException() ?? new Exception("unknown")); State = Stage.Failed; return; }
            _built = t.Result;
            State = Stage.Building;
        });
    }

    private sealed class Built { public HydroResult Result; public RiverField Field; public WaterMeshArrays Rivers, Lakes; }
    private volatile Built _built;

    /// <summary>On the main thread: takes what the worker built.</summary>
    public void Collect()
    {
        Built b = _built;
        if (b == null || State != Stage.Building) return;
        _built = null;
        Result = b.Result; Field = b.Field; RiverMesh = b.Rivers; LakeMesh = b.Lakes;
        MeshUploaded = false;
        State = Stage.Ready;
    }

    /// <summary>
    /// Puts a surface in the window's local frame (x east, y up, z north from the window's centre at sea level, on the sphere) with
    /// what the shader wants of each vertex.
    /// </summary>
    private static WaterMeshArrays ToArrays(WaterMeshData m, WindowFrame frame, double radius, double windowMeters, bool river)
    {
        int count = m.VertexCount;
        var a = new WaterMeshArrays
        {
            Positions = new Vector3[count], Uv0 = new Vector2[count], Uv1 = new Vector4[count], Uv2 = new Vector3[count], Indices = m.Indices.ToArray()
        };
        double half = 0.5 * windowMeters;
        for (int i = 0; i < count; i++)
        {
            double e = m.East[i] - half, n = m.North[i] - half;
            D3 dir = frame.ToDirection(e, n);
            double r = radius + m.Altitude[i];
            D3 p = dir * r;
            double x = D3.Dot(p, frame.East), y = D3.Dot(p, frame.Center) - radius, z = D3.Dot(p, frame.North);
            a.Positions[i] = new Vector3((float)x, (float)y, (float)z);
            a.MinY = Math.Min(a.MinY, (float)y); a.MaxY = Math.Max(a.MaxY, (float)y);
            a.Uv0[i] = river ? new Vector2(m.U[i], m.V[i]) : new Vector2((float)x, (float)z);
            a.Uv1[i] = new Vector4(m.Depth[i], m.Speed[i], m.Rapids[i], m.Fall[i]);
            a.Uv2[i] = new Vector3(m.FlowEast[i], m.FlowNorth[i], m.Width[i]);
        }
        return a;
    }
}
