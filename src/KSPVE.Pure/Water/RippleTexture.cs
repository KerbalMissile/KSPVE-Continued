using System;
using KSPVE.Pure.Space.Procedural;

namespace KSPVE.Pure.Water;

/// <summary>
/// A tileable field of small ripples for the shaders that animate rivers and lakes (the flow map slides it along the water): a
/// random height field with a power-law spectrum, as the ocean's, between 3 and 24 waves across, transformed with the same FFT;
/// the result is its slopes.
/// </summary>
public static class RippleTexture
{
    /// <summary>
    /// The slopes (x, then y) of an n x n field, scaled to unit standard deviation and then divided by 3 and clamped to -1..1, as
    /// 2 n^2 floats (x slopes, then y slopes). The shader multiplies by the strength it wants.
    /// </summary>
    public static float[] Generate(int n, ulong seed)
    {
        var re = new double[n * n];
        var im = new double[n * n];
        var sxRe = new double[n * n]; var sxIm = new double[n * n];
        var syRe = new double[n * n]; var syIm = new double[n * n];
        var rng = new Pcg32(seed, 91);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int mx = x < n / 2 ? x : x - n, my = y < n / 2 ? y : y - n;
                double k = Math.Sqrt(mx * mx + my * my);
                double a = rng.NextGaussian(), b = rng.NextGaussian();
                if (k < 3.0 || k > 24.0) continue;
                // Capillary-gravity ripples fall off about as k^-2.5 in height (a slope spectrum near k^-1).
                double amp = Math.Pow(k, -2.5);
                int i = y * n + x;
                re[i] = a * amp; im[i] = b * amp;
                // slope = i k h
                sxRe[i] = -mx * im[i]; sxIm[i] = mx * re[i];
                syRe[i] = -my * im[i]; syIm[i] = my * re[i];
            }
        }
        // Real fields need a Hermitian spectrum: take the real part of the transform of the symmetrised spectrum.
        Fft.Inverse2D(sxRe, sxIm, n);
        Fft.Inverse2D(syRe, syIm, n);
        var result = new float[2 * n * n];
        double sum = 0.0;
        for (int i = 0; i < n * n; i++) sum += sxRe[i] * sxRe[i] + syRe[i] * syRe[i];
        double std = Math.Sqrt(sum / (2.0 * n * n));
        for (int i = 0; i < n * n; i++)
        {
            result[i] = (float)Math.Max(-1.0, Math.Min(1.0, sxRe[i] / std / 3.0));
            result[n * n + i] = (float)Math.Max(-1.0, Math.Min(1.0, syRe[i] / std / 3.0));
        }
        return result;
    }
}
