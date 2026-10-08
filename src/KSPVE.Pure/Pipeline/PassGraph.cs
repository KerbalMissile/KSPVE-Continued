using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Pipeline;

/// <summary>An immutable GPU pass declaration: which named resources it reads and writes.</summary>
public sealed class PassNode
{
    public PassNode(string id, int declaredOrder, IReadOnlyList<string> reads, IReadOnlyList<string> writes)
    {
        Id = id ?? string.Empty;
        DeclaredOrder = declaredOrder;
        Reads = reads ?? Array.Empty<string>();
        Writes = writes ?? Array.Empty<string>();
    }

    public string Id { get; }
    public int DeclaredOrder { get; }
    public IReadOnlyList<string> Reads { get; }
    public IReadOnlyList<string> Writes { get; }
}

/// <summary>Result of <see cref="PassGraph.Order"/>; errors never abort ordering.</summary>
public sealed class PassOrderResult
{
    public PassOrderResult(IReadOnlyList<PassNode> ordered, IReadOnlyList<string> errors)
    {
        Ordered = ordered;
        Errors = errors;
    }

    public IReadOnlyList<PassNode> Ordered { get; }
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Dependency ordering of passes from their resource reads and writes.</summary>
public static class PassGraph
{
    public static PassOrderResult Order(IReadOnlyList<PassNode> passes)
    {
        var errors = new List<string>();
        var nodes = new List<PassNode>();
        if (passes == null) return new PassOrderResult(nodes, errors);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < passes.Count; i++)
        {
            var p = passes[i];
            if (p == null) continue;
            if (!seen.Add(p.Id))
            {
                errors.Add("Duplicate pass id '" + p.Id + "'; keeping the first.");
                continue;
            }
            nodes.Add(p);
        }

        nodes.Sort(CompareNodes);
        int n = nodes.Count;

        // Per resource: producers, modifiers, consumers as indices (nodes are pre-sorted, so lists stay sorted).
        var perResource = new Dictionary<string, ResourceUse>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var node = nodes[i];
            var reads = new HashSet<string>(node.Reads ?? Array.Empty<string>(), StringComparer.Ordinal);
            var writes = new HashSet<string>(node.Writes ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var r in reads)
            {
                if (r == null) continue;
                var use = Get(perResource, r);
                if (writes.Contains(r)) use.Modifiers.Add(i); else use.Consumers.Add(i);
            }
            foreach (var w in writes)
            {
                if (w == null || reads.Contains(w)) continue;
                Get(perResource, w).Producers.Add(i);
            }
        }

        var edges = new HashSet<long>();
        var successors = new List<int>[n];
        var indegree = new int[n];
        for (int i = 0; i < n; i++) successors[i] = new List<int>();

        void AddEdge(int from, int to)
        {
            if (from == to) return;
            if (!edges.Add(((long)from << 32) | (uint)to)) return;
            successors[from].Add(to);
            indegree[to]++;
        }

        foreach (var use in perResource.Values)
        {
            var mods = use.Modifiers;
            foreach (var p in use.Producers)
            {
                if (mods.Count > 0) AddEdge(p, mods[0]);
                foreach (var c in use.Consumers) AddEdge(p, c);
            }
            for (int m = 0; m + 1 < mods.Count; m++) AddEdge(mods[m], mods[m + 1]);
            if (mods.Count > 0)
                foreach (var c in use.Consumers) AddEdge(mods[mods.Count - 1], c);
        }

        // Kahn: nodes are sorted by (DeclaredOrder, Id), so lowest ready index == lowest key.
        var ordered = new List<PassNode>(n);
        var done = new bool[n];
        for (int step = 0; step < n; step++)
        {
            int pick = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && indegree[i] == 0) { pick = i; break; }
            if (pick < 0) break;
            done[pick] = true;
            ordered.Add(nodes[pick]);
            foreach (var s in successors[pick]) indegree[s]--;
        }

        if (ordered.Count < n)
        {
            var names = new List<string>();
            for (int i = 0; i < n; i++)
                if (!done[i]) { names.Add(nodes[i].Id); ordered.Add(nodes[i]); }
            errors.Add("Dependency cycle among passes: " + string.Join(", ", names) + ".");
        }

        return new PassOrderResult(ordered, errors);
    }

    private static int CompareNodes(PassNode a, PassNode b)
    {
        int c = a.DeclaredOrder.CompareTo(b.DeclaredOrder);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    private static ResourceUse Get(Dictionary<string, ResourceUse> map, string key)
    {
        if (!map.TryGetValue(key, out var use)) map[key] = use = new ResourceUse();
        return use;
    }

    private sealed class ResourceUse
    {
        public readonly List<int> Producers = new();
        public readonly List<int> Modifiers = new();
        public readonly List<int> Consumers = new();
    }
}
