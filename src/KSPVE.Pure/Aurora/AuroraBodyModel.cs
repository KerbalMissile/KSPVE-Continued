using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Aurora;

public enum OvalMode
{
    /// <summary>No oval: the body has no global field (aurora, if any, is global or over crustal fields).</summary>
    None,
    /// <summary>Set by the solar wind (Earth, Mercury): the oval moves equatorward with activity.</summary>
    Solar,
    /// <summary>Set by the magnetosphere's co-rotation (Jupiter, Saturn): nearly fixed.</summary>
    Corotation,
}

/// <summary>A patch of strong crustal magnetisation (Mars): discrete aurora sit over it when energetic particles arrive.</summary>
public sealed class CrustalRegion
{
    public double LatitudeDegrees, LongitudeDegrees, RadiusKm, Strength = 1.0;
}

/// <summary>A moon inside a planet's magnetosphere that draws a bright spot (and a trailing tail) on the planet (Io, Europa, Ganymede).</summary>
public sealed class FootprintMoon
{
    public string Name;
    /// <summary>Footprint brightness (kR of the giant row's reference line).</summary>
    public double Brightness = 8.0;
    /// <summary>Length of the trailing tail (degrees of longitude).</summary>
    public double TailDegrees = 40.0;
    /// <summary>The footprint leads or trails the moon's own field line by this many degrees of longitude (Io: 10-15).</summary>
    public double LeadDegrees = 10.0;
}

/// <summary>Everything the aurora and airglow models need to know about one body (from KSPVE_AURORA, or guessed).</summary>
public sealed class AuroraBodyModel
{
    public string Name;
    public Dipole Field;
    public OvalMode Oval = OvalMode.None;
    /// <summary>Corotation ovals: colatitude and half-width of the main oval in degrees.</summary>
    public double OvalColatitudeDegrees = 16.0, OvalHalfWidthDegrees = 1.2;
    public Composition Composition = Composition.N2O2;
    /// <summary>Draw aurora at all (an airless body has none).</summary>
    public bool Aurora = true;
    public bool Airglow = true;
    /// <summary>Scales the airglow layers' real brightnesses (1 = Earth's).</summary>
    public double AirglowScale = 1.0;
    /// <summary>Scales the aurora's brightnesses (1 = the body's natural level for its activity).</summary>
    public double AuroraScale = 1.0;
    /// <summary>The exospheric and mesospheric temperature (K) for quenching.</summary>
    public double ExosphereTemperature = 1000.0, MesosphereTemperature = 190.0;
    public List<CrustalRegion> Crustal = new List<CrustalRegion>();
    public List<FootprintMoon> Moons = new List<FootprintMoon>();
    /// <summary>The star whose wind drives it (empty = the body's own star).</summary>
    public string Star = string.Empty;
    /// <summary>Seed for substorms and structure (from the name unless given).</summary>
    public ulong Seed;
    /// <summary>Where the numbers came from: "config", or "guess" for the fallback.</summary>
    public string Source = "config";

    public bool HasField => Field.Exists;

    public static ulong SeedOf(string name)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in name ?? string.Empty) { h ^= c; h *= 1099511628211UL; }
            return h;
        }
    }

    // Config keys. Strengths in microtesla, angles in degrees, distances in kilometres or planetary radii as named.
    public static AuroraBodyModel FromConfig(CfgNode node, List<ConfigIssue> issues)
    {
        string name = node.GetLast("name") ?? string.Empty;
        var m = new AuroraBodyModel { Name = name, Seed = SeedOf(name) };
        double field = Number(node, "fieldStrength", 0.0, issues, name);   // microtesla at the magnetic equator on the surface
        m.Field = new Dipole
        {
            EquatorialField = field * 1e-6,
            Tilt = Number(node, "tilt", 0.0, issues, name) * Math.PI / 180.0,
            TiltLongitude = Number(node, "tiltLongitude", 0.0, issues, name) * Math.PI / 180.0,
            Offset = new Double3(Number(node, "offsetX", 0.0, issues, name), Number(node, "offsetY", 0.0, issues, name), Number(node, "offsetZ", 0.0, issues, name)),
            Polarity = Number(node, "polarity", -1.0, issues, name) >= 0 ? 1.0 : -1.0,
        };
        string mode = (node.GetLast("oval") ?? (field > 0 ? "solar" : "none")).Trim().ToLowerInvariant();
        m.Oval = mode == "corotation" ? OvalMode.Corotation : mode == "none" ? OvalMode.None : OvalMode.Solar;
        if (!m.HasField) m.Oval = OvalMode.None;
        m.OvalColatitudeDegrees = Number(node, "ovalColatitude", 16.0, issues, name);
        m.OvalHalfWidthDegrees = Number(node, "ovalWidth", 1.2, issues, name);
        m.Composition = ParseComposition(node.GetLast("composition"), Composition.N2O2);
        m.Aurora = Bool(node, "aurora", true);
        m.Airglow = Bool(node, "airglow", true);
        m.AirglowScale = Number(node, "airglowScale", 1.0, issues, name);
        m.AuroraScale = Number(node, "auroraScale", 1.0, issues, name);
        m.ExosphereTemperature = Number(node, "exosphereTemperature", 1000.0, issues, name);
        m.MesosphereTemperature = Number(node, "mesosphereTemperature", 190.0, issues, name);
        m.Star = node.GetLast("star") ?? string.Empty;
        string seed = node.GetLast("seed");
        if (seed != null && ulong.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s)) m.Seed = s;
        foreach (CfgNode child in node.Children)
        {
            if (child.Name.Equals("CRUSTAL", StringComparison.OrdinalIgnoreCase))
                m.Crustal.Add(new CrustalRegion
                {
                    LatitudeDegrees = Number(child, "latitude", 0.0, issues, name), LongitudeDegrees = Number(child, "longitude", 0.0, issues, name),
                    RadiusKm = Number(child, "radius", 500.0, issues, name), Strength = Number(child, "strength", 1.0, issues, name),
                });
            else if (child.Name.Equals("MOON", StringComparison.OrdinalIgnoreCase))
                m.Moons.Add(new FootprintMoon
                {
                    Name = child.GetLast("name") ?? string.Empty, Brightness = Number(child, "brightness", 8.0, issues, name),
                    TailDegrees = Number(child, "tail", 40.0, issues, name), LeadDegrees = Number(child, "lead", 10.0, issues, name),
                });
        }
        return m;
    }

    public static Composition ParseComposition(string text, Composition fallback)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "n2o2": case "air": case "n2": return Composition.N2O2;
            case "co2": return Composition.CO2;
            case "h2he": case "h2": case "hydrogen": return Composition.H2He;
            case "n2ch4": case "titan": return Composition.N2CH4;
            default: return fallback;
        }
    }

    private static bool Bool(CfgNode node, string key, bool fallback)
    {
        string v = node.GetLast(key);
        return v != null && bool.TryParse(v.Trim(), out bool b) ? b : fallback;
    }

    private static double Number(CfgNode node, string key, double fallback, List<ConfigIssue> issues, string body)
    {
        string v = node.GetLast(key);
        if (v == null) return fallback;
        if (double.TryParse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        issues?.Add(new ConfigIssue(IssueSeverity.Warning, "KSPVE_AURORA/" + body + "/" + key, "'" + v + "' is not a number; using " + fallback.ToString(CultureInfo.InvariantCulture)));
        return fallback;
    }

    /// <summary>What the game says about a body, for guessing a model when the config has none.</summary>
    public struct Facts
    {
        public string Name;
        public double Radius, MolarMass, SurfacePressure, SurfaceGravity, RotationPeriod;
        public bool HasAtmosphere, ContainsOxygen, IsStar;
    }

    /// <summary>
    /// A fallback model for a body the config does not know. The atmosphere's composition follows its molar mass (under 6 g/mol
    /// hydrogen and helium; 38 g/mol and up carbon dioxide; otherwise nitrogen and oxygen). A body that spins in under about 40
    /// hours and is large enough to hold a hot core is given a dipole by a hash of its name, so it is the same every game: giants
    /// 4-40 times Earth's field, rocky bodies 0.1-2 times Earth's scaled by size, tilted up to 30 degrees (giants up to 60), giants
    /// offset up to 0.3 radii. Bodies without a dipole get a global aurora when energetic particles arrive.
    /// </summary>
    public static AuroraBodyModel Guess(Facts f)
    {
        var m = new AuroraBodyModel { Name = f.Name, Seed = SeedOf(f.Name), Source = "guess" };
        if (!f.HasAtmosphere || f.IsStar) { m.Aurora = false; m.Airglow = false; return m; }
        bool giant = f.MolarMass < 0.006;
        m.Composition = giant ? Composition.H2He : f.MolarMass >= 0.038 ? Composition.CO2 : Composition.N2O2;
        ulong h = SeedOf(f.Name);
        double u1 = Frac(h, 1), u2 = Frac(h, 2), u3 = Frac(h, 3), u4 = Frac(h, 4), u5 = Frac(h, 5);
        bool dynamo = giant || (f.Radius > 1.0e6 && f.RotationPeriod > 0.0 && f.RotationPeriod < 1.5e5 && u1 < 0.55);
        if (dynamo)
        {
            double earth = 31.0e-6, rr = f.Radius / 6371000.0;
            double b = giant ? earth * (4.0 + 36.0 * u2) : earth * (0.1 + 1.9 * u2) * Math.Min(rr * rr, 4.0);
            m.Field = new Dipole
            {
                EquatorialField = b,
                Tilt = (giant && u3 > 0.75 ? 60.0 * u3 : 30.0 * u3) * Math.PI / 180.0,
                TiltLongitude = 2.0 * Math.PI * u4,
                Offset = giant ? new Double3(0.3 * (u2 - 0.5), 0.3 * (u3 - 0.5), 0.3 * (u4 - 0.5)) : Double3.Zero,
                Polarity = u5 < 0.5 ? -1.0 : 1.0,
            };
            m.Oval = giant ? OvalMode.Corotation : OvalMode.Solar;
            m.OvalColatitudeDegrees = 12.0 + 8.0 * u5;
        }
        m.ExosphereTemperature = giant ? 800.0 : 1000.0;
        return m;
    }

    private static double Frac(ulong h, int stream)
    {
        unchecked
        {
            ulong z = h + (ulong)stream * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) / 9007199254740992.0;
        }
    }
}
