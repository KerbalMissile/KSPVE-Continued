using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Post;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using KSP.Localization;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>
/// A black hole configured by a KSPVE_BLACKHOLE node, or another kind of object drawn round its body (SpaceObjects): a
/// neutron star, a protoplanetary or debris disk, an asteroid belt, a nebula or a comet.
/// </summary>
internal sealed class BlackHoleDefinition
{
    private CelestialBody _body;

    public string BodyName;
    public SpaceObjectKind Kind;
    /// <summary>Which of the body's objects this is (config order); matches the player's tweaks in user.cfg to it.</summary>
    public int Slot;
    /// <summary>Drawn at all (the settings window can switch off any object).</summary>
    public bool Enabled;
    /// <summary>Added in the settings window, not by a config: kept only in user.cfg.</summary>
    public bool AddedInGame;
    /// <summary>Placed by the procedural rules (ProceduralObjects): made again each game, kept only once the player keeps it.</summary>
    public bool Procedural;
    public bool FindByMass;
    public double MinMass;
    public double HorizonRadiusMeters;
    /// <summary>With HorizonRadiusMeters 0: the body's radius is the shadow's (the hole drawn the body's size), not the horizon's.</summary>
    public bool BodyIsShadow;
    public double DiskInner, DiskOuter, DiskThickness;
    public double PeakTemperature, Brightness, RotationSpeed, Turbulence, Spin;
    /// <summary>0..1: the disk drawn as swirling streaks when seen from above (BlackHoleSettings.StreakView).</summary>
    public double DiskStreaks, StreaksFrom, StreaksFull;
    /// <summary>Display aid: the disk beyond 20 horizon radii shown as if exposed for it (BlackHoleSettings.OuterGlow).</summary>
    public double OuterGlow;
    /// <summary>0..1: the disk's outer edge from wispy to crisp.</summary>
    public double DiskEdge;
    /// <summary>0..1 (display): the disk from dimming outward as it cools to evenly bright.</summary>
    public double DiskEvenness;
    public double ColorSaturation, Glow, PhotonRing, Particles, Debris;
    public double Jets, JetLength, JetWidth, JetSpeed, JetSpiral, JetTurns, JetFlow, JetAbsorb;
    public double JetKnots, JetKnotStart, JetKnotSpacing, JetKnotGrowth, JetStrands, JetStrandSharpness;
    // Mass transfer from a companion; a nebula's gas; a custom texture.
    public string Companion, Texture;
    public double StreamBrightness, StreamWidth, HotSpot, TextureStrength, TextureShape;
    public double CompanionSpots, CompanionGlare;
    public double CompanionDistance, CompanionAngle, CompanionHeight, CompanionRadius, CompanionTemperature, CompanionBrightness, CompanionMassRatio, CompanionPeriod, StreamTurns, StreamRing, StreamSpiralTo;
    public double StreamCurve, StreamFlatness, StreamOpacity, StreamFlowLines, StreamFlowScale, StreamFray, StreamTaper, StreamBridge, StreamHeat, CompanionPull;
    /// <summary>The stream's winding drawn by hand (KSPVE.Pure.Space.StreamShape); empty = the computed one.</summary>
    public string StreamShape;
    public Double3 StreamColor;
    public NebulaGas GasType;
    public TextureMode TextureMapping;
    public Vector3d Normal;
    public int Steps;
    public double BoundRadius;
    public bool DiskDrag;
    public double DiskDragTimescale, DiskDragMaxAccel, DiskBuffeting, DiskLight;
    public bool Pulsar;
    public double BeamPeriod, BeamTilt;
    /// <summary>No disk or jets until a tidal disruption builds them (TidalDisruptions).</summary>
    public bool DiskFromTde;

    // The other kinds of object (BlackHoleSettings' keys of the same names).
    public double StarRadius, SurfaceTemperature, StarTemperature, FieldLines, AccretionColumns, Beams;
    public double Flaring, Rings, RingDepth, RingWidth, StarGlow, Dust, LightFalloff;
    public double Rocks, RockSize, RockSpacing, RockAlbedo, RockMaxAngle;
    /// <summary>Whether its rocks can be hit, and the solid field's spacing, largest radius and reach (m).</summary>
    public RockCollisions RockCollisions;
    public double SolidRockSpacing, SolidRockSize, SolidRockRange;
    public double BulgeSize, BulgeBrightness, BarLength, StarClusters;
    public double DustLanes, CloudScale, CloudWarp, Filaments, Shell;
    public double Activity, ComaSize, IonTailLength, DustTailLength, DustTailCurve, DustTailWidth;
    public Double3 DustColor, ColorA, ColorB, ComaColor;
    public string LightSource;
    // Parts: the disk's colours and brightness inside and out, the jets', the photon ring's; a dusty disk's gaps one by one.
    public double DiskTint, InnerBrightness, OuterBrightness;
    public Double3 InnerColor, OuterColor, JetColor, PhotonRingColor, OuterDustColor, RockColor;
    public string Gaps, GapWidths, GapDepths;
    // Every other part (BlackHoleSettings' keys of the same names).
    public double DensityFalloff, EdgeSoftness, Clumps, ClumpSize, ScatterAsymmetry, RimGlow, RimTemperature, SpiralArms, SpiralStrength, SpiralWinding, RockSpread, RockSizeSpread, CloudStretch, CloudRagged, CloudFill, ComaBrightness, NucleusBrightness, IonBrightness, DustBrightness, IonTailWidth, Streamers, Striae, FieldLineCount, FieldShells, FieldShellRatio, FieldLineWidth, ColumnWidth, BeamWidth, Hotspots, DiskDensity, DiskOpacity, Emission, CloudSize, SelfShadow, InnerLight, StreakWinding, StreakStretch, StreakLanes, ParticleSpacing;

    /// <summary>The GameData config node, without the player's tweaks (for "reset to config").</summary>
    public CfgNode ConfigNode;
    /// <summary>The player has tweaked this hole in the settings window (or did in an earlier session).</summary>
    public bool Edited;

    public static BlackHoleDefinition From(SettingsValues v) => new BlackHoleDefinition
    {
        BodyName = v.Get(BlackHoleSettings.Body),
        Kind = v.Get(BlackHoleSettings.Kind),
        Slot = v.Get(BlackHoleSettings.Slot),
        Enabled = v.Get(BlackHoleSettings.Enabled),
        StarRadius = v.Get(BlackHoleSettings.StarRadius),
        SurfaceTemperature = v.Get(BlackHoleSettings.SurfaceTemperature),
        StarTemperature = v.Get(BlackHoleSettings.StarTemperature),
        FieldLines = v.Get(BlackHoleSettings.FieldLines),
        AccretionColumns = v.Get(BlackHoleSettings.AccretionColumns),
        Beams = v.Get(BlackHoleSettings.Beams),
        Flaring = v.Get(BlackHoleSettings.Flaring),
        Rings = v.Get(BlackHoleSettings.Rings),
        RingDepth = v.Get(BlackHoleSettings.RingDepth),
        RingWidth = v.Get(BlackHoleSettings.RingWidth),
        StarGlow = v.Get(BlackHoleSettings.StarGlow),
        Dust = v.Get(BlackHoleSettings.Dust),
        LightFalloff = v.Get(BlackHoleSettings.LightFalloff),
        DustColor = v.Get(BlackHoleSettings.DustColor),
        Rocks = v.Get(BlackHoleSettings.Rocks),
        RockSize = v.Get(BlackHoleSettings.RockSize),
        RockSpacing = v.Get(BlackHoleSettings.RockSpacing),
        RockAlbedo = v.Get(BlackHoleSettings.RockAlbedo),
        RockMaxAngle = v.Get(BlackHoleSettings.RockMaxAngle),
        BulgeSize = v.Get(BlackHoleSettings.BulgeSize),
        BulgeBrightness = v.Get(BlackHoleSettings.BulgeBrightness),
        BarLength = v.Get(BlackHoleSettings.BarLength),
        StarClusters = v.Get(BlackHoleSettings.StarClusters),
        DustLanes = v.Get(BlackHoleSettings.DustLanes),
        CloudScale = v.Get(BlackHoleSettings.CloudScale),
        CloudWarp = v.Get(BlackHoleSettings.CloudWarp),
        Filaments = v.Get(BlackHoleSettings.Filaments),
        Shell = v.Get(BlackHoleSettings.Shell),
        ColorA = v.Get(BlackHoleSettings.ColorA),
        ColorB = v.Get(BlackHoleSettings.ColorB),
        Activity = v.Get(BlackHoleSettings.Activity),
        ComaSize = v.Get(BlackHoleSettings.ComaSize),
        ComaColor = v.Get(BlackHoleSettings.ComaColor),
        IonTailLength = v.Get(BlackHoleSettings.IonTailLength),
        DustTailLength = v.Get(BlackHoleSettings.DustTailLength),
        DustTailCurve = v.Get(BlackHoleSettings.DustTailCurve),
        DustTailWidth = v.Get(BlackHoleSettings.DustTailWidth),
        LightSource = v.Get(BlackHoleSettings.LightSource),
        DiskTint = v.Get(BlackHoleSettings.DiskTint),
        InnerColor = v.Get(BlackHoleSettings.InnerColor),
        OuterColor = v.Get(BlackHoleSettings.OuterColor),
        InnerBrightness = v.Get(BlackHoleSettings.InnerBrightness),
        OuterBrightness = v.Get(BlackHoleSettings.OuterBrightness),
        JetColor = v.Get(BlackHoleSettings.JetColor),
        PhotonRingColor = v.Get(BlackHoleSettings.PhotonRingColor),
        OuterDustColor = v.Get(BlackHoleSettings.OuterDustColor),
        RockColor = v.Get(BlackHoleSettings.RockColor),
        Gaps = v.Get(BlackHoleSettings.Gaps),
        GapWidths = v.Get(BlackHoleSettings.GapWidths),
        GapDepths = v.Get(BlackHoleSettings.GapDepths),
        DensityFalloff = v.Get(BlackHoleSettings.DensityFalloff),
        EdgeSoftness = v.Get(BlackHoleSettings.EdgeSoftness),
        Clumps = v.Get(BlackHoleSettings.Clumps),
        ClumpSize = v.Get(BlackHoleSettings.ClumpSize),
        ScatterAsymmetry = v.Get(BlackHoleSettings.ScatterAsymmetry),
        RimGlow = v.Get(BlackHoleSettings.RimGlow),
        RimTemperature = v.Get(BlackHoleSettings.RimTemperature),
        SpiralArms = v.Get(BlackHoleSettings.SpiralArms),
        SpiralStrength = v.Get(BlackHoleSettings.SpiralStrength),
        SpiralWinding = v.Get(BlackHoleSettings.SpiralWinding),
        RockSpread = v.Get(BlackHoleSettings.RockSpread),
        RockSizeSpread = v.Get(BlackHoleSettings.RockSizeSpread),
        RockCollisions = v.Get(BlackHoleSettings.RockCollisionMode),
        SolidRockSpacing = v.Get(BlackHoleSettings.SolidRockSpacing),
        SolidRockSize = v.Get(BlackHoleSettings.SolidRockSize),
        SolidRockRange = v.Get(BlackHoleSettings.SolidRockRange),
        CloudStretch = v.Get(BlackHoleSettings.CloudStretch),
        CloudRagged = v.Get(BlackHoleSettings.CloudRagged),
        CloudFill = v.Get(BlackHoleSettings.CloudFill),
        ComaBrightness = v.Get(BlackHoleSettings.ComaBrightness),
        NucleusBrightness = v.Get(BlackHoleSettings.NucleusBrightness),
        IonBrightness = v.Get(BlackHoleSettings.IonBrightness),
        DustBrightness = v.Get(BlackHoleSettings.DustBrightness),
        IonTailWidth = v.Get(BlackHoleSettings.IonTailWidth),
        Streamers = v.Get(BlackHoleSettings.Streamers),
        Striae = v.Get(BlackHoleSettings.Striae),
        FieldLineCount = v.Get(BlackHoleSettings.FieldLineCount),
        FieldShells = v.Get(BlackHoleSettings.FieldShells),
        FieldShellRatio = v.Get(BlackHoleSettings.FieldShellRatio),
        FieldLineWidth = v.Get(BlackHoleSettings.FieldLineWidth),
        ColumnWidth = v.Get(BlackHoleSettings.ColumnWidth),
        BeamWidth = v.Get(BlackHoleSettings.BeamWidth),
        Hotspots = v.Get(BlackHoleSettings.Hotspots),
        DiskDensity = v.Get(BlackHoleSettings.DiskDensity),
        DiskOpacity = v.Get(BlackHoleSettings.DiskOpacity),
        Emission = v.Get(BlackHoleSettings.Emission),
        CloudSize = v.Get(BlackHoleSettings.CloudSize),
        SelfShadow = v.Get(BlackHoleSettings.SelfShadow),
        InnerLight = v.Get(BlackHoleSettings.InnerLight),
        StreakWinding = v.Get(BlackHoleSettings.StreakWinding),
        StreakStretch = v.Get(BlackHoleSettings.StreakStretch),
        StreakLanes = v.Get(BlackHoleSettings.StreakLanes),
        ParticleSpacing = v.Get(BlackHoleSettings.ParticleSpacing),
        FindByMass = v.Get(BlackHoleSettings.FindByMass),
        MinMass = v.Get(BlackHoleSettings.MinMass),
        HorizonRadiusMeters = v.Get(BlackHoleSettings.HorizonRadius),
        BodyIsShadow = v.Get(BlackHoleSettings.BodyIsShadow),
        DiskInner = v.Get(BlackHoleSettings.DiskInner),
        DiskOuter = v.Get(BlackHoleSettings.DiskOuter),
        DiskThickness = v.Get(BlackHoleSettings.DiskThickness),
        PeakTemperature = v.Get(BlackHoleSettings.PeakTemperature),
        Brightness = v.Get(BlackHoleSettings.Brightness),
        RotationSpeed = v.Get(BlackHoleSettings.RotationSpeed),
        Turbulence = v.Get(BlackHoleSettings.Turbulence),
        DiskStreaks = v.Get(BlackHoleSettings.DiskStreaks),
        StreaksFrom = v.Get(BlackHoleSettings.StreaksFrom),
        StreaksFull = v.Get(BlackHoleSettings.StreaksFull),
        OuterGlow = v.Get(BlackHoleSettings.OuterGlow),
        DiskEdge = v.Get(BlackHoleSettings.DiskEdge),
        DiskEvenness = v.Get(BlackHoleSettings.DiskEvenness),
        Spin = v.Get(BlackHoleSettings.Spin),
        ColorSaturation = v.Get(BlackHoleSettings.ColorSaturation),
        Glow = v.Get(BlackHoleSettings.Glow),
        PhotonRing = v.Get(BlackHoleSettings.PhotonRing),
        Normal = new Vector3d(v.Get(BlackHoleSettings.NormalX), v.Get(BlackHoleSettings.NormalY), v.Get(BlackHoleSettings.NormalZ)),
        Steps = v.Get(BlackHoleSettings.Steps),
        BoundRadius = v.Get(BlackHoleSettings.BoundRadius),
        Particles = v.Get(BlackHoleSettings.Particles),
        Jets = v.Get(BlackHoleSettings.Jets),
        JetLength = v.Get(BlackHoleSettings.JetLength),
        JetWidth = v.Get(BlackHoleSettings.JetWidth),
        JetSpeed = v.Get(BlackHoleSettings.JetSpeed),
        JetSpiral = v.Get(BlackHoleSettings.JetSpiral),
        JetTurns = v.Get(BlackHoleSettings.JetTurns),
        JetFlow = v.Get(BlackHoleSettings.JetFlow),
        JetAbsorb = v.Get(BlackHoleSettings.JetAbsorb),
        JetKnots = v.Get(BlackHoleSettings.JetKnots),
        JetStrands = v.Get(BlackHoleSettings.JetStrands),
        JetStrandSharpness = v.Get(BlackHoleSettings.JetStrandSharpness),
        JetKnotStart = v.Get(BlackHoleSettings.JetKnotStart),
        JetKnotSpacing = v.Get(BlackHoleSettings.JetKnotSpacing),
        JetKnotGrowth = v.Get(BlackHoleSettings.JetKnotGrowth),
        Companion = v.Get(BlackHoleSettings.Companion),
        StreamBrightness = v.Get(BlackHoleSettings.StreamBrightness),
        CompanionDistance = v.Get(BlackHoleSettings.CompanionDistance),
        CompanionSpots = v.Get(BlackHoleSettings.CompanionSpots),
        CompanionGlare = v.Get(BlackHoleSettings.CompanionGlare),
        CompanionAngle = v.Get(BlackHoleSettings.CompanionAngle),
        CompanionHeight = v.Get(BlackHoleSettings.CompanionHeight),
        CompanionRadius = v.Get(BlackHoleSettings.CompanionRadius),
        CompanionTemperature = v.Get(BlackHoleSettings.CompanionTemperature),
        CompanionBrightness = v.Get(BlackHoleSettings.CompanionBrightness),
        CompanionMassRatio = v.Get(BlackHoleSettings.CompanionMassRatio),
        CompanionPeriod = v.Get(BlackHoleSettings.CompanionPeriod),
        StreamTurns = v.Get(BlackHoleSettings.StreamTurns),
        StreamRing = v.Get(BlackHoleSettings.StreamRing),
        StreamSpiralTo = v.Get(BlackHoleSettings.StreamSpiralTo),
        StreamCurve = v.Get(BlackHoleSettings.StreamCurve),
        StreamShape = v.Get(BlackHoleSettings.StreamShape),
        StreamFlatness = v.Get(BlackHoleSettings.StreamFlatness),
        StreamOpacity = v.Get(BlackHoleSettings.StreamOpacity),
        StreamFlowLines = v.Get(BlackHoleSettings.StreamFlowLines),
        StreamFlowScale = v.Get(BlackHoleSettings.StreamFlowScale),
        StreamFray = v.Get(BlackHoleSettings.StreamFray),
        StreamTaper = v.Get(BlackHoleSettings.StreamTaper),
        StreamBridge = v.Get(BlackHoleSettings.StreamBridge),
        StreamHeat = v.Get(BlackHoleSettings.StreamHeat),
        CompanionPull = v.Get(BlackHoleSettings.CompanionPull),
        StreamWidth = v.Get(BlackHoleSettings.StreamWidth),
        StreamColor = v.Get(BlackHoleSettings.StreamColor),
        HotSpot = v.Get(BlackHoleSettings.HotSpot),
        GasType = v.Get(BlackHoleSettings.GasType),
        Texture = v.Get(BlackHoleSettings.Texture),
        TextureMapping = v.Get(BlackHoleSettings.TextureMapping),
        TextureStrength = v.Get(BlackHoleSettings.TextureStrength),
        TextureShape = v.Get(BlackHoleSettings.TextureShape),
        Debris = v.Get(BlackHoleSettings.Debris),
        DiskDrag = v.Get(BlackHoleSettings.DiskDrag),
        DiskDragTimescale = v.Get(BlackHoleSettings.DiskDragTimescale),
        DiskDragMaxAccel = v.Get(BlackHoleSettings.DiskDragMaxAccel),
        DiskBuffeting = v.Get(BlackHoleSettings.DiskBuffeting),
        DiskLight = v.Get(BlackHoleSettings.DiskLight),
        Pulsar = v.Get(BlackHoleSettings.Pulsar),
        BeamPeriod = v.Get(BlackHoleSettings.BeamPeriod),
        BeamTilt = v.Get(BlackHoleSettings.BeamTilt),
        DiskFromTde = v.Get(BlackHoleSettings.DiskFromTde)
    };

    /// <summary>
    /// Every value the settings window can change, as a KSPVE_BLACKHOLE node for PluginData/user.cfg: a whole object, so
    /// one added in game is kept complete.
    /// </summary>
    public CfgNode ToUserCfg()
    {
        CfgNode node = Values();
        if (!string.IsNullOrEmpty(LightSource)) node.Add(BlackHoleSettings.LightSource.Key, LightSource);
        if (!string.IsNullOrEmpty(Gaps)) node.Add(BlackHoleSettings.Gaps.Key, Gaps);
        if (!string.IsNullOrEmpty(StreamShape)) node.Add(BlackHoleSettings.StreamShape.Key, StreamShape);
        if (!string.IsNullOrEmpty(Companion)) node.Add(BlackHoleSettings.Companion.Key, Companion);
        if (!string.IsNullOrEmpty(Texture)) node.Add(BlackHoleSettings.Texture.Key, Texture);
        if (!string.IsNullOrEmpty(GapWidths)) node.Add(BlackHoleSettings.GapWidths.Key, GapWidths);
        if (!string.IsNullOrEmpty(GapDepths)) node.Add(BlackHoleSettings.GapDepths.Key, GapDepths);
        return node;
    }

    private CfgNode Values() => new CfgNode(BlackHoleSettings.Schema.NodeName)
        .Add(BlackHoleSettings.Body.Key, BodyName)
        .Add(BlackHoleSettings.Slot.Key, Slot.ToString(CultureInfo.InvariantCulture))
        .Add(BlackHoleSettings.Kind.Key, Kind.ToString())
        .Add(BlackHoleSettings.Enabled.Key, Bool(Enabled))
        .Add(BlackHoleSettings.HorizonRadius.Key, Num(HorizonRadiusMeters))
        .Add(BlackHoleSettings.BodyIsShadow.Key, Bool(BodyIsShadow))
        .Add(BlackHoleSettings.NormalX.Key, Num(Normal.x))
        .Add(BlackHoleSettings.NormalY.Key, Num(Normal.y))
        .Add(BlackHoleSettings.NormalZ.Key, Num(Normal.z))
        .Add(BlackHoleSettings.BoundRadius.Key, Num(BoundRadius))
        .Add(BlackHoleSettings.Pulsar.Key, Bool(Pulsar))
        .Add(BlackHoleSettings.DiskFromTde.Key, Bool(DiskFromTde))
        .Add(BlackHoleSettings.DiskInner.Key, Num(DiskInner))
        .Add(BlackHoleSettings.StarRadius.Key, Num(StarRadius))
        .Add(BlackHoleSettings.SurfaceTemperature.Key, Num(SurfaceTemperature))
        .Add(BlackHoleSettings.StarTemperature.Key, Num(StarTemperature))
        .Add(BlackHoleSettings.FieldLines.Key, Num(FieldLines))
        .Add(BlackHoleSettings.AccretionColumns.Key, Num(AccretionColumns))
        .Add(BlackHoleSettings.Beams.Key, Num(Beams))
        .Add(BlackHoleSettings.Flaring.Key, Num(Flaring))
        .Add(BlackHoleSettings.Rings.Key, Num(Rings))
        .Add(BlackHoleSettings.RingDepth.Key, Num(RingDepth))
        .Add(BlackHoleSettings.RingWidth.Key, Num(RingWidth))
        .Add(BlackHoleSettings.StarGlow.Key, Num(StarGlow))
        .Add(BlackHoleSettings.Dust.Key, Num(Dust))
        .Add(BlackHoleSettings.LightFalloff.Key, Num(LightFalloff))
        .Add(BlackHoleSettings.DustColor.Key, ColorSetting.Write(DustColor))
        .Add(BlackHoleSettings.OuterDustColor.Key, ColorSetting.Write(OuterDustColor))
        .Add(BlackHoleSettings.RockColor.Key, ColorSetting.Write(RockColor))
        .Add(BlackHoleSettings.DiskTint.Key, Num(DiskTint))
        .Add(BlackHoleSettings.InnerColor.Key, ColorSetting.Write(InnerColor))
        .Add(BlackHoleSettings.OuterColor.Key, ColorSetting.Write(OuterColor))
        .Add(BlackHoleSettings.InnerBrightness.Key, Num(InnerBrightness))
        .Add(BlackHoleSettings.OuterBrightness.Key, Num(OuterBrightness))
        .Add(BlackHoleSettings.JetColor.Key, ColorSetting.Write(JetColor))
        .Add(BlackHoleSettings.PhotonRingColor.Key, ColorSetting.Write(PhotonRingColor))
        .Add(BlackHoleSettings.DensityFalloff.Key, Num(DensityFalloff))
        .Add(BlackHoleSettings.EdgeSoftness.Key, Num(EdgeSoftness))
        .Add(BlackHoleSettings.Clumps.Key, Num(Clumps))
        .Add(BlackHoleSettings.ClumpSize.Key, Num(ClumpSize))
        .Add(BlackHoleSettings.ScatterAsymmetry.Key, Num(ScatterAsymmetry))
        .Add(BlackHoleSettings.RimGlow.Key, Num(RimGlow))
        .Add(BlackHoleSettings.RimTemperature.Key, Num(RimTemperature))
        .Add(BlackHoleSettings.SpiralArms.Key, Num(SpiralArms))
        .Add(BlackHoleSettings.SpiralStrength.Key, Num(SpiralStrength))
        .Add(BlackHoleSettings.SpiralWinding.Key, Num(SpiralWinding))
        .Add(BlackHoleSettings.RockSpread.Key, Num(RockSpread))
        .Add(BlackHoleSettings.RockCollisionMode.Key, RockCollisions.ToString())
        .Add(BlackHoleSettings.SolidRockSpacing.Key, Num(SolidRockSpacing))
        .Add(BlackHoleSettings.SolidRockSize.Key, Num(SolidRockSize))
        .Add(BlackHoleSettings.SolidRockRange.Key, Num(SolidRockRange))
        .Add(BlackHoleSettings.RockSizeSpread.Key, Num(RockSizeSpread))
        .Add(BlackHoleSettings.CloudStretch.Key, Num(CloudStretch))
        .Add(BlackHoleSettings.CloudRagged.Key, Num(CloudRagged))
        .Add(BlackHoleSettings.CloudFill.Key, Num(CloudFill))
        .Add(BlackHoleSettings.ComaBrightness.Key, Num(ComaBrightness))
        .Add(BlackHoleSettings.NucleusBrightness.Key, Num(NucleusBrightness))
        .Add(BlackHoleSettings.IonBrightness.Key, Num(IonBrightness))
        .Add(BlackHoleSettings.DustBrightness.Key, Num(DustBrightness))
        .Add(BlackHoleSettings.IonTailWidth.Key, Num(IonTailWidth))
        .Add(BlackHoleSettings.Streamers.Key, Num(Streamers))
        .Add(BlackHoleSettings.Striae.Key, Num(Striae))
        .Add(BlackHoleSettings.FieldLineCount.Key, Num(FieldLineCount))
        .Add(BlackHoleSettings.FieldShells.Key, Num(FieldShells))
        .Add(BlackHoleSettings.FieldShellRatio.Key, Num(FieldShellRatio))
        .Add(BlackHoleSettings.FieldLineWidth.Key, Num(FieldLineWidth))
        .Add(BlackHoleSettings.ColumnWidth.Key, Num(ColumnWidth))
        .Add(BlackHoleSettings.BeamWidth.Key, Num(BeamWidth))
        .Add(BlackHoleSettings.Hotspots.Key, Num(Hotspots))
        .Add(BlackHoleSettings.DiskDensity.Key, Num(DiskDensity))
        .Add(BlackHoleSettings.DiskOpacity.Key, Num(DiskOpacity))
        .Add(BlackHoleSettings.Emission.Key, Num(Emission))
        .Add(BlackHoleSettings.CloudSize.Key, Num(CloudSize))
        .Add(BlackHoleSettings.SelfShadow.Key, Num(SelfShadow))
        .Add(BlackHoleSettings.InnerLight.Key, Num(InnerLight))
        .Add(BlackHoleSettings.StreakWinding.Key, Num(StreakWinding))
        .Add(BlackHoleSettings.StreakStretch.Key, Num(StreakStretch))
        .Add(BlackHoleSettings.StreakLanes.Key, Num(StreakLanes))
        .Add(BlackHoleSettings.ParticleSpacing.Key, Num(ParticleSpacing))
        .Add(BlackHoleSettings.Rocks.Key, Num(Rocks))
        .Add(BlackHoleSettings.RockSize.Key, Num(RockSize))
        .Add(BlackHoleSettings.RockSpacing.Key, Num(RockSpacing))
        .Add(BlackHoleSettings.RockAlbedo.Key, Num(RockAlbedo))
        .Add(BlackHoleSettings.RockMaxAngle.Key, Num(RockMaxAngle))
        .Add(BlackHoleSettings.BulgeSize.Key, Num(BulgeSize))
        .Add(BlackHoleSettings.BulgeBrightness.Key, Num(BulgeBrightness))
        .Add(BlackHoleSettings.BarLength.Key, Num(BarLength))
        .Add(BlackHoleSettings.StarClusters.Key, Num(StarClusters))
        .Add(BlackHoleSettings.DustLanes.Key, Num(DustLanes))
        .Add(BlackHoleSettings.CloudScale.Key, Num(CloudScale))
        .Add(BlackHoleSettings.CloudWarp.Key, Num(CloudWarp))
        .Add(BlackHoleSettings.Filaments.Key, Num(Filaments))
        .Add(BlackHoleSettings.Shell.Key, Num(Shell))
        .Add(BlackHoleSettings.ColorA.Key, ColorSetting.Write(ColorA))
        .Add(BlackHoleSettings.ColorB.Key, ColorSetting.Write(ColorB))
        .Add(BlackHoleSettings.Activity.Key, Num(Activity))
        .Add(BlackHoleSettings.ComaSize.Key, Num(ComaSize))
        .Add(BlackHoleSettings.ComaColor.Key, ColorSetting.Write(ComaColor))
        .Add(BlackHoleSettings.IonTailLength.Key, Num(IonTailLength))
        .Add(BlackHoleSettings.DustTailLength.Key, Num(DustTailLength))
        .Add(BlackHoleSettings.DustTailCurve.Key, Num(DustTailCurve))
        .Add(BlackHoleSettings.DustTailWidth.Key, Num(DustTailWidth))
        .Add(BlackHoleSettings.PeakTemperature.Key, Num(PeakTemperature))
        .Add(BlackHoleSettings.ColorSaturation.Key, Num(ColorSaturation))
        .Add(BlackHoleSettings.Brightness.Key, Num(Brightness))
        .Add(BlackHoleSettings.Glow.Key, Num(Glow))
        .Add(BlackHoleSettings.PhotonRing.Key, Num(PhotonRing))
        .Add(BlackHoleSettings.RotationSpeed.Key, Num(RotationSpeed))
        .Add(BlackHoleSettings.Spin.Key, Num(Spin))
        .Add(BlackHoleSettings.Turbulence.Key, Num(Turbulence))
        .Add(BlackHoleSettings.DiskStreaks.Key, Num(DiskStreaks))
        .Add(BlackHoleSettings.StreaksFrom.Key, Num(StreaksFrom))
        .Add(BlackHoleSettings.StreaksFull.Key, Num(StreaksFull))
        .Add(BlackHoleSettings.OuterGlow.Key, Num(OuterGlow))
        .Add(BlackHoleSettings.DiskEdge.Key, Num(DiskEdge))
        .Add(BlackHoleSettings.DiskEvenness.Key, Num(DiskEvenness))
        .Add(BlackHoleSettings.DiskThickness.Key, Num(DiskThickness))
        .Add(BlackHoleSettings.DiskOuter.Key, Num(DiskOuter))
        .Add(BlackHoleSettings.Particles.Key, Num(Particles))
        .Add(BlackHoleSettings.Debris.Key, Num(Debris))
        .Add(BlackHoleSettings.Jets.Key, Num(Jets))
        .Add(BlackHoleSettings.JetLength.Key, Num(JetLength))
        .Add(BlackHoleSettings.JetWidth.Key, Num(JetWidth))
        .Add(BlackHoleSettings.JetSpeed.Key, Num(JetSpeed))
        .Add(BlackHoleSettings.JetSpiral.Key, Num(JetSpiral))
        .Add(BlackHoleSettings.JetTurns.Key, Num(JetTurns))
        .Add(BlackHoleSettings.JetFlow.Key, Num(JetFlow))
        .Add(BlackHoleSettings.JetAbsorb.Key, Num(JetAbsorb))
        .Add(BlackHoleSettings.JetKnots.Key, Num(JetKnots))
        .Add(BlackHoleSettings.JetStrands.Key, Num(JetStrands))
        .Add(BlackHoleSettings.JetStrandSharpness.Key, Num(JetStrandSharpness))
        .Add(BlackHoleSettings.JetKnotStart.Key, Num(JetKnotStart))
        .Add(BlackHoleSettings.JetKnotSpacing.Key, Num(JetKnotSpacing))
        .Add(BlackHoleSettings.JetKnotGrowth.Key, Num(JetKnotGrowth))
        .Add(BlackHoleSettings.StreamBrightness.Key, Num(StreamBrightness))
        .Add(BlackHoleSettings.CompanionDistance.Key, Num(CompanionDistance))
        .Add(BlackHoleSettings.CompanionSpots.Key, Num(CompanionSpots))
        .Add(BlackHoleSettings.CompanionGlare.Key, Num(CompanionGlare))
        .Add(BlackHoleSettings.CompanionAngle.Key, Num(CompanionAngle))
        .Add(BlackHoleSettings.CompanionHeight.Key, Num(CompanionHeight))
        .Add(BlackHoleSettings.CompanionRadius.Key, Num(CompanionRadius))
        .Add(BlackHoleSettings.CompanionTemperature.Key, Num(CompanionTemperature))
        .Add(BlackHoleSettings.CompanionBrightness.Key, Num(CompanionBrightness))
        .Add(BlackHoleSettings.CompanionMassRatio.Key, Num(CompanionMassRatio))
        .Add(BlackHoleSettings.CompanionPeriod.Key, Num(CompanionPeriod))
        .Add(BlackHoleSettings.StreamTurns.Key, Num(StreamTurns))
        .Add(BlackHoleSettings.StreamRing.Key, Num(StreamRing))
        .Add(BlackHoleSettings.StreamSpiralTo.Key, Num(StreamSpiralTo))
        .Add(BlackHoleSettings.StreamCurve.Key, Num(StreamCurve))
        .Add(BlackHoleSettings.StreamFlatness.Key, Num(StreamFlatness))
        .Add(BlackHoleSettings.StreamOpacity.Key, Num(StreamOpacity))
        .Add(BlackHoleSettings.StreamFlowLines.Key, Num(StreamFlowLines))
        .Add(BlackHoleSettings.StreamFlowScale.Key, Num(StreamFlowScale))
        .Add(BlackHoleSettings.StreamFray.Key, Num(StreamFray))
        .Add(BlackHoleSettings.StreamTaper.Key, Num(StreamTaper))
        .Add(BlackHoleSettings.StreamBridge.Key, Num(StreamBridge))
        .Add(BlackHoleSettings.StreamHeat.Key, Num(StreamHeat))
        .Add(BlackHoleSettings.CompanionPull.Key, Num(CompanionPull))
        .Add(BlackHoleSettings.StreamWidth.Key, Num(StreamWidth))
        .Add(BlackHoleSettings.StreamColor.Key, ColorSetting.Write(StreamColor))
        .Add(BlackHoleSettings.HotSpot.Key, Num(HotSpot))
        .Add(BlackHoleSettings.GasType.Key, GasType.ToString())
        .Add(BlackHoleSettings.TextureMapping.Key, TextureMapping.ToString())
        .Add(BlackHoleSettings.TextureStrength.Key, Num(TextureStrength))
        .Add(BlackHoleSettings.TextureShape.Key, Num(TextureShape))
        .Add(BlackHoleSettings.DiskLight.Key, Num(DiskLight))
        .Add(BlackHoleSettings.DiskDrag.Key, Bool(DiskDrag))
        .Add(BlackHoleSettings.DiskDragMaxAccel.Key, Num(DiskDragMaxAccel))
        .Add(BlackHoleSettings.DiskDragTimescale.Key, Num(DiskDragTimescale))
        .Add(BlackHoleSettings.DiskBuffeting.Key, Num(DiskBuffeting))
        .Add(BlackHoleSettings.BeamPeriod.Key, Num(BeamPeriod))
        .Add(BlackHoleSettings.BeamTilt.Key, Num(BeamTilt))
        .Add(BlackHoleSettings.Steps.Key, Steps.ToString(CultureInfo.InvariantCulture));

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Bool(bool v) => v ? "True" : "False";

    /// <summary>
    /// Keeps values typed in the settings window to what the maths needs, and nothing more: the jets below light speed,
    /// the disk outside the horizon, the 0..1 blends within 0..1, amounts not negative. There is no other limit.
    /// </summary>
    public void KeepPhysical()
    {
        JetSpeed = Math.Min(Math.Max(JetSpeed, 0.0), 0.999999);
        DiskInner = Math.Max(DiskInner, 1.0);
        DiskOuter = Math.Max(DiskOuter, DiskInner + 0.1);
        DiskThickness = Math.Max(DiskThickness, 0.0001);
        JetSpiral = Math.Min(Math.Max(JetSpiral, 0.0), 1.0);
        DiskStreaks = Math.Min(Math.Max(DiskStreaks, 0.0), 1.0);
        StreaksFrom = Math.Min(Math.Max(StreaksFrom, 0.0), 89.0);
        StreaksFull = Math.Min(Math.Max(StreaksFull, StreaksFrom + 1.0), 90.0);
        OuterGlow = Math.Max(OuterGlow, 0.0);
        DiskEdge = Math.Min(Math.Max(DiskEdge, 0.0), 1.0);
        DiskEvenness = Math.Min(Math.Max(DiskEvenness, 0.0), 1.0);
        PeakTemperature = Math.Max(PeakTemperature, 1.0);
        JetLength = Math.Max(JetLength, 0.1);
        JetWidth = Math.Max(JetWidth, 0.0001);
        DiskDragTimescale = Math.Max(DiskDragTimescale, 0.0001);
        HorizonRadiusMeters = Math.Max(HorizonRadiusMeters, 0.0);
        Steps = Math.Max(1, Math.Min(Steps, 1000000));
        Brightness = Math.Max(Brightness, 0.0);
        ColorSaturation = Math.Max(ColorSaturation, 0.0);
        Glow = Math.Max(Glow, 0.0);
        PhotonRing = Math.Max(PhotonRing, 0.0);
        RotationSpeed = Math.Max(RotationSpeed, 0.0);
        Turbulence = Math.Max(Turbulence, 0.0);
        Particles = Math.Max(Particles, 0.0);
        Debris = Math.Max(Debris, 0.0);
        Jets = Math.Max(Jets, 0.0);
        JetTurns = Math.Max(JetTurns, 0.0);
        JetFlow = Math.Max(JetFlow, 0.0);
        JetAbsorb = Math.Max(JetAbsorb, 0.0);
        JetKnots = Math.Min(Math.Max(JetKnots, 0.0), 1.0);
        JetStrands = Math.Min(Math.Max(JetStrands, 1.0), 200.0);
        JetStrandSharpness = Math.Min(Math.Max(JetStrandSharpness, 0.1), 100.0);
        JetKnotStart = Math.Min(Math.Max(JetKnotStart, 0.0), 1.0);
        JetKnotSpacing = Math.Max(JetKnotSpacing, 0.001);
        JetKnotGrowth = Math.Max(JetKnotGrowth, 0.0);
        StreamBrightness = Math.Max(StreamBrightness, 0.0);
        CompanionDistance = Math.Max(CompanionDistance, 0.0);
        CompanionSpots = Math.Min(Math.Max(CompanionSpots, 0.0), 1.0);
        CompanionGlare = Math.Max(CompanionGlare, 0.0);
        CompanionHeight = Math.Min(Math.Max(CompanionHeight, -90.0), 90.0);
        CompanionRadius = Math.Max(CompanionRadius, 0.0);
        CompanionTemperature = Math.Max(CompanionTemperature, 1.0);
        CompanionBrightness = Math.Max(CompanionBrightness, 0.0);
        CompanionMassRatio = Math.Max(CompanionMassRatio, 1e-6);
        CompanionPeriod = Math.Max(CompanionPeriod, 0.0);
        StreamTurns = Math.Min(Math.Max(StreamTurns, 0.0), 16.0);
        StreamRing = Math.Max(StreamRing, 0.0);
        StreamSpiralTo = Math.Min(Math.Max(StreamSpiralTo, 0.0), 1.0);
        StreamCurve = Math.Min(Math.Max(StreamCurve, 0.0), RocheStream.MaxSpiralCurve);
        StreamFlatness = Math.Min(Math.Max(StreamFlatness, 1.0), 20.0);
        StreamOpacity = Math.Min(Math.Max(StreamOpacity, 0.0), 50.0);
        StreamFlowLines = Math.Min(Math.Max(StreamFlowLines, 0.0), 1.0);
        StreamFlowScale = Math.Min(Math.Max(StreamFlowScale, 0.05), 20.0);
        StreamFray = Math.Min(Math.Max(StreamFray, 0.0), 2.0);
        StreamTaper = Math.Min(Math.Max(StreamTaper, 0.05), 3.0);
        StreamBridge = Math.Min(Math.Max(StreamBridge, 0.0), 2.0);
        StreamHeat = Math.Min(Math.Max(StreamHeat, 0.0), 10.0);
        CompanionPull = Math.Min(Math.Max(CompanionPull, 0.0), 1.0);
        StreamWidth = Math.Min(Math.Max(StreamWidth, 0.0001), 1.0);
        StreamColor = NonNegative(StreamColor);
        HotSpot = Math.Max(HotSpot, 0.0);
        TextureStrength = Math.Min(Math.Max(TextureStrength, 0.0), 1.0);
        TextureShape = Math.Min(Math.Max(TextureShape, 0.0), 1.0);
        DiskLight = Math.Max(DiskLight, 0.0);
        DiskDragMaxAccel = Math.Max(DiskDragMaxAccel, 0.0);
        DiskBuffeting = Math.Max(DiskBuffeting, 0.0);
        BeamPeriod = Math.Max(BeamPeriod, 0.0);
        BeamTilt = Math.Min(Math.Max(BeamTilt, 0.0), 180.0);
        BoundRadius = Math.Max(BoundRadius, 0.0);
        StarRadius = Math.Max(StarRadius, 1.01);
        SurfaceTemperature = Math.Max(SurfaceTemperature, 1.0);
        StarTemperature = Math.Max(StarTemperature, 1.0);
        FieldLines = Math.Max(FieldLines, 0.0);
        AccretionColumns = Math.Max(AccretionColumns, 0.0);
        Beams = Math.Max(Beams, 0.0);
        Flaring = Math.Max(Flaring, 0.0);
        Rings = Math.Min(Math.Max(Rings, 0.0), 16.0);
        RingDepth = Math.Min(Math.Max(RingDepth, 0.0), 1.0);
        RingWidth = Math.Max(RingWidth, 0.0001);
        StarGlow = Math.Max(StarGlow, 0.0);
        Dust = Math.Max(Dust, 0.0);
        LightFalloff = Math.Max(LightFalloff, 0.0);
        Rocks = Math.Min(Math.Max(Rocks, 0.0), 1.0);
        RockSize = Math.Max(RockSize, 0.0);
        RockSpacing = Math.Max(RockSpacing, 1e-7);
        RockAlbedo = Math.Max(RockAlbedo, 0.0);
        RockMaxAngle = Math.Min(Math.Max(RockMaxAngle, 0.0), 180.0);
        BulgeSize = Math.Max(BulgeSize, 0.0001);
        BulgeBrightness = Math.Max(BulgeBrightness, 0.0);
        BarLength = Math.Max(BarLength, 0.0);
        StarClusters = Math.Max(StarClusters, 0.0);
        DustLanes = Math.Max(DustLanes, 0.0);
        CloudScale = Math.Max(CloudScale, 0.0001);
        CloudWarp = Math.Max(CloudWarp, 0.0);
        Filaments = Math.Max(Filaments, 0.0);
        Shell = Math.Min(Math.Max(Shell, 0.0), 1.0);
        Activity = Math.Max(Activity, 0.0);
        ComaSize = Math.Max(ComaSize, 0.0001);
        IonTailLength = Math.Max(IonTailLength, 0.0001);
        DustTailLength = Math.Max(DustTailLength, 0.0001);
        DustTailCurve = Math.Max(DustTailCurve, 0.0);
        DustTailWidth = Math.Max(DustTailWidth, 0.0);
        DustColor = NonNegative(DustColor);
        ColorA = NonNegative(ColorA);
        ColorB = NonNegative(ColorB);
        ComaColor = NonNegative(ComaColor);
        OuterDustColor = NonNegative(OuterDustColor);
        RockColor = NonNegative(RockColor);
        InnerColor = NonNegative(InnerColor);
        OuterColor = NonNegative(OuterColor);
        JetColor = NonNegative(JetColor);
        PhotonRingColor = NonNegative(PhotonRingColor);
        DiskTint = Math.Min(Math.Max(DiskTint, 0.0), 1.0);
        InnerBrightness = Math.Max(InnerBrightness, 0.0);
        OuterBrightness = Math.Max(OuterBrightness, 0.0);
        DensityFalloff = Math.Max(DensityFalloff, 0.0);
        EdgeSoftness = Math.Max(EdgeSoftness, 0.01);
        Clumps = Math.Max(Clumps, 0.0);
        ClumpSize = Math.Max(ClumpSize, 0.001);
        ScatterAsymmetry = Math.Min(Math.Max(ScatterAsymmetry, -0.99), 0.99);
        RimGlow = Math.Max(RimGlow, 0.0);
        RimTemperature = Math.Max(RimTemperature, 1.0);
        SpiralArms = Math.Max(SpiralArms, 0.0);
        SpiralStrength = Math.Max(SpiralStrength, 0.0);
        SpiralWinding = Math.Min(Math.Max(SpiralWinding, -100.0), 100.0);
        RockSpread = Math.Max(RockSpread, 0.0);
        RockSizeSpread = Math.Max(RockSizeSpread, 0.0);
        SolidRockSpacing = Math.Min(Math.Max(SolidRockSpacing, 10.0), 1e9);
        SolidRockSize = Math.Min(Math.Max(SolidRockSize, 1.0), 1e6);
        SolidRockRange = Math.Min(Math.Max(SolidRockRange, 1000.0), 200000.0);
        CloudStretch = Math.Max(CloudStretch, 0.01);
        CloudRagged = Math.Max(CloudRagged, 0.0);
        CloudFill = Math.Min(Math.Max(CloudFill, -10.0), 10.0);
        ComaBrightness = Math.Max(ComaBrightness, 0.0);
        NucleusBrightness = Math.Max(NucleusBrightness, 0.0);
        IonBrightness = Math.Max(IonBrightness, 0.0);
        DustBrightness = Math.Max(DustBrightness, 0.0);
        IonTailWidth = Math.Max(IonTailWidth, 0.0);
        Streamers = Math.Max(Streamers, 0.0);
        Striae = Math.Max(Striae, 0.0);
        FieldLineCount = Math.Max(FieldLineCount, 1.0);
        FieldShells = Math.Max(FieldShells, 1.0);
        FieldShellRatio = Math.Max(FieldShellRatio, 1.01);
        FieldLineWidth = Math.Max(FieldLineWidth, 0.0001);
        ColumnWidth = Math.Max(ColumnWidth, 0.001);
        BeamWidth = Math.Max(BeamWidth, 0.0);
        Hotspots = Math.Max(Hotspots, 0.0);
        DiskDensity = Math.Max(DiskDensity, 0.0);
        DiskOpacity = Math.Max(DiskOpacity, 0.0);
        Emission = Math.Max(Emission, 0.0);
        CloudSize = Math.Max(CloudSize, 0.0001);
        SelfShadow = Math.Max(SelfShadow, 0.0);
        InnerLight = Math.Max(InnerLight, 0.0);
        StreakWinding = Math.Min(Math.Max(StreakWinding, -1000.0), 1000.0);
        StreakStretch = Math.Max(StreakStretch, 0.0);
        StreakLanes = Math.Max(StreakLanes, 0.0);
        ParticleSpacing = Math.Max(ParticleSpacing, 0.01);
        SpiralStrength = Math.Min(SpiralStrength, 1.0);
        StreakLanes = Math.Min(StreakLanes, 1.0);
        SpiralArms = Math.Min(SpiralArms, 16.0);
        FieldLineCount = Math.Min(FieldLineCount, 64.0);
        FieldShells = Math.Min(FieldShells, 16.0);
    }

    private static Double3 NonNegative(Double3 c) => new Double3(Math.Max(c.X, 0.0), Math.Max(c.Y, 0.0), Math.Max(c.Z, 0.0));

    /// <summary>
    /// The length unit of the object's sizes in metres: the event horizon radius for a black hole (from the body's radius
    /// unless horizonRadius is set), and SpaceObjects.UnitMeters for the other kinds.
    /// </summary>
    public double HorizonMeters(CelestialBody body) => SpaceObjects.UnitMeters(Kind, HorizonRadiusMeters, body.Radius, BodyIsShadow, StarRadius);

    /// <summary>A black hole (the other kinds neither drag nor light vessels, nor host a tidal disruption).</summary>
    public bool IsBlackHole => Kind == SpaceObjectKind.BlackHole;

    /// <summary>What the settings window calls it: the body, its kind unless a black hole, and its slot past the first.</summary>
    public string Title => BodyName + (IsBlackHole ? string.Empty : " " + SpaceObjects.KindName(Kind)) + (Slot > 0 ? " #" + (Slot + 1) : string.Empty);

    /// <summary>
    /// The body, looked up once the planetary system exists (null at the main menu). With findByMass it is the most
    /// massive body of at least minMass, whatever planet packs call it (Kcalbeloh with DistanceFactor 0 or Principia
    /// becomes the root "Sun", and under Real Solar System or Sol the home star is "Sun" too). Otherwise, or when no
    /// body is that heavy, it is found by internal name, then by display name.
    /// </summary>
    public CelestialBody Body
    {
        get
        {
            if (_body != null) return _body;
            if (FlightGlobals.fetch == null) return null;
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            if (FindByMass)
            {
                var masses = new List<double>(bodies.Count);
                foreach (CelestialBody b in bodies) masses.Add(b != null ? b.Mass : 0.0);
                int heaviest = BlackHoleSettings.HeaviestAbove(masses, MinMass);
                if (heaviest >= 0)
                {
                    _body = bodies[heaviest];
                    Log.Info($"Black hole {BodyName} is body '{_body.bodyName}' ({_body.Mass:0.###e+0} kg, the most massive body).");
                    return _body;
                }
            }
            _body = bodies.Find(b => b != null && b.bodyName == BodyName);
            if (_body == null)
            {
                _body = bodies.Find(b => b != null && DisplayName(b) == BodyName);
                if (_body != null) Log.Info($"Black hole {BodyName} found as body '{_body.bodyName}' (by its display name).");
            }
            return _body;
        }
    }

    private static string DisplayName(CelestialBody body) =>
        string.IsNullOrEmpty(body.bodyDisplayName) ? string.Empty : Localizer.Format(body.bodyDisplayName).LocalizeRemoveGender().Trim();
}

/// <summary>Loads KSPVE_BLACKHOLE nodes and keeps a <see cref="BlackHoleRenderer"/> on the ScaledSpace camera.</summary>
internal static class BlackHoles
{
    /// <summary>
    /// A star: KSP's flag, or (should a planet pack leave it unset) a scaled body drawn with the star shader, as the Sun's
    /// and Kopernicus' stars are.
    /// </summary>
    public static bool IsStar(CelestialBody b) =>
        b != null && (b.isStar || (b.scaledBody != null && b.scaledBody.GetComponent<SunShaderController>() != null));

    /// <summary>
    /// The star lighting a comet: its lightSource body, or the nearest star it orbits (up the chain of parents), or the
    /// root of the system.
    /// </summary>
    public static CelestialBody LightSourceOf(BlackHoleDefinition hole, CelestialBody body)
    {
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        if (!string.IsNullOrEmpty(hole.LightSource))
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] != null && bodies[i].bodyName == hole.LightSource) return bodies[i];
        }
        CelestialBody parent = body.referenceBody;
        for (int guard = 0; parent != null && parent != body && guard < 64; guard++)
        {
            if (IsStar(parent)) return parent;
            if (parent.referenceBody == parent) return parent;   // the root
            parent = parent.referenceBody;
        }
        return Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
    }

    private const string ScaledCameraName = "Camera ScaledSpace";
    private static Camera[] _cameras = new Camera[32];

    public static readonly List<BlackHoleDefinition> Definitions = new List<BlackHoleDefinition>();

    /// <summary>The procedural rules: GameData's KSPVE_PROCEDURAL with the player's changes (ProceduralEdited) over it.</summary>
    public static SettingsValues ProceduralRules = ProceduralObjects.Schema.Resolve(null, new List<ConfigIssue>());
    public static CfgNode ProceduralNode = new CfgNode(ProceduralObjects.Schema.NodeName);
    public static bool ProceduralEdited;
    private static bool _proceduralPlanned;
    private static float _proceduralChanged = -1f;

    public static void Load(List<ConfigIssue> issues)
    {
        Definitions.Clear();
        CfgNode user = null;
        try
        {
            user = KspConfig.LoadUserNodes().Find(n => n.Name == BlackHoleSettings.UserNodeName);
        }
        catch (Exception ex)
        {
            Log.Exception("Reading black hole tweaks from user.cfg failed; using the configs as they are", ex);
        }

        // The procedural rules (the last KSPVE_PROCEDURAL node), with the player's changes over them.
        ProceduralNode = new CfgNode(ProceduralObjects.Schema.NodeName);
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(ProceduralObjects.Schema.NodeName))
            ProceduralNode = CfgNode.Overlay(ProceduralNode, KspConfig.ToCfg(node));
        CfgNode userRules = user?.Children.Find(c => c.Name == ProceduralObjects.Schema.NodeName);
        ProceduralEdited = userRules != null;
        if (userRules != null) ProceduralNode = CfgNode.Overlay(ProceduralNode, userRules);
        ProceduralRules = ProceduralObjects.Schema.Resolve(ProceduralNode, issues);
        _proceduralPlanned = false;

        // Each body's objects are numbered (slots) in config order; the player's tweaks match an object by body and slot.
        var slots = new Dictionary<string, int>();
        var configured = new HashSet<string>();
        foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(BlackHoleSettings.Schema.NodeName))
        {
            CfgNode config = KspConfig.ToCfg(node);
            string body = config.GetLast(BlackHoleSettings.Body.Key)?.Trim();
            if (string.IsNullOrEmpty(body)) continue;
            int slot = slots.TryGetValue(body, out int taken) ? taken : 0;
            slots[body] = slot + 1;
            config.SetValue(BlackHoleSettings.Slot.Key, slot.ToString(CultureInfo.InvariantCulture));
            CfgNode merged = BlackHoleSettings.ApplyUserOverrides(config, slot, user);
            BlackHoleDefinition definition = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(merged, issues));
            definition.ConfigNode = config;
            definition.Edited = !ReferenceEquals(merged, config);
            Definitions.Add(definition);
            configured.Add(body + "#" + slot);
        }
        // Objects the player added in the settings window.
        if (user != null)
        {
            foreach (CfgNode child in user.Children)
            {
                string body = child.GetLast(BlackHoleSettings.Body.Key)?.Trim();
                if (child.Name != BlackHoleSettings.Schema.NodeName || string.IsNullOrEmpty(body)) continue;
                if (!configured.Add(body + "#" + BlackHoleSettings.SlotOf(child))) continue;
                BlackHoleDefinition added = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(child, issues));
                added.AddedInGame = true;
                added.Edited = true;
                Definitions.Add(added);
            }
        }
        if (Definitions.Count > 0)
            Log.Info($"Black holes and other objects configured: {string.Join(", ", Definitions.ConvertAll(d => d.Title).ToArray())}.");
    }

    /// <summary>Adds an object of <paramref name="kind"/> round <paramref name="body"/> (kept in user.cfg once saved).</summary>
    public static BlackHoleDefinition Add(string body, SpaceObjectKind kind)
    {
        int slot = 0;
        foreach (BlackHoleDefinition d in Definitions)
            if (d.BodyName == body) slot = Math.Max(slot, d.Slot + 1);
        CfgNode node = new CfgNode(BlackHoleSettings.Schema.NodeName)
            .Add(BlackHoleSettings.Body.Key, body)
            .Add(BlackHoleSettings.Slot.Key, slot.ToString(CultureInfo.InvariantCulture))
            .Add(BlackHoleSettings.Kind.Key, kind.ToString());
        BlackHoleDefinition added = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>()));
        added.AddedInGame = true;
        added.Edited = true;
        Definitions.Add(added);
        return added;
    }

    /// <summary>Removes an object added in game (not saved until Save).</summary>
    public static void Remove(int index)
    {
        if (Definitions[index].AddedInGame) Definitions.RemoveAt(index);
    }

    /// <summary>
    /// Turns an object into another kind, starting from that kind's settings (SpaceObjects.KindDefaults); it keeps its
    /// body, slot and orientation.
    /// </summary>
    public static void ChangeKind(int index, SpaceObjectKind kind)
    {
        BlackHoleDefinition old = Definitions[index];
        CfgNode node = new CfgNode(BlackHoleSettings.Schema.NodeName)
            .Add(BlackHoleSettings.Body.Key, old.BodyName)
            .Add(BlackHoleSettings.Slot.Key, old.Slot.ToString(CultureInfo.InvariantCulture))
            .Add(BlackHoleSettings.Kind.Key, kind.ToString())
            .Add(BlackHoleSettings.NormalX.Key, old.Normal.x.ToString("R", CultureInfo.InvariantCulture))
            .Add(BlackHoleSettings.NormalY.Key, old.Normal.y.ToString("R", CultureInfo.InvariantCulture))
            .Add(BlackHoleSettings.NormalZ.Key, old.Normal.z.ToString("R", CultureInfo.InvariantCulture));
        BlackHoleDefinition changed = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>()));
        changed.ConfigNode = old.ConfigNode;
        changed.AddedInGame = old.AddedInGame;
        changed.Edited = true;
        Definitions[index] = changed;
    }

    /// <summary>
    /// The player's tweaks for user.cfg: one child per edited object (procedural ones only once kept), and the procedural
    /// rules if changed (an empty node clears old tweaks).
    /// </summary>
    public static CfgNode UserNode()
    {
        var container = new CfgNode(BlackHoleSettings.UserNodeName);
        foreach (BlackHoleDefinition hole in Definitions)
            if (hole.Edited && !hole.Procedural) container.AddChild(hole.ToUserCfg());
        if (ProceduralEdited) container.AddChild(ProceduralNode.Clone());
        return container;
    }

    /// <summary>Changes one procedural rule (by key); the objects are planned again once the player stops changing them.</summary>
    public static void SetProceduralRule(string key, string value)
    {
        ProceduralNode.SetValue(key, value);
        ProceduralEdited = true;
        ProceduralRules = ProceduralObjects.Schema.Resolve(ProceduralNode, new List<ConfigIssue>());
        _proceduralPlanned = false;
        _proceduralChanged = Time.realtimeSinceStartup;
    }

    /// <summary>
    /// Replaces the procedural objects with a fresh plan from the rules and the game's bodies (a star's planets are the
    /// bodies orbiting it; their orbits' plane is the disks' plane).
    /// </summary>
    public static void PlanProcedural()
    {
        Definitions.RemoveAll(d => d.Procedural);
        if (FlightGlobals.fetch == null || !ProceduralRules.Get(ProceduralObjects.Enabled))
        {
            _proceduralPlanned = true;
            return;
        }
        // The bodies' orbits need the planetarium: plan in a game scene (it is tried again until then).
        if (!HighLogic.LoadedSceneIsGame || Planetarium.fetch == null) return;
        _proceduralPlanned = true;
        var bodies = new List<SystemBody>();
        foreach (CelestialBody b in FlightGlobals.Bodies)
        {
            if (b == null) continue;
            bool orbits = b.orbit != null && b.referenceBody != null && b.referenceBody != b;
            bodies.Add(new SystemBody
            {
                Name = b.bodyName,
                Parent = orbits ? b.referenceBody.bodyName : string.Empty,
                IsStar = IsStar(b),
                Radius = b.Radius,
                SemiMajorAxis = orbits ? b.orbit.semiMajorAxis : 0.0,
                Eccentricity = orbits ? b.orbit.eccentricity : 0.0,
                OrbitNormal = orbits ? OrbitNormal(b) : Double3.Zero
            });
        }
        var taken = new HashSet<string>();
        foreach (BlackHoleDefinition d in Definitions)
            taken.Add((d.Body != null ? d.Body.bodyName : d.BodyName) + "|" + d.Kind);
        List<CfgNode> plan = ProceduralObjects.Plan(bodies, ProceduralRules, taken);
        foreach (CfgNode node in plan)
        {
            string body = node.GetLast(BlackHoleSettings.Body.Key);
            int slot = 0;
            foreach (BlackHoleDefinition d in Definitions)
                if (d.BodyName == body) slot = Math.Max(slot, d.Slot + 1);
            node.SetValue(BlackHoleSettings.Slot.Key, slot.ToString(CultureInfo.InvariantCulture));
            BlackHoleDefinition placed = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(node, new List<ConfigIssue>()));
            placed.Procedural = true;
            Definitions.Add(placed);
        }
        Log.Info($"Procedural objects: {plan.Count} placed ({string.Join(", ", plan.ConvertAll(n => n.GetLast("body") + " " + n.GetLast("kind")).ToArray())}).");
    }

    /// <summary>The plane of a body's orbit (inertial frame), from where it is now and a quarter-orbit on.</summary>
    private static Double3 OrbitNormal(CelestialBody b)
    {
        double ut = Planetarium.GetUniversalTime();
        double dt = b.orbit.period > 0.0 && !double.IsInfinity(b.orbit.period) ? 0.25 * b.orbit.period : 86400.0;
        Vector3d r0 = b.getTruePositionAtUT(ut) - b.referenceBody.getTruePositionAtUT(ut);
        Vector3d r1 = b.getTruePositionAtUT(ut + dt) - b.referenceBody.getTruePositionAtUT(ut + dt);
        Vector3d n = Vector3d.Cross(r0, r1);
        if (n.sqrMagnitude < 1e-30) return Double3.Zero;
        Vector3d inertial = QuaternionD.Inverse(Planetarium.Rotation) * n.normalized;
        return new Double3(inertial.x, inertial.y, inertial.z);
    }

    /// <summary>A body by its name, or null.</summary>
    public static CelestialBody BodyNamed(string name)
    {
        if (string.IsNullOrEmpty(name) || FlightGlobals.fetch == null) return null;
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i] != null && bodies[i].bodyName == name) return bodies[i];
        return null;
    }

    /// <summary>Keeps a procedural object: it becomes one added in game, saved and editable like the rest.</summary>
    public static void Keep(int index)
    {
        BlackHoleDefinition d = Definitions[index];
        if (!d.Procedural) return;
        d.Procedural = false;
        d.AddedInGame = true;
        d.Edited = true;
    }

    /// <summary>Throws away the player's tweaks to one hole, back to its GameData config (not saved until Save).</summary>
    public static void ResetToConfig(int index)
    {
        BlackHoleDefinition hole = Definitions[index];
        if (hole.ConfigNode == null) return;
        BlackHoleDefinition reset = BlackHoleDefinition.From(BlackHoleSettings.ResolveWithKind(hole.ConfigNode, new List<ConfigIssue>()));
        reset.ConfigNode = hole.ConfigNode;
        Definitions[index] = reset;
    }

    /// <summary>
    /// The hole's disk and jets as they are now: as configured, or, with diskFromTde, as much of them as a tidal
    /// disruption has built (none without one).
    /// </summary>
    public static DiskState StateOf(BlackHoleDefinition hole) => hole.DiskFromTde && hole.IsBlackHole
        ? DiskState.Forming(TidalDisruptions.FrameFor(hole), hole.DiskInner, hole.DiskOuter, hole.DiskThickness, hole.Jets, hole.JetLength)
        : DiskState.Configured(hole.DiskInner, hole.DiskOuter, hole.DiskThickness, hole.Jets, hole.JetLength);

    public static void Update(PostProfile profile, bool allowed)
    {
        // Procedural objects are planned once the game's bodies exist.
        if (!_proceduralPlanned && Time.realtimeSinceStartup > _proceduralChanged + 0.3f
            && FlightGlobals.fetch != null && FlightGlobals.Bodies != null && FlightGlobals.Bodies.Count > 0)
        {
            try { PlanProcedural(); }
            catch (Exception ex) { Log.Exception("Planning procedural objects failed", ex); }
        }
        if (Definitions.Count == 0) return;
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || cam.name != ScaledCameraName) continue;
            bool enabled = allowed && profile.BlackHoles;
            var renderer = cam.GetComponent<BlackHoleRenderer>();
            if (renderer == null)
            {
                if (!enabled) return;
                renderer = cam.gameObject.AddComponent<BlackHoleRenderer>();
                Log.Info("Black hole renderer attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
            return;
        }
    }
}
