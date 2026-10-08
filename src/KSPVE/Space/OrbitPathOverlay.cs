using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Orbits;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Space;

/// <summary>
/// Draws, in the map view and the tracking station, the next lap of every celestial body's real path: from the bodies'
/// states as the game holds them now (Principia's, when it is installed), under all their mutual gravity
/// (<see cref="OrbitPaths"/>). Principia itself only draws what it has computed and stored, so a full lap of a slow
/// orbit takes that much game time to appear; this is computed in the background in well under a second, and again
/// every few seconds while game time moves, so it never drifts from the real state.
/// </summary>
internal static class OrbitPathOverlay
{
    private const float RefreshSeconds = 4f;    // real time between recomputations while game time moves
    private const int Layer = 9;                // as Principia's trajectories: lines in layer 31 vanish when zoomed out
    private const float NearAlpha = 1f, FarAlpha = 0.3f;   // the line fades along the lap

    private sealed class Line
    {
        public int[] Centre;
        public bool[] Whole;
        public int First, Count;                // in the point and vertex arrays
    }

    private static Task<OrbitPath[]> _job;
    private static List<CelestialBody> _jobBodies, _bodies;
    private static readonly List<Line> _lines = new List<Line>();
    private static Double3[] _points = new Double3[0];
    private static Vector3[] _vertices = new Vector3[0];
    private static double[] _systemMu = new double[0];
    private static Vector3d[] _systemPosition = new Vector3d[0];
    private static Mesh _mesh;
    private static Material _material;
    private static double _stateTime = double.NaN;
    private static float _nextRefresh;
    private static bool _errorLogged;

    public static void Update(bool enabled)
    {
        bool show = enabled && FlightGlobals.fetch != null && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 1
                    && (MapView.MapIsEnabled || HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                    && PlanetariumCamera.Camera != null;
        if (!show) return;
        try
        {
            Collect();
            Refresh();
            Draw();
        }
        catch (Exception ex)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            Log.Exception("Drawing orbit paths failed (logged once)", ex);
        }
    }

    /// <summary>Starts a computation from the bodies' states now, unless one is running or nothing has moved.</summary>
    private static void Refresh()
    {
        double now = Planetarium.GetUniversalTime();
        if (_job != null || Time.realtimeSinceStartup < _nextRefresh || (_lines.Count > 0 && now == _stateTime)) return;
        _nextRefresh = Time.realtimeSinceStartup + RefreshSeconds;
        _stateTime = now;

        List<CelestialBody> bodies = FlightGlobals.Bodies;
        var input = new PathBody[bodies.Count];
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody body = bodies[i];
            CelestialBody parent = body.orbitDriver != null && body.orbit != null ? body.orbit.referenceBody : null;
            int p = parent != null && parent != body ? bodies.IndexOf(parent) : -1;
            if (p < 0)
            {
                input[i] = new PathBody(-1, body.gravParameter, Double3.Zero, Double3.Zero);
                continue;
            }
            // The orbit's state is relative to the parent, along the world's axes (y and z swapped); the planetarium
            // frame is the inertial one, which stays valid if the world is turning with a planet.
            Vector3d r = Planetarium.Zup.WorldToLocal(body.orbit.pos), v = Planetarium.Zup.WorldToLocal(body.orbit.vel);
            input[i] = new PathBody(p, body.gravParameter, new Double3(r.x, r.y, r.z), new Double3(v.x, v.y, v.z));
        }
        _jobBodies = new List<CelestialBody>(bodies);
        _job = Task.Run(() => OrbitPaths.Compute(input));
    }

    /// <summary>Takes a finished computation's paths.</summary>
    private static void Collect()
    {
        if (_job == null || !_job.IsCompleted) return;
        Task<OrbitPath[]> job = _job;
        _job = null;
        if (job.IsFaulted)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                Log.Exception("Computing orbit paths failed (logged once)", job.Exception);
            }
            return;
        }

        OrbitPath[] paths = job.Result;
        _bodies = _jobBodies;
        _lines.Clear();
        int total = 0;
        foreach (OrbitPath path in paths) total += path.Points.Length;
        if (_points.Length < total)
        {
            _points = new Double3[total];
            _vertices = new Vector3[total];
        }
        var colors = new Color[_vertices.Length];
        var indices = new List<int>(2 * total);
        int next = 0;
        foreach (OrbitPath path in paths)
        {
            if (path.Points.Length < 2) continue;
            CelestialBody body = _bodies[path.Body];
            Color color = body.orbitDriver != null && body.orbitDriver.Renderer != null
                ? body.orbitDriver.Renderer.orbitColor
                : new Color(0.85f, 0.85f, 0.85f);
            var line = new Line { Centre = path.CentreBodies, Whole = path.CentreWholeSystems, First = next, Count = path.Points.Length };
            for (int k = 0; k < line.Count; k++)
            {
                _points[next] = path.Points[k];
                color.a = Mathf.Lerp(NearAlpha, FarAlpha, k / (float)(line.Count - 1));
                colors[next] = color;
                if (k > 0)
                {
                    indices.Add(next - 1);
                    indices.Add(next);
                }
                next++;
            }
            _lines.Add(line);
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
        }
        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.colors = colors;
        _mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
        // The vertices move every frame; never cull.
        _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e18f, 1e18f, 1e18f));
    }

    private static void Draw()
    {
        if (_lines.Count == 0 || _bodies == null) return;
        int n = _bodies.Count;
        if (_systemMu.Length < n)
        {
            _systemMu = new double[n];
            _systemPosition = new Vector3d[n];
        }
        // Every body's system (itself and all that orbits it) as one mass at its centre.
        for (int i = 0; i < n; i++)
        {
            _systemMu[i] = 0.0;
            _systemPosition[i] = Vector3d.zero;
        }
        for (int i = 0; i < n; i++)
        {
            CelestialBody body = _bodies[i];
            double mu = Math.Max(body.gravParameter, 1e-30);
            Vector3d weighted = body.position * mu;
            int j = i;
            for (int depth = 0; j >= 0 && depth < 16; depth++)
            {
                _systemMu[j] += mu;
                _systemPosition[j] += weighted;
                CelestialBody b = _bodies[j];
                CelestialBody parent = b.orbitDriver != null && b.orbit != null ? b.orbit.referenceBody : null;
                j = parent != null && parent != b ? _bodies.IndexOf(parent) : -1;
            }
        }

        // Inertial (planetarium) components to a world-space offset: y and z swap back.
        Vector3d ax = Planetarium.Zup.X.xzy, ay = Planetarium.Zup.Y.xzy, az = Planetarium.Zup.Z.xzy;
        foreach (Line line in _lines)
        {
            double mass = 0.0;
            Vector3d centre = Vector3d.zero;
            for (int k = 0; k < line.Centre.Length; k++)
            {
                int c = line.Centre[k];
                if (line.Whole[k])
                {
                    mass += _systemMu[c];
                    centre += _systemPosition[c];
                }
                else
                {
                    double mu = Math.Max(_bodies[c].gravParameter, 1e-30);
                    mass += mu;
                    centre += _bodies[c].position * mu;
                }
            }
            centre /= mass;
            for (int k = line.First, end = line.First + line.Count; k < end; k++)
            {
                Double3 p = _points[k];
                _vertices[k] = ScaledSpace.LocalToScaledSpace(centre + ax * p.X + ay * p.Y + az * p.Z);
            }
        }
        _mesh.vertices = _vertices;

        if (_material == null) _material = new Material(Shader.Find("KSP/Particles/Additive"));
        Graphics.DrawMesh(_mesh, Matrix4x4.identity, _material, Layer, PlanetariumCamera.Camera, 0, null, false, false);
    }
}
