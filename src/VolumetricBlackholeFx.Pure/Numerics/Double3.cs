using System;
using System.Globalization;

namespace VolumetricBlackholeFx.Pure.Numerics;

/// <summary>Double-precision 3D vector shared by all VolumetricBlackholeFx.Pure maths (no Unity dependency).</summary>
public readonly struct Double3 : IEquatable<Double3>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Double3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Double3 Zero => new Double3(0, 0, 0);
    public static Double3 UnitX => new Double3(1, 0, 0);
    public static Double3 UnitY => new Double3(0, 1, 0);
    public static Double3 UnitZ => new Double3(0, 0, 1);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>Unit vector in the same direction; zero stays zero.</summary>
    public Double3 Normalized()
    {
        double length = Length;
        return length > 0.0 ? this / length : Zero;
    }

    public static double Dot(Double3 a, Double3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Double3 Cross(Double3 a, Double3 b) =>
        new Double3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static Double3 Lerp(Double3 a, Double3 b, double t) => a + (b - a) * t;

    public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Double3 operator -(Double3 a) => new Double3(-a.X, -a.Y, -a.Z);
    public static Double3 operator *(Double3 a, double s) => new Double3(a.X * s, a.Y * s, a.Z * s);
    public static Double3 operator *(double s, Double3 a) => a * s;
    public static Double3 operator /(Double3 a, double s) => new Double3(a.X / s, a.Y / s, a.Z / s);

    public bool Equals(Double3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object obj) => obj is Double3 other && Equals(other);
    public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
    public static bool operator ==(Double3 a, Double3 b) => a.Equals(b);
    public static bool operator !=(Double3 a, Double3 b) => !a.Equals(b);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", X, Y, Z);
}
