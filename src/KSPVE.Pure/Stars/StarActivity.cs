using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Stars;

/// <summary>
/// Rotation of a Sun-like star's surface by latitude, in rad/s, scaled to the star's equatorial period.
/// </summary>
public static class StellarRotation
{
    private const double DegPerDayToRadPerSecond = Math.PI / 180.0 / 86400.0;

    /// <summary>
    /// The photosphere's and the long-lived magnetic features' (filaments, plage) sidereal rate, Snodgrass &amp; Ulrich (1990),
    /// omega = 14.713 - 2.396 sin^2(b) - 1.787 sin^4(b) deg/day, for a star whose equator turns once in
    /// <paramref name="equatorialPeriod"/> s (default: the Sun's, 24.47 days).
    /// </summary>
    public static double Snodgrass(double latitude, double equatorialPeriod = 360.0 / 14.713 * 86400.0)
    {
        double s2 = Math.Sin(latitude) * Math.Sin(latitude);
        double deg = 14.713 - 2.396 * s2 - 1.787 * s2 * s2;
        return deg * DegPerDayToRadPerSecond * (360.0 / 14.713 * 86400.0) / equatorialPeriod;
    }

    /// <summary>
    /// Sunspot groups' sidereal rate, omega = 14.522 - 2.84 sin^2(b) deg/day, the form Howard and others fit to the Mount
    /// Wilson and Greenwich sunspot records (coefficients rounded from the literature's fits): sunspots turn slightly faster
    /// at the equator than the photosphere and are less differential.
    /// </summary>
    public static double Howard(double latitude, double equatorialPeriod = 360.0 / 14.713 * 86400.0)
    {
        double s2 = Math.Sin(latitude) * Math.Sin(latitude);
        double deg = 14.522 - 2.84 * s2;
        return deg * DegPerDayToRadPerSecond * (360.0 / 14.713 * 86400.0) / equatorialPeriod;
    }
}

/// <summary>An active region: a bipolar magnetic area that emerges, carries sunspots, flares and erupts, and decays.</summary>
public sealed class ActiveRegion
{
    public long Id;
    /// <summary>When it emerges (s), how long it lasts (s).</summary>
    public double Emerge, Life;
    /// <summary>Latitude (rad, signed) and the longitude it emerged at (rad, in the model's inertial frame).</summary>
    public double Latitude, Longitude0;
    /// <summary>Largest area, in millionths of a hemisphere (MSH): about 100 for a middling group, 1000+ for a great one.</summary>
    public double AreaMsh;
    /// <summary>Joy's-law tilt (rad): the leading spot lies this much nearer the equator than the following one.</summary>
    public double Tilt;
    /// <summary>+1 or -1: the leading spot's polarity.</summary>
    public double Polarity;
    public int Cycle;
    public double Seed;

    public double AgeFraction(double ut) => (ut - Emerge) / Life;

    /// <summary>Growth over the first fifth of its life, then a slower decay to nothing.</summary>
    public static double Envelope(double age)
    {
        if (age <= 0.0 || age >= 1.0) return 0.0;
        if (age < 0.18)
        {
            double x = age / 0.18;
            return x * x * (3.0 - 2.0 * x);
        }
        return Math.Pow(1.0 - (age - 0.18) / 0.82, 1.5);
    }
}

/// <summary>A flare: a burst of soft X-rays (GOES class from the peak flux, W/m^2 in 0.1-0.8 nm: C is 1e-6, M 1e-5, X 1e-4).</summary>
public struct StarFlare
{
    public long RegionId;
    public double Time, Flux;
    public double Latitude, Longitude;

    public string Class
    {
        get
        {
            if (Flux < 1e-7) return "A";
            char letter = Flux < 1e-6 ? 'B' : Flux < 1e-5 ? 'C' : Flux < 1e-4 ? 'M' : 'X';
            double scale = letter == 'B' ? 1e-7 : letter == 'C' ? 1e-6 : letter == 'M' ? 1e-5 : 1e-4;
            return letter + (Flux / scale).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>A long-lived sheet of cool gas along a magnetic polarity inversion line: dark on the disc, a prominence at the limb.</summary>
public sealed class StarFilament
{
    public long Id;
    public double Start, Life;
    public double Latitude, Longitude0;
    /// <summary>Half its length (rad of arc), its scale height (stellar radii), and its tilt to the parallel (rad).</summary>
    public double HalfLength, Height, Tilt;
    /// <summary>Whether it ends by erupting (a CME), rather than fading.</summary>
    public bool Erupts;
    public double Seed;
    public double End => Start + Life;
}

/// <summary>
/// A star's magnetic activity, worked out from the game time and a seed alone (no state, so time warp, reloads and every
/// client agree, like Clouds' Weather): the activity cycle (Hathaway's asymmetric shape, an amplitude that varies from cycle
/// to cycle), the active regions that emerge at Sporer's-law latitudes (about 30 degrees, drifting to the equator over the
/// cycle) with Joy's-law tilts and Hale's-law polarities, their sunspots, their flares (a power law in flux), the filaments
/// of the quiet Sun, and the CMEs that flares and erupting filaments launch. Everything is generated from
/// <see cref="StarRandom"/> streams keyed by time slot, and memoised.
/// </summary>
public sealed class StarActivity
{
    private const double SlotRegions = 86400.0, SlotFilaments = 5.0 * 86400.0;
    private const int RegionCandidates = 6, FilamentCandidates = 3;
    private const double MaxRegionLife = 100.0 * 86400.0, MaxFilamentLife = 90.0 * 86400.0;
    private const double Deg = Math.PI / 180.0;
    /// <summary>The sunspot number's cycle shape: it peaks 36 per cent of the way through (4 of 11 years).</summary>
    private const double ShapeExponent = 0.678;
    /// <summary>Regions emerging per day at the cycle's peak, for the Sun: about 1.2 (12 or so groups on the disc at maximum).</summary>
    private const double RegionRate = 1.2;
    private const double FlareMinFlux = 1.0e-6, FlareMaxFlux = 3.0e-3, FlareIndex = 0.8;

    private readonly StarProfile _profile;
    private readonly object _gate = new object();
    private readonly Dictionary<long, List<ActiveRegion>> _regionSlots = new Dictionary<long, List<ActiveRegion>>();
    private readonly Dictionary<long, List<StarFilament>> _filamentSlots = new Dictionary<long, List<StarFilament>>();
    private readonly Dictionary<long, RegionEvents> _events = new Dictionary<long, RegionEvents>();
    private readonly List<Cme> _injected = new List<Cme>();
    private List<Cme> _cmeCache;
    private long _cmeKey = long.MinValue;
    private double _cmeAge;
    private int _cmeInjected = -1;

    private sealed class RegionEvents
    {
        public readonly List<StarFlare> Flares = new List<StarFlare>();
        public readonly List<Cme> Cmes = new List<Cme>();
    }

    public StarActivity(StarProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public StarProfile Profile => _profile;

    /// <summary>The profile's rotation rate relative to the Sun's.</summary>
    public double EquatorialPeriod => _profile.EquatorialRotationPeriod;

    // ---------------------------------------------------------------- the cycle

    /// <summary>Cycles elapsed (fractional) at a time.</summary>
    public double CyclePosition(double ut) => ut / _profile.CyclePeriod + _profile.CyclePhase;

    public long CycleIndex(double ut) => (long)Math.Floor(CyclePosition(ut));

    /// <summary>Where in the cycle (0 at the start, at minimum, to 1) a time falls.</summary>
    public double Phase(double ut)
    {
        double p = CyclePosition(ut);
        return p - Math.Floor(p);
    }

    /// <summary>
    /// The cycle's shape, 0 at the minima and 1 at the maximum, rising in 36 per cent of the cycle and falling over the
    /// rest (Hathaway 2015's cycles rise in about 4 years and fall over 7): sin^2(pi phase^k) with the peak at 0.36.
    /// </summary>
    public static double Shape(double phase)
    {
        if (phase <= 0.0 || phase >= 1.0) return 0.0;
        double s = Math.Sin(Math.PI * Math.Pow(phase, ShapeExponent));
        return s * s;
    }

    /// <summary>This cycle's strength relative to a typical one (0.6 .. 1.4, from the seed).</summary>
    public double CycleAmplitude(long cycle) => 0.6 + 0.8 * StarRandom.Unit(_profile.Seed, cycle, 1);

    /// <summary>The share of this cycle's regions that emerge in the north (0.38 .. 0.62: hemispheres are not equal).</summary>
    public double NorthShare(long cycle) => 0.5 + 0.12 * (2.0 * StarRandom.Unit(_profile.Seed, cycle, 2) - 1.0);

    /// <summary>
    /// The activity level at a time: the profile's activity times the cycle's amplitude times its shape, with a floor of
    /// 2 per cent of a typical maximum (the Sun is never entirely quiet). About 1 at an average maximum.
    /// </summary>
    public double Level(double ut)
    {
        double s = 0.02 + 0.98 * Shape(Phase(ut));
        return _profile.Activity * CycleAmplitude(CycleIndex(ut)) * s;
    }

    /// <summary>
    /// The mean latitude (rad) of regions emerging at a cycle phase, Sporer's law: about 30 degrees at the start of a
    /// cycle, 15 at maximum, 8 at the end (Maunder's butterfly diagram).
    /// </summary>
    public static double MeanLatitude(double phase)
    {
        double p = Math.Min(Math.Max(phase, 0.0), 1.0);
        return (8.0 + 22.0 * Math.Pow(1.0 - p, 1.3)) * Deg;
    }

    // ---------------------------------------------------------------- active regions

    private List<ActiveRegion> RegionSlot(long slot)
    {
        lock (_gate)
        {
            if (_regionSlots.TryGetValue(slot, out List<ActiveRegion> cached)) return cached;
            if (_regionSlots.Count > 20000) _regionSlots.Clear();
            var list = new List<ActiveRegion>();
            double t0 = slot * SlotRegions;
            double rate = RegionRate * Level(t0 + 0.5 * SlotRegions);
            double p = Math.Min(1.0, rate * (SlotRegions / 86400.0) / RegionCandidates);
            for (int j = 0; j < RegionCandidates; j++)
            {
                var rng = new StarRandom(_profile.Seed, slot, j, 11);
                if (rng.Next() > p) continue;
                double emerge = t0 + rng.Next() * SlotRegions;
                double phase = Phase(emerge);
                long cycle = CycleIndex(emerge);
                double cyclePhase = phase;
                // Where cycles overlap, a few regions of the old cycle (low latitude) and the new (high) show together.
                double u = rng.Next();
                if (phase > 0.86 && u < (phase - 0.86) / 0.14 * 0.5) { cycle++; cyclePhase = 0.02 * rng.Next(); }
                else if (phase < 0.10 && u < (0.10 - phase) / 0.10 * 0.5) { cycle--; cyclePhase = 0.97 + 0.03 * rng.Next(); }
                bool north = rng.Next() < NorthShare(cycle);
                double mean = MeanLatitude(cyclePhase);
                double lat = Math.Abs(mean + rng.Gauss() * (0.18 * mean + 2.0 * Deg));
                lat = Math.Min(Math.Max(lat, 2.0 * Deg), 40.0 * Deg);
                double area = Math.Min(Math.Max(rng.LogNormal(80.0, 1.0), 8.0), 3000.0);
                double lifeDays = Math.Min(Math.Max(area / 10.0, 1.5), 100.0);   // Gnevyshev-Waldmeier: area 10 MSH per day of life
                double tilt = (0.38 * lat + rng.Gauss() * 3.0 * Deg);
                tilt = Math.Min(Math.Max(tilt, -5.0 * Deg), 40.0 * Deg);
                list.Add(new ActiveRegion
                {
                    Id = slot * 8 + j,
                    Emerge = emerge,
                    Life = lifeDays * 86400.0,
                    Latitude = north ? lat : -lat,
                    Longitude0 = rng.Next() * 2.0 * Math.PI,
                    AreaMsh = area,
                    Tilt = tilt,
                    Polarity = (north ? 1.0 : -1.0) * ((cycle & 1) == 0 ? 1.0 : -1.0),
                    Cycle = (int)cycle,
                    Seed = rng.Next()
                });
            }
            _regionSlots[slot] = list;
            return list;
        }
    }

    /// <summary>The longitude (rad, inertial frame) of a region at a time: its sunspot-group rate (Howard) carries it round.</summary>
    public double RegionLongitude(ActiveRegion r, double ut) =>
        r.Longitude0 + StellarRotation.Howard(r.Latitude, EquatorialPeriod) * (ut - r.Emerge);

    /// <summary>Regions that exist at a time (emerged, not yet decayed away).</summary>
    public List<ActiveRegion> RegionsAt(double ut)
    {
        var result = new List<ActiveRegion>();
        long first = (long)Math.Floor((ut - MaxRegionLife) / SlotRegions), last = (long)Math.Floor(ut / SlotRegions);
        for (long s = first; s <= last; s++)
            foreach (ActiveRegion r in RegionSlot(s))
                if (ut >= r.Emerge && ut <= r.Emerge + r.Life) result.Add(r);
        return result;
    }

    /// <summary>
    /// A region's flares and CMEs, from a stream keyed to the region: flares in number proportional to its area, at times
    /// in the first three quarters of its life, with a power-law spread of fluxes (frequency-flux slope of about 1.8,
    /// Aschwanden); the stronger the flare, the likelier a CME (about 40 per cent of M flares, most X flares).
    /// </summary>
    private RegionEvents EventsOf(ActiveRegion r)
    {
        lock (_gate)
        {
            if (_events.TryGetValue(r.Id, out RegionEvents cached)) return cached;
            if (_events.Count > 40000) _events.Clear();
            var set = new RegionEvents();
            var rng = new StarRandom(_profile.Seed, r.Id, 0, 23);
            double mean = 12.0 * Math.Pow(r.AreaMsh / 100.0, 0.9) * Math.Sqrt(Math.Max(_profile.Activity, 0.0));
            int count = rng.Poisson(mean);
            for (int i = 0; i < count; i++)
            {
                double time = r.Emerge + r.Life * (0.05 + 0.7 * rng.Next());
                double flux = Math.Min(FlareMinFlux * Math.Pow(Math.Max(1.0 - rng.Next(), 1e-9), -1.0 / FlareIndex), FlareMaxFlux);
                double lon = RegionLongitude(r, time);
                set.Flares.Add(new StarFlare { RegionId = r.Id, Time = time, Flux = flux, Latitude = r.Latitude, Longitude = lon });
                double probability = Math.Min(Math.Max(0.04 + 0.42 * Math.Log10(flux / FlareMinFlux), 0.0), 0.95) * _profile.CmeRate;
                if (rng.Next() < probability) set.Cmes.Add(MakeCme(r.Id * 64 + i, time, r.Latitude, lon, flux, "Active region", r.Cycle));
            }
            set.Flares.Sort((a, b) => a.Time.CompareTo(b.Time));
            _events[r.Id] = set;
            return set;
        }
    }

    /// <summary>
    /// A CME from its source. Speed: lognormal, median 380 km/s times (flux / C1)^0.22 (so about 1000 km/s for an X1, 400
    /// for a C flare or a filament), sigma 0.3, 250-3200 km/s (LASCO: mean 490 km/s, up to 3000+). Half-width: median 25
    /// degrees (full 50) scaling with speed^0.4, 15-60 degrees. The cone turns towards the equator, the way the heliospheric
    /// current sheet deflects them. Flux rope handedness follows the hemispheric helicity rule, flipping with the cycle.
    /// </summary>
    private Cme MakeCme(long id, double time, double lat, double lon, double flareFlux, string source, int cycle)
    {
        var rng = new StarRandom(_profile.Seed, id, 0, 31);
        double fluxScale = Math.Max(flareFlux, FlareMinFlux) / FlareMinFlux;
        double speed = Math.Min(Math.Max(rng.LogNormal(3.8e5 * Math.Pow(fluxScale, 0.22), 0.3), 2.5e5), 3.2e6);
        double width = Math.Min(Math.Max(rng.LogNormal(25.0 * Math.Pow(speed / 5.0e5, 0.4), 0.4), 15.0), 60.0) * Deg;
        double deflected = lat * 0.6;
        double drag = Math.Min(Math.Max(rng.LogNormal(_profile.Drag, 0.5), _profile.Drag * 0.2), _profile.Drag * 5.0);
        double hemisphere = lat >= 0.0 ? 1.0 : -1.0;
        return new Cme
        {
            Id = id,
            LaunchTime = time,
            Latitude = deflected,
            Longitude = lon,
            LaunchSpeed = speed,
            HalfWidth = width,
            Drag = drag,
            WindSpeed = _profile.WindSpeed,
            StarRadius = Cme.SolarRadius,
            FlareFlux = flareFlux,
            Helicity = -hemisphere * ((cycle & 1) == 0 ? 1.0 : -1.0),
            AxisAngle = (rng.Next() * 2.0 - 1.0) * 90.0 * Deg,
            Impact = (rng.Next() * 2.0 - 1.0) * 0.8,
            CloudField = (6.0 + 9.0 * Math.Min(speed / 1.0e6, 2.0)) * 1e-9,
            Source = source
        }.Prepare();
    }

    /// <summary>The flares from every region in a window of time, oldest first.</summary>
    public List<StarFlare> FlaresBetween(double from, double to)
    {
        var result = new List<StarFlare>();
        long first = (long)Math.Floor((from - MaxRegionLife) / SlotRegions), last = (long)Math.Floor(to / SlotRegions);
        for (long s = first; s <= last; s++)
            foreach (ActiveRegion r in RegionSlot(s))
            {
                if (r.Emerge > to || r.Emerge + r.Life < from) continue;
                foreach (StarFlare f in EventsOf(r).Flares)
                    if (f.Time >= from && f.Time <= to) result.Add(f);
            }
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    // ---------------------------------------------------------------- filaments

    private List<StarFilament> FilamentSlot(long slot)
    {
        lock (_gate)
        {
            if (_filamentSlots.TryGetValue(slot, out List<StarFilament> cached)) return cached;
            if (_filamentSlots.Count > 8000) _filamentSlots.Clear();
            var list = new List<StarFilament>();
            double t0 = slot * SlotFilaments;
            double phase = Phase(t0);
            // About 0.35 a day, a little more in an active cycle's maximum; each lives a month or more.
            double rate = 0.35 * (0.6 + 0.4 * Math.Min(Level(t0) , 1.5));
            double p = Math.Min(1.0, rate * (SlotFilaments / 86400.0) / FilamentCandidates);
            for (int j = 0; j < FilamentCandidates; j++)
            {
                var rng = new StarRandom(_profile.Seed, slot, j, 41);
                if (rng.Next() > p) continue;
                double kind = rng.Next();
                double lat;
                if (kind < 0.45) lat = (40.0 + 25.0 * phase + rng.Gauss() * 5.0) * Deg;              // polar crown: migrates poleward as the cycle goes on
                else if (kind < 0.80) lat = (20.0 + 25.0 * rng.Next()) * Deg;                         // quiescent mid latitudes
                else lat = rng.Gauss() * 12.0 * Deg;                                                  // among the active regions
                lat = Math.Min(Math.Max(lat, -80.0 * Deg), 80.0 * Deg);
                if (kind < 0.80 && rng.Next() < 0.5) lat = -lat;
                double life = rng.LogNormal(35.0, 0.5) * 86400.0;
                life = Math.Min(Math.Max(life, 6.0 * 86400.0), MaxFilamentLife);
                list.Add(new StarFilament
                {
                    Id = slot * 8 + j,
                    Start = t0 + rng.Next() * SlotFilaments,
                    Life = life,
                    Latitude = lat,
                    Longitude0 = rng.Next() * 2.0 * Math.PI,
                    HalfLength = (0.06 + 0.5 * Math.Pow(rng.Next(), 1.5)) ,
                    Height = 0.025 + 0.05 * rng.Next(),
                    Tilt = rng.Gauss() * 12.0 * Deg,
                    Erupts = rng.Next() < 0.35,
                    Seed = rng.Next()
                });
            }
            _filamentSlots[slot] = list;
            return list;
        }
    }

    public double FilamentLongitude(StarFilament f, double ut) =>
        f.Longitude0 + StellarRotation.Snodgrass(f.Latitude, EquatorialPeriod) * (ut - f.Start);

    public List<StarFilament> FilamentsAt(double ut, double lead = 0.0)
    {
        var result = new List<StarFilament>();
        long first = (long)Math.Floor((ut - MaxFilamentLife) / SlotFilaments), last = (long)Math.Floor(ut / SlotFilaments);
        for (long s = first; s <= last; s++)
            foreach (StarFilament f in FilamentSlot(s))
                if (ut >= f.Start && ut <= f.End + lead) result.Add(f);
        return result;
    }

    private Cme FilamentCme(StarFilament f)
    {
        double lon = FilamentLongitude(f, f.End);
        return MakeCme(1_000_000_000L + f.Id, f.End, f.Latitude, lon, 0.0, "Filament eruption", (int)CycleIndex(f.End));
    }

    // ---------------------------------------------------------------- CMEs

    /// <summary>Adds a CME by hand (a test launch).</summary>
    public void Inject(Cme cme)
    {
        lock (_gate)
        {
            cme.Injected = true;
            _injected.Add(cme.Prepare());
            _cmeKey = long.MinValue;
        }
    }

    public void ClearInjected()
    {
        lock (_gate)
        {
            _injected.Clear();
            _cmeKey = long.MinValue;
        }
    }

    /// <summary>
    /// The CMEs launched in the <paramref name="maxAge"/> seconds up to <paramref name="ut"/>, oldest first: those that
    /// flares and erupting filaments launch (when the profile has CMEs on), and the injected ones.
    /// </summary>
    public List<Cme> CmesAt(double ut, double maxAge)
    {
        const double Quantum = 600.0;
        long key = (long)Math.Floor(ut / Quantum);
        lock (_gate)
        {
            if (key != _cmeKey || maxAge > _cmeAge || _cmeInjected != _injected.Count || _cmeCache == null)
            {
                double windowEnd = (key + 1) * Quantum;
                double age = Math.Max(maxAge, 3600.0);
                _cmeCache = Collect(windowEnd - age, windowEnd);
                _cmeKey = key;
                _cmeAge = age;
                _cmeInjected = _injected.Count;
            }
            var result = new List<Cme>();
            foreach (Cme c in _cmeCache)
                if (c.LaunchTime <= ut && c.LaunchTime >= ut - maxAge) result.Add(c);
            return result;
        }
    }

    /// <summary>The CMEs that launch between two times (past or future: the model knows its own future), oldest first.</summary>
    public List<Cme> CmesBetween(double from, double to)
    {
        lock (_gate) return Collect(from, to);
    }

    private List<Cme> Collect(double from, double windowEnd)
    {
        var all = new List<Cme>();
        if (_profile.Cmes && _profile.Activity > 0.0)
        {
            long firstR = (long)Math.Floor((from - MaxRegionLife) / SlotRegions), lastR = (long)Math.Floor(windowEnd / SlotRegions);
            for (long s = firstR; s <= lastR; s++)
                foreach (ActiveRegion r in RegionSlot(s))
                {
                    if (r.Emerge > windowEnd || r.Emerge + r.Life < from) continue;
                    foreach (Cme c in EventsOf(r).Cmes)
                        if (c.LaunchTime >= from && c.LaunchTime <= windowEnd) all.Add(c);
                }
            long firstF = (long)Math.Floor((from - MaxFilamentLife) / SlotFilaments), lastF = (long)Math.Floor(windowEnd / SlotFilaments);
            for (long s = firstF; s <= lastF; s++)
                foreach (StarFilament f in FilamentSlot(s))
                {
                    if (!f.Erupts || f.End < from || f.End > windowEnd) continue;
                    if (StarRandom.Unit(_profile.Seed, f.Id, 0, 43) < _profile.CmeRate) all.Add(FilamentCme(f));
                }
        }
        foreach (Cme c in _injected) if (c.LaunchTime >= from && c.LaunchTime <= windowEnd) all.Add(c);
        all.Sort((a, b) => a.LaunchTime.CompareTo(b.LaunchTime));
        return all;
    }

    // ---------------------------------------------------------------- what the renderer needs

    /// <summary>The model frame's tangent east (towards increasing longitude) and north at a latitude and longitude.</summary>
    public static void Tangents(double lat, double lon, out Double3 position, out Double3 east, out Double3 north)
    {
        double cl = Math.Cos(lat), sl = Math.Sin(lat), co = Math.Cos(lon), so = Math.Sin(lon);
        position = new Double3(cl * co, sl, cl * so);
        east = new Double3(-so, 0.0, co);
        north = new Double3(-sl * co, cl, -sl * so);
    }

    /// <summary>
    /// The scene at a time: spot groups, loop systems and prominences, CME shells, as the renderer draws them. At most
    /// <paramref name="maxStructures"/> structures and <paramref name="maxSpots"/> spot groups, those that matter most when
    /// seen from <paramref name="toViewer"/> (a unit vector from the star towards the camera in the model frame).
    /// </summary>
    public StarScene Snapshot(double ut, Double3 toViewer, int maxSpots = 24, int maxStructures = 20, int maxShells = 3)
    {
        var scene = new StarScene { Time = ut, Activity = Level(ut), CyclePhase = Phase(ut) };
        scene.StreamerSpread = Math.Min(1.0, Shape(Phase(ut)) * Math.Min(CycleAmplitude(CycleIndex(ut)), 1.2));
        scene.PolarityOfCycle = (CycleIndex(ut) & 1) == 0 ? 1.0 : -1.0;
        Double3 view = toViewer.Length > 0.0 ? toViewer.Normalized() : Double3.UnitZ;

        var structures = new List<KeyValuePair<double, StarStructure>>();
        var spots = new List<KeyValuePair<double, SpotGroup>>();
        List<StarFlare> recent = FlaresBetween(ut - 86400.0, ut);
        var groups = new List<(ActiveRegion region, Double3 centre, double half, double strength, double flare)>();

        if (_profile.Spots || _profile.Prominences)
        {
            foreach (ActiveRegion r in RegionsAt(ut))
            {
                double age = r.AgeFraction(ut);
                double strength = ActiveRegion.Envelope(age);
                if (strength < 0.03 || r.AreaMsh * strength < 6.0) continue;
                double lon = RegionLongitude(r, ut);
                Tangents(r.Latitude, lon, out Double3 pos, out Double3 east, out Double3 north);
                double area = r.AreaMsh * strength;
                double sep = 0.045 * Math.Sqrt(r.AreaMsh / 100.0) * (0.6 + 0.4 * Math.Min(1.0, age / 0.3));
                double half = 0.5 * sep;
                double sign = r.Latitude >= 0.0 ? 1.0 : -1.0;
                Double3 lead = (east * Math.Cos(r.Tilt) - north * (sign * Math.Sin(r.Tilt))).Normalized();
                // Flares in the last hours brighten the footpoints: an impulsive rise and a decay of about half an hour.
                double flare = 0.0, bigFlux = 0.0, bigTime = 0.0;
                foreach (StarFlare f in recent)
                {
                    if (f.RegionId != r.Id || f.Time > ut) continue;
                    double dt = ut - f.Time;
                    double e = Math.Exp(-dt / 2400.0) * Math.Min(1.0, Math.Log10(f.Flux / 1e-6 + 1.0) / 2.0);
                    flare = Math.Max(flare, e);
                    if (f.Flux > bigFlux && dt < 16.0 * 3600.0) { bigFlux = f.Flux; bigTime = f.Time; }
                }
                double penumbra = Math.Sqrt(1.1e-6 * area);
                if (_profile.Spots)
                {
                    var g = new SpotGroup
                    {
                        Dir = pos, Axis = lead, HalfSeparation = half, UmbraRadius = 0.42 * penumbra,
                        FaculaRadius = 2.6 * penumbra + 0.7 * half + 0.01, Strength = strength, Flare = flare, Seed = r.Seed,
                        Polarity = r.Polarity, AreaMsh = area, Age = age
                    };
                    spots.Add(new KeyValuePair<double, SpotGroup>(-area * (0.5 + 0.5 * Math.Max(Double3.Dot(pos, view) + 0.3, 0.0)), g));
                }
                if (_profile.Prominences)
                {
                    Double3 a = (pos + lead * half).Normalized(), b = (pos - lead * half).Normalized();
                    int loops = area > 200.0 ? 3 : area > 50.0 ? 2 : 1;
                    var rng = new StarRandom(_profile.Seed, r.Id, 0, 53);
                    for (int i = 0; i < loops; i++)
                    {
                        // The main loops join the leading and following polarities; a few more are offset across.
                        double offset = i == 0 ? 0.0 : (rng.Next() - 0.5) * 1.2 * half;
                        double scale = i == 0 ? 1.0 : 0.55 + 0.5 * rng.Next();
                        Double3 shift = north * offset;
                        // Loop systems lie at all angles to the parallels (a bipole's axis is only tilted a little, but the loops
                        // above it spread out and fan), so some are seen face on wherever they stand on the limb.
                        double fan = i == 0 ? (rng.Next() - 0.5) * 1.4 : (rng.Next() - 0.5) * 2.4;
                        Double3 across = Double3.Cross(pos, lead);
                        Double3 axisI = (lead * Math.Cos(fan) + across * Math.Sin(fan)).Normalized();
                        // Seen from the viewer, loops near the limb show as arcs when their plane faces the viewer: the active
                        // region's loops fan out over a wide range of angles to the bipole axis (Joy's tilt only sets the mean),
                        // so near the limb the ones that show as arcs are favoured.
                        double limb = 1.0 - Math.Abs(Double3.Dot(pos, view));
                        Double3 faceOn = Double3.Cross(pos, view);
                        if (faceOn.Length > 1e-3)
                        {
                            faceOn = faceOn.Normalized();
                            if (Double3.Dot(faceOn, axisI) < 0.0) faceOn = -faceOn;
                            double bias = 0.85 * Math.Min(Math.Max((limb - 0.15) / 0.5, 0.0), 1.0);
                            axisI = (axisI * (1.0 - bias) + faceOn * bias).Normalized();
                        }
                        Double3 pa = (pos + shift + axisI * half).Normalized(), pb = (pos + shift - axisI * half).Normalized();
                        double chord = (pa - pb).Length;
                        var s = new StarStructure
                        {
                            Kind = StructureKind.Arch, A = pa, B = pb,
                            Height = Math.Max(0.5 * chord * (0.9 + 0.5 * rng.Next()) * scale, 0.012),
                            Width = 0.010 + 0.006 * rng.Next(), Ribbon = 0.09 * chord * (0.6 + rng.Next()),
                            Nest = 1.0, Brightness = (0.5 + 0.5 * Math.Min(1.0, area / 400.0)) * (0.6 + 0.4 * strength) + 0.5 * flare,
                            Opacity = 0.6, Erupt = 0.0, Seed = r.Seed + i, Label = "Loops"
                        };
                        structures.Add(new KeyValuePair<double, StarStructure>(-Importance(s, view, 1.0), s));
                    }
                    // A post-flare arcade after an M-class flare or stronger: nested loops across the neutral line, rising
                    // slowly over hours (they last about half a day).
                    if (bigFlux >= 1e-5)
                    {
                        double since = ut - bigTime;
                        double rise = Math.Min(since / (4.0 * 3600.0), 1.0);
                        double fade = since < 8.0 * 3600.0 ? 1.0 : Math.Max(0.0, 1.0 - (since - 8.0 * 3600.0) / (8.0 * 3600.0));
                        if (fade > 0.0)
                        {
                            double chord = 1.6 * sep;
                            Double3 pa = (pos + north * (0.5 * chord)).Normalized(), pb = (pos - north * (0.5 * chord)).Normalized();
                            var s = new StarStructure
                            {
                                Kind = StructureKind.Arch, A = pa, B = pb,
                                Height = (0.4 + 0.6 * rise) * 0.6 * chord, Width = 0.004, Ribbon = 0.7 * half + 0.02,
                                Nest = 5.0, Brightness = (0.8 + 0.4 * Math.Min(1.0, Math.Log10(bigFlux / 1e-5))) * fade, Opacity = 0.5, Seed = r.Seed + 99.0, Label = "Post-flare arcade"
                            };
                            structures.Add(new KeyValuePair<double, StarStructure>(-Importance(s, view, 1.5), s));
                        }
                    }
                    groups.Add((r, pos, half, strength, flare));
                }
            }
            // Loops interconnecting nearby regions of one hemisphere: the big arches, up to a few tenths of a radius high.
            for (int i = 0; i < groups.Count; i++)
                for (int j = i + 1; j < groups.Count; j++)
                {
                    var gi = groups[i]; var gj = groups[j];
                    double dot = Double3.Dot(gi.centre, gj.centre);
                    double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
                    if (angle < 0.09 || angle > 0.95) continue;
                    if (StarRandom.Unit(_profile.Seed, gi.region.Id, gj.region.Id, 61) > 0.85) continue;
                    double chord = (gi.centre - gj.centre).Length;
                    var rng = new StarRandom(_profile.Seed, gi.region.Id, gj.region.Id, 62);
                    var s = new StarStructure
                    {
                        Kind = StructureKind.Arch, A = gi.centre, B = gj.centre,
                        Height = Math.Min(0.6 * chord * (0.8 + 0.5 * rng.Next()), 0.4), Width = 0.02 + 0.008 * rng.Next(), Ribbon = 0.06 * chord + 0.008,
                        Nest = 1.0, Brightness = 0.55 * Math.Min(gi.strength, gj.strength) + 0.15, Opacity = 0.4, Seed = rng.Next() * 100.0, Label = "Interconnecting loops"
                    };
                    structures.Add(new KeyValuePair<double, StarStructure>(-Importance(s, view, 1.2), s));
                }
        }

        if (_profile.Prominences)
        {
            // Filaments, lifted and brightened in their last hours when they are about to erupt.
            foreach (StarFilament f in FilamentsAt(ut, 6.0 * 3600.0))
            {
                double lon = FilamentLongitude(f, Math.Min(ut, f.End));
                Tangents(f.Latitude, lon, out Double3 pos, out Double3 east, out Double3 north);
                Double3 axis = (east * Math.Cos(f.Tilt) + north * Math.Sin(f.Tilt));
                axis = (axis - pos * Double3.Dot(axis, pos)).Normalized();
                double age = (ut - f.Start) / f.Life;
                double fade = Math.Min(1.0, Math.Min(age / 0.06, (1.0 - age) / 0.06));
                double toErupt = f.End - ut;
                bool launched = toErupt < 0.0;
                if (launched && !f.Erupts) continue;
                double lift = f.Erupts && toErupt < 4.0 * 3600.0 ? Math.Min(1.0, (1.0 - toErupt / (4.0 * 3600.0))) : 0.0;
                if (launched && f.Erupts) continue;   // the erupting prominence is drawn from its CME below
                var s = new StarStructure
                {
                    Kind = StructureKind.Filament, A = pos, B = axis,
                    Height = f.Height * (1.0 + 2.5 * lift * lift), Width = 0.006, Ribbon = f.HalfLength,
                    Brightness = Math.Max(fade, 0.0) * (0.55 + 0.4 * lift), Opacity = 0.9, Erupt = lift * 0.4, Seed = f.Seed * 100.0, Label = "Filament"
                };
                if (s.Brightness > 0.02) structures.Add(new KeyValuePair<double, StarStructure>(-Importance(s, view, 1.0), s));
            }
        }

        if (_profile.Cmes || _profile.Prominences)
        {
            // A CME launched within the last day: a prominence rising and opening up (an arch growing tall), and once it is
            // a few radii out, a shell.
            List<Cme> recentCmes = CmesAt(ut, 86400.0);
            // Newest first so that the brightest, youngest ones win the slots.
            for (int i = recentCmes.Count - 1; i >= 0; i--)
            {
                Cme c = recentCmes[i];
                double apex = c.Distance(ut) / c.StarRadius;
                Tangents(c.Latitude, c.Longitude, out Double3 pos, out Double3 east, out Double3 north);
                if (apex < 3.0 && _profile.Prominences)
                {
                    double axisAngle = c.AxisAngle * 0.3;
                    Double3 span = (east * Math.Cos(axisAngle) + north * Math.Sin(axisAngle)).Normalized();
                    double half = Math.Min(0.12 + 0.3 * c.HalfWidth, 0.45);
                    var s = new StarStructure
                    {
                        Kind = StructureKind.Arch, A = (pos + span * half).Normalized(), B = (pos - span * half).Normalized(),
                        Height = Math.Max((apex - 1.0) * 0.8, 0.02), Width = 0.012 * (1.0 + 0.5 * apex), Ribbon = 0.1 + 0.08 * apex,
                        Nest = 1.0, Brightness = 1.0, Opacity = 0.8, Erupt = 1.0 - Math.Min(1.0, (apex - 1.1) / 1.9), Seed = (double)(c.Id % 997), Label = "Eruption"
                    };
                    structures.Add(new KeyValuePair<double, StarStructure>(-Importance(s, view, 3.0), s));
                }
                if (apex > 1.5 && apex < 14.0 && _profile.Cmes && scene.Shells.Count < maxShells)
                    scene.Shells.Add(new CmeShell { Dir = pos, Apex = apex, HalfWidth = c.HalfWidth, Seed = (double)(c.Id % 997), Core = 1.0, Id = c.Id });
            }
        }

        spots.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < spots.Count && i < maxSpots; i++) scene.Spots.Add(spots[i].Value);
        structures.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < structures.Count && i < maxStructures; i++)
        {
            if (structures[i].Key >= 0.0) break;   // the rest are on the far side
            scene.Structures.Add(structures[i].Value);
        }
        foreach (SpotGroup g in scene.Spots) scene.FlareLevel = Math.Max(scene.FlareLevel, g.Flare);
        return scene;
    }

    /// <summary>How much a structure matters to the picture from a viewpoint: bigger, brighter and nearer the limb count for more.</summary>
    private static double Importance(StarStructure s, Double3 view, double weight)
    {
        Double3 centre = s.Kind == StructureKind.Arch ? (s.A + s.B).Normalized() : s.A;
        double facing = Double3.Dot(centre, view);
        // Anything on the far side but near the limb can still show over it; the far side proper is hidden.
        double visible = facing > -0.35 ? 1.0 : 0.0;
        double size = s.Kind == StructureKind.Arch ? s.Height + 0.5 * (s.A - s.B).Length : s.Height * 4.0 + s.Ribbon * 0.3;
        double limb = 1.0 + 1.5 * Math.Exp(-facing * facing * 6.0);
        return weight * visible * (size + 0.05) * (0.3 + s.Brightness) * limb;
    }
}
