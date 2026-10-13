using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Clouds;

/// <summary>A planet's climate, for its weather (<see cref="Weather"/>).</summary>
public sealed class Climate
{
    /// <summary>Radius (m) and sidereal rotation period (s; negative: it turns the other way).</summary>
    public double Radius = 600000.0, RotationPeriod = 21549.4;
    /// <summary>How moist the air is overall (0 desert world .. 1 water world), and how prone to storms.</summary>
    public double Humidity = 0.6, Storminess = 0.5;
    /// <summary>How many pressure systems there are, relative to an Earth-like planet's.</summary>
    public double SystemDensity = 1.0;
    /// <summary>A pressure system's radius (radians of the planet), and how long one lasts (in rotations of the planet).</summary>
    public double SystemRadius = 0.28, SystemLifetimeRotations = 5.0;
    /// <summary>A thunderstorm cell's radius (m) and life (s).</summary>
    public double StormRadius = 30000.0, StormLifetime = 7200.0;
    /// <summary>Thunderstorms per day per 1e12 m^2 of moist tropics, at full storminess.</summary>
    public double StormRate = 6.0;
    public ulong Seed = 1;

    /// <summary>Earth's weather scaled to a planet: systems as big, relative to the planet, as its spin and size allow.</summary>
    public static Climate For(double radius, double rotationPeriod, ulong seed)
    {
        var c = new Climate { Radius = radius, RotationPeriod = rotationPeriod, Seed = seed };
        // The Rossby radius (how big a weather system can be) shrinks as the planet spins faster: Earth's lows are about
        // 1000 km across a 6371 km planet that turns once in 86164 s. Kept within a range that reads well.
        double spin = Math.Abs(rotationPeriod) > 0.0 ? 86164.0 / Math.Abs(rotationPeriod) : 1.0;
        double size = 0.16 * Math.Sqrt(6371000.0 / Math.Max(radius, 1.0)) / Math.Max(spin, 0.05);
        c.SystemRadius = Math.Min(Math.Max(size, 0.15), 0.5);
        return c;
    }
}

/// <summary>The weather at a place and time.</summary>
public struct WeatherSample
{
    /// <summary>Moisture (0 dry, clear .. 1 saturated, overcast).</summary>
    public double Humidity;
    /// <summary>Convective storms (0 none .. 1 a mature thunderstorm): cumulonimbus, lightning, downpours.</summary>
    public double Storm;
    /// <summary>Rain or snow falling (0 none .. 1 heavy).</summary>
    public double Precipitation;
    /// <summary>Wind (m/s): east and north.</summary>
    public double WindEast, WindNorth;
}

/// <summary>
/// A planet's weather, worked out from the game time alone (no state, so time warp and reloads always agree): a climate
/// by latitude, with the prevailing winds of a three-cell circulation (trade winds, westerlies, polar easterlies; one cell
/// a hemisphere on a slowly turning planet); pressure systems born in their bands, drifting with the wind, turning the way
/// the planet's spin makes them (lows anticlockwise in the north on a prograde planet), growing and dying, a low trailing a
/// spiral front; and thunderstorms where the air is warm and moist, mostly in the afternoon. Latitude and longitude are on
/// the turning planet (radians, longitude from -pi to pi).
/// </summary>
public sealed class Weather
{
    private readonly Climate _climate;
    private readonly List<WeatherSystem> _systems = new List<WeatherSystem>();
    private readonly List<StormCell> _storms = new List<StormCell>();
    private double _preparedTime = double.NaN, _preparedSubsolar, _preparedSubsolarLatitude;

    public Weather(Climate climate)
    {
        _climate = climate;
    }

    public Climate Climate => _climate;

    /// <summary>+1 when the planet turns prograde (west to east), -1 retrograde.</summary>
    public double Spin => _climate.RotationPeriod < 0 ? -1.0 : 1.0;

    /// <summary>Days (rotations) are long enough that one circulation cell spans a hemisphere (Venus, Titan), rather than three.</summary>
    public bool SlowRotator => Math.Abs(_climate.RotationPeriod) > 10.0 * 86400.0;

    // ---------------------------------------------------------------- climate

    /// <summary>
    /// The latitude the sun is overhead at (rad; the season, from the body's axial tilt), as last prepared. The climate's
    /// belts follow it about halfway, as Earth's rain belt moves some 10 degrees either side of the equator over a year.
    /// </summary>
    public double SubsolarLatitude => _preparedSubsolarLatitude;

    /// <summary>The share of the sun's latitude the climate's belts move by.</summary>
    public const double SeasonalShift = 0.45;

    /// <summary>A latitude measured from the climate's (seasonally moved) equator.</summary>
    private double Seasonal(double latitude) => latitude - SeasonalShift * _preparedSubsolarLatitude;

    /// <summary>The climate's moisture at a latitude: a wet equator, dry subtropics, wet mid-latitude storm tracks, drier poles.</summary>
    public double ClimateHumidity(double latitude)
    {
        double a = Math.Abs(Seasonal(latitude)) * 180.0 / Math.PI;
        double h;
        if (SlowRotator)
            h = 0.55 + 0.25 * Math.Exp(-Sq(a / 20.0)) - 0.15 * Math.Exp(-Sq((a - 60.0) / 20.0));
        else
            h = 0.48 + 0.32 * Math.Exp(-Sq(a / 9.0)) - 0.30 * Math.Exp(-Sq((a - 26.0) / 9.0)) + 0.18 * Math.Exp(-Sq((a - 55.0) / 12.0)) - 0.12 * Math.Exp(-Sq((a - 85.0) / 10.0));
        return Clamp01(h + (_climate.Humidity - 0.6) * 0.8);
    }

    /// <summary>The prevailing wind's eastward speed at a latitude (m/s; trade winds blow west, westerlies east, polar easterlies west).</summary>
    public double PrevailingWind(double latitude)
    {
        double a = Math.Abs(Seasonal(latitude)) * 180.0 / Math.PI;
        double u;
        // Continuous across the cells' edges (calm at 30 and 62 degrees), so nothing carried by it ever jumps.
        if (SlowRotator) u = 12.0 * Math.Sin(Math.PI * a / 90.0);   // a single cell: east aloft everywhere, strongest at mid latitudes
        else if (a < 30.0) u = -6.0 * Math.Sin(Math.PI * (a + 10.0) / 40.0);
        else if (a < 62.0) u = 13.0 * Math.Sin(Math.PI * (a - 30.0) / 32.0);
        else u = -5.0 * Math.Sin(Math.PI * (a - 62.0) / 28.0);
        return u * Spin;
    }

    // ---------------------------------------------------------------- systems

    /// <summary>A pressure system: a low (wet, stormy front) or a high (dry, clear).</summary>
    public readonly struct WeatherSystem
    {
        /// <summary>The same for one system all its life.</summary>
        public readonly ulong Id;
        public readonly double Latitude, Longitude, Radius, Strength;
        public readonly bool IsLow;
        /// <summary>+1 anticlockwise seen from above, -1 clockwise.</summary>
        public readonly double Turning;
        public readonly double Phase;   // 0 at birth .. 1 at death

        public WeatherSystem(ulong id, double latitude, double longitude, double radius, double strength, bool isLow, double turning, double phase)
        {
            Id = id; Latitude = latitude; Longitude = longitude; Radius = radius; Strength = strength; IsLow = isLow; Turning = turning; Phase = phase;
        }
    }

    /// <summary>A thunderstorm cell.</summary>
    public readonly struct StormCell
    {
        public readonly double Latitude, Longitude, Radius, Strength;
        public StormCell(double latitude, double longitude, double radius, double strength) { Latitude = latitude; Longitude = longitude; Radius = radius; Strength = strength; }
    }

    public IReadOnlyList<WeatherSystem> Systems => _systems;
    public IReadOnlyList<StormCell> Storms => _storms;

    // Each hemisphere has bands systems are born in: lows in the storm tracks (and the tropics), highs in the subtropics.
    private static readonly double[] BandLatitudes = { 12.0, 28.0, 50.0, 68.0 };
    private static readonly double[] LowShare = { 0.55, 0.15, 0.85, 0.6 };
    private static readonly double[] BandRate = { 0.6, 1.0, 1.4, 0.6 };

    /// <summary>
    /// Works out the systems and storms alive at game time <paramref name="time"/> (s), with the sun over longitude
    /// <paramref name="subsolarLongitude"/> (on the turning planet). Call before sampling a new time.
    /// </summary>
    public void Prepare(double time, double subsolarLongitude) => Prepare(time, subsolarLongitude, 0.0);

    /// <summary>As <see cref="Prepare(double, double)"/>, in a season: the sun overhead at <paramref name="subsolarLatitude"/> (rad).</summary>
    public void Prepare(double time, double subsolarLongitude, double subsolarLatitude)
    {
        if (time == _preparedTime && subsolarLongitude == _preparedSubsolar && subsolarLatitude == _preparedSubsolarLatitude) return;
        _preparedTime = time;
        _preparedSubsolar = subsolarLongitude;
        _preparedSubsolarLatitude = subsolarLatitude;
        _systems.Clear();
        _storms.Clear();
        double rotation = Math.Max(Math.Abs(_climate.RotationPeriod), 600.0);
        double life = Math.Max(_climate.SystemLifetimeRotations, 0.5) * rotation;
        double slot = life / 3.0;   // births in slots a third of a life long: up to three generations overlap
        long now = (long)Math.Floor(time / slot);
        for (int hemisphere = -1; hemisphere <= 1; hemisphere += 2)
        {
            for (int band = 0; band < BandLatitudes.Length; band++)
            {
                int perSlot = Math.Max(1, (int)Math.Round(BandRate[band] * _climate.SystemDensity * 3.0));
                for (long s = now - 3; s <= now; s++)
                {
                    for (int k = 0; k < perSlot; k++)
                    {
                        ulong key = Key(_climate.Seed, (ulong)(hemisphere + 2), (ulong)band, (ulong)s, (ulong)k);
                        double birth = (s + Unit(key, 1)) * slot;
                        double lifetime = life * (0.6 + 0.8 * Unit(key, 2));
                        double age = time - birth;
                        if (age < 0.0 || age > lifetime) continue;
                        double latitude0 = hemisphere * (BandLatitudes[band] + 8.0 * (Unit(key, 3) - 0.5)) * Math.PI / 180.0;
                        double longitude0 = (Unit(key, 4) * 2.0 - 1.0) * Math.PI;
                        bool isLow = Unit(key, 5) < LowShare[band];
                        // It drifts with the wind of its latitude, slowly poleward if a low: the longitude is the wind
                        // integrated along that path (Simpson's rule), so it always moves the way the wind blows where it is.
                        double poleward = isLow ? hemisphere * 0.15 / lifetime : 0.0;
                        double latitude = latitude0 + poleward * age;
                        double longitude = Wrap(longitude0 + Travelled(latitude0, poleward, age, 0.8));
                        double phase = age / lifetime;
                        double strength = Math.Sin(Math.PI * phase) * (0.6 + 0.6 * Unit(key, 6));
                        double radius = _climate.SystemRadius * (0.7 + 0.6 * Unit(key, 7));
                        // Coriolis: on a prograde planet lows turn anticlockwise in the north, highs the other way.
                        double turning = Spin * hemisphere * (isLow ? 1.0 : -1.0);
                        _systems.Add(new WeatherSystem(key, latitude, longitude, radius, strength, isLow, turning, phase));
                    }
                }
            }
        }
        PrepareStorms(time, subsolarLongitude);
    }

    private void PrepareStorms(double time, double subsolar)
    {
        if (_climate.Storminess <= 0.0) return;
        // Cells are born on a grid of patches (each about four storm radii across) in slots a storm's life long; a patch's
        // chance grows with how moist it is and with the afternoon.
        double patch = 4.0 * _climate.StormRadius / _climate.Radius;   // radians
        int rows = Math.Max(1, (int)(Math.PI / patch));
        double life = Math.Max(_climate.StormLifetime, 60.0);
        long now = (long)Math.Floor(time / life);
        double area = Sq(patch * _climate.Radius);
        double chancePerSlot = _climate.StormRate * _climate.Storminess * (area / 1e12) * (life / 86400.0) * 20.0;
        for (int row = 0; row < rows; row++)
        {
            double latitude = -0.5 * Math.PI + (row + 0.5) * Math.PI / rows;
            double humid = ClimateHumidity(latitude);
            if (humid < 0.45) continue;
            int columns = Math.Max(1, (int)(2.0 * Math.PI * Math.Cos(latitude) / patch));
            double warmth = Math.Cos(latitude);
            for (int col = 0; col < columns; col++)
            {
                double longitude = -Math.PI + (col + 0.5) * 2.0 * Math.PI / columns;
                // Local solar time: noon where the sun is overhead; storms favour mid-afternoon.
                double hour = Wrap(longitude - subsolar) / Math.PI * 12.0 + 12.0;
                double afternoon = 0.35 + 0.65 * Math.Exp(-Sq((hour - 15.5) / 3.5));
                double chance = chancePerSlot * Sq(Clamp01((humid - 0.45) / 0.4)) * warmth * afternoon;
                if (chance <= 0.0) continue;
                for (long s = now - 1; s <= now; s++)
                {
                    ulong key = Key(_climate.Seed, 7, (ulong)row, (ulong)col, (ulong)s);
                    if (Unit(key, 1) >= chance) continue;
                    double birth = (s + Unit(key, 2)) * life;
                    double age = time - birth;
                    if (age < 0.0 || age > life) continue;
                    double phase = age / life;
                    double lat = latitude + (Unit(key, 3) - 0.5) * patch;
                    double drift = 0.6 * PrevailingWind(lat) / (_climate.Radius * Math.Max(Math.Cos(lat), 0.15));
                    double lon = Wrap(longitude + (Unit(key, 4) - 0.5) * patch / Math.Max(Math.Cos(lat), 0.15) + drift * age);
                    // Grows fast, matures, rains out and collapses.
                    double strength = Math.Pow(Math.Sin(Math.PI * Math.Pow(phase, 0.7)), 1.5) * (0.6 + 0.4 * Unit(key, 5));
                    double radius = _climate.StormRadius * (0.6 + 0.8 * Unit(key, 6)) / _climate.Radius;
                    _storms.Add(new StormCell(lat, lon, radius, strength));
                }
            }
        }
    }

    // ---------------------------------------------------------------- sampling

    /// <summary>The weather at <paramref name="latitude"/>, <paramref name="longitude"/> at the prepared time.</summary>
    public WeatherSample Sample(double latitude, double longitude)
    {
        var w = new WeatherSample { Humidity = ClimateHumidity(latitude), WindEast = PrevailingWind(latitude) };
        double front = 0.0;
        double sinLat = Math.Sin(latitude), cosLat = Math.Cos(latitude);
        foreach (WeatherSystem s in _systems)
        {
            // Offset from the system's centre on a local tangent plane (x east, y north, radians of the planet).
            double cosD = sinLat * Math.Sin(s.Latitude) + cosLat * Math.Cos(s.Latitude) * Math.Cos(longitude - s.Longitude);
            double d = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosD)));
            if (d > 2.5 * s.Radius) continue;
            double x = Math.Cos(s.Latitude) * Math.Sin(longitude - s.Longitude);
            double y = Math.Sin(latitude - s.Latitude);
            double q = d / s.Radius;
            double core = Math.Exp(-q * q);
            if (s.IsLow)
            {
                // A comma: moist air wound into the low along a spiral, densest on its leading side, a front trailing out.
                double angle = Math.Atan2(y, x);
                double spiral = Math.Cos(angle - s.Turning * 2.2 * Math.Log(q + 0.15) + 0.6);
                double band = Math.Exp(-Sq((spiral - 1.0) / 0.55)) * Math.Exp(-Sq(q / 1.6));
                w.Humidity += s.Strength * (0.35 * core + 0.45 * band);
                front += s.Strength * band * (0.6 + 0.4 * (1.0 - s.Phase));
            }
            else
                w.Humidity -= s.Strength * 0.45 * core;
            // Winds round the system (geostrophic: anticlockwise round a northern low on a prograde planet).
            double swirl = s.Strength * 9.0 * q * Math.Exp(-q * q) / Math.Max(d, 1e-6);
            w.WindEast += -s.Turning * swirl * y;
            w.WindNorth += s.Turning * swirl * x;
        }
        double storm = 0.0;
        foreach (StormCell c in _storms)
        {
            double cosD = sinLat * Math.Sin(c.Latitude) + cosLat * Math.Cos(c.Latitude) * Math.Cos(longitude - c.Longitude);
            double d = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosD)));
            if (d > 3.0 * c.Radius) continue;
            storm = Math.Max(storm, c.Strength * Math.Exp(-Sq(d / c.Radius)));
        }
        // Fronts embed storms; moist air rises in them.
        storm = Math.Max(storm, Clamp01(front - 0.35) * _climate.Storminess * 1.2);
        w.Humidity = Clamp01(w.Humidity + 0.4 * storm);
        w.Storm = Clamp01(storm);
        w.Precipitation = Clamp01(Math.Max(SmoothStep(0.72, 1.0, w.Humidity) * Clamp01(front * 1.5 + 0.2), w.Storm * 1.1));
        return w;
    }

    /// <summary>
    /// The weather on a <paramref name="width"/> x <paramref name="height"/> latitude-longitude map, into
    /// <paramref name="rgba"/> (4 floats a texel: humidity, storm, precipitation, wind speed / 50 m/s; row 0 the south
    /// pole's, column 0 at longitude -pi), at the prepared time.
    /// </summary>
    public void FillMap(int width, int height, float[] rgba)
    {
        for (int row = 0; row < height; row++)
        {
            double latitude = -0.5 * Math.PI + (row + 0.5) * Math.PI / height;
            for (int col = 0; col < width; col++)
            {
                double longitude = -Math.PI + (col + 0.5) * 2.0 * Math.PI / width;
                WeatherSample w = Sample(latitude, longitude);
                int i = 4 * (row * width + col);
                rgba[i] = (float)w.Humidity;
                rgba[i + 1] = (float)w.Storm;
                rgba[i + 2] = (float)w.Precipitation;
                rgba[i + 3] = (float)Math.Min(Math.Sqrt(w.WindEast * w.WindEast + w.WindNorth * w.WindNorth) / 50.0, 1.0);
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static double Sq(double x) => x * x;
    private static double Clamp01(double x) => x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
    private static double SmoothStep(double a, double b, double x) { double t = Clamp01((x - a) / (b - a)); return t * t * (3.0 - 2.0 * t); }
    /// <summary>
    /// The longitude (rad) something carried at <paramref name="share"/> of the prevailing wind travels in
    /// <paramref name="age"/> seconds, starting at <paramref name="latitude0"/> and moving <paramref name="poleward"/> rad/s.
    /// </summary>
    private double Travelled(double latitude0, double poleward, double age, double share)
    {
        double Rate(double a)
        {
            double lat = latitude0 + poleward * a;
            return share * PrevailingWind(lat) / (_climate.Radius * Math.Max(Math.Cos(lat), 0.15));
        }
        if (poleward == 0.0) return Rate(0.0) * age;
        const int Steps = 16;
        double h = age / Steps, sum = Rate(0.0) + Rate(age);
        for (int i = 1; i < Steps; i++) sum += (i % 2 == 1 ? 4.0 : 2.0) * Rate(i * h);
        return sum * h / 3.0;
    }

    private static double Wrap(double angle) => angle - 2.0 * Math.PI * Math.Floor((angle + Math.PI) / (2.0 * Math.PI));

    private static ulong Key(ulong seed, ulong a, ulong b, ulong c, ulong d) => Mix(Mix(Mix(Mix(seed ^ a) ^ b * 0x9E3779B97F4A7C15UL) ^ c * 0xC2B2AE3D27D4EB4FUL) ^ d * 0x165667B19E3779F9UL);
    private static double Unit(ulong key, int n) => (Mix(key + (ulong)n * 0x632BE59BD9B4E019UL) >> 11) * (1.0 / (1UL << 53));

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
