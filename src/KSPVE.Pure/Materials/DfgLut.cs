using System;

namespace KSPVE.Pure.Materials;

/// <summary>Split-sum environment BRDF (Karis 2013, "Real Shading in Unreal Engine 4").</summary>
public static class DfgLut
{
    /// <summary>
    /// Returns scale A and bias B so that the specular albedo is F0 * A + B (F90 = 1).
    /// Uses the height-correlated Smith visibility (as Filament's DFG bake).
    /// </summary>
    public static (double A, double B) Integrate(double nDotV, double perceptualRoughness, int sampleCount)
    {
        nDotV = Math.Max(nDotV, 1e-4);
        double alpha = Math.Max(perceptualRoughness * perceptualRoughness, 1e-6);
        double vx = Math.Sqrt(1.0 - nDotV * nDotV), vz = nDotV;
        double a = 0.0, b = 0.0;
        for (int i = 0; i < sampleCount; i++)
        {
            var (u, v) = Sampling.Hammersley(i, sampleCount);
            var h = Ggx.SampleHalfVector(u, v, alpha);
            double vDotH = vx * h.X + vz * h.Z;
            // L = 2 (V.H) H - V
            double lz = 2.0 * vDotH * h.Z - vz;
            double nDotL = lz;
            double nDotH = h.Z;
            if (nDotL <= 0.0 || vDotH <= 0.0) continue;
            // pdf(L) = D nDotH / (4 vDotH); integrand = D V 4 nDotL (times F): ratio = V 4 nDotL vDotH / nDotH
            double vis = Smith.VisibilityHeightCorrelated(nDotV, nDotL, alpha);
            double visV = vis * 4.0 * nDotL * vDotH / nDotH;
            double fc = Math.Pow(1.0 - vDotH, 5.0);
            a += (1.0 - fc) * visV;
            b += fc * visV;
        }
        return (a / sampleCount, b / sampleCount);
    }

    /// <summary>Row-major LUT, x = nDotV, y = perceptualRoughness (texel centres), 2 floats (A, B) per texel.</summary>
    public static float[] Bake(int size, int sampleCount)
    {
        var data = new float[size * size * 2];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var (a, b) = Integrate((x + 0.5) / size, (y + 0.5) / size, sampleCount);
            int idx = (y * size + x) * 2;
            data[idx] = (float)a;
            data[idx + 1] = (float)b;
        }
        return data;
    }
}

/// <summary>Multiple-scattering energy compensation (Kulla &amp; Conty 2017; Fdez-Aguera 2019; Filament).</summary>
public static class EnergyCompensation
{
    /// <summary>Single-scatter directional albedo E(nDotV) for F0 = 1 (= A + B).</summary>
    public static double DirectionalAlbedo(double nDotV, double perceptualRoughness, int sampleCount)
    {
        var (a, b) = DfgLut.Integrate(nDotV, perceptualRoughness, sampleCount);
        return a + b;
    }

    /// <summary>Filament: 1 + F0 (1/E - 1); multiply the single-scatter specular lobe by this.</summary>
    public static double MultiScatterFactor(double f0, double directionalAlbedo) =>
        1.0 + f0 * (1.0 / directionalAlbedo - 1.0);
}
