using KSPVE.Pure.Config;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// One KSPVE_TDE node (GameData/KSPVE/Config/KSPVE_TDE.cfg) sets up the tidal disruption event a save can switch on;
/// any of these keys can also be set per save in that save's KSPVE file, which wins. Distances in horizon radii.
/// The physics runs in the frame the renderer already draws: the drawn horizon is a real Schwarzschild horizon, so the
/// hole's mass is r_s c^2 / 2G (a few million Suns for Kcalbeloh), and every timescale is real, times timeScale.
/// The default star is a big O-type star coming in along the plane of the hole's disk on a deep orbit, so its debris
/// settles where the hole's disk is and builds it (a hole with diskFromTde has no disk until then).
/// </summary>
public static class TdeSettings
{
    public static readonly SettingsSchema Schema = new SettingsSchema("KSPVE_TDE");

    public static readonly DoubleSetting StarMass = Schema.Add(new DoubleSetting("starMass", 40.0, 0.08, 150.0, "Mass of the star, in Suns."));
    public static readonly DoubleSetting StarRadius = Schema.Add(new DoubleSetting("starRadius", 15.0, 0.05, 2000.0, "Radius of the star, in solar radii."));
    public static readonly DoubleSetting StarTemperature = Schema.Add(new DoubleSetting("starTemperature", 40000.0, 2000.0, 60000.0, "Surface temperature of the star, kelvin."));
    public static readonly DoubleSetting PolytropeIndex = Schema.Add(new DoubleSetting("polytropeIndex", 3.0, 0.5, 4.5, "Internal structure: 3 = radiative envelope (the Sun, massive stars), 1.5 = fully convective."));
    public static readonly DoubleSetting Periapsis = Schema.Add(new DoubleSetting("periapsis", 5.0, 0.0, 10000.0, "Closest approach of the star's orbit, horizon radii. The debris settles at about twice this, so 5 builds a disk where the hole's own is (3-14). 0 = set by beta instead."));
    public static readonly DoubleSetting Beta = Schema.Add(new DoubleSetting("beta", 2.0, 0.5, 50.0, "Used when periapsis = 0: tidal radius over periapsis. A star with n = 3 is only partly torn apart below ~1.85."));
    public static readonly DoubleSetting StartDistance = Schema.Add(new DoubleSetting("startDistance", 6.0, 1.5, 1000.0, "Where the star starts falling in, in tidal radii."));
    public static readonly DoubleSetting TimeScale = Schema.Add(new DoubleSetting("timeScale", 1.0, 0.0001, 100.0, "Playback speed of the whole event: 1 = real time (the disk forms over ~2-3 months), 0.5 = twice as slow."));
    public static readonly DoubleSetting Inclination = Schema.Add(new DoubleSetting("inclination", 0.0, -1.0, 180.0, "Tilt of the star's orbit from the disk plane, degrees. 0 = in the disk's plane, turning with it, so the debris builds the hole's own disk; -1 = random (fixed per save)."));
    public static readonly DoubleSetting Node = Schema.Add(new DoubleSetting("node", -1.0, -1.0, 360.0, "Longitude of the orbit's ascending node on the disk plane, degrees; -1 = random."));
    public static readonly DoubleSetting PeriapsisAngle = Schema.Add(new DoubleSetting("periapsisAngle", -1.0, -1.0, 360.0, "Argument of periapsis, degrees; -1 = random."));
    public static readonly DoubleSetting OutflowFraction = Schema.Add(new DoubleSetting("outflowFraction", 0.5, 0.0, 1.0, "Share of the gas shocked where the stream crosses itself that is blown out (Lu and Bonnerot 2020); the rest forms the disk."));
    public static readonly DoubleSetting Efficiency = Schema.Add(new DoubleSetting("efficiency", 0.1, 0.01, 0.42, "Radiative efficiency of accretion (0.057 non-spinning thin disk, up to 0.42)."));
    public static readonly DoubleSetting ViscousAlpha = Schema.Add(new DoubleSetting("viscousAlpha", 0.1, 0.001, 1.0, "Shakura-Sunyaev alpha of the new disk; sets how fast it drains into the hole."));
    public static readonly DoubleSetting EnvelopeOpacity = Schema.Add(new DoubleSetting("envelopeOpacity", 0.00001, 0.0, 1.0, "How opaque the outflow's photosphere is drawn: 1 = physical (at peak it hides everything inside it), lower lets the inner workings show through."));
    public static readonly DoubleSetting StarGlow = Schema.Add(new DoubleSetting("starGlow", 1.0, 0.0, 1000.0, "Display boost for the star: 1 = physical against the disk. A hot star needs none; a cool, Sun-like one needs ~50-100 to be seen beside the disk's 40,000 K gas."));
    public static readonly DoubleSetting StreamGlow = Schema.Add(new DoubleSetting("streamGlow", 40.0, 0.0, 1000.0, "Display boost for the debris stream once it has cooled (it fades in below ~16,000 K): at ~5,000 K it is about 100x dimmer than the disk in visible light; 1 = physical."));
    public static readonly DoubleSetting LineVisibility = Schema.Add(new DoubleSetting("lineVisibility", 0.35, 0.0, 1.0, "Display aid: the least share of a pixel the debris stream covers. Far off it is thinner than a pixel; 0 draws it physically (nearly invisible from afar)."));
    public static readonly DoubleSetting RenderRadius = Schema.Add(new DoubleSetting("renderRadius", 4000.0, 50.0, 100000.0, "Debris further than this from the hole (horizon radii) is no longer drawn."));
}

/// <summary>The resolved KSPVE_TDE values as plain numbers.</summary>
public sealed class TdeParameters
{
    public double StarMassSuns = 40.0, StarRadiusSuns = 15.0, StarTemperature = 40000.0, PolytropeIndex = 3.0, Periapsis = 5.0, Beta = 2.0;
    public double StartDistance = 6.0, TimeScale = 1.0;
    public double InclinationDeg = 0.0, NodeDeg = -1.0, PeriapsisAngleDeg = -1.0;
    public double OutflowFraction = 0.5, Efficiency = 0.1, ViscousAlpha = 0.1;
    public double EnvelopeOpacity = 0.00001, StarGlow = 1.0, StreamGlow = 40.0, LineVisibility = 0.35, RenderRadius = 4000.0;

    public static TdeParameters From(SettingsValues v) => new TdeParameters
    {
        StarMassSuns = v.Get(TdeSettings.StarMass),
        StarRadiusSuns = v.Get(TdeSettings.StarRadius),
        StarTemperature = v.Get(TdeSettings.StarTemperature),
        PolytropeIndex = v.Get(TdeSettings.PolytropeIndex),
        Periapsis = v.Get(TdeSettings.Periapsis),
        Beta = v.Get(TdeSettings.Beta),
        StartDistance = v.Get(TdeSettings.StartDistance),
        TimeScale = v.Get(TdeSettings.TimeScale),
        InclinationDeg = v.Get(TdeSettings.Inclination),
        NodeDeg = v.Get(TdeSettings.Node),
        PeriapsisAngleDeg = v.Get(TdeSettings.PeriapsisAngle),
        OutflowFraction = v.Get(TdeSettings.OutflowFraction),
        Efficiency = v.Get(TdeSettings.Efficiency),
        ViscousAlpha = v.Get(TdeSettings.ViscousAlpha),
        EnvelopeOpacity = v.Get(TdeSettings.EnvelopeOpacity),
        StarGlow = v.Get(TdeSettings.StarGlow),
        StreamGlow = v.Get(TdeSettings.StreamGlow),
        LineVisibility = v.Get(TdeSettings.LineVisibility),
        RenderRadius = v.Get(TdeSettings.RenderRadius)
    };

    public TdeParameters Clone() => (TdeParameters)MemberwiseClone();

    /// <summary>Everything that changes the physics (not the look), to tell when the model must be rebuilt.</summary>
    public string PhysicsKey() => string.Join("|", StarMassSuns, StarRadiusSuns, StarTemperature, PolytropeIndex, Periapsis, Beta, StartDistance,
        InclinationDeg, NodeDeg, PeriapsisAngleDeg, OutflowFraction, Efficiency, ViscousAlpha, RenderRadius);
}
