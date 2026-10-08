using System.Collections.Generic;
using System.Text;

namespace VolumetricBlackholeFx.Pure.Config;

/// <summary>Serialises CfgNodes in KSP's ConfigNode text format (tab-indented, one value per line).</summary>
public static class CfgWriter
{
    public static string Write(IEnumerable<CfgNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (CfgNode node in nodes) WriteNode(sb, node, 0);
        return sb.ToString();
    }

    private static void WriteNode(StringBuilder sb, CfgNode node, int depth)
    {
        string indent = new string('\t', depth);
        sb.Append(indent).Append(Sanitise(node.Name)).Append('\n');
        sb.Append(indent).Append("{\n");
        foreach (KeyValuePair<string, string> pair in node.Values)
        {
            sb.Append(indent).Append('\t').Append(Sanitise(pair.Key)).Append(" = ").Append(Sanitise(pair.Value)).Append('\n');
        }
        foreach (CfgNode child in node.Children) WriteNode(sb, child, depth + 1);
        sb.Append(indent).Append("}\n");
    }

    /// <summary>KSP config lines cannot contain line breaks or braces.</summary>
    private static string Sanitise(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) sb.Append(c == '\n' || c == '\r' || c == '{' || c == '}' ? ' ' : c);
        return sb.ToString();
    }
}
