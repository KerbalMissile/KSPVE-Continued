using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Pipeline;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Pipeline;

[TestFixture]
public sealed class BudgetedSchedulerTests
{
    [Test]
    public void BudgetIsRespected()
    {
        var s = new BudgetedScheduler(3.0);
        var costs = new Dictionary<string, double> { ["a"] = 1, ["b"] = 1.5, ["c"] = 1, ["d"] = 0.5 };
        int p = 1;
        foreach (var kv in costs) s.Register(kv.Key, kv.Value, p++);
        for (int f = 0; f < 50; f++)
        {
            var run = s.NextFrame();
            Assert.That(run.Sum(id => costs[id]), Is.LessThanOrEqualTo(3.0 + 1e-9));
            Assert.That(run.Count, Is.GreaterThan(0));
        }
    }

    [Test]
    public void LowPriorityJobIsNeverStarved()
    {
        var s = new BudgetedScheduler(1.0);
        s.Register("low", 1, 1);
        s.Register("hi1", 1, 4);
        s.Register("hi2", 1, 3);
        int bound = (4 + 3) / 1 + 3; // sum of other priorities / own priority + job count
        int? firstRun = null;
        for (int f = 1; f <= bound && firstRun == null; f++)
            if (s.NextFrame().Contains("low")) firstRun = f;
        Assert.That(firstRun, Is.Not.Null);
    }

    [Test]
    public void EveryJobRunsRepeatedlyOverLongHorizon()
    {
        var s = new BudgetedScheduler(1.0);
        s.Register("a", 1, 1); s.Register("b", 1, 2); s.Register("c", 1, 5);
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["c"] = 0 };
        for (int f = 0; f < 800; f++) foreach (var id in s.NextFrame()) counts[id]++;
        Assert.That(counts["a"], Is.GreaterThan(50));
        Assert.That(counts["c"], Is.GreaterThan(counts["a"]));
    }

    [Test]
    public void OversizedJobStillRunsAlone()
    {
        var s = new BudgetedScheduler(1.0);
        s.Register("big", 10, 1);
        var run = s.NextFrame();
        Assert.That(run, Is.EqualTo(new[] { "big" }));
    }

    [Test]
    public void OversizedJobWithHighestCreditRunsAlone()
    {
        var s = new BudgetedScheduler(2.0);
        s.Register("big", 10, 5);
        s.Register("small", 1, 1);
        Assert.That(s.NextFrame(), Is.EqualTo(new[] { "big" }));
    }

    [Test]
    public void DeterministicAcrossInstances()
    {
        static List<string> Trace()
        {
            var s = new BudgetedScheduler(2.0);
            s.Register("x", 1, 1); s.Register("y", 1, 2); s.Register("z", 1, 2); s.Register("w", 2, 3);
            var t = new List<string>();
            for (int i = 0; i < 40; i++) t.Add(string.Join("+", s.NextFrame()));
            return t;
        }
        Assert.That(Trace(), Is.EqualTo(Trace()));
    }

    [Test]
    public void RegisterValidationAndUnregister()
    {
        var s = new BudgetedScheduler(1.0);
        Assert.That(s.Register("a", 1, 0), Is.False);
        Assert.That(s.Register("a", -1, 1), Is.False);
        Assert.That(s.Register("a", 1, 1), Is.True);
        Assert.That(s.Register("a", 1, 1), Is.False);
        Assert.That(s.Count, Is.EqualTo(1));
        Assert.That(s.Unregister("a"), Is.True);
        Assert.That(s.Unregister("a"), Is.False);
        Assert.That(s.NextFrame(), Is.Empty);
    }
}
