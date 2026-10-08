using System;
using KSPVE.Pure;

namespace KSPVE.Core;

/// <summary>KSPVE logging to KSP.log. Every line is prefixed with "[KSPVE] ".</summary>
internal static class Log
{
    private const string Prefix = "[KSPVE] ";

    public static LogLevel MinLevel = LogLevel.Info;

    /// <summary>Raised after every Error or Exception line (used by the crash marker).</summary>
    public static event Action ErrorLogged;

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
        ErrorLogged?.Invoke();
    }

    public static void Exception(string context, Exception ex)
    {
        UnityEngine.Debug.LogError(Prefix + context + ": " + ex);
        ErrorLogged?.Invoke();
    }
}
