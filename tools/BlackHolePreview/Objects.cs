using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;

/// <summary>
/// CPU copy of BlackHole.shader's pass 2 (fragObject), the other kinds of object (KSPVE.Pure.Space.SpaceObjects), for
/// stills: a neutron star with its disk, accretion columns, beams and magnetic field lines; a protoplanetary disk; a debris
/// disk; an asteroid belt; a nebula; and a comet. It takes the same settings as the game, by their config keys:
///   object &lt;kind&gt; out.png width height inclination distance [time]
///       the kind's defaults, any key overridden by an OBJ_&lt;KEY&gt; environment variable (OBJ_DISKOUTER=300, OBJ_DUSTCOLOR=1,0.5,0.2)
///   object-cfg &lt;file.cfg&gt; &lt;body&gt; &lt;slot&gt; &lt;body radius m&gt; out.png width height inclination distance [time]
///       a KSPVE_BLACKHOLE node from a config file (the body's slot-th), as the game would resolve it
/// Distance is in multiples of the object's bounding radius. OBJ_AZ (degrees), OBJ_TARGET (x,y,z in bounding radii),
/// OBJ_SUN and OBJ_VEL (a comet's star direction and motion), OBJ_SKY=1 (a starfield behind) set up the view.
/// </summary>
internal static partial class Program
{
    // Kinds, as SpaceObjectKind numbers them.
    const int KindNeutronStar = 1, KindProtoplanetary = 2, KindDebrisDisk = 3, KindAsteroidBelt = 4, KindNebula = 5, KindComet = 6, KindGalaxy = 7;
    const float FieldShellStart = 1.6f;                     // SpaceObjects
    const float NebulaRadius = 10f;                         // SpaceObjects.NebulaShaderRadius

    // The shader's uniforms, named after the config keys they come from.
    static int ObjKind, ObjGapCount;
    static float ObjStarR, ObjStarT, ObjSurfaceT, ObjInner, ObjOuter, ObjThick, ObjFlaring, ObjDust, ObjFalloff, ObjGlowSize,
        ObjRockCell, ObjRocks, ObjRockSize, ObjRockAlbedo, ObjLanes, ObjCloudScale, ObjWarp, ObjFilaments, ObjShell,
        ObjFieldLines, ObjColumns, ObjBeams, ObjBeamLength, ObjActivity, ObjComa, ObjIonLength, ObjDustLength, ObjDustCurve,
        ObjDustWidth, ObjBright, ObjAngle, ObjDensityFalloff, ObjEdgeSoftness, ObjClumps, ObjClumpSize, ObjScatterG, ObjRimGlow,
        ObjRimT, ObjRockSpread, ObjSpiralArms, ObjSpiralStrength, ObjSpiralWinding, ObjRockSizeSpread, ObjCloudStretch,
        ObjCloudRagged, ObjCloudFill, ObjComaB, ObjIonB, ObjDustB, ObjNucleusB, ObjIonWidth, ObjStreamers, ObjStriae,
        ObjFieldCount, ObjFieldShells, ObjFieldRatio, ObjFieldWidth, ObjColumnWidth, ObjBeamWidth, ObjHotspots, ObjRockCap,
        ObjBulge, ObjBulgeB, ObjBar, ObjClusters;
    static Vector3 ObjInnerColor, ObjOuterColor;
    static Vector3 ObjColorA, ObjColorB, ObjDustColor, ObjOuterDust, ObjRockColor, ObjComaColor, ObjMag, ObjSunDir, ObjVelDir, ObjT, ObjB;
    static readonly Vector4[] ObjGaps = new Vector4[DiskGaps.Max];

    /// <summary>The disk's frame (the shader's ToDiskFrame): (r cos, r sin, height) about N, turned by ObjAngle.</summary>
    static Vector3 ObjFrame(Vector3 x, out float r)
    {
        float height = Vector3.Dot(x, N);
        Vector3 q = x - height * N;
        r = q.Length();
        float angle = MathF.Atan2(Vector3.Dot(q, ObjB), Vector3.Dot(q, ObjT)) - ObjAngle;
        return new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), height);
    }

    static float Hash1(float n) => Frac(MathF.Sin(n) * 43758.5453f);

    /// <summary>A cell's hash, evenly spread over [0,1) (Hash3's product form piles up near 0, which a probability test can't take).</summary>
    static float CellHash(Vector3 c, float salt) => Hash1(Vector3.Dot(c, new Vector3(127.1f, 311.7f, 74.7f)) * 0.0137f + salt);

    /// <summary>Henyey-Greenstein phase, scaled so isotropic scattering is 1.</summary>
    static float PhaseHG(float cosAngle, float g) => (1f - g * g) / MathF.Pow(1f + g * g - 2f * g * cosAngle, 1.5f);

    /// <summary>A star's colour at luminance 1.</summary>
    static Vector3 StarLight(float kelvin)
    {
        Vector3 c = Blackbody(kelvin);
        float l = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        return c / MathF.Max(l, 1e-4f);
    }

    // ---------------------------------------------------------------------------------------------- dusty disks

    /// <summary>The gaps planets clear (DiskGaps: spread over the disk, or placed one by one); they cut a belt's rocks too.</summary>
    static float GapFactor(float r)
    {
        float gaps = 1f;
        for (int k = 0; k < ObjGapCount; k++)
        {
            Vector4 gap = ObjGaps[k];
            gaps *= 1f - gap.Z * MathF.Exp(-Sq((r - gap.X) / gap.Y));
        }
        return gaps;
    }

    /// <summary>
    /// Dust density of a protoplanetary or debris disk (or an asteroid belt's dust) at x. Protoplanetary: flared (thicker
    /// further out), its density falling outward, cut by gaps where planets clear their orbits. Debris: one narrow, thin ring.
    /// </summary>
    static float DustDisk(Vector3 x, out float r, out Vector3 pd)
    {
        pd = ObjFrame(x, out r);
        float inner = ObjInner, outer = ObjOuter;
        // Its edges and gaps go by the distance from the star (orbits' sizes), its thickness by the angle above the plane
        // (their inclinations): a thick belt is lens-shaped, not a cylinder.
        float rs = pd.Length();
        float soft = ObjEdgeSoftness;
        if (rs < inner * MathF.Max(1f - 0.4f * soft, 0f) || rs > outer * (1f + 0.1f * soft)) return 0f;
        float h = ObjThick * r * MathF.Pow(r / outer, ObjFlaring);
        if (MathF.Abs(pd.Z) > 4f * h) return 0f;
        float radial;
        if (ObjKind == KindDebrisDisk)
        {
            float rc = 0.5f * (inner + outer), wr = 0.5f * (outer - inner);
            radial = MathF.Exp(-2f * Sq((rs - rc) / (wr * soft)));
        }
        else
            radial = SmoothStep(inner * (1f - 0.4f * soft), inner * (1f + 0.15f * soft), rs)
                * (1f - SmoothStep(outer * (1f - 0.1f * soft), outer * (1f + 0.1f * soft), rs)) * MathF.Pow(inner / rs, ObjDensityFalloff);
        float gaps = GapFactor(rs);
        float vertical = MathF.Exp(-0.5f * Sq(pd.Z / h));
        float clumps = 1f - 0.5f * ObjClumps + ObjClumps * Fbm(pd * (8f * ObjClumpSize / outer) + new Vector3(3.1f));
        if (ObjSpiralArms > 0f)
        {
            // Spiral arms: density waves winding out from the inner disk.
            float arms = MathF.Cos(ObjSpiralArms * MathF.Atan2(pd.Y, pd.X) - ObjSpiralWinding * MathF.Log(r / inner));
            clumps *= 1f + ObjSpiralStrength * arms;
        }
        return ObjDust * radial * gaps * gaps * vertical * MathF.Max(clumps, 0f) / h;
    }

    /// <summary>
    /// Starlight scattered by the dust (forward-scattering grains: brightest between the star and the eye), dimmed by the
    /// dust between it and the star, with the inner rim hot enough to glow.
    /// </summary>
    static void AccumulateDustDisk(Vector3 x, Vector3 vn, float dt, ref Vector3 color, ref float transmittance)
    {
        float rho = DustDisk(x, out float r, out _);
        float inner = ObjInner, outer = ObjOuter;
        if (rho * outer < 4e-4f) return;
        float d = x.Length();
        float s1 = DustDisk(x * 0.9f, out _, out _), s2 = DustDisk(x * 0.72f, out _, out _);
        float lightT = MathF.Exp(-(s1 * 0.1f + s2 * 0.18f) * d);
        float illum = MathF.Pow(outer / MathF.Max(d, inner * 0.5f), ObjFalloff);
        float phase = 0.25f + 0.75f * PhaseHG(Vector3.Dot(x / d, -vn), ObjScatterG) * 0.5f;
        Vector3 dustColor = Vector3.Lerp(ObjDustColor, ObjOuterDust, Math.Clamp((r - inner) / MathF.Max(outer - inner, 1e-6f), 0f, 1f));
        Vector3 scattered = dustColor * StarLight(ObjStarT) * (illum * lightT * phase);
        Vector3 rim = Blackbody(ObjRimT) * (ObjRimGlow * SmoothStep(inner * 1.8f, inner * 0.9f, r));
        color += transmittance * (scattered + rim) * (rho * dt);
        transmittance *= MathF.Exp(-rho * dt);
    }

    /// <summary>The star's glow in the gas round it (a young star is still wrapped in dust).</summary>
    static void AccumulateStarGlow(Vector3 x, float dt, ref Vector3 color, float transmittance)
    {
        float size = ObjGlowSize * (ObjKind == KindNebula ? NebulaRadius : ObjOuter);
        if (size <= 0f) return;
        float d = x.Length();
        float glow = 0.05f * size / (d * d + 0.01f * size * size) * MathF.Exp(-d / size);
        color += transmittance * StarLight(ObjStarT) * (glow * dt);
    }

    // ---------------------------------------------------------------------------------------------- rocks

    /// <summary>How likely a cell of rock space at pd (the disk's frame) holds a rock: a belt, or a debris ring.</summary>
    static float RockField(Vector3 pd)
    {
        float r = MathF.Sqrt(pd.X * pd.X + pd.Y * pd.Y), rs = pd.Length();   // as DustDisk: edges by distance, thickness by angle
        float inner = ObjInner, outer = ObjOuter;
        float radial = ObjKind == KindDebrisDisk
            ? MathF.Exp(-2f * Sq((rs - 0.5f * (inner + outer)) / (0.5f * (outer - inner))))
            : SmoothStep(inner * 0.9f, inner * 1.15f, rs) * (1f - SmoothStep(outer * 0.85f, outer * 1.1f, rs));
        float h = MathF.Max(ObjThick * r * ObjRockSpread, 1e-6f);
        return ObjRocks * radial * GapFactor(rs) * MathF.Exp(-0.5f * Sq(pd.Z / h));
    }

    /// <summary>
    /// Rocks: one candidate per cell of a grid in the disk's frame, counted by the ray segment its closest approach falls
    /// in, lit on the side facing the star. Never smaller than the pixel footprint, keeping their light, so distant rocks
    /// become a fine glitter rather than flickering.
    /// </summary>
    static void AccumulateRocks(Vector3 x0, Vector3 x1, float traveled, ref Vector3 color, ref float transmittance)
    {
        if (ObjRocks <= 0f) return;
        Vector3 a = ObjFrame(x0, out _), ab = ObjFrame(x1, out _) - a;
        float ab2 = Vector3.Dot(ab, ab);
        float cell = ObjRockCell;
        if (ab2 < 1e-10f * cell * cell) return;
        Vector3 vn = ab / MathF.Sqrt(ab2);
        Vector3 mid = (a + ab * 0.5f) / cell - new Vector3(0.5f);
        Vector3 baseCell = new Vector3(MathF.Floor(mid.X), MathF.Floor(mid.Y), MathF.Floor(mid.Z));
        float footprint = traveled * PixelAngle;
        Vector3 light = StarLight(ObjStarT);
        float inner = ObjInner, outer = ObjOuter;
        for (int k = 0; k < 8; k++)
        {
            Vector3 c = baseCell + new Vector3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
            Vector3 jitter = new Vector3(CellHash(c, 1.7f), CellHash(c, 9.2f), CellHash(c, 23.5f));
            Vector3 p = (c + new Vector3(0.2f) + jitter * 0.6f) * cell;
            // The hash holds only ~8 bits (frac of sin x 43758 in floats) and is exactly 0 one cell in 256: below that a
            // cell's chance is taken as none.
            float chance = RockField(p);
            if (chance < 1f / 256f || CellHash(c, 71.3f) >= chance) continue;
            float s = Vector3.Dot(p - a, ab) / ab2;
            if (s < 0f || s >= 1f) continue;
            Vector3 dv = a + ab * s - p;
            float dd = Vector3.Dot(dv, dv);
            float radius = ObjRockSize * cell * (0.08f + 0.35f * ObjRockSizeSpread * jitter.X * jitter.X * jitter.X);
            // Up close no rock looks wider than rockMaxAngle: they are sized to the belt to be seen from afar.
            if (ObjRockCap > 0f) radius = MathF.Min(radius, ObjRockCap * (traveled + s * MathF.Sqrt(ab2)));
            float sigma = MathF.Max(radius, footprint);
            float cover = Math.Clamp(MathF.Exp(-dd / (sigma * sigma)) * (radius * radius) / (sigma * sigma), 0f, 1f);
            if (cover < 1e-4f) continue;
            // The rock's visible face, lit by the star at the centre.
            Vector3 n = Vector3.Normalize(dv - vn * MathF.Sqrt(MathF.Max(radius * radius - dd, 0f)) + new Vector3(1e-6f));
            float lit = MathF.Max(Vector3.Dot(n, -Vector3.Normalize(p)), 0f);
            float illum = MathF.Pow(outer / MathF.Max(p.Length(), inner * 0.5f), ObjFalloff);
            float albedo = ObjRockAlbedo * (0.6f + 0.8f * jitter.Z);
            Vector3 rock = light * (albedo * (illum * lit * 1.4f + 0.02f)) * ObjRockColor * new Vector3(1f, 0.95f + 0.1f * jitter.Y, 0.95f + 0.1f * jitter.Y);
            color += transmittance * rock * cover;
            transmittance *= 1f - cover;
        }
    }

    // ---------------------------------------------------------------------------------------------- nebula

    /// <summary>A nebula NebulaRadius across: glowing gas in two colours, bright filaments, dark dust lanes, a ragged edge.</summary>
    static void Nebula(Vector3 x, out float gas, out float mix, out float filament, out float dust)
    {
        gas = mix = filament = dust = 0f;
        if (x.Length() > 1.3f * NebulaRadius) return;
        Vector3 pd = ObjFrame(x, out _);
        // Stretched along the normal and warped twice, so it isn't a ball.
        Vector3 xs = new Vector3(pd.X * 0.75f, pd.Z * 0.75f * ObjCloudStretch, pd.Y);
        Vector3 q = xs * (0.22f * ObjCloudScale) + new Vector3(0.0f, Time * 0.002f, 0.0f);
        Vector3 w = new Vector3(Fbm(q + new Vector3(1.7f)), Fbm(q + new Vector3(9.2f)), Fbm(q + new Vector3(4.3f))) - new Vector3(0.5f);
        Vector3 p = q + w * (2.2f * ObjWarp);
        Vector3 w2 = new Vector3(Noise3(p * 2.1f + new Vector3(5.5f)), Noise3(p * 2.1f + new Vector3(8.8f)), Noise3(p * 2.1f + new Vector3(2.2f))) - new Vector3(0.5f);
        p += w2 * (0.45f * ObjWarp);
        float edge = (1f - SmoothStep(2f, 11.5f, xs.Length() * (0.3f + ObjCloudRagged * Fbm(pd * 0.12f + new Vector3(6.6f)))))
            * (1f - SmoothStep(NebulaRadius, 1.3f * NebulaRadius, x.Length()));
        // A planetary nebula's shell: the gas piled into a hollow, ragged shell.
        float shell = (xs.Length() / NebulaRadius) * (0.85f + 0.3f * Fbm(p * 0.5f + new Vector3(1.1f)));
        edge *= 1f + ObjShell * (4f * MathF.Exp(-Sq((shell - 0.55f) / 0.06f)) - 1f);
        if (edge <= 0f) return;
        float n = Fbm(p);
        gas = edge * MathF.Pow(MathF.Max(n * 2.1f - ObjCloudFill, 0f), 2f) * ObjDust;
        mix = SmoothStep(0.25f, 0.75f, Fbm(p * 0.7f + new Vector3(7.7f)));
        mix += (SmoothStep(0.45f, 0.62f, shell) - mix) * ObjShell;   // a shell: one gas inside, the other outside (O III, H-alpha)
        // Filaments: ridges of two octaves, thin and bright where the gas is.
        float ridge1 = 1f - MathF.Abs(2f * Noise3(p * 2.6f + new Vector3(2.2f)) - 1f);
        float ridge2 = 1f - MathF.Abs(2f * Noise3(p * 6.1f + new Vector3(4.4f)) - 1f);
        filament = edge * (MathF.Pow(ridge1, 12f) + 0.5f * MathF.Pow(ridge2, 16f)) * SmoothStep(0.32f, 0.6f, n) * ObjDust * ObjFilaments;
        dust = edge * MathF.Max(Fbm(p * 1.4f + new Vector3(3.3f)) * 2f - 1.05f, 0f) * ObjLanes;
    }

    // ---------------------------------------------------------------------------------------------- comet

    /// <summary>
    /// A comet at the centre, its star along ObjSunDir and moving along ObjVelDir: a glowing coma, a straight ion tail
    /// pointing directly away from the star, streaked and knotted, and a dust tail curving back along the orbit (the grains
    /// keep the comet's motion as the starlight pushes them out), sharp on its leading edge.
    /// </summary>
    static void AccumulateComet(Vector3 x, float dt, float fp, ref Vector3 color, ref float transmittance)
    {
        float act = ObjActivity;
        if (act <= 0f) return;
        float r = x.Length();
        Vector3 away = -ObjSunDir, vel = ObjVelDir;
        // Coma: a soft glow round the nucleus, greenish (C2), with a bright false nucleus.
        float coma = MathF.Exp(-r / ObjComa) / (0.3f + r) * act;
        color += transmittance * ObjComaColor * (coma * 0.45f * ObjComaB * dt);
        float core = MathF.Exp(-r * r / MathF.Max(0.02f, fp * fp));
        color += transmittance * new Vector3(1f, 0.97f, 0.9f) * (core * 3f * ObjNucleusB * act * dt / MathF.Max(0.15f, fp));

        float s = Vector3.Dot(x, away);
        if (s <= -0.5f) return;
        float sp = MathF.Max(s, 0f);
        // Ion tail: straight away from the star, narrow, blue (CO+), streamers and knots drifting outward.
        Vector3 off = x - away * sp;
        float d = off.Length();
        float w = MathF.Max((0.25f + 0.025f * sp) * ObjIonWidth, fp);
        Vector3 e1 = Vector3.Normalize(Vector3.Cross(away, MathF.Abs(away.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        Vector3 e2 = Vector3.Cross(away, e1);
        float around = MathF.Atan2(Vector3.Dot(off, e2), Vector3.Dot(off, e1));
        float streak = ObjStreamers;
        float streamers = 1f - 0.7f * streak + 1.4f * streak * Sq(Noise3(new Vector3(MathF.Cos(around) * 3f, MathF.Sin(around) * 3f, sp * 0.05f - Time * 0.3f)));
        float knots = 1f - 0.4f * streak + 0.8f * streak * Noise3(new Vector3(0.3f, 1.7f, sp * 0.12f - Time * 0.5f));
        float ion = MathF.Exp(-Sq(d / w)) * MathF.Exp(-sp / ObjIonLength) * SmoothStep(-0.5f, 1f, s) * streamers * knots / w;
        color += transmittance * ObjColorA * (ion * 0.5f * ObjIonB * act * dt);

        // Dust tail: the grains spread back along the orbit (older ones lag more): a sharp leading edge, a soft trailing one.
        Vector3 curve = away * sp - vel * (ObjDustCurve * sp * sp / ObjDustLength + 0.1f * sp);
        Vector3 offD = x - curve;
        float lag = Vector3.Dot(offD, -vel);
        float wd = MathF.Max(0.25f + 0.04f * sp * ObjDustWidth, fp);
        float wl = lag > 0f ? wd * (1f + 2.5f * ObjDustWidth * sp / (sp + 20f)) : wd;
        float side = (offD + lag * vel).Length();
        float d2 = Sq(lag / wl) + Sq(side / (wd * 1.4f));
        float striae = 1f - 0.3f * ObjStriae + 0.6f * ObjStriae * Noise3(new Vector3(lag * 0.7f / (1f + 0.05f * sp), sp * 0.02f, 4.4f));
        float dustTail = MathF.Exp(-d2) * MathF.Exp(-sp / ObjDustLength) * SmoothStep(-0.5f, 1.5f, s) * striae / MathF.Sqrt(wd * wl);
        color += transmittance * ObjColorB * (dustTail * 0.35f * ObjDustB * act * dt);
    }

    // ---------------------------------------------------------------------------------------------- galaxy

    /// <summary>
    /// A galaxy NebulaRadius across, in the disk's frame: a bulge of old stars (stretched into a bar), a thin disk of stars
    /// wound into logarithmic spiral arms, bright with young blue stars and pink star-forming knots, dark dust lanes along the
    /// arms' inner edges, mottled with star clouds. No arms and a big bulge: an elliptical; warp: irregular.
    /// </summary>
    static void Galaxy(Vector3 x, out Vector3 emit, out float absorb)
    {
        emit = Vector3.Zero;
        absorb = 0f;
        float R = NebulaRadius;
        if (x.Length() > 1.25f * R) return;
        Vector3 pd = ObjFrame(x, out _);
        if (ObjWarp > 0f)
        {
            pd.X += (Fbm(pd * 0.15f + new Vector3(1.7f)) - 0.5f) * 4f * ObjWarp;
            pd.Y += (Fbm(pd * 0.15f + new Vector3(9.2f)) - 0.5f) * 4f * ObjWarp;
        }
        float r = MathF.Sqrt(pd.X * pd.X + pd.Y * pd.Y), z = pd.Z;
        float hz = MathF.Max(ObjThick, 1e-3f) * R;
        // Bulge (Sersic n = 2), stretched along x into the bar.
        float rb = MathF.Max(ObjBulge, 1e-3f) * R;
        float qb = new Vector3(pd.X / (1f + ObjBar * R / rb), pd.Y, pd.Z * 1.6f).Length() / rb;
        float bulge = 0.25f * MathF.Exp(-3.67f * (MathF.Sqrt(qb) - 1f)) / rb * (1f - SmoothStep(0.85f * R, 1.2f * R, x.Length()));
        // Disk: exponential in radius and height, fading at the edge.
        float disk = MathF.Exp(-r / (0.3f * R)) * MathF.Exp(-MathF.Abs(z) / hz) * (1f - SmoothStep(0.85f * R, 1.2f * R, r)) / hz;
        // Spiral arms, from the bar's ends.
        float theta = MathF.Atan2(pd.Y, pd.X);
        float lr = MathF.Log(MathF.Max(r, 0.02f * R) / R);
        float armMix = 0f, lane = 0f;
        if (ObjSpiralArms > 0f)
        {
            float phase = ObjSpiralArms * (theta - ObjSpiralWinding * lr) + 1.5f * (Fbm(pd * 0.5f + new Vector3(4.4f)) - 0.5f);
            float wave = 0.5f + 0.5f * MathF.Cos(phase);
            float wave2 = wave * wave;
            armMix = ObjSpiralStrength * wave2 * wave2 * wave * SmoothStep(0.03f * R, 0.25f * R, r);
            float dustWave = 0.5f + 0.5f * MathF.Cos(phase + 0.7f);   // trailing just inside each arm
            lane = dustWave * dustWave * dustWave * dustWave;
        }
        float mottle = MathF.Max(1f - 0.5f * ObjClumps + ObjClumps * Fbm(pd * 1.8f + new Vector3(2.2f)), 0f);
        // Star-forming knots: along the arms, or scattered through an irregular galaxy.
        float knots = SmoothStep(0.6f, 0.76f, Fbm(pd * 3.0f + new Vector3(6.1f))) * (ObjSpiralArms > 0f ? armMix : 0.5f * ObjWarp) * ObjClusters;
        // Between the arms only old stars; in them young blue ones; the dust lanes dim what is in them.
        float interArm = 1f - 0.75f * ObjSpiralStrength * (ObjSpiralArms > 0f ? 1f : 0f);
        Vector3 diskLight = ObjOuterColor * interArm + ObjColorA * (3f * armMix);
        float dimmed = 1f - MathF.Min(0.7f * lane * ObjLanes, 0.9f);
        emit = 0.5f * (ObjInnerColor * (bulge * ObjBulgeB) + disk * mottle * dimmed * (diskLight + ObjColorB * (8f * knots)));
        // Dust: in a thinner layer, in lanes along the arms (or smooth with none).
        float dustLayer = MathF.Exp(-MathF.Abs(z) / (0.5f * hz)) * MathF.Exp(-r / (0.4f * R)) * (1f - SmoothStep(0.8f * R, 1.1f * R, r));
        absorb = ObjLanes * dustLayer * (ObjSpiralArms > 0f ? 0.25f + lane : 0.6f) * (0.5f + Fbm(pd * 2.5f + new Vector3(7.7f))) * 8f / hz;
    }

    // ---------------------------------------------------------------------------------------------- neutron star

    static void MagneticAxes(out Vector3 m, out Vector3 e1, out Vector3 e2)
    {
        m = ObjMag;
        e1 = Vector3.Normalize(Vector3.Cross(m, MathF.Abs(m.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        e2 = Vector3.Cross(m, e1);
    }

    /// <summary>Dipole field lines: glowing loops r = L sin^2 theta about the magnetic axis, on a few shells and meridians.</summary>
    static void AccumulateFieldLines(Vector3 x, float dt, float fp, ref Vector3 color, float transmittance)
    {
        float r = x.Length();
        if (ObjFieldLines <= 0f || ObjStarR <= 0f || r < ObjStarR) return;
        MagneticAxes(out Vector3 m, out Vector3 e1, out Vector3 e2);
        float c = Vector3.Dot(x, m) / r;
        float s2 = MathF.Max(1f - c * c, 1e-4f);
        float L = r / s2;
        float l0 = ObjStarR * FieldShellStart;
        float k = MathF.Round(MathF.Log(L / l0) / MathF.Log(ObjFieldRatio));
        if (k < 0f || k > ObjFieldShells - 1f) return;
        float lk = l0 * MathF.Pow(ObjFieldRatio, k);
        float spacing = 2f * MathF.PI / MathF.Max(ObjFieldCount, 1f);
        float phi = MathF.Atan2(Vector3.Dot(x, e2), Vector3.Dot(x, e1)) + k * 0.4f;
        float dphi = phi - MathF.Round(phi / spacing) * spacing;
        float dr = r - lk * s2;
        float dperp = r * MathF.Sqrt(s2) * dphi;
        float w = MathF.Max(ObjFieldWidth * r, fp);
        float glow = MathF.Exp(-(dr * dr + dperp * dperp) / (w * w)) * ObjFieldLines / (1f + 0.3f * k) / w;
        color += transmittance * ObjColorA * (glow * 0.06f * dt);
    }

    /// <summary>Accretion columns: gas lifted off the disk's inner edge along the field lines and onto the magnetic poles.</summary>
    static void AccumulateColumns(Vector3 x, float dt, ref Vector3 color, ref float transmittance)
    {
        float r = x.Length();
        float lm = ObjInner;
        if (ObjColumns <= 0f || r < ObjStarR || r > lm * 1.1f) return;
        MagneticAxes(out Vector3 m, out Vector3 e1, out Vector3 e2);
        float c = Vector3.Dot(x, m) / r;
        float s2 = MathF.Max(1f - c * c, 1e-4f);
        float dl = (r / s2 - lm) / (ObjColumnWidth * lm);
        float curtain = MathF.Exp(-dl * dl);
        if (curtain < 1e-3f) return;
        float phi = MathF.Atan2(Vector3.Dot(x, e2), Vector3.Dot(x, e1));
        float streams = 0.25f + 1.5f * Sq(Noise3(new Vector3(MathF.Cos(phi) * 2.5f, MathF.Sin(phi) * 2.5f, r * 0.3f - Time * 2f)));
        float heat = MathF.Pow(lm / r, 1.5f);
        float gas = curtain * streams * ObjColumns;
        color += transmittance * Blackbody(25000f * MathF.Min(heat, 3f)) * (gas * heat * 0.6f * dt);
        transmittance *= MathF.Exp(-gas * 0.15f * dt);
    }

    /// <summary>The pulsar's beams: two narrow cones of light along the magnetic axis.</summary>
    static void AccumulateBeams(Vector3 x, float dt, ref Vector3 color, float transmittance)
    {
        if (ObjBeams <= 0f) return;
        float z = Vector3.Dot(x, ObjMag), az = MathF.Abs(z);
        if (az < ObjStarR) return;
        float perp = (x - z * ObjMag).Length();
        float rad = 0.25f + ObjBeamWidth * az;
        float beam = MathF.Exp(-Sq(perp / rad)) * MathF.Exp(-az / ObjBeamLength) / rad;
        color += transmittance * Vector3.Lerp(JetColor, Vector3.One, 0.55f) * (beam * ObjBeams * 0.8f * dt);
    }

    // ---------------------------------------------------------------------------------------------- the march

    static Vector4 MarchObject(Vector3 x, Vector3 v)
    {
        float bound = Bound;
        float b = Vector3.Dot(x, v), c = Vector3.Dot(x, x) - bound * bound, disc = b * b - c;
        if (disc < 0) return Vector4.Zero;
        float sq = MathF.Sqrt(disc), tEnter = MathF.Max(0, -b - sq);
        if (-b + sq < 0) return Vector4.Zero;
        x += v * tEnter;
        float traveled = tEnter;
        Vector3 hv = Vector3.Cross(x, v);
        float h2 = Vector3.Dot(hv, hv);
        bool neutron = ObjKind == KindNeutronStar;
        float bend = neutron ? 1f : 0f;   // only a neutron star is compact enough to bend light visibly
        float inner = ObjInner, outer = ObjOuter, thick = ObjThick;
        Vector3 color = Vector3.Zero;
        float transmittance = 1f;
        bool solid = false;
        for (int k = 0; k < MaxSteps; k++)
        {
            float r = x.Length();
            if (r > bound * 1.001f && Vector3.Dot(x, v) > 0) break;
            if (r < ObjStarR)
            {
                if (neutron)
                {
                    // The neutron star's surface, seen through its own lensing: hot, brightest at the magnetic poles.
                    Vector3 n = x / r, vn0 = Vector3.Normalize(v);
                    float pole = MathF.Exp(-(1f - MathF.Abs(Vector3.Dot(n, ObjMag))) * 30f);
                    float limb = 0.55f + 0.45f * MathF.Abs(Vector3.Dot(n, vn0));
                    color += transmittance * (Saturate(Blackbody(ObjSurfaceT), 1.3f) * limb + Blackbody(ObjSurfaceT * 3f) * (pole * ObjHotspots));
                }
                else if (ObjKind != KindGalaxy)
                {
                    // Preview only: the star the game draws (the shader stops here and lets it show); OBJ_STARSTYLE as for "star".
                    string style = Environment.GetEnvironmentVariable("OBJ_STARSTYLE");
                    color += transmittance * StarSurface(x / r, Vector3.Normalize(v), ObjStarT, string.IsNullOrEmpty(style) ? StarSun : StyleOf(style), Time) * 3f;
                }
                solid = true;
                break;
            }
            float nearPlane = MathF.Abs(Vector3.Dot(x, N));
            float dt = MathF.Min(0.08f * r, neutron ? 2f : outer / 20f);
            Vector3 vn = Vector3.Normalize(v);
            float fp = traveled * PixelAngle;
            if (neutron)
            {
                if (nearPlane < 5f * thick * r && r < outer * 1.2f) dt = MathF.Min(dt, 0.2f);
                dt = MathF.Min(dt, MathF.Max(0.05f * r, 0.05f));
            }
            else if (ObjKind == KindNebula)
                dt = r < 1.2f * NebulaRadius ? 0.02f * NebulaRadius : MathF.Max(0.025f * NebulaRadius, r - 1.15f * NebulaRadius);
            else if (ObjKind == KindComet)
                dt = MathF.Min(MathF.Max(0.15f * r, 0.03f), MathF.Max(ObjIonLength, ObjDustLength) / 30f);
            else if (ObjKind == KindGalaxy)
            {
                // Fine steps through the thin disk and the bulge, long ones above and below.
                float hz = MathF.Max(ObjThick, 1e-3f) * NebulaRadius;
                dt = nearPlane > 4f * hz ? Math.Clamp(nearPlane - 3f * hz, 0.3f * hz, 0.12f * NebulaRadius) : MathF.Max(0.3f * hz, 0.004f * NebulaRadius);
                dt = MathF.Min(dt, MathF.Max(0.15f * ObjBulge * NebulaRadius, 0.25f * MathF.Max(r - ObjBulge * NebulaRadius, 0f)));
            }
            else if (nearPlane < 6f * thick * r && r < outer * 1.4f && r > inner * 0.5f)
                dt = MathF.Min(dt, MathF.Max(0.02f * r, ObjRockCell * 0.7f));   // dusty disks: fine steps through the layer
            if (!neutron && ObjFieldLines > 0f && r < ObjStarR * FieldShellStart * MathF.Pow(ObjFieldRatio, ObjFieldShells - 1f) * 1.1f)
                dt = MathF.Min(dt, MathF.Max(0.05f * r, 0.05f * ObjStarR));   // and through the field lines
            if (k == 0) dt *= Jitter;

            if (neutron)
            {
                AccumulateDisk(MainDisk(), x, v, dt, traveled, ref color, ref transmittance);
                AccumulateColumns(x, dt, ref color, ref transmittance);
                AccumulateFieldLines(x, dt, fp, ref color, transmittance);
                AccumulateBeams(x, dt, ref color, transmittance);
            }
            else if (ObjKind == KindNebula)
            {
                Nebula(x, out float gas, out float mix, out float filament, out float dust);
                Vector3 glowGas = Vector3.Lerp(ObjColorA, ObjColorB, mix) * gas + ObjColorA * (filament * 0.8f);
                color += transmittance * glowGas * dt;
                transmittance *= MathF.Exp(-(gas * 0.12f + dust * 1.6f) * dt);
                AccumulateStarGlow(x, dt, ref color, transmittance);   // a star inside, lighting it
            }
            else if (ObjKind == KindComet)
                AccumulateComet(x, dt, fp, ref color, ref transmittance);
            else if (ObjKind == KindGalaxy)
            {
                Galaxy(x, out Vector3 emit, out float absorb);
                color += transmittance * emit * dt;
                transmittance *= MathF.Exp(-absorb * dt);
            }
            else
            {
                AccumulateDustDisk(x, vn, dt, ref color, ref transmittance);
                if (ObjKind == KindProtoplanetary) AccumulateStarGlow(x, dt, ref color, transmittance);
                else AccumulateRocks(x, x + v * dt, traveled, ref color, ref transmittance);
            }
            if (!neutron) AccumulateFieldLines(x, dt, fp, ref color, transmittance);   // any star may have them
            if (transmittance < 0.01f) break;

            Vector3 a1 = -1.5f * bend * h2 * x / MathF.Pow(r, 5);
            Vector3 xm = x + v * (0.5f * dt);
            Vector3 vm = v + a1 * (0.5f * dt);
            float rm = MathF.Max(xm.Length(), 1e-3f);
            Vector3 am = -1.5f * bend * h2 * xm / MathF.Pow(rm, 5);
            x += vm * dt;
            v += am * dt;
            traveled += dt * vm.Length();
        }
        Vector3 outc = Tonemap(color * ObjBright);
        return new Vector4(LinearToSrgb(outc.X), LinearToSrgb(outc.Y), LinearToSrgb(outc.Z), solid ? 1f : 1f - transmittance);
    }

    // ---------------------------------------------------------------------------------------------- settings

    static Vector3 EnvColor(string name, Vector3 fallback)
    {
        string s = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(s)) return fallback;
        string[] p = s.Split(',');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
    }

    static Vector3 V(Double3 c) => new Vector3((float)c.X, (float)c.Y, (float)c.Z);

    /// <summary>
    /// Sets the shader's uniforms from an object's settings, as BlackHoleRenderer does: sizes in shader units (the length
    /// unit, or for a nebula a tenth of its radius), the star at the centre <paramref name="bodyRadius"/> metres across.
    /// </summary>
    static void ApplyObject(SettingsValues v, double bodyRadius)
    {
        var kind = v.Get(BlackHoleSettings.Kind);
        ObjKind = (int)kind;
        double unit = SpaceObjects.UnitMeters(kind, v.Get(BlackHoleSettings.HorizonRadius), bodyRadius, v.Get(BlackHoleSettings.BodyIsShadow), v.Get(BlackHoleSettings.StarRadius));
        double shaderMeters = SpaceObjects.ShaderUnitMeters(kind, unit, v.Get(BlackHoleSettings.DiskOuter));
        ObjStarR = kind == SpaceObjectKind.NeutronStar ? (float)v.Get(BlackHoleSettings.StarRadius) : (float)(bodyRadius / shaderMeters);
        float F(DoubleSetting s) => (float)v.Get(s);
        ObjInner = F(BlackHoleSettings.DiskInner); ObjOuter = F(BlackHoleSettings.DiskOuter); ObjThick = F(BlackHoleSettings.DiskThickness);
        ObjStarT = F(BlackHoleSettings.StarTemperature); ObjSurfaceT = F(BlackHoleSettings.SurfaceTemperature);
        ObjFlaring = F(BlackHoleSettings.Flaring); ObjDust = F(BlackHoleSettings.Dust); ObjFalloff = F(BlackHoleSettings.LightFalloff);
        ObjGlowSize = F(BlackHoleSettings.StarGlow); ObjRocks = F(BlackHoleSettings.Rocks); ObjRockSize = F(BlackHoleSettings.RockSize);
        ObjRockCell = F(BlackHoleSettings.RockSpacing) * ObjOuter; ObjRockAlbedo = F(BlackHoleSettings.RockAlbedo);
        float maxAngle = F(BlackHoleSettings.RockMaxAngle);
        ObjRockCap = maxAngle > 0f ? MathF.Tan(0.5f * maxAngle * MathF.PI / 180f) : 0f;
        ObjLanes = F(BlackHoleSettings.DustLanes); ObjCloudScale = F(BlackHoleSettings.CloudScale); ObjWarp = F(BlackHoleSettings.CloudWarp);
        ObjFilaments = F(BlackHoleSettings.Filaments); ObjShell = F(BlackHoleSettings.Shell);
        ObjFieldLines = F(BlackHoleSettings.FieldLines); ObjColumns = F(BlackHoleSettings.AccretionColumns); ObjBeams = F(BlackHoleSettings.Beams);
        ObjBeamLength = F(BlackHoleSettings.JetLength); ObjActivity = F(BlackHoleSettings.Activity); ObjComa = F(BlackHoleSettings.ComaSize);
        ObjIonLength = F(BlackHoleSettings.IonTailLength); ObjDustLength = F(BlackHoleSettings.DustTailLength);
        ObjDustCurve = F(BlackHoleSettings.DustTailCurve); ObjDustWidth = F(BlackHoleSettings.DustTailWidth);
        ObjBright = F(BlackHoleSettings.Brightness);
        ObjDensityFalloff = F(BlackHoleSettings.DensityFalloff); ObjEdgeSoftness = F(BlackHoleSettings.EdgeSoftness);
        ObjClumps = F(BlackHoleSettings.Clumps); ObjClumpSize = F(BlackHoleSettings.ClumpSize); ObjScatterG = F(BlackHoleSettings.ScatterAsymmetry);
        ObjRimGlow = F(BlackHoleSettings.RimGlow); ObjRimT = F(BlackHoleSettings.RimTemperature); ObjRockSpread = F(BlackHoleSettings.RockSpread);
        ObjSpiralArms = F(BlackHoleSettings.SpiralArms); ObjSpiralStrength = F(BlackHoleSettings.SpiralStrength);
        ObjSpiralWinding = F(BlackHoleSettings.SpiralWinding); ObjRockSizeSpread = F(BlackHoleSettings.RockSizeSpread);
        ObjCloudStretch = F(BlackHoleSettings.CloudStretch); ObjCloudRagged = F(BlackHoleSettings.CloudRagged); ObjCloudFill = F(BlackHoleSettings.CloudFill);
        ObjComaB = F(BlackHoleSettings.ComaBrightness); ObjIonB = F(BlackHoleSettings.IonBrightness); ObjDustB = F(BlackHoleSettings.DustBrightness);
        ObjNucleusB = F(BlackHoleSettings.NucleusBrightness); ObjIonWidth = F(BlackHoleSettings.IonTailWidth);
        ObjStreamers = F(BlackHoleSettings.Streamers); ObjStriae = F(BlackHoleSettings.Striae);
        ObjFieldCount = MathF.Round(F(BlackHoleSettings.FieldLineCount)); ObjFieldShells = MathF.Round(F(BlackHoleSettings.FieldShells));
        ObjFieldRatio = F(BlackHoleSettings.FieldShellRatio); ObjFieldWidth = F(BlackHoleSettings.FieldLineWidth);
        ObjColumnWidth = F(BlackHoleSettings.ColumnWidth); ObjBeamWidth = F(BlackHoleSettings.BeamWidth); ObjHotspots = F(BlackHoleSettings.Hotspots);
        ObjBulge = F(BlackHoleSettings.BulgeSize); ObjBulgeB = F(BlackHoleSettings.BulgeBrightness); ObjBar = F(BlackHoleSettings.BarLength);
        ObjClusters = F(BlackHoleSettings.StarClusters);
        ObjInnerColor = V(v.Get(BlackHoleSettings.InnerColor)); ObjOuterColor = V(v.Get(BlackHoleSettings.OuterColor));
        ObjColorA = V(v.Get(BlackHoleSettings.ColorA)); ObjColorB = V(v.Get(BlackHoleSettings.ColorB));
        ObjDustColor = V(v.Get(BlackHoleSettings.DustColor)); ObjOuterDust = V(v.Get(BlackHoleSettings.OuterDustColor));
        ObjRockColor = V(v.Get(BlackHoleSettings.RockColor)); ObjComaColor = V(v.Get(BlackHoleSettings.ComaColor));
        var gaps = new DiskGap[DiskGaps.Max];
        ObjGapCount = DiskGaps.Fill(ObjInner, ObjOuter, v.Get(BlackHoleSettings.Rings), v.Get(BlackHoleSettings.RingDepth), v.Get(BlackHoleSettings.RingWidth),
            v.Get(BlackHoleSettings.Gaps), v.Get(BlackHoleSettings.GapWidths), v.Get(BlackHoleSettings.GapDepths), gaps);
        for (int i = 0; i < ObjGapCount; i++) ObjGaps[i] = new Vector4((float)gaps[i].Radius, (float)gaps[i].Width, (float)gaps[i].Depth, 0f);

        // Turning: the disk at the configured speed (SpinRate rad/s at rotationSpeed 1); the magnetic axis tilted by
        // beamTilt and swept once every beamPeriod s (along the normal when 0).
        float spinSign = v.Get(BlackHoleSettings.Spin) < 0 ? -1f : 1f;
        ObjAngle = spinSign * F(BlackHoleSettings.RotationSpeed) * SpinRate * Time;
        float period = F(BlackHoleSettings.BeamPeriod);
        float tilt = period > 0f ? F(BlackHoleSettings.BeamTilt) * MathF.PI / 180f : 0f;
        float sweep = period > 0f ? Time * 2f * MathF.PI / period : 0f;
        ObjMag = Vector3.Normalize(N * MathF.Cos(tilt) + (ObjT * MathF.Cos(sweep) + ObjB * MathF.Sin(sweep)) * MathF.Sin(tilt));
        MaxSteps = v.Get(BlackHoleSettings.Steps);
        if (kind == SpaceObjectKind.NeutronStar)
        {
            // Its disk is drawn as a black hole's.
            InnerNow = ObjInner; OuterNow = ObjOuter; ThickNow = ObjThick;
            PeakK = F(BlackHoleSettings.PeakTemperature); Saturation = F(BlackHoleSettings.ColorSaturation);
            DiskTint = F(BlackHoleSettings.DiskTint); InnerBrightness = F(BlackHoleSettings.InnerBrightness);
            OuterBrightness = F(BlackHoleSettings.OuterBrightness); TintInner = V(v.Get(BlackHoleSettings.InnerColor));
            TintOuter = V(v.Get(BlackHoleSettings.OuterColor)); JetColor = V(v.Get(BlackHoleSettings.JetColor));
            JetNow = 0f; DiskPresence = 1f; DiskBright = 1f;
        }
        double bound = v.Get(BlackHoleSettings.BoundRadius);
        Bound = (float)(bound > 0 ? bound : SpaceObjects.Bound(kind, ObjOuter, ObjThick, ObjFlaring, ObjEdgeSoftness, ObjStarR, ObjFieldLines,
            ObjFieldShells, ObjFieldRatio, ObjBeams, ObjBeamLength, ObjComa, ObjIonLength, ObjDustLength, ObjDustCurve));
    }

    /// <summary>A kind's settings: its defaults, any key overridden by an OBJ_&lt;KEY&gt; environment variable.</summary>
    static SettingsValues EnvSettings(SpaceObjectKind kind)
    {
        var node = new CfgNode(BlackHoleSettings.Schema.NodeName).Add("body", "Preview").Add("kind", kind.ToString());
        foreach (SettingDef def in BlackHoleSettings.Schema.Definitions)
        {
            string value = Environment.GetEnvironmentVariable("OBJ_" + def.Key.ToUpperInvariant());
            if (!string.IsNullOrEmpty(value) && def.Key != "kind") node.Add(def.Key, value);
        }
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.ResolveWithKind(node, issues);
        foreach (ConfigIssue issue in issues) Console.WriteLine("config: " + issue);
        return v;
    }

    /// <summary>The <paramref name="slot"/>-th KSPVE_BLACKHOLE node for <paramref name="body"/> in a config file, resolved as the game does.</summary>
    static SettingsValues FileSettings(string path, string body, int slot)
    {
        var found = new List<CfgNode>();
        foreach (CfgNode node in CfgText.Parse(System.IO.File.ReadAllText(path)))
            Collect(node, body, found);
        if (slot >= found.Count) throw new ArgumentException($"{path} has {found.Count} KSPVE_BLACKHOLE node(s) for {body}, not slot {slot}");
        var issues = new List<ConfigIssue>();
        SettingsValues v = BlackHoleSettings.ResolveWithKind(found[slot], issues);
        foreach (ConfigIssue issue in issues) Console.WriteLine("config: " + issue);
        return v;
    }

    static void Collect(CfgNode node, string body, List<CfgNode> found)
    {
        if (node.Name == BlackHoleSettings.Schema.NodeName && node.GetLast("body")?.Trim() == body) found.Add(node);
        foreach (CfgNode child in node.Children) Collect(child, body, found);
    }

    /// <summary>object &lt;kind&gt; ... or object-cfg &lt;file&gt; &lt;body&gt; &lt;slot&gt; &lt;body radius&gt; ... (see the class summary).</summary>
    static int RenderObject(string[] args)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        ObjT = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        ObjB = Vector3.Cross(N, ObjT);
        int at;
        SettingsValues settings;
        double bodyRadius;
        string label;
        if (args[0] == "object-cfg")
        {
            bodyRadius = double.Parse(args[4], ci);
            label = $"{args[2]} #{args[3]}";
            at = 5;
            Time = args.Length > at + 5 ? float.Parse(args[at + 5], ci) : 0f;
            settings = FileSettings(args[1], args[2], int.Parse(args[3], ci));
        }
        else
        {
            var kind = args[1].ToLowerInvariant() switch
            {
                "neutron" => SpaceObjectKind.NeutronStar, "proto" => SpaceObjectKind.ProtoplanetaryDisk, "debris" => SpaceObjectKind.DebrisDisk,
                "belt" => SpaceObjectKind.AsteroidBelt, "nebula" => SpaceObjectKind.Nebula, "comet" => SpaceObjectKind.Comet,
                "galaxy" => SpaceObjectKind.Galaxy,
                _ => throw new ArgumentException(args[1])
            };
            label = kind.ToString();
            at = 2;
            Time = args.Length > at + 5 ? float.Parse(args[at + 5], ci) : 0f;
            settings = EnvSettings(kind);
            // The star is the unit (the body's radius), unless OBJ_BODYRADIUS says how many metres it is.
            bodyRadius = EnvFloat("OBJ_BODYRADIUS", (float)SpaceObjects.UnitMeters(kind, settings.Get(BlackHoleSettings.HorizonRadius), 1.0,
                false, settings.Get(BlackHoleSettings.StarRadius)));
            if (kind == SpaceObjectKind.Comet || kind == SpaceObjectKind.Nebula || kind == SpaceObjectKind.Galaxy) bodyRadius = EnvFloat("OBJ_BODYRADIUS", 0f);
        }
        ApplyObject(settings, bodyRadius);

        string path = args[at];
        int w = int.Parse(args[at + 1]), h = int.Parse(args[at + 2]);
        float inclination = float.Parse(args[at + 3], ci) * MathF.PI / 180f;
        // OBJ_CAM_UNITS puts the camera that many of the object's units away instead, so views of objects round one body line up.
        float camUnits = EnvFloat("OBJ_CAM_UNITS", 0f);
        float distance = camUnits > 0f ? camUnits : float.Parse(args[at + 4], ci) * Bound;
        float azimuth = EnvFloat("OBJ_AZ", 0f) * MathF.PI / 180f;
        ObjSunDir = Vector3.Normalize(EnvColor("OBJ_SUN", new Vector3(-1f, 0f, 0.15f)));
        ObjVelDir = Vector3.Normalize(EnvColor("OBJ_VEL", new Vector3(0f, 0f, 1f)));

        Vector3 camPos = new Vector3(MathF.Sin(azimuth) * MathF.Sin(inclination), MathF.Cos(inclination), -MathF.Cos(azimuth) * MathF.Sin(inclination)) * distance;
        Vector3 target = EnvColor("OBJ_TARGET", Vector3.Zero) * Bound;
        Vector3 forward = Vector3.Normalize(target - camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        if (float.IsNaN(right.X)) right = Vector3.UnitX;
        Vector3 up = Vector3.Cross(forward, right);
        float tanY = MathF.Tan(EnvFloat("BH_FOV", 60f) * 0.5f * MathF.PI / 180f), tanX = tanY * w / h;
        PixelAngle = 2f * tanY / h;
        bool sky = Environment.GetEnvironmentVariable("OBJ_SKY") == "1";
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w, vv = 1f - (py + 0.5f) / h;
                Vector3 dir = Vector3.Normalize(forward + right * (u * 2 - 1) * tanX + up * (vv * 2 - 1) * tanY);
                Jitter = 0.2f + 0.8f * Frac(MathF.Sin(px * 12.9898f + py * 78.233f) * 43758.5453f);
                Vector4 c = MarchObject(camPos, dir);
                if (sky)
                {
                    // A starfield behind, as the game's skybox would be (premultiplied over it).
                    Vector3 bg = Tonemap(StarSky(dir));
                    c = new Vector4(c.X + (1f - c.W) * LinearToSrgb(bg.X), c.Y + (1f - c.W) * LinearToSrgb(bg.Y), c.Z + (1f - c.W) * LinearToSrgb(bg.Z), 1f);
                }
                int i = (py * w + px) * 3;
                rgb[i] = ToByte(c.X); rgb[i + 1] = ToByte(c.Y); rgb[i + 2] = ToByte(c.Z);
            }
        });
        WritePng(path, w, h, rgb);
        Console.WriteLine($"wrote {path}: {label} (bound {Bound:0.###} units)");
        return 0;
    }
}

/// <summary>
/// The procedural planner (KSPVE.Pure.Space.ProceduralObjects) run on a system written out as a table, the way the game
/// runs it on its bodies:  procedural &lt;bodies.tsv&gt; &lt;out.cfg&gt; [seed]. Columns: name, parent, star (1/0), radius (m),
/// semi-major axis (m), eccentricity. Every rule is on (outer disks always); the plan is written as KSPVE_BLACKHOLE nodes.
/// </summary>
internal static class ProceduralTool
{
    public static int Run(string[] args)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var bodies = new List<SystemBody>();
        foreach (string line in System.IO.File.ReadAllLines(args[1]))
        {
            if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
            string[] c = line.Split('	');
            bodies.Add(new SystemBody
            {
                Name = c[0], Parent = c[1], IsStar = c[2] == "1", Radius = double.Parse(c[3], ci),
                SemiMajorAxis = double.Parse(c[4], ci), Eccentricity = double.Parse(c[5], ci)
            });
        }
        var rules = new CfgNode("KSPVE_PROCEDURAL").Add("enabled", "True").Add("outerDiskChance", "1")
            .Add("seed", args.Length > 3 ? args[3] : "1");
        List<CfgNode> plan = ProceduralObjects.Plan(bodies, ProceduralObjects.Schema.Resolve(rules, new List<ConfigIssue>()), new HashSet<string>());
        System.IO.File.WriteAllText(args[2], CfgWriter.Write(plan));
        foreach (CfgNode n in plan)
            Console.WriteLine($"{n.GetLast("body")}: {n.GetLast("kind")} {n.GetLast("diskInner")}..{n.GetLast("diskOuter")}");
        return 0;
    }
}

/// <summary>
/// Reads KSP config text into CfgNodes: "key = value", "NAME { ... }" (the name may sit on its own line above the brace),
/// // comments. Module Manager's operators and :NEEDS / :FINAL suffixes are dropped from names and keys.
/// </summary>
internal static class CfgText
{
    public static List<CfgNode> Parse(string text)
    {
        var root = new CfgNode("root");
        var stack = new Stack<CfgNode>();
        stack.Push(root);
        string pendingName = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line.Substring(0, comment);
            line = line.Trim();
            while (line.Length > 0)
            {
                int open = line.IndexOf('{'), close = line.IndexOf('}'), eq = line.IndexOf('=');
                if (open >= 0 && (eq < 0 || open < eq) && (close < 0 || open < close))
                {
                    string name = line.Substring(0, open).Trim();
                    if (name.Length == 0) name = pendingName ?? "";
                    pendingName = null;
                    var node = new CfgNode(Clean(name));
                    stack.Peek().AddChild(node);
                    stack.Push(node);
                    line = line.Substring(open + 1).Trim();
                    continue;
                }
                if (close >= 0 && (eq < 0 || close < eq))
                {
                    if (stack.Count > 1) stack.Pop();
                    line = line.Substring(close + 1).Trim();
                    continue;
                }
                if (eq >= 0)
                {
                    string value = line.Substring(eq + 1);
                    int end = value.IndexOf('}');
                    string rest = end >= 0 ? value.Substring(end) : string.Empty;
                    if (end >= 0) value = value.Substring(0, end);
                    stack.Peek().Add(Clean(line.Substring(0, eq).Trim()), value.Trim());
                    line = rest.Trim();
                    continue;
                }
                pendingName = line;   // a node's name, its brace on the next line
                line = string.Empty;
            }
        }
        return root.Children;
    }

    private static string Clean(string name)
    {
        name = name.TrimStart('@', '%', '+', '$', '-', '!', '&', '|', '#').Trim();
        int colon = name.IndexOf(':');
        if (colon >= 0) name = name.Substring(0, colon);
        int bracket = name.IndexOf('[');
        return bracket >= 0 ? name.Substring(0, bracket) : name;
    }
}
