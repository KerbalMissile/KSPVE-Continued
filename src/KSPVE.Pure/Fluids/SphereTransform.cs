using System;
using System.Threading.Tasks;

namespace KSPVE.Pure.Fluids;

/// <summary>
/// Spherical-harmonic bookkeeping for a triangular truncation K: coefficient (m, l) with 0 &lt;= m &lt;= l &lt;= K+1 lives at
/// <see cref="Index"/>. The layer runs one degree past K because the derivative operators below raise the degree by one.
/// The harmonics are P(l,m)(mu) e^(i m lambda) with the Legendre functions normalised so that their square integrates to 1
/// over mu in [-1, 1]; a real field is then f = sum_l c(l,0) P(l,0) + 2 Re sum_(m&gt;0) sum_l c(l,m) P(l,m) e^(i m lambda).
/// </summary>
public sealed class SpectralLayout
{
    public readonly int K;
    private readonly int[] _offset;
    public readonly int Size;

    public SpectralLayout(int k)
    {
        if (k < 2) throw new ArgumentOutOfRangeException(nameof(k));
        K = k;
        _offset = new int[k + 2];
        int n = 0;
        for (int m = 0; m <= k; m++)
        {
            _offset[m] = n;
            n += k + 2 - m;
        }
        _offset[k + 1] = n;
        Size = n;
    }

    /// <summary>Position of coefficient (l, m), m &lt;= l &lt;= K+1.</summary>
    public int Index(int l, int m) => _offset[m] + (l - m);

    /// <summary>The weight a coefficient has in sums over the sphere: 1 for m = 0, 2 for the pair +-m.</summary>
    public static double MWeight(int m) => m == 0 ? 1.0 : 2.0;

    /// <summary>The ratio eps(l, m) = sqrt((l^2 - m^2) / (4 l^2 - 1)) of the Legendre recurrences.</summary>
    public static double Eps(int l, int m) => l <= 0 ? 0.0 : Math.Sqrt(Math.Max(0.0, ((double)l * l - (double)m * m) / (4.0 * l * l - 1.0)));
}

/// <summary>Gauss-Legendre nodes and weights on [-1, 1], ascending, found by Newton's method on P_n.</summary>
public static class GaussLegendre
{
    public static void Compute(int n, out double[] nodes, out double[] weights)
    {
        nodes = new double[n];
        weights = new double[n];
        int half = (n + 1) / 2;
        for (int i = 0; i < half; i++)
        {
            double x = Math.Cos(Math.PI * (i + 0.75) / (n + 0.5));
            double dp = 1.0;
            for (int iter = 0; iter < 100; iter++)
            {
                double p0 = 1.0, p1 = x;
                for (int k = 2; k <= n; k++)
                {
                    double p2 = ((2.0 * k - 1.0) * x * p1 - (k - 1.0) * p0) / k;
                    p0 = p1;
                    p1 = p2;
                }
                dp = n * (x * p1 - p0) / (x * x - 1.0);
                double dx = p1 / dp;
                x -= dx;
                if (Math.Abs(dx) < 1e-15) break;
            }
            double w = 2.0 / ((1.0 - x * x) * dp * dp);
            nodes[i] = -x;
            nodes[n - 1 - i] = x;
            weights[i] = w;
            weights[n - 1 - i] = w;
        }
    }
}

/// <summary>
/// The normalised associated Legendre functions P(l,m)(mu), l up to K+1, tabulated at a list of latitudes (mu = sin latitude).
/// Built with the standard stable recurrence in l from the sectoral value P(m,m).
/// </summary>
public sealed class LegendreTable
{
    public readonly SpectralLayout Layout;
    public readonly double[] Mu;
    /// <summary>Values: row j (latitude) is [j * Layout.Size, (j+1) * Layout.Size).</summary>
    public readonly double[] P;

    public int Count => Mu.Length;

    public LegendreTable(SpectralLayout layout, double[] mu)
    {
        Layout = layout;
        Mu = mu;
        int k = layout.K;
        P = new double[mu.Length * layout.Size];
        for (int j = 0; j < mu.Length; j++)
        {
            double x = mu[j];
            double cosLat = Math.Sqrt(Math.Max(0.0, 1.0 - x * x));
            int row = j * layout.Size;
            double pmm = Math.Sqrt(0.5);
            for (int m = 0; m <= k; m++)
            {
                if (m > 0) pmm *= Math.Sqrt((2.0 * m + 1.0) / (2.0 * m)) * cosLat;
                double prev2 = 0.0, prev1 = pmm;
                P[row + layout.Index(m, m)] = pmm;
                for (int l = m + 1; l <= k + 1; l++)
                {
                    double eps = SpectralLayout.Eps(l, m), epsPrev = SpectralLayout.Eps(l - 1, m);
                    double value = (x * prev1 - epsPrev * prev2) / eps;
                    P[row + layout.Index(l, m)] = value;
                    prev2 = prev1;
                    prev1 = value;
                }
            }
        }
    }
}

/// <summary>A radix-2 complex fast Fourier transform on separate real and imaginary arrays.</summary>
public sealed class Fft
{
    public readonly int N;
    private readonly double[] _cos, _sin;
    private readonly int[] _reverse;

    public Fft(int n)
    {
        if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("The length must be a power of two.", nameof(n));
        N = n;
        _cos = new double[n / 2];
        _sin = new double[n / 2];
        for (int i = 0; i < n / 2; i++)
        {
            _cos[i] = Math.Cos(2.0 * Math.PI * i / n);
            _sin[i] = Math.Sin(2.0 * Math.PI * i / n);
        }
        _reverse = new int[n];
        int bits = 0;
        while ((1 << bits) < n) bits++;
        for (int i = 0; i < n; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _reverse[i] = r;
        }
    }

    /// <summary>In place; sign +1 computes sum x_k e^(+2 pi i j k / N), -1 the opposite sign. Neither scales.</summary>
    public void Transform(double[] re, double[] im, int sign)
    {
        int n = N;
        for (int i = 0; i < n; i++)
        {
            int r = _reverse[i];
            if (r > i)
            {
                double t = re[i]; re[i] = re[r]; re[r] = t;
                t = im[i]; im[i] = im[r]; im[r] = t;
            }
        }
        for (int size = 2; size <= n; size <<= 1)
        {
            int halfSize = size >> 1, step = n / size;
            for (int start = 0; start < n; start += size)
            {
                for (int k = 0; k < halfSize; k++)
                {
                    double wr = _cos[k * step], wi = sign * _sin[k * step];
                    int a = start + k, b = a + halfSize;
                    double tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
            }
        }
    }
}

/// <summary>
/// Transforms between spectral coefficients and a latitude-longitude grid (rows = the table's latitudes south to north,
/// <see cref="NLon"/> equally spaced longitudes from 0). On a Gaussian grid with NLat &gt;= (3K+1)/2 and NLon &gt;= 3K+1 the
/// products of two band-limited fields are integrated exactly, which is what makes the vorticity scheme conserve energy and
/// enstrophy apart from its time stepping.
/// </summary>
public sealed class SphereGrid
{
    public readonly SpectralLayout Layout;
    public readonly LegendreTable Table;
    public readonly int NLon;
    /// <summary>Gaussian weights (sum 2) when built by <see cref="Gaussian"/>; null for a plain list of latitudes.</summary>
    public readonly double[] Weights;
    /// <summary>Longitude of column 0, rad (0 for the Gaussian grid; half a cell for a texture-friendly grid).</summary>
    public readonly double LonOffset;
    private readonly Fft _fft;
    private readonly double[] _fr, _fi;

    public int NLat => Table.Count;

    public SphereGrid(SpectralLayout layout, double[] mu, double[] weights, int nLon, double lonOffset = 0.0)
    {
        LonOffset = lonOffset;
        Layout = layout;
        Table = new LegendreTable(layout, mu);
        Weights = weights;
        NLon = nLon;
        _fft = new Fft(nLon);
        _fr = new double[nLon];
        _fi = new double[nLon];
    }

    /// <summary>The smallest power of two of at least 3K+1 longitudes and the Gauss latitudes that integrate products exactly.</summary>
    public static SphereGrid Gaussian(SpectralLayout layout)
    {
        int k = layout.K;
        int nLon = 4;
        while (nLon < 3 * k + 1) nLon <<= 1;
        int nLat = (3 * k + 2) / 2;
        if ((nLat & 1) == 1) nLat++;
        GaussLegendre.Compute(nLat, out double[] mu, out double[] w);
        return new SphereGrid(layout, mu, w, nLon);
    }

    /// <summary>
    /// How many threads the transforms may use. Rows and degrees are independent and each thread writes its own part, so the
    /// result is the same whatever this is (sums always run in the same order).
    /// </summary>
    public static int MaxThreads = 1;

    private static ParallelOptions Options() => new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, MaxThreads) };

    /// <summary>The field on the grid: out[j * NLon + k].</summary>
    public void Synthesize(double[] re, double[] im, double[] grid)
    {
        if (MaxThreads > 1 && NLat >= 32)
        {
            Parallel.For(0, NLat, Options(), () => new double[2][] { new double[NLon], new double[NLon] },
                (j, state, buffers) => { SynthesizeRow(j, re, im, grid, buffers[0], buffers[1]); return buffers; }, _ => { });
            return;
        }
        for (int j = 0; j < NLat; j++) SynthesizeRow(j, re, im, grid, _fr, _fi);
    }

    private void SynthesizeRow(int j, double[] re, double[] im, double[] grid, double[] fr, double[] fi)
    {
        int size = Layout.Size, k = Layout.K;
        int row = j * size;
        Array.Clear(fr, 0, NLon);
        Array.Clear(fi, 0, NLon);
        for (int m = 0; m <= k; m++)
        {
            double sr = 0.0, si = 0.0;
            int baseIndex = Layout.Index(m, m);
            for (int l = m; l <= k + 1; l++)
            {
                double p = Table.P[row + baseIndex + (l - m)];
                sr += re[baseIndex + (l - m)] * p;
                si += im[baseIndex + (l - m)] * p;
            }
            if (m == 0)
            {
                fr[0] = sr;
            }
            else
            {
                if (LonOffset != 0.0)
                {
                    double c = Math.Cos(m * LonOffset), sn = Math.Sin(m * LonOffset);
                    double t = sr * c - si * sn;
                    si = sr * sn + si * c;
                    sr = t;
                }
                fr[m] = sr; fi[m] = si;
                fr[NLon - m] = sr; fi[NLon - m] = -si;
            }
        }
        _fft.Transform(fr, fi, 1);
        Array.Copy(fr, 0, grid, j * NLon, NLon);
    }

    private double[][] _rowRe, _rowIm;

    /// <summary>The coefficients (l up to K+1) of a gridded field on a Gaussian grid.</summary>
    public void Analyse(double[] grid, double[] re, double[] im)
    {
        if (Weights == null) throw new InvalidOperationException("Analysis needs a Gaussian grid.");
        int size = Layout.Size, k = Layout.K;
        if (_rowRe == null)
        {
            _rowRe = new double[NLat][];
            _rowIm = new double[NLat][];
            for (int j = 0; j < NLat; j++) { _rowRe[j] = new double[k + 1]; _rowIm[j] = new double[k + 1]; }
        }
        double inv = 1.0 / NLon;
        // Stage one: the Fourier coefficient of each row (weighted by its quadrature weight).
        if (MaxThreads > 1 && NLat >= 32)
            Parallel.For(0, NLat, Options(), () => new double[2][] { new double[NLon], new double[NLon] },
                (j, state, b) => { AnalyseRow(j, grid, inv, b[0], b[1]); return b; }, _ => { });
        else
            for (int j = 0; j < NLat; j++) AnalyseRow(j, grid, inv, _fr, _fi);
        // Stage two: for each order m, the sum over latitudes of that row's coefficient times the Legendre function.
        if (MaxThreads > 1 && k >= 24)
            Parallel.For(0, k + 1, Options(), m => AnalyseOrder(m, re, im));
        else
            for (int m = 0; m <= k; m++) AnalyseOrder(m, re, im);
        for (int l = 0; l <= k + 1; l++) im[Layout.Index(l, 0)] = 0.0;
    }

    private void AnalyseRow(int j, double[] grid, double inv, double[] fr, double[] fi)
    {
        Array.Copy(grid, j * NLon, fr, 0, NLon);
        Array.Clear(fi, 0, NLon);
        _fft.Transform(fr, fi, -1);
        double w = Weights[j] * inv;
        double[] rr = _rowRe[j], ri = _rowIm[j];
        for (int m = 0; m <= Layout.K; m++) { rr[m] = fr[m] * w; ri[m] = fi[m] * w; }
    }

    private void AnalyseOrder(int m, double[] re, double[] im)
    {
        int size = Layout.Size, k = Layout.K;
        int baseIndex = Layout.Index(m, m);
        for (int l = m; l <= k + 1; l++) { re[baseIndex + (l - m)] = 0.0; im[baseIndex + (l - m)] = 0.0; }
        for (int j = 0; j < NLat; j++)
        {
            int row = j * size;
            double fr = _rowRe[j][m], fi = _rowIm[j][m];
            for (int l = m; l <= k + 1; l++)
            {
                double p = Table.P[row + baseIndex + (l - m)];
                re[baseIndex + (l - m)] += fr * p;
                im[baseIndex + (l - m)] += fi * p;
            }
        }
    }
}

/// <summary>Operators on coefficient arrays that stay in spectral space.</summary>
public static class SpectralOps
{
    /// <summary>
    /// out = coefficients of (1 - mu^2) d/dmu of the field, up to degree K+1: from (1 - mu^2) dP(l)/dmu = -l eps(l+1) P(l+1) +
    /// (l+1) eps(l) P(l-1).
    /// </summary>
    public static void OneMinusMuSquaredDMu(SpectralLayout layout, double[] re, double[] im, double[] outRe, double[] outIm)
    {
        int k = layout.K;
        for (int m = 0; m <= k; m++)
        {
            for (int l = m; l <= k + 1; l++)
            {
                double r = 0.0, i = 0.0;
                if (l - 1 >= m && l - 1 <= k)
                {
                    double c = -(l - 1) * SpectralLayout.Eps(l, m);
                    int idx = layout.Index(l - 1, m);
                    r += c * re[idx]; i += c * im[idx];
                }
                if (l + 1 <= k)
                {
                    double c = (l + 2) * SpectralLayout.Eps(l + 1, m);
                    int idx = layout.Index(l + 1, m);
                    r += c * re[idx]; i += c * im[idx];
                }
                int o = layout.Index(l, m);
                outRe[o] = r; outIm[o] = i;
            }
        }
    }
}
