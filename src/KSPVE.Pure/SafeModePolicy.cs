namespace KSPVE.Pure;

public enum SafeModeReason
{
    None,
    ForcedByLaunchOption,
    PreviousSessionCrashedWithErrors
}

/// <summary>Decides at startup whether KSPVE should keep all of its rendering disabled.</summary>
public static class SafeModePolicy
{
    public static SafeModeReason Decide(bool forcedByLaunchOption, bool previousSessionUnclean, int previousSessionErrorCount)
    {
        if (forcedByLaunchOption) return SafeModeReason.ForcedByLaunchOption;
        if (previousSessionUnclean && previousSessionErrorCount > 0) return SafeModeReason.PreviousSessionCrashedWithErrors;
        return SafeModeReason.None;
    }
}
