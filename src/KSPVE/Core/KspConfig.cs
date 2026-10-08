using System.Collections.Generic;
using System.IO;
using KSPVE.Pure.Config;

namespace KSPVE.Core;

/// <summary>Bridges KSP's ConfigNode into KSPVE.Pure's CfgNode, and reads/writes PluginData/user.cfg.</summary>
internal static class KspConfig
{
    public static CfgNode ToCfg(ConfigNode node)
    {
        var cfg = new CfgNode(node.name);
        for (int i = 0; i < node.values.Count; i++) cfg.Add(node.values[i].name, node.values[i].value);
        for (int i = 0; i < node.nodes.Count; i++) cfg.AddChild(ToCfg(node.nodes[i]));
        return cfg;
    }

    public static CfgNode LoadMerged() => LoadMerged(CoreSettings.Schema.NodeName);

    /// <summary>
    /// All <paramref name="nodeName"/> nodes from GameDatabase (after ModuleManager), in load order,
    /// then the same node from the user's PluginData/user.cfg on top. Later values win.
    /// </summary>
    public static CfgNode LoadMerged(string nodeName)
    {
        var merged = new CfgNode(nodeName);
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(nodeName))
            merged = CfgNode.Overlay(merged, ToCfg(node));

        foreach (CfgNode user in LoadUserNodes())
        {
            if (user.Name == nodeName) merged = CfgNode.Overlay(merged, user);
        }
        return merged;
    }

    public static List<CfgNode> LoadUserNodes()
    {
        var nodes = new List<CfgNode>();
        if (!File.Exists(Paths.UserConfigPath)) return nodes;
        ConfigNode file = ConfigNode.Load(Paths.UserConfigPath);
        if (file == null) return nodes;
        for (int i = 0; i < file.nodes.Count; i++) nodes.Add(ToCfg(file.nodes[i]));
        return nodes;
    }

    /// <summary>Replaces (by node name) or adds the given top-level nodes in user.cfg, keeping any others.</summary>
    public static void SaveUserNodes(IEnumerable<CfgNode> replacements)
    {
        List<CfgNode> nodes = LoadUserNodes();
        foreach (CfgNode replacement in replacements)
        {
            int index = nodes.FindIndex(n => n.Name == replacement.Name);
            if (index >= 0) nodes[index] = replacement;
            else nodes.Add(replacement);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.UserConfigPath));
        File.WriteAllText(Paths.UserConfigPath, CfgWriter.Write(nodes));
    }
}
