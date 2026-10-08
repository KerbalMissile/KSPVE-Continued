using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Config;

public sealed class SettingsSchema
{
    private readonly List<SettingDef> _definitions = new List<SettingDef>();

    public SettingsSchema(string nodeName)
    {
        NodeName = nodeName;
    }

    public string NodeName { get; }
    public IReadOnlyList<SettingDef> Definitions => _definitions;

    public TDef Add<TDef>(TDef definition) where TDef : SettingDef
    {
        if (Find(definition.Key) != null) throw new ArgumentException($"Setting '{definition.Key}' is already defined in {NodeName}.");
        _definitions.Add(definition);
        return definition;
    }

    /// <summary>
    /// Resolves every defined setting from <paramref name="node"/> (defaults when absent).
    /// Later values for the same key win. Problems are appended to <paramref name="issues"/>; never throws.
    /// </summary>
    public SettingsValues Resolve(CfgNode node, List<ConfigIssue> issues)
    {
        var values = new SettingsValues();
        foreach (SettingDef def in _definitions) values.Set(def, def.DefaultObject);
        if (node == null) return values;

        foreach (KeyValuePair<string, string> pair in node.Values)
        {
            string path = NodeName + "/" + pair.Key;
            SettingDef def = Find(pair.Key);
            if (def == null)
            {
                issues.Add(new ConfigIssue(IssueSeverity.Warning, path, "unknown setting; ignored"));
                continue;
            }
            values.Set(def, def.Resolve(pair.Value, path, issues));
        }
        return values;
    }

    private SettingDef Find(string key)
    {
        foreach (SettingDef def in _definitions)
        {
            if (string.Equals(def.Key, key, StringComparison.OrdinalIgnoreCase)) return def;
        }
        return null;
    }
}

public sealed class SettingsValues
{
    private readonly Dictionary<SettingDef, object> _values = new Dictionary<SettingDef, object>();

    internal void Set(SettingDef def, object value) => _values[def] = value;

    public T Get<T>(SettingDef<T> def) => _values.TryGetValue(def, out object value) ? (T)value : def.Default;
}
