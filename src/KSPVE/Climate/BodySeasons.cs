using System;
using System.Collections.Generic;
using System.Reflection;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Seasons;
using UnityEngine;

namespace KSPVE.Seasonal;

/// <summary>A body's seasons: the orbit that gives them (a moon takes its planet's), the real tilt and the one in use.</summary>
internal sealed class BodySeason
{
    public CelestialBody Body, Host, Star;
    /// <summary>The seasons as the game's bodies really are, and as shown (a pretend tilt replaces the axis).</summary>
    public SeasonOrbit Real, Active;
    /// <summary>The orbit's vectors could be matched to the world frame (otherwise the tilt is unknown and taken as none).</summary>
    public bool FrameFound;
    public bool Pretend;
    public int Revision;
    /// <summary>The star's irradiance at the host's semi-major axis (W/m^2).</summary>
    public double SolarConstant;
}

/// <summary>
/// Works out each body's seasons from its real axis and orbit. The orbit's vectors (periapsis, a quarter orbit on) come from
/// the game's <see cref="Orbit"/> in its own frame; the world frame they must be turned into is found by trying the
/// game's frames until the sun's direction from the orbit agrees with where the sun really is (to half a degree), so a
/// misremembered convention can never silently tilt a planet; if none agrees the body gets no seasons and a warning.
/// </summary>
internal static class BodySeasons
{
    private static readonly Dictionary<string, BodySeason> Cache = new Dictionary<string, BodySeason>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Warned = new HashSet<string>();
    private static Func<CelestialBody, double> _previous;
    private static string _frameName;

    /// <summary>Replaces the clouds' subsolar latitude with this (the real one unless a pretend tilt is set).</summary>
    public static void Install()
    {
        if (_previous != null) return;
        _previous = CloudWorld.SubsolarLatitudeOf ?? CloudWorld.RealSubsolarLatitude;
        CloudWorld.SubsolarLatitudeOf = SubsolarLatitude;
    }

    public static void Uninstall()
    {
        if (_previous == null) return;
        CloudWorld.SubsolarLatitudeOf = _previous;
        _previous = null;
    }

    public static void Forget() => Cache.Clear();

    /// <summary>The latitude (rad) the star is overhead at now: as it really is, or with a pretend tilt.</summary>
    public static double SubsolarLatitude(CelestialBody body)
    {
        BodySeason s = ClimateConfig.TryGetTilt(body.bodyName, out _) ? Of(body) : null;
        if (s == null || !s.Pretend) return (_previous ?? CloudWorld.RealSubsolarLatitude)(body);
        return s.Active.DeclinationAt(YearFraction(s, CloudWorld.Time));
    }

    /// <summary>The fraction of its year (time since perihelion over the period) a body is at.</summary>
    public static double YearFraction(BodySeason s, double ut)
    {
        Orbit o = s.Host.orbit;
        double f = o.getObtAtUT(ut) / o.period;
        return f - Math.Floor(f);
    }

    /// <summary>A body's seasons, or null when it does not orbit a star (or the game is not ready).</summary>
    public static BodySeason Of(CelestialBody body)
    {
        if (body == null || Planetarium.fetch == null) return null;
        if (Cache.TryGetValue(body.bodyName, out BodySeason s) && s.Body == body && s.Revision == ClimateConfig.Revision) return s;
        s = Build(body);
        if (s != null) Cache[body.bodyName] = s;
        return s;
    }

    private static BodySeason Build(CelestialBody body)
    {
        CelestialBody host = body;
        while (host != null && host.referenceBody != null && host.referenceBody != host && !CloudWorld.IsStar(host.referenceBody)) host = host.referenceBody;
        if (host == null || host.referenceBody == null || host.referenceBody == host || !CloudWorld.IsStar(host.referenceBody)) return null;
        Orbit o = host.orbit;
        if (o == null || !(o.eccentricity < 0.99) || !(o.period > 0.0) || double.IsInfinity(o.period)) return null;
        CelestialBody star = host.referenceBody;
        double ut = CloudWorld.Time;
        var s = new BodySeason { Body = body, Host = host, Star = star, Revision = ClimateConfig.Revision };
        s.SolarConstant = StarFlux(star, o.semiMajorAxis);

        // The orbit's perihelion and quarter-orbit directions, and the sun's real direction now, in one frame.
        Vector3d r0 = o.getRelativePositionFromTrueAnomaly(0.0), r90 = o.getRelativePositionFromTrueAnomaly(0.5 * Math.PI);
        Vector3d rNow = o.getRelativePositionFromTrueAnomaly(o.TrueAnomalyAtUT(ut));
        Vector3d sun = (star.position - body.position).normalized;
        CloudWorld.Basis(body, out _, out Vector3d north, out _);
        Func<Vector3d, Vector3d> toWorld = null;
        string name = null;
        double best = 0.0;
        foreach (KeyValuePair<string, Func<Vector3d, Vector3d>> candidate in Frames())
        {
            Vector3d predicted = (-candidate.Value(rNow)).normalized;
            double dot = Vector3d.Dot(predicted, sun);
            if (dot > best) best = dot;
            if (dot >= Math.Cos(0.5 * Math.PI / 180.0)) { toWorld = candidate.Value; name = candidate.Key; break; }
        }
        if (toWorld == null)
        {
            s.Real = s.Active = new SeasonOrbit(o.eccentricity, o.period);
            if (Warned.Add(body.bodyName))
                Log.Warning($"Seasons: the orbit of {host.bodyName} does not match the sun's direction in any of the game's frames (best {Math.Acos(Math.Min(best, 1.0)) * 180.0 / Math.PI:F1} degrees); {body.bodyName} gets no tilt.");
            return s;
        }
        if (_frameName != name)
        {
            _frameName = name;
            Log.Info($"Seasons: orbit vectors turned into the world frame by '{name}'.");
        }
        s.FrameFound = true;
        Vector3d e = toWorld(r0), q = toWorld(r90);
        s.Real = SeasonOrbit.FromAxis(ToD3(north), ToD3(e), ToD3(q), o.eccentricity, o.period);
        s.Active = s.Real;
        if (ClimateConfig.TryGetTilt(body.bodyName, out double degrees))
        {
            s.Active = s.Real.WithObliquity(degrees * Math.PI / 180.0);
            s.Pretend = true;
        }
        return s;
    }

    private static Double3 ToD3(Vector3d v) => new Double3(v.x, v.y, v.z);

    private static IEnumerable<KeyValuePair<string, Func<Vector3d, Vector3d>>> Frames()
    {
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("swizzle", v => v.xzy);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("planetarium rotation of the swizzle", v => Planetarium.Rotation * v.xzy);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("inverse planetarium rotation of the swizzle", v => UnityEngine.QuaternionD.Inverse(Planetarium.Rotation) * v.xzy);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("unchanged", v => v);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("planetarium rotation", v => Planetarium.Rotation * v);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("inverse planetarium rotation", v => UnityEngine.QuaternionD.Inverse(Planetarium.Rotation) * v);
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("zup local to world", v => Planetarium.Zup.LocalToWorld(v));
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("zup local to world of the swizzle", v => Planetarium.Zup.LocalToWorld(v.xzy));
        yield return new KeyValuePair<string, Func<Vector3d, Vector3d>>("zup world to local", v => Planetarium.Zup.WorldToLocal(v));
    }

    /// <summary>
    /// The star's irradiance (W/m^2) at a distance: its luminosity (the game's solar luminosity, or the star's own when
    /// a field called luminosity can be found on its scaled object) over 4 pi d^2.
    /// </summary>
    public static double StarFlux(CelestialBody star, double distance)
    {
        double luminosity = PhysicsGlobals.SolarLuminosity;
        if (star != null && Planetarium.fetch != null && star != Planetarium.fetch.Sun && star.scaledBody != null)
        {
            try
            {
                foreach (Component c in star.scaledBody.GetComponentsInChildren<Component>())
                {
                    if (c == null) continue;
                    Type t = c.GetType();
                    foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (m.Name.IndexOf("luminosity", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        object value = m is FieldInfo f ? f.GetValue(c) : m is PropertyInfo p && p.GetIndexParameters().Length == 0 && p.CanRead ? p.GetValue(c, null) : null;
                        if (value is double d && d > 0.0 && !double.IsInfinity(d)) { luminosity = d; goto found; }
                        if (value is float fl && fl > 0f) { luminosity = fl; goto found; }
                    }
                }
            }
            catch (Exception) { /* keep the game's solar luminosity */ }
        }
    found:
        return distance > 0.0 ? luminosity / (4.0 * Math.PI * distance * distance) : 1361.0;
    }
}
