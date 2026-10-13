using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Stars;

/// <summary>Where a point lies in a CME, from the star outwards: the undisturbed wind, the shocked sheath, the magnetic cloud.</summary>
public enum CmeZone
{
    Outside = 0,
    /// <summary>Between the shock and the cloud: shocked, compressed, turbulent wind.</summary>
    Sheath = 1,
    /// <summary>The magnetic cloud: a smooth flux rope whose field turns slowly.</summary>
    Cloud = 2
}

/// <summary>Where a point is in (or ahead of) a CME at a time.</summary>
public struct CmeSample
{
    public CmeZone Zone;
    /// <summary>How far through the zone, 0 at its front edge to 1 at its back.</summary>
    public double Fraction;
    /// <summary>Angle from the CME's axis (rad).</summary>
    public double Offset;
    /// <summary>The leading edge's radius along that direction (m), and its speed there (m/s).</summary>
    public double LeadRadius, LeadSpeed;
}

/// <summary>
/// A coronal mass ejection: launched from the star at a time and direction, a cone of a half-width, travelling out under
/// the drag-based model. Close to the star (up to <see cref="HandOffRadii"/> stellar radii) it accelerates to its launch
/// speed; beyond, the aerodynamic drag of the ambient wind pulls it towards the wind's speed (Vrsnak et al. 2013, Solar
/// Physics 285, 295): dv/dt = -gamma (v - w)|v - w|, which integrates in closed form,
///   v(t) = w + (v0 - w) / (1 + s gamma (v0 - w) t),   r(t) = r0 + w t + (s / gamma) ln(1 + s gamma (v0 - w) t),
/// s the sign of (v0 - w). The front is the "ice-cream cone" of Schwadron et al. (2005): a sphere in a cone from the star.
/// Its structure is carried out self-similarly: a shock sheath about a tenth of the front's distance deep, behind it a
/// magnetic cloud (flux rope) about a fifth, which at 1 AU and 450 km/s take about 9 and 20 hours to pass (typical
/// observations: sheaths 5-12 h, clouds 15-30 h).
/// </summary>
public sealed class Cme
{
    public const double SolarRadius = 6.957e8;
    /// <summary>Stellar radii at which the drag-based model takes over (Vrsnak et al. use 20).</summary>
    public const double HandOffRadii = 20.0;
    /// <summary>Radii at which the eruption starts to rise.</summary>
    public const double StartRadii = 1.1;
    /// <summary>The sheath's depth and the cloud's, as fractions of the front's distance from the star.</summary>
    public const double SheathDepth = 0.10, CloudDepth = 0.22;

    public long Id;
    /// <summary>When it leaves the star (s of game time).</summary>
    public double LaunchTime;
    /// <summary>Where it leaves (rad; model frame: y is the star's north, longitude from x towards z).</summary>
    public double Latitude, Longitude;
    /// <summary>The speed it reaches in the corona (m/s), at the start of the drag-based flight.</summary>
    public double LaunchSpeed;
    /// <summary>Half the angular width (rad) of its cone.</summary>
    public double HalfWidth;
    /// <summary>The drag parameter gamma (1/m) and the ambient wind speed w (m/s).</summary>
    public double Drag, WindSpeed;
    /// <summary>The star's radius (m).</summary>
    public double StarRadius = SolarRadius;
    /// <summary>The flare that went with it (peak soft X-ray flux, W/m^2), 0 for none.</summary>
    public double FlareFlux;
    /// <summary>+1 or -1: the flux rope's handedness (Bothmer &amp; Schwenn: the hemisphere's helicity sign, flipping each cycle).</summary>
    public double Helicity = 1.0;
    /// <summary>The flux rope's axis (rad from east, in the plane across the radial direction; +-pi/2 is along the star's north), and the closest approach of a straight path to it (-0.8..0.8 of its radius).</summary>
    public double AxisAngle, Impact;
    /// <summary>The magnetic cloud's peak field at 1 AU-equivalent (T).</summary>
    public double CloudField = 1.5e-8;
    /// <summary>Added by hand (a test), not by the model.</summary>
    public bool Injected;
    /// <summary>What erupted: "Active region 3", "Filament", "Test".</summary>
    public string Source = "";

    private double _riseTau, _handOffTime, _handOffSpeed;

    /// <summary>Works out the rise and the hand-off to the drag-based flight. Call after setting the fields.</summary>
    public Cme Prepare()
    {
        double r0 = HandOffRadii * StarRadius, rs = StartRadii * StarRadius;
        _riseTau = 4.0 * StarRadius / Math.Max(LaunchSpeed, 1.0);
        double t = (r0 - rs) / Math.Max(LaunchSpeed, 1.0) + _riseTau;
        for (int i = 0; i < 40; i++)
        {
            double f = RiseDistance(t) - r0, df = LaunchSpeed * (1.0 - Math.Exp(-t / _riseTau));
            if (df < 1.0) break;
            double next = t - f / df;
            if (Math.Abs(next - t) < 1e-6) { t = next; break; }
            t = next;
        }
        _handOffTime = t;
        _handOffSpeed = LaunchSpeed * (1.0 - Math.Exp(-t / _riseTau));
        return this;
    }

    public Double3 Direction => new Double3(Math.Cos(Latitude) * Math.Cos(Longitude), Math.Sin(Latitude), Math.Cos(Latitude) * Math.Sin(Longitude));

    /// <summary>Seconds after launch at which the drag-based flight begins (about 7 h at 1000 km/s).</summary>
    public double HandOffTime => _handOffTime;

    private double RiseDistance(double t) => StartRadii * StarRadius + LaunchSpeed * (t - _riseTau * (1.0 - Math.Exp(-t / _riseTau)));

    /// <summary>The apex's distance from the star's centre (m) at game time <paramref name="ut"/>.</summary>
    public double Distance(double ut)
    {
        double t = ut - LaunchTime;
        if (t <= 0.0) return StartRadii * StarRadius;
        if (t <= _handOffTime) return RiseDistance(t);
        return DragDistance(t - _handOffTime);
    }

    /// <summary>The apex's radial speed (m/s) at game time <paramref name="ut"/>.</summary>
    public double Speed(double ut)
    {
        double t = ut - LaunchTime;
        if (t <= 0.0) return 0.0;
        if (t <= _handOffTime) return LaunchSpeed * (1.0 - Math.Exp(-t / _riseTau));
        double delta = _handOffSpeed - WindSpeed;
        double s = delta >= 0.0 ? 1.0 : -1.0;
        return WindSpeed + delta / (1.0 + s * Drag * delta * (t - _handOffTime));
    }

    private double DragDistance(double t)
    {
        double delta = _handOffSpeed - WindSpeed;
        double s = delta >= 0.0 ? 1.0 : -1.0;
        return HandOffRadii * StarRadius + WindSpeed * t + s / Drag * Math.Log(1.0 + s * Drag * delta * t);
    }

    /// <summary>Seconds after launch at which the apex is <paramref name="distance"/> metres from the star.</summary>
    public double TimeToReach(double distance)
    {
        if (distance <= StartRadii * StarRadius) return 0.0;
        if (distance <= HandOffRadii * StarRadius)
        {
            double lo = 0.0, hi = _handOffTime;
            for (int i = 0; i < 80; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (RiseDistance(mid) < distance) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }
        double a = 0.0, b = 3600.0;
        while (DragDistance(b) < distance && b < 1e10) b *= 2.0;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (a + b);
            if (DragDistance(mid) < distance) a = mid; else b = mid;
        }
        return _handOffTime + 0.5 * (a + b);
    }

    /// <summary>
    /// The leading edge's radius along a direction <paramref name="offset"/> radians from the axis, per unit apex distance
    /// (1 on the axis); 0 outside the cone. Schwadron et al. (2005) ice-cream cone: a sphere of radius a = r sin(w)/(1+sin(w))
    /// about a centre c = r - a along the axis, cut by the cone.
    /// </summary>
    public double EdgeFactor(double offset)
    {
        if (offset > HalfWidth || HalfWidth <= 0.0) return 0.0;
        double s = Math.Sin(Math.Min(HalfWidth, Math.PI * 0.5));
        double a = s / (1.0 + s), c = 1.0 - a, sin = Math.Sin(offset);
        double d = a * a - c * c * sin * sin;
        return c * Math.Cos(offset) + Math.Sqrt(Math.Max(d, 0.0));
    }

    /// <summary>The angle (rad) between the CME's axis and a direction from the star.</summary>
    public double OffsetOf(Double3 position)
    {
        double len = position.Length;
        if (len < 1.0) return Math.PI;
        double dot = Double3.Dot(Direction, position) / len;
        return Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
    }

    /// <summary>When the front reaches a point (game time, s), NaN when the point is outside the cone.</summary>
    public double ArrivalTime(Double3 position)
    {
        double k = EdgeFactor(OffsetOf(position));
        if (k <= 0.0) return double.NaN;
        return LaunchTime + TimeToReach(position.Length / k);
    }

    /// <summary>The time the back of the magnetic cloud has passed a point (s), NaN when the point is outside the cone.</summary>
    public double DepartureTime(Double3 position)
    {
        double offset = OffsetOf(position);
        double k = EdgeFactor(offset);
        if (k <= 0.0) return double.NaN;
        double d = position.Length;
        // The back edge of the cloud is (SheathDepth + CloudDepth * taper) of the leading edge's radius behind it.
        double back = (SheathDepth + CloudDepth * CloudTaper(offset)) ;
        double lead = d / (1.0 - back);
        return LaunchTime + TimeToReach(lead / k);
    }

    /// <summary>The cloud is thinner towards the flank of the cone (it is not there beyond 80 per cent of the half-width).</summary>
    public double CloudTaper(double offset)
    {
        double x = offset / Math.Max(0.8 * HalfWidth, 1e-9);
        return x >= 1.0 ? 0.0 : Math.Sqrt(1.0 - x * x);
    }

    /// <summary>Where a point lies in this CME at <paramref name="ut"/>.</summary>
    public CmeSample Sample(Double3 position, double ut)
    {
        var sample = new CmeSample { Zone = CmeZone.Outside };
        double offset = OffsetOf(position);
        sample.Offset = offset;
        double k = EdgeFactor(offset);
        if (k <= 0.0 || ut <= LaunchTime) return sample;
        double apex = Distance(ut);
        double lead = apex * k;
        sample.LeadRadius = lead;
        sample.LeadSpeed = Speed(ut) * k;
        double depth = lead - position.Length;
        if (depth < 0.0) return sample;
        double sheath = SheathDepth * lead;
        if (depth < sheath)
        {
            sample.Zone = CmeZone.Sheath;
            sample.Fraction = depth / sheath;
            return sample;
        }
        double cloud = CloudDepth * lead * CloudTaper(offset);
        if (cloud > 0.0 && depth < sheath + cloud)
        {
            sample.Zone = CmeZone.Cloud;
            sample.Fraction = (depth - sheath) / cloud;
        }
        return sample;
    }

    /// <summary>A copy, prepared again.</summary>
    public Cme Copy() => ((Cme)MemberwiseClone()).Prepare();
}
