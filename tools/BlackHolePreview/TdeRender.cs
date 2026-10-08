// CPU port of the tidal disruption part of BlackHole.shader (TdeSample, and the new disk through AccumulateDisk),
// line for line.
using System;
using System.Numerics;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Tde;

internal static partial class Program
{
    // Uniforms, as BlackHoleRenderer sets them (disk frame mapped to the preview's: x, z, -y so the normal is +Y).
    static bool TdeOn;
    static int TdeCount, TdeChunks;
    static float TdeCore;                                   // radius of the disk & jets region
    static Vector3 TdeNormal; static float TdeSpin = 1f;
    static Vector4 TdeStar, TdeStarAxis, TdeStarVel, TdeStarLook; // centre + presence, long axis + long, velocity + short, temperature + tail
    static readonly Vector4[] TdeVerts = new Vector4[TdeFrame.MaxVertices];
    static readonly Vector4[] TdeVertLook = new Vector4[TdeFrame.MaxVertices];
    static readonly Vector4[] TdeVertVel = new Vector4[TdeFrame.MaxVertices];
    static readonly Vector4[] TdeChunk = new Vector4[TdeFrame.MaxChunks];
    static Vector4 TdeShock, TdeShockLook, TdeEnv, TdeLight, TdeGlows, TdeFlash;
    static readonly float[] TdeEnvK = new float[TdeFrame.EnvelopeBins];
    static readonly float[] TdeEnvT = new float[TdeFrame.EnvelopeBins];

    const float TdeSourceScale = 2f;      // an opaque surface at the disk's peak temperature looks about as bright as the disk there
    const float VisibleBand = 26170f;     // hc / (lambda k) at 550 nm, kelvin
    const float TdeSaturation = 1.4f;     // gentler than the disk's: stars look white-ish, not candy-coloured
    const float StreamMaxDepth = 1.6f;
    const float EnvelopeMaxDepth = 0.35f; // most optical depth of the outflow's haze any one view passes through: a glow, never a wall    // optical depth drawn through the stream's middle: soft and gaseous, its structure showing

    static Vector3 ToPreview(Double3 d) => new Vector3((float)d.X, (float)d.Z, (float)-d.Y);

    /// <summary>Loads a TdeFrame into the uniforms, as BlackHoleRenderer does.</summary>
    static void LoadTde(TdeFrame f, TdeParameters p)
    {
        TdeOn = f.Active;
        if (!TdeOn) return;
        TdeCount = f.VertexCount;
        TdeChunks = f.ChunkCount;
        TdeCore = BaseBound;
        TdeNormal = ToPreview(f.OrbitNormal);
        TdeStar = f.StarVisible ? new Vector4(ToPreview(f.StarCenter), (float)f.StarPresence) : Vector4.Zero;
        TdeStarAxis = new Vector4(ToPreview(f.StarAxis), (float)f.StarLong);
        TdeStarVel = new Vector4(ToPreview(f.StarVelocity), (float)f.StarShort);
        TdeStarLook = new Vector4((float)f.StarTemperature, (float)f.StarTail, 0f, 0f);
        for (int i = 0; i < f.VertexCount; i++)
        {
            TdeVerts[i] = new Vector4(ToPreview(f.Position[i]), (float)f.WidthA[i]);
            TdeVertLook[i] = new Vector4((float)f.Material[i], (float)f.Opacity[i], (float)f.Temperature[i], (float)f.WidthZ[i]);
            // A negative brightness marks a break: no segment joins this vertex to the one before.
            TdeVertVel[i] = new Vector4(ToPreview(f.Velocity[i]), f.Break[i] ? -(float)f.Brightness[i] - 0.01f : (float)f.Brightness[i]);
        }
        for (int c = 0; c < f.ChunkCount; c++) TdeChunk[c] = new Vector4(ToPreview(f.ChunkCenter[c]), (float)f.ChunkRadius[c]);
        TdeShock = f.ShockVisible ? new Vector4(ToPreview(f.ShockPosition), MathF.Max((float)f.ShockLuminosity, 1e-6f)) : Vector4.Zero;
        TdeShockLook = new Vector4(ToPreview(f.ShockDirection), (float)f.ShockRadius);
        TdeEnv = new Vector4((float)f.EnvelopeInner, (float)f.EnvelopeLogSpan, (float)f.EnvelopeFunnel, f.EnvelopeVisible ? 1f : 0f);
        for (int b = 0; b < TdeFrame.EnvelopeBins; b++)
        {
            TdeEnvK[b] = (float)(f.EnvelopeOpacity[b] * p.EnvelopeOpacity);
            TdeEnvT[b] = (float)f.EnvelopeTemperature[b];
        }
        TdeLight = new Vector4((float)f.Luminosity, (float)p.StreamGlow, (float)f.ClumpGrowth, (float)p.LineVisibility);
        TdeGlows = new Vector4((float)p.StarGlow, 0f, 0f, 0f);
        TdeFlash = f.FlashVisible ? new Vector4(ToPreview(f.FlashPosition), (float)f.FlashStrength) : Vector4.Zero;
        TdeGlows.Y = (float)f.FlashRadius;
    }

    /// <summary>Visible-band (550 nm) brightness of a blackbody, unnormalised.</summary>
    static float PlanckV(float kelvin) => 1f / (MathF.Exp(MathF.Min(VisibleBand / MathF.Max(kelvin, 500f), 80f)) - 1f);

    static Vector3 Thermal(float kelvin)
    {
        Vector3 c = Blackbody(kelvin);
        float l = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        c = Vector3.Max(new Vector3(l) + (c - new Vector3(l)) * TdeSaturation, Vector3.Zero);
        return c * (TdeSourceScale * PerceivedRatio(PlanckV(kelvin) / PlanckV(PeakK)));
    }

    /// <summary>
    /// Visible-band brightness relative to the disk at its peak temperature, physical up to that (a 6,000 K star is
    /// ~1.5% of it) and compressed logarithmically above it, as the eye does: a 10^6 K disk is ~25x brighter, which
    /// would otherwise burn everything near it to flat white.
    /// </summary>
    static float PerceivedRatio(float ratio) => ratio <= 1f ? ratio : 1f + 0.6f * MathF.Log(ratio);

    /// <summary>g = nu_obs / nu_emit for gas at r moving with velocity beta (fraction of c), photon heading along -vn.</summary>
    static float Shift(Vector3 x, Vector3 beta, Vector3 vn)
    {
        float b2 = MathF.Min(Vector3.Dot(beta, beta), 0.99f);
        float gamma = 1f / MathF.Sqrt(1f - b2);
        float doppler = 1f / (gamma * (1f - Vector3.Dot(beta, -vn)));
        return doppler * MathF.Sqrt(Math.Clamp(1f - 1f / x.Length(), 0f, 1f));
    }

    /// <summary>
    /// The event at x (all but the new disk, which AccumulateDisk draws): total opacity k (per horizon radius),
    /// opacity-weighted source (emission = k * source), pure glow (emission per unit length that does not absorb), and
    /// the largest safe step from here.
    /// </summary>
    static float TdeSample(Vector3 x, Vector3 vn, float traveled, out float k, out Vector3 emit, out Vector3 glow, out float envK, out Vector3 envEmit)
    {
        k = 0f; emit = Vector3.Zero; glow = Vector3.Zero; envK = 0f; envEmit = Vector3.Zero;
        float limit = 1e9f;
        float r = x.Length();
        float fp = traveled * PixelAngle;

        // The star: a tidally stretched, opaque ellipsoid with a glowing halo, gas pouring off its tips as it nears the
        // tidal radius (the inner tail leads, the outer trails), dissolving into the stream once torn apart.
        if (TdeStar.W > 0f)
        {
            Vector3 y = x - new Vector3(TdeStar.X, TdeStar.Y, TdeStar.Z);
            Vector3 axis = new Vector3(TdeStarAxis.X, TdeStarAxis.Y, TdeStarAxis.Z);
            Vector3 vel = new Vector3(TdeStarVel.X, TdeStarVel.Y, TdeStarVel.Z);
            float la = TdeStarAxis.W, sa = TdeStarVel.W, presence = TdeStar.W, tail = TdeStarLook.Y;
            float la2 = MathF.Sqrt(la * la + fp * fp), sa2 = MathF.Sqrt(sa * sa + fp * fp);
            float a = Vector3.Dot(y, axis);
            Vector3 yp = y - a * axis;
            float rho2 = a * a / (la2 * la2) + Vector3.Dot(yp, yp) / (sa2 * sa2);
            float tailLength = tail * 6f * sa;
            float d = y.Length() - (1.2f * la2 + tailLength + 2f * sa2);
            limit = MathF.Min(limit, MathF.Max(d, 0.1f * sa2));
            float temp0 = TdeStarLook.X;
            float glowStar = TdeGlows.X;
            if (rho2 < 1.21f)
            {
                float cover = MathF.Pow(la * sa * sa / (la2 * sa2 * sa2), 2f / 3f);       // projected area kept / smeared
                float tau = MathF.Min(40f, -MathF.Log(1f - 0.98f * cover));
                float ks = tau / (2f * sa2) * 3f * (1f - SmoothStep(0.81f, 1f, rho2)) * presence;
                Vector3 normal = Vector3.Normalize(a / (la2 * la2) * axis + yp / (sa2 * sa2) + new Vector3(1e-9f));
                float limb = 0.4f + 0.6f * MathF.Abs(Vector3.Dot(normal, vn));                  // Eddington limb darkening
                float stretch = la / MathF.Max(sa, 1e-6f) - 1f;
                float temp = temp0 * (1f - 0.1f * stretch * a * a / (la2 * la2));            // gravity darkening at the tips
                float granules = 0.88f + 0.24f * Fbm(y / sa2 * 6f + new Vector3(Time * 0.02f));
                Vector3 src = Thermal(temp * Shift(x, vel, vn)) * (limb * granules * glowStar);
                k += ks; emit += ks * src;
            }
            // Halo: the star's own light scattered in its wind and the gas lifting off it.
            float rho = MathF.Sqrt(rho2);
            if (rho < 4f)
                glow += Thermal(temp0 * Shift(x, vel, vn)) * (glowStar * presence * 0.05f * MathF.Exp(-MathF.Max(rho - 1f, 0f) / 0.15f) / sa2 * (la * sa) / (la2 * sa2));
            // Tidal tails through L1 (towards the hole) and L2 (away), bending ahead and behind along the orbit.
            if (tail > 0.001f)
            {
                float side = a < 0f ? -1f : 1f;
                float u = (MathF.Abs(a) - 0.8f * la2) / MathF.Max(tailLength, 1e-6f);
                if (u > 0f && u < 1f)
                {
                    Vector3 along = Vector3.Normalize(vel - Vector3.Dot(vel, axis) * axis + new Vector3(1e-9f));
                    Vector3 bend = along * (-side * u * u * tailLength * 0.5f);
                    Vector3 off = yp - bend;
                    float r0 = (0.45f - 0.3f * u) * sa;
                    float radius = MathF.Sqrt(r0 * r0 + fp * fp);   // smeared to the pixel, light kept
                    float n = Fbm(new Vector3(u * 6f, Vector3.Dot(off, along) / radius, side * 3.1f) + new Vector3(Time * 0.05f, 0f, 0f));
                    float kt = tail * presence * 2.5f * r0 / (radius * radius) * MathF.Exp(-Vector3.Dot(off, off) / (radius * radius)) * (1f - u) * (1f - u) * (0.4f + 1.2f * n);
                    if (kt > 1e-5f)
                    {
                        k += kt; emit += kt * Thermal(temp0 * 0.85f * Shift(x, vel, vn)) * glowStar;
                        limit = MathF.Min(limit, 0.3f * radius);
                    }
                }
            }
        }

        // The debris stream: a turbulent tube, optically thick in the middle with a soft edge and a faint halo,
        // clumped along its gas, glowing at its own temperature and lit by the flare.
        if (TdeCount > 1)
        {
            float bestK = 0f, nearest = 1e9f;
            Vector4 bLook = default, bVel = default;
            Vector3 bOff = default; float bWa = 1f, bWz = 1f, bDa = 0f, bDz = 0f;
            for (int c = 0; c < TdeChunks; c++)
            {
                Vector4 ch = TdeChunk[c];
                float dc = (x - new Vector3(ch.X, ch.Y, ch.Z)).Length() - ch.W - 4f * fp;
                // Well clear of the chunk: its bounding sphere is a safe step. Near it, test its segments, so a ray
                // grazing the sphere is not left creeping along it.
                if (dc > 0.25f * ch.W) { nearest = MathF.Min(nearest, dc); continue; }
                int last = Math.Min(c * TdeFrame.ChunkSegments + TdeFrame.ChunkSegments, TdeCount - 1);
                for (int i = c * TdeFrame.ChunkSegments; i < last; i++)
                {
                    if (TdeVertVel[i + 1].W < 0f) continue; // the stream breaks here
                    Vector4 va = TdeVerts[i], vb = TdeVerts[i + 1];
                    Vector3 pa = new Vector3(va.X, va.Y, va.Z), ab = new Vector3(vb.X, vb.Y, vb.Z) - pa;
                    float ab2 = Vector3.Dot(ab, ab);
                    if (ab2 < 1e-12f) continue;
                    float u = Math.Clamp(Vector3.Dot(x - pa, ab) / ab2, 0f, 1f);
                    Vector3 off = x - (pa + ab * u);
                    Vector4 look = Vector4.Lerp(TdeVertLook[i], TdeVertLook[i + 1], u);
                    float wa = va.W + (vb.W - va.W) * u, wz = look.W;
                    float wa2 = MathF.Sqrt(wa * wa + fp * fp), wz2 = MathF.Sqrt(wz * wz + fp * fp);
                    float dist = off.Length();
                    float reach = 4f * MathF.Max(wa2, wz2);
                    nearest = MathF.Min(nearest, dist - reach);
                    if (dist > reach) continue;
                    Vector3 tangent = ab / MathF.Sqrt(ab2);
                    Vector3 vert = TdeNormal - Vector3.Dot(TdeNormal, tangent) * tangent;
                    vert = vert.LengthSquared() > 1e-8f ? Vector3.Normalize(vert) : Vector3.Normalize(Vector3.Cross(tangent, MathF.Abs(tangent.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                    Vector3 across = Vector3.Cross(tangent, vert);
                    float da = Vector3.Dot(off, across) / wa2, dz = Vector3.Dot(off, vert) / wz2;
                    float q2 = da * da + dz * dz;
                    if (q2 > 16f) continue;
                    float cover = MathF.Max(MathF.Sqrt(wa * wz / (wa2 * wz2)), TdeLight.W);    // pixel coverage, with the display floor
                    float tau = MathF.Min(MathF.Min(look.Y * 2f * MathF.Min(wa, wz), StreamMaxDepth), -MathF.Log(1f - 0.98f * MathF.Min(cover, 1f)));
                    // A core and a faint halo of gas lifted off it.
                    float kk = tau / (1.7725f * MathF.Min(wa2, wz2)) * (MathF.Exp(-q2) + 0.015f * MathF.Exp(-q2 * 0.25f));
                    if (kk > bestK)
                    {
                        bestK = kk; bLook = look; bVel = Vector4.Lerp(TdeVertVel[i], TdeVertVel[i + 1], u);
                        bVel.W = Lerp(MathF.Abs(TdeVertVel[i].W), TdeVertVel[i + 1].W, u);
                        bOff = off; bWa = wa2; bWz = wz2; bDa = da; bDz = dz;
                    }
                }
            }
            limit = MathF.Min(limit, bestK > 0f ? 0.35f * MathF.Min(bWa, bWz) : MathF.Max(nearest, 0.3f * MathF.Max(fp, 0.02f)));
            if (bestK > 0f)
            {
                // Turbulent filaments, and clumps where it fragments under its own gravity; both ride with the gas.
                Vector3 nc = new Vector3(bLook.X * 70f, bDa * 0.8f, bDz * 0.8f);
                float wisps = 0.35f + 1.3f * Fbm(nc + new Vector3(0f, 0f, Time * 0.03f));
                float clump = Lerp(1f, SmoothStep(0.3f, 0.62f, Noise3(new Vector3(bLook.X * 90f, bDa * 0.6f, bDz * 0.6f))) + 0.05f, TdeLight.Z);
                float ks = bestK * clump * wisps;
                Vector3 vel = new Vector3(bVel.X, bVel.Y, bVel.Z);
                float knots = 0.85f + 0.3f * Noise3(new Vector3(bLook.X * 40f, 1.7f, 2.3f));   // hotter and cooler stretches along it
                Vector3 own = Thermal(bLook.Z * knots * Shift(x, vel, vn));
                // Embers: knots of gas still glowing hot where clumps collapse.
                own *= 1f + 5f * SmoothStep(0.8f, 0.95f, Noise3(new Vector3(bLook.X * 260f, bDa * 2.5f, bDz * 2.5f + Time * 0.2f)));
                // Lit by the central source: the flare (Eddington units) plus the quiescent disk, falling off as 1/r^2.
                float lit = 0.5f + 0.5f * Vector3.Dot(Vector3.Normalize(bOff + new Vector3(1e-9f)), -x / r);
                float incident = (0.4f + TdeLight.X) * MathF.Min(1f, 64f / (r * r));
                Vector3 scattered = Saturate(Blackbody(30000f)) * (TdeSourceScale * 0.25f * incident * lit);
                // The display boost is for cool gas only: hot gas is bright enough as it is.
                float boost = 1f + (TdeLight.Y - 1f) * (1f - SmoothStep(7000f, 16000f, bLook.Z));
                Vector3 src = (own + scattered) * (boost * bVel.W);
                k += ks; emit += ks * src;
            }
        }

        // The self-intersection shock: a white-hot knot where the returning stream hits the incoming one, spraying
        // shocked gas along the returning stream's motion in a widening fan.
        if (TdeShock.W > 0f)
        {
            Vector3 y = x - new Vector3(TdeShock.X, TdeShock.Y, TdeShock.Z);
            Vector3 dir = new Vector3(TdeShockLook.X, TdeShockLook.Y, TdeShockLook.Z);
            float rad = MathF.Sqrt(TdeShockLook.W * TdeShockLook.W + fp * fp);
            float len = 8f * rad;
            float d = y.Length() - (len * 2f + 3f * rad);
            limit = MathF.Min(limit, MathF.Max(d, 0.25f * rad));
            if (d < 0f)
            {
                float along = Vector3.Dot(y, dir);
                float perp = (y - along * dir).Length();
                float core = MathF.Exp(-Vector3.Dot(y, y) / (rad * rad));
                float spread = rad * (0.7f + 2.5f * MathF.Max(along, 0f) / len);
                float fan = along > -rad ? MathF.Exp(-Sq(MathF.Max(along, 0f) / len) - Sq(perp / spread)) * (0.4f + 1.2f * Fbm(y / rad * 0.7f + new Vector3(Time * 0.1f))) : 0f;
                glow += Thermal(TdeShock.W > 0.3f ? 60000f : 40000f) * (0.12f * MathF.Min(TdeShock.W, 3f) * (core + 0.5f * fan) / rad);
            }
        }

        // The periapsis flash: a brief white-hot burst riding with the debris.
        if (TdeFlash.W > 0f)
        {
            float rad = MathF.Sqrt(TdeGlows.Y * TdeGlows.Y + fp * fp);
            float dy = (x - new Vector3(TdeFlash.X, TdeFlash.Y, TdeFlash.Z)).Length();
            limit = MathF.Min(limit, MathF.Max(dy - 4f * rad, 0.25f * rad));
            glow += Thermal(80000f) * (0.3f * TdeFlash.W * MathF.Exp(-dy * dy / (rad * rad)) / rad);
        }

        // The outflow's envelope: radial profile, thinner in the polar funnels, streaked by the outflow.
        if (TdeEnv.W > 0f)
        {
            float lr = MathF.Log(r / TdeEnv.X) / TdeEnv.Y * (TdeFrame.EnvelopeBins - 1);
            if (lr >= 0f && lr <= TdeFrame.EnvelopeBins - 1)
            {
                limit = MathF.Min(limit, 0.06f * r);
                int i = Math.Min((int)lr, TdeFrame.EnvelopeBins - 2);
                float f = lr - i;
                float c = Vector3.Dot(x, TdeNormal) / r;
                float streaks = 0.35f + 1.3f * Fbm(x / r * 3f + new Vector3(0f, lr * 0.06f - Time * 0.01f, 0f));
                float ke = Lerp(TdeEnvK[i], TdeEnvK[i + 1], f) * (TdeEnv.Z + (1f - TdeEnv.Z) * (1f - c * c)) * streaks;
                float te = Lerp(TdeEnvT[i], TdeEnvT[i + 1], f);
                envK = ke; envEmit = ke * Thermal(te);
            }
        }
        return limit;
    }

    static float Sq(float v) => v * v;

    static bool SkyOn;

    static Vector4 SkyOnly(Vector3 v)
    {
        if (!SkyOn) return Vector4.Zero;
        Vector3 c = Tonemap(StarSky(Vector3.Normalize(v)));
        return new Vector4(LinearToSrgb(c.X), LinearToSrgb(c.Y), LinearToSrgb(c.Z), 0f);
    }

    /// <summary>A starfield with a faint Milky Way band, as a stand-in for KSP's skybox (linear radiance).</summary>
    static Vector3 StarSky(Vector3 dir)
    {
        Vector3 sky = Vector3.Zero;
        // Stars: one candidate per cell of two direction grids, a few bright ones and many faint.
        foreach ((float scale, float threshold, float gain) in new[] { (90f, 0.975f, 2.5f), (260f, 0.93f, 0.6f) })
        {
            Vector3 p = dir * scale;
            Vector3 cell = new Vector3(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z));
            float h = Hash3(cell + new Vector3(17.1f));
            if (h < threshold) continue;
            Vector3 jitter = new Vector3(Hash3(cell + new Vector3(3.3f)), Hash3(cell + new Vector3(7.7f)), Hash3(cell + new Vector3(11.9f)));
            Vector3 d = p - cell - (new Vector3(0.25f) + jitter * 0.5f);
            float px = MathF.Max(PixelAngle * scale, 0.04f);
            float bright = MathF.Pow(Hash3(cell + new Vector3(5.5f)), 6f) * gain * 0.04f / (px * px);
            float t = Hash3(cell + new Vector3(9.9f));
            Vector3 tint = t < 0.15f ? new Vector3(1f, 0.75f, 0.55f) : t < 0.6f ? new Vector3(1f, 0.95f, 0.9f) : new Vector3(0.75f, 0.85f, 1f);
            sky += tint * (bright * MathF.Exp(-Vector3.Dot(d, d) / (px * px)));
        }
        // The Milky Way: a soft band of unresolved stars and dust lanes.
        Vector3 pole = Vector3.Normalize(new Vector3(0.35f, 0.82f, 0.45f));
        float lat = Vector3.Dot(dir, pole);
        float band = MathF.Exp(-lat * lat / 0.025f) * (0.35f + 0.9f * Fbm(dir * 7f)) * (1f - 0.7f * SmoothStep(0.5f, 0.8f, Fbm(dir * 15f + new Vector3(2.1f))));
        sky += new Vector3(0.55f, 0.55f, 0.65f) * (0.035f * band);
        return sky;
    }

    /// <summary>
    /// The game's Cinematic look, roughly (KSPVE_POST preset: bloom 0.35, vignette 0.35, grain 0.08, lens streaks),
    /// on the finished image: bloom at two widths plus a horizontal anamorphic streak from the brightest highlights,
    /// a gentle teal/orange split-tone, vignette and film grain.
    /// </summary>
    static void Cinematic(float[] img, int w, int h)
    {
        int n = w * h;
        var bright = new float[n * 3];
        var hot = new float[n * 3];
        for (int i = 0; i < n; i++)
            for (int ch = 0; ch < 3; ch++)
            {
                float v = SrgbToLinear(img[i * 3 + ch]);
                const float knee = 0.4f, t = 0.7f;
                float soft = Math.Clamp(v - t + knee, 0f, 2f * knee);
                bright[i * 3 + ch] = MathF.Max(v - t, soft * soft / (4f * knee));
                hot[i * 3 + ch] = MathF.Max(v - 0.9f, 0f);
            }
        float[] a = Blur(bright, w, h, h * 0.012f), b = Blur(bright, w, h, h * 0.06f);
        float[] streak = BlurX(hot, w, h, w * 0.08f);
        uint seed = 1234567u;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float u = (x + 0.5f) / w - 0.5f, vv = (y + 0.5f) / h - 0.5f;
                float vignette = 1f - 0.35f * SmoothStep(0.25f, 0.85f, MathF.Sqrt(u * u * 1.2f + vv * vv * 2.2f));
                seed = seed * 1664525u + 1013904223u;
                float grain = ((seed >> 8) / 16777216f - 0.5f) * 0.035f;
                Vector3 c = new Vector3(SrgbToLinear(img[i * 3]), SrgbToLinear(img[i * 3 + 1]), SrgbToLinear(img[i * 3 + 2]));
                c += 0.35f * (0.55f * new Vector3(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]) + 0.45f * new Vector3(b[i * 3], b[i * 3 + 1], b[i * 3 + 2])) * 2f;
                c += new Vector3(0.55f, 0.75f, 1f) * (0.9f * (streak[i * 3] + streak[i * 3 + 1] + streak[i * 3 + 2]) / 3f);
                float lum = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
                c += new Vector3(-0.006f, 0.004f, 0.012f) * (1f - SmoothStep(0f, 0.25f, lum));   // teal shadows
                c *= new Vector3(1.03f, 1f, 0.97f) * SmoothStep(0.3f, 1f, lum) + Vector3.One * (1f - SmoothStep(0.3f, 1f, lum)); // warm highlights
                c = c * vignette + new Vector3(grain);
                img[i * 3] = LinearToSrgb(Math.Clamp(c.X, 0f, 1f));
                img[i * 3 + 1] = LinearToSrgb(Math.Clamp(c.Y, 0f, 1f));
                img[i * 3 + 2] = LinearToSrgb(Math.Clamp(c.Z, 0f, 1f));
            }
    }

    static float[] BlurX(float[] src, int w, int h, float sigma)
    {
        int rad = Math.Max(1, (int)(sigma * 3f));
        var kernel = new float[rad * 2 + 1];
        float sum = 0f;
        for (int i = -rad; i <= rad; i++) { kernel[i + rad] = MathF.Exp(-i * i / (2f * sigma * sigma)); sum += kernel[i + rad]; }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        var dst = new float[src.Length];
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
                for (int ch = 0; ch < 3; ch++)
                {
                    float acc = 0f;
                    for (int k = -rad; k <= rad; k++) acc += kernel[k + rad] * src[(y * w + Math.Clamp(x + k, 0, w - 1)) * 3 + ch];
                    dst[(y * w + x) * 3 + ch] = acc;
                }
        });
        return dst;
    }

    /// <summary>
    /// The game's bloom, roughly (KSPVE_POST defaults: threshold 0.8, intensity 0.25, wide scatter), on the finished
    /// image: brightness over the threshold, blurred at two widths, added back.
    /// </summary>
    static void Bloom(float[] img, int w, int h)
    {
        int n = w * h;
        var bright = new float[n * 3];
        for (int i = 0; i < n; i++)
            for (int ch = 0; ch < 3; ch++)
            {
                float v = SrgbToLinear(img[i * 3 + ch]);
                float knee = 0.4f, t = 0.8f;
                float soft = Math.Clamp(v - t + knee, 0f, 2f * knee);
                bright[i * 3 + ch] = MathF.Max(v - t, soft * soft / (4f * knee));
            }
        float[] a = Blur(bright, w, h, h * 0.012f), b = Blur(bright, w, h, h * 0.05f);
        for (int i = 0; i < n * 3; i++)
            img[i] = LinearToSrgb(MathF.Min(SrgbToLinear(img[i]) + 0.25f * (0.6f * a[i] + 0.4f * b[i]) * 2f, 1f));
    }

    static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    static float[] Blur(float[] src, int w, int h, float sigma)
    {
        int rad = Math.Max(1, (int)(sigma * 3f));
        var kernel = new float[rad * 2 + 1];
        float sum = 0f;
        for (int i = -rad; i <= rad; i++) { kernel[i + rad] = MathF.Exp(-i * i / (2f * sigma * sigma)); sum += kernel[i + rad]; }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        var tmp = new float[src.Length];
        var dst = new float[src.Length];
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
                for (int ch = 0; ch < 3; ch++)
                {
                    float acc = 0f;
                    for (int k = -rad; k <= rad; k++) acc += kernel[k + rad] * src[(y * w + Math.Clamp(x + k, 0, w - 1)) * 3 + ch];
                    tmp[(y * w + x) * 3 + ch] = acc;
                }
        });
        System.Threading.Tasks.Parallel.For(0, w, x =>
        {
            for (int y = 0; y < h; y++)
                for (int ch = 0; ch < 3; ch++)
                {
                    float acc = 0f;
                    for (int k = -rad; k <= rad; k++) acc += kernel[k + rad] * tmp[(Math.Clamp(y + k, 0, h - 1) * w + x) * 3 + ch];
                    dst[(y * w + x) * 3 + ch] = acc;
                }
        });
        return dst;
    }
}

internal static partial class Program
{
    static TidalDisruption _tde;
    static TdeParameters _tdeParams;

    /// <summary>
    /// tde out.png w h days camX camY camZ [tgtX tgtY tgtZ]: the event "days" after the disruption (negative: before),
    /// seen from a camera at (camX, camY, camZ) horizon radii in the disk frame (z along the disk normal).
    /// TDE_INC, TDE_NODE, TDE_ARGP (degrees) orient the orbit; TDE_HORIZON sets the horizon radius in metres;
    /// TDE_BLOOM=0 leaves out the game's bloom.
    /// </summary>
    static int RenderTde(string[] args)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string path = args[1];
        int w = int.Parse(args[2]), h = int.Parse(args[3]);
        double days = double.Parse(args[4], ci);
        var cam = new Double3(double.Parse(args[5], ci), double.Parse(args[6], ci), double.Parse(args[7], ci));
        var tgt = args.Length > 10 ? new Double3(double.Parse(args[8], ci), double.Parse(args[9], ci), double.Parse(args[10], ci)) : Double3.Zero;
        if (_tde == null)
        {
            _tdeParams = new TdeParameters
            {
                InclinationDeg = EnvFloat("TDE_INC", 0f),
                NodeDeg = EnvFloat("TDE_NODE", 20f),
                PeriapsisAngleDeg = EnvFloat("TDE_ARGP", 60f),
                EnvelopeOpacity = EnvFloat("TDE_ENV", 0.00001f),
                LineVisibility = EnvFloat("TDE_LINE", 0.35f),
                StarGlow = EnvFloat("TDE_STARGLOW", 1f),
                StreamGlow = EnvFloat("TDE_GLOW", 40f),
                StarMassSuns = EnvFloat("TDE_MASS", 40f),
                StarRadiusSuns = EnvFloat("TDE_RADIUS", 15f),
                StarTemperature = EnvFloat("TDE_TEMP", 40000f),
                Periapsis = EnvFloat("TDE_PERI", 5f)
            };
            _tde = new TidalDisruption(_tdeParams, EnvFloat("TDE_HORIZON", 9.6e9f), 42);
        }
        var frame = new TdeFrame();
        _tde.Evaluate(_tde.ApproachSeconds + days * 86400.0, frame);
        if (Environment.GetEnvironmentVariable("TDE_DUMP") == "1")
            for (int i = 0; i < frame.VertexCount; i += 8)
                Console.WriteLine($"  v{i}: r {frame.Position[i].Length:0.0} s {frame.Material[i]:0.000} wa {frame.WidthA[i]:0.0000} wz {frame.WidthZ[i]:0.0000} T {frame.Temperature[i]:0} k {frame.Opacity[i]:0.0} b {frame.Brightness[i]:0.00}");
        Bound = MathF.Max(BaseBound, (float)frame.Extent);
        MaxSteps = frame.Active ? 500 : 300;
        LoadTde(frame, _tdeParams);
        // The hole has no disk of its own (diskFromTde) unless BH_OWNDISK=1: the event builds it.
        DiskState disk = Environment.GetEnvironmentVariable("BH_OWNDISK") == "1"
            ? DiskState.Configured(Inner, Outer, Thickness, JetStrength, JetLength)
            : DiskState.Forming(frame, Inner, Outer, Thickness, JetStrength, JetLength);
        DiskPresence = (float)disk.Presence; DiskBright = (float)disk.Brightness;
        InnerNow = (float)disk.ShapeInner; OuterNow = (float)disk.ShapeOuter; ThickNow = (float)disk.Thickness;
        MaskIn = (float)disk.MaskInner; MaskOut = (float)disk.MaskOuter; MaskSoft = (float)disk.MaskSoftness;
        JetNow = (float)disk.Jets; JetLengthNow = (float)disk.JetLength;
        DiskSpiral = (float)disk.Spiral; JetHeads = (float)disk.JetHeads; LaunchFlash = (float)disk.LaunchFlash;
        Console.WriteLine($"disk: formed {frame.DiskFormation:0.00}, presence {disk.Presence:0.00}, {disk.Inner:0.0}-{disk.Outer:0.0} r_s, H/R {disk.Thickness:0.00}, x{disk.Brightness:0.00}; jets {disk.Jets:0.00} to {disk.JetLength:0} r_s, jet age {frame.JetAge * 32.0 / 86400.0:0.0000} d; flash {frame.FlashStrength:0.00}");

        // Views (TDE_VIEW): "free" (default) takes the camera and target from the arguments, in the disk frame.
        //   "top": straight down the star's orbit normal from camX horizon radii, onto the hole (or TDE_FOLLOW).
        //   "chase": from camX beyond the followed thing (star, stream, shock) on the line from the hole, camY above
        //            the orbit plane, looking back past it at the hole.
        //   "front": ahead of the star on its path, looking back at it.
        //   "orbit": camX away from the hole, camY degrees above the orbit plane, camZ degrees around it.
        string view = Environment.GetEnvironmentVariable("TDE_VIEW") ?? "free";
        string follow = Environment.GetEnvironmentVariable("TDE_FOLLOW") ?? string.Empty;
        Double3 anchor = Double3.Zero;
        if (follow == "star" && frame.StarVisible) anchor = frame.StarCenter;
        else if (follow == "shock" && frame.ShockVisible) anchor = frame.ShockPosition;
        else if (follow == "stream" && frame.VertexCount > 0) anchor = frame.Position[frame.VertexCount / 2];
        else if (follow == "head" && frame.VertexCount > 0) anchor = frame.Position[0];
        Double3 normal = frame.OrbitNormal.Length > 0.5 ? frame.OrbitNormal : new Double3(0, 0, 1);
        Double3 upHint = Math.Abs(normal.Z) < 0.9 ? new Double3(0, 0, 1) : new Double3(1, 0, 0);
        if (view == "top")
        {
            cam = anchor + normal * cam.X;
            tgt = anchor;
            upHint = Math.Abs(normal.Z) < 0.9 ? new Double3(0, 0, 1) : new Double3(0, 1, 0);
        }
        else if (view == "chase")
        {
            Double3 outward = anchor.Length > 0 ? anchor / anchor.Length : new Double3(1, 0, 0);
            cam = anchor + outward * cam.X + normal * cam.Y;
            tgt = anchor * EnvFloat("TDE_AIM", 0.6f);
            upHint = normal;
        }
        else if (view == "front" && frame.StarVisible)
        {
            Double3 ahead = frame.StarVelocity.Normalized();
            cam = anchor + ahead * cam.X + normal * cam.Y;
            tgt = anchor * EnvFloat("TDE_AIM", 1f);
            upHint = normal;
        }
        else if (view == "orbit")
        {
            Double3 e1 = Double3.Cross(normal, upHint).Normalized(), e2 = Double3.Cross(normal, e1);
            double el = cam.Y * Math.PI / 180.0, az = cam.Z * Math.PI / 180.0;
            Double3 dir = (e1 * Math.Cos(az) + e2 * Math.Sin(az)) * Math.Cos(el) + normal * Math.Sin(el);
            cam = anchor + dir * cam.X;
            tgt = anchor;
            upHint = normal;
        }
        else if (anchor != Double3.Zero) { cam = anchor + cam; tgt = anchor + tgt; }
        Vector3 camPos = ToPreview(cam), target = ToPreview(tgt);
        Vector3 forward = Vector3.Normalize(target - camPos);
        Vector3 hint = ToPreview(upHint);
        Vector3 right = Vector3.Normalize(Vector3.Cross(hint, forward));
        if (float.IsNaN(right.X)) right = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, forward));
        Vector3 up = Vector3.Cross(forward, right);
        float tanY = MathF.Tan(EnvFloat("BH_FOV", 60f) * 0.5f * MathF.PI / 180f), tanX = tanY * w / h;
        PixelAngle = 2f * tanY / h;
        if (Environment.GetEnvironmentVariable("TDE_RAY") == "1")
        {
            RayLog = new System.Text.StringBuilder();
            Jitter = 0.5f;
            Trace(camPos, forward);
            string log = RayLog.ToString();
            RayLog = null;
            string[] lines = log.Split((char)10);
            Console.WriteLine($"centre ray: {LastSteps} steps, exit {LastExit}");
            for (int i = 0; i < lines.Length; i += Math.Max(1, lines.Length / 40)) Console.WriteLine("  " + lines[i]);
        }
        SkyOn = Environment.GetEnvironmentVariable("TDE_SKY") != "0";
        var img = new float[w * h * 3];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        System.Threading.Tasks.Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w, v = 1f - (py + 0.5f) / h;
                Vector3 dir = Vector3.Normalize(forward + right * (u * 2 - 1) * tanX + up * (v * 2 - 1) * tanY);
                Jitter = 0.2f + 0.8f * Frac(MathF.Sin(px * 12.9898f + py * 78.233f) * 43758.5453f);
                Vector4 c = Trace(camPos, dir);
                int i = (py * w + px) * 3;
                img[i] = c.X; img[i + 1] = c.Y; img[i + 2] = c.Z;
            }
        });
        string look = Environment.GetEnvironmentVariable("TDE_LOOK") ?? "cinematic";
        if (look == "cinematic") Cinematic(img, w, h);
        else if (look != "raw") Bloom(img, w, h);
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < rgb.Length; i++) rgb[i] = ToByte(img[i]);
        WritePng(path, w, h, rgb);
        Console.WriteLine($"wrote {path}: {frame.Phase} {days:0.###} d, verts {frame.VertexCount}, extent {frame.Extent:0}, L {frame.Luminosity:0.00} L_Edd, {watch.ElapsedMilliseconds} ms");
        return 0;
    }
}
