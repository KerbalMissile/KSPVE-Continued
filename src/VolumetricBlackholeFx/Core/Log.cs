using System;
using VolumetricBlackholeFx.Pure;

namespace VolumetricBlackholeFx.Core;

/// <summary>Logging to KSP.log. Every line is prefixed with "[VolumetricBlackholeFx] ".</summary>
internal static class Log
{
    private const string Prefix = "[VolumetricBlackholeFx] ";

    public static LogLevel MinLevel = LogLevel.Info;

    public static void Debug(string message)
    {
        if (MinLevel <= LogLevel.Debug) UnityEngine.Debug.Log(Prefix + message);
    }

    public static void Info(string message)
    {
        if (MinLevel <= LogLevel.Info) UnityEngine.Debug.Log(Prefix + message);
    }

    public static void Warning(string message)
    {
        if (MinLevel <= LogLevel.Warning) UnityEngine.Debug.LogWarning(Prefix + message);
    }

    public static void Error(string message)
    {
        UnityEngine.Debug.LogError(Prefix + message);
    }

    public static void Exception(string context, Exception ex)
    {
        UnityEngine.Debug.LogError(Prefix + context + ": " + ex);
    }
}
