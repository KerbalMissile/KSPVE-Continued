using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Orbits;

/// <summary>A body for <see cref="OrbitPaths"/>: its parent (-1 for the root), its mass, and its state relative to its parent.</summary>
public readonly struct PathBody
{
    public readonly int Parent;
    public readonly double Mu;
    public readonly Double3 Position;
    public readonly Double3 Velocity;

    public PathBody(int parent, double muM3PerS2, Double3 positionFromParentMeters, Double3 velocityFromParentMetersPerSecond)
    {
        Parent = parent;
        Mu = muM3PerS2;
        Position = positionFromParentMeters;
        Velocity = velocityFromParentMetersPerSecond;
    }
}

/// <summary>
/// The path one body follows over its next lap. Points are relative to the centre of mass of <see cref="CentreBodies"/>,
/// each taken alone or, where <see cref="CentreWholeSystems"/> is set, together with everything orbiting it.
/// </summary>
public sealed class OrbitPath
{
    public int Body;
    public int[] CentreBodies;
    public bool[] CentreWholeSystems;
    public Double3[] Points;
    public double Seconds;
    /// <summary>False when the body did not come round within the time allowed (an escape, or a wildly changing orbit).</summary>
    public bool FullLap;
}

/// <summary>
/// One lap ahead of every body's real path, from the bodies' states now, under all their mutual gravity (point masses).
/// Principia can only draw what it has already computed and stored, which for orbits of centuries is tens of gigabytes;
/// this keeps only the line. It works in levels, so that a moon's hours and a star's centuries cost the same: for each
/// body with satellites, that body, its satellites (each with its own satellites lumped at their centre of mass) and,
/// as perturbers, its ancestors with their other satellites. Within a level the satellites are taken in tiers of similar
/// period, the faster ones merged into the centre for the slower tiers. A satellite's path is drawn around the mass
/// inside its orbit: its parent, plus any heavy sibling closer in (a planet outside a companion star circles both; the
/// stars ringing a pair of black holes circle the pair). The root gets a path too when a satellite outweighs a tenth of it.
/// </summary>
public static class OrbitPaths
{
    public const int SamplesPerLap = 180;
    private const int StepsPerLap = 96;             // of the fastest orbit in a tier (4th-order steps)
    private const double TierPeriodRatio = 64.0;    // orbits this much slower wait for the next tier
    private const double HeavySibling = 1e-3;       // of the parent's mass: counts towards the centre of outer orbits
    private const double HeavyCompanion = 0.1;      // of the root's mass: the root itself moves visibly
    private const double LapAllowance = 1.3;        // times the estimated period before giving up on a lap

    public static OrbitPath[] Compute(IReadOnlyList<PathBody> bodies)
    {
        int n = bodies.Count;
        var kids = new List<int>[n];
        for (int i = 0; i < n; i++) kids[i] = new List<int>();
        int root = -1;
        for (int i = 0; i < n; i++)
        {
            int p = bodies[i].Parent;
            if (p >= 0 && p < n && p != i) kids[p].Add(i);
            else if (root < 0) root = i;
        }
        var paths = new List<OrbitPath>();
        if (root < 0) return paths.ToArray();

        // Absolute states (the root at rest at the origin: only differences matter) and each subtree as one mass.
        var pos = new Double3[n];
        var vel = new Double3[n];
        var mu = new double[n];
        var order = new List<int> { root };
        for (int k = 0; k < order.Count; k++)
        {
            int b = order[k];
            foreach (int c in kids[b])
            {
                pos[c] = pos[b] + bodies[c].Position;
                vel[c] = vel[b] + bodies[c].Velocity;
                order.Add(c);
            }
        }
        for (int i = 0; i < n; i++) mu[i] = Math.Max(bodies[i].Mu, 1e-30);
        var sysMu = (double[])mu.Clone();
        var sysPos = new Double3[n];
        var sysVel = new Double3[n];
        for (int i = 0; i < n; i++)
        {
            sysPos[i] = pos[i] * mu[i];
            sysVel[i] = vel[i] * mu[i];
        }
        for (int k = order.Count - 1; k > 0; k--)
        {
            int b = order[k], p = bodies[b].Parent;
            sysMu[p] += sysMu[b];
            sysPos[p] += sysPos[b];
            sysVel[p] += sysVel[b];
        }
        foreach (int b in order)
        {
            sysPos[b] /= sysMu[b];
            sysVel[b] /= sysMu[b];
        }

        foreach (int node in order)
        {
            if (kids[node].Count == 0) continue;
            Level(node, node == root, bodies, kids, pos, vel, mu, sysMu, sysPos, sysVel, paths);
        }
        return paths.ToArray();
    }

    private sealed class Tracked
    {
        public int Local;               // index in the level's arrays
        public int Body;                // index in the input
        public int[] Centre;            // local indices
        public double Period;           // seconds, estimated
        public bool Bound;
        public List<Double3> Points = new List<Double3>();
        public double Angle, SinceSample, Seconds;
        public Double3 Last;
        public bool Done, FullLap;
    }

    private static void Level(int node, bool isRoot, IReadOnlyList<PathBody> bodies, List<int>[] kids, Double3[] pos,
        Double3[] vel, double[] mu, double[] sysMu, Double3[] sysPos, Double3[] sysVel, List<OrbitPath> paths)
    {
        // Local bodies: the node alone, its satellites as lumps, then the perturbers.
        var id = new List<int> { node };
        var whole = new List<bool> { false };
        foreach (int c in kids[node])
        {
            id.Add(c);
            whole.Add(true);
        }
        int satellites = kids[node].Count;
        for (int child = node, anc = bodies[node].Parent; anc >= 0 && anc != child; child = anc, anc = bodies[anc].Parent)
        {
            id.Add(anc);
            whole.Add(false);
            foreach (int s in kids[anc])
            {
                if (s == child) continue;
                id.Add(s);
                whole.Add(true);
            }
            if (bodies[anc].Parent == anc) break;
        }
        int m = id.Count;
        var x0 = new Double3[m];
        var v0 = new Double3[m];
        var gm0 = new double[m];
        for (int i = 0; i < m; i++)
        {
            x0[i] = whole[i] ? sysPos[id[i]] : pos[id[i]];
            v0[i] = whole[i] ? sysVel[id[i]] : vel[id[i]];
            gm0[i] = whole[i] ? sysMu[id[i]] : mu[id[i]];
        }

        var tracked = new List<Tracked>();
        for (int j = 1; j <= satellites; j++)
        {
            double dj = (x0[j] - x0[0]).Length;
            var centre = new List<int> { 0 };
            for (int s = 1; s <= satellites; s++)
            {
                if (s != j && gm0[s] > HeavySibling * gm0[0] && (x0[s] - x0[0]).Length < dj) centre.Add(s);
            }
            tracked.Add(NewTracked(j, id[j], centre.ToArray(), x0, v0, gm0));
        }
        if (isRoot)
        {
            int heaviest = 0;
            for (int s = 1; s <= satellites; s++)
            {
                if (heaviest == 0 || gm0[s] > gm0[heaviest]) heaviest = s;
            }
            if (heaviest > 0 && gm0[heaviest] > HeavyCompanion * gm0[0])
                tracked.Add(NewTracked(0, node, new[] { 0, heaviest }, x0, v0, gm0));
        }
        tracked.RemoveAll(t => t.Period <= 0.0 || double.IsNaN(t.Period));
        tracked.Sort((a, b) => a.Period.CompareTo(b.Period));

        var merged = new bool[m];
        int first = 0;
        while (first < tracked.Count)
        {
            double fastest = tracked[first].Period;
            int last = first;
            while (last + 1 < tracked.Count && tracked[last + 1].Period <= TierPeriodRatio * fastest) last++;
            Integrate(tracked, first, last, x0, v0, gm0, merged, fastest / StepsPerLap);
            // The slower tiers see this tier's satellites as part of the centre.
            for (int t = first; t <= last; t++)
            {
                if (tracked[t].Local != 0) merged[tracked[t].Local] = true;
            }
            first = last + 1;
        }

        foreach (Tracked t in tracked)
        {
            var centreBodies = new int[t.Centre.Length];
            var centreWhole = new bool[t.Centre.Length];
            for (int k = 0; k < t.Centre.Length; k++)
            {
                centreBodies[k] = id[t.Centre[k]];
                centreWhole[k] = whole[t.Centre[k]];
            }
            paths.Add(new OrbitPath
            {
                Body = t.Body,
                CentreBodies = centreBodies,
                CentreWholeSystems = centreWhole,
                Points = t.Points.ToArray(),
                Seconds = t.Seconds,
                FullLap = t.FullLap
            });
        }
    }

    private static Tracked NewTracked(int local, int body, int[] centre, Double3[] x, Double3[] v, double[] gm)
    {
        // The root's own path is about itself and its heavy companion: leave it out of its own centre's orbit estimate.
        double mc = 0.0;
        Double3 cx = Double3.Zero, cv = Double3.Zero;
        foreach (int k in centre)
        {
            if (k == local) continue;
            mc += gm[k];
            cx += x[k] * gm[k];
            cv += v[k] * gm[k];
        }
        var t = new Tracked { Local = local, Body = body, Centre = centre };
        if (mc <= 0.0) return t;
        Double3 r = x[local] - cx / mc, w = v[local] - cv / mc;
        double distance = r.Length, total = mc + gm[local];
        if (distance <= 0.0) return t;
        double energy = 0.5 * w.LengthSquared - total / distance;
        if (energy < 0.0)
        {
            double a = -total / (2.0 * energy);
            t.Period = 2.0 * Math.PI * Math.Sqrt(a * a * a / total);
            t.Bound = true;
        }
        else
        {
            // Not bound: follow it for as long as a circular orbit at this distance would take.
            t.Period = 2.0 * Math.PI * Math.Sqrt(distance * distance * distance / total);
        }
        return t;
    }

    private static Double3 Relative(Tracked t, Double3[] x, double[] gm, bool[] merged)
    {
        // Merged satellites already sit inside local body 0.
        double mc = 0.0;
        Double3 c = Double3.Zero;
        bool zero = false;
        foreach (int k in t.Centre)
        {
            int j = merged[k] ? 0 : k;
            if (j == 0)
            {
                if (zero) continue;
                zero = true;
            }
            mc += gm[j];
            c += x[j] * gm[j];
        }
        return x[t.Local] - c / mc;
    }

    private static void Integrate(List<Tracked> tracked, int first, int last, Double3[] x0, Double3[] v0, double[] gm0,
        bool[] merged, double dt)
    {
        int m = x0.Length;
        var x = (Double3[])x0.Clone();
        var v = (Double3[])v0.Clone();
        var gm = (double[])gm0.Clone();
        // Merge the faster satellites into the centre, at their common centre of mass.
        Double3 mx = x[0] * gm[0], mv = v[0] * gm[0];
        for (int i = 1; i < m; i++)
        {
            if (!merged[i]) continue;
            mx += x[i] * gm[i];
            mv += v[i] * gm[i];
            gm[0] += gm[i];
        }
        x[0] = mx / gm[0];
        v[0] = mv / gm[0];
        var live = new List<int>();
        for (int i = 0; i < m; i++)
        {
            if (!merged[i]) live.Add(i);
        }

        double horizon = 0.0;
        for (int t = first; t <= last; t++)
        {
            Tracked tr = tracked[t];
            tr.Last = Relative(tr, x, gm, merged);
            tr.Points.Add(tr.Last);
            horizon = Math.Max(horizon, tr.Period * LapAllowance);
        }
        int steps = (int)Math.Ceiling(horizon / dt);
        double sample = 2.0 * Math.PI / SamplesPerLap;
        var a = new Double3[m];

        // Yoshida's 4th-order composition of leapfrog steps.
        double w1 = 1.0 / (2.0 - Math.Pow(2.0, 1.0 / 3.0)), w0 = 1.0 - 2.0 * w1;
        double[] drift = { w1 / 2.0, (w0 + w1) / 2.0, (w0 + w1) / 2.0, w1 / 2.0 };
        double[] kick = { w1, w0, w1 };
        int open = last - first + 1;
        for (int s = 1; s <= steps && open > 0; s++)
        {
            for (int stage = 0; stage < 4; stage++)
            {
                double d = drift[stage] * dt;
                foreach (int i in live) x[i] += v[i] * d;
                if (stage == 3) break;
                Accelerations(live, x, gm, a);
                double kdt = kick[stage] * dt;
                foreach (int i in live) v[i] += a[i] * kdt;
            }
            for (int t = first; t <= last; t++)
            {
                Tracked tr = tracked[t];
                if (tr.Done) continue;
                Double3 r = Relative(tr, x, gm, merged);
                double turn = Math.Atan2(Double3.Cross(tr.Last, r).Length, Double3.Dot(tr.Last, r));
                tr.Angle += turn;
                tr.SinceSample += turn;
                tr.Last = r;
                tr.Seconds = s * dt;
                bool lap = tr.Angle >= 2.0 * Math.PI;
                if (tr.SinceSample >= sample || lap)
                {
                    tr.Points.Add(r);
                    tr.SinceSample = 0.0;
                }
                if (lap || s * dt >= tr.Period * LapAllowance)
                {
                    if (!lap) tr.Points.Add(r);
                    tr.Done = true;
                    tr.FullLap = lap && tr.Bound;
                    open--;
                }
            }
        }
        for (int t = first; t <= last; t++) tracked[t].Done = true;
    }

    private static void Accelerations(List<int> live, Double3[] x, double[] gm, Double3[] a)
    {
        foreach (int i in live) a[i] = Double3.Zero;
        for (int p = 0; p < live.Count; p++)
        {
            int i = live[p];
            for (int q = p + 1; q < live.Count; q++)
            {
                int j = live[q];
                Double3 d = x[j] - x[i];
                double r2 = d.LengthSquared;
                if (r2 <= 0.0) continue;
                double inv = 1.0 / (r2 * Math.Sqrt(r2));
                a[i] += d * (gm[j] * inv);
                a[j] -= d * (gm[i] * inv);
            }
        }
    }
}
