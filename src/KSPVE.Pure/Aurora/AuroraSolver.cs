using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;

namespace KSPVE.Pure.Aurora;

/// <summary>A bright spot on a giant planet where a moon's field line meets it, with its tail.</summary>
public struct FootprintSpot
{
    /// <summary>Colatitude of the footprint from its pole (rad) and magnetic local time angle from midnight (rad, +dawnward); the tail runs east (+).</summary>
    public double Colatitude, Angle;
    /// <summary>kR of the giant row's reference line, and the tail's length (rad of angle).</summary>
    public double Brightness, Tail;
}

/// <summary>A crustal-field region in the body frame as a unit vector and a radius, with its strength.</summary>
public struct CrustalSpot
{
    public Double3 Direction;
    public double Radius, Strength;
}

/// <summary>
/// Everything the renderer needs about one body's aurora and airglow at a moment, in the body's turning frame (x towards
/// latitude 0 longitude 0, y north, z longitude 90 E): the geometry of its field and the sun, the oval, the substorm, and the
/// brightness (kR of each emission row's reference line; rayleighs for airglow) of every kind.
/// </summary>
public sealed class AuroraFrame
{
    public const int MaxSpots = 4, MaxCrustal = 4;

    public string Body;
    /// <summary>The body's seed folded to 24 bits (a float holds it exactly, so the shader can be given it).</summary>
    public uint Seed24;
    public Composition Composition;
    public bool HasField, Valid;
    public double Radius;
    /// <summary>Dipole centre (m from the body's centre) and axis, noon and dawn directions, and the sun direction (unit), body frame.</summary>
    public Double3 Centre, Axis, Noon, Dawn, Sun;
    public OvalState Oval;
    public SubstormState Storm;
    public double Ut;

    // kR of each kind's reference line at its brightest.
    public double Arc, HardShare, SoftShare, Diffuse, Pulsating, Proton, Black, Sar, Steve, Theta, Global, Crustal, Giant;
    /// <summary>Airglow in rayleighs: green 557.7, sodium 589, hydroxyl (visible), red 630.</summary>
    public double GlowGreen, GlowSodium, GlowHydroxyl, GlowRed;
    public readonly FootprintSpot[] Spots = new FootprintSpot[MaxSpots];
    public int SpotCount;
    public readonly CrustalSpot[] CrustalSpots = new CrustalSpot[MaxCrustal];
    public int CrustalCount;
    /// <summary>The solar wind as the body meets it.</summary>
    public SolarWindState Wind;
    public bool InStorm;
    public bool StevePresent, ThetaPresent;

    /// <summary>The oval's equatorward edge at midnight, in degrees of latitude.</summary>
    public double OvalLatitudeDegrees => Oval.NightLatitudeDegrees;
}

/// <summary>Works out an <see cref="AuroraFrame"/> from a body model, the solar wind and the time.</summary>
public static class AuroraSolver
{
    /// <summary>A moon of the body, for footprints.</summary>
    public struct MoonPosition
    {
        public string Name;
        /// <summary>Position in the body frame, metres from the body's centre.</summary>
        public Double3 Position;
    }

    /// <summary>
    /// <paramref name="windAt"/> gives the solar wind at the body for a game time; <paramref name="sunBody"/> is the unit
    /// direction to the star in the body frame; <paramref name="kpOverride"/> forces the activity index (previews, tests).
    /// </summary>
    public static AuroraFrame Solve(AuroraBodyModel model, double radius, Func<double, SolarWindState> windAt, double ut, Double3 sunBody,
        IList<MoonPosition> moons = null, double? kpOverride = null)
    {
        var f = new AuroraFrame { Body = model.Name, Seed24 = (uint)((model.Seed ^ (model.Seed >> 24) ^ (model.Seed >> 48)) & 0xFFFFFFUL), Composition = model.Composition, Radius = radius, Ut = ut, Valid = true };
        SolarWindState wind = windAt(ut);
        f.Wind = wind;
        f.Sun = sunBody.Normalized();
        f.HasField = model.HasField;
        double logParticles = Math.Max(0.0, Math.Log10(Math.Max(wind.EnergeticParticles, 1.0)));

        if (model.HasField)
        {
            f.Axis = model.Field.Axis.Normalized();
            f.Centre = model.Field.Offset * radius;
            Double3 noon = f.Sun - f.Axis * Double3.Dot(f.Sun, f.Axis);
            if (noon.LengthSquared < 1e-12) noon = Math.Abs(f.Axis.Y) < 0.9 ? Double3.Cross(f.Axis, Double3.UnitY) : Double3.Cross(f.Axis, Double3.UnitX);
            f.Noon = noon.Normalized();
            // The body frame (x latitude 0 longitude 0, y north, z longitude 90 E) is left-handed, so the 6 o'clock meridian is axis x noon.
            f.Dawn = Double3.Cross(f.Axis, f.Noon).Normalized();

            double kp;
            if (model.Oval == OvalMode.Corotation)
            {
                kp = 2.0;   // a nominal level: the giants' aurora hardly follows the wind
                f.Oval = OvalModel.Corotating(model.Field, wind, model.OvalColatitudeDegrees, model.OvalHalfWidthDegrees);
                f.Oval.Kp = kp;
            }
            else
            {
                kp = kpOverride ?? Activity.Kp(t => windAt(t), ut, model.Field.Polarity);
                f.Oval = OvalModel.SolarDriven(model.Field, wind, kp);
            }
            f.Storm = Substorms.At(ut, kp, model.Seed);
            FillDipoleKinds(f, model, wind, kp, logParticles, moons);
        }
        else
        {
            f.Axis = Double3.UnitY; f.Noon = f.Sun; f.Dawn = Double3.Cross(f.Axis, f.Noon).Normalized();
            if (f.Dawn.LengthSquared < 1e-9) f.Dawn = Double3.UnitX;
            f.Storm = default;
            // No global field: particles fall over the whole night side, or over crustal fields, during energetic particle events.
            double events = Math.Pow(logParticles, 1.5);
            f.Global = model.Aurora ? 0.05 * events * model.AuroraScale : 0.0;
            f.Crustal = model.Aurora && model.Crustal.Count > 0 ? (0.05 + 0.3 * events) * model.AuroraScale : 0.0;
            f.Oval = new OvalState();
            f.InStorm = logParticles > 2.0;
        }
        FillCrustal(f, model, radius);
        double glow = model.Airglow ? model.AirglowScale : 0.0;
        f.GlowGreen = 250.0 * glow; f.GlowSodium = 30.0 * glow; f.GlowHydroxyl = 200.0 * glow; f.GlowRed = 15.0 * glow;
        if (!model.Aurora) { f.Arc = f.Diffuse = f.Proton = f.Sar = f.Steve = f.Theta = f.Global = f.Crustal = f.Giant = 0.0; f.SpotCount = 0; }
        return f;
    }

    private static void FillDipoleKinds(AuroraFrame f, AuroraBodyModel model, SolarWindState wind, double kp, double logParticles, IList<MoonPosition> moons)
    {
        double scale = model.AuroraScale;
        SubstormState s = f.Storm;
        bool giant = model.Oval == OvalMode.Corotation;
        if (giant)
        {
            // The main oval is steady; a moon's footprint is a spot at the foot of its field line with a tail downstream.
            f.Giant = 30.0 * scale;
            f.Diffuse = 2.0 * scale;
            f.Proton = 0.5 * scale;
            f.Arc = 0.0;
            if (moons != null)
            {
                foreach (MoonPosition m in moons)
                {
                    FootprintMoon def = model.Moons.Find(x => string.Equals(x.Name, m.Name, StringComparison.OrdinalIgnoreCase));
                    if (def == null || f.SpotCount >= AuroraFrame.MaxSpots) continue;
                    Double3 q = m.Position - f.Centre;
                    double r = q.Length / f.Radius;
                    if (r <= 1.0) continue;
                    double mu = Math.Max(-1.0, Math.Min(1.0, Double3.Dot(q / q.Length, f.Axis)));
                    double colat = Math.Acos(mu);
                    double foot = Dipole.FootprintColatitude(r, colat);
                    double psi = Math.Atan2(Double3.Dot(q, f.Dawn), -Double3.Dot(q, f.Noon));   // from midnight, towards dawn
                    f.Spots[f.SpotCount++] = new FootprintSpot
                    {
                        Colatitude = foot, Angle = psi + def.LeadDegrees * Math.PI / 180.0,   // the footprint leads, i.e. is east
                        Brightness = def.Brightness * scale, Tail = def.TailDegrees * Math.PI / 180.0,
                    };
                }
            }
            f.InStorm = false;
            return;
        }

        // Arcs: IBC I-II (1-10 kR of 557.7) when quiet, rising a tenth of a decade per unit of Kp; breakup flares them up to
        // IBC III-IV (100 kR and more).
        double arc = 8.0 * Math.Pow(10.0, 0.11 * kp) * (1.0 + 0.5 * s.Growth * s.Strength) * (1.0 + 6.0 * s.Breakup * s.Strength);
        f.Arc = arc * scale;
        f.HardShare = Math.Min(1.0, 0.08 + 0.06 * kp + 0.6 * s.Breakup * s.Strength);
        f.SoftShare = Math.Min(0.8, 0.04 + 0.05 * kp);
        f.Diffuse = (0.7 + 0.25 * kp) * scale;
        f.Pulsating = s.Pulsating * Math.Min(1.0, 0.4 + 0.1 * kp);
        f.Proton = (0.04 + 0.05 * kp) * scale;
        f.Black = Math.Min(1.0, 0.35 + 0.08 * kp) * (s.Phase == SubstormPhase.Recovery ? 1.0 : 0.4);
        f.Sar = kp > 4.5 ? 0.12 * Math.Pow(kp - 4.0, 1.3) * scale : 0.0;
        // STEVE needs a strong storm and a substorm (Gallardo-Lacourt et al. 2018: seen at Kp 3-9, mostly in the recovery
        // phase, in about half of the storm-time substorms).
        double steveChance = Math.Max(0.0, (kp - 3.5) * 0.15);
        f.StevePresent = s.Phase != SubstormPhase.None && s.Phase != SubstormPhase.Growth && Unit(model.Seed, s.OnsetUt) < steveChance;
        f.Steve = f.StevePresent ? 6.0 * s.Strength * scale : 0.0;
        // Theta aurora: northward interplanetary field (relative to the planet's own) fills the polar cap with sun-aligned arcs.
        double north = -model.Field.Polarity * wind.FieldNorth;   // positive: northward in the sense that does not reconnect
        f.ThetaPresent = north > 3e-9;
        f.Theta = f.ThetaPresent ? 2.0 * Math.Min(1.0, (north - 3e-9) / 6e-9) * scale : 0.0;
        f.Global = logParticles > 2.0 ? 0.04 * logParticles * scale : 0.0;
        f.InStorm = kp >= 5.0 || wind.InCme > 0.5;
    }

    private static void FillCrustal(AuroraFrame f, AuroraBodyModel model, double radius)
    {
        f.CrustalCount = 0;
        foreach (CrustalRegion r in model.Crustal)
        {
            if (f.CrustalCount >= AuroraFrame.MaxCrustal) break;
            double lat = r.LatitudeDegrees * Math.PI / 180.0, lon = r.LongitudeDegrees * Math.PI / 180.0;
            f.CrustalSpots[f.CrustalCount++] = new CrustalSpot
            {
                Direction = new Double3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon)),
                Radius = r.RadiusKm * 1000.0 / radius, Strength = r.Strength,
            };
        }
    }

    private static double Unit(ulong seed, double ut)
    {
        unchecked
        {
            ulong z = seed ^ (ulong)(long)Math.Floor(ut) * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) / 9007199254740992.0;
        }
    }
}
