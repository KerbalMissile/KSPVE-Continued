using System;
using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Config;

namespace KSPVE.Core;

/// <summary>
/// One self-contained part of KSPVE (clouds, aurorae, star effects, ...). Every non-abstract subclass with a public
/// parameterless constructor is found and created by the engine at startup, so adding a feature needs no edits elsewhere:
/// it loads its configs, updates each frame, draws its own section of the settings window and saves its own user node.
/// Each call is guarded: a feature that throws is logged (once per kind of call) and the rest carry on.
/// </summary>
internal abstract class Feature
{
    /// <summary>Its name in logs.</summary>
    public abstract string Name { get; }

    /// <summary>Features update and draw their settings in this order (lowest first).</summary>
    public virtual int Order => 0;

    /// <summary>Once, at startup, after the shaders are loaded: read configs (report problems to <paramref name="issues"/>).</summary>
    public virtual void Load(List<ConfigIssue> issues) { }

    /// <summary>Every frame (LateUpdate), after the scene context is built. <paramref name="allowed"/>: KSPVE may render.</summary>
    public virtual void Update(bool allowed) { }

    /// <summary>Every physics step.</summary>
    public virtual void FixedUpdate() { }

    /// <summary>Its header in the settings window, or null for none.</summary>
    public virtual string SettingsTitle => null;

    /// <summary>Its controls, under <see cref="SettingsTitle"/> (use <see cref="UI.Gui"/>'s helpers).</summary>
    public virtual void DrawSettings() { }

    /// <summary>What it keeps in PluginData/user.cfg when the player presses Save (a top-level node), or null.</summary>
    public virtual CfgNode UserNode() => null;

    /// <summary>The settings window's Defaults button: back to the configured values (not saved until Save).</summary>
    public virtual void ResetToDefaults() { }

    /// <summary>When the game quits.</summary>
    public virtual void Shutdown() { }
}

/// <summary>Finds, creates and runs every <see cref="Feature"/>.</summary>
internal static class Features
{
    private static readonly List<Feature> List = new List<Feature>();
    private static readonly HashSet<string> Failed = new HashSet<string>();

    public static IReadOnlyList<Feature> All => List;

    /// <summary>The created feature of a type, or null.</summary>
    public static T Get<T>() where T : Feature => List.OfType<T>().FirstOrDefault();

    public static void Create(List<ConfigIssue> issues)
    {
        if (List.Count > 0) return;
        IEnumerable<Type> types;
        try { types = typeof(Feature).Assembly.GetTypes(); }
        catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null); }
        foreach (Type type in types)
        {
            if (type.IsAbstract || !typeof(Feature).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            try { List.Add((Feature)Activator.CreateInstance(type)); }
            catch (Exception ex) { Log.Exception("Creating the feature " + type.Name + " failed", ex); }
        }
        List.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Name, b.Name));
        foreach (Feature f in List) Guard(f, "Load", () => f.Load(issues));
        Log.Info("Features: " + string.Join(", ", List.Select(f => f.Name).ToArray()));
    }

    public static void Update(bool allowed)
    {
        foreach (Feature f in List) Guard(f, "Update", () => f.Update(allowed));
    }

    public static void FixedUpdate()
    {
        foreach (Feature f in List) Guard(f, "FixedUpdate", f.FixedUpdate);
    }

    public static void Shutdown()
    {
        foreach (Feature f in List) Guard(f, "Shutdown", f.Shutdown);
    }

    public static IEnumerable<CfgNode> UserNodes()
    {
        foreach (Feature f in List)
        {
            CfgNode node = null;
            Guard(f, "UserNode", () => node = f.UserNode());
            if (node != null) yield return node;
        }
    }

    public static void ResetToDefaults()
    {
        foreach (Feature f in List) Guard(f, "ResetToDefaults", f.ResetToDefaults);
    }

    /// <summary>Runs one call of a feature, logging its first failure of that kind.</summary>
    public static void Guard(Feature f, string what, Action call)
    {
        try { call(); }
        catch (Exception ex)
        {
            if (Failed.Add(f.Name + "." + what)) Log.Exception($"{f.Name}: {what} failed (logged once)", ex);
        }
    }
}
