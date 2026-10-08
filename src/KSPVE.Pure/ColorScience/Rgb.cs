using System;
using System.Globalization;

namespace KSPVE.Pure.ColorScience;

/// <summary>Linear RGB triple (Rec.709 primaries unless stated otherwise).</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly double R;
    public readonly double G;
    public readonly double B;

    public Rgb(double r, double g, double b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Rec.709 relative luminance Y = 0.2126 R + 0.7152 G + 0.0722 B.</summary>
    public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;

    public double MaxComponent => Math.Max(R, Math.Max(G, B));

    public static Rgb operator +(Rgb a, Rgb b) => new Rgb(a.R + b.R, a.G + b.G, a.B + b.B);
    public static Rgb operator -(Rgb a, Rgb b) => new Rgb(a.R - b.R, a.G - b.G, a.B - b.B);
    public static Rgb operator -(Rgb a) => new Rgb(-a.R, -a.G, -a.B);
    public static Rgb operator *(Rgb a, Rgb b) => new Rgb(a.R * b.R, a.G * b.G, a.B * b.B);
    public static Rgb operator /(Rgb a, Rgb b) => new Rgb(a.R / b.R, a.G / b.G, a.B / b.B);
    public static Rgb operator *(Rgb a, double s) => new Rgb(a.R * s, a.G * s, a.B * s);
    public static Rgb operator *(double s, Rgb a) => new Rgb(a.R * s, a.G * s, a.B * s);
    public static Rgb operator /(Rgb a, double s) => new Rgb(a.R / s, a.G / s, a.B / s);

    public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);
    public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);

    public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object obj) => obj is Rgb other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = R.GetHashCode();
            h = h * 397 ^ G.GetHashCode();
            h = h * 397 ^ B.GetHashCode();
            return h;
        }
    }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", R, G, B);
}
