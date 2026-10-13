using System;

namespace KSPVE.Pure.Seasons;

/// <summary>What a planet's climate model needs to know about it (all from the game's bodies and configs).</summary>
public sealed class ClimateParameters
{
    /// <summary>The star's irradiance at the planet's mean distance (W/m^2; Earth 1361).</summary>
    public double SolarConstant = 1361.0;
    /// <summary>The planet's albedo without ice (Earth's Bond albedo, clouds included: 0.30) and with ice or snow (Budyko 0.62).</summary>
    public double Albedo = 0.30, IceAlbedo = 0.62;
    /// <summary>The temperature (K) below which a latitude band is ice covered (Budyko: -10 C), and the smoothing of the change.</summary>
    public double IceTemperature = 263.15, IceWidth = 2.0;
    /// <summary>Whether snow and ice change the albedo (a world with no water has none to freeze).</summary>
    public bool IceFeedback = true;
    /// <summary>
    /// How much brighter the poles are than the equator for the same ground (the albedo grows with the sun's zenith angle):
    /// North and Coakley (1979) have albedo = a0 + a2 P2(sin lat) with a2 = 0.08, P2 the second Legendre polynomial.
    /// </summary>
    public double ZenithAlbedo = 0.08;
    /// <summary>The length of the solar day (s).</summary>
    public double SolarDay = 21600.0;
    /// <summary>Surface pressure (atm) and surface gravity (Earth's = 1).</summary>
    public double PressureAtm = 1.0, GravityRatio = 1.0;
    /// <summary>
    /// The planet's global-mean surface air temperature (K) to hold to: the game's own sea level temperature where it has one
    /// (NaN: none, the greenhouse comes from the pressure alone).
    /// </summary>
    public double MeanTemperature = double.NaN;
    /// <summary>The longwave optical depth of the atmosphere (NaN: solved from <see cref="MeanTemperature"/>).</summary>
    public double Opacity = double.NaN;
    /// <summary>The share of each latitude band's surface that is ocean (<see cref="EnergyBalance.Bands"/> values, south to north), or null for <see cref="OceanShare"/> everywhere.</summary>
    public double[] OceanFraction;
    public double OceanShare = 0.7;
    /// <summary>
    /// The ocean's layer that exchanges heat with the air over a year (m): the seasonal heat capacity. A winter mixed layer is
    /// 50 to 100 m deep and a summer one 10 to 30; 30 m gives a zonal mean about Earth's range at mid latitudes (this model has
    /// no land and sea side by side, so an ocean world's temperatures lag the sun more than Earth's mixed ones, about 2.5 months).
    /// </summary>
    public double MixedLayer = 30.0;
}

/// <summary>
/// A one-dimensional latitudinal energy balance model (Budyko 1969, Sellers 1969, North 1975), solved for the periodic state
/// of a year:
///
///   C dT/dt = Q(lat, t) (1 - albedo(T)) - OLR(T) + D div(grad T)
///
/// with the daily-mean insolation Q (<see cref="Insolation.DailyMean"/>, through the orbit's declination and distance),
/// an albedo that jumps up where the band is ice covered, outgoing longwave radiation of a grey atmosphere
/// sigma T^4 / (1 + 3/4 tau) (the grey-gas solution, Hartmann eq. 2.28; tau is set so that the global mean is the game's own
/// sea level temperature), heat carried poleward by diffusion (D = 0.55 W/m^2/K for Earth, North's value, here scaled to the
/// grey OLR's own slope and to the air's mass: pressure over gravity), and a heat capacity of the atmosphere, the land's
/// annual skin and the ocean's mixed layer (less when it freezes), by latitude. 36 bands of 5 degrees, 96 steps a year,
/// backward Euler; the year is repeated from a warm start until the state at its start repeats (no random numbers: the same
/// planet always gives the same table).
/// </summary>
public sealed class EnergyBalance
{
    public const int Bands = 36;
    public const int StepsPerYear = 96;
    public const double StefanBoltzmann = 5.670374419e-8;
    public const double EarthTransport = 0.35;   // W/m^2/K: North's 0.55 for his linear OLR (slope 2.09 W/m^2/K) scaled towards the grey OLR's own slope (3.3), then tuned to Earth's 40 K equator to pole contrast

    private readonly ClimateParameters _p;
    private readonly SeasonOrbit _orbit;
    private readonly double[] _cosCentre = new double[Bands], _cosEdge = new double[Bands + 1];
    private readonly double[] _oceanFraction = new double[Bands];
    private float[] _table;   // [step * Bands + band], K
    private double _transport;

    public EnergyBalance(ClimateParameters parameters, SeasonOrbit orbit)
    {
        _p = parameters;
        _orbit = orbit;
        double dphi = Math.PI / Bands;
        for (int i = 0; i < Bands; i++)
        {
            _cosCentre[i] = Math.Cos(Latitude(i));
            _oceanFraction[i] = parameters.OceanFraction != null && parameters.OceanFraction.Length == Bands ? parameters.OceanFraction[i] : parameters.OceanShare;
        }
        for (int i = 0; i <= Bands; i++) _cosEdge[i] = Math.Max(Math.Cos(-0.5 * Math.PI + i * dphi), 0.0);
        _transport = parameters.PressureAtm < 1e-3 ? 0.0 : Math.Min(EarthTransport * parameters.PressureAtm / Math.Max(parameters.GravityRatio, 0.05), 12.0);
        Solve();
    }

    public ClimateParameters Parameters => _p;
    public SeasonOrbit Orbit => _orbit;

    /// <summary>The grey atmosphere's longwave optical depth the model ended up with.</summary>
    public double Opacity { get; private set; }

    /// <summary>The area-weighted mean of the year's temperatures (K).</summary>
    public double GlobalMean { get; private set; }

    /// <summary>Heat carried poleward (W/m^2/K).</summary>
    public double Transport => _transport;

    /// <summary>The mean outgoing longwave radiation (W/m^2).</summary>
    public double MeanOutgoing { get; private set; }

    /// <summary>The centre latitude (rad) of a band.</summary>
    public static double Latitude(int band) => -0.5 * Math.PI + (band + 0.5) * Math.PI / Bands;

    /// <summary>The share of a band that is ocean.</summary>
    public double OceanShareOf(int band) => _oceanFraction[band];

    /// <summary>
    /// The zonal-mean surface air temperature at sea level (K) at a latitude, <paramref name="yearFraction"/> of the way through
    /// the year from perihelion (linear between bands and steps; periodic in the year).
    /// </summary>
    public double Temperature(double latitude, double yearFraction)
    {
        double s = yearFraction - Math.Floor(yearFraction);
        double x = s * StepsPerYear - 0.5;
        int s0 = (int)Math.Floor(x);
        double fs = x - s0;
        int s1 = ((s0 + 1) % StepsPerYear + StepsPerYear) % StepsPerYear;
        s0 = (s0 % StepsPerYear + StepsPerYear) % StepsPerYear;
        double y = (latitude + 0.5 * Math.PI) / Math.PI * Bands - 0.5;
        int b0 = (int)Math.Floor(y);
        double fb = y - b0;
        int b1 = Math.Min(Math.Max(b0 + 1, 0), Bands - 1);
        b0 = Math.Min(Math.Max(b0, 0), Bands - 1);
        double a = _table[s0 * Bands + b0] + (_table[s0 * Bands + b1] - _table[s0 * Bands + b0]) * fb;
        double b = _table[s1 * Bands + b0] + (_table[s1 * Bands + b1] - _table[s1 * Bands + b0]) * fb;
        return a + (b - a) * fs;
    }

    /// <summary>Outgoing longwave radiation of the grey atmosphere (W/m^2) for a surface at <paramref name="kelvin"/>.</summary>
    public static double OutgoingLongwave(double kelvin, double opacity) =>
        StefanBoltzmann * kelvin * kelvin * kelvin * kelvin / (1.0 + 0.75 * opacity);

    private double AlbedoAt(double kelvin, int band)
    {
        double sin = Math.Sin(Latitude(band));
        double ground = _p.Albedo + _p.ZenithAlbedo * 0.5 * (3.0 * sin * sin - 1.0);
        if (!_p.IceFeedback) return ground;
        double ice = 0.5 * (1.0 - Math.Tanh((kelvin - _p.IceTemperature) / Math.Max(_p.IceWidth, 0.1)));
        return ground + (_p.IceAlbedo - ground) * ice;
    }

    /// <summary>The heat capacity of a band (J/m^2/K) at a temperature: air, the land's annual skin, the ocean's mixed layer (a tenth of it when frozen over).</summary>
    public double HeatCapacity(int band, double kelvin)
    {
        double air = 1004.0 * 101325.0 * _p.PressureAtm / (9.81 * Math.Max(_p.GravityRatio, 0.05));
        const double Land = 3.0e6;   // dry soil, 1500 kg/m^3 * 1000 J/kg/K * ~2 m (the skin of an annual wave)
        double ocean = 1025.0 * 3990.0 * _p.MixedLayer;
        double liquid = 0.5 * (1.0 + Math.Tanh((kelvin - 271.35) / 1.5));
        double water = ocean * (0.1 + 0.9 * liquid);
        double f = _oceanFraction[band];
        return air + (1.0 - f) * Land + f * water;
    }

    private void Run(double opacity, int maxYears, double tolerance, float[] start, float[] table, out double mean, out double outgoing, out int years)
    {
        var t = new double[Bands];
        for (int i = 0; i < Bands; i++) t[i] = start[i];
        var next = new double[Bands];
        var lower = new double[Bands];
        var diag = new double[Bands];
        var upper = new double[Bands];
        var rhs = new double[Bands];
        var solved = new double[Bands];
        double dt = _orbit.Period / StepsPerYear;
        double dphi2 = Math.Pow(Math.PI / Bands, 2.0);
        var begin = new double[Bands];
        years = 0;
        mean = 0.0;
        outgoing = 0.0;
        for (int year = 0; year < maxYears; year++)
        {
            years = year + 1;
            Array.Copy(t, begin, Bands);
            double sum = 0.0, weight = 0.0, olrSum = 0.0;
            for (int step = 0; step < StepsPerYear; step++)
            {
                double fraction = (step + 0.5) / StepsPerYear;
                double declination = _orbit.DeclinationAt(fraction);
                double flux = _p.SolarConstant * _orbit.DistanceFactorAt(fraction);
                Array.Copy(t, next, Bands);
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 0; i < Bands; i++)
                    {
                        double lat = Latitude(i);
                        double absorbed = Insolation.DailyMean(flux, lat, declination) * (1.0 - AlbedoAt(t[i], i));
                        double star = Math.Max(next[i], 3.0);
                        double olr = OutgoingLongwave(star, opacity);
                        double slope = 4.0 * olr / star;
                        double c = HeatCapacity(i, t[i]) / dt;
                        double k = _transport / (_cosCentre[i] * dphi2);
                        lower[i] = i > 0 ? -k * _cosEdge[i] : 0.0;
                        upper[i] = i < Bands - 1 ? -k * _cosEdge[i + 1] : 0.0;
                        diag[i] = c + slope + k * (_cosEdge[i] + _cosEdge[i + 1]);
                        rhs[i] = c * t[i] + absorbed - olr + slope * star;
                    }
                    Thomas(lower, diag, upper, rhs, solved);
                    for (int i = 0; i < Bands; i++) next[i] = Math.Max(solved[i], 3.0);
                }
                Array.Copy(next, t, Bands);
                for (int i = 0; i < Bands; i++)
                {
                    table[step * Bands + i] = (float)t[i];
                    double w = _cosCentre[i];
                    sum += t[i] * w;
                    olrSum += OutgoingLongwave(t[i], opacity) * w;
                    weight += w;
                }
            }
            mean = sum / weight;
            outgoing = olrSum / weight;
            double change = 0.0;
            for (int i = 0; i < Bands; i++) change = Math.Max(change, Math.Abs(t[i] - begin[i]));
            if (change < tolerance) break;
        }
    }

    private static void Thomas(double[] a, double[] b, double[] c, double[] d, double[] x)
    {
        int n = b.Length;
        var cp = new double[n];
        var dp = new double[n];
        cp[0] = c[0] / b[0];
        dp[0] = d[0] / b[0];
        for (int i = 1; i < n; i++)
        {
            double m = b[i] - a[i] * cp[i - 1];
            cp[i] = c[i] / m;
            dp[i] = (d[i] - a[i] * dp[i - 1]) / m;
        }
        x[n - 1] = dp[n - 1];
        for (int i = n - 2; i >= 0; i--) x[i] = dp[i] - cp[i] * x[i + 1];
    }

    private void Solve()
    {
        // A warm start (so the ice-albedo feedback settles on the warm solution, always the same one).
        var start = new float[Bands];
        double meanAbsorbed = _p.SolarConstant * 0.25 * (1.0 - _p.Albedo);
        double blackbody = Math.Pow(meanAbsorbed / StefanBoltzmann, 0.25);
        double warm = double.IsNaN(_p.MeanTemperature) ? blackbody + 20.0 : _p.MeanTemperature;
        for (int i = 0; i < Bands; i++) start[i] = (float)(warm + 25.0 * (Math.Cos(Latitude(i)) - 0.64));
        var table = new float[StepsPerYear * Bands];
        double mean, outgoing;
        int years;
        double tau;
        if (!double.IsNaN(_p.Opacity)) tau = Math.Max(_p.Opacity, 0.0);
        else if (double.IsNaN(_p.MeanTemperature) || _p.PressureAtm < 1e-3) tau = _p.PressureAtm < 1e-3 ? 0.0 : 0.84 * Math.Pow(_p.PressureAtm, 0.75);
        else
        {
            // The opacity that gives the game's own mean: bisection on a monotone relation (more greenhouse, warmer).
            double lo = 0.0, hi = 4000.0;
            Run(0.0, 60, 0.05, start, table, out mean, out outgoing, out years);
            if (mean >= _p.MeanTemperature) tau = 0.0;
            else
            {
                for (int i = 0; i < 40 && hi - lo > 1e-4 * (1.0 + lo); i++)
                {
                    double mid = 0.5 * (lo + hi);
                    Run(mid, 60, 0.05, start, table, out mean, out outgoing, out years);
                    if (mean < _p.MeanTemperature) lo = mid; else hi = mid;
                }
                tau = 0.5 * (lo + hi);
            }
        }
        Opacity = tau;
        Run(tau, 400, 0.002, start, table, out mean, out outgoing, out years);
        _table = table;
        GlobalMean = mean;
        MeanOutgoing = outgoing;
    }
}
