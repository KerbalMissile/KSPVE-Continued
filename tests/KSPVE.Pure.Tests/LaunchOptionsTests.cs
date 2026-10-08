using NUnit.Framework;

namespace KSPVE.Pure.Tests;

public class LaunchOptionsTests
{
    [Test]
    public void NoKspveArguments_AllSwitchesOff()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "KSP_x64.exe", "-force-d3d11" });
        Assert.That(o.SelfTest, Is.False);
        Assert.That(o.ForceSafeMode, Is.False);
    }

    [Test]
    public void SelfTestSwitch_IsRecognised_CaseInsensitive()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "KSP_x64.exe", "-KSPVE-SelfTest" });
        Assert.That(o.SelfTest, Is.True);
        Assert.That(o.ForceSafeMode, Is.False);
    }

    [Test]
    public void SafeSwitch_IsRecognised()
    {
        LaunchOptions o = LaunchOptions.Parse(new[] { "-kspve-safe" });
        Assert.That(o.ForceSafeMode, Is.True);
    }

    [Test]
    public void DumpBodiesSwitch_IsRecognised_AndOffByDefault()
    {
        Assert.That(LaunchOptions.Parse(new[] { "KSP_x64.exe" }).DumpBodies, Is.False);
        Assert.That(LaunchOptions.Parse(new[] { "KSP_x64.exe", "-KSPVE-Dump-Bodies" }).DumpBodies, Is.True);
    }

    [Test]
    public void NullArguments_AreTreatedAsEmpty()
    {
        LaunchOptions o = LaunchOptions.Parse(null);
        Assert.That(o.SelfTest, Is.False);
        Assert.That(o.ForceSafeMode, Is.False);
    }
}
