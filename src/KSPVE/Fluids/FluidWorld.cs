using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Fluids;
using KSPVE.Stars;
using UnityEngine;

namespace KSPVE.Fluids;

/// <summary>Where a body's atmosphere is: waiting, being spun up, running, or given up on.</summary>
internal enum FluidState
{
    Waiting,
    SpinningUp,
    Ready,
    Failed
}

/// <summary>
/// One body with a simulated atmosphere. The simulation lives on the worker thread (<see cref="FluidWorker"/>); the main
/// thread reads a snapshot of its fields (<see cref="Snapshot"/>, under <see cref="Lock"/>) and uploads it to a texture.
/// </summary>
internal sealed class FluidBody
{
    public CelestialBody Body;
    public FluidProfile Profile;
    /// <summary>The rotation period used for the pattern and the flow, s (always positive; the sign is in the body).</summary>
    public double RotationPeriod;
    public double Gravity;

    public volatile FluidState State = FluidState.Waiting;
    public AtmosphereSimulation Sim;
    public string Status = "waiting";
    public double SpinUpSeconds;
    /// <summary>Priority for the worker (smaller first): distance of the camera in radii.</summary>
    public double Priority = 1e30;

    public readonly object Lock = new object();
    public float[] Snapshot;
    public int Version;
    public int Uploaded = -1;
    /// <summary>The game time (s) of the model's time zero, and the time it should be at now.</summary>
    public double BaseUt;
    public double TargetUt;
    public bool HasTarget;
    /// <summary>How far the model is behind the game clock, s.</summary>
    public double Lag;
    public double SubstellarLongitude;
    /// <summary>Eastward zonal jets in the flow now (counted on the worker thread, which owns the model).</summary>
    public int JetCount;
    public int Chunks;

    // Main-thread resources.
    public Texture2D FieldTexture, EmissionTexture, EmissionInfraredTexture;
    public float[] UploadFloats;
    public byte[] UploadBytes;
    public float LastUpload;
}

/// <summary>
/// The bodies with atmospheres: reads KSPVE_FLUIDS, decides which bodies get one (brown dwarfs found in the game, bodies the
/// config names, and with the setting on gas giants that have no EVE clouds), keeps their profiles and the worker thread that
/// runs their simulations.
/// </summary>
internal static class FluidWorld
{
    public const string NodeName = "KSPVE_FLUIDS";
    public const double SunDiscRadiance = 2.0;

    private static readonly Dictionary<string, CfgNode> Nodes = new Dictionary<string, CfgNode>(StringComparer.Ordinal);
    private static readonly List<FluidBody> Bodies = new List<FluidBody>();
    private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
    private static FluidProfile _default = new FluidProfile();
    private static float _scanned = -100f;
    private static FluidWorker _worker;

    public static IReadOnlyList<FluidBody> All => Bodies;

    public static void Load(List<ConfigIssue> issues)
    {
        Nodes.Clear();
        _default = new FluidProfile();
        FluidSettings.Reset();
        try
        {
            if (GameDatabase.Instance != null)
            {
                foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(NodeName))
                {
                    CfgNode cfg = KspConfig.ToCfg(node);
                    FluidSettings.Read(cfg);
                    foreach (CfgNode child in cfg.Children)
                    {
                        if (child.Name == "DEFAULT") _default = FluidProfile.From(child, _default);
                        else if (child.Name == "BODY")
                        {
                            string body = child.GetLast("body");
                            if (string.IsNullOrWhiteSpace(body)) { issues?.Add(new ConfigIssue(IssueSeverity.Warning, NodeName + "/BODY", "a BODY needs a body")); continue; }
                            body = body.Trim();
                            Nodes[body] = Nodes.TryGetValue(body, out CfgNode old) ? CfgNode.Overlay(old, child) : child;
                        }
                    }
                }
            }
            foreach (CfgNode user in KspConfig.LoadUserNodes())
                if (user.Name == FluidSettings.UserNode) FluidSettings.Read(user);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading " + NodeName + " failed", ex);
        }
    }

    public static void Shutdown()
    {
        _worker?.Stop();
        _worker = null;
    }

    /// <summary>Called when the player changes a setting that decides which bodies have atmospheres.</summary>
    public static void Rescan() => _scanned = -100f;

    /// <summary>Every few seconds: finds the bodies that should have atmospheres and starts new ones.</summary>
    public static void Update(double ut)
    {
        if (!FluidSettings.Enabled || FlightGlobals.fetch == null || FlightGlobals.Bodies == null || Planetarium.fetch == null) return;
        if (_worker == null)
        {
            SphereGrid.MaxThreads = 2;
            _worker = new FluidWorker(Bodies);
        }
        if (Time.realtimeSinceStartup - _scanned > 3f)
        {
            _scanned = Time.realtimeSinceStartup;
            try { Scan(ut); }
            catch (Exception ex) { Log.Exception("Looking for bodies with fluid atmospheres failed", ex); }
        }
        Camera cam = Camera.main;
        foreach (FluidBody fb in Bodies)
        {
            lock (fb.Lock)
            {
                fb.TargetUt = ut;
                fb.HasTarget = true;
            }
            if (fb.Body != null && fb.Body.scaledBody != null && cam != null)
                fb.Priority = (fb.Body.scaledBody.transform.position - cam.transform.position).magnitude / Math.Max(fb.Body.Radius * ScaledSpace.InverseScaleFactor, 1e-6);
        }
    }

    private static void Scan(double ut)
    {
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            if (b == null || b.scaledBody == null || Seen.Contains(b.bodyName)) continue;
            FluidProfile profile = ProfileFor(b);
            if (profile == null) continue;
            Seen.Add(b.bodyName);
            var fb = new FluidBody { Body = b, Profile = profile };
            double period = profile.RotationPeriodHours > 0.0 ? profile.RotationPeriodHours * 3600.0 : (b.rotates ? Math.Abs(b.rotationPeriod) : 0.0);
            if (!(period > 600.0)) period = 5.0 * 3600.0;   // a body that does not turn: a typical brown dwarf's five hours
            fb.RotationPeriod = period;
            double g = profile.Gravity > 0.0 ? profile.Gravity : (b.Radius > 1.0 ? b.gravParameter / (b.Radius * b.Radius) : 0.0);
            fb.Gravity = g > 1.0 ? g : FluidPhysics.DwarfGravity;
            fb.SubstellarLongitude = SubstellarLongitude(b, ut, period);
            fb.TargetUt = ut;
            fb.HasTarget = true;
            lock (Bodies) Bodies.Add(fb);
            Log.Info($"Fluid atmosphere for {b.bodyName}: {profile.Kind}, {profile.Temperature:0} K, rotation {period / 3600.0:0.0} h, K = {profile.Resolution}, seed {profile.Seed}.");
        }
    }

    /// <summary>The profile a body would get, or null for a body that gets no atmosphere.</summary>
    private static FluidProfile ProfileFor(CelestialBody b)
    {
        Nodes.TryGetValue(b.bodyName, out CfgNode node);
        FluidProfile p = _default.Clone();
        p.Body = b.bodyName;
        bool dwarf = StarWorld.IsStar(b) && StarCatalogue.IsBrownDwarf(b);
        bool named = node != null;
        if (named) p = FluidProfile.From(node, p);
        if (!p.Enabled) return null;

        if (dwarf && !named)
        {
            if (!FluidSettings.BrownDwarfs) return null;
            p.Temperature = Math.Min(StarCatalogue.EffectiveTemperature(b), 2500.0);
            p.KindSet = false;
            p.Kind = Decks.KindForTemperature(p.Temperature);
        }
        else if (!named)
        {
            if (!FluidSettings.GasGiants || StarWorld.IsStar(b) || b.hasSolidSurface || !b.atmosphere) return null;
            BodyClouds clouds = CloudWorld.Loaded ? CloudWorld.Of(b) : null;
            if (clouds != null && clouds.Layers.Count > 0) return null;   // EVE draws clouds on it already
            p.Kind = FluidKind.Giant;
            p.KindSet = true;
            p.Temperature = 124.0;
        }
        else
        {
            if (p.Kind == FluidKind.HotJupiter && !FluidSettings.HotJupiters) return null;
            if (dwarf && !p.KindSet && !FluidSettings.BrownDwarfs) return null;
        }
        if (p.Kind == FluidKind.HotJupiter) p.TidallyLocked = true;
        if (p.Seed == 0) p.Seed = StableHash(b.bodyName);
        return p;
    }

    private static ulong StableHash(string s)
    {
        ulong h = 1469598103934665603UL;
        unchecked
        {
            foreach (char c in s) h = (h ^ c) * 1099511628211UL;
        }
        return h % 1000000007UL + 1UL;
    }

    // ---------------------------------------------------------------- the body's frame

    /// <summary>
    /// The body's frame in the world (model frame: x towards longitude 0 at the turn's start, y north, z towards longitude 90
    /// east), the way the star feature builds it; a retrograde body turns the other way.
    /// </summary>
    public static void Basis(CelestialBody body, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        Vector3d axis = body.RotationAxis;
        if (axis.sqrMagnitude < 1e-12) axis = Vector3d.up;
        Vector3d yc = axis.normalized;
        Vector3d reference = Math.Abs(Vector3d.Dot(yc, Vector3d.forward)) > 0.95 ? Vector3d.right : Vector3d.forward;
        Vector3d xc = (reference - yc * Vector3d.Dot(reference, yc)).normalized;
        Vector3d zc = Vector3d.Cross(yc, xc).normalized;
        if (body.rotationPeriod < 0.0) zc = -zc;
        QuaternionD rotation = Planetarium.Rotation;
        x = (Vector3)(rotation * xc);
        y = (Vector3)(rotation * yc);
        z = (Vector3)(rotation * zc);
    }

    /// <summary>The frame turned by the body's rotation: where longitude 0 and 90 east point now.</summary>
    public static void TurnedBasis(CelestialBody body, double rotationPeriod, double ut, out Vector3 lon0, out Vector3 north, out Vector3 lon90)
    {
        Basis(body, out Vector3 x, out Vector3 y, out Vector3 z);
        double turn = 2.0 * Math.PI * ((ut / rotationPeriod) % 1.0);
        float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
        lon0 = x * c + z * s;
        lon90 = z * c - x * s;
        north = y;
    }

    private static double SubstellarLongitude(CelestialBody body, double ut, double period)
    {
        CelestialBody star = StarWorld.StarOf(body);
        if (star == null || star == body) return 0.0;
        TurnedBasis(body, period, ut, out Vector3 lon0, out _, out Vector3 lon90);
        Vector3 toStar = (Vector3)(star.position - body.position);
        toStar.Normalize();
        double lon = Math.Atan2(Vector3.Dot(toStar, lon90), Vector3.Dot(toStar, lon0));
        return lon < 0.0 ? lon + 2.0 * Math.PI : lon;
    }

    // ---------------------------------------------------------------- light from other stars

    /// <summary>
    /// The stars that light a body (not the body itself): direction and strength relative to the home planet's sunlight,
    /// from the stars' radii, effective temperatures and distances (flux goes as (R / d)^2 T^4), brightest first.
    /// </summary>
    public static int LightOf(CelestialBody body, Vector3[] directions, float[] strengths)
    {
        int count = 0;
        CelestialBody home = FlightGlobals.GetHomeBody();
        CelestialBody homeStar = home != null ? StarWorld.StarOf(home) : null;
        double reference = homeStar != null && home != null ? RelativeFlux(homeStar, (home.position - homeStar.position).magnitude) : 1.0;
        if (!(reference > 0.0)) reference = 1.0;
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody s = bodies[i];
            if (s == null || s == body || !StarWorld.IsStar(s)) continue;
            Vector3d d = s.position - body.position;
            double dist = d.magnitude;
            if (!(dist > 1.0)) continue;
            float strength = (float)(RelativeFlux(s, dist) / reference);
            if (!(strength > 1e-4f)) continue;
            // Insert, keeping the brightest first.
            int at = count;
            while (at > 0 && strengths[at - 1] < strength) at--;
            if (at >= directions.Length) continue;
            for (int k = Math.Min(count, directions.Length - 1); k > at; k--) { directions[k] = directions[k - 1]; strengths[k] = strengths[k - 1]; }
            directions[at] = (Vector3)(d / dist);
            strengths[at] = strength;
            if (count < directions.Length) count++;
        }
        return count;
    }

    private static double RelativeFlux(CelestialBody star, double distance)
    {
        double t = StarCatalogue.EffectiveTemperature(star) / 5772.0;
        double r = star.Radius / distance;
        return r * r * t * t * t * t;
    }
}

/// <summary>
/// The thread that runs the simulations, so a spin-up or a burst of time warp never stalls a frame. A body that is waiting is
/// spun up (at full speed, nearest first); a running one is advanced towards the game clock in short chunks, sleeping between
/// them so that the thread uses no more than <see cref="FluidSettings.WorkShare"/> of a core. That is the cap on the work
/// under time warp: the model takes as many steps as the share allows; if it falls more than
/// <see cref="FluidSettings.MaxLagDays"/> behind the game clock it jumps the rest without integrating.
/// </summary>
internal sealed class FluidWorker
{
    private const int ChunkSteps = 6;
    private readonly List<FluidBody> _bodies;
    private readonly Thread _thread;
    private volatile bool _stop;

    public FluidWorker(List<FluidBody> bodies)
    {
        _bodies = bodies;
        _thread = new Thread(Run) { IsBackground = true, Name = "KSPVE fluids", Priority = System.Threading.ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Stop() => _stop = true;

    private void Run()
    {
        var watch = new Stopwatch();
        while (!_stop)
        {
            FluidBody job = Pick();
            if (job == null)
            {
                Thread.Sleep(60);
                continue;
            }
            watch.Restart();
            try
            {
                if (job.State == FluidState.Waiting) SpinUp(job);
                else Advance(job);
            }
            catch (Exception ex)
            {
                job.State = FluidState.Failed;
                job.Status = "failed: " + ex.Message;
                Log.Exception("The fluid atmosphere of " + (job.Body != null ? job.Body.bodyName : "?") + " failed", ex);
            }
            // Rest so that the thread's share of a core is what the setting says.
            double busy = watch.Elapsed.TotalMilliseconds;
            if (job.State == FluidState.Ready)
            {
                double share = Math.Max(0.05, Math.Min(1.0, FluidSettings.WorkShare));
                int rest = (int)Math.Min(500.0, busy * (1.0 - share) / share);
                if (rest > 0) Thread.Sleep(rest);
            }
        }
    }

    /// <summary>A waiting body (nearest first) before a running one that is behind (most behind first).</summary>
    private FluidBody Pick()
    {
        FluidBody best = null;
        double bestScore = double.MaxValue;
        lock (_bodies)
        {
            foreach (FluidBody b in _bodies)
            {
                if (b.State == FluidState.Failed || b.State == FluidState.SpinningUp) continue;
                double score;
                if (b.State == FluidState.Waiting) score = b.Priority;
                else
                {
                    if (b.Sim == null || !b.HasTarget) continue;
                    double target;
                    lock (b.Lock) target = b.TargetUt - b.BaseUt;
                    if (target - b.Sim.Time < b.Sim.NextDt) continue;
                    score = 1e20 - (target - b.Sim.Time);
                }
                if (score < bestScore) { bestScore = score; best = b; }
            }
        }
        return best;
    }

    private static void SpinUp(FluidBody b)
    {
        b.State = FluidState.SpinningUp;
        b.Status = "spinning up";
        var watch = Stopwatch.StartNew();
        var sim = new AtmosphereSimulation(b.Profile, b.Body.Radius, b.RotationPeriod, b.Gravity, b.SubstellarLongitude);
        sim.SpinUp(() => false);
        b.SpinUpSeconds = watch.Elapsed.TotalSeconds;
        lock (b.Lock)
        {
            b.Sim = sim;
            b.Snapshot = new float[sim.Tracers.NLon * sim.Tracers.NLat * 4];
            sim.WriteFields(b.Snapshot);
            b.Version++;
            b.BaseUt = b.TargetUt;
        }
        b.JetCount = Jets.Count(sim.Model.ZonalMeanWind(), 0.3);
        b.Status = $"running (spun up in {b.SpinUpSeconds:0} s, {sim.Model.RmsSpeed():0} m/s rms wind)";
        b.State = FluidState.Ready;
        Log.Info($"Fluid atmosphere of {b.Body.bodyName} spun up in {b.SpinUpSeconds:0.0} s ({sim.Steps} steps), rms wind {sim.Model.RmsSpeed():0} m/s, {b.JetCount} eastward jets.");
    }

    private static void Advance(FluidBody b)
    {
        AtmosphereSimulation sim = b.Sim;
        double target;
        lock (b.Lock)
        {
            target = b.TargetUt - b.BaseUt;
            // The game clock went back (a reverted flight or an earlier save): keep the weather and move the time zero.
            if (target < sim.Time - 86400.0)
            {
                b.BaseUt = b.TargetUt - sim.Time;
                target = sim.Time;
            }
        }
        double maxLag = FluidSettings.MaxLagDays * 86400.0;
        if (target - sim.Time > maxLag) sim.SkipTo(target - maxLag);
        sim.AdvanceTo(target, ChunkSteps);
        if (++b.Chunks % 40 == 0) b.JetCount = Jets.Count(sim.Model.ZonalMeanWind(), 0.3);
        lock (b.Lock)
        {
            sim.WriteFields(b.Snapshot);
            b.Version++;
            b.Lag = Math.Max(0.0, target - sim.Time);
        }
    }
}
