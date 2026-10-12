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
    public static readonly DoubleSetting HorizonRadius = Schema.Add(new DoubleSetting("horizonRadius", 0.0, 0.0, 1e30, "Event horizon radius in metres; 0 takes it from the body's radius (see bodyIsShadow)."));
    public static readonly BoolSetting BodyIsShadow = Schema.Add(new BoolSetting("bodyIsShadow", false, "With horizonRadius = 0: True draws the black hole exactly the body's size (the body's radius is the shadow's, so the horizon is 1/2.6 of it), as packs that size the body as the visible hole intend; False makes the body's radius the event horizon, whose shadow looks 2.6x bigger, as a real black hole's does."));
    public static readonly DoubleSetting DiskInner = Schema.Add(new DoubleSetting("diskInner", 3.0, 1.0, 1e9, "Disk inner edge in horizon radii (3 = innermost stable circular orbit)."));
    public static readonly DoubleSetting DiskOuter = Schema.Add(new DoubleSetting("diskOuter", 14.0, 1.0, 1e9, "Disk outer edge in horizon radii."));
    public static readonly DoubleSetting DiskThickness = Schema.Add(new DoubleSetting("diskThickness", 0.12, 0.0001, 100.0, "Disk half-thickness as a fraction of radius."));
    public static readonly DoubleSetting PeakTemperature = Schema.Add(new DoubleSetting("peakTemperature", 6000.0, 1.0, 1e9, "Hottest disk temperature in kelvin, before relativistic shifts."));
    public static readonly DoubleSetting Brightness = Schema.Add(new DoubleSetting("brightness", 0.9, 0.0, 1e9, "Disk brightness."));
    public static readonly DoubleSetting RotationSpeed = Schema.Add(new DoubleSetting("rotationSpeed", 1.0, 0.0, 1e9, "Spin speed of the disk; 1 turns it once every ~8 s (the whole disk turns together)."));
    public static readonly DoubleSetting DiskStreaks = Schema.Add(new DoubleSetting("diskStreaks", 0.0, 0.0, 1.0, "Seen from above, the disk's gas drawn out along its orbit into long swirling streaks (the inner disk orbits faster and shears it): 0 keeps the cloudy disk from every side, 1 gives full streaks looking straight down. They fade in from 10 degrees above the disk; from the side it is unchanged."));
    public static readonly DoubleSetting DiskTint = Schema.Add(new DoubleSetting("diskTint", 0.0, 0.0, 1.0, "The disk's colours by hand: 0 = physical (the gas's blackbody colour at its temperature), 1 = innerColor at the inner edge shading to outerColor at the outer, keeping how bright each part is."));
    public static readonly ColorSetting InnerColor = Schema.Add(new ColorSetting("innerColor", new Double3(1.0, 0.85, 0.6), "With diskTint: the colour of the inner disk (and the glow round the hole), r, g, b."));
    public static readonly ColorSetting OuterColor = Schema.Add(new ColorSetting("outerColor", new Double3(1.0, 0.45, 0.15), "With diskTint: the colour of the outer disk, r, g, b."));
    public static readonly DoubleSetting InnerBrightness = Schema.Add(new DoubleSetting("innerBrightness", 1.0, 0.0, 1e6, "Brightness of the inner disk (it shades to outerBrightness at the outer edge)."));
    public static readonly DoubleSetting OuterBrightness = Schema.Add(new DoubleSetting("outerBrightness", 1.0, 0.0, 1e6, "Brightness of the outer disk."));
    public static readonly ColorSetting JetColor = Schema.Add(new ColorSetting("jetColor", new Double3(0.3, 0.55, 1.0), "Colour of the jets (a neutron star's beams too), r, g, b; their brightest parts go white."));
    public static readonly ColorSetting PhotonRingColor = Schema.Add(new ColorSetting("photonRingColor", new Double3(0.9, 0.95, 1.0), "Colour of the photon ring, r, g, b."));
    public static readonly DoubleSetting OuterGlow = Schema.Add(new DoubleSetting("outerGlow", 0.0, 0.0, 1e6, "Display aid for big disks: beyond 20 horizon radii the disk is shown as if exposed for it, lit by the inner disk (warmed by its light, its inverse-square falloff made up, scattering light in the inner disk's colour). 0 is physical: the far disk is too dim to see."));
    public static readonly DoubleSetting Turbulence = Schema.Add(new DoubleSetting("turbulence", 0.8, 0.0, 100.0, "Clumpy, streaky structure in the disk."));
    public static readonly DoubleSetting Spin = Schema.Add(new DoubleSetting("spin", 1.0, -1.0, 1.0, "Orbit direction about the normal: positive prograde, negative retrograde."));
    public static readonly DoubleSetting NormalX = Schema.Add(new DoubleSetting("normalX", 0.0, -1e6, 1e6, "Disk normal, X (inertial frame)."));
    public static readonly DoubleSetting NormalY = Schema.Add(new DoubleSetting("normalY", 1.0, -1e6, 1e6, "Disk normal, Y (inertial frame)."));
    public static readonly DoubleSetting NormalZ = Schema.Add(new DoubleSetting("normalZ", 0.0, -1e6, 1e6, "Disk normal, Z (inertial frame)."));
    public static readonly DoubleSetting ColorSaturation = Schema.Add(new DoubleSetting("colorSaturation", 2.2, 0.0, 1e6, "Colour vividness; 1 is the plain blackbody colour."));
    public static readonly DoubleSetting Glow = Schema.Add(new DoubleSetting("glow", 1.0, 0.0, 1e9, "Glow of hot plasma around the hole."));
    public static readonly DoubleSetting PhotonRing = Schema.Add(new DoubleSetting("photonRing", 1.0, 0.0, 1e9, "Brightness of the thin photon ring at the edge of the shadow."));
    public static readonly DoubleSetting Particles = Schema.Add(new DoubleSetting("particles", 1.0, 0.0, 1e6, "Sparks of white-hot matter riding in the disk."));
    public static readonly DoubleSetting Debris = Schema.Add(new DoubleSetting("debris", 1.0, 0.0, 1e6, "Dark chunks of debris riding in the disk."));
    public static readonly DoubleSetting Jets = Schema.Add(new DoubleSetting("jets", 1.3, 0.0, 1e9, "Relativistic jets along the spin axis; 0 turns them off."));
    public static readonly DoubleSetting JetLength = Schema.Add(new DoubleSetting("jetLength", 60.0, 0.1, 1e9, "Length of each jet in horizon radii."));
    public static readonly DoubleSetting JetWidth = Schema.Add(new DoubleSetting("jetWidth", 0.9, 0.0001, 1e6, "Jet width: radius = jetWidth * (distance along the axis)^0.55, in horizon radii."));
    public static readonly DoubleSetting JetSpeed = Schema.Add(new DoubleSetting("jetSpeed", 0.85, 0.0, 0.999999, "Speed of the jet gas as a fraction of light speed; faster beams the approaching jet brighter."));
    public static readonly DoubleSetting JetSpiral = Schema.Add(new DoubleSetting("jetSpiral", 1.0, 0.0, 1.0, "Make each jet a solid spiral core, its surface swirled with streaks of brighter gas; 0 is a plain column."));
    public static readonly DoubleSetting JetTurns = Schema.Add(new DoubleSetting("jetTurns", 4.0, 0.0, 1e6, "Turns the swirl makes along each jet's length."));
    public static readonly DoubleSetting JetFlow = Schema.Add(new DoubleSetting("jetFlow", 30.0, 0.0, 1e9, "How fast the jet's pattern streams outward, in horizon radii per second at mid-length (it speeds up further out)."));
    public static readonly DoubleSetting JetKnots = Schema.Add(new DoubleSetting("jetKnots", 0.0, 0.0, 1.0, "Past jetKnotStart the jets come apart into a chain of blobs (plasmoids) streaming outward, each a turbulent puff with a bright bow-shaped front: 0 = a continuous jet, 1 = all blobs."));
    public static readonly DoubleSetting JetKnotStart = Schema.Add(new DoubleSetting("jetKnotStart", 0.3, 0.0, 1.0, "Where along the jets they break into blobs, as a fraction of their length."));
    public static readonly DoubleSetting JetKnotSpacing = Schema.Add(new DoubleSetting("jetKnotSpacing", 0.1, 0.001, 10.0, "Spacing of the blobs (fraction of the jet; they are further apart further out)."));
    public static readonly DoubleSetting JetKnotGrowth = Schema.Add(new DoubleSetting("jetKnotGrowth", 1.0, 0.0, 100.0, "How much the blobs swell as they travel out."));
    public static readonly DoubleSetting JetAbsorb = Schema.Add(new DoubleSetting("jetAbsorb", 0.6, 0.0, 1e6, "How much the jet gas absorbs light behind it, so near filaments hide far ones; 0 is a pure glow."));
    public static readonly BoolSetting DiskDrag = Schema.Add(new BoolSetting("diskDrag", true, "Gas in the disk drags vessels toward co-rotating with it."));
    public static readonly DoubleSetting DiskDragTimescale = Schema.Add(new DoubleSetting("diskDragTimescale", 0.5, 0.0001, 1e12, "Seconds for a vessel to match the gas speed at full disk density."));
    public static readonly DoubleSetting DiskDragMaxAccel = Schema.Add(new DoubleSetting("diskDragMaxAccel", 2000.0, 0.0, 1e15, "Strongest pull at the disk's outer edge in m/s^2 (2000 = 2 km/s every second); it rises inward with the gas density."));
    public static readonly DoubleSetting DiskBuffeting = Schema.Add(new DoubleSetting("diskBuffeting", 1.0, 0.0, 1e6, "Turbulent shaking of vessels inside the disk; 0 turns it off."));
    public static readonly DoubleSetting DiskLight = Schema.Add(new DoubleSetting("diskLight", 1.0, 0.0, 1e6, "How strongly the disk's glow lights nearby vessels; 0 turns it off."));
    public static readonly IntSetting Steps = Schema.Add(new IntSetting("steps", 300, 1, 1000000, "Maximum ray-march steps per pixel."));
    public static readonly DoubleSetting BoundRadius = Schema.Add(new DoubleSetting("boundRadius", 0.0, 0.0, 1e12, "Radius (horizon radii) where rays start bending; 0 = automatic."));
    public static readonly BoolSetting Pulsar = Schema.Add(new BoolSetting("pulsar", false, "A pulsar, not a black hole: no shadow, lensing or disk, the body's own star shows; the jets are its beams, swept round the spin axis (the normal)."));
    public static readonly DoubleSetting BeamPeriod = Schema.Add(new DoubleSetting("beamPeriod", 0.0, 0.0, 1e9, "Seconds (real time) for the jets to sweep once round the spin axis, like a pulsar's beams; 0 keeps them along the axis."));
    public static readonly BoolSetting DiskFromTde = Schema.Add(new BoolSetting("diskFromTde", false, "No disk or jets of its own: a tidal disruption event builds the configured disk from its debris over a couple of months, and the jets launch once it is half built."));
    public static readonly DoubleSetting BeamTilt = Schema.Add(new DoubleSetting("beamTilt", 30.0, 0.0, 180.0, "Angle between the sweeping jets and the spin axis, in degrees."));

    // Other kinds of object (SpaceObjects): what the node draws, and the settings only they use. A node for one of them
    // starts from its kind's defaults (SpaceObjects.KindDefaults), then its own values.
    public static readonly EnumSetting<SpaceObjectKind> Kind = Schema.Add(new EnumSetting<SpaceObjectKind>("kind", SpaceObjectKind.BlackHole, "What this draws: BlackHole, NeutronStar, ProtoplanetaryDisk, DebrisDisk, AsteroidBelt, Nebula or Comet. Sizes are in horizon radii for a black hole or neutron star, in length units (horizonRadius metres; 0 = the body's radius) for the rest."));
    public static readonly BoolSetting Enabled = Schema.Add(new BoolSetting("enabled", true, "Draw it at all; False switches it off without removing the node."));
    public static readonly IntSetting Slot = Schema.Add(new IntSetting("slot", 0, 0, 1000, "Which of the body's objects this is, in config order (0 = the first); the settings window writes it, configs need not."));
    public static readonly DoubleSetting StarRadius = Schema.Add(new DoubleSetting("starRadius", 3.0, 1.01, 1e9, "Neutron star: its radius in horizon radii (a 1.4 Sun, 12 km neutron star is 2.9); the body's radius is this many horizon radii."));
    public static readonly DoubleSetting SurfaceTemperature = Schema.Add(new DoubleSetting("surfaceTemperature", 1e6, 1.0, 1e12, "Neutron star: surface temperature in kelvin (its magnetic poles, where the columns land, three times hotter)."));
    public static readonly DoubleSetting StarTemperature = Schema.Add(new DoubleSetting("starTemperature", 5800.0, 1.0, 1e9, "Temperature (kelvin) of the star at the centre, whose light the dust, gas and rocks scatter."));
    public static readonly DoubleSetting FieldLines = Schema.Add(new DoubleSetting("fieldLines", 0.0, 0.0, 1e9, "Glow of the central star's dipole magnetic field lines, looping out to 27 star radii round the magnetic axis (the normal, or swept like a pulsar's beams by beamPeriod and beamTilt); 0 hides them. Any kind but a black hole."));
    public static readonly StringSetting LightSource = Schema.Add(new StringSetting("lightSource", string.Empty, "Comet: the body whose light and wind push its tails away (a star); empty = the nearest star it orbits."));
    public static readonly DoubleSetting AccretionColumns = Schema.Add(new DoubleSetting("accretionColumns", 1.0, 0.0, 1e9, "Neutron star: gas lifted off the disk's inner edge along the field lines and onto the magnetic poles; 0 = none."));
    public static readonly DoubleSetting Beams = Schema.Add(new DoubleSetting("beams", 1.0, 0.0, 1e9, "Neutron star: its two beams along the magnetic axis (beamTilt from the spin axis, sweeping once every beamPeriod s), jetLength long; 0 = none."));
    public static readonly DoubleSetting Flaring = Schema.Add(new DoubleSetting("flaring", 0.0, 0.0, 100.0, "Dusty disk: how much thicker it grows outward: half-thickness = diskThickness * r * (r / diskOuter)^flaring."));
    public static readonly DoubleSetting Rings = Schema.Add(new DoubleSetting("rings", 0.0, 0.0, 16.0, "Dusty disk: gaps cleared by planets, spread over the disk (up to 16)."));
    public static readonly DoubleSetting RingDepth = Schema.Add(new DoubleSetting("ringDepth", 0.97, 0.0, 1.0, "How empty the gaps are: 1 = no dust at all."));
    public static readonly DoubleSetting RingWidth = Schema.Add(new DoubleSetting("ringWidth", 0.02, 0.0001, 10.0, "Width of the gaps, as a fraction of the outer radius."));
    public static readonly StringSetting Gaps = Schema.Add(new StringSetting("gaps", string.Empty, "Dusty disk: gaps placed by hand, as fractions of the outer radius (\"0.25, 0.4, 0.7\"; up to 16); empty spreads rings of them over the disk."));
    public static readonly StringSetting GapWidths = Schema.Add(new StringSetting("gapWidths", string.Empty, "With gaps: each gap's width, as a fraction of the outer radius, in the same order (missing ones take ringWidth)."));
    public static readonly StringSetting GapDepths = Schema.Add(new StringSetting("gapDepths", string.Empty, "With gaps: how empty each gap is, 0..1, in the same order (missing ones take ringDepth)."));
    public static readonly DoubleSetting StarGlow = Schema.Add(new DoubleSetting("starGlow", 0.0, 0.0, 1e6, "The star's light glowing in the gas round it, out to this fraction of the outer radius; 0 = none."));
    public static readonly DoubleSetting Dust = Schema.Add(new DoubleSetting("dust", 0.2, 0.0, 1e9, "How much dust (or, in a nebula, gas): it scatters the starlight and hides what is behind it."));
    public static readonly DoubleSetting LightFalloff = Schema.Add(new DoubleSetting("lightFalloff", 2.0, 0.0, 100.0, "How the starlight on the dust and rocks fades with distance from the star: 2 is physical (inverse square); lower is a display aid that keeps the outer parts bright."));
    public static readonly ColorSetting DustColor = Schema.Add(new ColorSetting("dustColor", new Double3(1.0, 0.55, 0.28), "Colour of the dust (it scatters the starlight in this colour), r, g, b."));
    public static readonly ColorSetting OuterDustColor = Schema.Add(new ColorSetting("outerDustColor", new Double3(1.0, 0.55, 0.28), "Colour of the dust at the outer edge (it shades from dustColor at the inner), r, g, b."));
    public static readonly ColorSetting RockColor = Schema.Add(new ColorSetting("rockColor", new Double3(1.0, 0.97, 0.87), "Colour of the rocks, r, g, b (each varies a little)."));
    public static readonly DoubleSetting Rocks = Schema.Add(new DoubleSetting("rocks", 0.0, 0.0, 1.0, "Rocks drawn by the shader (no vessels, nothing for KSP to track): the share of places, at the middle of the belt, that hold one."));
    public static readonly DoubleSetting RockSize = Schema.Add(new DoubleSetting("rockSize", 1.0, 0.0, 1e6, "Size of the rocks, relative to their spacing (1: up to 0.43 of it). Never drawn smaller than a pixel: distant ones become a fine glitter."));
    public static readonly DoubleSetting RockSpacing = Schema.Add(new DoubleSetting("rockSpacing", 0.009, 1e-7, 10.0, "Spacing of the rocks, as a fraction of the outer radius."));
    public static readonly DoubleSetting RockMaxAngle = Schema.Add(new DoubleSetting("rockMaxAngle", 0.5, 0.0, 180.0, "Display aid: no rock looks wider than this (degrees) however close the camera is, so flying through a belt shows rocks, not planet-sized boulders (rocks are sized to the belt to be seen from afar); 0 = no limit."));
    public static readonly DoubleSetting RockAlbedo = Schema.Add(new DoubleSetting("rockAlbedo", 0.3, 0.0, 1e3, "How much of the starlight the rocks reflect."));
    public static readonly DoubleSetting DustLanes = Schema.Add(new DoubleSetting("dustLanes", 1.0, 0.0, 1e6, "Nebula: dark lanes of dust across the glowing gas."));
    public static readonly DoubleSetting CloudScale = Schema.Add(new DoubleSetting("cloudScale", 1.0, 0.0001, 1e4, "Nebula: size of its billows (more = smaller, more of them)."));
    public static readonly DoubleSetting CloudWarp = Schema.Add(new DoubleSetting("cloudWarp", 1.0, 0.0, 100.0, "Nebula: how swirled and torn its gas is."));
    public static readonly DoubleSetting Filaments = Schema.Add(new DoubleSetting("filaments", 1.5, 0.0, 1e6, "Nebula: bright thin filaments in the gas."));
    public static readonly DoubleSetting Shell = Schema.Add(new DoubleSetting("shell", 0.0, 0.0, 1.0, "Nebula: how much of its gas lies in a hollow, ragged shell, as a planetary nebula's round a dying star: 0 = a cloud, 1 = a shell."));
    public static readonly DoubleSetting DensityFalloff = Schema.Add(new DoubleSetting("densityFalloff", 0.6, 0.0, 10.0, "Dusty disk: how fast the dust thins outward (surface density ~ r^-densityFalloff)."));
    public static readonly DoubleSetting EdgeSoftness = Schema.Add(new DoubleSetting("edgeSoftness", 1.0, 0.01, 100.0, "Dusty disk: how soft its inner and outer edges are (1 = as tuned; more = more gradual)."));
    public static readonly DoubleSetting Clumps = Schema.Add(new DoubleSetting("clumps", 0.6, 0.0, 10.0, "Dusty disk: how clumpy the dust is (0 = smooth)."));
    public static readonly DoubleSetting ClumpSize = Schema.Add(new DoubleSetting("clumpSize", 1.0, 0.001, 1000.0, "Dusty disk: size of the clumps (more = smaller, more of them)."));
    public static readonly DoubleSetting ScatterAsymmetry = Schema.Add(new DoubleSetting("scatterAsymmetry", 0.45, -0.99, 0.99, "Dusty disk: how much the grains scatter light forward (Henyey-Greenstein g: 0 = evenly, 0.45 = typical dust, negative = back)."));
    public static readonly DoubleSetting RimGlow = Schema.Add(new DoubleSetting("rimGlow", 2.0, 0.0, 1000000.0, "Dusty disk: glow of its inner rim, heated by the star."));
    public static readonly DoubleSetting RimTemperature = Schema.Add(new DoubleSetting("rimTemperature", 1500.0, 1.0, 1000000000.0, "Dusty disk: temperature of the inner rim (kelvin; dust sublimates at ~1500 K)."));
    public static readonly DoubleSetting SpiralArms = Schema.Add(new DoubleSetting("spiralArms", 0.0, 0.0, 16.0, "Dusty disk: spiral arms in the dust (0 = none), as in Elias 2-27's disk."));
    public static readonly DoubleSetting SpiralStrength = Schema.Add(new DoubleSetting("spiralStrength", 0.5, 0.0, 1.0, "Dusty disk: how much denser the arms are than between them."));
    public static readonly DoubleSetting SpiralWinding = Schema.Add(new DoubleSetting("spiralWinding", 3.0, -100.0, 100.0, "Dusty disk: how tightly the arms wind (turns per e-fold of radius; negative winds the other way)."));
    public static readonly DoubleSetting RockSpread = Schema.Add(new DoubleSetting("rockSpread", 1.0, 0.0, 100.0, "Rocks: how far above and below the disk they spread, relative to diskThickness."));
    public static readonly DoubleSetting RockSizeSpread = Schema.Add(new DoubleSetting("rockSizeSpread", 1.0, 0.0, 100.0, "Rocks: how much their sizes vary (0 = all alike)."));
    public static readonly DoubleSetting CloudStretch = Schema.Add(new DoubleSetting("cloudStretch", 1.8, 0.01, 100.0, "Nebula: how much longer it is along its normal than across (1 = round)."));
    public static readonly DoubleSetting CloudRagged = Schema.Add(new DoubleSetting("cloudRagged", 1.9, 0.0, 100.0, "Nebula: how ragged and lobed its outline is."));
    public static readonly DoubleSetting CloudFill = Schema.Add(new DoubleSetting("cloudFill", 0.9, -10.0, 10.0, "Nebula: how much of it is empty between the clouds (lower fills it in)."));
    public static readonly DoubleSetting ComaBrightness = Schema.Add(new DoubleSetting("comaBrightness", 1.0, 0.0, 1000000.0, "Comet: brightness of the coma."));
    public static readonly DoubleSetting NucleusBrightness = Schema.Add(new DoubleSetting("nucleusBrightness", 1.0, 0.0, 1000000.0, "Comet: brightness of the false nucleus at its heart."));
    public static readonly DoubleSetting IonBrightness = Schema.Add(new DoubleSetting("ionBrightness", 1.0, 0.0, 1000000.0, "Comet: brightness of the ion tail."));
    public static readonly DoubleSetting DustBrightness = Schema.Add(new DoubleSetting("dustBrightness", 1.0, 0.0, 1000000.0, "Comet: brightness of the dust tail."));
    public static readonly DoubleSetting IonTailWidth = Schema.Add(new DoubleSetting("ionTailWidth", 1.0, 0.0, 100.0, "Comet: width of the ion tail (1 = as tuned)."));
    public static readonly DoubleSetting Streamers = Schema.Add(new DoubleSetting("streamers", 1.0, 0.0, 100.0, "Comet: how streaked the ion tail is with streamers and knots (0 = smooth)."));
    public static readonly DoubleSetting Striae = Schema.Add(new DoubleSetting("striae", 1.0, 0.0, 100.0, "Comet: the faint bands (striae) across the dust tail (0 = none)."));
    public static readonly DoubleSetting FieldLineCount = Schema.Add(new DoubleSetting("fieldLineCount", 8.0, 1.0, 64.0, "Field lines: how many loops go round the magnetic axis."));
    public static readonly DoubleSetting FieldShells = Schema.Add(new DoubleSetting("fieldShells", 7.0, 1.0, 16.0, "Field lines: how many nested shells of loops."));
    public static readonly DoubleSetting FieldShellRatio = Schema.Add(new DoubleSetting("fieldShellRatio", 1.6, 1.01, 10.0, "Field lines: how much bigger each shell is than the one inside it (the first reaches 1.6 star radii)."));
    public static readonly DoubleSetting FieldLineWidth = Schema.Add(new DoubleSetting("fieldLineWidth", 0.015, 0.0001, 1.0, "Field lines: their thickness, as a fraction of the distance from the star."));
    public static readonly DoubleSetting ColumnWidth = Schema.Add(new DoubleSetting("columnWidth", 0.1, 0.001, 10.0, "Neutron star: width of the accretion columns, as a fraction of the disk's inner radius."));
    public static readonly DoubleSetting BeamWidth = Schema.Add(new DoubleSetting("beamWidth", 0.06, 0.0, 10.0, "Neutron star: how fast its beams widen (radius grows by this per unit along them)."));
    public static readonly DoubleSetting Hotspots = Schema.Add(new DoubleSetting("hotspots", 4.0, 0.0, 1000000.0, "Neutron star: brightness of the hot spots at its magnetic poles, where the columns land."));
    public static readonly DoubleSetting DiskDensity = Schema.Add(new DoubleSetting("diskDensity", 45.0, 0.0, 1000000000.0, "Black hole disk (advanced): how dense the gas is (it is divided by the radius)."));
    public static readonly DoubleSetting DiskOpacity = Schema.Add(new DoubleSetting("diskOpacity", 0.35, 0.0, 1000000.0, "Black hole disk (advanced): how much the gas hides what is behind it (low: a bit translucent)."));
    public static readonly DoubleSetting Emission = Schema.Add(new DoubleSetting("emission", 3.6, 0.0, 1000000000.0, "Black hole disk (advanced): the hot gas's own glow."));
    public static readonly DoubleSetting CloudSize = Schema.Add(new DoubleSetting("cloudSize", 0.45, 0.0001, 10000.0, "Black hole disk (advanced): puffy lumps per horizon radius (more = smaller lumps)."));
    public static readonly DoubleSetting SelfShadow = Schema.Add(new DoubleSetting("selfShadow", 0.9, 0.0, 1000000.0, "Black hole disk (advanced): how deeply its lumps shade each other from the inner disk's light."));
    public static readonly DoubleSetting InnerLight = Schema.Add(new DoubleSetting("innerLight", 3.0, 0.0, 1000000000.0, "Black hole disk (advanced): light from the inner disk lighting the bulges further out."));
    public static readonly DoubleSetting StreakWinding = Schema.Add(new DoubleSetting("streakWinding", 5.0, -1000.0, 1000.0, "Black hole disk (advanced): how tightly the streaks seen from above wind into a spiral."));
    public static readonly DoubleSetting StreakStretch = Schema.Add(new DoubleSetting("streakStretch", 30.0, 0.0, 1000000.0, "Black hole disk (advanced): how drawn out along the orbit the streaks are."));
    public static readonly DoubleSetting StreakLanes = Schema.Add(new DoubleSetting("streakLanes", 0.85, 0.0, 1.0, "Black hole disk (advanced): how dark the lanes between the streaks are."));
    public static readonly DoubleSetting ParticleSpacing = Schema.Add(new DoubleSetting("particleSpacing", 0.5, 0.01, 1000.0, "Black hole disk (advanced): spacing of the sparks and debris (horizon radii; less = more of them)."));
    public static readonly DoubleSetting BulgeSize = Schema.Add(new DoubleSetting("bulgeSize", 0.12, 0.0001, 10.0, "Galaxy: radius of its central bulge of old stars, as a fraction of its radius (large with no arms: an elliptical galaxy)."));
    public static readonly DoubleSetting BulgeBrightness = Schema.Add(new DoubleSetting("bulgeBrightness", 1.0, 0.0, 1e6, "Galaxy: brightness of the bulge (its colour is innerColor)."));
    public static readonly DoubleSetting BarLength = Schema.Add(new DoubleSetting("barLength", 0.15, 0.0, 10.0, "Galaxy: length of the central bar the arms start from, as a fraction of its radius; 0 = none."));
    public static readonly DoubleSetting StarClusters = Schema.Add(new DoubleSetting("starClusters", 1.0, 0.0, 1e6, "Galaxy: pink star-forming knots along the arms (colorB)."));
    public static readonly ColorSetting ColorA = Schema.Add(new ColorSetting("colorA", new Double3(1.0, 0.25, 0.45), "Nebula: the glow of its gas (hydrogen-alpha red-pink); comet: its ion tail (blue); neutron star: its field lines. r, g, b."));
    public static readonly ColorSetting ColorB = Schema.Add(new ColorSetting("colorB", new Double3(0.2, 0.75, 0.85), "Nebula: the glow of its other gas (oxygen teal); comet: its dust tail. r, g, b."));
    public static readonly DoubleSetting Activity = Schema.Add(new DoubleSetting("activity", 1.0, 0.0, 1e6, "Comet: how strongly it outgasses: the coma and tails together; 0 = a dormant nucleus."));
    public static readonly DoubleSetting ComaSize = Schema.Add(new DoubleSetting("comaSize", 2.0, 0.0001, 1e9, "Comet: size of the glowing coma round the nucleus, in length units."));
    public static readonly ColorSetting ComaColor = Schema.Add(new ColorSetting("comaColor", new Double3(0.6, 1.0, 0.75), "Comet: colour of the coma (C2 green), r, g, b."));
    public static readonly DoubleSetting IonTailLength = Schema.Add(new DoubleSetting("ionTailLength", 60.0, 0.0001, 1e9, "Comet: length of the straight ion tail (it fades over this many length units), pointing straight away from the star."));
    public static readonly DoubleSetting DustTailLength = Schema.Add(new DoubleSetting("dustTailLength", 35.0, 0.0001, 1e9, "Comet: length of the curved dust tail, in length units."));
    public static readonly DoubleSetting DustTailCurve = Schema.Add(new DoubleSetting("dustTailCurve", 0.5, 0.0, 100.0, "Comet: how far the dust tail curves back along the orbit (the grains keep the comet's motion as the starlight pushes them out)."));
    public static readonly DoubleSetting DustTailWidth = Schema.Add(new DoubleSetting("dustTailWidth", 1.0, 0.0, 100.0, "Comet: how broad the dust tail fans out behind."));

    /// <summary>
    /// How much wider a Schwarzschild black hole's shadow looks than its event horizon: light passing closer than
    /// sqrt(27)/2 horizon radii is captured.
    /// </summary>
    public static readonly double ShadowToHorizon = Math.Sqrt(27.0) / 2.0;

    /// <summary>
    /// The event horizon radius in metres: <paramref name="configured"/> when set, otherwise from the body's radius,
    /// which is the horizon itself or, with <paramref name="bodyIsShadow"/>, the radius of the shadow.
    /// </summary>
    public static double HorizonMeters(double configured, double bodyRadius, bool bodyIsShadow) =>
        configured > 0.0 ? configured : bodyIsShadow ? bodyRadius / ShadowToHorizon : bodyRadius;

    /// <summary>
    /// Beyond this radius (horizon radii) a disk's clumps grow with the radius, so a disk of any size looks alike at every
    /// scale; a disk bigger than this is also drawn with steps that grow with the radius. Must match BlackHole.shader.
    /// </summary>
    public const double DetailRadius = 20.0;

    /// <summary>Extra ray-march steps a disk this big (horizon radii) needs: rays may skim hundreds of radii of it.</summary>
    public static int ExtraSteps(double diskOuter) => diskOuter > DetailRadius ? (int)(100.0 * Math.Log(diskOuter / DetailRadius)) : 0;

    /// <summary>The streak look fades in from this height above the disk's plane (degrees, seen from the hole)...</summary>
    public const double StreaksFromDegrees = 10.0;
    /// <summary>...growing smoothly to all of it here.</summary>
    public const double StreaksFullDegrees = 85.0;

    /// <summary>
    /// How much of the streak look a view gets: <paramref name="streaks"/> (diskStreaks), faded in smoothly with the camera's
    /// height above the disk's plane as seen from the hole (<paramref name="elevationDegrees"/>, 0 edge-on, 90 straight down).
    /// </summary>
    public static double StreakView(double streaks, double elevationDegrees)
    {
        double t = (elevationDegrees - StreaksFromDegrees) / (StreaksFullDegrees - StreaksFromDegrees);
        t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
        return streaks * t * t * (3.0 - 2.0 * t);
    }

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
    public static CfgNode ApplyUserOverrides(CfgNode baseNode, CfgNode userContainer) => ApplyUserOverrides(baseNode, 0, userContainer);

    /// <summary>
    /// The config node with the player's in-game tweaks for the same object laid over it (theirs win): the same body and
    /// the same <paramref name="slot"/>, its place among the body's configured objects (a tweak without one is slot 0).
    /// </summary>
    public static CfgNode ApplyUserOverrides(CfgNode baseNode, int slot, CfgNode userContainer)
    {
        CfgNode child = UserNodeFor(baseNode?.GetLast(Body.Key)?.Trim(), slot, userContainer);
        return child != null ? CfgNode.Overlay(baseNode, child) : baseNode;
    }

    /// <summary>The player's tweaks for the body's object in <paramref name="slot"/>, or null.</summary>
    public static CfgNode UserNodeFor(string body, int slot, CfgNode userContainer)
    {
        if (userContainer == null || string.IsNullOrEmpty(body)) return null;
        foreach (CfgNode child in userContainer.Children)
        {
            if (child.Name == Schema.NodeName && child.GetLast(Body.Key)?.Trim() == body && SlotOf(child) == slot)
                return child;
        }
        return null;
    }

    /// <summary>A node's slot (0 when it has none or it is not a number).</summary>
    public static int SlotOf(CfgNode node) =>
        int.TryParse(node?.GetLast(Slot.Key)?.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int slot) && slot >= 0 ? slot : 0;

    /// <summary>A node's kind (BlackHole when it has none or it is not one).</summary>
    public static SpaceObjectKind KindOf(CfgNode node) =>
        Enum.TryParse(node?.GetLast(Kind.Key)?.Trim(), true, out SpaceObjectKind kind) && Enum.IsDefined(typeof(SpaceObjectKind), kind) ? kind : SpaceObjectKind.BlackHole;

    /// <summary>
    /// Resolves a node with its kind's defaults (SpaceObjects.KindDefaults) under its own values, so a nebula or a comet
    /// only needs the settings it changes.
    /// </summary>
    public static SettingsValues ResolveWithKind(CfgNode node, List<ConfigIssue> issues) =>
        Schema.Resolve(CfgNode.Overlay(SpaceObjects.KindDefaults(KindOf(node)), node ?? new CfgNode(Schema.NodeName)), issues);

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
