using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.ColorScience;

/// <summary>Bradford chromatic adaptation (Lindbloom, "Chromatic Adaptation").</summary>
public static class ChromaticAdaptation
{
    private static readonly double[,] Bradford =
    {
        {  0.8951000,  0.2664000, -0.1614000 },
        { -0.7502000,  1.7135000,  0.0367000 },
        {  0.0389000, -0.0685000,  1.0296000 },
    };

    /// <summary>XYZ to XYZ matrix (row-major, applied to column vectors) adapting from the source to the destination white.</summary>
    public static double[,] BradfordMatrix(Double3 sourceWhiteXyz, Double3 destWhiteXyz)
    {
        double[] s = Mul(Bradford, sourceWhiteXyz.X, sourceWhiteXyz.Y, sourceWhiteXyz.Z);
        double[] d = Mul(Bradford, destWhiteXyz.X, destWhiteXyz.Y, destWhiteXyz.Z);
        double[,] scale =
        {
            { d[0] / s[0], 0, 0 },
            { 0, d[1] / s[1], 0 },
            { 0, 0, d[2] / s[2] },
        };
        return Multiply(Invert(Bradford), Multiply(scale, Bradford));
    }

    /// <summary>
    /// Re-balances a colour captured under a blackbody of sourceKelvin so that this illuminant becomes targetKelvin
    /// (default 6504 K, near D65). Whites come from the Planckian locus (Kim et al.) with Y = 1.
    /// </summary>
    public static Rgb WhiteBalance(Rgb linearRec709, double sourceKelvin, double targetKelvin = 6504.0)
    {
        double[,] m = BradfordMatrix(ColorTemperature.PlanckianXyz(sourceKelvin), ColorTemperature.PlanckianXyz(targetKelvin));
        Double3 xyz = ColorTemperature.LinearRec709ToXyz(linearRec709);
        double[] a = Mul(m, xyz.X, xyz.Y, xyz.Z);
        return ColorTemperature.XyzToLinearRec709(a[0], a[1], a[2]);
    }

    private static double[] Mul(double[,] m, double x, double y, double z) => new[]
    {
        m[0, 0] * x + m[0, 1] * y + m[0, 2] * z,
        m[1, 0] * x + m[1, 1] * y + m[1, 2] * z,
        m[2, 0] * x + m[2, 1] * y + m[2, 2] * z,
    };

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    private static double[,] Invert(double[,] m)
    {
        double a = m[0, 0], b = m[0, 1], c = m[0, 2];
        double d = m[1, 0], e = m[1, 1], f = m[1, 2];
        double g = m[2, 0], h = m[2, 1], i = m[2, 2];
        double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        double inv = 1.0 / det;
        return new[,]
        {
            { (e * i - f * h) * inv, (c * h - b * i) * inv, (b * f - c * e) * inv },
            { (f * g - d * i) * inv, (a * i - c * g) * inv, (c * d - a * f) * inv },
            { (d * h - e * g) * inv, (b * g - a * h) * inv, (a * e - b * d) * inv },
        };
    }
}
