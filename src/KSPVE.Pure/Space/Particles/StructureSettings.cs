using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Particles;

/// <summary>
/// The structures drawn as clouds of separate particles (clumps, puffs, grains) whose places come from orbital mechanics at the
/// game's time. Each is a KSPVE_STRUCTURE node on a body.
/// </summary>
public enum StructureKind
{
    /// <summary>A planet stripped by its star: gas and dust tails, Roche-overflow streams, clumps (chthonian planets, hot Jupiters, KIC 12557548b).</summary>
    EvaporatingPlanet = 0,
    /// <summary>The cloud of molten and vaporised blobs thrown out by a giant impact, cooling and falling back.</summary>
    ImpactDebris = 1,
    /// <summary>A giant impact's remnant: a hot corotating vapour disc and body (Lock and Stewart 2017), optionally with a moon condensing.</summary>
    Synestia = 2,
    /// <summary>A planet of density below 0.1 g/cm^3 with a deep, extended hazy envelope (Kepler-51).</summary>
    SuperPuff = 3,
    /// <summary>A moon inside the Roche limit coming apart into a ring.</summary>
    RingingMoon = 4,
    /// <summary>A rubble-pile asteroid spun up past the point where it holds together: it sheds boulders from the equator or splits as a contact binary.</summary>
    RubblePile = 5,
    /// <summary>SPECULATIVE: a planet shaped like a torus (Liu 2012's analysis finds it only short-lived). Drawn only with the "speculative" setting on.</summary>
    ToroidalPlanet = 6
}

/// <summary>One setting of a structure: its key, label, default, range and which kinds use it.</summary>
public sealed class StructureParam
{
    public readonly string Key, Label, Hint, Section;
    public readonly double Default, Min, Max;
    public readonly int Kinds;
    /// <summary>The settings window draws it on a logarithmic slider.</summary>
    public readonly bool Log;
    /// <summary>The settings window shows it as a yes/no.</summary>
    public readonly bool Toggle;

    public StructureParam(string section, string key, string label, double def, double min, double max, int kinds, string hint, bool log = false, bool toggle = false)
    {
        Section = section; Key = key; Label = label; Default = def; Min = min; Max = max; Kinds = kinds; Hint = hint; Log = log; Toggle = toggle;
    }

    public bool UsedBy(StructureKind kind) => (Kinds & (1 << (int)kind)) != 0;
}

/// <summary>A colour setting of a structure.</summary>
public sealed class StructureColor
{
    public readonly string Key, Label, Hint;
    public readonly Double3 Default;
    public readonly int Kinds;

    public StructureColor(string key, string label, Double3 def, int kinds, string hint)
    {
        Key = key; Label = label; Default = def; Kinds = kinds; Hint = hint;
    }

    public bool UsedBy(StructureKind kind) => (Kinds & (1 << (int)kind)) != 0;
}

/// <summary>The values of one structure's settings.</summary>
public sealed class StructureConfig
{
    public string Body = string.Empty;
    public StructureKind Kind;
    public bool Enabled = true;
    public int Slot;
    public string LightSource = string.Empty;
    public readonly Dictionary<string, double> Numbers = new Dictionary<string, double>();
    public readonly Dictionary<string, Double3> Colors = new Dictionary<string, Double3>();

    /// <summary>A number setting's value (its default when unset).</summary>
    public double N(string key) => Numbers.TryGetValue(key, out double v) ? v : StructureSettings.Param(key).Default;

    public Double3 C(string key) => Colors.TryGetValue(key, out Double3 v) ? v : StructureSettings.Color(key).Default;

    public void Set(string key, double value) => Numbers[key] = value;

    public void Set(string key, Double3 value) => Colors[key] = value;

    public StructureConfig Clone()
    {
        var c = new StructureConfig { Body = Body, Kind = Kind, Enabled = Enabled, Slot = Slot, LightSource = LightSource };
        foreach (var kv in Numbers) c.Numbers[kv.Key] = kv.Value;
        foreach (var kv in Colors) c.Colors[kv.Key] = kv.Value;
        return c;
    }

    /// <summary>Keeps every number inside its setting's range.</summary>
    public void Clamp()
    {
        foreach (StructureParam p in StructureSettings.Params)
            if (Numbers.TryGetValue(p.Key, out double v)) Numbers[p.Key] = Math.Min(Math.Max(v, p.Min), p.Max);
    }
}

/// <summary>The KSPVE_STRUCTURE node: its settings, presets and parsing.</summary>
public static class StructureSettings
{
    public const string NodeName = "KSPVE_STRUCTURE";
    /// <summary>Node in PluginData/user.cfg holding one KSPVE_STRUCTURE child per structure the player has made or tweaked.</summary>
    public const string UserNodeName = "KSPVE_STRUCTURES";

    private const int Ev = 1 << (int)StructureKind.EvaporatingPlanet, Im = 1 << (int)StructureKind.ImpactDebris, Sy = 1 << (int)StructureKind.Synestia,
        Sp = 1 << (int)StructureKind.SuperPuff, Rm = 1 << (int)StructureKind.RingingMoon, Rp = 1 << (int)StructureKind.RubblePile,
        To = 1 << (int)StructureKind.ToroidalPlanet, All = Ev | Im | Sy | Sp | Rm | Rp | To;

    public static readonly string[] KindNames =
    {
        "Evaporating planet", "Impact debris cloud", "Synestia", "Super-puff planet", "Moon torn into a ring", "Rubble pile breaking apart", "Toroidal planet (speculative)"
    };

    public static string KindName(StructureKind kind) => KindNames[(int)kind];

    public static readonly StructureParam[] Params =
    {
        // Everywhere.
        new StructureParam("Look", "count", "Particles", 8000, 100, 60000, All, "How many separate particles make the structure (the drawn clumps, puffs and grains).", log: true),
        new StructureParam("Look", "seed", "Random seed", 1, 0, 100000, All, "Varies the particles' places and sizes; the same seed always gives the same cloud."),
        new StructureParam("Look", "brightness", "Brightness", 1, 0, 1000, All, "Overall brightness.", log: true),
        new StructureParam("Look", "visibilityBoost", "Visibility boost (display)", 1, 0.01, 1e6, All, "Display aid, not physics: multiplies how bright every particle is so it can be seen from far away; 1 is the real brightness.", log: true),
        new StructureParam("Look", "minPixels", "Smallest particle (pixels)", 1, 0, 8, All, "Display: no particle is drawn smaller than this; a smaller one is dimmed in proportion, so the light is conserved."),
        new StructureParam("Look", "sizeScale", "Particle size", 1, 0.01, 1000, All, "Scales every particle's radius.", log: true),
        new StructureParam("Look", "albedo", "Albedo of the grains", 0.3, 0, 1, All, "Share of the starlight the particles reflect."),
        new StructureParam("Look", "scatterAsymmetry", "Forward scattering", 0.3, -0.9, 0.95, All, "Henyey-Greenstein g of the grains: 0 scatters evenly, above 0 brightens them looking towards the star (dust near the star glows forward)."),
        new StructureParam("Look", "selfShadow", "Self-shadowing", 0.8, 0, 1, All, "How much particles on the star's side shade the ones behind them (the dark side of a cloud)."),
        new StructureParam("Look", "starTemperature", "Star temperature (K)", 5800, 1000, 50000, All, "Colour and heat of the star lighting it.", log: true),
        new StructureParam("Look", "starLuminosity", "Star luminosity (W; 0 = from the star's size and temperature)", 0, 0, 1e32, All, "The star's total power; 0 works it out from the star's radius and the temperature above."),
        new StructureParam("Look", "emissionCap", "Glow cap (display)", 10000, 1, 1e8, All, "Display: the brightest a glowing particle may be, relative to a sunlit white surface; hot material really is thousands of times brighter, which blooms to white.", log: true),
        new StructureParam("Look", "glowGain", "Glow of hot material (display)", 1, 1e-8, 1e6, Im | Sy, "Display aid, not physics: multiplies how bright incandescent material is. 1 is real: 2600 K rock is 1700 times brighter than sunlit white rock and drowns the picture.", log: true),
        new StructureParam("Look", "ageRate", "Evolution speed (display)", 1, 0, 1e9, All, "How fast stripping, cooling, break-up and condensing go compared with the game's clock; 1 is real. 0 freezes it at its starting age.", log: true),
        new StructureParam("Look", "startAgeHours", "Age at game time 0 (hours)", 0, 0, 1e9, All, "How long since the event (the impact, the break-up, the start of the stripping) at the start of the game."),
        new StructureParam("Look", "normalX", "Axis x", 0, -1, 1, Im | Sy | Sp | Rm | Rp | To, "The axis it turns about (inertial frame); all 0 = the body's pole."),
        new StructureParam("Look", "normalY", "Axis y", 1, -1, 1, Im | Sy | Sp | Rm | Rp | To, ""),
        new StructureParam("Look", "normalZ", "Axis z", 0, -1, 1, Im | Sy | Sp | Rm | Rp | To, ""),

        // Evaporating planet.
        new StructureParam("Atmosphere and loss", "atmosphereLeft", "Atmosphere left (1 = gas giant, 0 = bare core)", 1, 0, 1, Ev, "How much of the gas envelope remains. It sets the planet's mass and size, and with 0 the bare chthonian core loses dust only."),
        new StructureParam("Atmosphere and loss", "coreMassFraction", "Core's share of the planet's mass", 0.1, 0.005, 1, Ev, "The rocky core's mass over the full planet's: what remains when the envelope is gone."),
        new StructureParam("Atmosphere and loss", "coreDensity", "Core density (kg/m^3)", 5500, 500, 20000, Ev, "Sets the bare core's radius.", log: true),
        new StructureParam("Atmosphere and loss", "evolve", "Let the atmosphere evolve with game time", 0, 0, 1, Ev, "Strips the envelope over the game's time at the real rate times the evolution speed; off keeps it at 'atmosphere left'.", toggle: true),
        new StructureParam("Atmosphere and loss", "starAgeGyr", "Star's age (Gyr)", 4, 0.01, 14, Ev, "Sets the X-ray and ultraviolet share of the star's light (a young star is much more active)."),
        new StructureParam("Atmosphere and loss", "xuvFraction", "XUV share of the star's light (0 = from its age)", 0, 0, 0.01, Ev, "Fraction of the luminosity emitted as X-rays and extreme ultraviolet."),
        new StructureParam("Atmosphere and loss", "escapeEfficiency", "Escape efficiency", 0.15, 0.01, 1, Ev, "Share of the absorbed XUV energy that lifts gas away (heating efficiency; 0.1 to 0.3 is typical)."),
        new StructureParam("Atmosphere and loss", "tailDensity", "Tail density (display)", 1, 0.001, 1e9, Ev | Im | Sy, "Display aid, not physics: multiplies the mass in the tails so thin real tails can be seen; 1 is the real loss.", log: true),
        new StructureParam("Atmosphere and loss", "envelopeMu", "Envelope's mean molecular weight (amu)", 2.3, 1, 50, Ev | Sp, "2.3 for hydrogen and helium."),
        new StructureParam("Atmosphere and loss", "bondAlbedo", "Bond albedo", 0.3, 0, 0.95, Ev, "Share of the star's light the planet reflects: cools the dayside."),
        new StructureParam("Tails", "ejectionSpeed", "Ejection speed (m/s)", 8000, 100, 1e6, Ev, "How fast the gas and clumps leave the planet or its Lagrange points.", log: true),
        new StructureParam("Tails", "speedSpread", "Speed spread", 0.5, 0, 3, Ev | Im, "How much the ejection speeds vary (log-normal width for debris): a tail widens downstream in proportion."),
        new StructureParam("Tails", "spreadAngle", "Spread of directions (deg)", 25, 0, 180, Ev, "Half-angle of the cone the particles leave in."),
        new StructureParam("Tails", "streamHours", "Streams' length (hours of flight)", 120, 0.1, 1e6, Ev, "How long the clumps from L1, L2 and the surface fly before they fade: the length of the stream, which follows the orbit.", log: true),
        new StructureParam("Tails", "gasTailHours", "Gas tail length (hours of flight)", 150, 0.1, 1e6, Ev, "How long the gas flies: the length of the straight, ion-like tail.", log: true),
        new StructureParam("Tails", "dustTailHours", "Dust tail length (hours of flight)", 100, 0.1, 1e6, Ev, "How long the dust flies: the length of the curved tail.", log: true),
        new StructureParam("Tails", "clumpShare", "Clumps (share of the particles)", 0.5, 0, 1, Ev, "Discrete bright clumps launched from L1, L2 and the surface."),
        new StructureParam("Tails", "gasShare", "Gas (share of the particles)", 0.3, 0, 1, Ev, "Soft glowing gas puffs; the rest, after the clumps and gas, is dust."),
        new StructureParam("Tails", "haloShare", "Bound atmosphere (share of the particles)", 0.08, 0, 1, Ev, "Gas still held round the planet inside its Roche lobe, drawn out towards L1 and L2."),
        new StructureParam("Tails", "l1Share", "Clumps from L1 (towards the star)", 0.45, 0, 1, Ev, "Share of the clumps leaving through L1: they run ahead of the planet along its orbit."),
        new StructureParam("Tails", "l2Share", "Clumps from L2 (away from the star)", 0.2, 0, 1, Ev, "Share leaving through L2: they trail behind. The rest leave the surface."),
        new StructureParam("Tails", "clumpRadius", "Clump radius (planet radii)", 0.01, 1e-4, 10, Ev, "Radius of a clump in planet radii (times particle size).", log: true),
        new StructureParam("Tails", "clumpBeta", "Clumps' radiation pressure (beta)", 0.02, 0, 10, Ev, "Radiation force over gravity on a clump; above 1 the star repels it."),
        new StructureParam("Tails", "gasBeta", "Gas's radiation pressure (beta)", 2, 0, 200, Ev, "Force on the gas (Lyman-alpha pressure on hydrogen, stellar wind) over gravity: it pushes the tail straight away from the star. Comet ion tails see 10 to 1000."),
        new StructureParam("Tails", "grainSizeUm", "Dust grain radius (micrometres)", 0.3, 0.01, 1000, Ev, "Typical grain radius; beta = 3 L Q / (16 pi G M c rho s), so smaller grains are pushed harder and curve more.", log: true),
        new StructureParam("Tails", "grainDensity", "Grain density (kg/m^3)", 3000, 500, 8000, Ev, "Silicate grains: 3000."),
        new StructureParam("Tails", "gasPuffRadius", "Gas puff radius (planet radii)", 0.35, 0.001, 100, Ev, "Size of one gas puff.", log: true),
        new StructureParam("Tails", "dustPuffRadius", "Dust puff radius (planet radii)", 0.25, 0.001, 100, Ev, "Size of one dust puff.", log: true),
        new StructureParam("Tails", "gasEmission", "Gas glow", 1, 0, 1e6, Ev, "How brightly the gas shines in its lines (hydrogen-alpha, sodium D, helium) lit by the star's emission, scaled by the mass lost.", log: true),

        // Impact debris.
        new StructureParam("Impact", "impactMass", "Debris mass (share of the body's mass)", 0.02, 1e-5, 2, Im | Sy, "Mass thrown out by the impact, over the body's mass; it sets the blobs' size and how hot they stay."),
        new StructureParam("Impact", "cloudRadius", "Starting cloud radius (body radii)", 0.6, 0.01, 50, Im, "Radius of the cloud at the moment of the impact."),
        new StructureParam("Impact", "ejectaSpeed", "Ejecta speed (escape speed units)", 0.7, 0.05, 5, Im, "Typical blob speed over the body's escape speed: below 1 most fall back or orbit, above 1 they escape.", log: true),
        new StructureParam("Impact", "impactSpin", "Spin of the debris (share of circular speed)", 0.35, 0, 1.5, Im, "Sideways speed from the impact's angular momentum: the cloud swirls and orbits instead of falling straight back."),
        new StructureParam("Impact", "blobRadius", "Blob radius (body radii)", 0.03, 1e-4, 5, Im | Rm | Rp, "Radius of one blob.", log: true),
        new StructureParam("Impact", "blobSizeSpread", "Blob size spread", 0.6, 0, 3, Im | Rm | Rp, "How much blob sizes vary (log-normal)."),
        new StructureParam("Impact", "initialTemperature", "Starting temperature (K)", 3500, 300, 20000, Im | Sy, "Temperature of the molten and vaporised rock at the impact. It cools radiatively: T^-3 = T0^-3 + 3 a t, a = 3 sigma eps / (rho c s).", log: true),
        new StructureParam("Impact", "dropletRadius", "Droplet radius (m)", 1.0, 0.001, 1000, Im | Sy, "A blob is a swarm of molten droplets of about this radius, which cool by radiating (T^-3 = T0^-3 + 3 a t, a = 3 sigma eps / (rho c s)): small ones cool in minutes, metre-sized ones in hours.", log: true),
        new StructureParam("Impact", "dropletSpread", "Droplet size spread", 1.0, 0, 4, Im | Sy, "How much the droplet sizes (and so the cooling) vary from blob to blob (log-normal): some blobs stay hot while others go dark.")
,
        new StructureParam("Impact", "blobDensity", "Blob density (kg/m^3)", 2800, 200, 12000, Im, "Rock: 2800."),
        new StructureParam("Impact", "heatCapacity", "Heat capacity (J/kg/K)", 1200, 200, 5000, Im | Sy, "Silicate melt: about 1200."),
        new StructureParam("Impact", "emissivity", "Emissivity", 0.9, 0.05, 1, Im | Sy, "How well the blobs radiate."),
        new StructureParam("Impact", "reaccrete", "Blobs that hit the body are absorbed", 1, 0, 1, Im | Rm | Rp, "Re-accretion: a blob whose orbit meets the surface falls in and vanishes.", toggle: true),

        // Synestia.
        new StructureParam("Synestia", "extent", "Outer edge (body radii)", 6, 1.5, 100, Sy, "Radius of the vapour structure."),
        new StructureParam("Synestia", "corotation", "Corotation limit (body radii)", 2.5, 1.05, 50, Sy, "Inside this the vapour turns rigidly with the body (the spin is the Kepler rate there, a few hours: Lock and Stewart 2017); outside it orbits at Kepler speeds."),
        new StructureParam("Synestia", "coolingYears", "Cooling time (years)", 50, 0.01, 1e5, Sy, "Time for the vapour to fall to 0.8 of its temperature; synestias cool over decades to centuries.", log: true),
        new StructureParam("Synestia", "moon", "A moon condenses", 0, 0, 1, Sy, "A moon forms from the vapour beyond the corotation limit and gathers over the condensing time.", toggle: true),
        new StructureParam("Synestia", "moonDistance", "Moon's distance (body radii; 0 = at the corotation limit)", 0, 0, 100, Sy, "Where the moon condenses."),
        new StructureParam("Synestia", "moonYears", "Moon's condensing time (years)", 20, 0.01, 1e4, Sy, "How long the moon takes to gather.", log: true),
        new StructureParam("Synestia", "moonRadius", "Moon radius (body radii)", 0.27, 0.01, 2, Sy, "Radius of the moon when done (0.27 is the Moon's over the Earth's)."),
        new StructureParam("Synestia", "flareBoost", "Flare of the vapour (display)", 1, 0.1, 5, Sy, "Display aid: scales the structure's thickness; 1 is the real scale height c_s / v_K."),

        // Super-puff.
        new StructureParam("Envelope", "puffRadius", "Visible radius (body radii)", 6, 1.2, 60, Sp, "Radius of the haze's top (the transit radius), over the core's. Kepler-51 d: 9 Earth radii round a core of 2 to 3."),
        new StructureParam("Envelope", "atmTemperature", "Envelope temperature (K)", 3500, 50, 20000, Sp, "Temperature of the deep envelope, which sets how far it extends (isothermal and hydrostatic: rho ~ exp(-lambda (1 - R/r)), lambda = G M m / k T R). A super-puff has a hot, high-entropy interior.", log: true),
        new StructureParam("Envelope", "hazeOpacity", "Haze", 1, 0, 100, Sp, "How much of the starlight the haze scatters.", log: true),
        new StructureParam("Envelope", "puffRadiusSpread", "Puff size (envelope scale heights)", 1.5, 0.05, 20, Sp, "Radius of one haze puff in the local scale height."),

        // Ringing moon.
        new StructureParam("Moon", "moonDistanceRoche", "Moon's orbit (Roche limits)", 0.75, 0.3, 3, Rm, "The moon's distance over the fluid Roche limit 2.456 R (rho_planet / rho_moon)^(1/3): below 1 it comes apart."),
        new StructureParam("Moon", "moonSizeKm", "Moon radius (km)", 120, 1, 5000, Rm, "Radius of the moon.", log: true),
        new StructureParam("Moon", "moonDensity", "Moon density (kg/m^3)", 1200, 300, 8000, Rm, "Icy moon: 1200; rocky: 3000."),
        new StructureParam("Moon", "disruptStartHours", "Break-up begins (hours after the start)", 12, 0, 1e6, Rm, "When the outer layers start to be pulled off."),
        new StructureParam("Moon", "disruptSpanHours", "Break-up takes (hours)", 400, 0.1, 1e7, Rm, "How long all of the moon takes to come apart; the outer layers go first.", log: true),
        new StructureParam("Moon", "grainRadius", "Particle radius (moon radii)", 0.05, 0.001, 2, Rm, "Radius of one piece of the moon.", log: true),
        new StructureParam("Moon", "moonEccentricity", "Moon's orbit eccentricity", 0.05, 0, 0.9, Rm, ""),

        // Rubble pile.
        new StructureParam("Rubble pile", "spinFraction", "Spin (share of the break-up spin)", 0.92, 0.1, 1.5, Rp, "Rotation rate over the rate at which the equator orbits: at 1, loose rocks on the equator float away."),
        new StructureParam("Rubble pile", "spinUpYears", "Spin-up time (years to double the spin rate)", 1e5, 1e-4, 1e9, Rp, "YORP: sunlight's torque speeds the spin; real timescales are millions of years (see evolution speed).", log: true),
        new StructureParam("Rubble pile", "contactBinary", "Two lobes (contact binary)", 0, 0, 1, Rp, "Two lumps in contact instead of one.", toggle: true),
        new StructureParam("Rubble pile", "lobeRatio", "Smaller lobe (share of the larger's radius)", 0.7, 0.1, 1, Rp, ""),
        new StructureParam("Rubble pile", "splitHours", "Lobes part (hours after the start)", 48, 0, 1e7, Rp, "When the contact binary splits.", log: false),
        new StructureParam("Rubble pile", "splitSpeed", "Parting speed (share of circular)", 0.7, 0.05, 1.5, Rp, "Relative speed at the split over the circular speed there: below 1.41 they stay bound as a binary."),

        // Toroidal planet.
        new StructureParam("Torus", "speculative", "Allow speculative structures", 0, 0, 1, To, "A toroidal planet is not known to exist and is unstable; it is drawn only with this on.", toggle: true),
        new StructureParam("Torus", "majorRadius", "Ring radius (body radii)", 1.2, 0.2, 20, To, "Distance from the centre to the middle of the tube."),
        new StructureParam("Torus", "tubeRadius", "Tube radius (body radii)", 0.45, 0.05, 10, To, ""),
        new StructureParam("Torus", "torusPeriodHours", "Turning period (hours)", 3, 0.1, 1000, To, "The torus must spin to hold itself up (Liu 2012)."),
    };

    public static readonly StructureColor[] Colors =
    {
        new StructureColor("clumpColor", "Clump colour", new Double3(1.0, 1.0, 1.0), Ev | Im | Sy | Rm | Rp | To, "Tint of the reflecting grains (clumps, blobs, boulders)."),
        new StructureColor("gasColor", "Gas glow colour", new Double3(1.0, 0.32, 0.28), Ev, "Hydrogen-alpha 656 nm is red-pink; sodium D 589 nm yellow; helium 1083 nm is invisible."),
        new StructureColor("dustColor", "Dust colour", new Double3(1.0, 0.78, 0.6), Ev, "Silicate dust reddens the light it scatters."),
        new StructureColor("hazeColor", "Haze colour", new Double3(0.86, 0.82, 0.72), Sp, "Colour of the haze."),
        new StructureColor("coreColor", "Surface colour", new Double3(0.55, 0.5, 0.46), Rp | To | Rm, "Colour of the rocky surface."),
    };

    private static readonly Dictionary<string, StructureParam> ParamByKey = BuildParams();
    private static readonly Dictionary<string, StructureColor> ColorByKey = BuildColors();

    private static Dictionary<string, StructureParam> BuildParams()
    {
        var d = new Dictionary<string, StructureParam>();
        foreach (StructureParam p in Params)
        {
            if (d.ContainsKey(p.Key)) throw new InvalidOperationException("Duplicate structure setting " + p.Key);
            d[p.Key] = p;
        }
        return d;
    }

    private static Dictionary<string, StructureColor> BuildColors()
    {
        var d = new Dictionary<string, StructureColor>();
        foreach (StructureColor c in Colors) d[c.Key] = c;
        return d;
    }

    public static StructureParam Param(string key) => ParamByKey.TryGetValue(key, out StructureParam p) ? p : throw new KeyNotFoundException("No structure setting " + key);

    public static StructureColor Color(string key) => ColorByKey.TryGetValue(key, out StructureColor c) ? c : throw new KeyNotFoundException("No structure colour " + key);

    public static bool HasParam(string key) => ParamByKey.ContainsKey(key);

    /// <summary>A node's kind (EvaporatingPlanet when it has none).</summary>
    public static StructureKind KindOf(CfgNode node) =>
        Enum.TryParse(node?.GetLast("kind")?.Trim(), true, out StructureKind kind) && Enum.IsDefined(typeof(StructureKind), kind) ? kind : StructureKind.EvaporatingPlanet;

    /// <summary>The settings from a node; anything it does not say is the kind's default, then the preset it names, then the setting's own default.</summary>
    public static StructureConfig Parse(CfgNode node, List<ConfigIssue> issues = null)
    {
        var config = new StructureConfig { Kind = KindOf(node) };
        if (node == null) return config;
        config.Body = node.GetLast("body")?.Trim() ?? string.Empty;
        config.LightSource = node.GetLast("lightSource")?.Trim() ?? string.Empty;
        if (bool.TryParse(node.GetLast("enabled")?.Trim(), out bool enabled)) config.Enabled = enabled;
        if (int.TryParse(node.GetLast("slot")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot >= 0) config.Slot = slot;
        string preset = node.GetLast("preset")?.Trim();
        if (!string.IsNullOrEmpty(preset))
        {
            CfgNode presetNode = PresetNode(preset);
            if (presetNode != null) Read(presetNode, config, issues);
            else issues?.Add(new ConfigIssue(IssueSeverity.Warning, NodeName + "/preset", $"No preset called '{preset}'; using the defaults"));
        }
        Read(node, config, issues);
        config.Clamp();
        return config;
    }

    private static void Read(CfgNode node, StructureConfig config, List<ConfigIssue> issues)
    {
        foreach (var kv in node.Values)
        {
            string key = kv.Key.Trim();
            if (ParamByKey.TryGetValue(key, out StructureParam p))
            {
                string raw = kv.Value.Trim();
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v)) config.Numbers[key] = v;
                else if (bool.TryParse(raw, out bool b)) config.Numbers[key] = b ? 1.0 : 0.0;
                else issues?.Add(new ConfigIssue(IssueSeverity.Warning, NodeName + "/" + key, $"'{kv.Value}' is not a number"));
            }
            else if (ColorByKey.ContainsKey(key))
            {
                string[] parts = kv.Value.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var c = new double[3];
                bool ok = parts.Length == 3;
                for (int i = 0; ok && i < 3; i++) ok = double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out c[i]) && c[i] >= 0.0;
                if (ok) config.Colors[key] = new Double3(c[0], c[1], c[2]);
                else issues?.Add(new ConfigIssue(IssueSeverity.Warning, NodeName + "/" + key, $"'{kv.Value}' is not three numbers r, g, b"));
            }
        }
    }

    /// <summary>The whole structure as a node (every setting that differs from its default, plus its body, kind and slot).</summary>
    public static CfgNode ToNode(StructureConfig config)
    {
        var node = new CfgNode(NodeName)
            .Add("body", config.Body).Add("slot", config.Slot.ToString(CultureInfo.InvariantCulture))
            .Add("kind", config.Kind.ToString()).Add("enabled", config.Enabled ? "True" : "False");
        if (!string.IsNullOrEmpty(config.LightSource)) node.Add("lightSource", config.LightSource);
        foreach (StructureParam p in Params)
        {
            if (!config.Numbers.TryGetValue(p.Key, out double v) || !p.UsedBy(config.Kind)) continue;
            node.Add(p.Key, v.ToString("R", CultureInfo.InvariantCulture));
        }
        foreach (StructureColor c in Colors)
            if (config.Colors.TryGetValue(c.Key, out Double3 v) && c.UsedBy(config.Kind)) node.Add(c.Key, ColorSetting.Write(v));
        return node;
    }

    /// <summary>A named look: the settings a button in the window or a node's <c>preset</c> sets.</summary>
    public sealed class Preset
    {
        public readonly string Name;
        public readonly StructureKind Kind;
        public readonly string Description;
        public readonly CfgNode Node;

        public Preset(string name, StructureKind kind, string description, CfgNode node)
        {
            Name = name; Kind = kind; Description = description; Node = node;
        }
    }

    public static readonly Preset[] Presets = BuildPresets();

    private static CfgNode P(StructureKind kind, params string[] pairs)
    {
        var node = new CfgNode(NodeName).Add("kind", kind.ToString());
        for (int i = 0; i + 1 < pairs.Length; i += 2) node.Add(pairs[i], pairs[i + 1]);
        return node;
    }

    private static Preset[] BuildPresets()
    {
        var evap = StructureKind.EvaporatingPlanet;
        return new[]
        {
            new Preset("Hot Jupiter evaporating", evap,
                "A gas giant shedding its upper atmosphere (HD 209458 b, WASP-69 b): a long straight gas tail, a bound halo, a faint stream through L1 and L2.",
                P(evap, "atmosphereLeft", "1", "clumpShare", "0.06", "gasShare", "0.82", "haloShare", "0.1", "gasBeta", "4", "ejectionSpeed", "12000",
                    "gasTailHours", "180", "streamHours", "90", "tailDensity", "3000", "gasEmission", "3", "clumpRadius", "0.02", "count", "9000")),
            new Preset("Disintegrating rocky planet (KIC 12557548 b)", evap,
                "A small rocky planet sublimating on its star-facing side: a comet-like tail of dust curved by radiation pressure (Rappaport et al. 2012; K2-22 b is the same).",
                P(evap, "atmosphereLeft", "0", "coreMassFraction", "1", "coreDensity", "4000", "gasShare", "0", "clumpShare", "0.0", "haloShare", "0",
                    "grainSizeUm", "0.4", "dustTailHours", "12", "ejectionSpeed", "3000", "spreadAngle", "50", "bondAlbedo", "0.9", "tailDensity", "4000", "count", "9000")),
            new Preset("Chthonian core", evap,
                "The bare core of a gas giant whose envelope the star stripped: a hot rocky world with a thin dust and vapour tail.",
                P(evap, "atmosphereLeft", "0", "coreMassFraction", "0.3", "coreDensity", "6000", "gasShare", "0.1", "clumpShare", "0.15", "haloShare", "0",
                    "dustTailHours", "90", "streamHours", "60", "ejectionSpeed", "5000", "tailDensity", "2000", "count", "9000")),
            new Preset("Like the reference", evap,
                "A body shedding a long stream of separate bright white clumps: narrow and dense at the body, a few trailing the other way, widening downstream into a broad cloud of thousands of points. The particle size and smallest-pixel display settings keep the clumps visible from a distance.",
                P(evap, "atmosphereLeft", "0.6", "clumpShare", "0.96", "gasShare", "0.02", "haloShare", "0.02", "l1Share", "0.06", "l2Share", "0.3", "clumpRadius", "0.05",
                    "clumpBeta", "0.0", "ejectionSpeed", "30000", "speedSpread", "0.8", "spreadAngle", "14", "streamHours", "10", "gasTailHours", "10", "dustTailHours", "10",
                    "count", "14000", "visibilityBoost", "1", "minPixels", "1.6", "albedo", "1", "scatterAsymmetry", "0", "selfShadow", "0", "sizeScale", "2", "emissionCap", "6")
                    .Add("clumpColor", "1, 1, 1")),
            new Preset("Impact debris ball (like the reference)", StructureKind.ImpactDebris,
                "A lumpy cotton-candy cloud of thousands of soft overlapping blobs: pale lavender-blue on the lit side, shadowed on the other where orange and pink hot specks glow through, ragged edges with blobs flying off.",
                P(StructureKind.ImpactDebris, "count", "16000", "cloudRadius", "0.9", "ejectaSpeed", "1.0", "speedSpread", "0.5", "impactSpin", "0.3", "blobRadius", "0.06",
                    "blobSizeSpread", "0.7", "initialTemperature", "2600", "dropletRadius", "0.5", "dropletSpread", "1.5", "startAgeHours", "0.7", "brightness", "1.5", "ageRate", "1", "visibilityBoost", "1", "minPixels", "2", "selfShadow", "0.95",
                    "albedo", "0.55", "scatterAsymmetry", "0.1", "emissionCap", "2", "glowGain", "0.0004", "tailDensity", "1", "starTemperature", "9000")
                    .Add("clumpColor", "0.72, 0.74, 1.0")),
            new Preset("Giant-impact debris cloud", StructureKind.ImpactDebris, "The debris of a Moon-forming impact: expanding, cooling over days, falling back.",
                P(StructureKind.ImpactDebris, "count", "12000", "ejectaSpeed", "0.8", "initialTemperature", "3500", "impactMass", "0.1", "startAgeHours", "1", "glowGain", "0.0003", "emissionCap", "3", "blobRadius", "0.12")),
            new Preset("Synestia (Lock and Stewart)", StructureKind.Synestia, "A hot corotating vapour structure after a giant impact: glowing silicate vapour at 2000 to 4000 K, cooling over decades.",
                P(StructureKind.Synestia, "count", "20000", "initialTemperature", "3400", "extent", "6", "corotation", "2.5", "coolingYears", "50", "glowGain", "0.0003", "emissionCap", "3", "dropletRadius", "0.2")),
            new Preset("Synestia with a moon condensing", StructureKind.Synestia, "As above with a moon gathering in the vapour beyond the corotation limit.",
                P(StructureKind.Synestia, "count", "22000", "initialTemperature", "3000", "moon", "1", "moonYears", "20", "startAgeHours", "8760", "glowGain", "0.0003", "emissionCap", "3", "dropletRadius", "0.2")),
            new Preset("Super-puff (Kepler-51 d)", StructureKind.SuperPuff, "A planet of a few Earth masses with a hazy envelope 9 Earth radii across: density about 0.04 g/cm^3.",
                P(StructureKind.SuperPuff, "count", "14000", "puffRadius", "7", "atmTemperature", "3500", "hazeOpacity", "1.5", "visibilityBoost", "2", "scatterAsymmetry", "0.5")),
            new Preset("Moon torn into a ring", StructureKind.RingingMoon, "An icy moon inside the Roche limit coming apart into a ring, outer layers first.",
                P(StructureKind.RingingMoon, "count", "14000", "moonDistanceRoche", "0.7", "moonSizeKm", "150", "disruptStartHours", "10", "disruptSpanHours", "300", "startAgeHours", "120", "sizeScale", "4", "visibilityBoost", "4")
                    .Add("clumpColor", "0.9, 0.86, 0.8")),
            new Preset("Rubble pile shedding", StructureKind.RubblePile, "A fast-spinning asteroid flinging boulders into orbit from its equator.",
                P(StructureKind.RubblePile, "count", "9000", "spinFraction", "0.97", "spinUpYears", "0.0005", "visibilityBoost", "4")),
            new Preset("Contact binary splitting", StructureKind.RubblePile, "Two lobes in contact spinning up until they part into a binary.",
                P(StructureKind.RubblePile, "count", "9000", "contactBinary", "1", "spinFraction", "0.6", "splitHours", "20", "splitSpeed", "0.8", "visibilityBoost", "4")),
            new Preset("Toroidal planet (speculative)", StructureKind.ToroidalPlanet, "SPECULATIVE: a planet shaped like a doughnut, spinning to hold itself up. Nothing like it is known.",
                P(StructureKind.ToroidalPlanet, "count", "12000", "speculative", "1", "visibilityBoost", "4")),
        };
    }

    /// <summary>The node of a preset by name, or null.</summary>
    public static CfgNode PresetNode(string name)
    {
        foreach (Preset p in Presets)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Node;
        return null;
    }

    /// <summary>Applies a preset to a structure: its kind and settings, replacing what the preset names.</summary>
    public static void Apply(Preset preset, StructureConfig config)
    {
        config.Kind = preset.Kind;
        Read(preset.Node, config, null);
        config.Clamp();
    }
}
