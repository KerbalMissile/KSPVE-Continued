namespace KSPVE.Pure.Config;

/// <summary>The top-level KSPVE settings node. Feature-specific settings live in their own schemas.</summary>
public static class CoreSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE");

    public static readonly BoolSetting Enabled =
        Schema.Add(new BoolSetting("enabled", true, "Master switch for all KSPVE rendering."));

    public static readonly EnumSetting<LogLevel> MinLogLevel =
        Schema.Add(new EnumSetting<LogLevel>("logLevel", LogLevel.Info, "Lowest log level written to KSP.log."));

    public static readonly IntSetting SelfTestFlightFrames =
        Schema.Add(new IntSetting("selfTestFlightFrames", 600, 60, 36000, "Frames the self-test waits in flight before capturing."));

    public static readonly BoolSetting HdrCameras =
        Schema.Add(new BoolSetting("hdrCameras", true, "Render KSP's scene cameras in HDR so values above 1 survive until tonemapping."));
}
