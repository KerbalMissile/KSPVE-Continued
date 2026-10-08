using System;
using System.Collections.Generic;
using KSPVE.Pure.Config;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Config;

public class SettingsSchemaTests
{
    public enum Mode { Play, Cinematic }

    private SettingsSchema _schema;
    private BoolSetting _flag;
    private IntSetting _count;
    private DoubleSetting _scale;
    private EnumSetting<Mode> _mode;

    [SetUp]
    public void SetUp()
    {
        _schema = new SettingsSchema("TEST");
        _flag = _schema.Add(new BoolSetting("flag", true, "a flag"));
        _count = _schema.Add(new IntSetting("count", 10, 1, 100, "a count"));
        _scale = _schema.Add(new DoubleSetting("scale", 1.0, 0.25, 4.0, "a scale"));
        _mode = _schema.Add(new EnumSetting<Mode>("mode", Mode.Play, "a mode"));
    }

    [Test]
    public void NullNode_YieldsDefaults_WithoutIssues()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(null, issues);
        Assert.That(v.Get(_flag), Is.True);
        Assert.That(v.Get(_count), Is.EqualTo(10));
        Assert.That(v.Get(_scale), Is.EqualTo(1.0));
        Assert.That(v.Get(_mode), Is.EqualTo(Mode.Play));
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void ValidValues_AreParsed()
    {
        var node = new CfgNode("TEST").Add("flag", "False").Add("count", "42").Add("scale", "2.5").Add("mode", "cinematic");
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(node, issues);
        Assert.That(v.Get(_flag), Is.False);
        Assert.That(v.Get(_count), Is.EqualTo(42));
        Assert.That(v.Get(_scale), Is.EqualTo(2.5));
        Assert.That(v.Get(_mode), Is.EqualTo(Mode.Cinematic));
        Assert.That(issues, Is.Empty);
    }

    [Test]
    public void InvalidBool_FallsBackToDefault_WithWarningNamingPathAndValue()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(new CfgNode("TEST").Add("flag", "maybe"), issues);
        Assert.That(v.Get(_flag), Is.True);
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Severity, Is.EqualTo(IssueSeverity.Warning));
        Assert.That(issues[0].Path, Is.EqualTo("TEST/flag"));
        Assert.That(issues[0].Message, Does.Contain("maybe"));
    }

    [Test]
    public void OutOfRangeInt_IsClamped_WithWarning()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(new CfgNode("TEST").Add("count", "500"), issues);
        Assert.That(v.Get(_count), Is.EqualTo(100));
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Message, Does.Contain("clamped"));
    }

    [Test]
    public void CommaDecimal_IsRejected_BecauseConfigIsInvariantCulture()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(new CfgNode("TEST").Add("scale", "2,5"), issues);
        Assert.That(v.Get(_scale), Is.EqualTo(1.0));
        Assert.That(issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void NumericEnumValue_IsRejected()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(new CfgNode("TEST").Add("mode", "1"), issues);
        Assert.That(v.Get(_mode), Is.EqualTo(Mode.Play));
        Assert.That(issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void UnknownKey_IsReportedAndIgnored()
    {
        var issues = new List<ConfigIssue>();
        _schema.Resolve(new CfgNode("TEST").Add("colour", "blue"), issues);
        Assert.That(issues, Has.Count.EqualTo(1));
        Assert.That(issues[0].Path, Is.EqualTo("TEST/colour"));
        Assert.That(issues[0].Message, Does.Contain("unknown"));
    }

    [Test]
    public void LaterValueForSameKey_Wins()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = _schema.Resolve(new CfgNode("TEST").Add("count", "5").Add("count", "7"), issues);
        Assert.That(v.Get(_count), Is.EqualTo(7));
    }

    [Test]
    public void DuplicateDefinition_Throws()
    {
        Assert.Throws<ArgumentException>(() => _schema.Add(new BoolSetting("FLAG", false, "dup")));
    }

    [Test]
    public void CoreSettings_HaveSaneDefaults()
    {
        var issues = new List<ConfigIssue>();
        SettingsValues v = CoreSettings.Schema.Resolve(null, issues);
        Assert.That(CoreSettings.Schema.NodeName, Is.EqualTo("KSPVE"));
        Assert.That(v.Get(CoreSettings.Enabled), Is.True);
        Assert.That(v.Get(CoreSettings.MinLogLevel), Is.EqualTo(LogLevel.Info));
        Assert.That(v.Get(CoreSettings.SelfTestFlightFrames), Is.EqualTo(600));
        Assert.That(v.Get(CoreSettings.HdrCameras), Is.True);
        Assert.That(issues, Is.Empty);
    }
}
