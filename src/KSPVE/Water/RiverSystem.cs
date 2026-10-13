using System;
using System.Collections.Generic;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Water;
using KSPVE.Pure.Water.Hydrology;
using UnityEngine;
using UnityEngine.Rendering;
using D3 = KSPVE.Pure.Numerics.Double3;

namespace KSPVE.Water;

/// <summary>
/// Rivers and lakes at their real elevation. Round the camera a window of the body's terrain (a square tens of kilometres wide,
/// on a lattice so it is the same window each time) is read, its depressions filled to their spill level, the rain that runs off
/// added up down the slopes, and the channels and basins that result drawn as water: ribbons that follow the river bed down the
/// terrain, flat sheets at a lake's spill level, animated by the flow. The work is done a little a frame (terrain heights) or on
/// worker threads (the solver and the meshes); windows are cached in memory and the terrain on disk. A coarser regional window,
/// eight times wider, tells the fine one which large rivers run in from outside it.
/// </summary>
internal sealed class RiverSystem
{
    private const string ShaderName = "Hidden/KSPVE/Water/Surface";
    private const string NearCameraName = "Camera 00";
    private const int TerrainLayer = 15;
    /// <summary>The camera must be lower than this above the ground for the local water to be worth drawing.</summary>
    private const double MaxAltitude = 80000.0;
    private const int RegionalFactor = 8;
    private const int RegionalGrid = 128;

    private readonly Dictionary<string, HydroTile> _tiles = new Dictionary<string, HydroTile>();
    private HydroTile _current, _pending, _regional;
    private string _hydrologyKey;
    private CelestialBody _body;
    private Camera _camera;
    private Shader _shader;
    private Material _material;
    private Texture2D _ripples;
    private Mesh _riverMesh, _lakeMesh;
    private HydroTile _meshesFrom;

    /// <summary>The window currently drawn (for queries and the settings window's information), or null.</summary>
    public HydroTile Current => _current;
    public bool Active => _current != null && _current.State == HydroTile.Stage.Ready;

    /// <summary>The body the water is for now: one with a liquid sea, in flight.</summary>
    private static bool Qualifies(CelestialBody body) => body != null && body.pqsController != null && body.hasSolidSurface && (body.ocean || WaterConfig.LiquidOf(body.bodyName) != null);

    public void Update(bool allowed)
    {
        bool want = allowed && WaterConfig.Enabled && WaterConfig.Rivers && HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && ShaderLibrary.Loaded;
        CelestialBody body = want ? FlightGlobals.currentMainBody : null;
        if (!Qualifies(body)) body = null;
        if (body != _body)
        {
            DropTiles();
            _body = body;
        }
        string key = WaterConfig.HydrologyKey;
        if (key != _hydrologyKey)
        {
            _hydrologyKey = key;
            DropTiles();
        }
        if (_body == null) { _current = null; return; }
        if (_camera == null) _camera = FindCamera(NearCameraName);
        if (_camera == null) return;
        if (_shader == null) _shader = ShaderLibrary.Find(ShaderName);
        if (_shader == null) return;

        Vector3d camWorld = _camera.transform.position;
        PQS pqs = _body.pqsController;
        double altitude = (camWorld - _body.position).magnitude - _body.Radius;
        if (altitude > MaxAltitude) { _current = _pending == null ? _current : _current; return; }

        // Which window: snapped on a lattice a quarter of its size apart, so it is the same window each time.
        double windowMeters = WaterConfig.WindowKm * 1000.0;
        int grid = WaterConfig.GridSize;
        double lat = _body.GetLatitude(camWorld), lon = _body.GetLongitude(camWorld);
        HydroTile wanted = TileFor(lat, lon, windowMeters, grid, false);
        wanted.LastUsed = Time.realtimeSinceStartup;
        if (_current == null) { _current = wanted; }
        else if (wanted != _current)
        {
            // Keep drawing the current window until the new one is ready (or the camera has left the current one).
            _pending = wanted;
            if (wanted.State == HydroTile.Stage.Ready) { _current = wanted; _pending = null; }
        }
        // Regional window.
        HydroTile regional = TileFor(lat, lon, windowMeters * RegionalFactor, RegionalGrid, true);
        regional.LastUsed = Time.realtimeSinceStartup;
        _regional = regional;

        foreach (HydroTile t in _tiles.Values) t.Collect();
        if (_current != null && _regional != null && _regional.State == HydroTile.Stage.Ready) _current.UseRegional(_regional);
        if (_pending != null && _regional != null && _regional.State == HydroTile.Stage.Ready) _pending.UseRegional(_regional);
        if (_pending != null && _pending.State == HydroTile.Stage.Ready) { _current = _pending; _pending = null; }
        Prune();

        if (_current == null || _current.State != HydroTile.Stage.Ready) return;
        Upload(_current);
        Draw(_current, pqs);
    }

    private HydroTile TileFor(double lat, double lon, double windowMeters, int grid, bool regional)
    {
        double step = windowMeters / 4.0;
        double latStep = step / _body.Radius * 180.0 / Math.PI;
        long kLat = (long)Math.Round(lat / latStep);
        double latC = kLat * latStep;
        double lonStep = latStep / Math.Max(Math.Cos(latC * Math.PI / 180.0), 0.05);
        long kLon = (long)Math.Round(lon / lonStep);
        double lonC = kLon * lonStep;
        string key = string.Join("_", regional ? "R" : "F", kLat, kLon, ((long)windowMeters).ToString(), grid.ToString());
        string full = _body.bodyName + "|" + key;
        if (_tiles.TryGetValue(full, out HydroTile tile)) return tile;
        // The frame, in the body's local (PQS) frame: the centre direction, and east and north by a small step of longitude and latitude.
        Vector3d c = _body.GetRelSurfaceNVector(latC, lonC), e0 = _body.GetRelSurfaceNVector(latC, lonC + 0.01), n0 = _body.GetRelSurfaceNVector(Math.Min(latC + 0.01, 89.99), lonC);
        var frame = new WindowFrame(new D3(c.x, c.y, c.z), new D3(e0.x - c.x, e0.y - c.y, e0.z - c.z), new D3(n0.x - c.x, n0.y - c.y, n0.z - c.z), _body.Radius);
        tile = new HydroTile(_body, key, frame, latC, grid, windowMeters, regional);
        _tiles[full] = tile;
        if (tile.State == HydroTile.Stage.Sampling) TerrainSampler.Enqueue(tile);
        return tile;
    }

    private void Prune()
    {
        if (_tiles.Count <= 6) return;
        var drop = new List<string>();
        foreach (KeyValuePair<string, HydroTile> kv in _tiles)
        {
            HydroTile t = kv.Value;
            if (t == _current || t == _pending || t == _regional || t.State == HydroTile.Stage.Sampling) continue;
            drop.Add(kv.Key);
        }
        drop.Sort((a, b) => _tiles[a].LastUsed.CompareTo(_tiles[b].LastUsed));
        while (_tiles.Count > 6 && drop.Count > 0)
        {
            _tiles.Remove(drop[0]);
            drop.RemoveAt(0);
        }
    }

    private void DropTiles()
    {
        foreach (HydroTile t in _tiles.Values) TerrainSampler.Cancel(t);
        _tiles.Clear();
        _current = _pending = _regional = null;
        _meshesFrom = null;
        DestroyMeshes();
    }

    // ---------------------------------------------------------------- drawing

    private void Upload(HydroTile tile)
    {
        if (_meshesFrom == tile && tile.MeshUploaded) return;
        DestroyMeshes();
        _riverMesh = MakeMesh(tile.RiverMesh, "KSPVE rivers");
        _lakeMesh = MakeMesh(tile.LakeMesh, "KSPVE lakes");
        tile.MeshUploaded = true;
        _meshesFrom = tile;
    }

    private static Mesh MakeMesh(WaterMeshArrays a, string name)
    {
        if (a == null || a.Empty) return null;
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = a.Positions;
        mesh.uv = a.Uv0;
        mesh.SetUVs(1, new List<Vector4>(a.Uv1));
        mesh.SetUVs(2, new List<Vector3>(a.Uv2));
        mesh.SetIndices(a.Indices, MeshTopology.Triangles, 0);
        float lo = Math.Min(a.MinY, 0f) - 50f, hi = Math.Max(a.MaxY, 0f) + 50f;
        mesh.bounds = new Bounds(new Vector3(0f, 0.5f * (lo + hi), 0f), new Vector3(1e5f, hi - lo, 1e5f));
        return mesh;
    }

    private void DestroyMeshes()
    {
        if (_riverMesh != null) UnityEngine.Object.Destroy(_riverMesh);
        if (_lakeMesh != null) UnityEngine.Object.Destroy(_lakeMesh);
        _riverMesh = _lakeMesh = null;
    }

    private void Draw(HydroTile tile, PQS pqs)
    {
        if (_riverMesh == null && _lakeMesh == null) return;
        if (_material == null)
        {
            _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, name = "KSPVE water surface" };
            _ripples = MakeRippleTexture();
            _material.SetTexture("_KSPVE_Ripples", _ripples);
        }
        // The window's own frame to the world, in double precision (the planet is large; the window is near the origin).
        QuaternionD rot = (QuaternionD)pqs.transform.rotation;
        Vector3d origin = _body.position + rot * ((Vector3d)new Vector3d(tile.Frame.Center.X, tile.Frame.Center.Y, tile.Frame.Center.Z) * _body.Radius);
        Vector3 e = (Vector3)(rot * new Vector3d(tile.Frame.East.X, tile.Frame.East.Y, tile.Frame.East.Z));
        Vector3 u = (Vector3)(rot * new Vector3d(tile.Frame.Center.X, tile.Frame.Center.Y, tile.Frame.Center.Z));
        Vector3 n = (Vector3)(rot * new Vector3d(tile.Frame.North.X, tile.Frame.North.Y, tile.Frame.North.Z));
        var m = new Matrix4x4();
        m.SetColumn(0, new Vector4(e.x, e.y, e.z, 0f));
        m.SetColumn(1, new Vector4(u.x, u.y, u.z, 0f));
        m.SetColumn(2, new Vector4(n.x, n.y, n.z, 0f));
        m.SetColumn(3, new Vector4((float)origin.x, (float)origin.y, (float)origin.z, 1f));

        WaterLight.Apply(_body, u);
        FreshWater(out Vector4 deep, out Vector4 absorb, out Vector4 backscatter);
        double wind = WaterConfig.WindOverride >= 0.0 ? WaterConfig.WindOverride : 0.0;
        Vector2 windDir = new Vector2(1f, 0f);
        try
        {
            Vector3d w = CloudWorld.WindWorld(CloudWorld.Of(_body), origin);
            if (WaterConfig.WindOverride < 0.0) wind = w.magnitude;
            if (w.magnitude > 0.01)
            {
                Vector3 wl = ((Vector3)w).normalized;
                windDir = new Vector2(Vector3.Dot(wl, e), Vector3.Dot(wl, n));
            }
        }
        catch (Exception) { }
        _material.SetVector("_KSPVE_Fresh", deep);
        _material.SetVector("_KSPVE_FreshAbsorb", absorb);
        _material.SetVector("_KSPVE_FreshBackscatter", backscatter);
        _material.SetVector("_KSPVE_Flow", new Vector4((float)WaterConfig.FlowAnimation, WaterConfig.Foam ? 1f : 0f, (float)wind, (float)WaterConfig.Brightness));
        _material.SetVector("_KSPVE_LakeWind", new Vector4(windDir.x, windDir.y, 0f, 0f));
        if (_riverMesh != null) Graphics.DrawMesh(_riverMesh, m, _material, TerrainLayer, _camera, 0, null, false, false, false);
        if (_lakeMesh != null) Graphics.DrawMesh(_lakeMesh, m, _material, TerrainLayer, _camera, 0, null, false, false, false);
        _camera.depthTextureMode |= DepthTextureMode.Depth;
    }

    /// <summary>
    /// Fresh water: pure water plus dissolved organic matter (a stream's tea colour, absorbing blue: a(440) = 0.25 per metre falling as
    /// exp(-0.014 (lambda - 440 nm)), typical of streams and lakes, Kirk 1994) and a little suspended matter (b_b = 0.01 per metre).
    /// </summary>
    private static void FreshWater(out Vector4 deep, out Vector4 absorb, out Vector4 backscatter)
    {
        D3 w = WaterOptics.Wavelengths;
        D3 pure = WaterOptics.PureAbsorption;
        D3 a = new D3(pure.X + 0.25 * Math.Exp(-0.014 * (w.X - 440.0)), pure.Y + 0.25 * Math.Exp(-0.014 * (w.Y - 440.0)), pure.Z + 0.25 * Math.Exp(-0.014 * (w.Z - 440.0)));
        D3 bb = new D3(0.01, 0.01, 0.01);
        D3 r = WaterOptics.DeepReflectance(a, bb);
        deep = new Vector4((float)r.X, (float)r.Y, (float)r.Z, (float)WaterOptics.SeawaterIndex);
        absorb = new Vector4((float)a.X, (float)a.Y, (float)a.Z, 0f);
        backscatter = new Vector4((float)bb.X, (float)bb.Y, (float)bb.Z, 0f);
    }

    private static Texture2D MakeRippleTexture()
    {
        const int n = 128;
        float[] s = RippleTexture.Generate(n, 20240229);
        var colors = new Color[n * n];
        for (int i = 0; i < n * n; i++) colors[i] = new Color(s[i] * 0.5f + 0.5f, s[n * n + i] * 0.5f + 0.5f, 0.5f, 1f);
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true, true) { name = "KSPVE ripples", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.HideAndDontSave };
        t.SetPixels(colors);
        t.Apply(true, false);
        return t;
    }

    private static Camera FindCamera(string name)
    {
        int count = Camera.allCamerasCount;
        var cams = new Camera[count];
        Camera.GetAllCameras(cams);
        foreach (Camera c in cams) if (c != null && c.name == name) return c;
        return null;
    }

    // ---------------------------------------------------------------- queries

    /// <summary>
    /// The water at a world position (a lake or river of the window being drawn), or one with InWater false. The altitude is the
    /// point's above the sea; flow is returned as a world-space velocity (m/s).
    /// </summary>
    public bool WaterAtWorld(Vector3d world, double altitude, out WaterAt water, out Vector3d flowVelocity)
    {
        water = default;
        flowVelocity = Vector3d.zero;
        HydroTile tile = _current;
        if (tile == null || tile.State != HydroTile.Stage.Ready || tile.Field == null || _body == null) return false;
        PQS pqs = _body.pqsController;
        Vector3d rel = pqs.transform.InverseTransformPoint(world);
        if (rel.magnitude < 1.0) return false;
        Vector3d d = rel.normalized;
        tile.Frame.FromDirection(new D3(d.x, d.y, d.z), out double e, out double n);
        double half = 0.5 * tile.WindowMeters;
        water = tile.Field.At(e + half, n + half, altitude);
        if (!water.InWater) return false;
        if (water.Speed > 0.0)
        {
            QuaternionD rot = (QuaternionD)pqs.transform.rotation;
            Vector3d east = rot * new Vector3d(tile.Frame.East.X, tile.Frame.East.Y, tile.Frame.East.Z), north = rot * new Vector3d(tile.Frame.North.X, tile.Frame.North.Y, tile.Frame.North.Z);
            flowVelocity = (east * water.FlowEast + north * water.FlowNorth) * water.Speed;
        }
        return true;
    }

    public void Shutdown()
    {
        DropTiles();
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_ripples != null) UnityEngine.Object.Destroy(_ripples);
        _material = null; _ripples = null;
    }
}
