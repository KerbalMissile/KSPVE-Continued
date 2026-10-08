using System;
using System.Collections.Generic;

namespace KSPVE.Pure;

/// <summary>KSPVE command-line switches, parsed from the KSP process arguments.</summary>
public sealed class LaunchOptions
{
    public const string SelfTestSwitch = "-kspve-selftest";
    public const string SafeModeSwitch = "-kspve-safe";
    public const string DumpBodiesSwitch = "-kspve-dump-bodies";

    /// <summary>Run the automated self-test, write a report and quit.</summary>
    public bool SelfTest { get; private set; }

    /// <summary>Start with every KSPVE pass disabled.</summary>
    public bool ForceSafeMode { get; private set; }

    /// <summary>Write every celestial body's orbit and gravity to PluginData/bodies.tsv at the main menu.</summary>
    public bool DumpBodies { get; private set; }

    public static LaunchOptions Parse(IEnumerable<string> args)
    {
        var options = new LaunchOptions();
        if (args == null) return options;
        foreach (string arg in args)
        {
            if (string.Equals(arg, SelfTestSwitch, StringComparison.OrdinalIgnoreCase)) options.SelfTest = true;
            else if (string.Equals(arg, SafeModeSwitch, StringComparison.OrdinalIgnoreCase)) options.ForceSafeMode = true;
            else if (string.Equals(arg, DumpBodiesSwitch, StringComparison.OrdinalIgnoreCase)) options.DumpBodies = true;
        }
        return options;
    }
}
