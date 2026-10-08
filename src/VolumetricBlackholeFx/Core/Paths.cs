using System.IO;

namespace VolumetricBlackholeFx.Core;

/// <summary>Absolute paths inside the KSP install. The mod writes only under <see cref="ModDir"/>.</summary>
internal static class Paths
{
    public const string ModFolderName = "VolumetricBlackholeFx";

    public static string KspRoot => Path.GetFullPath(KSPUtil.ApplicationRootPath);
    public static string GameDataDir => Path.Combine(KspRoot, "GameData");
    public static string ModDir => Path.Combine(GameDataDir, ModFolderName);
    public static string PluginDataDir => Path.Combine(ModDir, "PluginData");
    public static string UserConfigPath => Path.Combine(PluginDataDir, "user.cfg");
}
