using System;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Textures;

namespace KSPVE.Pure.Seasons;

/// <summary>The constants of the snow pack model (real values, with where they come from).</summary>
public sealed class SnowParameters
{
    /// <summary>
    /// How much precipitation the world gets compared with Earth (0 none .. 1 Earth's: a wet world with oceans and clouds;
    /// a dry one with an atmosphere but no water cycle gets a little frost).
    /// </summary>
    public double Moisture = 1.0;
    /// <summary>The wettest zonal mean precipitation (m of water a year; Earth's tropics about 2).</summary>
    public double WettestAnnual = 2.2;
    /// <summary>Earth's year (s), the unit of the annual figures above.</summary>
    public const double EarthYear = 3.15576e7;
    /// <summary>Melt for each degree-day above freezing: 4 mm of water a degree a day (snow, 2 to 6 in the literature).</summary>
    public double DegreeDayFactor = 4.0e-3 / 86400.0;
    /// <summary>The water equivalent (m) at which a surface is 63 percent covered (a few cm of snow: 2.5 cm of water is about 10 cm of snow).</summary>
    public double CoverScale = 0.025;
    /// <summary>The snow depth (m of water) the snow line is where it reaches.</summary>
    public double LineDepth = 0.03;
    /// <summary>A glacier's thickness cap (m of water): ice flows away beyond it.</summary>
    public double IceCap = 30.0;
    /// <summary>The highest altitude the pack is worked out to (m).</summary>
    public double TopAltitude = 16000.0;
}

/// <summary>
/// The snow pack of a planet over its year by latitude and height: a degree-day model (snow falls where the air is below
/// freezing and melts by degrees above it), run year after year until it repeats, from the planet's own temperatures
/// (<see cref="PlanetClimate"/>) and a precipitation climatology. Where the pack never melts it keeps growing: that is a
/// glacier (up to a cap), and a place with ice all year is a perennial one.
/// </summary>
public sealed class SnowClimatology
{
    public const int Phases = 48;
    public const int Latitudes = 72;
    public const int Years = 40;

    private readonly SnowParameters _p;
    private readonly PlanetClimate _climate;
    private readonly int _bands;
    private readonly float[] _swe;         // [phase][latitude][altitude]
    private readonly float[] _perennial;   // [latitude][altitude]

    public SnowParameters Parameters => _p;
    public PlanetClimate Climate => _climate;
    public int AltitudeBands => _bands;
    public double AltitudeStep => _p.TopAltitude / (_bands - 1);

    public static double Latitude(int i) => -0.5 * Math.PI + (i + 0.5) * Math.PI / Latitudes;

    /// <param name="precipitation">Water falling at sea level (m/s) at a latitude and fraction of the year.</param>
    public SnowClimatology(PlanetClimate climate, SnowParameters parameters, Func<double, double, double> precipitation)
    {
        _climate = climate;
        _p = parameters;
        _bands = 65;
        _swe = new float[Phases * Latitudes * _bands];
        _perennial = new float[Latitudes * _bands];
        double period = climate.Orbit.Period;
        double dt = period / Phases;
        var rate = new double[Phases];
        var minimum = new double[_bands];
        for (int i = 0; i < Latitudes; i++)
        {
            double lat = Latitude(i);
            for (int k = 0; k < Phases; k++) rate[k] = Math.Max(precipitation(lat, (k + 0.5) / Phases), 0.0);
            var tz = new float[Phases * _bands];   // degrees C
            for (int k = 0; k < Phases; k++)
                for (int j = 0; j < _bands; j++)
                    tz[k * _bands + j] = (float)(climate.Temperature(lat, (k + 0.5) / Phases, j * AltitudeStep) - 273.15);
            RunRecorded(i, tz, rate, dt, minimum);
            for (int j = 0; j < _bands; j++)
            {
                // Ice that survives its summers; or, where the year's mean is below -6 C (fully so below -12 C), an ice sheet
                // that keeps itself (its own height and brightness hold off the melt: Greenland, Antarctica).
                double mean = 0.0;
                for (int k = 0; k < Phases; k++) mean += tz[k * _bands + j];
                mean /= Phases;
                _perennial[i * _bands + j] = (float)Math.Max(Smooth(0.1, 1.5, minimum[j]), Smooth(6.0, 12.0, -mean));
            }
        }
    }

    /// <summary>Runs the years of one latitude until the pack repeats (or the glaciers have grown for the whole run), keeping the last year's phases.</summary>
    private void RunRecorded(int lat, float[] tz, double[] rate, double dt, double[] minimum)
    {
        var state = new double[_bands];
        var begin = new double[_bands];
        var record = new float[Phases * _bands];
        for (int year = 0; year < Years; year++)
        {
            for (int j = 0; j < _bands; j++) minimum[j] = double.MaxValue;
            for (int j = 0; j < _bands; j++) begin[j] = state[j];
            for (int k = 0; k < Phases; k++)
                for (int j = 0; j < _bands; j++)
                {
                    double z = j * AltitudeStep;
                    double tc = tz[k * _bands + j];
                    // Precipitation is greatest a little up the slopes (orographic lift) and dies away aloft.
                    double orography = (1.0 + z / 2000.0) * Math.Exp(-z / 4000.0);
                    double next = state[j] + rate[k] * orography * SnowFraction(tc) * dt - _p.DegreeDayFactor * Math.Max(tc, 0.0) * dt;
                    next = Math.Min(Math.Max(next, 0.0), _p.IceCap);
                    state[j] = next;
                    minimum[j] = Math.Min(minimum[j], next);
                    record[k * _bands + j] = (float)next;
                }
            double change = 0.0;
            for (int j = 0; j < _bands; j++) change = Math.Max(change, Math.Abs(state[j] - begin[j]));
            if (year >= 3 && change < 1e-4) break;
        }
        for (int k = 0; k < Phases; k++)
            Array.Copy(record, k * _bands, _swe, (k * Latitudes + lat) * _bands, _bands);
    }

    /// <summary>The share of precipitation that falls as snow at a temperature (C): all of it at 0 and below, none from 2 C up.</summary>
    public static double SnowFraction(double celsius) => celsius <= 0.0 ? 1.0 : celsius >= 2.0 ? 0.0 : 1.0 - celsius / 2.0;

    /// <summary>The cover (0..1) a depth of snow gives (m of water).</summary>
    public double Cover(double depth) => 1.0 - Math.Exp(-Math.Max(depth, 0.0) / _p.CoverScale);

    private static double Smooth(double a, double b, double x)
    {
        double t = Math.Min(Math.Max((x - a) / (b - a), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// The snow pack (m of water) against height at a latitude, a fraction of the year, into <paramref name="into"/>
    /// (<see cref="AltitudeBands"/> values, interpolated between the table's latitudes and phases).
    /// </summary>
    public void Profile(double latitude, double yearFraction, float[] into)
    {
        double s = yearFraction - Math.Floor(yearFraction);
        double x = s * Phases - 0.5;
        int k0 = (int)Math.Floor(x);
        double fk = x - k0;
        int k1 = ((k0 + 1) % Phases + Phases) % Phases;
        k0 = (k0 % Phases + Phases) % Phases;
        double y = (latitude + 0.5 * Math.PI) / Math.PI * Latitudes - 0.5;
        int l0 = (int)Math.Floor(y);
        double fl = y - l0;
        int l1 = Math.Min(Math.Max(l0 + 1, 0), Latitudes - 1);
        l0 = Math.Min(Math.Max(l0, 0), Latitudes - 1);
        for (int j = 0; j < _bands; j++)
        {
            double a = Mix(_swe[(k0 * Latitudes + l0) * _bands + j], _swe[(k0 * Latitudes + l1) * _bands + j], fl);
            double b = Mix(_swe[(k1 * Latitudes + l0) * _bands + j], _swe[(k1 * Latitudes + l1) * _bands + j], fl);
            into[j] = (float)Mix(a, b, fk);
        }
    }

    /// <summary>How much of the pack is perennial ice against height at a latitude (0..1), into <paramref name="into"/>.</summary>
    public void IceProfile(double latitude, float[] into)
    {
        double y = (latitude + 0.5 * Math.PI) / Math.PI * Latitudes - 0.5;
        int l0 = (int)Math.Floor(y);
        double fl = y - l0;
        int l1 = Math.Min(Math.Max(l0 + 1, 0), Latitudes - 1);
        l0 = Math.Min(Math.Max(l0, 0), Latitudes - 1);
        for (int j = 0; j < _bands; j++) into[j] = (float)Mix(_perennial[l0 * _bands + j], _perennial[l1 * _bands + j], fl);
    }

    /// <summary>A value from a profile at an altitude (m; below the sea is the sea level's, above the top the top's).</summary>
    public double Interpolate(float[] profile, double altitude)
    {
        double x = Math.Min(Math.Max(altitude, 0.0), _p.TopAltitude) / AltitudeStep;
        int j = Math.Min((int)x, _bands - 2);
        return Mix(profile[j], profile[j + 1], x - j);
    }

    /// <summary>
    /// The height (m) at which a profile reaches <paramref name="depth"/>, coming down from the top (-1e5: it is snowy even at
    /// sea level; 1e9: nowhere).
    /// </summary>
    public double LineOf(float[] profile, double depth)
    {
        int top = _bands - 1;
        if (profile[top] < depth) return 1e9;
        for (int j = top - 1; j >= 0; j--)
        {
            if (profile[j] >= depth) continue;
            double f = (depth - profile[j]) / Math.Max(profile[j + 1] - profile[j], 1e-9);
            return (j + f) * AltitudeStep;
        }
        return -1e5;
    }

    private static double Mix(double a, double b, double f) => a + (b - a) * f;
}

/// <summary>
/// The snowfall of the last days from the weather model, at coarse steps (a few times back over a window), worked out a few
/// rows at a time so a frame is never held up. Each step takes the weather's precipitation as an intensity (1 = 2.5 mm of
/// water an hour, a heavy snowfall) held for the step, weighted down with age.
/// </summary>
public sealed class RecentPrecipitation
{
    public const double HeavyRate = 2.5e-3 / 3600.0;   // m/s of water

    private readonly Weather _weather;
    private readonly int _width, _height, _steps;
    private readonly float[] _accumulating, _result;
    private int _step = -1, _cell;
    private double _now, _window, _scale;
    private Func<double, double> _longitude, _latitude;

    public RecentPrecipitation(Weather weather, int width = 64, int height = 32, int steps = 6)
    {
        _weather = weather;
        _width = width;
        _height = height;
        _steps = steps;
        _accumulating = new float[width * height];
        _result = new float[width * height];
        Map = new GreyMap(width, height, _result, false);
    }

    /// <summary>The potential snowfall (m of water) of the window, by latitude and longitude (row 0 the south).</summary>
    public GreyMap Map { get; }

    /// <summary>A pass is under way.</summary>
    public bool Busy => _step >= 0;

    /// <summary>Starts a pass over the <paramref name="window"/> (s) before <paramref name="now"/>.</summary>
    public void Begin(double now, double window, double scale, Func<double, double> subsolarLongitude, Func<double, double> subsolarLatitude)
    {
        _now = now;
        _window = window;
        _scale = scale;
        _longitude = subsolarLongitude;
        _latitude = subsolarLatitude;
        Array.Clear(_accumulating, 0, _accumulating.Length);
        _step = 0;
        _cell = 0;
    }

    /// <summary>Samples up to <paramref name="cells"/> more cells of the weather; true when the pass is done (the map then holds the result).</summary>
    public bool Step(int cells)
    {
        if (_step < 0) return true;
        while (cells > 0 && _step < _steps)
        {
            double age = _window * (_step + 0.5) / _steps;
            double time = _now - age;
            if (_cell == 0) _weather.Prepare(time, _longitude(time), _latitude(time));
            double weight = 1.0 - age / _window;
            double dt = _window / _steps;
            for (; _cell < _width * _height && cells > 0; _cell++, cells--)
            {
                int row = _cell / _width, col = _cell % _width;
                double lat = -0.5 * Math.PI + (row + 0.5) * Math.PI / _height, lon = -Math.PI + (col + 0.5) * 2.0 * Math.PI / _width;
                WeatherSample w = _weather.Sample(lat, lon);
                _accumulating[_cell] += (float)(w.Precipitation * HeavyRate * dt * weight * _scale);
            }
            if (_cell >= _width * _height) { _step++; _cell = 0; }
        }
        if (_step < _steps) return false;
        Array.Copy(_accumulating, _result, _result.Length);
        Map.Changed();
        _step = -1;
        return true;
    }
}

/// <summary>
/// A planet's snow, ice and sea ice maps (latitude by longitude, row 0 the south, column 0 at longitude -pi), from the
/// snow pack, the terrain's height and the recent snowfall. Cover is 0..1; the snow line is the height (m) from which the
/// ground is snowy now.
/// </summary>
public sealed class SnowField
{
    public const double SeaWater = 271.35;   // K: seawater freezes at -1.8 C

    private readonly SnowClimatology _snow;
    private readonly GreyMap _terrain;
    private readonly bool _ocean, _flatSea;
    private readonly float[] _profile, _iceProfile;
    private readonly float[] _tProfile;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Snow cover on the land (at the terrain's height), perennial ice (glaciers and ice caps), sea ice.</summary>
    public GreyMap Snow { get; }
    public GreyMap Ice { get; }
    public GreyMap SeaIce { get; }
    /// <summary>The snow line (m above the sea; clamped to +-1e5) and the terrain's height (m), for the local terrain shader.</summary>
    public GreyMap SnowLine { get; }
    public GreyMap Terrain => _terrain;

    /// <param name="terrain">Height above the sea (m) by latitude and longitude (a coarse map); null: a world that is all sea (or, with no ocean, all flat land at sea level).</param>
    /// <param name="ocean">The world has a water ocean at height 0.</param>
    public SnowField(SnowClimatology snow, GreyMap terrain, bool ocean, int width = 256, int height = 128)
    {
        _snow = snow;
        _flatSea = terrain == null;
        _terrain = terrain ?? new GreyMap(2, 2, new float[4], false);
        _ocean = ocean;
        Width = width;
        Height = height;
        Snow = new GreyMap(width, height, new float[width * height], false);
        Ice = new GreyMap(width, height, new float[width * height], false);
        SeaIce = new GreyMap(width, height, new float[width * height], false);
        SnowLine = new GreyMap(width, height, new float[width * height], false);
        _profile = new float[snow.AltitudeBands];
        _iceProfile = new float[snow.AltitudeBands];
        _tProfile = new float[snow.AltitudeBands];
    }

    /// <summary>Works out rows <paramref name="firstRow"/> up to (not including) <paramref name="endRow"/> for a time of the year, with recent snowfall (or none).</summary>
    public void UpdateRows(int firstRow, int endRow, double yearFraction, GreyMap fresh)
    {
        PlanetClimate climate = _snow.Climate;
        double step = _snow.AltitudeStep;
        for (int row = firstRow; row < endRow; row++)
        {
            double lat = -0.5 * Math.PI + (row + 0.5) * Math.PI / Height;
            double v = (row + 0.5) / Height;
            _snow.Profile(lat, yearFraction, _profile);
            _snow.IceProfile(lat, _iceProfile);
            for (int j = 0; j < _tProfile.Length; j++) _tProfile[j] = (float)(climate.Temperature(lat, yearFraction, j * step) - 273.15);
            double seaLevel = climate.SeaLevel(lat, yearFraction);
            // Ice forms where the air is below the sea's freezing point, thickening in cover as it gets colder (concentration
            // grows over some 10 K, not at once: leads, polynyas and floes).
            double seaIce = _ocean ? Ramp((SeaWater + 3.0 - seaLevel) / 10.0) : 0.0;
            for (int col = 0; col < Width; col++)
            {
                double u = (col + 0.5) / Width;
                double z = _terrain.Sample(u, v);
                int i = row * Width + col;
                bool sea = _ocean && (_flatSea || z < 0.0);
                double tc = _snow.Interpolate(_tProfile, z);
                double freshSwe = fresh != null ? Math.Max(fresh.Sample(u, v), 0.0) : 0.0;
                // Fresh snow lies where it is freezing now; a thaw takes it off in a day or two.
                double lies = tc <= 0.0 ? 1.0 : tc >= 1.5 ? 0.0 : 1.0 - tc / 1.5;
                double swe = _snow.Interpolate(_profile, z) + freshSwe * lies;
                Snow.Values[i] = sea ? 0f : (float)_snow.Cover(swe);
                Ice.Values[i] = sea ? 0f : (float)_snow.Interpolate(_iceProfile, z);
                SeaIce.Values[i] = sea ? (float)seaIce : 0f;
                double line = _snow.LineOf(_profile, _snow.Parameters.LineDepth);
                if (freshSwe * 1.0 >= _snow.Parameters.LineDepth)
                {
                    // Fresh snow covers everything above the freezing level.
                    double freezing = climate.FreezingAltitude(lat, yearFraction, _snow.Parameters.TopAltitude);
                    line = Math.Min(line, Math.Max(freezing, -1e5));
                }
                SnowLine.Values[i] = (float)Math.Max(-1e5, Math.Min(1e5, line));
            }
        }
    }

    /// <summary>Marks the maps as new (after the last rows).</summary>
    public void Finish()
    {
        Snow.Changed();
        Ice.Changed();
        SeaIce.Changed();
        SnowLine.Changed();
    }

    /// <summary>Works out the whole maps now.</summary>
    public void Update(double yearFraction, GreyMap fresh)
    {
        UpdateRows(0, Height, yearFraction, fresh);
        Finish();
    }

    private static double Ramp(double x) { x = Math.Min(Math.Max(x, 0.0), 1.0); return x * x * (3.0 - 2.0 * x); }
}
