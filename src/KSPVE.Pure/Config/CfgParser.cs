using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Config;

/// <summary>
/// Reads KSP's config file text into <see cref="CfgNode"/> trees: "key = value" lines (split at the first '=', both
/// sides trimmed), nodes as a name followed by braces (on the same line or the next), "//" comments. A stray closing
/// brace is skipped; unclosed nodes end with the text.
/// </summary>
public static class CfgParser
{
    /// <summary>The top-level nodes in <paramref name="text"/> (values outside any node are dropped).</summary>
    public static List<CfgNode> Parse(string text)
    {
        var root = new CfgNode("root");
        var stack = new Stack<CfgNode>();
        stack.Push(root);
        string pendingName = null;
        foreach (string raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            line = line.Trim();
            while (line.Length > 0)
            {
                int open = line.IndexOf('{'), close = line.IndexOf('}'), eq = line.IndexOf('=');
                int brace = open < 0 ? close : close < 0 ? open : Math.Min(open, close);
                if (eq >= 0 && (brace < 0 || eq < brace))
                {
                    // A value runs to the end of the line, or to a brace that closes the node on the same line.
                    string key = line.Substring(0, eq).Trim();
                    string rest = line.Substring(eq + 1);
                    int end = rest.IndexOf('}');
                    string value = (end >= 0 ? rest.Substring(0, end) : rest).Trim();
                    if (pendingName != null) { stack.Peek().Add(pendingName, string.Empty); pendingName = null; }
                    if (key.Length > 0) stack.Peek().Add(key, value);
                    line = end >= 0 ? rest.Substring(end) : string.Empty;
                    continue;
                }
                if (brace == open && open >= 0)
                {
                    string name = line.Substring(0, open).Trim();
                    if (name.Length == 0) name = pendingName ?? string.Empty;
                    else if (pendingName != null) stack.Peek().Add(pendingName, string.Empty);
                    pendingName = null;
                    var node = new CfgNode(name);
                    stack.Peek().AddChild(node);
                    stack.Push(node);
                    line = line.Substring(open + 1).Trim();
                    continue;
                }
                if (brace == close && close >= 0)
                {
                    string before = line.Substring(0, close).Trim();
                    if (before.Length > 0) stack.Peek().Add(before, string.Empty);
                    if (pendingName != null) { stack.Peek().Add(pendingName, string.Empty); pendingName = null; }
                    if (stack.Count > 1) stack.Pop();
                    line = line.Substring(close + 1).Trim();
                    continue;
                }
                // A bare word: a node's name if braces follow, else a key with no value.
                if (pendingName != null) stack.Peek().Add(pendingName, string.Empty);
                pendingName = line;
                line = string.Empty;
            }
        }
        if (pendingName != null) stack.Peek().Add(pendingName, string.Empty);
        return root.Children;
    }
}
