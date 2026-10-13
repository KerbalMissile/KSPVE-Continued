using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Seasons;
using KSPVE.Pure.Textures;
using UnityEngine;

namespace KSPVE.Seasonal;

/// <summary>
/// One body's climate, snow and ice, built in stages without ever holding a frame up: the terrain's heights are read
/// from the game a few at a time, then the energy balance and snow pack models (<see cref="PlanetClimate"/>,
/// <see cref="SnowClimatology"/>) are worked out on a worker thread, and from then on the maps (<see cref="SnowField"/>) are
/// refreshed a few rows at a time as the seasons turn and the weather brings snow (<see cref="RecentPrecipitation"/>).
/// </summary>
internal sealed class BodyClimateState
{
    public const int HeightWidth = 128, HeightHeight = 64;

    public readonly CelestialBody Body;
    public readonly string Name;
    public int Revision;
    /// <summary>-1 nothing to do, 0 new, 1 reading heights, 2 working out the models, 3 ready.</summary>
    public volatile int Stage;
    public BodySeason Season;
    public PlanetClimate Climate;
    public SnowClimatology Snow;
    public SnowField Field;
    public bool HasSnow;
    public GreyMap Terrain;
    /// <summary>Goes up each time the maps are refreshed.</summary>
    public int Version;
    public double Moisture;
    public SnowTextures Textures;

    private float[] _heights;
    private int _heightIndex;
    private bool _heightsFailed;
    private RecentPrecipitation _recent;
    private int _phase, _row;
    private double _passYear, _passTime, _lastBegin = -1e9;
    private double _solarDay = 21600.0;

    /// <param name="previous">The body's earlier state, whose terrain heights (the slow part to read) are kept.</param>
    public BodyClimateState(CelestialBody body, int revision, BodyClimateState previous = null)
    {
        Body = body;
        Name = body.bodyName;
        Revision = revision;
        if (previous != null && previous.Terrain != null && previous._heights != null && previous._heightIndex >= HeightWidth * HeightHeight)
        {
            _heights = previous._heights;
            _heightIndex = previous._heightIndex;
            Terrain = previous.Terrain;
            _heightsKept = true;
        }
    }

    /// <summary>The terrain heights are being read (only one body at a time reads them).</summary>
    public bool ReadingHeights => Stage == 1;
    private bool _heightsKept;

    /// <summary>The air temperature (K) now at a latitude (rad), height (m) and hour angle (rad from local noon).</summary>
    public double TemperatureNow(double latitude, double altitude, double hourAngle)
    {
        if (Climate == null || Season == null) return double.NaN;
        double year = BodySeasons.YearFraction(Season, CloudWorld.Time);
        return Climate.Temperature(latitude, year, altitude, hourAngle);
    }

    public double MeanTemperatureNow(double latitude, double altitude)
    {
        if (Climate == null || Season == null) return double.NaN;
        return Climate.Temperature(latitude, BodySeasons.YearFraction(Season, CloudWorld.Time), altitude);
    }

    public void Tick(Stopwatch budget, double milliseconds)
    {
        switch (Stage)
        {
            case 0: Start(); break;
            case 1: ReadHeights(budget, milliseconds); break;
            case 3: Refresh(budget, milliseconds); break;
        }
    }

    private void Start()
    {
        BodyClimateOverride o = ClimateConfig.For(Name);
        Season = BodySeasons.Of(Body);
        bool candidate = Season != null && (o == null || o.Enabled) && !CloudWorld.IsStar(Body);
        if (!candidate) { Stage = -1; return; }
        HasSnow = Body.atmosphere && Body.hasSolidSurface && Body.pqsController != null;
        if (!_heightsKept)
        {
            _heights = new float[HeightWidth * HeightHeight];
            _heightIndex = 0;
        }
        Stage = HasSnow && !_heightsKept ? 1 : 2;
        if (Stage == 2) Compute();
    }

    private void ReadHeights(Stopwatch budget, double milliseconds)
    {
        long start = budget.ElapsedTicks;
        double limit = milliseconds * Stopwatch.Frequency / 1000.0;
        int total = HeightWidth * HeightHeight;
        while (_heightIndex < total && !_heightsFailed)
        {
            int row = _heightIndex / HeightWidth, col = _heightIndex % HeightWidth;
            double lat = -90.0 + (row + 0.5) * 180.0 / HeightHeight, lon = -180.0 + (col + 0.5) * 360.0 / HeightWidth;
            double z = 0.0;
            try
            {
                z = Body.TerrainAltitude(lat, lon, true);
                if (double.IsNaN(z) || Math.Abs(z) > 0.5 * Body.Radius) throw new InvalidOperationException("the terrain height is not usable: " + z);
            }
            catch (Exception ex)
            {
                _heightsFailed = true;
                Log.Warning($"Snow: reading {Name}'s terrain heights failed ({ex.Message}); the snow line works from sea level.");
                Array.Clear(_heights, 0, _heights.Length);
                break;
            }
            _heights[_heightIndex++] = (float)z;
            if ((_heightIndex & 31) == 0 && budget.ElapsedTicks - start > limit) return;
        }
        Terrain = new GreyMap(HeightWidth, HeightHeight, _heights, false);
        Stage = 2;
        Compute();
    }

    private void Compute()
    {
        BodyClimateOverride o = ClimateConfig.For(Name);
        var p = new ClimateParameters();
        p.SolarConstant = Season.SolarConstant;
        p.PressureAtm = Body.atmosphere ? Body.atmospherePressureSeaLevel / 101.325 : 0.0;
        p.GravityRatio = Body.GeeASL > 0.0 ? Body.GeeASL : 0.6;
        if (Body.albedo > 0.05 && Body.albedo < 0.9) p.Albedo = Body.albedo;
        p.IceFeedback = Body.atmosphere;
        double rotation = Math.Abs(Body.rotationPeriod), year = Season.Host.orbit.period;
        if (!Body.rotates || rotation <= 0.0) rotation = year;
        double rate = Body.solarRotationPeriod ? 1.0 / rotation : Math.Abs(1.0 / rotation - Math.Sign(Body.rotationPeriod == 0.0 ? 1.0 : Body.rotationPeriod) / year);
        p.SolarDay = Math.Min(rate > 0.0 ? 1.0 / rate : 1e9, 1e9);
        _solarDay = p.SolarDay;
        double mean = o != null && o.MeanTemperature.HasValue ? o.MeanTemperature.Value : Body.atmosphere ? Body.GetTemperature(0.0) : double.NaN;
        p.MeanTemperature = mean > 20.0 && mean < 2000.0 ? mean : double.NaN;

        AltitudeProfile profile = null;
        if (Body.atmosphere && Body.atmosphereDepth > 0.0)
        {
            var z = new List<double>();
            var t = new List<double>();
            for (int i = 0; i <= 48; i++)
            {
                double f = i / 48.0, alt = Body.atmosphereDepth * f * f;
                z.Add(alt);
                t.Add(Body.GetTemperature(alt));
            }
            profile = new AltitudeProfile(z, t);
        }
        double moisture = o != null && o.Moisture.HasValue ? o.Moisture.Value
            : Body.ocean ? 1.0
            : HasCloudLayers(Body.bodyName) ? 0.5 : 0.1;
        Moisture = Math.Max(moisture, 0.0);
        HasSnow = HasSnow && Moisture > 0.0;
        double gravity = Body.GeeASL > 0.0 ? Body.GeeASL * 9.80665 : 5.0;

        BodySeason season = Season;
        GreyMap terrain = Terrain;
        bool ocean = Body.ocean;
        bool hasSnow = HasSnow;
        double moistureFinal = Moisture;
        float[] heights = HasSnow ? _heights : null;
        double radius = Body.Radius, rotationPeriod = Body.rotationPeriod;
        Task.Run(() =>
        {
            try
            {
                if (ocean && heights != null)
                {
                    p.OceanFraction = new double[EnergyBalance.Bands];
                    for (int b = 0; b < EnergyBalance.Bands; b++)
                    {
                        double lo = -0.5 * Math.PI + b * Math.PI / EnergyBalance.Bands, hi = lo + Math.PI / EnergyBalance.Bands;
                        int r0 = (int)((lo + 0.5 * Math.PI) / Math.PI * HeightHeight), r1 = Math.Max(r0 + 1, (int)((hi + 0.5 * Math.PI) / Math.PI * HeightHeight));
                        int wet = 0, n = 0;
                        for (int r = r0; r < Math.Min(r1, HeightHeight); r++)
                            for (int c = 0; c < HeightWidth; c++) { n++; if (heights[r * HeightWidth + c] < 0f) wet++; }
                        p.OceanFraction[b] = n > 0 ? (double)wet / n : 0.7;
                    }
                }
                else p.OceanShare = ocean ? 0.7 : 0.0;
                var balance = new EnergyBalance(p, season.Active);
                var climate = new PlanetClimate(balance, gravity, profile);
                SnowClimatology snow = null;
                SnowField field = null;
                if (hasSnow)
                {
                    // The belts (which decide where it rains) do not depend on the weather's random systems, so any seed will do.
                    var weather = new Weather(Pure.Clouds.Climate.For(radius, rotationPeriod, 1));
                    var parameters = new SnowParameters { Moisture = moistureFinal };
                    snow = new SnowClimatology(climate, parameters, PrecipitationClimate.From(weather, season.Active, parameters));
                    field = new SnowField(snow, terrain, ocean);
                    field.Update(0.0, null);
                }
                Climate = climate;
                Snow = snow;
                Field = field;
                Stage = 3;
            }
            catch (Exception ex)
            {
                Log.Exception($"Working out {Name}'s climate failed", ex);
                Stage = -1;
            }
        });
    }

    /// <summary>EVE has cloud layers for the body (without building the body's clouds, which loads their maps).</summary>
    private static bool HasCloudLayers(string body)
    {
        foreach (CloudLayer layer in CloudWorld.Set.Layers)
            if (string.Equals(layer.Body, body, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void Refresh(Stopwatch budget, double milliseconds)
    {
        if (!HasSnow || Field == null) return;
        double now = Time.realtimeSinceStartup;
        if (_phase == 0)
        {
            if (now - _lastBegin < 4.0) return;
            _lastBegin = now;
            _passTime = CloudWorld.Time;
            _passYear = BodySeasons.YearFraction(Season, _passTime);
            _phase = 1;
            _row = 0;
            // Recent snowfall needs the very weather the clouds show, so only for the body the player is at.
            BodyClouds clouds = Body == FlightGlobals.currentMainBody ? CloudWorld.Of(Body) : null;
            double window = Math.Min(ClimateConfig.RecentWindowDays * _solarDay, Season.Host.orbit.period / 24.0);
            if (_recent == null && clouds?.Weather != null && ClimateConfig.RecentWindowDays > 0.0)
                _recent = new RecentPrecipitation(new Weather(clouds.Weather.Climate));
            if (_recent != null && clouds == null) _recent = null;
            if (_recent != null && window > 0.0)
            {
                double lon0 = CloudWorld.SubsolarLongitude(Body), spin = clouds.Weather.Spin, solarDay = _solarDay, t0 = _passTime;
                BodySeason s = Season;
                _recent.Begin(_passTime, window, Math.Min(Moisture, 1.5),
                    t => lon0 - spin * 2.0 * Math.PI * (t - t0) / solarDay,
                    t => s.Active.DeclinationAt(BodySeasons.YearFraction(s, t)));
            }
        }
        if (_phase == 1)
        {
            if (_recent != null && _recent.Busy)
            {
                if (!_recent.Step(500)) return;
            }
            _phase = 2;
        }
        long start = budget.ElapsedTicks;
        double limit = milliseconds * Stopwatch.Frequency / 1000.0;
        while (_row < Field.Height)
        {
            Field.UpdateRows(_row, _row + 4, _passYear, _recent != null ? _recent.Map : null);
            _row += 4;
            if (budget.ElapsedTicks - start > limit) return;
        }
        Field.Finish();
        Version++;
        _phase = 0;
    }
}

/// <summary>The snow, ice and sea ice maps of a body as GPU textures (kept up to date from a <see cref="SnowField"/>).</summary>
internal sealed class SnowTextures
{
    /// <summary>R snow cover, G perennial ice, B sea ice (0..1); latitude by longitude, row 0 the south.</summary>
    public Texture2D Maps;
    /// <summary>R terrain height (m), G snow line (m).</summary>
    public Texture2D Aux;
    private int _snow = -1, _ice = -1, _sea = -1, _line = -1;
    private float[] _mapsData, _auxData;

    public void Sync(SnowField field)
    {
        if (field == null) return;
        int w = field.Width, h = field.Height;
        if (Maps == null || Maps.width != w || Maps.height != h)
        {
            Release();
            Maps = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { name = "KSPVE snow maps", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            Aux = new Texture2D(w, h, TextureFormat.RGFloat, false, true) { name = "KSPVE snow aux", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            _mapsData = new float[w * h * 4];
            _auxData = new float[w * h * 2];
            _snow = _ice = _sea = _line = -1;
        }
        if (field.Snow.Version != _snow || field.Ice.Version != _ice || field.SeaIce.Version != _sea)
        {
            float[] a = field.Snow.Values, b = field.Ice.Values, c = field.SeaIce.Values;
            for (int i = 0; i < w * h; i++)
            {
                _mapsData[4 * i] = a[i];
                _mapsData[4 * i + 1] = b[i];
                _mapsData[4 * i + 2] = c[i];
                _mapsData[4 * i + 3] = 0f;
            }
            Maps.SetPixelData(_mapsData, 0);
            Maps.Apply(false);
            _snow = field.Snow.Version;
            _ice = field.Ice.Version;
            _sea = field.SeaIce.Version;
        }
        if (field.SnowLine.Version != _line)
        {
            float[] line = field.SnowLine.Values;
            GreyMap terrain = field.Terrain;
            for (int row = 0; row < h; row++)
                for (int col = 0; col < w; col++)
                {
                    int i = row * w + col;
                    _auxData[2 * i] = (float)terrain.Sample((col + 0.5) / w, (row + 0.5) / h);
                    _auxData[2 * i + 1] = Mathf.Clamp(line[i], -3000f, 30000f);   // the sentinels (snow everywhere, nowhere) as heights beyond the terrain's
                }
            Aux.SetPixelData(_auxData, 0);
            Aux.Apply(false);
            _line = field.SnowLine.Version;
        }
    }

    public void Release()
    {
        if (Maps != null) UnityEngine.Object.Destroy(Maps);
        if (Aux != null) UnityEngine.Object.Destroy(Aux);
        Maps = Aux = null;
    }
}

/// <summary>
/// Snow cover, glaciers and sea ice for every body that has a climate: the maps other features (the local ocean's shader, the
/// scaled planets, the terrain) read. A body's maps appear a short while after it is first asked for, and are null until then
/// (and for worlds without an atmosphere or ground to snow on).
/// </summary>
internal static class SnowCover
{
    private static readonly Dictionary<string, BodyClimateState> States = new Dictionary<string, BodyClimateState>(StringComparer.OrdinalIgnoreCase);
    private static readonly Stopwatch Budget = Stopwatch.StartNew();
    private static readonly List<CelestialBody> Requested = new List<CelestialBody>();
    private static int _cursor, _seenRevision;
    private static float _revisionTime;

    /// <summary>The share of the land covered by snow (0..1) by latitude and longitude (row 0 the south, column 0 at longitude -180), or null.</summary>
    public static GreyMap SnowOf(CelestialBody b) => Ready(b)?.Field?.Snow;

    /// <summary>The share of sea covered by ice (0..1, only over the ocean) by latitude and longitude, or null.</summary>
    public static GreyMap SeaIceOf(CelestialBody b) => Ready(b)?.Field?.SeaIce;

    /// <summary>The share of the land under glaciers and ice caps (0..1), or null.</summary>
    public static GreyMap IceOf(CelestialBody b) => Ready(b)?.Field?.Ice;

    /// <summary>The snow line (m above the sea level; -1e5 snow everywhere, 1e9 nowhere) now, or null.</summary>
    public static GreyMap SnowLineOf(CelestialBody b) => Ready(b)?.Field?.SnowLine;

    /// <summary>The body's climate (temperatures, seasons) once worked out, or null.</summary>
    public static BodyClimateState StateOf(CelestialBody b) => Ready(b);

    /// <summary>Asks for a body's maps to be worked out (and kept up to date).</summary>
    public static void Request(CelestialBody b)
    {
        if (b == null) return;
        if (!States.ContainsKey(b.bodyName) && !Requested.Contains(b)) Requested.Add(b);
        else if (States.TryGetValue(b.bodyName, out BodyClimateState s) && s.Body != b) { States.Remove(b.bodyName); Requested.Add(b); }
    }

    private static BodyClimateState Ready(CelestialBody b)
    {
        if (b == null) return null;
        Request(b);
        return States.TryGetValue(b.bodyName, out BodyClimateState s) && s.Body == b && s.Stage == 3 ? s : null;
    }

    /// <summary>The GPU textures of a body's maps (made and kept current on the main thread), or null.</summary>
    public static SnowTextures TexturesOf(CelestialBody b)
    {
        BodyClimateState s = Ready(b);
        if (s == null || s.Field == null) return null;
        if (s.Textures == null) s.Textures = new SnowTextures();
        s.Textures.Sync(s.Field);
        return s.Textures;
    }

    /// <summary>A body's maps changed (a number that goes up with each refresh).</summary>
    public static int VersionOf(CelestialBody b) => Ready(b)?.Version ?? -1;

    public static void Forget()
    {
        foreach (BodyClimateState s in States.Values) s.Textures?.Release();
        States.Clear();
        Requested.Clear();
    }

    /// <summary>Starts requested bodies and moves every body's work on within a couple of milliseconds a frame.</summary>
    public static void Tick()
    {
        for (int i = Requested.Count - 1; i >= 0; i--)
        {
            CelestialBody b = Requested[i];
            Requested.RemoveAt(i);
            if (b != null) States[b.bodyName] = new BodyClimateState(b, ClimateConfig.Revision);
        }
        // A changed setting (a pretend tilt) builds the climates again, once it has stopped changing (a slider being dragged).
        if (_seenRevision != ClimateConfig.Revision)
        {
            _seenRevision = ClimateConfig.Revision;
            _revisionTime = Time.realtimeSinceStartup;
        }
        List<string> stale = null;
        if (Time.realtimeSinceStartup - _revisionTime > 0.6f)
            foreach (KeyValuePair<string, BodyClimateState> pair in States)
                if (pair.Value.Revision != ClimateConfig.Revision && pair.Value.Stage != 2) (stale ??= new List<string>()).Add(pair.Key);
        if (stale != null)
            foreach (string name in stale)
            {
                BodyClimateState old = States[name];
                old.Textures?.Release();
                States[name] = new BodyClimateState(old.Body, ClimateConfig.Revision, old);
            }
        if (States.Count == 0) return;
        Budget.Restart();
        var list = new List<BodyClimateState>(States.Values);
        // Round robin: one body works at a time through the heights; the others only refresh.
        _cursor = (_cursor + 1) % list.Count;
        bool reading = false;
        for (int k = 0; k < list.Count; k++)
        {
            BodyClimateState s = list[(_cursor + k) % list.Count];
            if (s.Body == null) { States.Remove(s.Name); continue; }
            if (s.ReadingHeights)
            {
                if (reading) continue;
                reading = true;
                s.Tick(Budget, 2.5);
            }
            else s.Tick(Budget, 0.6);
        }
    }
}
