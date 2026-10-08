using System;
using System.Collections.Generic;

namespace VolumetricBlackholeFx.Pure.Config;

/// <summary>
/// Unity-free config tree mirroring KSP's ConfigNode shape (ordered key/value pairs plus
/// child nodes). Duplicate keys are allowed; consumers treat the last value as the winner.
/// </summary>
public sealed class CfgNode
{
    public CfgNode(string name)
    {
        Name = name ?? string.Empty;
    }

    public string Name { get; }
    public List<KeyValuePair<string, string>> Values { get; } = new List<KeyValuePair<string, string>>();
    public List<CfgNode> Children { get; } = new List<CfgNode>();

    /// <summary>Children with the same node name and the same "name" value are the same logical node.</summary>
    private string Identity => Name + "|" + (GetLast("name") ?? string.Empty);

    public CfgNode Add(string key, string value)
    {
        Values.Add(new KeyValuePair<string, string>(key, value));
        return this;
    }

    public CfgNode AddChild(CfgNode child)
    {
        Children.Add(child);
        return this;
    }

    /// <summary>Removes every value for <paramref name="key"/> (case-insensitive) and adds one with the new value.</summary>
    public CfgNode SetValue(string key, string value)
    {
        Values.RemoveAll(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase));
        return Add(key, value);
    }

    public string GetLast(string key)
    {
        for (int i = Values.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Values[i].Key, key, StringComparison.OrdinalIgnoreCase)) return Values[i].Value;
        }
        return null;
    }

    public CfgNode Clone()
    {
        var copy = new CfgNode(Name);
        copy.Values.AddRange(Values);
        foreach (CfgNode child in Children) copy.Children.Add(child.Clone());
        return copy;
    }

    /// <summary>
    /// Returns a new tree: base values followed by override values (so overrides win on lookup),
    /// with children merged recursively by identity and unmatched override children appended.
    /// Neither input is modified.
    /// </summary>
    public static CfgNode Overlay(CfgNode baseNode, CfgNode overrides)
    {
        if (baseNode == null) return overrides?.Clone();
        CfgNode result = baseNode.Clone();
        if (overrides == null) return result;

        result.Values.AddRange(overrides.Values);
        foreach (CfgNode child in overrides.Children)
        {
            int index = result.Children.FindIndex(c => c.Identity == child.Identity);
            if (index >= 0) result.Children[index] = Overlay(result.Children[index], child);
            else result.Children.Add(child.Clone());
        }
        return result;
    }
}
