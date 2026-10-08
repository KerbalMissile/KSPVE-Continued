using KSPVE.Pure.Pipeline;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Pipeline;

[TestFixture]
public sealed class FrameBudgetGovernorTests
{
    private static FrameBudgetGovernor Make(int dwell = 5)
    {
        var g = new FrameBudgetGovernor(10.0, new[]
        {
            new GovernorKnob("res", 3, 0, 3, 0),
            new GovernorKnob("fx", 2, 0, 2, 1),
            new GovernorKnob("aa", 1, 0, 1, 1),
        });
        g.SmoothingAlpha = 1.0; // no smoothing for simple tests
        g.MinDwellFrames = dwell;
        return g;
    }

    [Test]
    public void DefaultsMatchSpec()
    {
        var g = new FrameBudgetGovernor(10, new GovernorKnob[0]);
        Assert.That(g.SmoothingAlpha, Is.EqualTo(0.1));
        Assert.That(g.HighMargin, Is.EqualTo(0.05));
        Assert.That(g.LowMargin, Is.EqualTo(0.15));
        Assert.That(g.MinDwellFrames, Is.EqualTo(60));
    }

    [Test]
    public void SustainedOverloadLowersOnlyAfterDwell()
    {
        var g = Make(5);
        for (int i = 0; i < 4; i++) Assert.That(g.Update(20).Action, Is.EqualTo(GovernorAction.Hold));
        var d = g.Update(20);
        Assert.That(d.Action, Is.EqualTo(GovernorAction.Lowered));
        Assert.That(d.KnobId, Is.EqualTo("res"));
        Assert.That(d.Level, Is.EqualTo(2));
    }

    [Test]
    public void NoiseInsideDeadbandNeverChangesAnything()
    {
        var g = Make(1);
        for (int i = 0; i < 500; i++)
        {
            var d = g.Update(i % 2 == 0 ? 10.4 : 8.6);
            Assert.That(d.Action, Is.EqualTo(GovernorAction.Hold));
        }
        Assert.That(g.Knobs[0].Level, Is.EqualTo(3));
    }

    [Test]
    public void LowersLowestPriorityThenById()
    {
        var g = Make(1);
        Assert.That(g.Update(20).KnobId, Is.EqualTo("res"));
        Assert.That(g.Update(20).KnobId, Is.EqualTo("res"));
        Assert.That(g.Update(20).KnobId, Is.EqualTo("res"));
        Assert.That(g.Update(20).KnobId, Is.EqualTo("aa"));   // res at floor; aa < fx by Id
        Assert.That(g.Update(20).KnobId, Is.EqualTo("fx"));   // aa at floor
    }

    [Test]
    public void RecoveryRaisesInLifoOrder()
    {
        var g = Make(1);
        g.Update(20); g.Update(20); g.Update(20); // res 3->0
        g.Update(20);                             // aa 1->0
        var r1 = g.Update(1);
        Assert.That(r1.Action, Is.EqualTo(GovernorAction.Raised));
        Assert.That(r1.KnobId, Is.EqualTo("aa"));
        var r2 = g.Update(1);
        Assert.That(r2.KnobId, Is.EqualTo("res"));
        Assert.That(r2.Level, Is.EqualTo(1));
        Assert.That(g.Update(1).KnobId, Is.EqualTo("res"));
        Assert.That(g.Update(1).KnobId, Is.EqualTo("res"));
        var end = g.Update(1);
        Assert.That(end.Action, Is.EqualTo(GovernorAction.AtCeiling));
    }

    [Test]
    public void FloorReportedWhenNothingCanLower()
    {
        var g = new FrameBudgetGovernor(10, new[] { new GovernorKnob("a", 0, 0, 2, 0) }) { SmoothingAlpha = 1, MinDwellFrames = 1 };
        Assert.That(g.Update(50).Action, Is.EqualTo(GovernorAction.AtFloor));
        Assert.That(g.Knobs[0].Level, Is.EqualTo(0));
    }

    [Test]
    public void DwellRestartsAfterChange()
    {
        var g = Make(3);
        for (int i = 0; i < 3; i++) g.Update(20);
        Assert.That(g.Update(20).Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(g.Update(20).Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(g.Update(20).Action, Is.EqualTo(GovernorAction.Lowered));
    }

    [Test]
    public void InvalidInputIgnored()
    {
        var g = Make(1);
        Assert.That(g.Update(double.NaN).Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(g.Update(-5).Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(g.Update(double.PositiveInfinity).Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(g.SmoothedMs, Is.EqualTo(0));
        Assert.That(g.Knobs[0].Level, Is.EqualTo(3));
    }

    [Test]
    public void SmoothingDelaysReaction()
    {
        var g = new FrameBudgetGovernor(10, new[] { new GovernorKnob("a", 3, 0, 3, 0) }) { MinDwellFrames = 1 };
        g.Update(10);
        var d = g.Update(14); // 10 + 0.1*(14-10) = 10.4, inside the deadband
        Assert.That(d.Action, Is.EqualTo(GovernorAction.Hold));
        Assert.That(d.SmoothedMs, Is.EqualTo(10.4).Within(1e-9));
    }
}
