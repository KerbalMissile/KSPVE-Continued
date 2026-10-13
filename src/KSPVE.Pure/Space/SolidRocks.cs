using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>Whether a belt's or disk's rocks can be hit (rockCollisions).</summary>
public enum RockCollisions
{
    /// <summary>Rocks are drawn only: craft pass through them.</summary>
    Off,
    /// <summary>A field of solid rocks round the craft, spaced and sized by solidRockSpacing and solidRockSize, drawn as it is:
    /// what you see you can hit.</summary>
    AsDrawn,
    /// <summary>Solid rocks as many as a real belt holds (<see cref="SolidRocks.RealSpacing"/>): collisions as rare as in reality.</summary>
    Realistic,
}

/// <summary>
/// A solid rock near a craft, in its object's disk frame (metres: x and y in the plane from the disk's reference direction,
/// z along its normal): where it is, how it moves, its radius, and an id that stays the same while it is near.
/// </summary>
public readonly struct SolidRock
{
    public readonly long Id;
    public readonly Double3 Position, Velocity;
    public readonly double Radius;
    /// <summary>Its angular speed round the normal (rad/s; negative: the other way), the same for its whole ring of cells.</summary>
    public readonly double AngularSpeed;

    public SolidRock(long id, Double3 position, Double3 velocity, double radius, double angularSpeed)
    {
        Id = id;
        Position = position;
        Velocity = velocity;
        Radius = radius;
        AngularSpeed = angularSpeed;
    }

    /// <summary>Where it is <paramref name="dt"/> seconds on: carried round the normal on its circular orbit.</summary>
    public Double3 PositionAfter(double dt)
    {
        double a = AngularSpeed * dt, c = Math.Cos(a), s = Math.Sin(a);
        return new Double3(Position.X * c - Position.Y * s, Position.X * s + Position.Y * c, Position.Z);
    }

    /// <summary>Its velocity <paramref name="dt"/> seconds on.</summary>
    public Double3 VelocityAfter(double dt)
    {
        double a = AngularSpeed * dt, c = Math.Cos(a), s = Math.Sin(a);
        return new Double3(Velocity.X * c - Velocity.Y * s, Velocity.X * s + Velocity.Y * c, Velocity.Z);
    }
}

/// <summary>
/// Where a belt's or disk's rocks can be, in metres, as its drawn rocks are placed (BlackHole.shader's RockField): between
/// its edges (a debris disk: round its middle), thinning above and below the plane, cut by its gaps. 0 to 1.
/// </summary>
public sealed class RockFieldShape
{
    public SpaceObjectKind Kind = SpaceObjectKind.AsteroidBelt;
    public double Inner, Outer;
    /// <summary>Half-thickness over radius (diskThickness), and how far the rocks spread above and below (rockSpread).</summary>
    public double Thickness, Spread = 1.0;
    /// <summary>Its gaps (DiskGaps), in metres.</summary>
    public DiskGap[] Gaps = new DiskGap[0];
    public int GapCount;

    public double Chance(Double3 p)
    {
        double r = Math.Sqrt(p.X * p.X + p.Y * p.Y), rs = p.Length;   // edges by distance, thickness by angle, as the shader
        double radial = Kind == SpaceObjectKind.DebrisDisk
            ? Math.Exp(-2.0 * Sq((rs - 0.5 * (Inner + Outer)) / Math.Max(0.5 * (Outer - Inner), 1e-9)))
            : SmoothStep(Inner * 0.9, Inner * 1.15, rs) * (1.0 - SmoothStep(Outer * 0.85, Outer * 1.1, rs));
        if (radial <= 0.0) return 0.0;
        double gaps = 1.0;
        for (int k = 0; k < GapCount; k++) gaps *= 1.0 - Gaps[k].Depth * Math.Exp(-Sq((rs - Gaps[k].Radius) / Math.Max(Gaps[k].Width, 1e-9)));
        double h = Math.Max(Thickness * r * Spread, 1e-6);
        return radial * gaps * Math.Exp(-0.5 * Sq(p.Z / h));
    }

    private static double Sq(double x) => x * x;

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Min(Math.Max((x - a) / (b - a), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}

/// <summary>
/// The solid rocks round a point of a belt or disk. Rocks sit on rings of cells round the central body, each ring turning
/// at the circular orbital speed of its radius in game time (so they shear as real orbits do), one rock per cell where
/// the field (<see cref="RockFieldShape"/>) and a hash say so. Everything follows from the cell, the seed and the time, so
/// the same rocks are there however often they are looked up, and on any computer.
/// </summary>
public static class SolidRocks
{
    /// <summary>
    /// Asteroids at least 1 km across in the main asteroid belt: about 1.2 million (Tedesco and Desert 2002), in some
    /// 6e34 m^3 (2.1-3.3 AU, about 10 degrees either side of the plane): one every 3.7 million km.
    /// </summary>
    public const double RealSpacing = 3.7e9;
    /// <summary>Real rocks' radii (m): from 0.5 km, more of the small ones (N(&gt;R) ~ R^-2.5), up to this.</summary>
    public const double RealMinRadius = 500.0, RealMaxRadius = 50000.0;
    /// <summary>The most cells looked at in one lookup (a guard against a spacing far smaller than the range).</summary>
    public const long MaxCells = 400000;

    /// <summary>
    /// The rocks within <paramref name="range"/> of <paramref name="center"/> (disk frame, m) at game time
    /// <paramref name="ut"/>, added to <paramref name="into"/>. <paramref name="share"/>: the share of cells, where the field
    /// is full, that hold a rock (the object's rocks setting; a realistic field is always full). <paramref name="gm"/>: the
    /// central body's gravitational parameter (m^3/s^2); <paramref name="spin"/>: +1 round the normal, -1 the other way.
    /// <paramref name="spacing"/> and <paramref name="largest"/> (the largest rock's radius, m) size an as-drawn field;
    /// <paramref name="sizeSpread"/> how much radii vary (0: all the largest).
    /// </summary>
    public static void Near(RockFieldShape shape, RockCollisions mode, double share, double gm, double spin, double spacing, double largest,
        double sizeSpread, ulong seed, double ut, Double3 center, double range, List<SolidRock> into)
    {
        if (mode == RockCollisions.Off || gm <= 0.0) return;
        bool real = mode == RockCollisions.Realistic;
        double s = real ? RealSpacing : Math.Max(spacing, 1e-3);
        if (real) share = 1.0;
        if (share <= 0.0) return;
        double rho = Math.Sqrt(center.X * center.X + center.Y * center.Y);
        long iFrom = Math.Max(0, (long)Math.Floor((rho - range) / s) - 1), iTo = (long)Math.Floor((rho + range) / s) + 1;
        long kFrom = (long)Math.Floor((center.Z - range) / s) - 1, kTo = (long)Math.Floor((center.Z + range) / s) + 1;
        double thetaC = Math.Atan2(center.Y, center.X);
        long cells = 0;
        for (long i = iFrom; i <= iTo; i++)
        {
            double ri = (i + 0.5) * s;
            long n = Math.Max(1, (long)Math.Floor(2.0 * Math.PI * ri / s));
            double step = 2.0 * Math.PI / n;
            double omega = spin * Math.Sqrt(gm / (ri * ri * ri));
            double phase = omega * ut;
            // The cells round this ring that come within range: all of them when the ring is not much bigger than it.
            long jFrom, jTo;
            if (range + 2.0 * s >= ri) { jFrom = 0; jTo = n - 1; }
            else
            {
                double half = Math.Asin(Math.Min(1.0, (range + 2.0 * s) / ri));
                double rotated = thetaC - phase;
                jFrom = (long)Math.Floor((rotated - half) / step) - 1;
                jTo = (long)Math.Floor((rotated + half) / step) + 1;
            }
            for (long jj = jFrom; jj <= jTo; jj++)
            {
                long j = ((jj % n) + n) % n;
                for (long k = kFrom; k <= kTo; k++)
                {
                    if (++cells > MaxCells) return;
                    ulong key = Key(seed, i, j, k);
                    double rr = (i + 0.2 + 0.6 * Unit(key, 1)) * s;
                    double theta = phase + (jj + 0.2 + 0.6 * Unit(key, 2)) * step;
                    double z = (k + 0.2 + 0.6 * Unit(key, 3)) * s;
                    var p = new Double3(rr * Math.Cos(theta), rr * Math.Sin(theta), z);
                    double chance = share * shape.Chance(p);
                    if (chance <= 0.0 || Unit(key, 4) >= chance) continue;
                    double u = Unit(key, 5);
                    double radius = real
                        ? Math.Min(RealMinRadius * Math.Pow(Math.Max(1.0 - u, 1e-12), -1.0 / 2.5), RealMaxRadius)
                        : largest * (1.0 + (0.2 + 0.8 * u * u * u - 1.0) * Math.Min(Math.Max(sizeSpread, 0.0), 1.0));
                    if ((p - center).Length > range + radius) continue;
                    var v = new Double3(-omega * p.Y, omega * p.X, 0.0);
                    into.Add(new SolidRock((long)(key & 0x7FFFFFFFFFFFFFFFUL), p, v, radius, omega));
                }
            }
        }
    }

    /// <summary>A seed for an object's rocks, from its body's name and slot (FNV-1a), so each object's field differs.</summary>
    public static ulong Seed(string body, int slot)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in (body ?? string.Empty) + "#" + slot) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    private static ulong Key(ulong seed, long i, long j, long k) =>
        Mix(Mix(Mix(seed ^ (ulong)i) ^ (ulong)j * 0x9E3779B97F4A7C15UL) ^ (ulong)k * 0xC2B2AE3D27D4EB4FUL);

    /// <summary>The <paramref name="n"/>th uniform number in [0,1) from a cell's key.</summary>
    private static double Unit(ulong key, int n) => (Mix(key + (ulong)n * 0x632BE59BD9B4E019UL) >> 11) * (1.0 / (1UL << 53));

    /// <summary>SplitMix64's finaliser: every bit of the input spread over the output.</summary>
    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
