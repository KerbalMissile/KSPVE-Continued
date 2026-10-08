using System;
using System.Collections.Generic;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>
/// One KSPVE_BLACKHOLE node per black hole (catalog #80, #81). Distances in the disk are in units of
/// the horizon radius r_s, so the physics is scale-free: the innermost stable circular orbit is 3 r_s
/// and the photon sphere 1.5 r_s regardless of how large a planet pack makes the hole.
/// </summary>
public static class BlackHoleSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE_BLACKHOLE");

    public static readonly StringSetting Body = Schema.Add(new StringSetting("body", string.Empty, "Name of the celestial body that is the black hole."));
    public static readonly BoolSetting FindByMass = Schema.Add(new BoolSetting("findByMass", false, "Put the disk on the most massive body (at least minMass) rather than looking it up by name, so it survives planet packs renaming bodies."));
    public static readonly DoubleSetting MinMass = Schema.Add(new DoubleSetting("minMass", 5e30, 0.0, 1e40, "With findByMass: the least mass (kg) a body needs to count as the black hole; 5e30 is 2.5 Suns, so no ordinary star qualifies."));
    public static readonly DoubleSetting HorizonRadius = Schema.Add(new DoubleSetting("horizonRadius", 0.0, 0.0, 1e15, "Event horizon radius in metres; 0 uses the body's radius."));
    public static readonly DoubleSetting DiskInner = Schema.Add(new DoubleSetting("diskInner", 3.0, 1.5, 100.0, "Disk inner edge in horizon radii (3 = innermost stable circular orbit)."));
    public static readonly DoubleSetting DiskOuter = Schema.Add(new DoubleSetting("diskOuter", 14.0, 2.0, 1000.0, "Disk outer edge in horizon radii."));
    public static readonly DoubleSetting DiskThickness = Schema.Add(new DoubleSetting("diskThickness", 0.12, 0.005, 0.5, "Disk half-thickness as a fraction of radius."));
    public static readonly DoubleSetting PeakTemperature = Schema.Add(new DoubleSetting("peakTemperature", 6000.0, 1000.0, 100000.0, "Hottest disk temperature in kelvin, before relativistic shifts."));
    public static readonly DoubleSetting Brightness = Schema.Add(new DoubleSetting("brightness", 0.9, 0.0, 20.0, "Disk brightness."));
    public static readonly DoubleSetting RotationSpeed = Schema.Add(new DoubleSetting("rotationSpeed", 1.0, 0.0, 100.0, "Spin speed of the disk; 1 turns it once every ~8 s (the whole disk turns together)."));
    public static readonly DoubleSetting Turbulence = Schema.Add(new DoubleSetting("turbulence", 0.8, 0.0, 1.0, "Clumpy, streaky structure in the disk."));
    public static readonly DoubleSetting Spin = Schema.Add(new DoubleSetting("spin", 1.0, -1.0, 1.0, "Orbit direction about the normal: positive prograde, negative retrograde."));
    public static readonly DoubleSetting NormalX = Schema.Add(new DoubleSetting("normalX", 0.0, -1e6, 1e6, "Disk normal, X (inertial frame)."));
    public static readonly DoubleSetting NormalY = Schema.Add(new DoubleSetting("normalY", 1.0, -1e6, 1e6, "Disk normal, Y (inertial frame)."));
    public static readonly DoubleSetting NormalZ = Schema.Add(new DoubleSetting("normalZ", 0.0, -1e6, 1e6, "Disk normal, Z (inertial frame)."));
    public static readonly DoubleSetting ColorSaturation = Schema.Add(new DoubleSetting("colorSaturation", 2.2, 0.0, 4.0, "Colour vividness; 1 is the plain blackbody colour."));
    public static readonly DoubleSetting Glow = Schema.Add(new DoubleSetting("glow", 1.0, 0.0, 5.0, "Glow of hot plasma around the hole."));
    public static readonly DoubleSetting PhotonRing = Schema.Add(new DoubleSetting("photonRing", 1.0, 0.0, 5.0, "Brightness of the thin photon ring at the edge of the shadow."));
    public static readonly DoubleSetting Particles = Schema.Add(new DoubleSetting("particles", 1.0, 0.0, 2.0, "Sparks of white-hot matter riding in the disk."));
    public static readonly DoubleSetting Debris = Schema.Add(new DoubleSetting("debris", 1.0, 0.0, 1.0, "Dark chunks of debris riding in the disk."));
    public static readonly DoubleSetting Jets = Schema.Add(new DoubleSetting("jets", 1.3, 0.0, 5.0, "Relativistic jets along the spin axis; 0 turns them off."));
    public static readonly DoubleSetting JetLength = Schema.Add(new DoubleSetting("jetLength", 60.0, 5.0, 500.0, "Length of each jet in horizon radii."));
    public static readonly DoubleSetting JetWidth = Schema.Add(new DoubleSetting("jetWidth", 0.9, 0.1, 3.0, "Jet width: radius = jetWidth * (distance along the axis)^0.55, in horizon radii."));
    public static readonly DoubleSetting JetSpeed = Schema.Add(new DoubleSetting("jetSpeed", 0.85, 0.0, 0.99, "Speed of the jet gas as a fraction of light speed; faster beams the approaching jet brighter."));
    public static readonly DoubleSetting JetSpiral = Schema.Add(new DoubleSetting("jetSpiral", 1.0, 0.0, 1.0, "Make each jet a solid spiral core, its surface swirled with streaks of brighter gas; 0 is a plain column."));
    public static readonly DoubleSetting JetTurns = Schema.Add(new DoubleSetting("jetTurns", 4.0, 0.0, 20.0, "Turns the swirl makes along each jet's length."));
    public static readonly DoubleSetting JetFlow = Schema.Add(new DoubleSetting("jetFlow", 30.0, 0.0, 500.0, "How fast the jet's pattern streams outward, in horizon radii per second at mid-length (it speeds up further out)."));
    public static readonly DoubleSetting JetAbsorb = Schema.Add(new DoubleSetting("jetAbsorb", 0.6, 0.0, 5.0, "How much the jet gas absorbs light behind it, so near filaments hide far ones; 0 is a pure glow."));
    public static readonly BoolSetting DiskDrag = Schema.Add(new BoolSetting("diskDrag", true, "Gas in the disk drags vessels toward co-rotating with it."));
    public static readonly DoubleSetting DiskDragTimescale = Schema.Add(new DoubleSetting("diskDragTimescale", 0.5, 0.1, 10000.0, "Seconds for a vessel to match the gas speed at full disk density."));
    public static readonly DoubleSetting DiskDragMaxAccel = Schema.Add(new DoubleSetting("diskDragMaxAccel", 2000.0, 0.0, 100000.0, "Strongest pull at the disk's outer edge in m/s^2 (2000 = 2 km/s every second); it rises inward with the gas density."));
    public static readonly DoubleSetting DiskBuffeting = Schema.Add(new DoubleSetting("diskBuffeting", 1.0, 0.0, 5.0, "Turbulent shaking of vessels inside the disk; 0 turns it off."));
    public static readonly DoubleSetting DiskLight = Schema.Add(new DoubleSetting("diskLight", 1.0, 0.0, 5.0, "How strongly the disk's glow lights nearby vessels; 0 turns it off."));
    public static readonly IntSetting Steps = Schema.Add(new IntSetting("steps", 300, 32, 1000, "Maximum ray-march steps per pixel."));
    public static readonly DoubleSetting BoundRadius = Schema.Add(new DoubleSetting("boundRadius", 0.0, 0.0, 10000.0, "Radius (horizon radii) where rays start bending; 0 = automatic."));
    public static readonly BoolSetting Pulsar = Schema.Add(new BoolSetting("pulsar", false, "A pulsar, not a black hole: no shadow, lensing or disk, the body's own star shows; the jets are its beams, swept round the spin axis (the normal)."));
    public static readonly DoubleSetting BeamPeriod = Schema.Add(new DoubleSetting("beamPeriod", 0.0, 0.0, 1000.0, "Seconds (real time) for the jets to sweep once round the spin axis, like a pulsar's beams; 0 keeps them along the axis."));
    public static readonly BoolSetting DiskFromTde = Schema.Add(new BoolSetting("diskFromTde", false, "No disk or jets of its own: a tidal disruption event builds the configured disk from its debris over a couple of months, and the jets launch once it is half built."));
    public static readonly DoubleSetting BeamTilt = Schema.Add(new DoubleSetting("beamTilt", 30.0, 0.0, 90.0, "Angle between the sweeping jets and the spin axis, in degrees."));

    /// <summary>Index of the most massive body with at least <paramref name="minMass"/> kg, or -1 when none is that heavy.</summary>
    public static int HeaviestAbove(IReadOnlyList<double> masses, double minMass)
    {
        int best = -1;
        for (int i = 0; i < masses.Count; i++)
        {
            if (masses[i] < minMass) continue;
            if (best < 0 || masses[i] > masses[best]) best = i;
        }
        return best;
    }

    /// <summary>Node in PluginData/user.cfg holding one KSPVE_BLACKHOLE child per body the player has tweaked in game.</summary>
    public const string UserNodeName = "KSPVE_BLACKHOLES";

    /// <summary>The config node with the player's in-game tweaks for the same body laid over it (theirs win).</summary>
    public static CfgNode ApplyUserOverrides(CfgNode baseNode, CfgNode userContainer)
    {
        string body = baseNode?.GetLast(Body.Key)?.Trim();
        if (userContainer == null || string.IsNullOrEmpty(body)) return baseNode;
        foreach (CfgNode child in userContainer.Children)
        {
            if (child.Name == Schema.NodeName && child.GetLast(Body.Key)?.Trim() == body)
                return CfgNode.Overlay(baseNode, child);
        }
        return baseNode;
    }

    /// <summary>
    /// Swirl twist of the jets in radians per unit of zeta = z^0.45 (z in horizon radii), for <paramref name="turns"/>
    /// turns between the jet's base (z = 1.1) and its tip. Working in zeta keeps the pitch in step with the jet's radius
    /// (~ z^0.55), so the swirl keeps the same angle along the jet.
    /// </summary>
    public static double JetTwistRate(double turns, double length) =>
        2.0 * Math.PI * turns / (Math.Pow(Math.Max(length, 1.2), 0.45) - Math.Pow(1.1, 0.45));

    /// <summary>
    /// Outward flow of the jets' pattern in zeta per second, so it moves at <paramref name="flow"/> horizon radii per
    /// second at mid-length: dz/dt = rate * z^0.55 / 0.45.
    /// </summary>
    public static double JetFlowRate(double flow, double length) => flow * 0.45 / Math.Pow(0.5 * length, 0.55);

    /// <summary>
    /// With the spiral the jets are thick from their rounded base: radius = jetWidth * (z + JetBase * jetSpiral)^0.55
    /// (z in horizon radii). Must match JetBase in BlackHole.shader.
    /// </summary>
    public const double JetBase = 12.0;

    /// <summary>
    /// Radius (horizon radii) of the sphere around the hole that holds the whole jet, tip and edge glow included.
    /// </summary>
    public static double JetBound(double length, double width, double spiral)
    {
        double edge = 3.0 * width * Math.Pow(length + JetBase * spiral, 0.55);
        return Math.Sqrt(length * length + edge * edge) + 4.0;
    }

    /// <summary>
    /// A pulsar's beam axis: tilted <paramref name="tiltDegrees"/> from the spin axis and turned <paramref name="phaseRadians"/>
    /// round it (the lighthouse sweep). The tilt leans towards a fixed direction perpendicular to the spin axis, so the
    /// beam traces the same cone every turn.
    /// </summary>
    public static Double3 PulsarBeamAxis(Double3 spinAxis, double tiltDegrees, double phaseRadians)
    {
        Double3 n = spinAxis.Normalized();
        if (n.LengthSquared == 0.0) return Double3.UnitY;
        Double3 seed = Math.Abs(n.X) < 0.9 ? Double3.UnitX : Double3.UnitY;
        Double3 u = Double3.Cross(n, seed).Normalized();
        Double3 w = Double3.Cross(n, u);
        double tilt = tiltDegrees * Math.PI / 180.0;
        Double3 across = u * Math.Cos(phaseRadians) + w * Math.Sin(phaseRadians);
        return n * Math.Cos(tilt) + across * Math.Sin(tilt);
    }

    /// <summary>Colour presets for the settings window: disk peak temperature (K), colour saturation and brightness.</summary>
    public static readonly ColorPreset[] ColorPresets =
    {
        new ColorPreset("Gold", 6000, 2.2, 0.9),
        new ColorPreset("Blue", 40000, 3.4, 3.5),
        new ColorPreset("White-hot", 12000, 1.2, 1.2),
        new ColorPreset("Ember", 3000, 2.6, 2.0),
    };
}

/// <summary>A named disk colour: peak temperature (K), colour saturation and brightness.</summary>
public readonly struct ColorPreset
{
    public readonly string Name;
    public readonly double PeakTemperature, ColorSaturation, Brightness;

    public ColorPreset(string name, double peakTemperature, double colorSaturation, double brightness)
    {
        Name = name;
        PeakTemperature = peakTemperature;
        ColorSaturation = colorSaturation;
        Brightness = brightness;
    }
}
