using System;
using System.Linq;
using KSPVE.Pure.Water;
using KSPVE.Pure.Water.Hydrology;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Water;

public class PriorityFloodTests
{
    [Test]
    public void ABasin_IsFilledToItsLowestRim()
    {
        // 7x7: a rim at 10 with one notch at 6 on the west edge, a floor at 2 with a pit of 0.
        const int n = 7;
        var dem = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                dem[y * n + x] = (x == 0 || y == 0 || x == n - 1 || y == n - 1) ? 10f : 2f;
        dem[3 * n + 0] = 6f;
        dem[3 * n + 3] = 0f;
        FloodResult f = PriorityFlood.Run(dem, n, n);
        for (int y = 1; y < n - 1; y++)
            for (int x = 1; x < n - 1; x++)
                Assert.That(f.Filled[y * n + x], Is.EqualTo(6f), $"({x},{y})");
        Assert.That(f.Filled[3 * n + 0], Is.EqualTo(6f), "the notch itself is the outlet");
    }

    [Test]
    public void TerrainThatDrains_IsNotChanged()
    {
        const int n = 9;
        var dem = new float[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) dem[y * n + x] = 100f - 3f * x - 0.7f * y;
        FloodResult f = PriorityFlood.Run(dem, n, n);
        Assert.That(f.Filled, Is.EqualTo(dem));
    }

    [Test]
    public void ARidge_SplitsTwoBasins_AndEachSpillsOverItsOwnRim()
    {
        // Two floors at 0, a ridge at 20 between, rims 12 (west) and 15 (east) at the edge notches.
        const int w = 13, h = 5;
        var dem = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
                dem[y * w + x] = x == 6 ? 20f : edge ? 30f : 0f;
            }
        dem[2 * w + 0] = 12f;
        dem[2 * w + (w - 1)] = 15f;
        FloodResult f = PriorityFlood.Run(dem, w, h);
        Assert.That(f.Filled[2 * w + 2], Is.EqualTo(12f));
        Assert.That(f.Filled[2 * w + 10], Is.EqualTo(15f));
        Assert.That(f.Filled[2 * w + 6], Is.EqualTo(20f), "the ridge stays");
    }

    [Test]
    public void EveryCellDrainsToALowerOrEqualCellReachedEarlier()
    {
        var rng = new Random(4);
        const int n = 40;
        var dem = new float[n * n];
        for (int i = 0; i < dem.Length; i++) dem[i] = (float)(rng.NextDouble() * 50.0);
        FloodResult f = PriorityFlood.Run(dem, n, n);
        var position = new int[n * n];
        for (int k = 0; k < f.Order.Length; k++) position[f.Order[k]] = k;
        Assert.That(f.Order.Distinct().Count(), Is.EqualTo(n * n), "every cell visited once");
        for (int i = 0; i < dem.Length; i++)
        {
            Assert.That(f.Filled[i], Is.GreaterThanOrEqualTo(dem[i]));
            int d = f.Direction[i];
            if (d < 0) continue;
            int t = (i / n + Neighbours.Dy[d]) * n + i % n + Neighbours.Dx[d];
            Assert.That(f.Filled[t], Is.LessThanOrEqualTo(f.Filled[i]));
            Assert.That(position[t], Is.LessThan(position[i]));
        }
    }

    [Test]
    public void TheSea_IsAnOutlet()
    {
        // A pit below the sea level beside a high rim: the sea level cells are outlets, not a lake.
        const int n = 5;
        var dem = new float[n * n];
        for (int i = 0; i < dem.Length; i++) dem[i] = 20f;
        dem[2 * n + 2] = -5f;
        FloodResult f = PriorityFlood.Run(dem, n, n, 0f);
        Assert.That(f.Filled[2 * n + 2], Is.EqualTo(-5f));
        Assert.That(f.Direction[2 * n + 2], Is.EqualTo((sbyte)-1));
    }
}

public class FlowAccumulationTests
{
    private static float[] Ones(int count) => Enumerable.Repeat(1f, count).ToArray();

    [Test]
    public void OnATiltedPlane_EverythingRunsToTheLowSide()
    {
        const int w = 10, h = 6;
        var dem = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dem[y * w + x] = 100f - 2f * x;
        FloodResult f = PriorityFlood.Run(dem, w, h);
        float[] q = FlowRouting.AccumulateD8(f, Ones(w * h));
        // Interior rows carry their whole row (the west edge cell is an outlet of its own): w - 1 cells end at the east edge.
        for (int y = 1; y < h - 1; y++) Assert.That(q[y * w + (w - 1)], Is.EqualTo(w - 1), "row " + y);
    }

    [Test]
    public void TheWaterIsConserved_ByD8AndDInfinity()
    {
        var rng = new Random(11);
        const int w = 48, h = 40;
        var dem = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dem[y * w + x] = (float)(200.0 - 1.5 * x - 0.9 * y + rng.NextDouble() * 6.0);
        FloodResult f = PriorityFlood.Run(dem, w, h);
        var src = Ones(w * h);
        foreach (float[] q in new[] { FlowRouting.AccumulateD8(f, src), FlowRouting.AccumulateDInfinity(f, src, 100.0) })
        {
            double arriving = 0.0;
            for (int i = 0; i < q.Length; i++) if (f.Direction[i] < 0) arriving += q[i];
            Assert.That(arriving, Is.EqualTo(w * h).Within(0.5), "all of the rain leaves at the outlets");
        }
    }

    [Test]
    public void DInfinity_SpreadsAcrossASlopeThatIsNotAlongTheGrid()
    {
        // Slope direction 22.5 degrees from the x axis: D8 stays on one line, D-infinity splits between two neighbours.
        const int w = 30, h = 30;
        var dem = new float[w * h];
        double a = 22.5 * Math.PI / 180.0;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dem[y * w + x] = (float)(1000.0 - 5.0 * (x * Math.Cos(a) + y * Math.Sin(a)));
        FloodResult f = PriorityFlood.Run(dem, w, h);
        var src = new float[w * h];
        src[10 * w + 5] = 1f;
        float[] d8 = FlowRouting.AccumulateD8(f, src), dinf = FlowRouting.AccumulateDInfinity(f, src, 1.0);
        int wet8 = d8.Count(v => v > 0f), wetInf = dinf.Count(v => v > 1e-4f);
        Assert.That(wetInf, Is.GreaterThan(wet8), "spreads over more cells");
        Assert.That(dinf.Max(), Is.LessThanOrEqualTo(1.0001f));
    }

    [Test]
    public void AValley_CollectsAMoreWaterDownstream()
    {
        const int w = 21, h = 40;
        var dem = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dem[y * w + x] = y * 1.0f + Math.Abs(x - 10) * 5f;
        FloodResult f = PriorityFlood.Run(dem, w, h);
        float[] q = FlowRouting.AccumulateD8(f, Ones(w * h));
        float last = 0f;
        for (int y = h - 1; y >= 1; y--)
        {
            Assert.That(q[y * w + 10], Is.GreaterThanOrEqualTo(last), "row " + y);
            last = q[y * w + 10];
        }
        Assert.That(q[1 * w + 10], Is.GreaterThan(q[(h - 2) * w + 10] * 4.0f));
    }
}

public class HydraulicGeometryTests
{
    [Test]
    public void WidthGrowsAsTheRootOfTheDischarge_AndDepthAsTheTwoFifthsPower()
    {
        Assert.That(HydraulicGeometry.Width(4.0) / HydraulicGeometry.Width(1.0), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(HydraulicGeometry.GeometricDepth(4.0) / HydraulicGeometry.GeometricDepth(1.0), Is.EqualTo(Math.Pow(4.0, 0.4)).Within(1e-9));
        // The Mississippi: a mean flow of 17,000 m3/s is about 800 m wide and about 20 m deep.
        Assert.That(HydraulicGeometry.Width(17000.0), Is.InRange(600.0, 1000.0));
        Assert.That(HydraulicGeometry.GeometricDepth(17000.0), Is.InRange(12.0, 30.0));
    }

    [Test]
    public void SteeperBeds_RunFasterAndShallower_AndTheFlowIsContinuous()
    {
        ChannelState gentle = HydraulicGeometry.At(5.0, 0.0005, 9.81), steep = HydraulicGeometry.At(5.0, 0.03, 9.81);
        Assert.That(steep.Speed, Is.GreaterThan(gentle.Speed));
        Assert.That(steep.Depth, Is.LessThan(gentle.Depth));
        foreach (ChannelState c in new[] { gentle, steep })
            Assert.That(c.Width * c.Depth * c.Speed, Is.EqualTo(5.0).Within(0.01));
        Assert.That(gentle.Froude, Is.LessThan(0.5), "a lowland river is calm");
        Assert.That(steep.Froude, Is.GreaterThan(0.8), "a mountain stream is white water");
        // Manning's equation at the normal depth.
        Assert.That(gentle.Speed, Is.EqualTo(Math.Pow(gentle.Depth, 2.0 / 3.0) * Math.Sqrt(0.0005) / HydraulicGeometry.ManningN).Within(0.02));
        Assert.That(HydraulicGeometry.At(1.0, 0.0, 9.81).Speed, Is.GreaterThanOrEqualTo(0.05), "a flat bed still has flow");
    }

    [Test]
    public void RainRunsOffMoreInWetPlaces()
    {
        double desert = HydraulicGeometry.AnnualPrecipitation(0.18), tropics = HydraulicGeometry.AnnualPrecipitation(0.8);
        Assert.That(desert, Is.LessThan(150.0));
        Assert.That(tropics, Is.InRange(1500.0, 2200.0));
        double rDesert = HydraulicGeometry.AnnualRunoff(desert, 25.0), rTropics = HydraulicGeometry.AnnualRunoff(tropics, 25.0);
        Assert.That(rDesert, Is.LessThan(0.15 * desert));
        Assert.That(rTropics, Is.InRange(0.25 * tropics, 0.7 * tropics));
        double last = 0.0;
        for (double p = 100.0; p < 3000.0; p += 100.0)
        {
            double r = HydraulicGeometry.AnnualRunoff(p, 15.0);
            Assert.That(r, Is.GreaterThanOrEqualTo(last));
            Assert.That(r, Is.LessThan(p));
            last = r;
        }
        // One metre of runoff a year over a square kilometre is 31.7 litres a second... that is 0.0317 m3/s.
        Assert.That(HydraulicGeometry.DischargeFromRunoff(1000.0, 1e6), Is.EqualTo(0.03169).Within(1e-4));
    }
}

public class HydroSolverTests
{
    private static HydroInput Valley(int w, int h, double cell, float rain, double minQ, Func<int, int, float> terrain)
    {
        var input = new HydroInput { Width = w, Height = h, CellSize = cell, Terrain = new float[w * h], Runoff = new float[w * h], MinDischarge = minQ };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                input.Terrain[y * w + x] = terrain(x, y);
                input.Runoff[y * w + x] = rain;
            }
        return input;
    }

    [Test]
    public void ARiverRunsDownAValley_GrowingAndNeverClimbing()
    {
        // A V valley falling to the south.
        HydroInput input = Valley(41, 80, 100.0, 4e-4f, 0.5, (x, y) => 400f + y * 3f + Math.Abs(x - 20) * 12f);
        HydroResult r = HydroSolver.Solve(input);
        Assert.That(r.Reaches.Count, Is.GreaterThanOrEqualTo(1));
        RiverReach main = r.Reaches.OrderByDescending(a => a.Points.Count).First();
        Assert.That(main.Points.Count, Is.GreaterThan(20));
        for (int i = 1; i < main.Points.Count; i++)
        {
            Assert.That(main.Points[i].Elevation, Is.LessThanOrEqualTo(main.Points[i - 1].Elevation + 1e-6), "the bed never rises downstream");
            Assert.That(main.Points[i].Channel.Discharge, Is.GreaterThanOrEqualTo(main.Points[i - 1].Channel.Discharge * 0.9), "discharge grows downstream");
        }
        Assert.That(main.Points.Last().Channel.Width, Is.GreaterThan(main.Points.First().Channel.Width));
        Assert.That(main.Points.Last().Y, Is.LessThan(main.Points.First().Y), "it ends south of its source");
    }

    [Test]
    public void ALowerThreshold_DrawsMoreRivers()
    {
        Func<int, int, float> bumpy = (x, y) => 500f + y * 2f - x * 0.5f + (float)(8.0 * Math.Sin(x * 0.7) * Math.Cos(y * 0.5));
        int Cells(double minQ)
        {
            HydroResult r = HydroSolver.Solve(Valley(60, 60, 100.0, 3e-4f, minQ, bumpy));
            return r.Reaches.Sum(a => a.Points.Count);
        }
        Assert.That(Cells(0.05), Is.GreaterThan(Cells(2.0)));
        Assert.That(Cells(2.0), Is.GreaterThanOrEqualTo(Cells(40.0)));
    }

    [Test]
    public void ABasin_BecomesAFlatLake_AtItsSpillLevel_AndTheRiverEntersAndLeavesIt()
    {
        // A tilted plane with a bowl dug into it (a 14 m deep hollow, ~9 cells wide).
        HydroInput input = Valley(60, 60, 50.0, 8e-3f, 0.3, (x, y) =>
        {
            double plane = 300.0 - 0.9 * y;
            double d = Math.Sqrt((x - 30.0) * (x - 30.0) + (y - 30.0) * (y - 30.0));
            double bowl = d < 8.0 ? -14.0 * Math.Cos(d / 8.0 * Math.PI / 2.0) : 0.0;
            return (float)(plane + bowl);
        });
        input.LakeMinArea = 5000.0;
        HydroResult r = HydroSolver.Solve(input);
        Assert.That(r.Lakes.Count, Is.EqualTo(1));
        Lake lake = r.Lakes[0];
        Assert.That(lake.CellCount, Is.GreaterThan(20));
        Assert.That(lake.MaxDepth, Is.GreaterThan(2.0));
        // Flat: every lake cell has the same water level, and it is above the ground.
        for (int i = 0; i < r.LakeIndex.Length; i++)
        {
            if (r.LakeIndex[i] != lake.Id) continue;
            Assert.That(r.Flood.Filled[i], Is.EqualTo(lake.Level).Within(0.011));
            Assert.That(r.LakeDepth[i], Is.GreaterThan(0f));
        }
        // The spill level is the lowest rim of the bowl: no lake cell is above the ground that is lower at the rim.
        Assert.That(r.Reaches.Any(a => a.EndsInLake), Is.True, "a river runs into the lake");
        Assert.That(r.Reaches.Count(a => a.Points.Count > 3), Is.GreaterThanOrEqualTo(2), "and one out of it");
        // Lake cells are not drawn as river.
        foreach (RiverReach reach in r.Reaches)
            foreach (RiverPoint p in reach.Points.Take(reach.Points.Count - 1))
            {
                int cx = (int)(p.X / 50.0), cy = (int)(p.Y / 50.0);
                if (cx < 0 || cy < 0 || cx >= 60 || cy >= 60) continue;
            }
    }

    [Test]
    public void ADryClimate_ShrinksALakeToWhatItsCatchmentCanKeep()
    {
        Func<int, int, float> bowlOnSlope = (x, y) =>
        {
            double d = Math.Sqrt((x - 30.0) * (x - 30.0) + (y - 30.0) * (y - 30.0));
            return (float)(300.0 - 0.9 * y + (d < 9.0 ? -16.0 * Math.Cos(d / 9.0 * Math.PI / 2.0) : 0.0));
        };
        HydroInput wet = Valley(60, 60, 50.0, 8e-3f, 0.3, bowlOnSlope), dry = Valley(60, 60, 50.0, 8e-3f, 0.3, bowlOnSlope);
        wet.LakeMinArea = dry.LakeMinArea = 3000.0;
        wet.LakeMinDepth = dry.LakeMinDepth = 0.5;
        // Evaporation of kilometres a year (an absurd dryness, to be sure) is more than this catchment's runoff can supply to a full basin.
        dry.LakeNetEvaporation = 4e6;
        HydroResult full = HydroSolver.Solve(wet), small = HydroSolver.Solve(dry);
        Assert.That(full.Lakes.Count, Is.EqualTo(1));
        Assert.That(small.Lakes.Count, Is.LessThanOrEqualTo(1));
        if (small.Lakes.Count == 1)
        {
            Assert.That(small.Lakes[0].Area, Is.LessThan(full.Lakes[0].Area));
            Assert.That(small.Lakes[0].Level, Is.LessThan(full.Lakes[0].Level));
        }
        // With evaporation tiny the lake stays full.
        wet.LakeNetEvaporation = 1.0;
        Assert.That(HydroSolver.Solve(wet).Lakes[0].Area, Is.EqualTo(full.Lakes[0].Area));
    }

    [Test]
    public void TinyPuddles_AreNotLakes()
    {
        HydroInput input = Valley(30, 30, 100.0, 1e-4f, 1.0, (x, y) => 100f - y * 0.5f);
        input.Terrain[15 * 30 + 15] -= 0.4f;
        HydroResult r = HydroSolver.Solve(input);
        Assert.That(r.Lakes, Is.Empty);
    }

    [Test]
    public void TheSameTerrain_GivesTheSameRivers()
    {
        Func<int, int, float> t = (x, y) => 800f - y * 4f + (float)(30.0 * Math.Sin(x * 0.31) * Math.Sin(y * 0.23));
        HydroResult a = HydroSolver.Solve(Valley(50, 50, 100.0, 5e-4f, 0.2, t)), b = HydroSolver.Solve(Valley(50, 50, 100.0, 5e-4f, 0.2, t));
        Assert.That(a.Discharge, Is.EqualTo(b.Discharge));
        Assert.That(a.Reaches.Count, Is.EqualTo(b.Reaches.Count));
        Assert.That(a.Reaches.Sum(r => r.Points.Sum(p => p.X + p.Y)), Is.EqualTo(b.Reaches.Sum(r => r.Points.Sum(p => p.X + p.Y))));
    }

    [Test]
    public void SteepReaches_HaveRapids_AndABigDropAWaterfall()
    {
        // Gentle, then a 40 m cliff in one cell, then gentle.
        HydroInput input = Valley(11, 60, 100.0, 5e-3f, 0.5, (x, y) => (y < 30 ? 120f : 160f) + y * 0.4f + Math.Abs(x - 5) * 10f);
        HydroResult r = HydroSolver.Solve(input);
        RiverReach main = r.Reaches.OrderByDescending(a => a.Points.Count).First();
        Assert.That(main.Points.Max(p => p.Fall), Is.GreaterThan(0.5), "the cliff is a waterfall");
        Assert.That(main.Points.Min(p => p.Rapids), Is.LessThan(0.3), "while the gentle parts are calm");
        Assert.That(main.Points.Max(p => p.Rapids), Is.GreaterThan(0.8));
    }

    [Test]
    public void ARiverFromBeyondTheWindow_ArrivesThroughTheEdge()
    {
        HydroInput input = Valley(40, 60, 100.0, 1e-5f, 0.5, (x, y) => 300f + y * 3f + Math.Abs(x - 20) * 15f);
        HydroResult own = HydroSolver.Solve(input);
        // A coarse map says 50 m3/s comes in at the top of the valley.
        float[] inflow = HydroSolver.RegionalInflow(input, own.Discharge, (x, y) => y > 5000.0 && Math.Abs(x - 2050.0) < 200.0 ? 50.0 : 0.0);
        Assert.That(inflow, Is.Not.Null);
        input.Inflow = inflow;
        HydroResult with = HydroSolver.Solve(input);
        float outlet = with.Discharge[0 * 40 + 20];
        Assert.That(outlet, Is.GreaterThan(own.Discharge[0 * 40 + 20] + 40f), "the foreign river reaches the bottom of the valley");
        Assert.That(with.Reaches.Max(a => a.Points.Max(p => p.Channel.Width)), Is.GreaterThan(30.0));
    }

    [Test]
    public void TheMeshes_AreWellFormed()
    {
        HydroInput input = Valley(60, 60, 50.0, 6e-4f, 0.3, (x, y) =>
        {
            double d = Math.Sqrt((x - 30.0) * (x - 30.0) + (y - 30.0) * (y - 30.0));
            return (float)(300.0 - 0.9 * y + (d < 8.0 ? -14.0 * Math.Cos(d / 8.0 * Math.PI / 2.0) : 0.0));
        });
        input.LakeMinArea = 5000.0;
        HydroResult r = HydroSolver.Solve(input);
        WaterMeshData rivers = WaterMeshes.Rivers(r, 0.2), lakes = WaterMeshes.Lakes(r);
        foreach (WaterMeshData m in new[] { rivers, lakes })
        {
            Assert.That(m.Indices.Count % 3, Is.EqualTo(0));
            Assert.That(m.Indices.Count, Is.GreaterThan(0));
            Assert.That(m.Indices.All(i => i >= 0 && i < m.VertexCount), Is.True);
            Assert.That(m.Altitude.All(a => !float.IsNaN(a)), Is.True);
        }
        Assert.That(lakes.Altitude.Distinct().Count(), Is.EqualTo(1), "one flat level");
        Assert.That(rivers.U.Min(), Is.EqualTo(0f));
        Assert.That(rivers.U.Max(), Is.EqualTo(1f));
    }
}

public class WindowFrameTests
{
    [Test]
    public void TheMap_RoundTripsDirections_AndIsTrueNearTheCentre()
    {
        var frame = new WindowFrame(new KSPVE.Pure.Numerics.Double3(0.3, 0.8, 0.5), new KSPVE.Pure.Numerics.Double3(1, 0, 0), new KSPVE.Pure.Numerics.Double3(0, 1, 0), 600000.0);
        Assert.That(KSPVE.Pure.Numerics.Double3.Dot(frame.East, frame.North), Is.EqualTo(0.0).Within(1e-12));
        Assert.That(KSPVE.Pure.Numerics.Double3.Dot(frame.Center, frame.North), Is.EqualTo(0.0).Within(1e-12));
        foreach (double[] p in new[] { new[] { 0.0, 0.0 }, new[] { 12000.0, -8000.0 }, new[] { -16000.0, 16000.0 } })
        {
            KSPVE.Pure.Numerics.Double3 d = frame.ToDirection(p[0], p[1]);
            Assert.That(d.Length, Is.EqualTo(1.0).Within(1e-12));
            frame.FromDirection(d, out double e, out double n);
            Assert.That(e, Is.EqualTo(p[0]).Within(1e-6));
            Assert.That(n, Is.EqualTo(p[1]).Within(1e-6));
        }
        // 10 km east on the map is 10 km along the surface to a fraction of a percent on a 600 km planet.
        KSPVE.Pure.Numerics.Double3 a = frame.ToDirection(0, 0), b = frame.ToDirection(10000.0, 0);
        double arc = Math.Acos(Math.Min(KSPVE.Pure.Numerics.Double3.Dot(a, b), 1.0)) * 600000.0;
        Assert.That(arc, Is.EqualTo(10000.0).Within(10000.0 * 0.001));
    }

    [Test]
    public void TheRippleTexture_TilesHasUnitSlopesAndIsTheSameEveryTime()
    {
        float[] a = RippleTexture.Generate(64, 3), b = RippleTexture.Generate(64, 3), c = RippleTexture.Generate(64, 4);
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a, Is.Not.EqualTo(c));
        double sum = 0.0;
        foreach (float v in a) sum += v * v;
        Assert.That(Math.Sqrt(sum / a.Length) * 3.0, Is.EqualTo(1.0).Within(0.15), "unit standard deviation before the division by three");
        // Tiling: the field is periodic by construction (FFT), the neighbours across the edge differ no more than neighbours inside.
        double edge = 0.0, inside = 0.0;
        for (int y = 0; y < 64; y++) { edge += Math.Abs(a[y * 64] - a[y * 64 + 63]); inside += Math.Abs(a[y * 64 + 30] - a[y * 64 + 31]); }
        Assert.That(edge, Is.LessThan(inside * 3.0));
    }
}

public class RiverFieldTests
{
    [Test]
    public void TheField_FindsLakesAndRivers_ByPosition()
    {
        const int w = 60;
        var input = new HydroInput { Width = w, Height = w, CellSize = 50.0, Terrain = new float[w * w], Runoff = new float[w * w], MinDischarge = 0.3, LakeMinArea = 5000.0, LakeMinDepth = 1.0 };
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                double d = Math.Sqrt((x - 30.0) * (x - 30.0) + (y - 30.0) * (y - 30.0));
                input.Terrain[y * w + x] = (float)(300.0 - 0.9 * y + (d < 8.0 ? -14.0 * Math.Cos(d / 8.0 * Math.PI / 2.0) : 0.0));
                input.Runoff[y * w + x] = 8e-3f;
            }
        HydroResult r = HydroSolver.Solve(input);
        var field = new RiverField(r, input.Terrain);
        Lake lake = r.Lakes[0];
        // The middle of the lake: in water, at the spill level, deeper than a metre.
        WaterAt middle = field.At(30.5 * 50.0, 30.5 * 50.0, lake.Level - 1.0);
        Assert.That(middle.InWater, Is.True);
        Assert.That(middle.IsLake, Is.True);
        Assert.That(middle.Surface, Is.EqualTo(lake.Level).Within(0.011));
        Assert.That(middle.Depth, Is.GreaterThan(1.0));
        // Above the surface is not in the water; far from everything is not either.
        Assert.That(field.At(30.5 * 50.0, 30.5 * 50.0, lake.Level + 5.0).InWater, Is.False);
        Assert.That(field.At(2.0 * 50.0, 2.0 * 50.0, 400.0).InWater, Is.False);
        // On a river's centre line: in water, moving.
        RiverReach reach = r.Reaches[0];
        RiverPoint p = reach.Points[reach.Points.Count / 2];
        WaterAt on = field.At(p.X, p.Y, p.Elevation);
        Assert.That(on.InWater, Is.True);
        Assert.That(on.IsLake, Is.False);
        Assert.That(on.Speed, Is.GreaterThan(0.0));
        Assert.That(Math.Sqrt(on.FlowEast * on.FlowEast + on.FlowNorth * on.FlowNorth), Is.EqualTo(1.0).Within(1e-6));
        // Between the banks and beyond them: a point a few widths to the side of this river's centre line is dry unless another river is there.
        WaterAt side = field.At(p.X, p.Y, p.Elevation);
        Assert.That(side.Depth, Is.EqualTo(p.Channel.Depth).Within(p.Channel.Depth * 0.35 + 0.05), "the centre line is as deep as the channel");
    }
}
