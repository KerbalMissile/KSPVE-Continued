using System.Collections.Generic;
using KSPVE.Pure.Config;
using KSPVE.Pure.Space.Tde;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Tde;

public class TdeSaveStateTests
{
    [TestCase("on", true)]
    [TestCase(" ON ", true)]
    [TestCase("true", true)]
    [TestCase("yes", true)]
    [TestCase("1", true)]
    [TestCase("off", false)]
    [TestCase("false", false)]
    [TestCase("maybe", false)]
    [TestCase("", false)]
    public void Switch(string raw, bool on) => Assert.That(TdeSaveState.ParseSwitch(raw), Is.EqualTo(on));

    [Test]
    public void ParsesTheSwitchStartBodyAndOverrides()
    {
        var node = new CfgNode("KSPVE_SAVE")
            .Add("tde", "on")
            .Add("tdeStart", "123456.5")
            .Add("tdeBody", " Kerr ")
            .Add("timeScale", "0.05")
            .Add("somethingElse", "kept");
        TdeSaveState state = TdeSaveState.Parse(node);
        Assert.That(state.On, Is.True);
        Assert.That(state.StartUt, Is.EqualTo(123456.5));
        Assert.That(state.Body, Is.EqualTo("Kerr"));
        Assert.That(state.Overrides.GetLast("timeScale"), Is.EqualTo("0.05"));
        Assert.That(state.Other, Has.Count.EqualTo(1));

        var issues = new List<ConfigIssue>();
        TdeParameters p = TdeParameters.From(TdeSettings.Schema.Resolve(state.Overrides, issues));
        Assert.That(p.TimeScale, Is.EqualTo(0.05));
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void MissingNode_IsOff()
    {
        TdeSaveState state = TdeSaveState.Parse(null);
        Assert.That(state.On, Is.False);
        Assert.That(state.StartUt, Is.Null);
    }

    [Test]
    public void Write_KeepsEverythingAndExplainsTheSwitch()
    {
        var state = new TdeSaveState { On = true, StartUt = 98765.25, Body = "Kcalbeloh" };
        state.Overrides.SetValue("beta", "3");
        state.Other.Add(new KeyValuePair<string, string>("future", "1"));
        string text = state.Write("My Save");
        Assert.That(text, Does.Contain("tde = on"));
        Assert.That(text, Does.Contain("tdeStart = 98765.25"));
        Assert.That(text, Does.Contain("tdeBody = Kcalbeloh"));
        Assert.That(text, Does.Contain("beta = 3"));
        Assert.That(text, Does.Contain("future = 1"));
        Assert.That(text, Does.Contain("\"My Save\""));
        Assert.That(text, Does.Contain("KSPVE_SAVE"));
    }

    [Test]
    public void Defaults_AreABigStarOnADeepCoplanarOrbit_InRealTime()
    {
        var issues = new List<ConfigIssue>();
        TdeParameters p = TdeParameters.From(TdeSettings.Schema.Resolve(null, issues));
        Assert.That(p.StarMassSuns, Is.EqualTo(40.0));
        Assert.That(p.StarRadiusSuns, Is.EqualTo(15.0));
        Assert.That(p.PolytropeIndex, Is.EqualTo(3.0));
        Assert.That(p.Periapsis, Is.EqualTo(5.0));
        Assert.That(p.InclinationDeg, Is.EqualTo(0.0));
        Assert.That(p.TimeScale, Is.EqualTo(1.0));
        Assert.That(issues, Is.Empty);
    }
}
