namespace VolumetricBlackholeFx.Pure.Config;

/// <summary>The mod-wide settings node. Each black hole has its own VOLUMETRIC_BLACKHOLE node (BlackHoleSettings).</summary>
public static class CoreSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("VOLUMETRIC_BLACKHOLE_FX");

    public static readonly BoolSetting Enabled =
        Schema.Add(new BoolSetting("enabled", true, "Master switch: draw the black holes (disk drag and disk light go with them)."));

    public static readonly EnumSetting<LogLevel> MinLogLevel =
        Schema.Add(new EnumSetting<LogLevel>("logLevel", LogLevel.Info, "Lowest log level written to KSP.log."));

    public static readonly DoubleSetting Brightness =
        Schema.Add(new DoubleSetting("brightness", 1.0, 0.0, 5.0, "Brightness multiplier for every accretion disk."));

    public static readonly DoubleSetting RenderScale =
        Schema.Add(new DoubleSetting("renderScale", 0.5, 0.25, 1.0, "Render scale of the black holes: 0.5 draws them at half resolution (about 4x faster), 1 at full resolution."));
}
