using System;

namespace KSPVE.Pure.ColorScience;

internal static class Mat3
{
    /// <summary>Row-major 3x3 times column vector.</summary>
    public static Rgb Mul(double[,] m, Rgb v) => new Rgb(
        m[0, 0] * v.R + m[0, 1] * v.G + m[0, 2] * v.B,
        m[1, 0] * v.R + m[1, 1] * v.G + m[1, 2] * v.B,
        m[2, 0] * v.R + m[2, 1] * v.G + m[2, 2] * v.B);

    public static double Saturate(double x) => x < 0.0 ? 0.0 : (x > 1.0 ? 1.0 : x);

    public static Rgb Saturate(Rgb c) => new Rgb(Saturate(c.R), Saturate(c.G), Saturate(c.B));
}

/// <summary>
/// ACES fitted curve by Stephen Hill (MJP/BakingLab ACES.hlsl): sRGB to AP1-ish RRT input,
/// rational RRT+ODT fit, then output matrix. No 1/0.6 exposure pre-scale is applied here.
/// </summary>
public static class AcesFitted
{
    // sRGB => XYZ => D65_2_D60 => AP1 => RRT_SAT (BakingLab, row-major as used with mul(M, v)).
    private static readonly double[,] InputMat =
    {
        { 0.59719, 0.35458, 0.04823 },
        { 0.07600, 0.90834, 0.01566 },
        { 0.02840, 0.13383, 0.83777 },
    };

    // ODT_SAT => XYZ => D60_2_D65 => sRGB
    private static readonly double[,] OutputMat =
    {
        {  1.60475, -0.53108, -0.07367 },
        { -0.10208,  1.10813, -0.00605 },
        { -0.00327, -0.07276,  1.07602 },
    };

    public static double RrtAndOdtFit(double v)
    {
        double a = v * (v + 0.0245786) - 0.000090537;
        double b = v * (0.983729 * v + 0.4329510) + 0.238081;
        return a / b;
    }

    public static Rgb Apply(Rgb color)
    {
        Rgb v = Mat3.Mul(InputMat, color);
        v = new Rgb(RrtAndOdtFit(v.R), RrtAndOdtFit(v.G), RrtAndOdtFit(v.B));
        return Mat3.Saturate(Mat3.Mul(OutputMat, v));
    }
}

/// <summary>
/// Khronos PBR Neutral tone mapper (2024), line-for-line with KhronosGroup/ToneMapping PBR_Neutral/pbrNeutral.glsl.
/// Like the reference, there is no final clamp: non-negative input yields output in [0, 1) by construction.
/// </summary>
public static class KhronosPbrNeutral
{
    private const double StartCompression = 0.8 - 0.04;
    private const double Desaturation = 0.15;

    public static Rgb Apply(Rgb color)
    {
        double x = Math.Min(color.R, Math.Min(color.G, color.B));
        double offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
        color -= new Rgb(offset, offset, offset);

        double peak = color.MaxComponent;
        if (peak < StartCompression) return color;

        const double d = 1.0 - StartCompression;
        double newPeak = 1.0 - d * d / (peak + d - StartCompression);
        color *= newPeak / peak;

        double g = 1.0 - 1.0 / (Desaturation * (peak - newPeak) + 1.0);
        // mix(color, newPeak * (1,1,1), g)
        Rgb white = new Rgb(newPeak, newPeak, newPeak);
        return color + (white - color) * g;
    }
}

/// <summary>
/// "Minimal AgX" approximation (iolite engine blog, after Troy Sobotka's AgX as shipped in Blender).
/// Base look only; output is display-linear (the 2.2 power EOTF of the reference is applied).
/// </summary>
public static class AgX
{
    private const double MinEv = -12.47393;
    private const double MaxEv = 4.026069;

    // Row-major. The reference GLSL mat3 constructor is column-major, so its argument list is the transpose of this;
    // verified against the iolite 'Minimal AgX' listing (agx_mat / agx_mat_inv). Each row sums to ~1.
    private static readonly double[,] InputMat =
    {
        { 0.842479062253094, 0.0784335999999992, 0.0792237451477643 },
        { 0.0423282422610123, 0.878468636469772, 0.0791661274605434 },
        { 0.0423756549057051, 0.0784336, 0.879142973793104 },
    };

    private static readonly double[,] InverseMat =
    {
        { 1.19687900512017, -0.0980208811401368, -0.0990297440797205 },
        { -0.0528968517574562, 1.15190312990417, -0.0989611768448433 },
        { -0.0529716355144438, -0.0980434501171241, 1.15107367264116 },
    };

    /// <summary>6th-order polynomial fit of the AgX base contrast sigmoid (x in [0,1]).</summary>
    public static double ContrastApprox(double x)
    {
        double x2 = x * x;
        double x4 = x2 * x2;
        return 15.5 * x4 * x2
             - 40.14 * x4 * x
             + 31.96 * x4
             - 6.868 * x2 * x
             + 0.4298 * x2
             + 0.1191 * x
             - 0.00232;
    }

    private static double Encode(double v)
    {
        double l2 = v > 0.0 ? Math.Log(v, 2.0) : MinEv;
        l2 = Math.Max(MinEv, Math.Min(MaxEv, l2));
        return ContrastApprox((l2 - MinEv) / (MaxEv - MinEv));
    }

    public static Rgb Apply(Rgb color)
    {
        Rgb v = Mat3.Mul(InputMat, color);
        v = new Rgb(Encode(v.R), Encode(v.G), Encode(v.B));
        v = Mat3.Mul(InverseMat, v);
        // Reference applies pow(v, 2.2) with no clamp (pow of a negative is undefined in GLSL); we saturate first.
        return new Rgb(
            Math.Pow(Mat3.Saturate(v.R), 2.2),
            Math.Pow(Mat3.Saturate(v.G), 2.2),
            Math.Pow(Mat3.Saturate(v.B), 2.2));
    }
}
