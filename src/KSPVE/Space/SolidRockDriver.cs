using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>
/// Solid rocks round the active craft in belts and dusty disks whose rockCollisions is on (<see cref="SolidRocks"/>): real
/// objects KSP lights and collides with, kept within solidRockRange of the craft and moved each physics step at their true
/// orbital velocity relative to it. Parts hitting one crash as they would into anything (by their crash tolerance); a hit
/// too fast for one physics step to catch (a part could pass right through the rock) is caught by sweeping the craft's path,
/// and destroys it. Off in on-rails time warp. Call from FixedUpdate.
/// </summary>
internal static class SolidRockDriver
{
    private const int MaxRocks = 600;
    private const float ScanInterval = 0.25f;   // s between lookups of which rocks are near
    private const int ShapeVariants = 16;

    private sealed class Instance
    {
        public GameObject Go;
        public Rigidbody Body;
        public SolidRock Rock;   // as looked up at Time
        public double Time;
        public BlackHoleDefinition Owner;
        public float Radius;
        public bool Seen;
    }

    private struct Frame
    {
        public Vector3d Origin, T, B, N;
    }

    private static readonly Dictionary<long, Instance> Live = new Dictionary<long, Instance>();
    private static readonly List<SolidRock> Found = new List<SolidRock>();
    private static readonly List<long> Gone = new List<long>();
    private static readonly DiskGap[] GapBuffer = new DiskGap[DiskGaps.Max];
    private static Mesh[] _shapes;
    private static Material _material;
    private static MaterialPropertyBlock _tint;
    private static float _nextScan;
    private static bool _failed;
    private static Guid _vessel;

    public static void FixedUpdate()
    {
        if (_failed) return;
        try
        {
            Vessel active = HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
            Engine engine = Engine.Instance;
            bool onRails = TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRate > 1f;
            if (active == null || active.packed || onRails || engine == null || !engine.Settings.Get(CoreSettings.Enabled))
            {
                if (Live.Count > 0) Clear();
                return;
            }
            if (active.id != _vessel)
            {
                Clear();
                _vessel = active.id;
                _nextScan = 0f;
            }
            double ut = Planetarium.GetUniversalTime();
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + ScanInterval;
                Scan(active, ut);
            }
            Move(active, ut);
        }
        catch (Exception ex)
        {
            _failed = true;
            Clear();
            Log.Exception("Solid rocks failed; they are off for this session", ex);
        }
    }

    /// <summary>Finds the rocks now within range of the craft: new ones made, ones gone out of range removed.</summary>
    private static void Scan(Vessel active, double ut)
    {
        foreach (Instance inst in Live.Values) inst.Seen = false;
        var definitions = BlackHoles.Definitions;
        for (int d = 0; d < definitions.Count; d++)
        {
            BlackHoleDefinition hole = definitions[d];
            if (!hole.Enabled || hole.RockCollisions == RockCollisions.Off || hole.Rocks <= 0.0 || !HasRocks(hole.Kind)) continue;
            CelestialBody body = hole.Body;
            if (body == null || active.mainBody != body) continue;   // inside a planet's sphere of influence its rocks are not near
            Frame f = FrameOf(hole, body);
            Vector3d p = active.CoMD - f.Origin;
            var at = new Double3(Vector3d.Dot(p, f.T), Vector3d.Dot(p, f.B), Vector3d.Dot(p, f.N));
            Found.Clear();
            SolidRocks.Near(ShapeOf(hole, body), hole.RockCollisions, hole.Rocks, body.gravParameter, hole.Spin < 0 ? -1.0 : 1.0,
                hole.SolidRockSpacing, hole.SolidRockSize, hole.RockSizeSpread, SolidRocks.Seed(hole.BodyName, hole.Slot), ut, at,
                hole.SolidRockRange, Found);
            Found.Sort((a, b) => (a.Position - at).Length.CompareTo((b.Position - at).Length));   // the nearest first
            for (int k = 0; k < Found.Count; k++)
            {
                SolidRock rock = Found[k];
                if (Live.TryGetValue(rock.Id, out Instance inst))
                {
                    inst.Rock = rock;
                    inst.Time = ut;
                    inst.Owner = hole;
                    inst.Seen = true;
                    continue;
                }
                if (Live.Count >= MaxRocks) break;
                inst = Create(rock, hole);
                inst.Time = ut;
                Live[rock.Id] = inst;
            }
        }
        Gone.Clear();
        foreach (KeyValuePair<long, Instance> entry in Live)
            if (!entry.Value.Seen || entry.Value.Go == null) Gone.Add(entry.Key);
        foreach (long id in Gone)
        {
            if (Live[id].Go != null) UnityEngine.Object.Destroy(Live[id].Go);
            Live.Remove(id);
        }
    }

    /// <summary>Carries every rock along its orbit to where it is now, relative to the craft, moving at its velocity.</summary>
    private static void Move(Vessel active, double ut)
    {
        Rigidbody root = active.rootPart != null ? active.rootPart.rb : null;
        if (root == null) return;
        float dt = Time.fixedDeltaTime;
        Vector3d craft = active.CoMD, craftVelocity = active.obt_velocity;
        foreach (Instance inst in Live.Values)
        {
            if (inst.Go == null) continue;
            CelestialBody body = inst.Owner.Body;
            if (body == null) continue;
            Frame f = FrameOf(inst.Owner, body);
            double age = ut - inst.Time;
            Double3 p = inst.Rock.PositionAfter(age), v = inst.Rock.VelocityAfter(age);
            Vector3d world = f.Origin + f.T * p.X + f.B * p.Y + f.N * p.Z;
            Vector3d velocity = f.T * v.X + f.B * v.Y + f.N * v.Z;
            Vector3 relative = (Vector3)(velocity - craftVelocity);   // the rock's velocity relative to the craft
            Vector3 unityVelocity = root.velocity + relative;

            // Rocks grow in from the edge of the range rather than appear.
            double range = inst.Owner.SolidRockRange;
            float edge = Mathf.Clamp01((float)((range - (world - craft).magnitude) / (0.15 * range)));
            float size = Mathf.Max(inst.Radius * edge * edge * (3f - 2f * edge), 0.01f);
            if (Mathf.Abs(inst.Go.transform.localScale.x - size) > 1e-3f * inst.Radius) inst.Go.transform.localScale = Vector3.one * size;

            var position = (Vector3)world;
            inst.Body.position = position;
            inst.Body.MovePosition(position + unityVelocity * dt);
            if (Sweep(active, inst, position, relative, dt, size)) return;   // the craft is gone
        }
    }

    /// <summary>
    /// A hit too fast for the physics step: the craft would cover more than half the rock in one step and could pass
    /// through it. If its path this step comes within the rock, the impact destroys it, as a hit at that speed would.
    /// </summary>
    private static bool Sweep(Vessel active, Instance inst, Vector3 rock, Vector3 relative, float dt, float size)
    {
        float travel = relative.magnitude * dt;
        if (travel < 0.5f * size) return false;
        // In the rock's frame the craft moves by -relative * dt this step.
        Vector3 start = (Vector3)active.CoMD - rock, move = -relative * dt;
        float t = Mathf.Clamp01(-Vector3.Dot(start, move) / Mathf.Max(move.sqrMagnitude, 1e-9f));
        float closest = (start + move * t).magnitude;
        float reach = size + 0.5f * Mathf.Max(active.vesselSize.x, Mathf.Max(active.vesselSize.y, active.vesselSize.z));
        if (closest > reach) return false;
        string message = $"{active.vesselName} hit a rock at {relative.magnitude:N0} m/s";
        Log.Info(message + $" ({inst.Owner.BodyName}'s {inst.Owner.Kind}, rock radius {inst.Radius:N0} m).");
        ScreenMessages.PostScreenMessage(message, 5f, ScreenMessageStyle.UPPER_CENTER);
        foreach (Part part in active.parts.ToArray()) part.explode();
        return true;
    }

    private static bool HasRocks(SpaceObjectKind kind) =>
        kind == SpaceObjectKind.AsteroidBelt || kind == SpaceObjectKind.DebrisDisk || kind == SpaceObjectKind.ProtoplanetaryDisk;

    /// <summary>The object's disk frame in world axes (fixed in inertial space), centred on its body.</summary>
    private static Frame FrameOf(BlackHoleDefinition hole, CelestialBody body)
    {
        Vector3d n = hole.Normal.normalized;
        Vector3d t = Vector3d.Cross(n, Math.Abs(n.y) < 0.99 ? Vector3d.up : Vector3d.right).normalized;
        Vector3d b = Vector3d.Cross(n, t);
        QuaternionD rotation = Planetarium.Rotation;
        return new Frame { Origin = body.position, T = rotation * t, B = rotation * b, N = rotation * n };
    }

    /// <summary>Where the object's rocks can be, in metres (its edges, thickness and gaps, as the shader draws them).</summary>
    private static RockFieldShape ShapeOf(BlackHoleDefinition hole, CelestialBody body)
    {
        double metres = SpaceObjects.ShaderUnitMeters(hole.Kind, hole.HorizonMeters(body), hole.DiskOuter);
        var shape = new RockFieldShape
        {
            Kind = hole.Kind,
            Inner = hole.DiskInner * metres,
            Outer = hole.DiskOuter * metres,
            Thickness = hole.DiskThickness,
            Spread = hole.RockSpread,
        };
        int count = DiskGaps.Fill(hole.DiskInner, hole.DiskOuter, hole.Rings, hole.RingDepth, hole.RingWidth, hole.Gaps, hole.GapWidths, hole.GapDepths, GapBuffer);
        shape.Gaps = new DiskGap[count];
        for (int k = 0; k < count; k++) shape.Gaps[k] = new DiskGap(GapBuffer[k].Radius * metres, GapBuffer[k].Width * metres, GapBuffer[k].Depth);
        shape.GapCount = count;
        return shape;
    }

    private static Instance Create(SolidRock rock, BlackHoleDefinition hole)
    {
        EnsureAssets();
        int variant = (int)((ulong)rock.Id % ShapeVariants);
        var go = new GameObject("KSPVE rock") { layer = 0 };
        go.AddComponent<MeshFilter>().sharedMesh = _shapes[variant];
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        float shade = Mathf.Clamp01((float)hole.RockAlbedo * 2f);
        _tint.SetColor("_Color", new Color((float)hole.RockColor.X * shade, (float)hole.RockColor.Y * shade, (float)hole.RockColor.Z * shade, 1f));
        renderer.SetPropertyBlock(_tint);
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = _shapes[variant];
        collider.convex = true;
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var random = new System.Random((int)(rock.Id & 0x7FFFFFFF));
        go.transform.rotation = Quaternion.Euler((float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
        go.transform.localScale = Vector3.one * 0.01f;
        return new Instance { Go = go, Body = body, Rock = rock, Owner = hole, Radius = (float)rock.Radius, Seen = true };
    }

    private static void EnsureAssets()
    {
        if (_shapes != null) return;
        _shapes = new Mesh[ShapeVariants];
        for (int v = 0; v < ShapeVariants; v++) _shapes[v] = RockMesh(v);
        Shader shader = Shader.Find("KSP/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Standard");
        _material = new Material(shader) { mainTexture = Texture2D.whiteTexture };
        _tint = new MaterialPropertyBlock();
    }

    /// <summary>
    /// A rock of radius about 1: an icosphere (80 faces, few enough for a convex collider) squashed and dented at random,
    /// flat-shaded so its facets catch the light.
    /// </summary>
    private static Mesh RockMesh(int variant)
    {
        var random = new System.Random(9173 + variant * 7919);
        float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var points = new List<Vector3>
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1),
        };
        for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
        int[] faces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        // One subdivision: each face into four, the new corners pushed out onto the sphere.
        var midpoints = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midpoints.TryGetValue(key, out int index)) return index;
            points.Add(((points[a] + points[b]) * 0.5f).normalized);
            midpoints[key] = points.Count - 1;
            return points.Count - 1;
        }
        var triangles = new List<int>();
        for (int f = 0; f < faces.Length; f += 3)
        {
            int a = faces[f], b = faces[f + 1], c = faces[f + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            triangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        // Squashed along random axes, with a few broad dents.
        var squash = new Vector3(1f, 0.7f + 0.3f * (float)random.NextDouble(), 0.55f + 0.35f * (float)random.NextDouble());
        var dents = new Vector3[6];
        for (int k = 0; k < dents.Length; k++)
            dents[k] = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f).normalized;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float r = 1f;
            foreach (Vector3 d in dents) r -= 0.18f * Mathf.Pow(Mathf.Max(Vector3.Dot(p, d), 0f), 4f);
            points[i] = Vector3.Scale(p * r, squash);
        }
        // Flat shading: each face its own corners.
        var vertices = new Vector3[triangles.Count];
        var indices = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            vertices[i] = points[triangles[i]];
            indices[i] = i;
        }
        var mesh = new Mesh { name = "KSPVE rock " + variant, vertices = vertices, triangles = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Clear()
    {
        foreach (Instance inst in Live.Values)
            if (inst.Go != null) UnityEngine.Object.Destroy(inst.Go);
        Live.Clear();
    }
}
