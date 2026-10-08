using System.IO;

namespace KSPVE.Core;

/// <summary>Absolute paths inside the KSP install. KSPVE writes only under <see cref="ModDir"/>.</summary>
internal static class Paths
{
    public static string KspRoot => Path.GetFullPath(KSPUtil.ApplicationRootPath);
    public static string GameDataDir => Path.Combine(KspRoot, "GameData");
    public static string ModDir => Path.Combine(GameDataDir, "KSPVE");
    public static string PluginDataDir => Path.Combine(ModDir, "PluginData");
    public static string UserConfigPath => Path.Combine(PluginDataDir, "user.cfg");
    public static string SessionMarkerPath => Path.Combine(PluginDataDir, "session.marker");
    public static string SelfTestReportPath => Path.Combine(Path.Combine(PluginDataDir, "selftest"), "report.json");
    public static string SavesDir => Path.Combine(KspRoot, "saves");
    public static string BodyDumpPath => Path.Combine(PluginDataDir, "bodies.tsv");
}
