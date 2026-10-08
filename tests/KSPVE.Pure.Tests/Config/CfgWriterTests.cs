using KSPVE.Pure.Config;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Config;

public class CfgWriterTests
{
    [Test]
    public void WritesNestedNodesInKspFormat()
    {
        var root = new CfgNode("KSPVE_POST").Add("enabled", "True").Add("exposure", "0.5")
            .AddChild(new CfgNode("CHILD").Add("name", "x"));
        string text = CfgWriter.Write(new[] { root });
        Assert.That(text, Is.EqualTo(
            "KSPVE_POST\n{\n\tenabled = True\n\texposure = 0.5\n\tCHILD\n\t{\n\t\tname = x\n\t}\n}\n"));
    }

    [Test]
    public void SanitisesCharactersKspConfigCannotHold()
    {
        string text = CfgWriter.Write(new[] { new CfgNode("N").Add("k", "a\nb{c}") });
        Assert.That(text, Does.Contain("k = a b c "));
    }

    [Test]
    public void SetValue_ReplacesAllExistingValuesForKey()
    {
        var n = new CfgNode("KSPVE").Add("hdrCameras", "True").Add("HDRCAMERAS", "True").Add("logLevel", "Info");
        n.SetValue("hdrCameras", "False");
        Assert.That(n.GetLast("hdrCameras"), Is.EqualTo("False"));
        Assert.That(n.Values.FindAll(v => v.Key.ToLowerInvariant() == "hdrcameras").Count, Is.EqualTo(1));
        Assert.That(n.GetLast("logLevel"), Is.EqualTo("Info"));
    }
}
