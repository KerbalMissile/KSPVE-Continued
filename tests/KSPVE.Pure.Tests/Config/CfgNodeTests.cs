using KSPVE.Pure.Config;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Config;

public class CfgNodeTests
{
    [Test]
    public void GetLast_ReturnsLastValueForKey_CaseInsensitive()
    {
        var n = new CfgNode("KSPVE").Add("enabled", "True").Add("Enabled", "False");
        Assert.That(n.GetLast("ENABLED"), Is.EqualTo("False"));
        Assert.That(n.GetLast("missing"), Is.Null);
    }

    [Test]
    public void Overlay_KeepsBaseValuesAndAppendsOverrides()
    {
        var defaults = new CfgNode("KSPVE").Add("enabled", "True").Add("logLevel", "Info");
        var user = new CfgNode("KSPVE").Add("logLevel", "Debug");
        CfgNode merged = CfgNode.Overlay(defaults, user);
        Assert.That(merged.GetLast("enabled"), Is.EqualTo("True"));
        Assert.That(merged.GetLast("logLevel"), Is.EqualTo("Debug"));
        Assert.That(defaults.GetLast("logLevel"), Is.EqualTo("Info"), "base node must not be modified");
    }

    [Test]
    public void Overlay_MergesChildrenWithSameNameAndNameValue_AppendsOthers()
    {
        var defaults = new CfgNode("KSPVE")
            .AddChild(new CfgNode("KSPVE_BODY").Add("name", "Kerbin").Add("exposureBias", "0"));
        var user = new CfgNode("KSPVE")
            .AddChild(new CfgNode("KSPVE_BODY").Add("name", "Kerbin").Add("exposureBias", "0.5"))
            .AddChild(new CfgNode("KSPVE_BODY").Add("name", "Duna").Add("exposureBias", "-0.3"));
        CfgNode merged = CfgNode.Overlay(defaults, user);
        Assert.That(merged.Children.Count, Is.EqualTo(2));
        Assert.That(merged.Children[0].GetLast("exposureBias"), Is.EqualTo("0.5"));
        Assert.That(merged.Children[1].GetLast("name"), Is.EqualTo("Duna"));
    }

    [Test]
    public void Overlay_WithNullBase_ReturnsCopyOfOverrides()
    {
        var user = new CfgNode("KSPVE").Add("enabled", "False");
        CfgNode merged = CfgNode.Overlay(null, user);
        Assert.That(merged, Is.Not.SameAs(user));
        Assert.That(merged.GetLast("enabled"), Is.EqualTo("False"));
    }
}
