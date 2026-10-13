using System;

namespace KSPVE.Pure.Fluids;

/// <summary>What the barotropic flow is made of: the planet, the damping and the stochastic stirring that stands in for convection.</summary>
public sealed class FlowParameters
{
    /// <summary>Planet radius, m.</summary>
    public double Radius = 7.0e7;
    /// <summary>Rotation rate, rad/s (negative is retrograde).</summary>
    public double Omega = 2.0 * Math.PI / (5.0 * 3600.0);
    /// <summary>Linear drag / radiative damping time of the flow, s (0 = none).</summary>
    public double DragTime = 3.0e5;
    /// <summary>
    /// Energy put into the flow by the stirring, W/kg (m^2/s^3). Zero for a free flow. See <see cref="FluidProfile.StirringPower"/>
    /// for where the real number comes from.
    /// </summary>
    public double ForcingPower = 0.0;
    /// <summary>The stirring acts on total degrees from <see cref="ForcingLMin"/> to <see cref="ForcingLMax"/> (convective cells' scale).</summary>
    public int ForcingLMin = 20, ForcingLMax = 28;
    /// <summary>How long a convective plume's stirring stays coherent, s.</summary>
    public double ForcingCorrelation = 2.0e4;
    /// <summary>
    /// Scale-selective damping of the smallest scales: the e-folding time, in time steps, of the highest degree K (the damping
    /// goes as the fourth power of the Laplacian, so the large scales are untouched). 0 = none.
    /// </summary>
    public double HyperEfoldSteps = 6.0;
    public ulong Seed = 1;
}

/// <summary>
/// The 2-D barotropic vorticity equation on a rotating sphere, solved with spherical harmonics:
///   d zeta / dt = -div( V (zeta + f) ) + F - zeta / tau - nu (del^2)^4 zeta,   f = 2 Omega sin(latitude),
/// V the non-divergent wind with stream function psi (del^2 psi = zeta). The nonlinear term is evaluated on a Gaussian grid
/// in its flux (divergence) form with Gaussian quadrature, so a truncated, alias-free field conserves energy and enstrophy
/// exactly apart from the time stepping (fourth order Runge-Kutta); drag and the hyperviscosity are applied exactly
/// (integrating factor). The planet's rotation enters through f, so the beta effect (the poleward gradient of f) makes
/// Rossby waves and, with a small-scale stirring and drag, the flow self-organises into zonal jets near the Rhines scale
/// (Rhines 1975; Showman &amp; Kaspi 2013; Zhang &amp; Showman 2014).
/// Everything is in the frame turning with the planet; time is in seconds.
/// </summary>
public sealed class BarotropicModel
{
    public readonly int K;
    public readonly FlowParameters Parameters;
    public readonly SpectralLayout Layout;
    public readonly SphereGrid Grid;

    /// <summary>The relative vorticity coefficients (degree up to K; the K+1 entries stay zero).</summary>
    public readonly double[] ZRe, ZIm;
    public double Time { get; private set; }
    public long Steps { get; private set; }
    /// <summary>The fastest wind seen in the last right-hand side, m/s.</summary>
    public double MaxSpeed { get; private set; }

    private readonly FluidRandom _rng;
    private readonly StochasticField _stir;
    private readonly double[] _damp;          // 1/tau + hyperviscous rate for each coefficient (l), per (m,l)
    private readonly double[] _psiRe, _psiIm, _hRe, _hIm, _vRe, _vIm;
    private readonly double[] _zg, _hg, _vg, _ag, _bg;
    private readonly double[] _aRe, _aIm, _bRe, _bIm;
    private readonly double[] _k1Re, _k1Im, _k2Re, _k2Im, _k3Re, _k3Im, _k4Re, _k4Im, _tRe, _tIm;
    private double _forcingSum;
    private double _hyperRate;
    private double[] _relaxRe;
    private double _relaxTime;
    private readonly double[] _f;

    /// <summary>The coefficients of the stirring pattern that drives the vorticity now (also used for the heating that goes with it).</summary>
    public StochasticField Stirring => _stir;

    public BarotropicModel(int truncation, FlowParameters parameters)
    {
        K = truncation;
        Parameters = parameters;
        Layout = new SpectralLayout(K);
        Grid = SphereGrid.Gaussian(Layout);
        int n = Layout.Size;
        ZRe = new double[n]; ZIm = new double[n];
        _psiRe = new double[n]; _psiIm = new double[n]; _hRe = new double[n]; _hIm = new double[n]; _vRe = new double[n]; _vIm = new double[n];
        _aRe = new double[n]; _aIm = new double[n]; _bRe = new double[n]; _bIm = new double[n];
        _k1Re = new double[n]; _k1Im = new double[n]; _k2Re = new double[n]; _k2Im = new double[n];
        _k3Re = new double[n]; _k3Im = new double[n]; _k4Re = new double[n]; _k4Im = new double[n];
        _tRe = new double[n]; _tIm = new double[n];
        int cells = Grid.NLat * Grid.NLon;
        _zg = new double[cells]; _hg = new double[cells]; _vg = new double[cells]; _ag = new double[cells]; _bg = new double[cells];
        _damp = new double[n];
        _rng = new FluidRandom(parameters.Seed);
        _stir = new StochasticField(Layout, parameters.ForcingLMin, parameters.ForcingLMax, _rng);
        _f = new double[Grid.NLat];
        for (int j = 0; j < Grid.NLat; j++) _f[j] = 2.0 * parameters.Omega * Grid.Table.Mu[j];

        double sum = 0.0;
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, Math.Max(1, parameters.ForcingLMin)); l <= Math.Min(K, parameters.ForcingLMax); l++)
                sum += SpectralLayout.MWeight(m) / (l * (l + 1.0));
        _forcingSum = sum;
        SetStep(3600.0);
    }

    /// <summary>
    /// Standard deviation of the stirring's vorticity tendency per unit-variance coefficient, 1/s^2, for the power in
    /// <see cref="FlowParameters.ForcingPower"/>. The energy input of such a forcing, whose coefficients are unit-variance
    /// Ornstein-Uhlenbeck processes with correlation time tau, is eps = 1/4 sum w a^2 / (l (l+1)) * 2 sigma^2 tau, so
    /// sigma^2 = 2 eps / (a^2 tau sum w / (l (l+1))).
    /// </summary>
    public double ForcingSigma(double tau)
    {
        double power = Parameters.ForcingPower;
        if (!(power > 0.0) || !(_forcingSum > 0.0)) return 0.0;
        double a2 = Parameters.Radius * Parameters.Radius;
        return Math.Sqrt(2.0 * power / (a2 * tau * _forcingSum));
    }

    /// <summary>Sets the damping rates for a time step of dt (the hyperviscosity is tied to it).</summary>
    private void SetStep(double dt)
    {
        double drag = Parameters.DragTime > 0.0 ? 1.0 / Parameters.DragTime : 0.0;
        double top = (double)K * (K + 1.0);
        _hyperRate = Parameters.HyperEfoldSteps > 0.0 ? 1.0 / (Parameters.HyperEfoldSteps * dt) : 0.0;
        for (int m = 0; m <= K; m++)
            for (int l = m; l <= K; l++)
            {
                double r = l * (l + 1.0) / top;
                double r2 = r * r;
                _damp[Layout.Index(l, m)] = drag + _hyperRate * r2 * r2;
            }
    }

    /// <summary>
    /// A relaxation of the zonal-mean flow towards a given zonal wind u(latitude) with the given time scale (an
    /// equatorial super-rotation maintained by the day-night wave drag a barotropic model cannot make itself).
    /// </summary>
    public void SetZonalRelaxation(Func<double, double> zonalWindOfSinLatitude, double tau)
    {
        if (zonalWindOfSinLatitude == null || !(tau > 0.0)) { _relaxRe = null; return; }
        var target = new double[Grid.NLat * Grid.NLon];
        const double h = 1e-4;
        double a = Parameters.Radius;
        for (int j = 0; j < Grid.NLat; j++)
        {
            double mu = Grid.Table.Mu[j];
            double lo = Math.Max(-1.0 + 1e-9, mu - h), hi = Math.Min(1.0 - 1e-9, mu + h);
            double uLo = zonalWindOfSinLatitude(lo) * Math.Sqrt(1.0 - lo * lo), uHi = zonalWindOfSinLatitude(hi) * Math.Sqrt(1.0 - hi * hi);
            double z = -(uHi - uLo) / (hi - lo) / a;
            for (int k = 0; k < Grid.NLon; k++) target[j * Grid.NLon + k] = z;
        }
        var re = new double[Layout.Size];
        var im = new double[Layout.Size];
        Grid.Analyse(target, re, im);
        _relaxRe = new double[K + 2];
        for (int l = 1; l <= K; l++) _relaxRe[l] = re[Layout.Index(l, 0)];
        _relaxTime = tau;
    }

    // ---------------------------------------------------------------- the equation

    /// <summary>Stream function coefficients from vorticity: psi = -a^2 zeta / (l (l+1)).</summary>
    private void Psi(double[] zr, double[] zi)
    {
        double a2 = Parameters.Radius * Parameters.Radius;
        Array.Clear(_psiRe, 0, _psiRe.Length);
        Array.Clear(_psiIm, 0, _psiIm.Length);
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, 1); l <= K; l++)
            {
                int i = Layout.Index(l, m);
                double s = -a2 / (l * (l + 1.0));
                _psiRe[i] = s * zr[i];
                _psiIm[i] = s * zi[i];
            }
    }

    /// <summary>The tendency of the vorticity from advection alone, for the vorticity (zr, zi).</summary>
    private void Tendency(double[] zr, double[] zi, double[] outRe, double[] outIm)
    {
        double a = Parameters.Radius;
        Psi(zr, zi);
        SpectralOps.OneMinusMuSquaredDMu(Layout, _psiRe, _psiIm, _hRe, _hIm);
        Array.Clear(_vRe, 0, _vRe.Length);
        Array.Clear(_vIm, 0, _vIm.Length);
        for (int m = 1; m <= K; m++)
            for (int l = m; l <= K; l++)
            {
                int i = Layout.Index(l, m);
                _vRe[i] = -m * _psiIm[i];
                _vIm[i] = m * _psiRe[i];
            }
        Grid.Synthesize(zr, zi, _zg);
        Grid.Synthesize(_hRe, _hIm, _hg);
        Grid.Synthesize(_vRe, _vIm, _vg);
        int nLon = Grid.NLon;
        double vmax2 = 0.0;
        for (int j = 0; j < Grid.NLat; j++)
        {
            double mu = Grid.Table.Mu[j];
            double om = 1.0 / (1.0 - mu * mu);
            double f = _f[j];
            for (int k = 0; k < nLon; k++)
            {
                int c = j * nLon + k;
                double eta = _zg[c] + f;
                double u = -_hg[c] / a, v = _vg[c] / a;      // U = u cos(lat), V = v cos(lat)
                double s2 = (u * u + v * v) * om;
                if (s2 > vmax2) vmax2 = s2;
                _ag[c] = u * eta * om;
                _bg[c] = v * eta * om;
            }
        }
        MaxSpeed = Math.Sqrt(vmax2);
        Grid.Analyse(_ag, _aRe, _aIm);
        Grid.Analyse(_bg, _bRe, _bIm);
        Array.Clear(outRe, 0, outRe.Length);
        Array.Clear(outIm, 0, outIm.Length);
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, 1); l <= K; l++)
            {
                int i = Layout.Index(l, m);
                double tr = m * _aIm[i] / a, ti = -m * _aRe[i] / a;
                double up = l * SpectralLayout.Eps(l + 1, m), dn = (l + 1.0) * SpectralLayout.Eps(l, m);
                double br = -up * _bRe[Layout.Index(l + 1, m)], bi = -up * _bIm[Layout.Index(l + 1, m)];
                if (l - 1 >= m)
                {
                    br += dn * _bRe[Layout.Index(l - 1, m)];
                    bi += dn * _bIm[Layout.Index(l - 1, m)];
                }
                outRe[i] = tr + br / a;
                outIm[i] = ti + bi / a;
            }
    }

    /// <summary>The wind's coefficients: (1 - mu^2) d psi / d mu (h) and d psi / d lambda (v), as the transforms of U and V need them.</summary>
    public void WindSpectra(double[] hRe, double[] hIm, double[] vRe, double[] vIm)
    {
        Psi(ZRe, ZIm);
        SpectralOps.OneMinusMuSquaredDMu(Layout, _psiRe, _psiIm, hRe, hIm);
        Array.Clear(vRe, 0, vRe.Length);
        Array.Clear(vIm, 0, vIm.Length);
        for (int m = 1; m <= K; m++)
            for (int l = m; l <= K; l++)
            {
                int i = Layout.Index(l, m);
                vRe[i] = -m * _psiIm[i];
                vIm[i] = m * _psiRe[i];
            }
    }

    /// <summary>The largest stable time step, s, for a Courant number (the spectral Runge-Kutta limit is about 2.8 a / (K u)).</summary>
    public double CourantDt(double courant, double maxDt)
    {
        double u = Math.Max(MaxSpeed, 1.0);
        return Math.Min(maxDt, courant * 2.8 * Parameters.Radius / (K * u));
    }

    /// <summary>One Runge-Kutta step of dt seconds, with the stirring, drag and hyperviscosity.</summary>
    public void Step(double dt)
    {
        SetStep(dt);
        int n = Layout.Size;
        double[] zr = ZRe, zi = ZIm;
        Tendency(zr, zi, _k1Re, _k1Im);
        for (int i = 0; i < n; i++) { _tRe[i] = zr[i] + 0.5 * dt * _k1Re[i]; _tIm[i] = zi[i] + 0.5 * dt * _k1Im[i]; }
        Tendency(_tRe, _tIm, _k2Re, _k2Im);
        for (int i = 0; i < n; i++) { _tRe[i] = zr[i] + 0.5 * dt * _k2Re[i]; _tIm[i] = zi[i] + 0.5 * dt * _k2Im[i]; }
        Tendency(_tRe, _tIm, _k3Re, _k3Im);
        for (int i = 0; i < n; i++) { _tRe[i] = zr[i] + dt * _k3Re[i]; _tIm[i] = zi[i] + dt * _k3Im[i]; }
        Tendency(_tRe, _tIm, _k4Re, _k4Im);
        for (int i = 0; i < n; i++)
        {
            zr[i] += dt / 6.0 * (_k1Re[i] + 2.0 * _k2Re[i] + 2.0 * _k3Re[i] + _k4Re[i]);
            zi[i] += dt / 6.0 * (_k1Im[i] + 2.0 * _k2Im[i] + 2.0 * _k3Im[i] + _k4Im[i]);
        }
        if (Parameters.ForcingPower > 0.0)
        {
            double tau = Math.Max(Parameters.ForcingCorrelation, 4.0 * dt);
            _stir.Advance(dt, tau);
            _stir.AddTo(zr, zi, ForcingSigma(tau) * dt, Parameters.Omega, tau);
        }
        // Drag and hyperviscosity, exactly over the step.
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, 1); l <= K; l++)
            {
                int i = Layout.Index(l, m);
                double e = Math.Exp(-_damp[i] * dt);
                zr[i] *= e; zi[i] *= e;
            }
        if (_relaxRe != null)
        {
            double e = Math.Exp(-dt / _relaxTime);
            for (int l = 1; l <= K; l++)
            {
                int i = Layout.Index(l, 0);
                zr[i] = _relaxRe[l] + (zr[i] - _relaxRe[l]) * e;
            }
        }
        zr[0] = 0.0; zi[0] = 0.0;
        for (int l = 0; l <= K + 1; l++) zi[Layout.Index(l, 0)] = 0.0;
        Time += dt;
        Steps++;
    }

    /// <summary>Starts from a given coefficient (for tests and restarts).</summary>
    public void SetCoefficient(int l, int m, double re, double im)
    {
        ZRe[Layout.Index(l, m)] = re;
        ZIm[Layout.Index(l, m)] = m == 0 ? 0.0 : im;
    }

    // ---------------------------------------------------------------- diagnostics

    /// <summary>Kinetic energy per unit mass averaged over the sphere, m^2/s^2: 1/4 sum w a^2 |zeta|^2 / (l (l+1)).</summary>
    public double Energy()
    {
        double a2 = Parameters.Radius * Parameters.Radius, e = 0.0;
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, 1); l <= K; l++)
            {
                int i = Layout.Index(l, m);
                e += SpectralLayout.MWeight(m) * (ZRe[i] * ZRe[i] + ZIm[i] * ZIm[i]) / (l * (l + 1.0));
            }
        return 0.25 * a2 * e;
    }

    /// <summary>Relative enstrophy 1/2 &lt;zeta^2&gt;, 1/s^2.</summary>
    public double Enstrophy()
    {
        double e = 0.0;
        for (int m = 0; m <= K; m++)
            for (int l = Math.Max(m, 1); l <= K; l++)
            {
                int i = Layout.Index(l, m);
                e += SpectralLayout.MWeight(m) * (ZRe[i] * ZRe[i] + ZIm[i] * ZIm[i]);
            }
        return 0.25 * e;
    }

    /// <summary>Absolute enstrophy 1/2 &lt;(zeta + f)^2&gt; less the constant part of f, which the nonlinear term conserves.</summary>
    public double AbsoluteEnstrophy()
    {
        // f = 2 Omega mu = 2 Omega / sqrt(3/2) * P(1,0): <zeta f> = 1/2 zeta(1,0) f(1,0).
        double f10 = 2.0 * Parameters.Omega / Math.Sqrt(1.5);
        return Enstrophy() + 0.5 * ZRe[Layout.Index(1, 0)] * f10;
    }

    /// <summary>Zonal-mean eastward wind at the grid's latitudes, m/s.</summary>
    public double[] ZonalMeanWind()
    {
        Psi(ZRe, ZIm);
        SpectralOps.OneMinusMuSquaredDMu(Layout, _psiRe, _psiIm, _hRe, _hIm);
        // The zonal mean of (1 - mu^2) d psi / d mu is the m = 0 sum.
        var u = new double[Grid.NLat];
        double a = Parameters.Radius;
        for (int j = 0; j < Grid.NLat; j++)
        {
            double mu = Grid.Table.Mu[j], h = 0.0;
            int row = j * Layout.Size;
            for (int l = 0; l <= K + 1; l++) h += _hRe[Layout.Index(l, 0)] * Grid.Table.P[row + Layout.Index(l, 0)];
            double cosLat = Math.Sqrt(Math.Max(1e-12, 1.0 - mu * mu));
            u[j] = -h / (a * cosLat);
        }
        return u;
    }

    /// <summary>Root-mean-square wind, m/s: sqrt(2 E).</summary>
    public double RmsSpeed() => Math.Sqrt(2.0 * Energy());

    /// <summary>Whether the state has gone non-finite (a blown-up run).</summary>
    public bool IsFinite()
    {
        for (int i = 0; i < ZRe.Length; i++)
            if (double.IsNaN(ZRe[i]) || double.IsInfinity(ZRe[i]) || double.IsNaN(ZIm[i]) || double.IsInfinity(ZIm[i])) return false;
        return true;
    }
}

/// <summary>
/// A random field made of spherical harmonics in a band of total degrees, each coefficient an Ornstein-Uhlenbeck process (a
/// random walk pulled back to zero over its correlation time) with unit variance. It stands for convective plumes: patches
/// that live for a while and then give way to others.
/// </summary>
public sealed class StochasticField
{
    private readonly SpectralLayout _layout;
    private readonly int _lMin, _lMax;
    private readonly FluidRandom _rng;
    public readonly double[] Re, Im;
    /// <summary>Sum of the sphere weights of the modes: the field's variance is half of it.</summary>
    public readonly double WeightSum;

    public StochasticField(SpectralLayout layout, int lMin, int lMax, FluidRandom rng)
    {
        _layout = layout;
        _lMin = Math.Max(1, lMin);
        _lMax = Math.Min(layout.K, Math.Max(lMax, _lMin));
        _rng = rng;
        Re = new double[layout.Size];
        Im = new double[layout.Size];
        double w = 0.0;
        for (int m = 0; m <= layout.K; m++)
            for (int l = Math.Max(m, _lMin); l <= _lMax; l++) w += SpectralLayout.MWeight(m);
        WeightSum = w;
        // Start from the stationary distribution.
        Advance(0.0, 1.0);
    }

    /// <summary>Spatial standard deviation of the field (grid value) given unit-variance coefficients.</summary>
    public double Rms => Math.Sqrt(0.5 * WeightSum);

    /// <summary>Moves the process on by dt (correlation time tau).</summary>
    public void Advance(double dt, double tau)
    {
        double alpha = dt <= 0.0 ? 0.0 : Math.Exp(-dt / tau);
        double noise = Math.Sqrt(1.0 - alpha * alpha);
        for (int m = 0; m <= _layout.K; m++)
            for (int l = Math.Max(m, _lMin); l <= _lMax; l++)
            {
                int i = _layout.Index(l, m);
                if (m == 0)
                {
                    Re[i] = alpha * Re[i] + noise * _rng.NextGaussian();
                    Im[i] = 0.0;
                }
                else
                {
                    const double half = 0.70710678118654752;
                    Re[i] = alpha * Re[i] + noise * half * _rng.NextGaussian();
                    Im[i] = alpha * Im[i] + noise * half * _rng.NextGaussian();
                }
            }
    }

    public void AddTo(double[] re, double[] im, double scale)
    {
        for (int m = 0; m <= _layout.K; m++)
            for (int l = Math.Max(m, _lMin); l <= _lMax; l++)
            {
                int i = _layout.Index(l, m);
                re[i] += scale * Re[i];
                im[i] += scale * Im[i];
            }
    }

    /// <summary>
    /// As <see cref="AddTo(double[], double[], double)"/>, with each mode's amplitude raised by sqrt(1 + (omega tau)^2), omega the
    /// mode's Rossby frequency 2 Omega m / (l (l+1)). A wave that turns through a radian or more while the forcing stays
    /// coherent takes up less of it (the energy input is 1 / (1 + (omega tau)^2) of the white-noise value), so without
    /// this the real power put in would fall short of the one asked for by up to a half.
    /// </summary>
    public void AddTo(double[] re, double[] im, double scale, double omega, double tau)
    {
        for (int m = 0; m <= _layout.K; m++)
            for (int l = Math.Max(m, _lMin); l <= _lMax; l++)
            {
                int i = _layout.Index(l, m);
                double w = 2.0 * omega * m / (l * (l + 1.0)) * tau;
                double g = scale * Math.Sqrt(1.0 + w * w);
                re[i] += g * Re[i];
                im[i] += g * Im[i];
            }
    }
}
