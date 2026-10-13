// CPU port of unity/KSPVEShaders/Assets/Shaders/Space/BlackHole.shader, line for line, to preview the
// image without launching KSP.
// Usage: dotnet run -- <out.png> [width] [height] [inclinationDeg] [distance] [time]
//        dotnet run -- debug
using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading.Tasks;

internal static partial class Program
{
    // Kcalbeloh defaults (KSPVE_BLACKHOLE_Kcalbeloh.cfg)
    static readonly float Inner = EnvFloat("BH_INNER", 3f), Outer = EnvFloat("BH_OUTER", 14f), Thickness = EnvFloat("BH_THICK", 0.12f);
    static readonly float Turbulence = EnvFloat("BH_TURB", 0.8f), DiskEvenness = EnvFloat("BH_EVEN", 0f);
    const float Spin = 1f, RotationSpeed = 1f;
    const float Ring = 1f;
    static readonly float Glow = EnvFloat("BH_GLOW", 1f);
    // Look, overridable for tuning: BH_PEAK (kelvin), BH_SAT (colour saturation) and BH_BRIGHT environment variables.
    // The disk's parts (diskTint, innerColor, outerColor, innerBrightness, outerBrightness, jetColor, photonRingColor).
    static float DiskTint = EnvFloat("BH_TINT", 0f), InnerBrightness = EnvFloat("BH_INNERBRIGHT", 1f), OuterBrightness = EnvFloat("BH_OUTERBRIGHT", 1f);
    static Vector3 TintInner = EnvVec("BH_INNERCOLOR", new Vector3(1f, 0.85f, 0.6f)), TintOuter = EnvVec("BH_OUTERCOLOR", new Vector3(1f, 0.45f, 0.15f));
    static Vector3 JetColor = EnvVec("BH_JETCOLOR", new Vector3(0.3f, 0.55f, 1f)), RingColor = EnvVec("BH_RINGCOLOR", new Vector3(0.9f, 0.95f, 1f));
    static float Luma(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    static Vector3 EnvVec(string name, Vector3 fallback)
    {
        string s = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(s)) return fallback;
        string[] p = s.Split(',');
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
    }
    // A custom texture (texture, textureMode, textureStrength, textureShape): BH_TEXTURE=file.png for the hole's disk.
    static TextureFile Tex;
    static float TexMode, TexStrength = 1f, TexShape = 0.5f;
    static Vector4 TexAt(float px, float py, float r, float inner, float outer) =>
        TexMode < 1.5f ? Tex.Sample(0.5f + px / (2f * outer), 0.5f + py / (2f * outer)) : Tex.Sample(Math.Clamp((r - inner) / MathF.Max(outer - inner, 1e-6f), 0f, 1f), 0.5f);
    static Vector3 TexColor(Vector3 c, Vector4 t)
    {
        var rgb = new Vector3(t.X, t.Y, t.Z);
        return Vector3.Lerp(c, rgb * (Luma(c) / MathF.Max(Luma(rgb), 1e-3f)), TexStrength);
    }
    static float TexShapeOf(Vector4 t) => 1f + (Math.Clamp(Luma(new Vector3(t.X, t.Y, t.Z)) * 1.6f, 0f, 1f) * t.W - 1f) * TexShape;

    // Mass transfer from a companion (KSPVE.Pure.Space.RocheStream): BH_COMPANION_Q (mass ratio), BH_COMPANION_A (separation,
    // horizon radii), BH_COMPANION_ANGLE (degrees round the disk), BH_STREAM, BH_STREAMWIDTH, BH_HOTSPOT; the companion
    // itself drawn too (preview only; the game draws the body), filling its Roche lobe unless BH_COMPANION_R says.
    const int StreamBallistic = 24, StreamPoints = 25;   // 1 out of the star, 24 falling (the winding on is a spiral)
    // Its winding on in (BlackHole.shader's _KSPVE_StreamSpiral...): BH_STREAMTURNS turns from where it strikes (BH_STREAMRING,
    // horizon radii, or the disk's edge) in to BH_SPIRALTO of that radius.
    static float SpR0, SpR1, SpAngle, SpA0, SpSense, SpW0, SpW1, SpFall, SpCurve = 1f;
    static Vector3 StreamEx = Vector3.UnitX;
    // Its look (streamFlatness, streamOpacity, streamFlowLines, streamFlowScale, streamFray, companionPull, streamHeat), and
    // a winding drawn by hand (BH_STREAMSHAPE, as streamShape).
    static float StreamFlat = 2.5f, StreamOpac = 2f, FlowLines = 1f, FlowScale = 1f, Fray = 0.35f, Pull = 1f, StreamHeat = 0.6f;
    static readonly Vector4[] Drawn = new Vector4[KSPVE.Pure.Space.StreamShape.MaxPoints];
    static int DrawnCount;
    static readonly Vector4[] StreamPts = new Vector4[StreamPoints];
    static int StreamCount, StreamImpact;
    static float StreamBright, HotSpotBright, StreamRadius, CompanionR, CompanionT;
    static Vector3 StreamCenter, StreamColor = new Vector3(0.8f, 0.9f, 1f), CompanionPos;
    // The drawn companion (BlackHole.shader's _KSPVE_CompanionLobe / _KSPVE_CompanionAxis): CompanionR the sphere holding it.
    static float LobeQ, LobeL1, LobePhi, CompanionReq;
    static Vector3 CompanionAxis = Vector3.UnitY;

    static void InitStream()
    {
        float q = EnvFloat("BH_COMPANION_Q", 0f), a = EnvFloat("BH_COMPANION_A", 0f);
        if (q <= 0f || a <= 0f) return;
        float angle = EnvFloat("BH_COMPANION_ANGLE", 0f) * MathF.PI / 180f, height = EnvFloat("BH_COMPANION_HEIGHT", 0f) * MathF.PI / 180f;
        Vector3 t = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX)), b = Vector3.Cross(N, t);
        Vector3 ex = (t * MathF.Cos(angle) + b * MathF.Sin(angle)) * MathF.Cos(height) + N * MathF.Sin(height);
        Vector3 ey = Vector3.Normalize(Vector3.Cross(N, ex)) * (Spin < 0 ? -1f : 1f);   // the companion orbits the way the disk turns
        float ring = EnvFloat("BH_STREAMRING", 0f);
        var path = KSPVE.Pure.Space.RocheStream.Path(q, (ring > 0f ? ring : OuterNow) / a, StreamBallistic);
        // What it winds on as (BlackHoleRenderer.StreamPath): worked out, the fall eased into it an eighth of a turn round, or
        // drawn by hand (BH_STREAMSHAPE), the fall eased into the drawing's start.
        var spiral = KSPVE.Pure.Space.RocheStream.SpiralOf(path, Math.Min(EnvFloat("BH_STREAMTURNS", 0f), 16f), EnvFloat("BH_SPIRALTO", 0.85f), EnvFloat("BH_CURVE", 1f));
        var drawnPts = KSPVE.Pure.Space.StreamShape.Resample(KSPVE.Pure.Space.StreamShape.Parse(Environment.GetEnvironmentVariable("BH_STREAMSHAPE")), KSPVE.Pure.Space.StreamShape.MaxPoints);
        DrawnCount = drawnPts.Count >= 2 ? drawnPts.Count : 0;
        double strike = path[path.Count - 1].Length;
        var fall = path;
        if (DrawnCount > 0)
            fall = KSPVE.Pure.Space.RocheStream.EaseInto(path, 16, drawnPts[0] * strike, drawnPts[1] - drawnPts[0], StreamBallistic - 16);
        else if (spiral.Turns > 0.0)
        {
            double t0 = Math.Min(KSPVE.Pure.Space.RocheStream.EaseAngle / (2.0 * Math.PI * spiral.Turns), 0.5);
            fall = KSPVE.Pure.Space.RocheStream.EaseInto(path, 16, spiral.At(t0), spiral.DirectionAt(t0), StreamBallistic - 16);
            spiral = spiral.From(t0);
        }
        StreamCount = Math.Min(fall.Count + 1, StreamPoints);
        StreamImpact = StreamCount - 1;
        float width = EnvFloat("BH_STREAMWIDTH", 0.05f);
        float reach = 0f;
        // Filling its Roche lobe (no BH_COMPANION_R) it is drawn out to a point at L1, the farthest of it from its centre.
        float radiusSet = EnvFloat("BH_COMPANION_R", 0f);
        CompanionReq = radiusSet > 0f ? radiusSet : (float)KSPVE.Pure.Space.RocheStream.LobeRadius(q) * a;
        LobeL1 = (float)KSPVE.Pure.Space.RocheStream.L1FromCompact(q);
        LobeQ = radiusSet > 0f ? 0f : q;
        LobePhi = (float)KSPVE.Pure.Space.RocheStream.LobePotential(q);
        CompanionR = LobeQ > 0f ? MathF.Max((1f - LobeL1) * a, CompanionReq) : CompanionReq;
        CompanionAxis = Vector3.Normalize(Vector3.Cross(ex, ey));
        // Out of the star (BlackHoleRenderer.StreamPath): a funnel from inside it out to L1, wide where it leaves the star, and
        // a broad bridge tapering as it falls.
        float from = LobeQ > 0f ? CompanionReq : MathF.Min(CompanionReq, (1f - (float)path[0].X) * a);
        float bridge = EnvFloat("BH_BRIDGE", 0.3f) * from;
        StreamFlat = EnvFloat("BH_STREAMFLAT", 2.5f); StreamOpac = EnvFloat("BH_STREAMOPACITY", 2f); FlowLines = EnvFloat("BH_FLOWLINES", 1f);
        FlowScale = EnvFloat("BH_FLOWSCALE", 1f); Fray = EnvFloat("BH_FRAY", 0.35f); Pull = EnvFloat("BH_PULL", 1f); StreamHeat = EnvFloat("BH_HEAT", 0.6f);
        for (int i = 1; i < StreamCount; i++)
        {
            Vector3 w = (ex * (float)fall[i - 1].X + ey * (float)fall[i - 1].Y) * a;
            float u = MathF.Min((i - 1f) / MathF.Max(StreamImpact - 1, 1), 1f);
            StreamPts[i] = new Vector4(w, MathF.Max(0.5f * width * a * (1f + 1.5f * u), bridge * (1f - u)));   // as wide on round the disk as where it struck
        }
        StreamPts[0] = new Vector4(ex * (a - 0.6f * from), MathF.Max(0.5f * from, StreamPts[1].W));
        // Winding on in (BlackHoleRenderer.StreamPath).
        SpCurve = (float)spiral.Curve;
        SpFall = 0f;
        for (int i = 0; i + 1 < StreamCount; i++) SpFall += (new Vector3(StreamPts[i + 1].X, StreamPts[i + 1].Y, StreamPts[i + 1].Z) - new Vector3(StreamPts[i].X, StreamPts[i].Y, StreamPts[i].Z)).Length();
        SpR0 = (float)spiral.R0 * a; SpR1 = (float)spiral.R1 * a; SpAngle = (float)(2.0 * Math.PI * spiral.Turns); SpA0 = (float)spiral.A0;
        SpSense = (float)spiral.Sense; SpW0 = StreamPts[StreamCount - 1].W; SpW1 = EnvFloat("BH_TAPER", 0.6f) * SpW0;
        for (int k = 0; k < DrawnCount; k++)
            Drawn[k] = new Vector4((ex * (float)drawnPts[k].X + ey * (float)drawnPts[k].Y) * (float)(strike * a), SpW0 + (SpW1 - SpW0) * k / (DrawnCount - 1f));
        if (DrawnCount > 0) SpAngle = 0f;
        StreamEx = ex;
        for (int i = 0; i < StreamCount; i++) StreamCenter += new Vector3(StreamPts[i].X, StreamPts[i].Y, StreamPts[i].Z) / StreamCount;
        for (int i = 0; i < StreamCount; i++)
        {
            var p = new Vector3(StreamPts[i].X, StreamPts[i].Y, StreamPts[i].Z);
            StreamRadius = MathF.Max(StreamRadius, (p - StreamCenter).Length() + 4f * StreamPts[i].W);
            reach = MathF.Max(reach, p.Length() + 3f * StreamPts[i].W);
        }
        for (int k = 0; k < DrawnCount; k++)
        {
            var p = new Vector3(Drawn[k].X, Drawn[k].Y, Drawn[k].Z);
            StreamRadius = MathF.Max(StreamRadius, (p - StreamCenter).Length() + 4f * SpW0);
            reach = MathF.Max(reach, p.Length() + 3f * SpW0);
        }
        if (SpAngle > 0f)
            for (int k = 0; k <= 64; k++)
            {
                var sp = spiral.At(k / 64.0);
                Vector3 p = (ex * (float)sp.X + ey * (float)sp.Y) * a;
                StreamRadius = MathF.Max(StreamRadius, (p - StreamCenter).Length() + 4f * SpW0);
                reach = MathF.Max(reach, p.Length() + 3f * SpW0);
            }
        StreamBright = EnvFloat("BH_STREAM", 1f);
        HotSpotBright = EnvFloat("BH_HOTSPOT", 1f);
        StreamColor = EnvVec("BH_STREAMCOLOR", StreamColor);
        CompanionPos = ex * a;
        CompanionT = EnvFloat("BH_COMPANION_T", 9000f);
        Bound = MathF.Max(Bound, MathF.Max(reach, a + 1.6f * CompanionR));
    }

    /// <summary>The drawn companion's surface (BlackHole.shader's CompanionSurface): its own brightness, whatever the disk's.</summary>
    static Vector3 CompanionSurface(Vector3 n, Vector3 vn)
    {
        float mu = MathF.Max(Vector3.Dot(n, -vn), 0f), edge = 1f - mu;
        float limb = 1f - 0.6f * edge - 0.2f * edge * edge;
        float granules = 0.84f + 0.32f * Fbm(n * 22f + new Vector3(Time * 0.01f));
        granules *= 1f - 0.8f * EnvFloat("BH_COMPANION_SPOTS", 0.5f) * SmoothStep(0.68f, 0.74f, Fbm(n * 7f + new Vector3(3.3f)));   // a few small, round dark starspots
        Vector3 c = Saturate(Blackbody(CompanionT), 1.5f), cool = Saturate(Blackbody(CompanionT * 0.8f), 1.5f);
        c /= MathF.Max(Luma(c), 1e-4f);
        cool /= MathF.Max(Luma(cool), 1e-4f);
        if (StreamCount > 0)
        {
            // Being pulled off: towards the hole its gas is drawn out into flow lines converging on the point the stream
            // leaves from, in the stream's colour.
            Vector3 toHole = -Vector3.Normalize(CompanionPos);
            float tip = Vector3.Dot(n, toHole);
            float pull = SmoothStep(0.2f, 0.9f, tip) * Pull;
            Vector3 e2 = Vector3.Normalize(Vector3.Cross(toHole, MathF.Abs(toHole.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), e3 = Vector3.Cross(toHole, e2);
            float around = MathF.Atan2(Vector3.Dot(n, e2), Vector3.Dot(n, e3));
            float lines = 0.55f + 0.9f * Noise3(new Vector3(MathF.Cos(around) * 9f, MathF.Sin(around) * 9f, MathF.Acos(Math.Clamp(tip, 0f, 1f)) * 1.5f - Time * 0.3f));
            granules += (lines - granules) * pull;
            Vector3 sc = StreamColor / MathF.Max(Luma(StreamColor), 1e-4f);
            c = Vector3.Lerp(c, sc, 0.6f * pull);
            cool = Vector3.Lerp(cool, sc, 0.6f * pull);
        }
        return Vector3.Lerp(c, cool, edge * 0.6f) * (limb * granules * 2.0f * EnvFloat("BH_COMPANION_BRIGHT", 1f) / MathF.Max(Brightness, 1e-3f));
    }

    /// <summary>Whether x is inside the drawn companion (BlackHole.shader's InCompanion), its outward normal, and the safe step.</summary>
    static bool InCompanion(Vector3 x, out Vector3 normal, out float dist)
    {
        Vector3 dc = x - CompanionPos;
        float dcl = dc.Length(), R = CompanionR;
        normal = dc / MathF.Max(dcl, 1e-6f);
        dist = MathF.Max(dcl - R, 0.02f * R) + 0.001f;
        if (dcl >= R) return false;
        if (LobeQ <= 0f) return true;
        float a = CompanionPos.Length();
        Vector3 ex = CompanionPos / a, ez = CompanionAxis, ey = Vector3.Cross(ez, ex);
        var p = new Vector3(Vector3.Dot(x, ex), Vector3.Dot(x, ey), Vector3.Dot(x, ez)) / a;   // the hole at 0, the companion at (1, 0, 0)
        float m1 = 1f / (1f + LobeQ), m2 = LobeQ / (1f + LobeQ);
        Vector3 d2 = p - Vector3.UnitX, c = new Vector3(p.X - m2, p.Y, 0f);
        float r1 = MathF.Max(p.Length(), 1e-6f), r2 = MathF.Max(d2.Length(), 1e-6f);
        Vector3 g = p * (m1 / (r1 * r1 * r1)) + d2 * (m2 / (r2 * r2 * r2)) - c;
        float excess = -m1 / r1 - m2 / r2 - 0.5f * c.LengthSquared() - LobePhi;
        normal = Vector3.Normalize(ex * g.X + ey * g.Y + ez * g.Z);
        dist = Math.Clamp(0.8f * excess / MathF.Max(g.Length(), 1e-4f) * a, 0.004f * CompanionReq, 0.1f * CompanionReq);
        return excess < 0f && p.X > LobeL1;
    }

    /// <summary>The glare round the drawn companion (BlackHole.shader's CompanionGlare), per unit length at distance d.</summary>
    static Vector3 CompanionGlare(float d)
    {
        float glare = EnvFloat("BH_COMPANION_GLARE", 1f), R = CompanionReq;
        if (R <= 0f || glare <= 0f || d < R) return Vector3.Zero;
        Vector3 c = Saturate(Blackbody(CompanionT * 1.3f), 1.6f);
        c /= MathF.Max(Luma(c), 1e-4f);
        float hh = (d - R) / R;
        return c * (glare * (0.5f * MathF.Exp(-hh / 0.08f) + 0.02f * MathF.Exp(-hh / 0.5f)) / R / MathF.Max(Brightness, 1e-3f));
    }

    // The hole's disk's rings (BH_RINGS, BH_RINGDEPTH, BH_RINGWIDTH, or BH_GAPS / BH_GAPWIDTHS / BH_GAPDEPTHS by hand).
    static int HoleGapCount = -1;
    static readonly Vector4[] HoleGaps = new Vector4[KSPVE.Pure.Space.DiskGaps.Max];
    static float HoleGapFactor(float r)
    {
        if (HoleGapCount < 0)
        {
            var gaps = new KSPVE.Pure.Space.DiskGap[KSPVE.Pure.Space.DiskGaps.Max];
            HoleGapCount = KSPVE.Pure.Space.DiskGaps.Fill(InnerNow, OuterNow, EnvFloat("BH_RINGS", 0f), EnvFloat("BH_RINGDEPTH", 0.97f), EnvFloat("BH_RINGWIDTH", 0.02f),
                Environment.GetEnvironmentVariable("BH_GAPS"), Environment.GetEnvironmentVariable("BH_GAPWIDTHS"), Environment.GetEnvironmentVariable("BH_GAPDEPTHS"), gaps);
            for (int i = 0; i < HoleGapCount; i++) HoleGaps[i] = new Vector4((float)gaps[i].Radius, (float)gaps[i].Width, (float)gaps[i].Depth, 0f);
        }
        float f = 1f;
        for (int k = 0; k < HoleGapCount; k++) f *= 1f - HoleGaps[k].Z * MathF.Exp(-Sq((r - HoleGaps[k].X) / HoleGaps[k].Y));
        return f;
    }

    // The stream's glow per unit optical depth (how opaque it is: streamOpacity).
    const float StreamSurface = 1.2f;

    /// <summary>The stream at x (BlackHole.shader's StreamSample): its opaque gas (extinction tubeK, glow per unit optical
    /// depth tubeColor), the glows round L1 and the hot spot per unit length (returned), and the step that cannot skip it.</summary>
    static Vector3 StreamSample(Vector3 x, float fp, out float limit, out float tubeK, out Vector3 tubeColor)
    {
        limit = 1e9f;
        tubeK = 0f;
        tubeColor = Vector3.Zero;
        if (StreamCount < 2) return Vector3.Zero;
        float reach = (x - StreamCenter).Length() - StreamRadius;
        if (reach > 0f) { limit = MathF.Max(reach, StreamPts[0].W); return Vector3.Zero; }
        Vector3 ez = CompanionAxis;
        float best = 1e9f, bestU = 0f, bestW = 1f, bestD = 1e9f, bestS = 0f, arc = 0f, bestTrue = 1e9f, bestFlat = 1f;
        Vector3 bestO = Vector3.Zero, bestDir = Vector3.UnitX;
        bool wound = false;
        // Falling: round as it leaves the star, spreading flat as it nears where it strikes.
        for (int s = 0; s + 1 < StreamCount; s++)
        {
            Vector4 a = StreamPts[s], b = StreamPts[s + 1];
            var pa = new Vector3(a.X, a.Y, a.Z);
            Vector3 ab = new Vector3(b.X, b.Y, b.Z) - pa;
            float tt = Math.Clamp(Vector3.Dot(x - pa, ab) / MathF.Max(Vector3.Dot(ab, ab), 1e-12f), 0f, 1f);
            Vector3 o = x - (pa + ab * tt);
            float uu = (s + tt) / (StreamCount - 1f);
            float squash = 1f + (StreamFlat - 1f) * SmoothStep(0.3f, 1f, uu);
            Vector3 band = o + ez * (Vector3.Dot(o, ez) * (squash - 1f));
            float d = band.Length();
            float w = a.W + (b.W - a.W) * tt;
            float len = ab.Length();
            if (d / w < best) { best = d / w; bestD = d; bestTrue = o.Length(); bestW = w; bestU = uu; bestS = arc + tt * len; bestO = band; bestDir = ab; bestFlat = squash; }
            arc += len;
        }
        // Winding on in round the hole: for each winding the point's angle could be on, the spiral's radius there.
        if (SpAngle > 0f)
        {
            Vector3 ex = StreamEx, ey = Vector3.Cross(ez, ex);
            float px = Vector3.Dot(x, ex), py = Vector3.Dot(x, ey), h = Vector3.Dot(x, ez);
            float rr = MathF.Max(MathF.Sqrt(px * px + py * py), 1e-6f);
            float delta = Frac(SpSense * (MathF.Atan2(py, px) - SpA0) / (2f * MathF.PI)) * 2f * MathF.PI;
            Vector3 radial = (ex * px + ey * py) / rr;
            for (int k = 0; k < 17; k++)
            {
                float th = delta + 2f * MathF.PI * k;
                bool atEnd = th >= SpAngle;
                th = MathF.Min(th, SpAngle);   // past its end: its rounded end
                float f = th / SpAngle;
                float ringR = SpR1 + (SpR0 - SpR1) * MathF.Pow(MathF.Max(1f - f, 0f), SpCurve), w = SpW0 + (SpW1 - SpW0) * f;
                Vector3 o = radial * (rr - ringR) + ez * h;
                if (atEnd) o = x - (ex * (ringR * MathF.Cos(SpA0 + SpSense * th)) + ey * (ringR * MathF.Sin(SpA0 + SpSense * th)));
                Vector3 band = o + ez * (Vector3.Dot(o, ez) * (StreamFlat - 1f));   // a flat band: thinner out of the plane
                float d = band.Length();
                if (d / w < best)
                {
                    best = d / w; bestD = d; bestTrue = o.Length(); bestW = w; bestO = band; wound = true; bestFlat = StreamFlat;
                    bestS = SpFall + th * 0.5f * (SpR0 + ringR);
                    bestDir = Vector3.Cross(ez, radial) * SpSense;
                }
                if (atEnd) break;
            }
        }
        // Or winding on as drawn by hand, from where it strikes.
        if (DrawnCount >= 2)
        {
            Vector4 prev = StreamPts[StreamImpact];
            float drawnArc = SpFall;
            for (int j = 0; j < DrawnCount; j++)
            {
                Vector4 b = Drawn[j];
                var pa = new Vector3(prev.X, prev.Y, prev.Z);
                Vector3 ab = new Vector3(b.X, b.Y, b.Z) - pa;
                float tt = Math.Clamp(Vector3.Dot(x - pa, ab) / MathF.Max(Vector3.Dot(ab, ab), 1e-12f), 0f, 1f);
                Vector3 o = x - (pa + ab * tt);
                Vector3 band = o + ez * (Vector3.Dot(o, ez) * (StreamFlat - 1f));
                float d = band.Length();
                float w = prev.W + (b.W - prev.W) * tt;
                float len = ab.Length();
                if (d / w < best) { best = d / w; bestD = d; bestTrue = o.Length(); bestW = w; bestO = band; bestDir = ab; bestS = drawnArc + tt * len; wound = true; bestFlat = StreamFlat; }
                drawnArc += len;
                prev = b;
            }
        }
        float ww = MathF.Sqrt(bestW * bestW + fp * fp);
        limit = MathF.Max(bestTrue - 2.5f * ww, 0.3f * ww / bestFlat);
        // Where across it x is (in its widths: in the orbit's plane and out of it), and how far along it (in its widths), so its
        // texture is drawn out along the flow: fine flow lines, gas being pulled along, not billowing cloud.
        Vector3 flowDir = Vector3.Normalize(bestDir + new Vector3(1e-6f));
        Vector3 sideDir = Vector3.Normalize(Vector3.Cross(ez, flowDir) + new Vector3(1e-6f));
        float acrossX = Vector3.Dot(bestO, sideDir) / ww, acrossY = Vector3.Dot(bestO, ez) / ww;
        float along = bestS / ww;
        float lines = 0.25f + 1.5f * SmoothStep(0.3f, 0.7f, Fbm(new Vector3(along * 0.25f * FlowScale - Time * 0.6f, acrossX * 3f * FlowScale, acrossY * 3f * FlowScale)));
        float turb = 1f + (lines - 1f) * FlowLines;
        // Hotter (brighter) the further it has fallen; past where it strikes it carries on, one continuous stream, winding in
        // round the hole.
        float impactU = MathF.Max(StreamImpact / (StreamCount - 1f), 1e-3f);
        float fallen = wound ? 1f : MathF.Min(bestU / impactU, 1f);
        float heat = 1f + StreamHeat * fallen * fallen;
        // Its edge frays into the flow lines; thinner than a pixel it fades rather than growing.
        float fluff = Fbm(new Vector3(along * 0.12f * FlowScale - Time * 0.6f, acrossX * 1.2f * FlowScale, acrossY * 1.2f * FlowScale) + new Vector3(4.1f));
        float body = 1f - SmoothStep(0.55f, 1.15f, bestD / ww + Fray * (fluff - 0.5f));
        tubeK = StreamOpac * body * (bestW / ww) / ww * bestFlat;   // a band as much gas as the tube
        tubeColor = StreamColor * (StreamBright * heat * turb * StreamSurface);
        Vector3 emit = Vector3.Zero;
        Vector4 first = StreamPts[1], last = StreamPts[StreamImpact];
        var pf = new Vector3(first.X, first.Y, first.Z);
        var pl = new Vector3(last.X, last.Y, last.Z);
        float r0 = 2f * first.W, rs = 1.5f * last.W;
        float r0p = MathF.Sqrt(r0 * r0 + fp * fp), rsp = MathF.Sqrt(rs * rs + fp * fp);
        emit += StreamColor * (StreamBright * 0.1f * MathF.Exp(-Vector3.Dot(x - pf, x - pf) / (r0p * r0p)) / r0p);
        emit += new Vector3(0.85f, 0.92f, 1f) * (HotSpotBright * 0.15f * MathF.Exp(-Vector3.Dot(x - pl, x - pl) / (rsp * rsp)) * (rs * rs) / (rsp * rsp) / rs);
        limit = MathF.Min(limit, MathF.Max((x - pl).Length() - 3f * rsp, 0.3f * rsp));
        return emit;
    }

    // The spiral jets' strands (jetStrands, jetStrandSharpness).
    static float JetStrands = EnvFloat("BH_JETSTRANDS", 12f), JetStrandSharpness = EnvFloat("BH_JETSHARP", 1f);
    // Jets breaking into blobs (jetKnots, jetKnotStart, jetKnotSpacing, jetKnotGrowth).
    static float JetKnots = EnvFloat("BH_JETKNOTS", 0f), JetKnotStart = EnvFloat("BH_JETKNOTSTART", 0.3f),
        JetKnotSpacing = EnvFloat("BH_JETKNOTSPACING", 0.1f), JetKnotGrowth = EnvFloat("BH_JETKNOTGROWTH", 1f);
    static float PeakK = EnvFloat("BH_PEAK", 40000f), Saturation = EnvFloat("BH_SAT", 3.4f), Brightness = EnvFloat("BH_BRIGHT", 3.5f);   // an object's config may set them
    static float EnvFloat(string name, float fallback) =>
        float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
    static int MaxSteps = (int)EnvFloat("BH_STEPS", 300f);
    // Beyond this radius (horizon radii) the disk's clumps, and the march's steps through it, grow with the radius, so a
    // disk of any size looks alike at every scale and costs about the same to draw.
    const float DetailRadius = 20f;
    // Display aid: the outer disk (beyond DetailRadius) shown as if exposed for it, lit by the inner disk (0 = physical).
    static readonly float OuterGlow = EnvFloat("BH_OUTERGLOW", 0f);
    [ThreadStatic] static Vector3 CloudPw1;
    [ThreadStatic] static float CloudBlend;
    // Jets along the spin axis: strength (BH_JET, 0 = off), length (horizon radii), collimation (width = JetWidth * z^0.55),
    // gas speed (fraction of c) and the visual outward flow of the knots (horizon radii per second).
    static readonly float JetStrength = EnvFloat("BH_JET", 1.3f);
    static readonly float JetLength = EnvFloat("BH_JET_LEN", 60f), JetWidth = EnvFloat("BH_JET_W", 0.9f), JetSpeed = EnvFloat("BH_JET_V", 0.85f);
    // Spiral (0 = plain column, 1 = solid spiral core), turns of the swirl along the jet, outward speed of the pattern at
    // mid-jet (horizon radii per second) and how much the jet gas absorbs (optical depth across one jet radius per unit density).
    static readonly float JetSpiral = EnvFloat("BH_JET_SPIRAL", 1f), JetTurns = EnvFloat("BH_JET_TURNS", 4f);
    static readonly float JetFlowMid = EnvFloat("BH_JET_FLOW", 30f), JetAbsorb = EnvFloat("BH_JET_ABSORB", 0.6f);
    // The pattern lives in zeta = z^0.45, where the swirl's pitch keeps pace with the widening jet (radius ~ z^0.55), so
    // the swirl keeps its angle and the flow speeds up outward. Twist in radians per unit zeta; flow in zeta per second.
    static readonly float JetTwist = 2f * MathF.PI * JetTurns / (MathF.Pow(JetLength, 0.45f) - MathF.Pow(1.1f, 0.45f));
    static readonly float JetFlow = JetFlowMid * 0.45f / MathF.Pow(JetLength * 0.5f, 0.55f);
    // With the spiral the jet is a thick tube from its rounded base: its radius follows (z + JetBase)^0.55.
    const float JetBase = 12f;
    // Blobs (jetKnots) reach a quarter further out and swell well past the jet's width.
    static readonly float KnotLen = JetKnots > 0f ? 1.6f : 1f, KnotWide = JetKnots > 0f ? 2.5f + JetKnotGrowth : 1f;
    static readonly float BaseBound = MathF.Max(Outer * 1.25f + 8f, JetStrength > 0
        ? MathF.Sqrt(Sq(JetLength * KnotLen) + MathF.Pow(3f * JetWidth * KnotWide * MathF.Pow(JetLength * KnotLen + JetBase * JetSpiral, 0.55f), 2f)) + 4f : 0f);
    static float Bound = BaseBound;
    // The disk and jets as they are now (BlackHoles.StateOf): as configured, or as far as a tidal disruption has built them.
    static float DiskPresence = 1f, DiskBright = 1f, InnerNow = Inner, OuterNow = Outer, ThickNow = Thickness;
    static float MaskIn, MaskOut, MaskSoft;
    static readonly float Streaks = EnvFloat("BH_STREAK", 0f), Winding = EnvFloat("BH_WIND", 5f);
    static readonly float StreakRing = EnvFloat("BH_RING", 2f), StreakRadial = EnvFloat("BH_RADIAL", 1.6f);
    static readonly float StreakStretch = EnvFloat("BH_STRETCH", 30f);
    // Streaks fade in gradually as the view rises over the disk: none below StreakFrom degrees above its plane, growing
    // smoothly to all of them at StreakFull.
    static readonly float StreakFrom = EnvFloat("BH_STREAK_FROM", 10f), StreakFull = EnvFloat("BH_STREAK_FULL", 85f);
    [ThreadStatic] static float FaceOn;
    // Seen from above: dark lanes between the streaks, the streaks hotter (whiter); the photon ring fainter.
    static readonly float StreakGap = EnvFloat("BH_GAP", 0.85f), StreakHeat = EnvFloat("BH_SHEAT", 0.8f), StreakRingDim = EnvFloat("BH_RINGDIM", 1f);
    static readonly float StreakGlowDim = EnvFloat("BH_GLOWDIM", 0.8f), StreakFine = EnvFloat("BH_FINE", 6f);
    // Face on, the whole thickness of glowing gas is in view and the disk would burn out to a flat sheet: its light is
    // toned down to this, at full streaks, so the arcs read.
    static readonly float StreakExposure = EnvFloat("BH_FACEEXP", 0.35f);
    static readonly float ArcLo = EnvFloat("BH_ARCLO", 0.25f), ArcHi = EnvFloat("BH_ARCHI", 0.6f);
    static readonly float StreakSaturation = EnvFloat("BH_FACESAT", 2.8f);
    static readonly float StreakSoftRim = EnvFloat("BH_SOFTRIM", 0.8f);   // band a forming disk fills (MaskSoft 0 = all of it)
    static float JetNow = JetStrength, JetLengthNow = JetLength, DiskSpiral = 0f, JetHeads = 0f, LaunchFlash = 0f;
    static readonly Vector3 N = Vector3.UnitY; // disk normal in the preview frame
    static float Time = 0f;
    static float PixelAngle = 0.002f; // radians per pixel, so particles never shrink below a pixel
    static readonly float ParticleAmount = EnvFloat("BH_PARTICLES", 1f), DebrisAmount = EnvFloat("BH_DEBRIS", 1f);

    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "object" || args[0] == "object-cfg")) return RenderObject(args);
        if (args.Length > 0 && args[0] == "star") return RenderStar(args);
        if (args.Length > 0 && args[0] == "starfx") return StarFx.Run(args);
        if (args.Length > 0 && args[0] == "structure") return StructurePreview.Run(args);
        if (args.Length > 0 && args[0] == "procedural") return ProceduralTool.Run(args);
        if (args.Length > 0 && args[0] == "tde-info") return TdeInfo.Run(args);
        if (args.Length > 0 && args[0] == "eve-info") return EveInfo.Run(args);
        if (args.Length > 0 && args[0] == "browndwarf") return BrownDwarfPreview.Run(args);
        if (args.Length > 0 && args[0] == "aurora") return AuroraPreview.Run(args);
        if (args.Length > 0 && args[0] == "snow") return SnowPreview.Run(args);
        if (args.Length > 0 && (args[0] == "water-ocean" || args[0] == "water-rivers")) return WaterPreview.Run(args);
        if (args.Length > 0 && args[0] == "tde") return RenderTde(args);
        if (args.Length > 0 && args[0] == "tde-svg") return TdeSvg.Run(args);
        if (args.Length > 0 && args[0] == "palette")
        {
            foreach (float k in new[] { 2500f, 3500f, 4500f, 6000f, 7500f, 9000f })
            {
                Vector3 c = Saturate(Blackbody(k));
                Console.Write($"{k,6}K lin=({c.X:F2},{c.Y:F2},{c.Z:F2}) srgb:");
                foreach (float e in new[] { 0.5f, 1f, 2f, 4f, 8f })
                {
                    Vector3 m = Tonemap(c * e);
                    Console.Write($" [{(int)(LinearToSrgb(m.X) * 255)},{(int)(LinearToSrgb(m.Y) * 255)},{(int)(LinearToSrgb(m.Z) * 255)}]");
                }
                Console.WriteLine();
            }
            return 0;
        }
        if (args.Length > 0 && args[0] == "debug")
        {
            Vector3 cam = new Vector3(0, MathF.Cos(80f * MathF.PI / 180f), -MathF.Sin(80f * MathF.PI / 180f)) * 32f;
            foreach (float side in new[] { 5f, -5f })
            {
                Vector3 d = Vector3.Normalize(new Vector3(side, 0, 0) - cam);
                Jitter = 0.5f;
                Vector4 c = Trace(cam, d);
                Console.WriteLine($"x={side,5}: color=({c.X:F3},{c.Y:F3},{c.Z:F3}) alpha={c.W:F3}");
            }
            return 0;
        }

        if (args.Length > 0 && args[0] == "anim")
        {
            // anim <prefix> w h inclination distance frames fps: numbered frames of the spinning disk.
            int frames = int.Parse(args[6]), fps = int.Parse(args[7]);
            for (int f = 0; f < frames; f++)
                Main(new[] { $"{args[1]}{f:D3}.png", args[2], args[3], args[4], args[5], (f / (float)fps).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return 0;
        }

        string path = args.Length > 0 ? args[0] : "bh-preview.png";
        int w = args.Length > 1 ? int.Parse(args[1]) : 640;
        int h = args.Length > 2 ? int.Parse(args[2]) : 360;
        float inclination = (args.Length > 3 ? float.Parse(args[3]) : 80f) * MathF.PI / 180f;
        float distance = args.Length > 4 ? float.Parse(args[4]) : 32f;
        Time = args.Length > 5 ? float.Parse(args[5]) : 0f;

        SkyOn = Environment.GetEnvironmentVariable("BH_SKY") == "1";   // a starfield behind, lensed by the hole
        Tex = TextureFile.Load(Environment.GetEnvironmentVariable("BH_TEXTURE"));
        TexMode = Tex != null ? EnvFloat("BH_TEXMODE", 1f) : 0f;
        TexStrength = EnvFloat("BH_TEXSTRENGTH", 1f);
        TexShape = EnvFloat("BH_TEXSHAPE", 0.5f);
        InitStream();
        // Camera looking at the hole from 'distance' horizon radii, 'inclination' from the disk normal.
        Vector3 camPos = new Vector3(0, MathF.Cos(inclination), -MathF.Sin(inclination)) * distance;
        Vector3 forward = Vector3.Normalize(-camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        if (float.IsNaN(right.X)) right = Vector3.UnitX;
        Vector3 up = Vector3.Cross(forward, right);
        float roll = EnvFloat("BH_ROLL", 0f) * MathF.PI / 180f;   // the camera turned about its view
        (right, up) = (right * MathF.Cos(roll) + up * MathF.Sin(roll), up * MathF.Cos(roll) - right * MathF.Sin(roll));
        float tanY = MathF.Tan(EnvFloat("BH_FOV", 60f) * 0.5f * MathF.PI / 180f), tanX = tanY * w / h;   // vertical field of view
        PixelAngle = 2f * tanY / h;
        // Where in the frame the hole appears (-1..1 across, -1..1 up; 0,0 = the middle): an off-centre lens, as a crop
        // of a larger picture, to frame it like a reference image.
        float shiftX = EnvFloat("BH_SHIFTX", 0f), shiftY = EnvFloat("BH_SHIFTY", 0f);

        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w, v = 1f - (py + 0.5f) / h;
                Vector3 dir = Vector3.Normalize(forward + right * (u * 2 - 1 - shiftX) * tanX + up * (v * 2 - 1 - shiftY) * tanY);
                Jitter = 0.2f + 0.8f * Frac(MathF.Sin(px * 12.9898f + py * 78.233f) * 43758.5453f);
                Vector4 c = Trace(camPos, dir);
                // The shader writes sRGB-encoded colour premultiplied over the scene; composite over black here.
                int i = (py * w + px) * 3;
                rgb[i] = ToByte(c.X); rgb[i + 1] = ToByte(c.Y); rgb[i + 2] = ToByte(c.Z);
            }
        });
        WritePng(path, w, h, rgb);
        Console.WriteLine($"wrote {path} ({w}x{h}, inclination {inclination * 180 / MathF.PI:F0} deg, distance {distance} r_s)");
        return 0;
    }

    static byte ToByte(float encoded) => (byte)Math.Clamp((int)(encoded * 255f + 0.5f), 0, 255);

    static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    [ThreadStatic] static float Jitter;
    [ThreadStatic] static int LastSteps;
    [ThreadStatic] static string LastExit;
    [ThreadStatic] static System.Text.StringBuilder RayLog;

    static Vector4 Trace(Vector3 x, Vector3 v)
    {
        // How high the camera sits above the disk's plane, seen from the hole (degrees): the same for every ray of a view.
        FaceOn = MathF.Asin(Math.Clamp(MathF.Abs(Vector3.Dot(Vector3.Normalize(x), N)), 0f, 1f)) * 180f / MathF.PI;
        float b = Vector3.Dot(x, v), c = Vector3.Dot(x, x) - Bound * Bound, disc = b * b - c;
        if (disc < 0) return SkyOnly(v);
        float s = MathF.Sqrt(disc), tEnter = MathF.Max(0, -b - s);
        if (-b + s < 0) return SkyOnly(v);
        x += v * tEnter;
        float traveled = tEnter;
        Vector3 hv = Vector3.Cross(x, v);
        float h2 = Vector3.Dot(hv, hv);
        Vector3 color = Vector3.Zero;
        float transmittance = 1f;
        float envDepth = 0f;   // the outflow's haze passed through so far (capped: TdeRender.EnvelopeMaxDepth)
        bool captured = false;
        float rMin = 1e9f;
        Vector3 hazeColor = Saturate(Blackbody(PeakK * 0.6f));
        if (DiskTint > 0f) hazeColor = Vector3.Lerp(hazeColor, TintInner * (Luma(hazeColor) / MathF.Max(Luma(TintInner), 1e-4f)), DiskTint);
        LastExit = "steps";
        // A disk bigger than DetailRadius gets more steps: rays may skim hundreds of horizon radii of it.
        int steps = MaxSteps + (OuterNow > DetailRadius ? (int)(100f * MathF.Log(OuterNow / DetailRadius)) : 0);
        for (int k = 0; k < steps; k++)
        {
            LastSteps = k;
            float r = x.Length();
            rMin = MathF.Min(rMin, r);
            if (r < 1f) { captured = true; LastExit = "captured"; break; }
            float starStep = 1e9f;
            if (CompanionR > 0f && InCompanion(x, out Vector3 cn, out starStep))
            {
                // The companion star the shader draws (a planet pack's is the game's body), lensed like everything else.
                color += transmittance * CompanionSurface(cn, Vector3.Normalize(v));
                captured = true; LastExit = "companion"; break;
            }
            if (r > Bound * 1.001f && Vector3.Dot(x, v) > 0) { LastExit = "escaped"; break; }
            float nearPlane = MathF.Abs(Vector3.Dot(x, N));
            // A disk bigger than DetailRadius: its clumps, and so the steps, grow with the radius out there. Smaller disks
            // keep the fixed steps they were tuned with.
            float grain = OuterNow > DetailRadius ? MathF.Max(1f, r / DetailRadius) : 1f;
            float dtCap = TdeOn && r > TdeCore ? 0.08f * r : 2f * grain;   // beyond the disk & jets the event may need long strides
            float dt = Math.Clamp(MathF.Min(0.08f * r, 0.35f * MathF.Max(nearPlane, 0.3f * Thickness * r)), 0.004f, dtCap);
            // Fine steps inside the cloud layer: about three per clump out where the clumps are big.
            if (nearPlane < 5f * ThickNow * r && r < OuterNow * 1.2f) dt = MathF.Min(dt, 0.2f * grain * MathF.Min(grain, 3f));
            if (JetNow > 0 && nearPlane > 1.1f * (1f - JetSpiral) && nearPlane < JetLengthNow * (JetKnots > 0f ? 1.6f : 1f))   // and inside the jets
            {
                float jetR = JetWidth * MathF.Pow(nearPlane + JetBase * JetSpiral, 0.55f);
                if ((x - Vector3.Dot(x, N) * N).Length() < (JetKnots > 0f ? 7f : 3f) * jetR) dt = MathF.Min(dt, (JetKnots > 0f ? 0.12f : 0.35f - 0.15f * JetSpiral - 0.12f * Math.Clamp((JetStrandSharpness - 1f) / 5f, 0f, 1f)) * jetR);   // blobs' thin shells and fine threads need finer steps
            }
            float tdeK = 0f, envK = 0f; Vector3 tdeEmit = Vector3.Zero, tdeGlow = Vector3.Zero, envEmit = Vector3.Zero;
            if (TdeOn) dt = MathF.Min(dt, MathF.Max(TdeSample(x, Vector3.Normalize(v), traveled, out tdeK, out tdeEmit, out tdeGlow, out envK, out envEmit), 0.0005f));
            float streamLimit;
            Vector3 streamGlow = StreamSample(x, traveled * PixelAngle, out streamLimit, out float streamK, out Vector3 streamColor);
            dt = MathF.Min(dt, MathF.Max(streamLimit, 0.0005f));
            if (CompanionR > 0f) dt = MathF.Min(dt, starStep);
            if (k == 0) dt *= Jitter;
            if (envK > 0f)
            {
                float ke = MathF.Min(envK, MathF.Max(EnvelopeMaxDepth - envDepth, 0f) / dt);
                envDepth += ke * dt;
                tdeK += ke;
                tdeEmit += envEmit * (ke / envK);
            }
            RayLog?.AppendLine($"{k}: r={r:0.0} dt={dt:0.###} tdeK={tdeK:0.###}");

            // Glow: hot, thin plasma filling the space around the hole (lensed along with everything else).
            color += transmittance * hazeColor * (Glow * (1f - StreakGlowDim * Streaks * SmoothStep(StreakFrom, StreakFull, FaceOn)) * DiskPresence * (1f + 3f * LaunchFlash) * 0.03f * MathF.Exp(-(r - 1f) / 2.2f)) * dt;
            AccumulateDisk(MainDisk(), x, v, dt, traveled, ref color, ref transmittance);
            AccumulateJets(x, v, dt, ref color, ref transmittance);
            {
                // The companion's stream onto the disk: its tube, exact for a uniform step (it is often opaque), and the glows.
                float sa = 1f - MathF.Exp(-streamK * dt);
                color += transmittance * (streamColor * sa + streamGlow * dt);
                transmittance *= 1f - sa;
            }
            color += transmittance * CompanionGlare((x - CompanionPos).Length()) * dt;
            if (tdeK > 0f || tdeGlow != Vector3.Zero)
            {
                // Exact for a uniform slab: the event's gas is often far too opaque for emission * dt.
                float a = 1f - MathF.Exp(-tdeK * dt);
                color += transmittance * (tdeEmit / MathF.Max(tdeK, 1e-12f) * a + tdeGlow * dt);
                transmittance *= 1f - a;
            }
            if (transmittance < 0.01f) { LastExit = "opaque"; break; }

            Vector3 a1 = -1.5f * h2 * x / MathF.Pow(r, 5);
            Vector3 xm = x + v * (0.5f * dt), vm = v + a1 * (0.5f * dt);
            float rm = MathF.Max(xm.Length(), 1e-3f);
            Vector3 am = -1.5f * h2 * xm / MathF.Pow(rm, 5);
            x += vm * dt; v += am * dt;
            traveled += dt * vm.Length();
        }

        // Photon ring: rays that skim the photon sphere (r = 1.5) before escaping pile up light from every
        // orbit around the hole, giving a razor-thin bright ring at the edge of the shadow.
        if (!captured)
        {
            float d = (rMin - 1.5f) / 0.045f;
            float faceOn = Streaks * SmoothStep(StreakFrom, StreakFull, FaceOn);   // fainter seen from above, as the streaks come in
            color += RingColor * (Ring * (1f - StreakRingDim * faceOn) * 8f * MathF.Exp(-d * d));
        }

        // The sky behind, seen along the bent ray: in the game this is KSP's skybox (lensed by Singularity).
        if (SkyOn && !captured) color += transmittance * StarSky(Vector3.Normalize(v)) / Brightness;
        if (!captured) color += transmittance * EnvVec("BH_BG", Vector3.Zero) / Brightness;   // a background haze (preview only)
        color = Tonemap(color * Brightness);
        color = new Vector3(LinearToSrgb(color.X), LinearToSrgb(color.Y), LinearToSrgb(color.Z));
        return new Vector4(color, captured ? 1f : 1f - transmittance);
    }

    const float Absorb = 0.35f, ShadowAbsorb = 0.9f, LightStrength = 3f, DensityScale = 45f, CloudScale = 0.45f, EmissionStrength = 3.6f;

    /// <summary>The whole disk turns together at the orbital rate of its inner edge (rad/s): the outer disk is
    /// sped up to match, so nothing shears into swirls.</summary>
    static float SpinRate => RotationSpeed * 0.75f; // once every ~8 s at rotationSpeed 1

    /// <summary>
    /// A cloudy, self-shadowed disk (the hole's own, MainDisk). Ecc makes it eccentric about the hole (circles in r + e (q . Peri)); Cloud sets the clump size.
    /// </summary>
    struct DiskP
    {
        public Vector3 N, T, B, Peri;
        public float Spin, Inner, Outer, Thick, Turb, Angle, Peak, Presence, Bright, Cloud, Ecc, Particles, Debris, Cell, Spiral;
        public float MaskIn, MaskOut, MaskSoft;
        public float Streak, Winding, Face;
    }

    static DiskP MainDisk()
    {
        Vector3 t = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        return new DiskP
        {
            N = N, T = t, B = Vector3.Cross(N, t), Peri = t, Spin = Spin, Inner = InnerNow, Outer = OuterNow, Thick = ThickNow,
            Turb = Turbulence, Angle = Spin * SpinRate * Time, Peak = PeakK, Presence = DiskPresence, Bright = DiskBright, Cloud = CloudScale,
            Ecc = 0f, Particles = ParticleAmount * DiskPresence, Debris = DebrisAmount * DiskPresence, Cell = ParticleCell, Spiral = DiskSpiral,
            MaskIn = MaskIn, MaskOut = MaskOut, MaskSoft = MaskSoft,
            Streak = Streaks, Winding = Winding,
            Face = Streaks * SmoothStep(StreakFrom, StreakFull, FaceOn)
        };
    }

    /// <summary>Shape of the disk at x (horizon radii), without the hot clumps. The light march uses this alone;
    /// pw is the point's position in the disk's rotating frame, for the hot clumps; re its (eccentric) radius.</summary>
    static float DiskShape(in DiskP d, Vector3 x, out float re, out Vector3 pw) => DiskShape(d, x, out re, out pw, out _);

    static float DiskShape(in DiskP d, Vector3 x, out float re, out Vector3 pw, out Vector3 ps)
    {
        pw = Vector3.Zero;
        ps = Vector3.Zero;
        float height = Vector3.Dot(x, d.N);
        Vector3 q = x - height * d.N;
        float r = q.Length();
        re = r + d.Ecc * Vector3.Dot(q, d.Peri);
        if (re < d.Inner * 0.8f || re > d.Outer * 1.1f) return 0f;
        float ht = d.Thick * re;
        if (MathF.Abs(height) > 3f * ht) return 0f;
        // The outer disk thins into translucent wisps, or (diskEdge) ends in a crisp edge.
        float edgeAmount = EnvFloat("BH_EDGE", 0f);
        float outerFade = (1 - SmoothStep(d.Outer * 0.35f, d.Outer, re)) * (1 - edgeAmount) + (1 - SmoothStep(d.Outer * 0.93f, d.Outer, re)) * edgeAmount;
        // Seen from above the inner rim fades in softly, so the dark centre has a soft edge.
        float radial = SmoothStep(d.Inner * 0.85f, d.Inner * (1.1f + StreakSoftRim * d.Face), re) * outerFade * outerFade;
        if (d.MaskSoft > 0f)
            radial *= SmoothStep(d.MaskIn - d.MaskSoft, d.MaskIn + d.MaskSoft, re) * (1f - SmoothStep(d.MaskOut - d.MaskSoft, d.MaskOut + d.MaskSoft, re));
        if (radial <= 0) return 0f;
        radial *= HoleGapFactor(re);   // rings (gaps), as a dusty disk's

        float phi = MathF.Atan2(Vector3.Dot(q, d.B), Vector3.Dot(q, d.T));
        float angle = phi - d.Angle;

        // Puffy, irregular surface: big rounded lumps with smaller billows on them bulge the disk's half-height.
        float cloud = 1f - d.Face, warp = 0f, lumps = 0f, billows = 0f;
        CloudBlend = 0f;
        if (cloud > 0f)
        {
            // True 3D position in the rotating frame. Beyond DetailRadius the clumps grow with the radius: the noise is
            // taken at the two power-of-two scales around the wanted one and blended, so it stays seamless.
            Vector3 pd = new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), height);
            float level = MathF.Log2(MathF.Max(1f, re / DetailRadius));
            int k0 = (int)level;
            float blend = level - k0;
            Clouds(pd, k0, d.Cloud, out pw, out float w0, out float l0, out float b0);
            if (blend > 0.001f)
            {
                Clouds(pd, k0 + 1, d.Cloud, out Vector3 pw1, out float w1, out float l1, out float b1);
                w0 += (w1 - w0) * blend; l0 += (l1 - l0) * blend; b0 += (b1 - b0) * blend;
                CloudPw1 = pw1;
                CloudBlend = blend;
            }
            warp = w0 * cloud;
            lumps = l0 * cloud;
            billows = b0 * cloud;
        }
        if (d.Face > 0f)
        {
            // Seen from above, the gas is sheared along its orbit (the inner disk orbits faster): its clumps are drawn out
            // into long arcs winding into a trailing spiral. The noise runs round a circle (no seam) whose size sets how
            // many arcs go round; the radius runs along the third axis, so neighbouring orbits differ.
            float stretch = 1f + StreakStretch;
            float a = angle + d.Spin * d.Winding * MathF.Log(re / d.Inner);
            float ring = StreakRing / MathF.Sqrt(stretch);
            ps = new Vector3(ring * MathF.Cos(a), ring * MathF.Sin(a),
                (StreakRadius(re) * StreakRadial + 0.8f * height * DetailRadius / MathF.Max(re, DetailRadius)) * d.Cloud);
            lumps += Fbm(ps) * d.Face;
            billows += (1f - MathF.Abs(2f * Noise3(ps * 2.7f + new Vector3(5.1f)) - 1f)) * d.Face;
        }
        float surface = 0.5f + d.Turb * (1.3f * lumps + 0.3f * billows);
        float hAbs = MathF.Abs(height / ht - warp);
        float body = 1f - SmoothStep(surface - 0.3f, surface, hAbs); // 1 inside, rounded edge at the surface
        if (d.Spiral > 0f)
        {
            // While it forms, two spiral shocks wind through it (gas still settling onto circular orbits).
            float arms = 0.5f + 0.5f * MathF.Cos(2f * angle - 5f * MathF.Log(re / d.Inner + 1f) + 1.3f * lumps);
            body *= MathF.Max(1f + d.Spiral * (1.8f * arms - 0.9f), 0f);
        }

        return body * radial * (DensityScale / re) * d.Presence;
    }

    /// <summary>The disk's cloud noise at scale level (2^-level of the inner disk's clump frequency), with its warped position.</summary>
    static void Clouds(Vector3 pd, int level, float cloudScale, out Vector3 pw, out float warp, out float lumps, out float billows)
    {
        Vector3 pc = pd * (cloudScale * MathF.Pow(2f, -level)) + new Vector3(17.3f, 5.1f, 9.7f) * level;
        Vector3 w = new Vector3(Noise3(pc * 0.5f + new Vector3(11.3f)), Noise3(pc * 0.5f + new Vector3(27.1f)), Noise3(pc * 0.5f + new Vector3(41.7f))) - new Vector3(0.5f);
        pw = pc + w * 0.5f;
        warp = (Noise3(pc * 0.3f + new Vector3(3.3f)) - 0.5f) * 0.8f;
        lumps = Fbm(pw);
        billows = 1f - MathF.Abs(2f * Noise3(pw * 2.7f + new Vector3(5.1f)) - 1f);
    }

    /// <summary>The radius the streak pattern runs on: the radius itself, then logarithmic beyond DetailRadius.</summary>
    static float StreakRadius(float r) => r <= DetailRadius ? r : DetailRadius * (1f + MathF.Log(r / DetailRadius));

    static void AccumulateDisk(in DiskP d, Vector3 x, Vector3 v, float dt, float traveled, ref Vector3 color, ref float transmittance)
    {
        float shape = DiskShape(d, x, out float r, out Vector3 pw, out Vector3 ps);
        if (shape < 1e-3f) return;
        // From above the streak pattern varies across the disk only, not up through it, so it stays smooth.
        Vector3 flat = new Vector3(ps.X, ps.Y, StreakRadius(r) * StreakRadial * d.Cloud);
        float streakHeat = d.Face > 0f ? Fbm(flat * 1.3f + new Vector3(9.1f)) : 0f;
        float cloudHeat = 0f;
        if (d.Face < 1f)
        {
            cloudHeat = Fbm(pw * 1.3f + new Vector3(9.1f));
            if (CloudBlend > 0f) cloudHeat += (Fbm(CloudPw1 * 1.3f + new Vector3(9.1f)) - cloudHeat) * CloudBlend;
        }
        float heat = cloudHeat * (1f - d.Face) + streakHeat * d.Face;
        float density = shape * (0.6f + 0.4f * heat); // hot clumps inside a continuous body: no holes
        float lane = 1f;
        if (d.Face > 0f)
        {
            // From above: a few big bright arcs, finely striated along the orbit, with dark lanes between them. The gas is
            // too thick for thinner lanes to show, so the light dims with them too.
            float arcs = SmoothStep(ArcLo, ArcHi, streakHeat);
            float fine = 1f - MathF.Abs(2f * Noise3(new Vector3(flat.X * 1.5f, flat.Y * 1.5f, flat.Z * StreakFine) + new Vector3(7.7f)) - 1f);
            float fineWeight = 0.85f;
            lane = 1f - d.Face * StreakGap * (1f - arcs * (1f - fineWeight + fineWeight * fine * fine));
            density *= lane;
            lane *= 1f - d.Face * (1f - StreakExposure);
        }

        // Self-shadowing towards the hot inner disk: lit flanks face the hole, crevices and far sides fall dark.
        // Two shape-only samples (average clump density) keep this cheap; they reach as far as the clumps are big.
        Vector3 toHole = -Vector3.Normalize(x);
        float reach = CloudScale / d.Cloud;
        float d1 = DiskShape(d, x + toHole * (0.4f * reach), out _, out _);
        float d2 = DiskShape(d, x + toHole * (1.2f * reach), out _, out _);
        float lightTransmittance = MathF.Exp(-(d1 * 0.55f + d2 * 0.85f) * 0.8f * ShadowAbsorb * reach);

        Vector3 vn = Vector3.Normalize(v);
        float cosScatter = Vector3.Dot(-toHole, -vn);
        const float hgG = 0.35f;
        float phase = 0.25f + 0.75f * (1 - hgG * hgG) / MathF.Pow(1 + hgG * hgG - 2 * hgG * cosScatter, 1.5f);
        float incident = LightStrength * MathF.Pow(d.Inner / MathF.Max(r, d.Inner), 2f);
        // outerGlow: beyond DetailRadius the disk is shown as if exposed for it, lit by the inner disk: warmed by its light
        // (an irradiated disk's temperature falls only as r^-1/2), the inverse-square falloff made up, and the light it
        // scatters in the inner disk's colour.
        float outer = OuterGlow * SmoothStep(DetailRadius, 3f * DetailRadius, r);
        float exposure = 1f + outer * (Sq(r / DetailRadius) - 1f);
        incident *= exposure;

        float knots = SmoothStep(0.45f, 0.75f, heat) * d.Turb;
        float temperature = DiskTemperature(r, d.Inner, d.Peak) * (0.9f + (0.3f + StreakHeat * d.Face) * knots);
        if (outer > 0f) temperature = MathF.Max(temperature, d.Peak * 0.3f * MathF.Sqrt(d.Inner / r) * (0.9f + 0.3f * knots) * MathF.Min(outer, 1f));
        Vector3 orbit = Vector3.Normalize(Vector3.Cross(d.N, x - Vector3.Dot(x, d.N) * d.N)) * d.Spin;
        float beta = MathF.Min(MathF.Sqrt(0.5f / MathF.Max(r - 1f, 0.05f)), 0.95f);
        float gamma = 1f / MathF.Sqrt(1 - beta * beta);
        float doppler = 1f / (gamma * (1 - beta * Vector3.Dot(orbit, -vn)));
        float grav = MathF.Sqrt(Math.Clamp(1 - 1f / x.Length(), 0f, 1f));
        float g = doppler * grav;
        float beaming = g * g * g;

        // Hot plasma in its own blackbody colour at the Doppler-shifted temperature: white-hot where it races towards
        // you, then yellow, gold and deep orange further out. Brighter in the hot clumps, darker in the shadowed
        // crevices, plus light from the inner disk scattered off the bulges.
        // From above the colour is softer: the bright streaks go white-blue rather than cyan.
        Vector3 hot = Saturate(Blackbody(temperature * g), Saturation + (StreakSaturation - Saturation) * d.Face);
        // The disk's parts by hand (diskTint): colours shading from the inner to the outer edge (keeping each part's
        // brightness), and brightness inside and out, over the log of the radius.
        float span = Math.Clamp(MathF.Log(r / d.Inner) / MathF.Max(MathF.Log(d.Outer / d.Inner), 1e-3f), 0f, 1f);
        if (DiskTint > 0f)
        {
            Vector3 tint = Vector3.Lerp(TintInner, TintOuter, span);
            hot = Vector3.Lerp(hot, tint * (Luma(hot) / MathF.Max(Luma(tint), 1e-4f)), DiskTint);
        }
        float partBrightness = InnerBrightness + (OuterBrightness - InnerBrightness) * span;
        if (Tex != null && TexMode > 0.5f)
        {
            Vector3 tp = ToDiskFrame(d, x);
            Vector4 tx = TexAt(tp.X, tp.Y, MathF.Sqrt(tp.X * tp.X + tp.Y * tp.Y), d.Inner, d.Outer);
            hot = TexColor(hot, tx);
            density *= TexShapeOf(tx);
        }
        float shade = 0.3f + 0.7f * lightTransmittance;
        // diskEvenness (display): the cooler outer disk shown as bright as the hottest part, keeping its clumps.
        float glowFall = MathF.Pow(temperature / d.Peak, 3);
        glowFall += (MathF.Pow(temperature / MathF.Max(DiskTemperature(r, d.Inner, d.Peak), 1f), 3) - glowFall) * DiskEvenness;
        Vector3 thermal = hot * (glowFall * EmissionStrength * (0.6f + 1.4f * knots) * shade * exposure);
        Vector3 lightColor = outer > 0f ? Vector3.Lerp(hot, Saturate(Blackbody(d.Peak * 0.8f)), MathF.Min(outer, 1f)) : hot;
        Vector3 scattered = lightColor * (incident * lightTransmittance * phase * 0.4f);
        color += transmittance * (thermal + scattered) * (d.Bright * beaming * density * lane * dt * partBrightness);
        transmittance *= MathF.Exp(-density * Absorb * dt);

        AccumulateParticles(d, x, x + v * dt, traveled, Math.Clamp(shape / 5f, 0f, 1f) * (1f - d.Face * (1f - StreakExposure)), temperature, g, ref color, ref transmittance);
    }

    /// <summary>
    /// Relativistic jets along the spin axis: a collimated column (width grows as z^0.55). With JetSpiral it is a solid,
    /// bright core whose surface is streaked by bands of gas winding round it, and the whole pattern streams outward (and
    /// so appears to spin). The gas glows blue-white and absorbs, so the core looks solid and the far side stays hidden.
    /// Doppler beaming for gas moving at JetSpeed makes the approaching jet outshine the receding one. Dimmed by the disk
    /// in front of it and lensed like the rest.
    /// </summary>
    static void AccumulateJets(Vector3 x, Vector3 v, float dt, ref Vector3 color, ref float transmittance)
    {
        if (JetNow <= 0f) return;
        float z = Vector3.Dot(x, N), az = MathF.Abs(z);
        if (JetHeads > 0f)
        {
            // The heads punching out through the gas round the hole: bright knots at the jets' tips.
            float hr = JetWidth * MathF.Pow(JetLengthNow + JetBase * JetSpiral, 0.55f);   // as wide as the jet there
            float hd = az - 0.95f * JetLengthNow;
            float rr = (x - z * N).LengthSquared();
            float head = MathF.Exp(-(hd * hd + rr) / (hr * hr));
            color += transmittance * new Vector3(0.75f, 0.88f, 1f) * (JetHeads * JetNow * 0.25f * head * dt / hr);
        }
        float start = 1.1f * (1f - JetSpiral);                                                       // the spiral tube starts at the hole
        float knotAmount = JetKnots;
        if (az < start || az > JetLengthNow * (knotAmount > 0f ? 1.6f : 1f)) return;
        Vector3 perp = x - z * N;
        float rho = perp.Length();
        float R = JetWidth * MathF.Pow(az + JetBase * JetSpiral, 0.55f);
        if (rho > (knotAmount > 0f ? 7f : 3f) * R) return;
        float s = rho / R;
        float tail = (1f - SmoothStep(JetLengthNow * 0.6f, JetLengthNow, az)) * MathF.Pow(2f / MathF.Max(az, 2f), 0.6f);
        float along = SmoothStep(1.1f, 2.5f, az) * tail;

        // Angle around the axis, and the advected coordinate along it (the pattern streams outward at JetFlow).
        Vector3 t = Vector3.Normalize(Vector3.Cross(N, MathF.Abs(N.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        Vector3 bb = Vector3.Cross(N, t);
        float phi = MathF.Atan2(Vector3.Dot(perp, bb), Vector3.Dot(perp, t));
        float flow = MathF.Pow(az, 0.45f) - JetFlow * Time + (z < 0f ? 7.3f : 0f);
        float twist = JetTwist * flow;

        // Turbulence twisted along with the swirl, so the wisps ride the spiral.
        float tw = phi - 0.5f * twist * JetSpiral;
        var q = new Vector3(s * MathF.Cos(tw) * 1.8f, s * MathF.Sin(tw) * 1.8f, flow * 2.6f);
        float n = Fbm(q);
        float lumps = 0.35f + 1.3f * n * n;

        float plain = (MathF.Exp(-2.5f * s * s) + 0.25f * MathF.Exp(-0.6f * s * s)) * lumps;       // spine + sheath
        // Spiral core: a solid column with a soft edge, its surface streaked by bands of brighter gas winding round it.
        // The swirl changes how brightly the gas glows, not how much it absorbs, so it still shows where the column is thick.
        float u = Math.Clamp((az - start) / (0.85f * R), 0f, 1f);
        float cap = MathF.Max(MathF.Sqrt(u * (2f - u)), 1e-3f);                                      // rounded end
        // Sharp strands (jetStrandSharpness 1 to 6) turn the solid column into separate threads with dark gaps between,
        // the column's edge softening into wisps.
        float threads = Math.Clamp((JetStrandSharpness - 1f) / 5f, 0f, 1f);
        float column = MathF.Exp(-MathF.Pow(s / (0.85f * cap), 10f + (3f - 10f) * threads));
        // Strands, thinner the sharper; as threads they wander, and split into filaments at several radii, like hair.
        float swirl = MathF.Pow(0.5f + 0.5f * MathF.Cos(JetStrands * phi - twist + (1.5f + 1.5f * threads) * n), JetStrandSharpness);
        swirl *= 1f + (MathF.Pow(0.5f + 0.5f * MathF.Cos(6f * s + 3f * n), 0.5f * JetStrandSharpness) - 1f) * threads;
        float streaks = 0.7f + (0.6f * swirl - 0.3f) * SmoothStep(0.15f, 0.6f, s);                   // on the surface, not the axis
        streaks += (0.08f + 1.8f * swirl * SmoothStep(0.1f, 0.5f, s) - streaks) * threads;   // not on the axis, where the threads all meet
        // The glow fades out halfway along; the gas carries on as a darker, smoky plume.
        float glowFade = 1f - SmoothStep(JetLengthNow * 0.3f, JetLengthNow * 0.75f, az);
        float spiralGlow = column * streaks * (0.9f + 0.2f * n) * glowFade;
        float spiralGas = 4f * column * (1f - glowFade * 0.4f) * (0.5f + n);
        float density = along * plain + (tail * spiralGlow - along * plain) * JetSpiral;
        float gas = along * plain + (tail * spiralGas - along * plain) * JetSpiral;
        if (knotAmount > 0f)
        {
            // Past the break the beam comes apart into a chain of blobs (plasmoids) streaming out with the flow, each a
            // turbulent puff with a brighter bow-shaped front, swelling and fading as it goes. They are spaced in z^0.45, so
            // further out they are further apart, as the flow speeds up.
            float len = JetLengthNow;
            float breakAt = JetKnotStart * len;
            float knotWeight = knotAmount * SmoothStep(breakAt * 0.85f, breakAt * 1.15f + 1f, az);
            float side = z < 0f ? 0.37f : 0f;
            float spacing = MathF.Max(JetKnotSpacing, 1e-3f) * MathF.Pow(len, 0.45f);
            float moved = JetFlow * Time;
            float k0 = MathF.Floor((MathF.Pow(az, 0.45f) - moved) / spacing - side);
            float blob = 0f;
            for (int j = -1; j < 3; j++)   // the blobs round it (each at most 1.2 spacings across), so they overlap rather than cut off
            {
                float kq = k0 + j;
                float zc = MathF.Pow(MathF.Max((kq + side) * spacing + moved, 1e-3f), 1f / 0.45f);
                float seed = kq * 12.9898f + side * 78.233f;
                float hb = Frac(MathF.Sin(seed) * 43758.5453f), hx = Frac(MathF.Sin(seed + 1.7f) * 43758.5453f), hy = Frac(MathF.Sin(seed + 3.1f) * 43758.5453f);
                float Rb = JetWidth * MathF.Pow(zc + JetBase * JetSpiral, 0.55f) * (0.7f + 1.0f * JetKnotGrowth * zc / len) * (0.75f + 0.5f * hb);
                Rb = MathF.Min(Rb, 1.2f * spacing * MathF.Pow(zc, 0.55f) / 0.45f);
                float wx = (hx - 0.5f) * 0.7f * Rb, wy = (hy - 0.5f) * 0.7f * Rb;
                var local = new Vector3(Vector3.Dot(perp, t) - wx, Vector3.Dot(perp, bb) - wy, az - zc) / Rb;
                var seedOffset = new Vector3(kq * 3.7f, side * 9.1f, Time * 0.05f);
                float turb = Fbm(local * 1.6f + seedOffset) - 0.5f;
                float wisps = 1f - MathF.Abs(2f * Noise3(local * 4f + seedOffset * 1.3f) - 1f);   // filaments
                // Squashed at the front, drawn out into a wispy trail behind.
                float sd = new Vector3(local.X, local.Y, local.Z * (local.Z > 0f ? 1.7f : 0.75f)).Length() + 0.9f * turb;
                float shellD = (sd - 0.65f) / 0.15f, coreD = sd / 0.4f;
                float front = 0.3f + 0.7f * SmoothStep(-0.5f, 0.7f, local.Z);   // the bow-shaped leading cap
                blob += (MathF.Exp(-shellD * shellD) * front * (0.5f + wisps * wisps) + 0.25f * MathF.Exp(-coreD * coreD)) * (0.6f + 0.8f * hb);
            }
            float fade = MathF.Pow(2f / MathF.Max(az, 2f), 0.6f) * (1f - SmoothStep(len * 0.9f, len * 1.35f, az));
            blob *= 3f * fade;
            density += (blob - density) * knotWeight;
            gas += (blob * 0.5f - gas) * knotWeight;   // glowing plasma: they hide little behind them
        }

        // Relativistic beaming: the gas moves away from the hole along the axis at JetSpeed.
        float cosT = Vector3.Dot(N * MathF.Sign(z), -Vector3.Normalize(v));
        float gamma = 1f / MathF.Sqrt(1f - JetSpeed * JetSpeed);
        float boost = gamma * (1f - JetSpeed * cosT);
        float beam = MathF.Min(1f / (boost * boost * boost * gamma * gamma * gamma), 10f);   // D^3 / gamma^3 (D = 1 / boost), capped
                                                                                            // so a jet seen end-on doesn't drown the view

        Vector3 jetColor = Vector3.Lerp(JetColor, Vector3.One, Math.Clamp(0.4f * density * beam / 8f, 0f, 0.35f));
        color += transmittance * jetColor * (JetNow * 2.5f * density * beam * dt);
        transmittance *= MathF.Exp(-JetAbsorb * gas * dt / R);
    }

    const float ParticleCell = 0.5f; // horizon radii per particle cell

    /// <summary>A point in the disk's rotating frame, where the particles and debris ride with the spin.</summary>
    static Vector3 ToDiskFrame(in DiskP d, Vector3 x)
    {
        float height = Vector3.Dot(x, d.N);
        Vector3 q = x - height * d.N;
        float r = q.Length();
        float angle = MathF.Atan2(Vector3.Dot(q, d.B), Vector3.Dot(q, d.T)) - d.Angle;
        return new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), height);
    }

    /// <summary>
    /// Sparks of white-hot matter and dark chunks of debris riding in the disk. One candidate per cell of a grid in the
    /// rotating frame; each is counted by the one ray segment its closest approach falls in, and its size never drops
    /// below the pixel footprint (energy-conserving), so distant particles fade instead of flickering.
    /// </summary>
    static void AccumulateParticles(in DiskP d, Vector3 x0, Vector3 x1, float traveled, float presence, float temperature, float g,
        ref Vector3 color, ref float transmittance)
    {
        if (presence <= 0f || d.Particles + d.Debris <= 0f) return;
        Vector3 a = ToDiskFrame(d, x0), ab = ToDiskFrame(d, x1) - a;
        float ab2 = Vector3.Dot(ab, ab);
        if (ab2 < 1e-8f) return;
        Vector3 m = (a + ab * 0.5f) / d.Cell - new Vector3(0.5f);
        Vector3 baseCell = new Vector3(MathF.Floor(m.X), MathF.Floor(m.Y), MathF.Floor(m.Z));
        float footprint = traveled * PixelAngle;
        float size = d.Cell / ParticleCell;
        Vector3 spark = Saturate(Blackbody(temperature * g * 1.4f)) * (g * g * g);
        for (int k = 0; k < 8; k++)
        {
            Vector3 cell = baseCell + new Vector3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
            float kind = Hash3(cell + new Vector3(71.3f));
            if (kind > 0.32f) continue; // most cells are empty
            Vector3 jitter = new Vector3(Hash3(cell + new Vector3(1.7f)), Hash3(cell + new Vector3(9.2f)), Hash3(cell + new Vector3(23.5f)));
            Vector3 p = (cell + new Vector3(0.2f) + jitter * 0.6f) * d.Cell;
            float s = Vector3.Dot(p - a, ab) / ab2;
            if (s < 0f || s >= 1f) continue; // counted by the segment its closest approach falls in
            Vector3 dd = a + ab * s - p;
            bool debris = kind < 0.08f;
            float radius = (debris ? 0.07f + 0.08f * jitter.X : 0.012f + 0.018f * jitter.Y) * size;
            float sigma = MathF.Max(radius, footprint);
            float fall = MathF.Exp(-Vector3.Dot(dd, dd) / (sigma * sigma)) * (radius * radius) / (sigma * sigma) * presence;
            if (debris)
            {
                // Dark rock in front of the glow, with a faint rim lit by the disk.
                float cover = Math.Clamp(d.Debris * fall * 0.95f, 0f, 1f);
                color += transmittance * spark * (0.04f * cover);
                transmittance *= 1f - cover;
            }
            else
            {
                color += transmittance * spark * (d.Particles * d.Bright * (3f + 8f * jitter.Z * jitter.Z * jitter.Z) * fall); // a few much brighter
            }
        }
    }

    /// <summary>Per-channel exponential roll-off: the hottest regions saturate to white, the rest keep their colour.</summary>
    static Vector3 Tonemap(Vector3 c) => new Vector3(1 - MathF.Exp(-c.X), 1 - MathF.Exp(-c.Y), 1 - MathF.Exp(-c.Z));

    /// <summary>Chroma boost around luminance (a real 25,000 K blackbody is only pale blue).</summary>
    static Vector3 Saturate(Vector3 c) => Saturate(c, Saturation);

    static Vector3 Saturate(Vector3 c, float saturation)
    {
        float l = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        return Vector3.Max(new Vector3(l) + (c - new Vector3(l)) * saturation, Vector3.Zero);
    }

    static float DiskTemperature(float r, float inner, float peak)
    {
        if (r <= inner) return 0;
        float rPeak = inner * (49f / 36f);
        return peak * MathF.Pow(r / rPeak, -0.75f) * MathF.Pow(Math.Clamp((1 - MathF.Sqrt(inner / r)) * 7f, 0f, 1f), 0.25f);
    }

    static Vector3 Blackbody(float kelvin)
    {
        float t = Math.Clamp(kelvin, 1667f, 25000f), t2 = t * t, t3 = t2 * t;
        float x = t < 4000f ? -0.2661239e9f / t3 - 0.2343589e6f / t2 + 0.8776956e3f / t + 0.179910f
                            : -3.0258469e9f / t3 + 2.1070379e6f / t2 + 0.2226347e3f / t + 0.240390f;
        float x2 = x * x, x3 = x2 * x;
        float y = t < 2222f ? -1.1063814f * x3 - 1.34811020f * x2 + 2.18555832f * x - 0.20219683f
                : t < 4000f ? -0.9549476f * x3 - 1.37418593f * x2 + 2.09137015f * x - 0.16748867f
                            : 3.0817580f * x3 - 5.87338670f * x2 + 3.75112997f * x - 0.37001483f;
        float X = x / y, Y = 1f, Z = (1 - x - y) / y;
        var rgb = new Vector3(3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z,
                             -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z,
                              0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z);
        rgb = Vector3.Max(rgb, Vector3.Zero);
        rgb /= MathF.Max(MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z)), 1e-5f);
        return rgb * Math.Clamp(kelvin / 1667f, 0f, 1f);
    }

    static float Hash3(Vector3 p)
    {
        p = Frac(p * 0.3183099f + new Vector3(0.1f));
        p *= 17f;
        return Frac(p.X * p.Y * p.Z * (p.X + p.Y + p.Z));
    }

    // BlackHole.shader's Noise3 reads its lattice from a 3D texture, 64 to a side and repeating: the same here.
    static float Lattice(Vector3 c) => Hash3(new Vector3(Wrap64(c.X), Wrap64(c.Y), Wrap64(c.Z)));
    static float Wrap64(float v) => v - 64f * MathF.Floor(v / 64f);

    static float Noise3(Vector3 x)
    {
        Vector3 i = new Vector3(MathF.Floor(x.X), MathF.Floor(x.Y), MathF.Floor(x.Z));
        Vector3 f = x - i;
        f = f * f * (new Vector3(3f) - 2f * f);
        float n000 = Lattice(i), n100 = Lattice(i + Vector3.UnitX), n010 = Lattice(i + Vector3.UnitY), n110 = Lattice(i + new Vector3(1, 1, 0));
        float n001 = Lattice(i + Vector3.UnitZ), n101 = Lattice(i + new Vector3(1, 0, 1)), n011 = Lattice(i + new Vector3(0, 1, 1)), n111 = Lattice(i + Vector3.One);
        return Lerp(Lerp(Lerp(n000, n100, f.X), Lerp(n010, n110, f.X), f.Y), Lerp(Lerp(n001, n101, f.X), Lerp(n011, n111, f.X), f.Y), f.Z);
    }

    static float Fbm(Vector3 p)
    {
        float sum = 0, a = 0.5f;
        for (int k = 0; k < 3; k++) { sum += a * Noise3(p); p = p * 2.03f + new Vector3(1.7f); a *= 0.5f; }
        return sum / 0.875f;
    }

    static float Frac(float v) => v - MathF.Floor(v);
    static Vector3 Frac(Vector3 v) => new Vector3(Frac(v.X), Frac(v.Y), Frac(v.Z));
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float SmoothStep(float e0, float e1, float x) { float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f); return t * t * (3 - 2 * t); }

    // Minimal PNG writer (RGB8, zlib via ZLibStream).
    internal static void WritePngPublic(string path, int w, int h, byte[] rgb) => WritePng(path, w, h, rgb);

    static void WritePng(string path, int w, int h, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgb, y * w * 3, w * 3); }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, td, 4, data.Length);
        s.Write(td);
        var crc = new byte[4]; WriteBE(crc, 0, (int)Crc32(td)); s.Write(crc);
    }

    static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte d in data)
        {
            crc ^= d;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
