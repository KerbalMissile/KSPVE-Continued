using System;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Particles;

/// <summary>
/// One particle of a structure: where it is (metres from its body's centre, inertial frame), how big, and its light as the
/// renderer needs it. <see cref="Reflect"/> is the share of the starlight it scatters (before the star's phase function and
/// its colour, which the renderer adds), <see cref="Emit"/> its own glow in units of a sunlit white surface's radiance, and
/// <see cref="Opacity"/> how much it blocks the starlight for the particles behind it (self-shadowing).
/// </summary>
public struct StructureParticle
{
    public Double3 Position;
    public double Radius;
    public Double3 Reflect, Emit;
    public double Opacity;
    /// <summary>0: a hard-edged disc (a clump); 1: a soft round puff of gas.</summary>
    public float Softness;
}

/// <summary>The particles of one structure at one time, reused from frame to frame.</summary>
public sealed class ParticleSet
{
    public StructureParticle[] Items = new StructureParticle[0];
    public int Count;

    /// <summary>Per-particle numbers that do not change with time (impact times, say), valid while <see cref="CacheKey"/> matches.</summary>
    public double[] Cache = new double[0];
    public ulong CacheKey;
    public bool CacheValid;

    public void Clear() => Count = 0;

    public void Ensure(int capacity)
    {
        if (Items.Length < capacity) Array.Resize(ref Items, Math.Max(capacity, Items.Length * 2));
    }

    public void Add(Double3 position, double radius, Double3 reflect, Double3 emit, double opacity, float softness)
    {
        Ensure(Count + 1);
        Items[Count++] = new StructureParticle { Position = position, Radius = radius, Reflect = reflect, Emit = emit, Opacity = opacity, Softness = softness };
    }
}

/// <summary>What the generators need to know about the body, its star and the time.</summary>
public sealed class StructureContext
{
    /// <summary>Game time (s).</summary>
    public double Ut;
    /// <summary>The body: gravitational parameter (m^3/s^2), radius (m).</summary>
    public double BodyMu, BodyRadius;
    /// <summary>The star lighting it, if there is one (the body orbits it, or a star was named).</summary>
    public bool HasStar;
    public double StarMu, StarRadius, StarTemperature, StarLuminosity;
    /// <summary>The body's place and velocity relative to the star (inertial frame, m and m/s).</summary>
    public Double3 BodyFromStar, BodyVelocity;
    /// <summary>The axis it turns about (unit, inertial frame).</summary>
    public Double3 Axis = Double3.UnitY;

    public double BodyMass => BodyMu / UniversalKepler.G;
    public double StarMass => StarMu / UniversalKepler.G;
    public double StarDistance => BodyFromStar.Length;

    /// <summary>The unit vector from the body to the star (+x without a star).</summary>
    public Double3 ToStar => HasStar && BodyFromStar.LengthSquared > 0.0 ? (-BodyFromStar).Normalized() : Double3.UnitX;

    /// <summary>The starlight at the body (W/m^2): the star's luminosity over 4 pi d^2, or the Sun's at Earth without one.</summary>
    public double LocalFlux
    {
        get
        {
            if (!HasStar || StarDistance <= 0.0) return 1361.0;
            double lum = StarLuminosity > 0.0 ? StarLuminosity : Evaporation.Luminosity(StarRadius, StarTemperature);
            return lum / (4.0 * Math.PI * StarDistance * StarDistance);
        }
    }
}

/// <summary>Deterministic numbers from a seed, a particle and a launch: the same everywhere, in any order.</summary>
internal static class Rng
{
    public static ulong Key(ulong seed, long i, long k) => Mix(Mix(seed ^ (ulong)i * 0x9E3779B97F4A7C15UL) ^ (ulong)k * 0xC2B2AE3D27D4EB4FUL);

    /// <summary>The <paramref name="n"/>th uniform number in [0,1) of a key.</summary>
    public static double U(ulong key, int n) => (Mix(key + (ulong)n * 0x632BE59BD9B4E019UL) >> 11) * (1.0 / (1UL << 53));

    /// <summary>A standard normal number (Box-Muller) from two of a key's uniforms.</summary>
    public static double Gauss(ulong key, int n) =>
        Math.Sqrt(-2.0 * Math.Log(Math.Max(U(key, n), 1e-300))) * Math.Cos(2.0 * Math.PI * U(key, n + 1));

    /// <summary>A direction uniform over the sphere (uses uniforms n and n + 1).</summary>
    public static Double3 Direction(ulong key, int n)
    {
        double z = 2.0 * U(key, n) - 1.0, phi = 2.0 * Math.PI * U(key, n + 1), s = Math.Sqrt(Math.Max(1.0 - z * z, 0.0));
        return new Double3(s * Math.Cos(phi), s * Math.Sin(phi), z);
    }

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>Colours and light for the particles.</summary>
public static class StructureLight
{
    /// <summary>A blackbody's colour in linear Rec.709 scaled to luminance 1 (the way the black hole shader's StarLight is).</summary>
    public static Double3 Blackbody(double kelvin)
    {
        Rgb c = ColorTemperature.ToLinearRec709(kelvin);
        double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        return l > 1e-6 ? new Double3(c.R / l, c.G / l, c.B / l) : new Double3(1, 1, 1);
    }

    /// <summary>
    /// The glow of material at <paramref name="kelvin"/> relative to a white surface lit by the star: emissivity sigma T^4
    /// over the starlight there (so a 3000 K surface at 1 AU is 10^4 times brighter than a sunlit white one), times the display <paramref name="gain"/> (1 is physical), capped at <paramref name="cap"/>.
    /// </summary>
    public static Double3 Glow(double kelvin, double emissivity, double localFlux, double cap, double gain = 1.0)
    {
        if (kelvin < 300.0) return Double3.Zero;
        double ratio = Math.Min(gain * emissivity * Evaporation.StefanBoltzmann * Math.Pow(kelvin, 4.0) / Math.Max(localFlux, 1e-9), cap);
        return Blackbody(kelvin) * ratio;
    }

    /// <summary>
    /// Self-shadowing and eclipse: each particle's share of the starlight after the ones nearer the star. The particles are
    /// binned on a grid across the star's direction and swept from the star outwards, each bin accumulating the cross-section
    /// of the particles (their opacity times pi r^2) it has passed, so a particle behind a dense part sees exp(-tau) of the
    /// light; <paramref name="strength"/> blends from no shadow (0) to full. The body itself (a sphere of
    /// <paramref name="bodyRadius"/> at the origin) shadows what is behind it.
    /// </summary>
    public static void ApplyShadows(ParticleSet set, Double3 toStar, double strength, double bodyRadius)
    {
        int n = set.Count;
        if (n == 0) return;
        Double3 star = toStar.Normalized();
        Double3 seed = Math.Abs(star.Y) < 0.9 ? Double3.UnitY : Double3.UnitX;
        Double3 u = Double3.Cross(star, seed).Normalized(), w = Double3.Cross(star, u);

        var depth = new double[n];
        var order = new int[n];
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            Double3 p = set.Items[i].Position;
            depth[i] = -Double3.Dot(p, star);   // ascending = nearest the star first
            order[i] = i;
            double a = Double3.Dot(p, u), b = Double3.Dot(p, w);
            sumSq += a * a + b * b;
        }
        Array.Sort(depth, order);
        double extent = Math.Max(3.0 * Math.Sqrt(sumSq / n), 1e-9);
        int g = Math.Min(Math.Max((int)Math.Sqrt(n / 6.0), 8), 96);
        double cell = 2.0 * extent / g, cellArea = cell * cell;
        var accumulated = new double[g * g];
        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            StructureParticle p = set.Items[i];
            double a = Double3.Dot(p.Position, u), b = Double3.Dot(p.Position, w);
            int ia = Math.Min(Math.Max((int)((a + extent) / cell), 0), g - 1), ib = Math.Min(Math.Max((int)((b + extent) / cell), 0), g - 1);
            double tau = accumulated[ib * g + ia] / cellArea;
            double shade = 1.0 - strength + strength * Math.Exp(-tau);
            // The body: behind it, inside its shadow cylinder, no starlight.
            if (bodyRadius > 0.0 && Double3.Dot(p.Position, star) < 0.0 && a * a + b * b < bodyRadius * bodyRadius) shade = 0.0;
            set.Items[i].Reflect = p.Reflect * shade;
            accumulated[ib * g + ia] += p.Opacity * Math.PI * p.Radius * p.Radius;
        }
    }
}
