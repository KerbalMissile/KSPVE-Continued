using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Materials;

/// <summary>GGX / Trowbridge-Reitz normal distribution. alpha = perceptualRoughness^2.</summary>
public static class Ggx
{
    /// <summary>Isotropic D(h); Walter et al. 2007 eq. 33. Integrates to 1 against nDotH over the hemisphere.</summary>
    public static double D(double nDotH, double alpha)
    {
        double a2 = alpha * alpha;
        double d = nDotH * nDotH * (a2 - 1.0) + 1.0;
        return a2 / (Math.PI * d * d);
    }

    /// <summary>Anisotropic D(h) in tangent space; Burley 2012 (Disney course notes), eq. 13.</summary>
    public static double DAnisotropic(double nDotH, double hDotT, double hDotB, double alphaT, double alphaB)
    {
        double t = hDotT / alphaT;
        double b = hDotB / alphaB;
        double d = t * t + b * b + nDotH * nDotH;
        return 1.0 / (Math.PI * alphaT * alphaB * d * d);
    }

    /// <summary>
    /// Importance-samples a GGX half vector (tangent space, N = +Z), pdf = D(h) nDotH.
    /// Walter 2007 eq. 35-36 / Karis 2013 "ImportanceSampleGGX".
    /// </summary>
    public static Double3 SampleHalfVector(double u1, double u2, double alpha)
    {
        double phi = 2.0 * Math.PI * u1;
        double cosTheta = Math.Sqrt((1.0 - u2) / (1.0 + (alpha * alpha - 1.0) * u2));
        double sinTheta = Math.Sqrt(Math.Max(0.0, 1.0 - cosTheta * cosTheta));
        return new Double3(sinTheta * Math.Cos(phi), sinTheta * Math.Sin(phi), cosTheta);
    }
}

/// <summary>Smith masking-shadowing.</summary>
public static class Smith
{
    /// <summary>
    /// Height-correlated visibility V = G / (4 nDotV nDotL); Heitz 2014 "Understanding the
    /// Masking-Shadowing Function", eq. 99, in the Filament formulation (Guy &amp; Agopian).
    /// </summary>
    public static double VisibilityHeightCorrelated(double nDotV, double nDotL, double alpha)
    {
        double a2 = alpha * alpha;
        double ggxV = nDotL * Math.Sqrt(nDotV * nDotV * (1.0 - a2) + a2);
        double ggxL = nDotV * Math.Sqrt(nDotL * nDotL * (1.0 - a2) + a2);
        return 0.5 / (ggxV + ggxL);
    }
}

public static class Fresnel
{
    /// <summary>Schlick 1994 with F90 = 1.</summary>
    public static double Schlick(double f0, double vDotH) => SchlickF90(f0, 1.0, vDotH);

    public static double SchlickF90(double f0, double f90, double vDotH)
    {
        double m = 1.0 - vDotH;
        double m2 = m * m;
        return f0 + (f90 - f0) * (m2 * m2 * m);
    }
}

public static class Diffuse
{
    public static double Lambert() => 1.0 / Math.PI;

    /// <summary>Burley 2012 (Disney) diffuse, fd90 = 0.5 + 2 roughness lDotH^2, divided by pi (as in Filament).</summary>
    public static double Burley(double nDotV, double nDotL, double lDotH, double perceptualRoughness)
    {
        double fd90 = 0.5 + 2.0 * perceptualRoughness * lDotH * lDotH;
        double lightScatter = 1.0 + (fd90 - 1.0) * Pow5(1.0 - nDotL);
        double viewScatter = 1.0 + (fd90 - 1.0) * Pow5(1.0 - nDotV);
        return lightScatter * viewScatter / Math.PI;
    }

    private static double Pow5(double x)
    {
        double x2 = x * x;
        return x2 * x2 * x;
    }
}

public static class Sampling
{
    /// <summary>Van der Corput sequence (bit reversal); Hammersley 1960 / Karis 2013.</summary>
    public static double RadicalInverseBase2(uint i)
    {
        i = (i << 16) | (i >> 16);
        i = ((i & 0x55555555u) << 1) | ((i & 0xAAAAAAAAu) >> 1);
        i = ((i & 0x33333333u) << 2) | ((i & 0xCCCCCCCCu) >> 2);
        i = ((i & 0x0F0F0F0Fu) << 4) | ((i & 0xF0F0F0F0u) >> 4);
        i = ((i & 0x00FF00FFu) << 8) | ((i & 0xFF00FF00u) >> 8);
        return i * 2.3283064365386963e-10; // 1 / 2^32
    }

    public static (double u, double v) Hammersley(int i, int count) =>
        ((double)i / count, RadicalInverseBase2((uint)i));

    /// <summary>Malley's method: cosine-weighted hemisphere direction, N = +Z.</summary>
    public static Double3 CosineHemisphere(double u1, double u2)
    {
        double r = Math.Sqrt(u1);
        double phi = 2.0 * Math.PI * u2;
        return new Double3(r * Math.Cos(phi), r * Math.Sin(phi), Math.Sqrt(Math.Max(0.0, 1.0 - u1)));
    }
}
