using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;

namespace KSPVE.Pure.Stars;

/// <summary>A CME on its way to (or past) a place.</summary>
public struct CmeForecast
{
    public Cme Cme;
    /// <summary>When the shock arrives, and when the back of the magnetic cloud has gone by (game time, s).</summary>
    public double Arrival, Departure;
    /// <summary>The front's speed at the place (m/s) when it gets there.</summary>
    public double Speed;
    /// <summary>Angle between the CME's axis and the place (rad).</summary>
    public double Offset;
    /// <summary>The cloud's peak field there (T).</summary>
    public double PeakField;
    public bool HasArrived(double ut) => ut >= Arrival;
    public bool IsOver(double ut) => ut >= Departure;
    /// <summary>The same flavour of rating the Dst index has: how bad the storm will be. Minor, Moderate, Strong, Severe, Extreme.</summary>
    public string StormClass
    {
        get
        {
            double coupling = PeakField * Speed * 1e-3;   // roughly V Bs in mV/m
            return coupling < 1.0 ? "Minor" : coupling < 3.0 ? "Moderate" : coupling < 6.0 ? "Strong" : coupling < 12.0 ? "Severe" : "Extreme";
        }
    }
}

/// <summary>
/// The solar wind of a star at a place and time (<see cref="ISolarWindSource"/>): the quiet wind (Parker; density as 1/r^2 at
/// constant speed, field as 1/r far out; at the reference distance, the Sun's at 1 AU: 400 km/s, 5 /cm^3, 5 nT), plus
/// whatever CME is passing. The model frame is the one the activity model uses: y is the star's north, the origin the star.
/// A CME has a shock (density up to about 4 times, the speed jump of a strong shock: 3/4 of the front's excess over the wind),
/// a sheath of turbulent compressed wind, and a magnetic cloud: a Lundquist (1950) force-free flux rope, B_axial = B0 J0(a r),
/// B_tangential = H B0 J1(a r) with a = 2.405, whose north-south field turns through the passage over a day at 1 AU, and
/// whose strength falls as distance^-1.6 (observed between Mercury and 1 AU, Leitner et al. 2007). After fast CMEs and
/// strong flares comes a solar energetic particle event, rising in hours and decaying over a day, strongest for observers
/// whose Parker spiral field line leads back to the eruption (about 55 degrees west of the central meridian at 1 AU).
/// </summary>
/// <remarks>
/// Distances are scaled so that the reference distance (the home planet's orbit) counts as 1 AU: the CME model is in real metres
/// and speeds, so a CME takes the time to reach the home planet that it takes to reach the Earth, whatever the size of the system.
/// </remarks>
public sealed class StarWind
{
    public const double Au = 1.495978707e11;
    private readonly StarActivity _activity;
    private readonly double _reference;

    /// <param name="reference">The distance at which the wind is the Sun's at 1 AU (m): the home planet's orbit.</param>
    public StarWind(StarActivity activity, double reference)
    {
        _activity = activity;
        _reference = Math.Max(reference, 1.0);
    }

    public StarActivity Activity => _activity;
    public double ReferenceDistance => _reference;

    /// <summary>A position in the game's system as the equivalent position in a system whose home planet orbits at 1 AU.</summary>
    public Double3 Equivalent(Double3 position) => position * (Au / _reference);

    /// <summary>The quiet wind only, with the cycle's modulation of the field (a stronger interplanetary field near maximum).</summary>
    public SolarWindState Quiet(double distance, double ut)
    {
        SolarWindState s = SolarWindState.Quiet(distance, _reference);   // distance in the game's system
        s.Speed = _activity.Profile.WindSpeed;
        double level = Math.Min(_activity.Level(ut), 1.5);
        s.FieldStrength *= 1.0 + 0.3 * (level - 0.5);
        return s;
    }

    /// <summary>How long to look back for CMEs that could have reached <paramref name="distance"/> by now (slowest ones included).</summary>
    public static double LookBack(double distance) => Math.Min(Math.Max(distance / 1.6e5, 2.0 * 86400.0), 600.0 * 86400.0);

    public SolarWindState At(Double3 gamePosition, double ut)
    {
        SolarWindState quiet = Quiet(Math.Max(gamePosition.Length, 1.0), ut);
        Double3 position = Equivalent(gamePosition);
        double distance = Math.Max(position.Length, 1.0);
        SolarWindState result = quiet;
        List<Cme> cmes = _activity.CmesAt(ut, LookBack(distance));
        double sep = 0.0;
        Double3 field = Double3.Zero;
        bool any = false;
        for (int i = 0; i < cmes.Count; i++)
        {
            Cme c = cmes[i];
            CmeSample sample = c.Sample(position, ut);
            if (sample.Zone != CmeZone.Outside)
            {
                any = true;
                Disturb(ref result, ref field, quiet, c, sample, position, distance);
            }
            sep += EnergeticParticlesAt(c, position, distance, ut);
            // An energetic storm particle event: particles accelerated at the shock itself pile up just ahead of and behind it.
            if (sample.Zone == CmeZone.Sheath && sample.Fraction < 0.3) sep += 0.5 * PeakParticles(c, distance) * (1.0 - sample.Fraction / 0.3);
        }
        if (any)
        {
            result.InCme = 1.0;
            if (field.Length > 0.0)
            {
                result.FieldStrength = Math.Max(field.Length, quiet.FieldStrength * 0.5);
                result.FieldNorth = field.Y;
            }
        }
        result.EnergeticParticles = 1.0 + sep;
        return result;
    }

    private void Disturb(ref SolarWindState result, ref Double3 field, SolarWindState quiet, Cme c, CmeSample sample, Double3 position, double distance)
    {
        double wind = c.WindSpeed;
        double excess = Math.Max(sample.LeadSpeed - wind, 0.0);
        // A strong shock compresses 4 times; a weak one hardly at all (the fast magnetosonic speed here is about 60 km/s).
        double ratio = 1.0 + 3.0 * (1.0 - Math.Exp(-excess / 1.2e5));
        double scale = Math.Pow(Au / distance, 1.6);
        if (sample.Zone == CmeZone.Sheath)
        {
            double x = sample.Fraction;
            double compression = ratio - (ratio - 1.0) * 0.5 * x;
            result.Density = Math.Max(result.Density, quiet.Density * compression);
            result.Speed = Math.Max(result.Speed, wind + (1.0 - 1.0 / ratio) * excess * (1.0 - 0.35 * x));
            double b = quiet.FieldStrength * (1.0 + 0.7 * (compression - 1.0));
            // Turbulent: the field's north-south part wanders, sum of a few sinusoids fixed by the CME.
            double wander = 0.55 * (Math.Sin(x * 31.0 + c.Id) + 0.7 * Math.Sin(x * 53.0 + 2.0 * c.Id) + 0.5 * Math.Sin(x * 97.0 + 3.0 * c.Id)) / 2.2;
            Double3 vec = new Double3(0.0, b * wander, 0.0);
            field = field + vec + Double3.UnitZ * (b * Math.Sqrt(Math.Max(1.0 - wander * wander, 0.0)));
            return;
        }
        // The magnetic cloud: low density, smooth, a flux rope; it expands, so the front moves faster than the back.
        result.Density = Math.Min(result.Density, quiet.Density * 0.35);
        result.Speed = Math.Max(result.Speed, wind + excess * (0.8 - 0.4 * sample.Fraction));
        field = field + FluxRope(c, position, sample.Fraction, c.CloudField * scale);
    }

    /// <summary>J0 and J1 for arguments up to 3 (Abramowitz &amp; Stegun 9.4.1, 9.4.4).</summary>
    public static void Bessel01(double x, out double j0, out double j1)
    {
        double t = x / 3.0, t2 = t * t;
        j0 = 1.0 + t2 * (-2.2499997 + t2 * (1.2656208 + t2 * (-0.3163866 + t2 * (0.0444479 + t2 * (-0.0039444 + t2 * 0.0002100)))));
        j1 = x * (0.5 + t2 * (-0.56249985 + t2 * (0.21093573 + t2 * (-0.03954289 + t2 * (0.00443319 + t2 * (-0.00031761 + t2 * 0.00001109))))));
    }

    /// <summary>
    /// The field of a Lundquist flux rope along a straight path through it, at a fraction (0 front, 1 back) of the passage,
    /// in tesla. The rope's axis lies across the radial direction at <see cref="Cme.AxisAngle"/> (0 east, pi/2 north); the
    /// path passes at <see cref="Cme.Impact"/> of its radius from the axis.
    /// </summary>
    public static Double3 FluxRope(Cme c, Double3 position, double fraction, double peakField)
    {
        Double3 radial = position.Normalized();
        Double3 east = Double3.Cross(Double3.UnitY, radial);
        if (east.Length < 1e-6) east = Double3.Cross(Double3.UnitX, radial);
        east = east.Normalized();
        Double3 northPerp = Double3.Cross(radial, east).Normalized();
        Double3 axis = (east * Math.Cos(c.AxisAngle) + northPerp * Math.Sin(c.AxisAngle)).Normalized();
        Double3 travel = (-radial) - axis * Double3.Dot(-radial, axis);
        if (travel.Length < 1e-6) travel = east;
        travel = travel.Normalized();
        Double3 across = Double3.Cross(axis, travel);
        double y = c.Impact;
        double half = Math.Sqrt(Math.Max(1.0 - y * y, 0.0));
        double x = (2.0 * fraction - 1.0) * half;
        double rho = Math.Min(Math.Sqrt(x * x + y * y), 1.0);
        Bessel01(2.405 * rho, out double j0, out double j1);
        Double3 tangent = rho > 1e-9 ? (across * x - travel * y) / rho : Double3.Zero;
        return axis * (peakField * j0) + tangent * (peakField * c.Helicity * j1);
    }

    /// <summary>
    /// The energetic particles one eruption adds at a place (relative to quiet): for a CME faster than 600 km/s or a flare of
    /// M5 or more, a peak of 10 to 10^5 (more for a faster event), weaker with distance as r^-2.3 and by how far the place's
    /// magnetic connection is from the eruption (Gaussian, 35 degrees, with a floor for cross-field transport), rising over
    /// about 1.5 h and decaying over about 14 h.
    /// </summary>
    public double EnergeticParticles(Cme c, Double3 gamePosition, double ut)
    {
        Double3 position = Equivalent(gamePosition);
        double distance = Math.Max(position.Length, 1.0);
        return EnergeticParticlesAt(c, position, distance, ut);
    }

    private double EnergeticParticlesAt(Cme c, Double3 position, double distance, double ut)
    {
        double peak = PeakParticles(c, distance);
        if (peak <= 0.0) return 0.0;
        // Particles of about 0.25 c arrive along a spiral longer than the straight line.
        double delay = 1.2 * distance / 7.5e7;
        double t = ut - c.LaunchTime - delay;
        if (t <= 0.0) return 0.0;
        double riseTime = 1.5 * 3600.0 * Math.Sqrt(Math.Max(distance / Au, 0.1)), decay = 14.0 * 3600.0;
        double tPeak = Math.Log(decay / riseTime) * riseTime * decay / (decay - riseTime);
        double norm = Math.Exp(-tPeak / decay) - Math.Exp(-tPeak / riseTime);
        double shape = (Math.Exp(-t / decay) - Math.Exp(-t / riseTime)) / norm;
        // Connection: the spiral's footpoint lies ahead of the observer in the star's rotation by omega r / w.
        double omega = StellarRotation.Snodgrass(0.0, _activity.EquatorialPeriod);
        double footpoint = Math.Atan2(position.Z, position.X) + omega * distance / _activity.Profile.WindSpeed;
        double d = footpoint - c.Longitude;
        d -= 2.0 * Math.PI * Math.Floor((d + Math.PI) / (2.0 * Math.PI));
        double sigma = 35.0 * Math.PI / 180.0;
        double connection = 0.1 + 0.9 * Math.Exp(-0.5 * d * d / (sigma * sigma));
        return peak * connection * Math.Max(shape, 0.0);
    }

    /// <summary>The peak enhancement (relative to quiet) of one eruption's energetic particles at a distance, 0 for a weak eruption.</summary>
    public double PeakParticles(Cme c, double distance)
    {
        double v = c.LaunchSpeed;
        double flare = c.FlareFlux;
        if (v < 6.0e5 && flare < 5.0e-5) return 0.0;
        double strength = Math.Min(Math.Max((v - 6.0e5) / 2.0e6, 0.0) + 0.3 * Math.Min(Math.Max(Math.Log10(Math.Max(flare, 1e-9) / 5e-5), 0.0), 2.0), 1.0);
        return Math.Pow(10.0, 1.0 + 4.0 * strength) * Math.Pow(Au / distance, 2.3);
    }

    /// <summary>The CMEs that have not yet passed a place, with when they arrive and go, soonest first.</summary>
    public List<CmeForecast> Incoming(Double3 gamePosition, double ut, double horizon)
    {
        var result = new List<CmeForecast>();
        Double3 position = Equivalent(gamePosition);
        double distance = Math.Max(position.Length, 1.0);
        foreach (Cme c in _activity.CmesBetween(ut - LookBack(distance), ut + horizon))
        {
            CmeForecast f = ForecastAt(c, position);
            if (double.IsNaN(f.Arrival) || f.Departure < ut || f.Arrival > ut + horizon) continue;
            result.Add(f);
        }
        result.Sort((a, b) => a.Arrival.CompareTo(b.Arrival));
        return result;
    }

    /// <summary>When one CME reaches a place, and at what speed (Arrival is NaN when it misses).</summary>
    public CmeForecast Forecast(Cme c, Double3 gamePosition) => ForecastAt(c, Equivalent(gamePosition));

    private CmeForecast ForecastAt(Cme c, Double3 position)
    {
        double arrival = c.ArrivalTime(position);
        var f = new CmeForecast { Cme = c, Arrival = arrival, Departure = double.NaN, Offset = c.OffsetOf(position) };
        if (double.IsNaN(arrival)) return f;
        f.Departure = c.DepartureTime(position);
        double k = c.EdgeFactor(f.Offset);
        f.Speed = c.Speed(arrival) * k;
        f.PeakField = c.CloudField * Math.Pow(Au / Math.Max(position.Length, 1.0), 1.6);
        return f;
    }
}

/// <summary>The solar wind source for the game: one <see cref="StarWind"/> per star (by body name), a quiet wind for any other.</summary>
public sealed class StarWindSource : ISolarWindSource
{
    private readonly Dictionary<string, StarWind> _winds = new Dictionary<string, StarWind>(StringComparer.Ordinal);

    public void Set(string star, StarWind wind) => _winds[star] = wind;
    public void Clear() => _winds.Clear();

    public StarWind Wind(string star) => star != null && _winds.TryGetValue(star, out StarWind w) ? w : null;

    public SolarWindState At(string star, Double3 position, double ut)
    {
        StarWind wind = Wind(star);
        return wind != null ? wind.At(position, ut) : SolarWindState.Quiet(position.Length, SpaceWeather.SpaceWeather.ReferenceDistance);
    }
}
