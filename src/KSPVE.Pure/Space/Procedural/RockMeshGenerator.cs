using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Procedural;

/// <summary>Displacement parameters for a procedural rock (base radius 1).</summary>
public sealed class RockStyle
{
    public double NoiseAmplitude { get; }
    public double NoiseFrequency { get; }
    public int Octaves { get; }
    public int CraterCount { get; }
    /// <summary>Crater bowl depth as a fraction of the base radius.</summary>
    public double CraterDepth { get; }

    public RockStyle(double noiseAmplitude, double noiseFrequency, int octaves, int craterCount, double craterDepth)
    {
        NoiseAmplitude = noiseAmplitude;
        NoiseFrequency = noiseFrequency;
        Octaves = octaves;
        CraterCount = craterCount;
        CraterDepth = craterDepth;
    }
}

public sealed class MeshData
{
    public Double3[] Vertices { get; }
    public Double3[] Normals { get; }
    /// <summary>Counter-clockwise (viewed from outside) triangle index triples.</summary>
    public int[] Triangles { get; }

    public MeshData(Double3[] vertices, Double3[] normals, int[] triangles)
    {
        Vertices = vertices;
        Normals = normals;
        Triangles = triangles;
    }
}

/// <summary>Icosphere with fBm radial displacement and spherical-cap craters. Right-handed frame; Unity layer converts.</summary>
public static class RockMeshGenerator
{
    public static MeshData Generate(int seed, int subdivisions, RockStyle style)
    {
        var verts = new List<Double3>();
        var tris = new List<int>();
        BuildIcosahedron(verts, tris);
        for (int s = 0; s < subdivisions; s++) Subdivide(verts, tris);

        var rng = new Pcg32((ulong)(uint)seed, 0x726f636bUL);
        var craters = new (Double3 Dir, double Angle, double Depth)[Math.Max(0, style.CraterCount)];
        for (int i = 0; i < craters.Length; i++)
        {
            double cz = 2.0 * rng.NextDouble() - 1.0, phi = 2.0 * Math.PI * rng.NextDouble();
            double sz = Math.Sqrt(1.0 - cz * cz);
            double angle = 0.15 + 0.3 * rng.NextDouble();
            double depth = style.CraterDepth * (0.5 + 0.5 * rng.NextDouble());
            craters[i] = (new Double3(sz * Math.Cos(phi), sz * Math.Sin(phi), cz), angle, depth);
        }

        var vertices = new Double3[verts.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            Double3 d = verts[i];
            double n = Noise3.Fbm(d.X * style.NoiseFrequency, d.Y * style.NoiseFrequency, d.Z * style.NoiseFrequency,
                Math.Max(1, style.Octaves), 2.0, 0.5, seed);
            double r = 1.0 + style.NoiseAmplitude * n;
            foreach (var c in craters) r += CraterOffset(Double3.Dot(d, c.Dir), c.Angle, c.Depth);
            vertices[i] = d * Math.Max(0.05, r);
        }

        var triangles = tris.ToArray();
        var normals = new Double3[vertices.Length];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Double3 fn = Double3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]); // area weighted
            normals[a] += fn; normals[b] += fn; normals[c] += fn;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            Double3 n = normals[i].Normalized();
            normals[i] = n.LengthSquared > 0 ? n : verts[i];
        }
        return new MeshData(vertices, normals, triangles);
    }

    /// <summary>Parabolic bowl inside the cap (angle theta &lt; r) plus a Gaussian rim bump at theta = r.</summary>
    private static double CraterOffset(double cosTheta, double capAngle, double depth)
    {
        double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosTheta)));
        if (theta > 2.0 * capAngle) return 0.0;
        double offset = 0.0;
        if (theta < capAngle)
        {
            double q = theta / capAngle;
            offset -= depth * (1.0 - q * q);
        }
        double w = (theta - capAngle) / (0.25 * capAngle);
        offset += 0.4 * depth * Math.Exp(-w * w);
        return offset;
    }

    private static void BuildIcosahedron(List<Double3> v, List<int> t)
    {
        double g = (1.0 + Math.Sqrt(5.0)) / 2.0;
        double[,] p =
        {
            { -1, g, 0 }, { 1, g, 0 }, { -1, -g, 0 }, { 1, -g, 0 },
            { 0, -1, g }, { 0, 1, g }, { 0, -1, -g }, { 0, 1, -g },
            { g, 0, -1 }, { g, 0, 1 }, { -g, 0, -1 }, { -g, 0, 1 },
        };
        for (int i = 0; i < 12; i++) v.Add(new Double3(p[i, 0], p[i, 1], p[i, 2]).Normalized());
        int[] f =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };
        t.AddRange(f);
    }

    private static void Subdivide(List<Double3> v, List<int> t)
    {
        var cache = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if (cache.TryGetValue(key, out int m)) return m;
            v.Add(((v[a] + v[b]) * 0.5).Normalized());
            return cache[key] = v.Count - 1;
        }
        var next = new List<int>(t.Count * 4);
        for (int i = 0; i < t.Count; i += 3)
        {
            int a = t[i], b = t[i + 1], c = t[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        t.Clear();
        t.AddRange(next);
    }
}
