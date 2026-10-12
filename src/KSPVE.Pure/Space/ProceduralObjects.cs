using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>A body as the procedural planner sees it (any planet pack's).</summary>
public sealed class SystemBody
{
    public string Name = string.Empty;
    /// <summary>The body it orbits; empty for the root.</summary>
    public string Parent = string.Empty;
    public bool IsStar;
    public double Radius, SemiMajorAxis, Eccentricity;
    /// <summary>Its orbit's normal (inertial frame); zero when unknown.</summary>
    public Double3 OrbitNormal;
}

/// <summary>
/// Objects placed by rule round any planet pack's stars, from one KSPVE_PROCEDURAL node, so a system needs no hand-written
/// config: an asteroid belt in each star's widest gap between neighbouring planets' orbits, a debris disk (a Kuiper belt)
/// beyond its outermost planet, and comet tails on small bodies on eccentric orbits round a star. Each object's look varies
/// from a hash of the seed and its body's name, so every system differs and stays the same from load to load. Bodies that
/// already have an object of that kind (configured, or added in game) are left alone.
/// </summary>
public static class ProceduralObjects
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE_PROCEDURAL");

    public static readonly BoolSetting Enabled = Schema.Add(new BoolSetting("enabled", false, "Place objects round every star by these rules."));
    public static readonly IntSetting Seed = Schema.Add(new IntSetting("seed", 1, int.MinValue, int.MaxValue, "Varies which systems get what, and every object's look; the same seed gives the same objects."));
    public static readonly BoolSetting Belts = Schema.Add(new BoolSetting("belts", true, "An asteroid belt in each star's widest gap between neighbouring planets' orbits."));
    public static readonly DoubleSetting BeltChance = Schema.Add(new DoubleSetting("beltChance", 1.0, 0.0, 1.0, "Share of the stars with a wide enough gap that get a belt."));
    public static readonly DoubleSetting BeltMinGap = Schema.Add(new DoubleSetting("beltMinGap", 1.8, 1.01, 1e6, "The least ratio between neighbouring planets' orbits that holds a belt (Mars to Jupiter is 3.4)."));
    public static readonly DoubleSetting PlanetMinRadius = Schema.Add(new DoubleSetting("planetMinRadius", 100000.0, 0.0, 1e12, "Bodies at least this big (radius, m) count as planets that clear gaps; smaller ones (dwarf planets, asteroids) can sit in a belt."));
    public static readonly BoolSetting OuterDisks = Schema.Add(new BoolSetting("outerDisks", true, "A debris disk (a Kuiper belt) beyond each star's outermost planet."));
    public static readonly DoubleSetting OuterDiskChance = Schema.Add(new DoubleSetting("outerDiskChance", 0.7, 0.0, 1.0, "Share of the stars with planets that get one."));
    public static readonly BoolSetting Comets = Schema.Add(new BoolSetting("comets", true, "Comet tails on small bodies on eccentric orbits round a star."));
    public static readonly DoubleSetting CometMaxRadius = Schema.Add(new DoubleSetting("cometMaxRadius", 25000.0, 0.0, 1e12, "Bodies up to this radius (m) can be comets."));
    public static readonly DoubleSetting CometMinEccentricity = Schema.Add(new DoubleSetting("cometMinEccentricity", 0.3, 0.0, 1.0, "...on orbits at least this eccentric."));
    public static readonly DoubleSetting Variety = Schema.Add(new DoubleSetting("variety", 0.5, 0.0, 1.0, "How much the objects' colours, thickness and number of rocks vary from system to system."));
    public static readonly DoubleSetting StarTemperature = Schema.Add(new DoubleSetting("starTemperature", 5800.0, 1.0, 1e9, "Temperature of the stars lighting them (K), as the game does not say."));

    /// <summary>A hash of the seed, a name and a salt, evenly spread over [0,1) (FNV-1a).</summary>
    public static double Hash(int seed, string name, string salt)
    {
        unchecked
        {
            uint h = 2166136261u ^ (uint)seed;
            foreach (char c in name + "|" + salt) { h ^= c; h *= 16777619u; }
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>
    /// The objects the rules place, as KSPVE_BLACKHOLE nodes (kind, body and settings; no slot). <paramref name="taken"/>
    /// holds "body|Kind" for the objects bodies already have.
    /// </summary>
    public static List<CfgNode> Plan(IReadOnlyList<SystemBody> bodies, SettingsValues rules, ISet<string> taken)
    {
        var plan = new List<CfgNode>();
        if (!rules.Get(Enabled)) return plan;
        int seed = rules.Get(Seed);
        double variety = rules.Get(Variety);
        double kelvin = rules.Get(StarTemperature);

        var byName = new Dictionary<string, SystemBody>();
        foreach (SystemBody b in bodies) byName[b.Name] = b;

        foreach (SystemBody star in bodies)
        {
            if (!star.IsStar || star.Radius <= 0.0) continue;
            var planets = new List<SystemBody>();
            foreach (SystemBody b in bodies)
                if (b.Parent == star.Name && !b.IsStar && b.SemiMajorAxis > star.Radius && b.Radius >= rules.Get(PlanetMinRadius) && !IsComet(b, rules))
                    planets.Add(b);
            planets.Sort((a, b) => a.SemiMajorAxis.CompareTo(b.SemiMajorAxis));
            Double3 normal = MeanNormal(planets);

            // A belt in the widest gap, clear of both planets' reach (Hill spheres and resonances: ~1.3x and ~0.75x).
            if (rules.Get(Belts) && planets.Count >= 2 && !taken.Contains(star.Name + "|" + SpaceObjectKind.AsteroidBelt)
                && Hash(seed, star.Name, "belt") < rules.Get(BeltChance))
            {
                int best = -1;
                double bestRatio = 0.0;
                for (int i = 0; i + 1 < planets.Count; i++)
                {
                    double ratio = planets[i + 1].SemiMajorAxis / planets[i].SemiMajorAxis;
                    if (ratio > bestRatio) { bestRatio = ratio; best = i; }
                }
                if (best >= 0 && bestRatio >= rules.Get(BeltMinGap))
                {
                    double inner = planets[best].SemiMajorAxis * 1.3 / star.Radius;
                    double outer = planets[best + 1].SemiMajorAxis * 0.75 / star.Radius;
                    if (outer > inner * 1.05)
                        plan.Add(Belt(star, inner, outer, normal, seed, variety, kelvin));
                }
            }

            // A debris disk (Kuiper belt) beyond the outermost planet.
            if (rules.Get(OuterDisks) && planets.Count >= 1 && !taken.Contains(star.Name + "|" + SpaceObjectKind.DebrisDisk)
                && Hash(seed, star.Name, "disk") < rules.Get(OuterDiskChance))
            {
                double last = planets[planets.Count - 1].SemiMajorAxis / star.Radius;
                plan.Add(OuterDisk(star, last * 1.4, last * 2.6, normal, seed, variety, kelvin));
            }
        }

        // Comets: small bodies on eccentric orbits straight round a star.
        if (rules.Get(Comets))
        {
            foreach (SystemBody b in bodies)
            {
                if (!IsComet(b, rules) || taken.Contains(b.Name + "|" + SpaceObjectKind.Comet)) continue;
                if (!byName.TryGetValue(b.Parent, out SystemBody parent) || !parent.IsStar) continue;
                plan.Add(Comet(b, seed, variety));
            }
        }
        return plan;
    }

    private static bool IsComet(SystemBody b, SettingsValues rules) =>
        !b.IsStar && b.Radius > 0.0 && b.Radius <= rules.Get(CometMaxRadius) && b.Eccentricity >= rules.Get(CometMinEccentricity) && b.SemiMajorAxis > 0.0;

    /// <summary>The planets' mean orbit normal (their common plane), or zero when none is known.</summary>
    private static Double3 MeanNormal(List<SystemBody> planets)
    {
        Double3 sum = Double3.Zero;
        foreach (SystemBody p in planets)
            if (p.OrbitNormal.LengthSquared > 0.0) sum = sum + p.OrbitNormal.Normalized();
        return sum.LengthSquared > 1e-12 ? sum.Normalized() : Double3.Zero;
    }

    private static CfgNode Belt(SystemBody star, double inner, double outer, Double3 normal, int seed, double variety, double kelvin)
    {
        double V(string salt) => 2.0 * Hash(seed, star.Name, "belt " + salt) - 1.0;   // -1..1
        CfgNode node = Node(star.Name, SpaceObjectKind.AsteroidBelt, normal)
            .Add("diskInner", Num(inner)).Add("diskOuter", Num(outer))
            .Add("diskThickness", Num(0.08 * (1.0 + 0.6 * variety * V("thick"))))
            .Add("rocks", Num(0.2 * (1.0 + 0.5 * variety * V("rocks"))))
            .Add("dust", Num(0.01 * (1.0 + 0.8 * variety * V("dust"))))
            .Add("starTemperature", Num(kelvin));
        Double3 dust = Tint(new Double3(1.0, 0.62, 0.38), variety, seed, star.Name, "belt colour");
        return node.Add("dustColor", ColorSetting.Write(dust)).Add("outerDustColor", ColorSetting.Write(dust))
            .Add("rockColor", ColorSetting.Write(Tint(new Double3(1.0, 0.97, 0.87), variety * 0.5, seed, star.Name, "rock colour")));
    }

    private static CfgNode OuterDisk(SystemBody star, double inner, double outer, Double3 normal, int seed, double variety, double kelvin)
    {
        double V(string salt) => 2.0 * Hash(seed, star.Name, "disk " + salt) - 1.0;
        int gaps = (int)Math.Floor(4.0 * Hash(seed, star.Name, "disk gaps") * variety);
        CfgNode node = Node(star.Name, SpaceObjectKind.DebrisDisk, normal)
            .Add("diskInner", Num(inner)).Add("diskOuter", Num(outer))
            .Add("diskThickness", Num(0.06 * (1.0 + 0.5 * variety * V("thick"))))
            .Add("dust", Num(0.05 * (1.0 + 0.6 * variety * V("dust"))))
            .Add("rocks", Num(0.15 * (1.0 + 0.5 * variety * V("rocks"))))
            .Add("rockSpacing", "0.005").Add("rings", gaps.ToString(CultureInfo.InvariantCulture))
            .Add("lightFalloff", "1.5")    // display aid: lit enough to be seen so far out
            .Add("starTemperature", Num(kelvin));
        Double3 dust = Tint(new Double3(0.9, 0.62, 0.48), variety, seed, star.Name, "disk colour");
        return node.Add("dustColor", ColorSetting.Write(dust))
            .Add("outerDustColor", ColorSetting.Write(Tint(new Double3(0.65, 0.55, 0.62), variety, seed, star.Name, "disk outer colour")));
    }

    private static CfgNode Comet(SystemBody b, int seed, double variety)
    {
        double V(string salt) => 2.0 * Hash(seed, b.Name, "comet " + salt) - 1.0;
        // Tails a few percent of its orbit long: the unit is 1/1500 of its semi-major axis (AC-1: ~20,000 km).
        CfgNode node = Node(b.Name, SpaceObjectKind.Comet, Double3.Zero)
            .Add("horizonRadius", Num(b.SemiMajorAxis / 1500.0))
            .Add("activity", Num(1.0 + 0.4 * variety * V("activity")))
            .Add("dustTailCurve", Num(0.5 + 0.4 * variety * V("curve")))
            .Add("dustTailWidth", Num(1.0 + 0.5 * variety * V("width")));
        return node.Add("colorA", ColorSetting.Write(Tint(new Double3(0.45, 0.65, 1.0), variety * 0.5, seed, b.Name, "ion")))
            .Add("comaColor", ColorSetting.Write(Tint(new Double3(0.6, 1.0, 0.75), variety * 0.5, seed, b.Name, "coma")));
    }

    private static CfgNode Node(string body, SpaceObjectKind kind, Double3 normal)
    {
        CfgNode node = new CfgNode(BlackHoleSettings.Schema.NodeName).Add("body", body).Add("kind", kind.ToString());
        if (normal.LengthSquared > 0.0)
            node.Add("normalX", Num(normal.X)).Add("normalY", Num(normal.Y)).Add("normalZ", Num(normal.Z));
        return node;
    }

    /// <summary>A colour varied by up to 25% per channel (times <paramref name="variety"/>), never negative.</summary>
    private static Double3 Tint(Double3 c, double variety, int seed, string name, string salt) => new Double3(
        Math.Max(c.X * (1.0 + 0.25 * variety * (2.0 * Hash(seed, name, salt + " r") - 1.0)), 0.0),
        Math.Max(c.Y * (1.0 + 0.25 * variety * (2.0 * Hash(seed, name, salt + " g") - 1.0)), 0.0),
        Math.Max(c.Z * (1.0 + 0.25 * variety * (2.0 * Hash(seed, name, salt + " b") - 1.0)), 0.0));

    private static string Num(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
}
