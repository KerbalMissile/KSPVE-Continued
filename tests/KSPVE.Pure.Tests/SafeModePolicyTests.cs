using NUnit.Framework;

namespace KSPVE.Pure.Tests;

public class SafeModePolicyTests
{
    [Test]
    public void CleanPreviousSession_StartsNormally()
    {
        Assert.That(SafeModePolicy.Decide(false, false, 0), Is.EqualTo(SafeModeReason.None));
    }

    [Test]
    public void UncleanExitWithoutKspveErrors_StartsNormally()
    {
        // KSP can crash for reasons unrelated to KSPVE; only our own errors justify safe mode.
        Assert.That(SafeModePolicy.Decide(false, true, 0), Is.EqualTo(SafeModeReason.None));
    }

    [Test]
    public void UncleanExitWithKspveErrors_StartsInSafeMode()
    {
        Assert.That(SafeModePolicy.Decide(false, true, 3), Is.EqualTo(SafeModeReason.PreviousSessionCrashedWithErrors));
    }

    [Test]
    public void LaunchOption_AlwaysWins()
    {
        Assert.That(SafeModePolicy.Decide(true, false, 0), Is.EqualTo(SafeModeReason.ForcedByLaunchOption));
        Assert.That(SafeModePolicy.Decide(true, true, 5), Is.EqualTo(SafeModeReason.ForcedByLaunchOption));
    }
}
