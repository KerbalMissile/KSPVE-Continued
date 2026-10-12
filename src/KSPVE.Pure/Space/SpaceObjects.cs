using System;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Space;

/// <summary>
/// What a KSPVE_BLACKHOLE node draws (its kind key). BlackHole.shader numbers them the same way (ObjKind) and draws all
/// but the black hole in its object pass.
/// </summary>
public enum SpaceObjectKind
{
    BlackHole = 0,
    NeutronStar = 1,
    ProtoplanetaryDisk = 2,
    DebrisDisk = 3,
    AsteroidBelt = 4,
    Nebula = 5,
    Comet = 6,
    Galaxy = 7
}

/// <summary>
/// The other kinds of object a KSPVE_BLACKHOLE node can draw round its body, all in the shader, with nothing for KSP to
/// track: a neutron star with its disk, accretion columns, beams and magnetic field lines; a protoplanetary disk with its
/// ring gaps; a debris disk; an asteroid belt of shader-drawn rocks; a nebula; and a comet's coma and tails.
/// </summary>
public static class SpaceObjects
{
    /// <summary>The shader draws a nebula this many of its units in radius; the renderer sizes the unit to diskOuter.</summary>
    public const double NebulaShaderRadius = 10.0;

    /// <summary>Neutron star field lines: the innermost shell's apex (in star radii) and the ratio between shells. Must match BlackHole.shader.</summary>
    public const double FieldShellStart = 1.6, FieldShellRatio = 1.6;
    /// <summary>Neutron star field lines: the shells drawn, the innermost being 0.</summary>
    public const int FieldShells = 7;

    /// <summary>The kinds as the settings window names them, in SpaceObjectKind order.</summary>
    public static readonly string[] KindNames = { "Black hole", "Neutron star", "Protoplanetary disk", "Debris disk", "Asteroid belt", "Nebula", "Comet", "Galaxy" };

    public static string KindName(SpaceObjectKind kind) => KindNames[(int)kind];

    /// <summary>Kinds whose light the hole's gravity bends (their sizes are in horizon radii).</summary>
    public static bool BendsLight(SpaceObjectKind kind) => kind == SpaceObjectKind.BlackHole || kind == SpaceObjectKind.NeutronStar;

    /// <summary>Kinds whose central body is a star the game draws: it hides what is behind it.</summary>
    public static bool BodyHidesObject(SpaceObjectKind kind) =>
        kind == SpaceObjectKind.ProtoplanetaryDisk || kind == SpaceObjectKind.DebrisDisk || kind == SpaceObjectKind.AsteroidBelt;

    /// <summary>
    /// The length unit of the object's sizes in metres: <paramref name="configured"/> (horizonRadius) when set; otherwise
    /// the horizon radius for a black hole (BlackHoleSettings.HorizonMeters), the body's radius over starRadius for a
    /// neutron star (so the body is the star), and the body's radius for the rest.
    /// </summary>
    public static double UnitMeters(SpaceObjectKind kind, double configured, double bodyRadius, bool bodyIsShadow, double starRadius)
    {
        if (configured > 0.0) return configured;
        switch (kind)
        {
            case SpaceObjectKind.BlackHole: return BlackHoleSettings.HorizonMeters(0.0, bodyRadius, bodyIsShadow);
            case SpaceObjectKind.NeutronStar: return bodyRadius / Math.Max(starRadius, 1.01);
            default: return bodyRadius;
        }
    }

    /// <summary>The unit the shader works in (metres): the length unit, except a nebula's or galaxy's, sized so it is diskOuter units across.</summary>
    public static double ShaderUnitMeters(SpaceObjectKind kind, double unitMeters, double diskOuter) =>
        kind == SpaceObjectKind.Nebula || kind == SpaceObjectKind.Galaxy ? unitMeters * diskOuter / NebulaShaderRadius : unitMeters;

    /// <summary>
    /// Radius holding the outermost magnetic field line round a star of <paramref name="starRadius"/>: the first shell
    /// reaches FieldShellStart star radii, each further one <paramref name="ratio"/> times more.
    /// </summary>
    public static double FieldExtent(double starRadius, double shells, double ratio) =>
        starRadius * FieldShellStart * Math.Pow(ratio, Math.Max(Math.Round(shells), 1.0) - 1.0) * 1.1;

    /// <summary>
    /// Radius of the sphere holding all of the object, in shader units; <paramref name="starRadius"/> is the central star's
    /// (shader units), round which any magnetic field lines loop.
    /// </summary>
    public static double Bound(SpaceObjectKind kind, double diskOuter, double diskThickness, double flaring, double edgeSoftness,
        double starRadius, double fieldLines, double fieldShells, double fieldShellRatio, double beams, double beamLength,
        double comaSize, double ionTail, double dustTail, double dustTailCurve)
    {
        double field = fieldLines > 0.0 && starRadius > 0.0 ? FieldExtent(starRadius, fieldShells, fieldShellRatio) : 0.0;
        return Math.Max(field, KindBound(kind, diskOuter, diskThickness, flaring, edgeSoftness, beams, beamLength, comaSize, ionTail, dustTail, dustTailCurve));
    }

    private static double KindBound(SpaceObjectKind kind, double diskOuter, double diskThickness, double flaring, double edgeSoftness,
        double beams, double beamLength, double comaSize, double ionTail, double dustTail, double dustTailCurve)
    {
        switch (kind)
        {
            case SpaceObjectKind.NeutronStar:
                return Math.Max(diskOuter * 1.25 + 8.0, beams > 0.0 ? 2.5 * beamLength : 0.0);   // the beams fade by e^-2.5
            case SpaceObjectKind.ProtoplanetaryDisk:
            case SpaceObjectKind.DebrisDisk:
            case SpaceObjectKind.AsteroidBelt:
                // The disk reaches 1 + 0.1 edgeSoftness outer radii, and 4 half-thicknesses (flared out there) above and below it.
                double reach = 1.0 + 0.1 * edgeSoftness;
                double height = 4.0 * diskThickness * reach * Math.Pow(reach, flaring);
                return diskOuter * reach * Math.Sqrt(1.0 + height * height) + 1e-3 * diskOuter;
            case SpaceObjectKind.Nebula:
                return 1.3 * NebulaShaderRadius;
            case SpaceObjectKind.Galaxy:
                return 1.25 * NebulaShaderRadius;
            case SpaceObjectKind.Comet:
                // The tails fade out over their length: three lengths hold all but 5%; the dust tail's curve bends it out too.
                double tail = 3.0 * Math.Max(ionTail, dustTail * (1.0 + 3.0 * dustTailCurve));
                return Math.Max(tail, 10.0 * comaSize);
            default:
                return diskOuter * 1.25 + 8.0;
        }
    }

    /// <summary>
    /// The settings that suit each kind, laid under a node's own values: a protoplanetary disk 8 to 215 star radii (the
    /// Sun's: out to 1 AU) with six ring gaps; a debris disk, a narrow ring at 160 to 215; an asteroid belt at 100 to 155;
    /// a neutron star 3 horizon radii across with its disk at 18 to 70; a nebula 1000 body radii across; a comet's coma
    /// and tails in 10,000 km units. None of them drag or light vessels, or wait for a tidal disruption.
    /// </summary>
    public static CfgNode KindDefaults(SpaceObjectKind kind)
    {
        var node = new CfgNode(BlackHoleSettings.Schema.NodeName);
        if (kind == SpaceObjectKind.BlackHole) return node;
        node.Add("diskDrag", "False").Add("diskLight", "0").Add("diskFromTde", "False").Add("jets", "0").Add("pulsar", "False");
        switch (kind)
        {
            case SpaceObjectKind.NeutronStar:
                node.Add("starRadius", "3").Add("diskInner", "18").Add("diskOuter", "70").Add("diskThickness", "0.08")
                    .Add("peakTemperature", "40000").Add("colorSaturation", "3.4").Add("brightness", "1").Add("surfaceTemperature", "1000000")
                    .Add("fieldLines", "1").Add("accretionColumns", "1").Add("beams", "1").Add("jetLength", "120")
                    .Add("beamPeriod", "4").Add("beamTilt", "25").Add("colorA", "0.55, 0.75, 1").Add("steps", "1000");
                break;
            case SpaceObjectKind.ProtoplanetaryDisk:
                node.Add("diskInner", "8").Add("diskOuter", "215").Add("diskThickness", "0.06").Add("flaring", "0.3")
                    .Add("rings", "6").Add("ringDepth", "0.97").Add("ringWidth", "0.02").Add("starGlow", "0.08").Add("dust", "0.2")
                    .Add("dustColor", "1, 0.55, 0.28").Add("outerDustColor", "1, 0.55, 0.28").Add("rocks", "0").Add("rotationSpeed", "0.01").Add("brightness", "1").Add("steps", "600");
                break;
            case SpaceObjectKind.DebrisDisk:
                node.Add("diskInner", "160").Add("diskOuter", "215").Add("diskThickness", "0.015").Add("flaring", "0").Add("rings", "0")
                    .Add("dust", "0.08").Add("dustColor", "1, 0.62, 0.38").Add("outerDustColor", "1, 0.62, 0.38").Add("rocks", "0.25").Add("rotationSpeed", "0.01")
                    .Add("brightness", "1").Add("steps", "600");
                break;
            case SpaceObjectKind.AsteroidBelt:
                node.Add("diskInner", "100").Add("diskOuter", "155").Add("diskThickness", "0.06").Add("flaring", "0").Add("rings", "0")
                    .Add("dust", "0.01").Add("dustColor", "1, 0.62, 0.38").Add("outerDustColor", "1, 0.62, 0.38").Add("rocks", "0.2").Add("rotationSpeed", "0.01")
                    .Add("brightness", "1").Add("steps", "600");
                break;
            case SpaceObjectKind.Nebula:
                node.Add("diskOuter", "1000").Add("dust", "1").Add("dustLanes", "1").Add("cloudScale", "1").Add("cloudWarp", "1")
                    .Add("filaments", "1.5").Add("colorA", "1, 0.25, 0.45").Add("colorB", "0.2, 0.75, 0.85").Add("rotationSpeed", "0")
                    .Add("brightness", "1").Add("steps", "300");
                break;
            case SpaceObjectKind.Galaxy:
                // The Milky Way's size: 50,000 light years in radius, a thin disk, a bar, two main arms.
                node.Add("horizonRadius", "9460730472580800").Add("diskOuter", "50000").Add("diskThickness", "0.03")
                    .Add("bulgeSize", "0.12").Add("bulgeBrightness", "2.5").Add("barLength", "0.15").Add("starClusters", "1")
                    .Add("spiralArms", "2").Add("spiralWinding", "4.5").Add("spiralStrength", "0.85").Add("dustLanes", "1")
                    .Add("clumps", "0.6").Add("cloudWarp", "0.1").Add("innerColor", "1, 0.82, 0.55").Add("outerColor", "1, 0.92, 0.82")
                    .Add("colorA", "0.55, 0.7, 1").Add("colorB", "1, 0.35, 0.55").Add("rotationSpeed", "0").Add("brightness", "1")
                    .Add("steps", "400");
                break;
            case SpaceObjectKind.Comet:
                node.Add("horizonRadius", "10000000").Add("activity", "1").Add("comaSize", "2").Add("ionTailLength", "60")
                    .Add("dustTailLength", "35").Add("dustTailCurve", "0.5").Add("dustTailWidth", "1").Add("colorA", "0.45, 0.65, 1")
                    .Add("colorB", "1, 0.92, 0.75").Add("comaColor", "0.6, 1, 0.75").Add("rotationSpeed", "0").Add("brightness", "1")
                    .Add("steps", "300");
                break;
        }
        return node;
    }
}
