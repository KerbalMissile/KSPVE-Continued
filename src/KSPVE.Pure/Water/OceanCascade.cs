using System;
using KSPVE.Pure.Space.Procedural;

namespace KSPVE.Pure.Water;

/// <summary>A power-of-two complex FFT (radix 2, in place).</summary>
public static class Fft
{
    /// <summary>
    /// The unnormalised inverse transform x[n] = sum_m X[m] exp(+2 pi i m n / N) of a line of <paramref name="count"/> values
    /// at <paramref name="offset"/> with stride <paramref name="stride"/>.
    /// </summary>
    public static void Inverse(double[] re, double[] im, int offset, int stride, int count)
    {
        int j = 0;
        for (int i = 1; i < count; i++)
        {
            int bit = count >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                int a = offset + i * stride, b = offset + j * stride;
                (re[a], re[b]) = (re[b], re[a]);
                (im[a], im[b]) = (im[b], im[a]);
            }
        }
        for (int len = 2; len <= count; len <<= 1)
        {
            double ang = 2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < count; i += len)
            {
                double cr = 1.0, ci = 0.0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = offset + (i + k) * stride, b = offset + (i + k + len / 2) * stride;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double nr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = nr;
                }
            }
        }
    }

    /// <summary>The unnormalised 2D inverse transform of an n x n array (rows, then columns).</summary>
    public static void Inverse2D(double[] re, double[] im, int n)
    {
        for (int y = 0; y < n; y++) Inverse(re, im, y * n, 1, n);
        for (int x = 0; x < n; x++) Inverse(re, im, x, n, n);
    }
}

/// <summary>The fields of one cascade at one moment (n x n, x fastest), each a function of the horizontal position.</summary>
public sealed class CascadeField
{
    public readonly int N;
    /// <summary>Height and the horizontal displacement (before the choppiness is applied) along x and z.</summary>
    public readonly double[] Height, DispX, DispZ;
    /// <summary>The slopes dh/dx and dh/dz.</summary>
    public readonly double[] SlopeX, SlopeZ;
    /// <summary>The displacement's gradient: dDx/dx, dDz/dz and dDx/dz.</summary>
    public readonly double[] Dxx, Dzz, Dxz;

    public CascadeField(int n)
    {
        N = n;
        Height = new double[n * n]; DispX = new double[n * n]; DispZ = new double[n * n];
        SlopeX = new double[n * n]; SlopeZ = new double[n * n];
        Dxx = new double[n * n]; Dzz = new double[n * n]; Dxz = new double[n * n];
    }
}

/// <summary>
/// One band of the ocean's height field as Tessendorf (2001) builds it: the sea's spectrum sampled at random on a periodic
/// square of side <see cref="Length"/>, h(k, t) = h0(k) e^(i w t) + conj(h0(-k)) e^(-i w t), then Fourier transformed. The
/// angular frequencies are rounded to multiples of 2 pi / <see cref="Period"/>, so the sea repeats exactly every Period seconds
/// (and the game can keep time modulo that, exact in single precision for ever). The same maths runs on the GPU for the game;
/// this CPU copy is for tests and the preview.
/// </summary>
public sealed class OceanCascade
{
    /// <summary>The sea repeats every this many seconds.</summary>
    public const double Period = 600.0;

    public readonly int N;
    public readonly double Length;
    public readonly double KMin, KMax;
    public readonly double Gravity, Depth, Tension;
    private readonly double[] _h0re, _h0im, _omega, _kx, _kz, _kk;

    /// <summary>
    /// Builds the band holding the wave numbers from <paramref name="kMin"/> to <paramref name="kMax"/> (rad/m) of a sea
    /// (<paramref name="wind"/> in m/s at 10 m, <paramref name="windAngle"/> the direction it blows towards in the x-z plane,
    /// radians from +x), drawing the random amplitudes from <paramref name="seed"/>.
    /// </summary>
    public OceanCascade(int n, double length, double kMin, double kMax, double wind, double windAngle, double fetch, double gravity, double depth, ulong seed, double tension = OceanSpectrum.WaterSurfaceTension)
    {
        N = n; Length = length; KMin = kMin; KMax = kMax; Gravity = gravity; Depth = depth; Tension = tension;
        int count = n * n;
        _h0re = new double[count]; _h0im = new double[count]; _omega = new double[count];
        _kx = new double[count]; _kz = new double[count]; _kk = new double[count];
        var rng = new Pcg32(seed, 54);
        double dk = 2.0 * Math.PI / length, w0 = 2.0 * Math.PI / Period;
        double wp = OceanSpectrum.PeakFrequency(Math.Max(wind, 0.1), fetch, gravity);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                int mx = x < n / 2 ? x : x - n, mz = y < n / 2 ? y : y - n;
                double kx = mx * dk, kz = mz * dk, k = Math.Sqrt(kx * kx + kz * kz);
                _kx[i] = kx; _kz[i] = kz; _kk[i] = k;
                // The random draws are made for every texel so the pattern does not shift when the band edges move.
                double gr = rng.NextGaussian(), gi = rng.NextGaussian();
                if (k <= 0.0 || k < kMin || k >= kMax || x == n / 2 || y == n / 2) continue;
                double w = OceanSpectrum.Dispersion(k, depth, gravity, tension);
                _omega[i] = Math.Max(Math.Round(w / w0), 1.0) * w0;
                double theta = Math.Atan2(kz, kx) - windAngle;
                double e = SpectrumAt(k, theta, wind, fetch, depth, gravity, wp);
                double amp = 0.5 * dk * Math.Sqrt(e);
                _h0re[i] = gr * amp; _h0im[i] = gi * amp;
            }
        }
    }

    private static double SpectrumAt(double k, double theta, double wind, double fetch, double depth, double gravity, double wp)
    {
        if (wind < 0.05) return 0.0;
        double w = OceanSpectrum.Dispersion(k, depth, gravity);
        double s = OceanSpectrum.SpreadExponent(w, wp, wind, gravity);
        return OceanSpectrum.Jonswap(w, wind, fetch, gravity) * OceanSpectrum.Directional(theta, s) * OceanSpectrum.GroupVelocity(k, depth, gravity) / k;
    }

    /// <summary>The sum over the band of the spectrum times the squared wave-number cell, which the field's variance should match.</summary>
    public double ExpectedVariance()
    {
        double dk = 2.0 * Math.PI / Length, sum = 0.0;
        for (int i = 0; i < N * N; i++) sum += 2.0 * (_h0re[i] * _h0re[i] + _h0im[i] * _h0im[i]);
        return sum;
    }

    /// <summary>
    /// Writes the band's initial spectrum as four floats a texel (h0(k) real and imaginary, then h0(-k) real and imaginary),
    /// in the order the compute shader reads: x fastest, from <paramref name="offset"/>.
    /// </summary>
    public void CopyH0(float[] destination, int offset)
    {
        int n = N;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x, j = ((n - y) % n) * n + (n - x) % n;
                int o = offset + 4 * i;
                destination[o] = (float)_h0re[i]; destination[o + 1] = (float)_h0im[i];
                destination[o + 2] = (float)_h0re[j]; destination[o + 3] = (float)_h0im[j];
            }
        }
    }

    /// <summary>Evaluates the fields at time <paramref name="t"/> (seconds).</summary>
    public void Evaluate(double t, CascadeField f)
    {
        int n = N, count = n * n;
        var re = new double[8][];
        var im = new double[8][];
        for (int c = 0; c < 8; c++) { re[c] = new double[count]; im[c] = new double[count]; }
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                int nx = (n - x) % n, nz = (n - y) % n, j = nz * n + nx;   // the index of -k
                double k = _kk[i];
                if (k <= 0.0) continue;
                double c = Math.Cos(_omega[i] * t), s = Math.Sin(_omega[i] * t);
                // h0(k) e^(iwt) + conj(h0(-k)) e^(-iwt)
                double hr = _h0re[i] * c - _h0im[i] * s + _h0re[j] * c - _h0im[j] * s;
                double hi = _h0re[i] * s + _h0im[i] * c - _h0re[j] * s - _h0im[j] * c;
                double ux = _kx[i] / k, uz = _kz[i] / k;
                // height; displacement D = i (k/|k|) h; slope = i k h; gradient of D = -(k k / |k|) h
                Set(re, im, 0, i, hr, hi);
                Set(re, im, 1, i, -ux * hi, ux * hr);
                Set(re, im, 2, i, -uz * hi, uz * hr);
                Set(re, im, 3, i, -_kx[i] * hi, _kx[i] * hr);
                Set(re, im, 4, i, -_kz[i] * hi, _kz[i] * hr);
                Set(re, im, 5, i, -ux * _kx[i] * hr, -ux * _kx[i] * hi);
                Set(re, im, 6, i, -uz * _kz[i] * hr, -uz * _kz[i] * hi);
                Set(re, im, 7, i, -ux * _kz[i] * hr, -ux * _kz[i] * hi);
            }
        }
        double[][] outs = { f.Height, f.DispX, f.DispZ, f.SlopeX, f.SlopeZ, f.Dxx, f.Dzz, f.Dxz };
        for (int c = 0; c < 8; c++)
        {
            Fft.Inverse2D(re[c], im[c], n);
            Array.Copy(re[c], outs[c], count);
        }
    }

    private static void Set(double[][] re, double[][] im, int c, int i, double r, double m) { re[c][i] = r; im[c][i] = m; }

    /// <summary>
    /// The Jacobian of the mapping x -> x + lambda D(x) at a texel: below 0 the surface folds over itself (a breaking
    /// wave), and the whitecaps form where it falls under the threshold from <see cref="Whitecaps.JacobianThreshold"/>.
    /// </summary>
    public static double Jacobian(CascadeField f, int i, double lambda)
    {
        double a = 1.0 + lambda * f.Dxx[i], d = 1.0 + lambda * f.Dzz[i], b = lambda * f.Dxz[i];
        return a * d - b * b;
    }
}
