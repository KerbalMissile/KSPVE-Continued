using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Procedural;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Procedural;

[TestFixture]
public sealed class RockMeshGeneratorTests
{
    private static readonly RockStyle Style = new RockStyle(0.15, 1.5, 3, 3, 0.1);

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void ClosedManifoldWithExpectedCounts(int n)
    {
        var mesh = RockMeshGenerator.Generate(11, n, Style);
        int v = mesh.Vertices.Length, f = mesh.Triangles.Length / 3;
        Assert.That(v, Is.EqualTo(10 * (int)Math.Pow(4, n) + 2));
        Assert.That(f, Is.EqualTo(20 * (int)Math.Pow(4, n)));
        var edges = new Dictionary<long, int>();
        for (int t = 0; t < f; t++)
            for (int k = 0; k < 3; k++)
            {
                int a = mesh.Triangles[t * 3 + k], b = mesh.Triangles[t * 3 + (k + 1) % 3];
                long key = (long)Math.Min(a, b) * 1000000 + Math.Max(a, b);
                edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        foreach (var kv in edges) Assert.That(kv.Value, Is.EqualTo(2));
        Assert.That(v - edges.Count + f, Is.EqualTo(2));
    }

    [Test]
    public void NormalsAreUnitLength()
    {
        var mesh = RockMeshGenerator.Generate(3, 2, Style);
        Assert.That(mesh.Normals.Length, Is.EqualTo(mesh.Vertices.Length));
        foreach (var n in mesh.Normals) Assert.That(n.Length, Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void DeterministicAndSeedDependent()
    {
        var a = RockMeshGenerator.Generate(5, 2, Style);
        var b = RockMeshGenerator.Generate(5, 2, Style);
        var c = RockMeshGenerator.Generate(6, 2, Style);
        Assert.That(a.Vertices, Is.EqualTo(b.Vertices));
        Assert.That(a.Triangles, Is.EqualTo(b.Triangles));
        Assert.That(a.Vertices, Is.Not.EqualTo(c.Vertices));
    }

    [Test]
    public void WindingIsOutward()
    {
        var mesh = RockMeshGenerator.Generate(8, 3, Style);
        double vol = 0;
        for (int t = 0; t < mesh.Triangles.Length; t += 3)
        {
            var a = mesh.Vertices[mesh.Triangles[t]];
            var b = mesh.Vertices[mesh.Triangles[t + 1]];
            var c = mesh.Vertices[mesh.Triangles[t + 2]];
            vol += Double3.Dot(a, Double3.Cross(b, c)) / 6.0;
        }
        Assert.That(vol, Is.GreaterThan(0));
    }

    [Test]
    public void DisplacementChangesRadius()
    {
        var mesh = RockMeshGenerator.Generate(8, 3, Style);
        double min = double.MaxValue, max = 0;
        foreach (var v in mesh.Vertices) { min = Math.Min(min, v.Length); max = Math.Max(max, v.Length); }
        Assert.That(max - min, Is.GreaterThan(0.05));
    }
}
