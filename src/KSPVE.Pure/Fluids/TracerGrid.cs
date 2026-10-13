using System;

namespace KSPVE.Pure.Fluids;

/// <summary>What drives the temperature anomaly and the condensates on the cloud grid.</summary>
public sealed class TracerForcing
{
    /// <summary>Radiative relaxation time of the temperature anomaly, s.</summary>
    public double RadiativeTime = 2.0e5;
    /// <summary>The amplitude of the heating (K/s per unit of the normalised pattern): see <see cref="TracerGrid.Step"/>.</summary>
    public double HeatingRate;
    /// <summary>How strongly the stirring pattern's heating is tied to the vorticity forcing (cyclonic sinking, anticyclonic rising) rather than independent.</summary>
    public double Coupling = 0.7;
    /// <summary>The share of the heating that is the convective stirring pattern (patchy cells that come and go).</summary>
    public double ConvectiveShare = 0.45;
    /// <summary>
    /// The share that follows the flow's own vorticity: cyclonic regions (vorticity of the sign of f) sink, warm and clear;
    /// anticyclones rise, cool and cloud over, as in Jupiter's dark belts, bright zones and white ovals.
    /// </summary>
    public double VorticityShare = 0.35;
    /// <summary>The share that follows the zonal-mean vorticity alone: the belts and zones between the jets.</summary>
    public double ZonalShare = 0.8;
    /// <summary>For a tidally locked body: the day-night equilibrium anomaly in K at (sin lat, lon); null for none.</summary>
    public Func<double, double, double> Equilibrium;
    /// <summary>The decks' temperatures, K, for the response of each condensate.</summary>
    public double[] DeckKelvin = new double[0];
    /// <summary>The temperature scales over which each deck's equilibrium condensate changes by a factor e, K.</summary>
    public double[] DeckScale = new double[0];
    public double[] CondensationTime = new double[0];
    /// <summary>The share of the temperature anomaly each deck feels (deeper decks are less disturbed).</summary>
    public double[] DeckShare = new double[0];
    /// <summary>Largest condensate (supersaturated) as a multiple of the mean.</summary>
    public double MaxCondensate = 3.0;
}

/// <summary>
/// The cloud grid: a temperature anomaly theta (K) and the condensate of each deck on a latitude-longitude grid, carried by
/// the flow with a semi-Lagrangian scheme (departure points by a midpoint rule in three dimensions, so the poles are
/// not special; Catmull-Rom cubic interpolation limited to the range of the four neighbours so it makes no new extremes).
/// Between advection steps each condensate relaxes towards its equilibrium amount q_eq = exp(-theta / Theta), the
/// Clausius-Clapeyron response of the vapour pressure to the temperature anomaly at the deck (cooler: more condenses;
/// warmer: the cloud evaporates and the gap lets the deeper, hotter layers shine through).
/// </summary>
public sealed class TracerGrid
{
    public readonly int NLon, NLat, Decks;
    /// <summary>Temperature anomaly, K; cell (i, k) of row i (south to north) and column k is at [i * NLon + k].</summary>
    public readonly double[] Theta;
    /// <summary>The condensate of each deck: 1 is the mean amount.</summary>
    public readonly double[][] Q;

    private readonly SphereGrid _synth;
    private readonly BarotropicModel _model;
    private readonly double[] _sinLat, _cosLat, _lat, _lon;
    private readonly double[] _hg, _vg, _zg, _s1, _s2;
    private readonly double[] _vx, _vy, _vz;
    private readonly double[] _depLat, _depLon;
    private readonly double[] _hRe, _hIm, _vRe, _vIm;
    private readonly double[] _scratch;
    private readonly StochasticField _heat2;
    private readonly double _heat1Rms, _heat2Rms;
    private double _zetaRms = 1e-6;
    private readonly double[] _zonal, _gain;

    public TracerGrid(BarotropicModel model, int nLon, int decks, FluidRandom rng, int forcingLMin, int forcingLMax)
    {
        _model = model;
        NLon = nLon;
        NLat = nLon / 2;
        Decks = decks;
        var mu = new double[NLat];
        _sinLat = mu;
        _cosLat = new double[NLat];
        _lat = new double[NLat];
        for (int i = 0; i < NLat; i++)
        {
            _lat[i] = -Math.PI / 2 + (i + 0.5) * Math.PI / NLat;
            _sinLat[i] = Math.Sin(_lat[i]);
            _cosLat[i] = Math.Cos(_lat[i]);
        }
        _lon = new double[NLon];
        for (int k = 0; k < NLon; k++) _lon[k] = 2.0 * Math.PI * (k + 0.5) / NLon;
        _synth = new SphereGrid(model.Layout, mu, null, NLon, Math.PI / NLon);
        int n = NLat * NLon;
        Theta = new double[n];
        Q = new double[decks][];
        for (int d = 0; d < decks; d++)
        {
            Q[d] = new double[n];
            for (int c = 0; c < n; c++) Q[d][c] = 1.0;
        }
        _hg = new double[n]; _vg = new double[n]; _zg = new double[n]; _s1 = new double[n]; _s2 = new double[n];
        _vx = new double[n]; _vy = new double[n]; _vz = new double[n];
        _depLat = new double[n]; _depLon = new double[n];
        _scratch = new double[n];
        _zonal = new double[NLat];
        _gain = new double[NLat];
        int sz = model.Layout.Size;
        _hRe = new double[sz]; _hIm = new double[sz]; _vRe = new double[sz]; _vIm = new double[sz];
        _heat2 = new StochasticField(model.Layout, forcingLMin, forcingLMax, rng);
        _heat1Rms = model.Stirring.Rms;
        _heat2Rms = _heat2.Rms;
    }

    /// <summary>The latitude (rad) of row i and the longitude (rad) of column k, for rendering and tests.</summary>
    public double Latitude(int i) => _lat[i];
    public double Longitude(int k) => _lon[k];

    /// <summary>Largest value |wind| the last step used, m/s.</summary>
    public double MaxWind { get; private set; }

    // ---------------------------------------------------------------- stepping

    /// <summary>
    /// One step of dt seconds: advection by the model's present wind, then the heating (the stirring's pattern, tied to the
    /// vorticity forcing as convective upwelling is: warm where the top of the plume diverges, anticyclonic in the northern
    /// hemisphere and cyclonic in the southern, so the coupling changes sign across the equator; plus an independent part and a
    /// part from the flow's own vorticity), the radiative relaxation of the anomaly and the condensation.
    /// </summary>
    public void Step(double dt, TracerForcing forcing)
    {
        double a = _model.Parameters.Radius;
        _model.WindSpectra(_hRe, _hIm, _vRe, _vIm);
        _synth.Synthesize(_hRe, _hIm, _hg);
        _synth.Synthesize(_vRe, _vIm, _vg);
        double maxWind2 = 0.0;
        for (int i = 0; i < NLat; i++)
        {
            double sl = _sinLat[i], cl = _cosLat[i];
            for (int k = 0; k < NLon; k++)
            {
                int c = i * NLon + k;
                double u = -_hg[c] / (a * cl), v = _vg[c] / (a * cl);
                double w2 = u * u + v * v;
                if (w2 > maxWind2) maxWind2 = w2;
                double lon = _lon[k];
                double sinL = Math.Sin(lon), cosL = Math.Cos(lon);
                // East (-sin lon, cos lon, 0) and north (-sin lat cos lon, -sin lat sin lon, cos lat).
                _vx[c] = -u * sinL - v * sl * cosL;
                _vy[c] = u * cosL - v * sl * sinL;
                _vz[c] = v * cl;
            }
        }
        MaxWind = Math.Sqrt(maxWind2);
        DeparturePoints(dt / a);

        // The advected fields.
        Advect(Theta);
        for (int d = 0; d < Decks; d++) Advect(Q[d]);

        // Heating from the stirring pattern, an independent pattern and the vorticity.
        if (forcing.HeatingRate > 0.0)
        {
            _synth.Synthesize(_model.Stirring.Re, _model.Stirring.Im, _s1);
            _heat2.Advance(dt, Math.Max(_model.Parameters.ForcingCorrelation, 4.0 * dt));
            _synth.Synthesize(_heat2.Re, _heat2.Im, _s2);
            _synth.Synthesize(_model.ZRe, _model.ZIm, _zg);
            double zsum = 0.0;
            for (int c = 0; c < _zg.Length; c++) zsum += _zg[c] * _zg[c];
            _zetaRms = Math.Max(Math.Sqrt(zsum / _zg.Length), 1e-9);
        }
        // The zonal-mean vorticity at each latitude (the m = 0 harmonics), normalised to unit rms.
        double zonalRms = 0.0, zonalWeight = 0.0;
        for (int i = 0; i < NLat; i++)
        {
            double z = 0.0;
            int row = i * _model.Layout.Size;
            for (int l = 1; l <= _model.K; l++) z += _model.ZRe[_model.Layout.Index(l, 0)] * _synth.Table.P[row + _model.Layout.Index(l, 0)];
            _zonal[i] = z;
            zonalRms += z * z * _cosLat[i];
            zonalWeight += _cosLat[i];
        }
        zonalRms = Math.Max(Math.Sqrt(zonalRms / zonalWeight), 1e-12);
        // A local gain (the rms over about +-15 degrees, never below a third of the global one) so every belt counts, not only the strongest.
        int half = Math.Max(1, (int)Math.Round(0.26 / (Math.PI / NLat)));
        for (int i = 0; i < NLat; i++)
        {
            double sum = 0.0;
            int n = 0;
            for (int d = -half; d <= half; d++)
            {
                int j = i + d;
                if (j < 0 || j >= NLat) continue;
                sum += _zonal[j] * _zonal[j];
                n++;
            }
            _gain[i] = 1.0 / Math.Max(Math.Sqrt(sum / n), 0.33 * zonalRms);
        }
        double tau = forcing.RadiativeTime;
        double relax = dt / tau;
        for (int i = 0; i < NLat; i++)
        {
            double sl = _sinLat[i];
            double sign = Math.Tanh(sl / 0.2);
            for (int k = 0; k < NLon; k++)
            {
                int c = i * NLon + k;
                double source = 0.0;
                if (forcing.HeatingRate > 0.0)
                {
                    double p1 = _s1[c] / _heat1Rms, p2 = _s2[c] / _heat2Rms;
                    double convective = -sign * forcing.Coupling * p1 + Math.Sqrt(Math.Max(0.0, 1.0 - forcing.Coupling * forcing.Coupling)) * p2;
                    // Belts and zones: cyclonic regions (vorticity of the sign of f) sink, warm and clear; anticyclones rise and cloud.
                    double vort = sign * _zg[c] / _zetaRms;
                    double zonal = sign * _zonal[i] * _gain[i];
                    // Soft limits keep the strongest jet from dominating; the zonal term fades out over the last few degrees at the poles.
                    double polar = Math.Min(1.0, _cosLat[i] / 0.3);
                    source = forcing.HeatingRate * (forcing.ConvectiveShare * convective
                        + forcing.VorticityShare * 1.5 * Math.Tanh(vort / 1.5)
                        + forcing.ZonalShare * polar * 1.5 * Math.Tanh(zonal / 1.5));
                }
                double equilibrium = 0.0;
                double rate = relax;
                if (forcing.Equilibrium != null) equilibrium = forcing.Equilibrium(sl, _lon[k]);
                Theta[c] = (Theta[c] + dt * source + relax * equilibrium) / (1.0 + rate);
            }
        }
        for (int d = 0; d < Decks; d++)
        {
            double scale = forcing.DeckScale[d], share = forcing.DeckShare[d];
            double relaxQ = dt / forcing.CondensationTime[d];
            double[] q = Q[d];
            for (int c = 0; c < q.Length; c++)
            {
                double eq = Math.Exp(-Theta[c] * share / scale);
                if (eq > forcing.MaxCondensate) eq = forcing.MaxCondensate;
                q[c] = (Math.Max(q[c], 0.0) + relaxQ * eq) / (1.0 + relaxQ);
            }
        }
    }

    // ---------------------------------------------------------------- semi-Lagrangian advection

    private void DeparturePoints(double dtOverRadius)
    {
        for (int i = 0; i < NLat; i++)
        {
            double cl = _cosLat[i], sl = _sinLat[i];
            for (int k = 0; k < NLon; k++)
            {
                int c = i * NLon + k;
                double cosL = Math.Cos(_lon[k]), sinL = Math.Sin(_lon[k]);
                double x = cl * cosL, y = cl * sinL, z = sl;
                // Midpoint: step back half the way, take the wind there, step back the whole way with it.
                double mx = x - 0.5 * dtOverRadius * _vx[c], my = y - 0.5 * dtOverRadius * _vy[c], mz = z - 0.5 * dtOverRadius * _vz[c];
                double len = Math.Sqrt(mx * mx + my * my + mz * mz);
                mx /= len; my /= len; mz /= len;
                SampleWind(mx, my, mz, out double wx, out double wy, out double wz);
                // Keep the wind tangent at the midpoint.
                double dot = wx * mx + wy * my + wz * mz;
                wx -= dot * mx; wy -= dot * my; wz -= dot * mz;
                double dx = x - dtOverRadius * wx, dy = y - dtOverRadius * wy, dz = z - dtOverRadius * wz;
                len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                dz /= len;
                _depLat[c] = Math.Asin(Math.Max(-1.0, Math.Min(1.0, dz)));
                double lon = Math.Atan2(dy, dx);
                if (lon < 0.0) lon += 2.0 * Math.PI;
                _depLon[c] = lon;
            }
        }
    }

    /// <summary>Bilinear interpolation of the wind's Cartesian components at a point on the unit sphere.</summary>
    private void SampleWind(double x, double y, double z, out double wx, out double wy, out double wz)
    {
        double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, z)));
        double lon = Math.Atan2(y, x);
        if (lon < 0.0) lon += 2.0 * Math.PI;
        double fi = (lat + Math.PI / 2) / Math.PI * NLat - 0.5;
        double fk = lon / (2.0 * Math.PI) * NLon - 0.5;
        int i0 = (int)Math.Floor(fi), k0 = (int)Math.Floor(fk);
        double ti = fi - i0, tk = fk - k0;
        wx = Bilinear(_vx, i0, k0, ti, tk);
        wy = Bilinear(_vy, i0, k0, ti, tk);
        wz = Bilinear(_vz, i0, k0, ti, tk);
    }

    private double Bilinear(double[] f, int i0, int k0, double ti, double tk)
    {
        double a = Cell(f, i0, k0), b = Cell(f, i0, k0 + 1), c = Cell(f, i0 + 1, k0), d = Cell(f, i0 + 1, k0 + 1);
        return (a * (1 - tk) + b * tk) * (1 - ti) + (c * (1 - tk) + d * tk) * ti;
    }

    /// <summary>A cell by row and column with the longitude wrapped and rows past a pole reflected over it.</summary>
    private double Cell(double[] f, int i, int k)
    {
        if (i < 0) { i = -1 - i; k += NLon / 2; }
        else if (i >= NLat) { i = 2 * NLat - 1 - i; k += NLon / 2; }
        k %= NLon;
        if (k < 0) k += NLon;
        return f[i * NLon + k];
    }

    private void Advect(double[] field)
    {
        for (int c = 0; c < field.Length; c++)
        {
            double fi = (_depLat[c] + Math.PI / 2) / Math.PI * NLat - 0.5;
            double fk = _depLon[c] / (2.0 * Math.PI) * NLon - 0.5;
            int i0 = (int)Math.Floor(fi), k0 = (int)Math.Floor(fk);
            double ti = fi - i0, tk = fk - k0;
            double wi0 = CatmullRom(ti, -1), wi1 = CatmullRom(ti, 0), wi2 = CatmullRom(ti, 1), wi3 = CatmullRom(ti, 2);
            double wk0 = CatmullRom(tk, -1), wk1 = CatmullRom(tk, 0), wk2 = CatmullRom(tk, 1), wk3 = CatmullRom(tk, 2);
            double value = 0.0, min = double.MaxValue, max = double.MinValue;
            for (int di = -1; di <= 2; di++)
            {
                double wi = di == -1 ? wi0 : di == 0 ? wi1 : di == 1 ? wi2 : wi3;
                double row = 0.0;
                for (int dk = -1; dk <= 2; dk++)
                {
                    double wk = dk == -1 ? wk0 : dk == 0 ? wk1 : dk == 1 ? wk2 : wk3;
                    double v = Cell(field, i0 + di, k0 + dk);
                    row += wk * v;
                    if (di >= 0 && di <= 1 && dk >= 0 && dk <= 1)
                    {
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
                value += wi * row;
            }
            _scratch[c] = value < min ? min : value > max ? max : value;
        }
        Array.Copy(_scratch, field, field.Length);
    }

    /// <summary>The Catmull-Rom weight of the sample at offset j (-1, 0, 1, 2) for a point t of the way from sample 0 to sample 1.</summary>
    private static double CatmullRom(double t, int j)
    {
        double t2 = t * t, t3 = t2 * t;
        switch (j)
        {
            case -1: return 0.5 * (-t3 + 2.0 * t2 - t);
            case 0: return 0.5 * (3.0 * t3 - 5.0 * t2 + 2.0);
            case 1: return 0.5 * (-3.0 * t3 + 4.0 * t2 + t);
            default: return 0.5 * (t3 - t2);
        }
    }
}
