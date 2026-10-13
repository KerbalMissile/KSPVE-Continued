using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using KSPVE.Pure.Stars;
using D3 = KSPVE.Pure.Numerics.Double3;

/// <summary>
/// starfx view out.png [width height] [size] [phase] [seed] [days]: a star as Star.shader draws it (a line-for-line CPU copy
/// of that file, fed by the real StarActivity model), view eye | halpha | euv. size is the star's radius as a share of the
/// frame's height (default 0.30; 0.18 leaves room for big loops); phase the activity cycle's phase (default 0.36, maximum);
/// days of game time added. Settings: SF_VIS (structures' visibility, 1), SF_DETAIL (granulation contrast, 1), SF_TILT (deg
/// the viewer sits above the star's equator, 7), SF_TEMP (K, 5772), SF_DISC (disc radiance, 2), SF_EXPOSURE (stops, 0),
/// SF_VEIL (eye view: the eclipse veil 0..1, 1), SF_CME (a test CME launched this many hours earlier, off the limb), SF_LON
/// (deg, the longitude facing the viewer, 0).
/// </summary>
internal static class StarFx
{
    static float Env(string n, float d) => float.TryParse(Environment.GetEnvironmentVariable(n), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : d;

    // ---- uniforms (the shader's) ----
    static float Teff = 5772f, Disc = 2f, Vis = 1f, Detail = 1f, Veil = 1f, Activity = 1f, Spread = 1f, TexTurn = 0f, TimeS = 0f, PixelAngle = 0.002f, MeanLat = 0.3f, Seed = 0f;
    static int View;
    static bool Eclipse = Environment.GetEnvironmentVariable("SF_ECLIPSE") == "1";
    static bool DoProminences = true, DoCmes = true, DoCorona = true;
    static StarScene Scene;

    static float Frac(float v) => v - MathF.Floor(v);
    static Vector3 Frac(Vector3 v) => new Vector3(Frac(v.X), Frac(v.Y), Frac(v.Z));
    static float Saturate(float v) => Math.Clamp(v, 0f, 1f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    static float Smooth(float e0, float e1, float x) { float t = Saturate((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
    static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    // ---- noise ----
    static float Hash13(Vector3 p3)
    {
        p3 = Frac(p3 * 0.1031f);
        p3 += new Vector3(Vector3.Dot(p3, new Vector3(p3.Z, p3.Y, p3.X) + new Vector3(31.32f)));
        return Frac((p3.X + p3.Y) * p3.Z);
    }
    static Vector3 Hash33(Vector3 p3)
    {
        p3 = Frac(p3 * new Vector3(0.1031f, 0.1030f, 0.0973f));
        p3 += new Vector3(Vector3.Dot(p3, new Vector3(p3.Y, p3.X, p3.Z) + new Vector3(33.33f)));
        return Frac(new Vector3((p3.X + p3.Y) * p3.Z, (p3.X + p3.X) * p3.Y, (p3.Y + p3.X) * p3.X));
    }
    static float Noise3(Vector3 x)
    {
        Vector3 i = new Vector3(MathF.Floor(x.X), MathF.Floor(x.Y), MathF.Floor(x.Z));
        Vector3 f = x - i;
        f = f * f * (new Vector3(3f) - 2f * f);
        float n000 = Hash13(i), n100 = Hash13(i + Vector3.UnitX), n010 = Hash13(i + Vector3.UnitY), n110 = Hash13(i + new Vector3(1, 1, 0));
        float n001 = Hash13(i + Vector3.UnitZ), n101 = Hash13(i + new Vector3(1, 0, 1)), n011 = Hash13(i + new Vector3(0, 1, 1)), n111 = Hash13(i + Vector3.One);
        return Lerp(Lerp(Lerp(n000, n100, f.X), Lerp(n010, n110, f.X), f.Y), Lerp(Lerp(n001, n101, f.X), Lerp(n011, n111, f.X), f.Y), f.Z);
    }
    static void Cells(Vector3 p, float boil, out float d1, out float lane)
    {
        Vector3 ip = new Vector3(MathF.Floor(p.X), MathF.Floor(p.Y), MathF.Floor(p.Z)), fp = p - ip;
        float a = 8f, b = 8f;
        for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
        {
            var g = new Vector3(x, y, z);
            Vector3 h = Hash33(ip + g);
            var o = new Vector3(0.5f + 0.5f * MathF.Sin(6.2831853f * (h.X + boil)), 0.5f + 0.5f * MathF.Sin(6.2831853f * (h.Y + boil)), 0.5f + 0.5f * MathF.Sin(6.2831853f * (h.Z + boil)));
            Vector3 r = g + o - fp;
            float d = Vector3.Dot(r, r);
            if (d < a) { b = a; a = d; } else if (d < b) b = d;
        }
        d1 = MathF.Sqrt(a); lane = MathF.Sqrt(b) - d1;
    }

    // ---- colour ----
    static Vector3 Blackbody(float kelvin)
    {
        float t = Math.Clamp(kelvin, 1667f, 25000f), t2 = t * t, t3 = t2 * t;
        float x = t < 4000f ? -0.2661239e9f / t3 - 0.2343589e6f / t2 + 0.8776956e3f / t + 0.179910f : -3.0258469e9f / t3 + 2.1070379e6f / t2 + 0.2226347e3f / t + 0.240390f;
        float x2 = x * x, x3 = x2 * x;
        float y = t < 2222f ? -1.1063814f * x3 - 1.34811020f * x2 + 2.18555832f * x - 0.20219683f : t < 4000f ? -0.9549476f * x3 - 1.37418593f * x2 + 2.09137015f * x - 0.16748867f : 3.0817580f * x3 - 5.87338670f * x2 + 3.75112997f * x - 0.37001483f;
        float X = x / y, Y = 1f, Z = (1 - x - y) / y;
        var rgb = new Vector3(3.2404542f * X - 1.5371385f * Y - 0.4985314f * Z, -0.9692660f * X + 1.8760108f * Y + 0.0415560f * Z, 0.0556434f * X - 0.2040259f * Y + 1.0572252f * Z);
        rgb = Vector3.Max(rgb, Vector3.Zero);
        rgb /= MathF.Max(MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z)), 1e-5f);
        return rgb * Saturate(kelvin / 1667f);
    }
    static Vector3 StarLight(float k) { var c = Blackbody(k); float l = Lum(c); c = Vector3.Max(new Vector3(l) + (c - new Vector3(l)) * 1.6f, Vector3.Zero); return c / MathF.Max(Lum(c), 1e-4f); }
    static readonly Vector3 Lambda = new Vector3(610f, 550f, 465f);
    const float C2 = 1.438777e7f;
    static float GreyT(float teff, float tau) => teff * MathF.Pow(0.75f * (tau + 0.6666667f), 0.25f);
    static Vector3 PlanckRatio(float t, float tc)
    {
        Vector3 inv = new Vector3(C2 / Lambda.X, C2 / Lambda.Y, C2 / Lambda.Z);
        return new Vector3((MathF.Exp(inv.X / tc) - 1f) / (MathF.Exp(inv.X / t) - 1f), (MathF.Exp(inv.Y / tc) - 1f) / (MathF.Exp(inv.Y / t) - 1f), (MathF.Exp(inv.Z / tc) - 1f) / (MathF.Exp(inv.Z / t) - 1f));
    }
    static Vector3 RotateY(Vector3 v, float a) { float c = MathF.Cos(a), s = MathF.Sin(a); return new Vector3(c * v.X + s * v.Z, v.Y, -s * v.X + c * v.Z); }
    static float DiskF(float d, float r, float soft) => 1f - Smooth(r - soft, r + soft, d);
    static Vector3 V(D3 d) => new Vector3((float)d.X, (float)d.Y, (float)d.Z);

    static bool Sphere(Vector3 ro, Vector3 rd, Vector3 c, float r, out float t0, out float t1)
    {
        Vector3 oc = ro - c; float b = Vector3.Dot(oc, rd), cc = Vector3.Dot(oc, oc) - r * r, disc = b * b - cc;
        t0 = 0; t1 = 0;
        if (disc <= 0f) return false;
        float s = MathF.Sqrt(disc); t0 = -b - s; t1 = -b + s; return t1 > 0f;
    }

    // ---- surface ----
    static Vector4 SpotField(Vector3 n, float soft)
    {
        float umbra = 0, pen = 0, fac = 0, fl = 0;
        foreach (SpotGroup g in Scene.Spots)
        {
            Vector3 Apos = V(g.Dir), Baxis = V(g.Axis);
            float Aw = (float)g.HalfSeparation, Bw = (float)g.UmbraRadius, Cx = (float)g.FaculaRadius, strength = (float)g.Strength, Cz = (float)g.Flare, seed = (float)(g.Seed % 997.0), age = (float)g.Age;
            float reach = Cx * 1.5f + Aw * 2f + 0.02f;
            Vector3 dc = n - Apos;
            if (Vector3.Dot(dc, dc) > reach * reach) continue;
            float ru = Bw * (0.4f + 0.6f * strength);
            Vector3 lead = Vector3.Normalize(Apos + Baxis * Aw), fol = Vector3.Normalize(Apos - Baxis * Aw);
            float edge = 1f + 0.5f * (Noise3((n - Apos) * (5f / MathF.Max(ru, 1e-3f)) + new Vector3(seed * 13f)) - 0.5f);
            float dl = (n - lead).Length() / edge, df = (n - fol).Length() / edge;
            float rl = ru, rf = ru * 0.55f * (1f - 0.75f * Smooth(0.45f, 0.95f, age));
            float s = MathF.Max(soft, 0.12f * ru);
            Vector3 t2 = Vector3.Cross(Apos, Baxis);
            float ang = MathF.Atan2(Vector3.Dot(n - lead, t2), Vector3.Dot(n - lead, Baxis));
            float fib = 0.72f + 0.18f * Noise3(new Vector3(ang * 5f, dl / MathF.Max(rl, 1e-3f) * 2f, seed)) + 0.10f * Noise3(new Vector3(ang * 23f, dl / MathF.Max(rl, 1e-3f) * 5f, seed + 3f));
            float u = MathF.Max(DiskF(dl, rl, s), DiskF(df, rf, s));
            float p = MathF.Max(DiskF(dl, rl * 2.4f, s * 1.5f), DiskF(df, rf * 2.4f, s * 1.5f)) * fib;
            float pores = 3f + 4f * Smooth(0.3f, 0.9f, age);
            for (int j = 0; j < 7; j++)
            {
                if (j >= pores) break;
                Vector3 h = Hash33(new Vector3(seed * 7f, j, 3.7f));
                Vector3 pc = Vector3.Normalize(Apos + Baxis * ((h.X - 0.5f) * 3f * Aw) + t2 * ((h.Y - 0.5f) * 2.4f * Aw));
                float rp = ru * (0.2f + 0.25f * h.Z);
                u = MathF.Max(u, DiskF((n - pc).Length(), rp, s) * 0.9f);
                p = MathF.Max(p, DiskF((n - pc).Length(), rp * 2f, s));
            }
            umbra = MathF.Max(umbra, u * strength);
            pen = MathF.Max(pen, p * strength);
            float dg = (n - Apos).Length();
            float rough = 0.6f + 0.8f * Noise3(n * 70f + new Vector3(seed * 5f));
            fac = MathF.Max(fac, strength * MathF.Exp(-dg * dg / (Cx * Cx)) * rough);
            fl = MathF.Max(fl, Cz * MathF.Exp(-Vector3.Dot(n - Apos, n - Apos) / (0.5f * Aw * Aw + 1e-5f)));
        }
        return new Vector4(umbra, pen, fac, fl);
    }

    static Vector3 Granulation(Vector3 n, float footprint, float mu)
    {
        Vector3 q = RotateY(n, -TexTurn);
        Cells(q * 23f, TimeS * 1.2e-5f, out float d1, out float lane);
        float network = 1f - Smooth(0f, 0.22f, lane);
        float cellular = Smooth(0f, 0.35f, lane) - 0.6f;
        float superFade = Saturate(1f - footprint / (0.043f * 0.8f));
        Cells(q * 100f, TimeS * 2.0e-4f, out d1, out lane);
        float meso = Smooth(0f, 0.3f, lane) - 0.65f;
        float mesoFade = Saturate(1f - footprint / (0.01f * 0.8f));
        Cells(q * 700f, TimeS * 1.4e-3f, out d1, out lane);
        float gran = 0.9f * Smooth(0f, 0.32f, lane) + 0.1f * (1f - d1) - 0.62f;
        float granFade = Saturate(1f - footprint / 0.0014f);
        float c = Lerp(0.12f, 0.30f, Saturate(mu));
        float g = 1f + Detail * (0.42f * c * gran * granFade + 0.08f * meso * mesoFade - 0.03f * network * superFade);
        return new Vector3(g, network * superFade, cellular * superFade);
    }

    static Vector3 Surface(Vector3 n, Vector3 rd, float footprint)
    {
        float mu = Saturate(Vector3.Dot(n, -rd));
        float soft = MathF.Max(footprint * 0.7f, 0.0005f);
        Vector4 spot = SpotField(n, soft);
        Vector3 gran = Granulation(n, footprint, mu);
        float t = GreyT(Teff, mu), tc = GreyT(Teff, 1f);
        float scale = 1f;
        scale = Lerp(scale, 0.93f, spot.Y);
        scale = Lerp(scale, 0.69f, spot.X);
        scale += spot.Z * 0.035f * (0.35f + 1.65f * (1f - mu)) * (1f - spot.Y);
        scale += spot.W * 0.25f;
        Vector3 colour = StarLight(Teff) * PlanckRatio(t * scale, tc) * gran.X;
        if (View == 0) return colour * Disc;
        if (View == 1)
        {
            float net = gran.Y * (0.18f + 0.5f * Saturate(spot.Z)) + 0.35f * spot.Z + 0.6f * spot.W;
            colour *= Vector3.One + net * new Vector3(0.55f, 0.28f, 0.12f);
            colour *= 1f + 0.30f * Detail * gran.Z;
            colour = Lerp(colour, colour * new Vector3(1f, 0.8f, 0.62f), 0.35f * (1f - mu));
            return colour * Disc;
        }
        float hole = Smooth(0.78f, 0.96f, MathF.Abs(n.Y)) * 0.8f;
        float euv = 0.10f + 0.55f * gran.Y + 1.8f * spot.Z + 0.7f * spot.X + 0.9f * spot.Y + 2.5f * spot.W;
        euv *= (1f - hole) * (1f + 0.9f * (1f - mu));
        euv *= 0.8f + 0.4f * (gran.X - 1f) * 4f + 0.2f;
        return new Vector3(1f, 0.62f, 0.17f) * (euv * Disc);
    }

    // ---- prominences ----
    static Vector4 GasSource() => View == 0 ? new Vector4(1.5e-4f * Vis, 1f, 0.45f, 0.5f) : View == 1 ? new Vector4(1.0f * Vis, 1f, 0.33f, 0.10f) : new Vector4(0.55f * Vis, 1f, 0.68f, 0.22f);
    static float AbsorptionScale() => View == 0 ? 0f : View == 1 ? 1f : 0.35f;

    static float ArchDensity(Vector3 p, Vector3 m, Vector3 u, Vector3 v, Vector3 w, float hc, float hv, float width, float ribbon, float nest, float seed, float erupt, out float core)
    {
        core = 0f;
        Vector3 q = p - m;
        float pu = Vector3.Dot(q, u), pv = Vector3.Dot(q, v), pw = Vector3.Dot(q, w);
        if (pv < -0.03f) return 0f;
        float eu = pu / hc, ev = MathF.Max(pv, 0f) / hv;
        float e = MathF.Sqrt(eu * eu + ev * ev + 1e-9f);
        float theta = MathF.Atan2(ev, eu);
        float s = theta / 3.1415927f;
        float g = MathF.Sqrt(pu * pu / (hc * hc * hc * hc) + MathF.Max(pv, 0f) * MathF.Max(pv, 0f) / (hv * hv * hv * hv)) / e;
        float gap = 0.085f, e2 = e;
        if (nest > 1.5f)
        {
            float k = Math.Clamp(MathF.Floor((1f - e) / gap + 0.5f), 0f, nest - 1f);
            e2 = e + k * gap;
        }
        float d = (e2 - 1f) / MathF.Max(g, 1e-3f);
        float w0 = width * (0.65f + 0.55f * MathF.Sin(theta)) * (1f + 0.5f * erupt);
        float tube = MathF.Exp(-d * d / (w0 * w0));
        float across = pw / MathF.Max(ribbon, 1e-3f);
        float ribbonShape = MathF.Exp(-across * across * 1.4f);
        float wobble = 5f * Noise3(new Vector3(s * 5f, pw / MathF.Max(ribbon, 1e-3f) * 3f, seed * 3.1f));
        float ph = d / (w0 * 0.15f) * 3.1415927f + wobble + s * 6f * (Hash13(new Vector3(seed, 5f, 1f)) - 0.5f);
        float strandId = MathF.Floor(ph / 3.1415927f);
        float strandBright = 0.35f + 1.0f * Hash13(new Vector3(strandId, seed, 1.7f));
        float strands = 0.08f + 0.92f * MathF.Pow(Saturate(0.5f + 0.5f * MathF.Sin(ph)), 2.5f) * strandBright * (0.5f + 1.0f * Noise3(new Vector3(pw / MathF.Max(width * 0.6f, 1e-3f), s * 3f, seed)));
        float knots = 0.55f + 0.9f * Noise3(new Vector3(s * 7f, seed, pw / MathF.Max(ribbon, 1e-3f) * 2f));
        float feet = 0.75f + 0.5f * MathF.Pow(MathF.Abs(MathF.Cos(theta)), 3f);
        core = erupt * MathF.Exp(-(s - 0.5f) * (s - 0.5f) / 0.015f) * MathF.Exp(-d * d / (w0 * w0 * 4f)) * ribbonShape;
        return tube * ribbonShape * strands * knots * feet;
    }

    static float FilamentDensity(Vector3 p, Vector3 c, Vector3 e, float H, float width, float halfLen, float seed)
    {
        float r = p.Length(), h = r - 1f;
        if (h < -0.012f) return 0f;
        Vector3 ph = p / r;
        float s = MathF.Atan2(Vector3.Dot(ph, e), Vector3.Dot(ph, c));
        if (MathF.Abs(s) > halfLen) return 0f;
        Vector3 f = Vector3.Cross(c, e);
        float wOff = Vector3.Dot(ph, f) * r;
        float along = MathF.Abs(s) / halfLen;
        float env = MathF.Pow(Saturate(1f - along * along), 0.6f);
        float meander = (Noise3(new Vector3(s * 5f, seed, 1.7f)) - 0.5f) * 0.035f;
        float column = Noise3(new Vector3(s * 55f, seed * 3.1f, 5f));
        float hc = H * (0.5f + 1.2f * column);
        float vertical = (1f - Smooth(0.45f, 1f, MathF.Max(h, 0f) / hc)) * Smooth(-0.012f, 0.004f, h);
        float sheet = MathF.Exp(-(wOff - meander) * (wOff - meander) / (width * width));
        float threads = 0.5f + 0.5f * Noise3(new Vector3(s * 240f, MathF.Max(h, 0f) * 55f, seed));
        return env * vertical * sheet * threads;
    }

    static Vector3 Integrate(StarStructure st, Vector3 ro, Vector3 rd, float tLimit, float jitter)
    {
        Vector3 A = V(st.A), B = V(st.B);
        bool arch = st.Kind == StructureKind.Arch;
        float height = (float)st.Height, width = (float)st.Width, ribbon = (float)st.Ribbon, nest = (float)st.Nest, brightness = (float)st.Brightness, opacity = (float)st.Opacity, erupt = (float)st.Erupt, seed = (float)(st.Seed % 997.0);
        Vector3 centre, fa = Vector3.Zero, fu = Vector3.Zero, fv = Vector3.Zero, fw = Vector3.Zero;
        float radius, hc = 1f, hv = 1f;
        if (arch)
        {
            Vector3 m = 0.5f * (A + B);
            hc = MathF.Max(0.5f * (B - A).Length(), 1e-4f);
            Vector3 u = (B - A) / (2f * hc);
            Vector3 up = m / MathF.Max(m.Length(), 1e-4f);
            Vector3 v = Vector3.Normalize(up - u * Vector3.Dot(up, u));
            hv = MathF.Max(1f + height - m.Length(), 1e-3f);
            fa = m; fu = u; fv = v; fw = Vector3.Cross(u, v);
            centre = m + v * (0.5f * hv);
            radius = MathF.Sqrt(hc * hc + 0.25f * hv * hv + ribbon * ribbon) + 3f * width + 0.01f;
        }
        else
        {
            centre = A * (1f + 2f * height);
            radius = 2f * MathF.Sin(MathF.Min(ribbon, 1.5f) * 0.5f) + 4.5f * height + 3f * width + 0.02f;
        }
        if (!Sphere(ro, rd, centre, radius, out float t0, out float t1)) return Vector3.Zero;
        t0 = MathF.Max(t0, 0f); t1 = MathF.Min(t1, tLimit);
        if (t1 <= t0) return Vector3.Zero;
        const int steps = 40;
        float dt = (t1 - t0) / steps, kappa = (arch ? 34f : 48f) * opacity;
        float T = 1f, em = 0f, coreEm = 0f, tau = 0f;
        for (int i = 0; i < steps; i++)
        {
            Vector3 p = ro + rd * (t0 + (i + jitter) * dt);
            float core = 0f;
            float dens = arch ? ArchDensity(p, fa, fu, fv, fw, hc, hv, width, ribbon, nest, seed, erupt, out core) : FilamentDensity(p, A, B, height, width, ribbon, seed);
            float dtau = kappa * dens * dt;
            em += T * brightness * (1f - MathF.Exp(-dtau));
            coreEm += T * (1f - MathF.Exp(-kappa * 2f * core * dt));
            T *= MathF.Exp(-dtau);
            tau += dtau;
        }
        return new Vector3(em, coreEm, tau);
    }

    // ---- corona ----
    static float KCorona(float rho)
    {
        float r = MathF.Max(rho, 1f);
        return 1e-6f * (0.0532f * MathF.Pow(r, -2.5f) + 1.425f * MathF.Pow(r, -7f) + 2.565f * MathF.Pow(r, -17f)) + 7e-9f * MathF.Pow(r, -2.2f);
    }
    static float CoronaShape(Vector3 dir, float r)
    {
        float lat = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)), lon = MathF.Atan2(dir.Z, dir.X);
        float halfWidth = Lerp(0.3f, 1.6f, Spread);
        float belt = MathF.Exp(-lat * lat / (halfWidth * halfWidth));
        float baseV = Lerp(0.25f, 1f, belt);
        float rays = 0.55f + 0.9f * Noise3(new Vector3(lon * 3.5f + Seed, lat * 5f, 2f));
        float fine = 0.7f + 0.6f * Noise3(new Vector3(lon * 14f, lat * 18f, 7f + Seed));
        float pole = Smooth(0.9f, 1.25f, MathF.Abs(lat));
        float plumes = pole * MathF.Pow(Noise3(new Vector3(lon * 9f, 4f, Seed)), 3f) * 2.5f;
        float helmet = belt * MathF.Exp(-(r - 1f) / 0.5f) * (0.4f + 0.8f * Noise3(new Vector3(lon * 6f, 9f, 3f)));
        return baseV * rays * fine + plumes + 0.6f * helmet;
    }
    static float CoronaAlong(Vector3 ro, Vector3 rd, float tClosest, float rho)
    {
        float sum = 0, weight = 0;
        for (int i = 0; i < 5; i++)
        {
            float t = tClosest + (i - 2f) * 0.8f * rho;
            Vector3 p = ro + rd * t;
            float r = p.Length(), w = MathF.Pow(MathF.Max(r, 1f), -3.5f);
            sum += w * CoronaShape(p / r, r); weight += w;
        }
        return sum / MathF.Max(weight, 1e-9f);
    }

    static Vector3 ShellAlong(CmeShell sh, Vector3 ro, Vector3 rd, float tLimit, float jitter)
    {
        Vector3 axis = V(sh.Dir);
        float apex = (float)sh.Apex, hw = MathF.Min((float)sh.HalfWidth, 1.5f), seed = (float)(sh.Seed % 997.0), coreAmt = (float)sh.Core;
        float s = MathF.Sin(hw), a = apex * s / (1f + s), cc = apex - a;
        Vector3 sc = axis * cc;
        if (!Sphere(ro, rd, sc, a * 1.35f, out float t0, out float t1)) return Vector3.Zero;
        t0 = MathF.Max(t0, 0f); t1 = MathF.Min(t1, tLimit);
        if (t1 <= t0) return Vector3.Zero;
        const int steps = 30;
        float dt = (t1 - t0) / steps, front = 0, cavity = 0, core = 0;
        Vector3 coreC = axis * (cc - 0.22f * a);
        for (int i = 0; i < steps; i++)
        {
            Vector3 p = ro + rd * (t0 + (i + jitter) * dt);
            float r = p.Length(), cosAng = Vector3.Dot(p / r, axis);
            float cone = Smooth(MathF.Cos(hw), MathF.Cos(hw * 0.75f), cosAng);
            float dd = (p - sc).Length() / a;
            float shell = MathF.Exp(-(dd - 1f) * (dd - 1f) / 0.018f);
            shell *= 0.6f + 0.8f * Noise3(p * (4f / a) + new Vector3(seed));
            float inside = 1f - Smooth(0.35f, 0.82f, dd);
            float dr = (p - coreC).Length() / a;
            float blob = MathF.Exp(-dr * dr / 0.035f) * (0.7f + 0.6f * Noise3(p * (9f / a) + new Vector3(seed * 3f)));
            float falloff = MathF.Pow(MathF.Max(r, 1f), -2.5f);
            front += cone * shell * falloff * dt;
            cavity += cone * inside * dt / MathF.Max(a, 0.5f);
            core += cone * blob * falloff * dt * coreAmt;
        }
        return new Vector3(front, cavity, core);
    }

    // ---- one pixel (model frame = preview frame) ----
    static Vector4 Pixel(int px, int py, Vector3 camPos, Vector3 dir)
    {
        Vector3 ro = camPos, rd = dir;
        if (Eclipse) { float bb = Vector3.Dot(ro, rd); if (MathF.Sqrt(MathF.Max(Vector3.Dot(ro, ro) - bb * bb, 0f)) < 1.002f && bb < 0f) return Vector4.Zero; }
        float disc = Disc;
        float veil = View == 0 ? Veil : 1f;
        bool hitStar = Sphere(ro, rd, Vector3.Zero, 1f, out float t0s, out float t1s) && t0s > 0f;
        float tSurf = hitStar ? t0s : 1e30f;
        float tLimit = tSurf;
        float jitter = Frac(Hash13(new Vector3(px, py, 7f)));
        Vector3 emission = Vector3.Zero, result;
        float alpha = 0f, absorb = 1f;
        float footprint = PixelAngle * MathF.Max(tSurf == 1e30f ? 1f : tSurf, 1f);
        Vector4 gas = GasSource();
        Vector3 gasColour = new Vector3(gas.Y, gas.Z, gas.W);
        Vector3 coreColour = Lerp(gasColour, new Vector3(1f, 0.8f, 0.45f), 0.35f);
        if (DoProminences)
        {
            float absorbScale = AbsorptionScale();
            foreach (StarStructure st in Scene.Structures)
            {
                Vector3 r = Integrate(st, ro, rd, tLimit, jitter);
                emission += (r.X * gasColour + r.Y * coreColour * 1.4f) * gas.X * disc * veil;
                absorb *= MathF.Exp(-r.Z * absorbScale);
            }
        }
        if (hitStar)
        {
            Vector3 n = Vector3.Normalize(ro + rd * tSurf);
            result = Surface(n, rd, footprint) * absorb + emission;
            alpha = 1f;
        }
        else result = emission;

        float b = Vector3.Dot(ro, rd), rho = MathF.Sqrt(MathF.Max(Vector3.Dot(ro, ro) - b * b, 0f)), tClosest = -b;
        if (!hitStar && tClosest > 0f)
        {
            Vector3 pc = ro + rd * tClosest;
            float h = rho - 1f;
            float azimuth = MathF.Atan2(pc.Z, pc.X) + pc.Y * 3f;
            float spicule = MathF.Pow(Noise3(new Vector3(azimuth * 160f, h * 40f, TimeS * 0.004f)), 4f);
            float chromo = MathF.Exp(-MathF.Max(h, 0f) / 0.0035f) * (0.65f + 0.35f * Noise3(new Vector3(azimuth * 40f, 3f, 1f))) + spicule * 1.4f * MathF.Exp(-MathF.Max(h, 0f) / 0.007f);
            float chromoGain = View == 0 ? 1.2e-3f : View == 1 ? 0.9f : 0.04f;
            Vector3 chromoColour = View == 2 ? new Vector3(1f, 0.7f, 0.25f) : new Vector3(1f, 0.32f, 0.30f);
            Vector3 rim = chromoColour * (chromo * chromoGain * disc * (View == 1 ? Vis : 1f) * veil);
            float corona = 0f; Vector3 coronaColour = Vector3.One;
            if (DoCorona)
            {
                float shape = CoronaAlong(ro, rd, tClosest, rho);
                if (View == 0) { corona = KCorona(rho) * shape * disc * Vis; coronaColour = new Vector3(1f, 0.97f, 0.93f); }
                else if (View == 1) { corona = (0.050f * MathF.Exp(-(rho - 1f) / 0.30f) + 0.010f / (rho * rho)) * (0.4f + 0.6f * Activity) * shape * disc * Vis; coronaColour = new Vector3(1f, 0.42f, 0.15f); }
                else { corona = (0.20f * MathF.Exp(-(rho - 1f) / 0.20f) + 0.025f / (rho * rho)) * (0.5f + 0.5f * Activity) * shape * disc * Vis; coronaColour = new Vector3(1f, 0.68f, 0.22f); }
                corona *= veil;
            }
            float cavity = 0f; Vector3 cme = Vector3.Zero;
            if (DoCmes)
            {
                float cmeGain = View == 0 ? 2.3e-7f : View == 1 ? 0.12f : 0.15f;
                foreach (CmeShell sh in Scene.Shells)
                {
                    Vector3 s = ShellAlong(sh, ro, rd, 1e30f, jitter);
                    cavity += s.Y;
                    cme += (s.X * Lerp(new Vector3(1f, 0.97f, 0.93f), gasColour, View == 0 ? 0f : 0.5f) + s.Z * coreColour * 1.6f) * cmeGain * disc * Vis * veil;
                }
            }
            result += rim + coronaColour * corona * MathF.Exp(-1.6f * cavity) + cme;
        }
        return new Vector4(result, alpha);
    }

    public static int Run(string[] args)
    {
        string viewName = args[1].ToLowerInvariant(), path = args[2];
        View = viewName == "eye" ? 0 : viewName == "halpha" ? 1 : 2;
        int w = args.Length > 3 ? int.Parse(args[3]) : 1000, h = args.Length > 4 ? int.Parse(args[4]) : 800;
        float size = args.Length > 5 ? float.Parse(args[5], CultureInfo.InvariantCulture) : 0.30f;
        double phase = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : 0.36;
        ulong seed = args.Length > 7 ? ulong.Parse(args[7]) : 1UL;
        double days = args.Length > 8 ? double.Parse(args[8], CultureInfo.InvariantCulture) : 0.0;
        Vis = Env("SF_VIS", 1f); Detail = Env("SF_DETAIL", 1f); Teff = Env("SF_TEMP", 5772f); Disc = Env("SF_DISC", 2f); Veil = Env("SF_VEIL", 1f);
        float tilt = Env("SF_TILT", 7f) * MathF.PI / 180f, exposure = MathF.Pow(2f, Env("SF_EXPOSURE", 0f));
        float faceLon = Env("SF_LON", 0f) * MathF.PI / 180f;
        var profile = new StarProfile { Body = "Sun", Seed = seed, CyclePhase = phase, Temperature = Teff };
        var act = new StarActivity(profile);
        double ut = days * 86400.0 + 5.0 * profile.CyclePeriod - (profile.CyclePhase - phase) * profile.CyclePeriod;
        float cmeHours = Env("SF_CME", -1f);
        if (cmeHours >= 0f)
        {
            double launch = ut - cmeHours * 3600.0;
            act.Inject(new Cme { Id = 99, LaunchTime = launch, Latitude = 0.3, Longitude = 0.0 + MathF.PI * 0.5f - faceLon + 0.0, LaunchSpeed = 1.2e6, HalfWidth = 0.6, Drag = 2e-11, WindSpeed = 4e5 });
        }
        // Viewer: above the equator by tilt, at longitude faceLon.
        var toViewer = new D3(Math.Cos(tilt) * Math.Cos(faceLon), Math.Sin(tilt), Math.Cos(tilt) * Math.Sin(faceLon));
        Scene = act.Snapshot(ut, toViewer, 24, 20, 3);
        if (Environment.GetEnvironmentVariable("SF_DEMO") == "1")
        {
            // Loops standing on the limb as seen from +x, to tune how a loop bundle looks face-on.
            Scene.Structures.Clear();
            double[] angles = { 20, 75, 130, 200, 250, 310 }; double[] sizes = { 0.5, 0.35, 0.7, 0.4, 0.55, 0.3 };
            for (int q = 0; q < angles.Length; q++)
            {
                double th = angles[q] * Math.PI / 180.0, half = sizes[q] * 0.5;
                var rad = new D3(0.0, Math.Sin(th), Math.Cos(th)); var tan = new D3(0.0, Math.Cos(th), -Math.Sin(th));
                var a = (rad * Math.Cos(half) + tan * Math.Sin(half)).Normalized(); var b = (rad * Math.Cos(half) - tan * Math.Sin(half)).Normalized();
                Scene.Structures.Add(new StarStructure { Kind = StructureKind.Arch, A = a, B = b, Height = 0.8 * sizes[q], Width = 0.04, Ribbon = 0.08, Nest = q % 2 == 0 ? 3 : 1, Brightness = 1.0, Opacity = 0.6, Seed = q * 7.0, Erupt = q == 2 ? 0.8 : 0.0 });
            }
        }
        string only = Environment.GetEnvironmentVariable("SF_ONLY");
        if (only != null) Scene.Structures.RemoveAll(x => (only == "arch") != (x.Kind == StructureKind.Arch));
        float loopSize = Env("SF_LOOP", Environment.GetEnvironmentVariable("SF_DEMO") == "1" ? 1f : 2.5f);
        for (int q = 0; q < Scene.Structures.Count; q++) { var st = Scene.Structures[q]; st.Height *= loopSize; st.Width *= Math.Sqrt(loopSize); Scene.Structures[q] = st; }
        Activity = (float)Math.Min(Scene.Activity, 1.5); Spread = (float)Scene.StreamerSpread; MeanLat = (float)StarActivity.MeanLatitude(Scene.CyclePhase);
        TexTurn = (float)(2.0 * Math.PI * ((ut / profile.EquatorialRotationPeriod) % 1.0)); TimeS = 1234f; Seed = seed % 997;
        Console.WriteLine($"phase {Scene.CyclePhase:0.00} activity {Scene.Activity:0.00}: {Scene.Spots.Count} spot groups, {Scene.Structures.Count} structures, {Scene.Shells.Count} CME shells");
        foreach (var s in Scene.Structures) Console.WriteLine($"  {s.Label}: height {s.Height:0.000} width {s.Width:0.000}");

        if (Environment.GetEnvironmentVariable("SF_DEBUG") == "1") foreach (float m in new[] { 1f, 0.5f, 0.2f, 0.05f }) { Vector3 nn = new Vector3(MathF.Sqrt(1 - m * m), 0f, m); Console.WriteLine($"mu {m}: {Surface(nn, new Vector3(0, 0, -1), 0.001f)}"); }
        float tanY = MathF.Tan(30f * MathF.PI / 180f), distance = 1f / (size * tanY);
        float bound = View == 0 ? 3.5f : 5f;
        Vector3 viewer = V(toViewer);
        Vector3 camPos = viewer * distance;
        Vector3 forward = -viewer;
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);
        float tanX = tanY * w / h;
        PixelAngle = 2f * tanY / h;
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w * 2f - 1f, vv = 1f - (py + 0.5f) / h * 2f;
                Vector3 dir = Vector3.Normalize(forward + right * (u * tanX) + up * (vv * tanY));
                Vector4 o = Pixel(px, py, camPos, dir);
                // Over the dark sky, premultiplied.
                Vector3 c = new Vector3(o.X, o.Y, o.Z) * exposure;
                c = new Vector3(1f - MathF.Exp(-c.X), 1f - MathF.Exp(-c.Y), 1f - MathF.Exp(-c.Z));
                int i = (py * w + px) * 3;
                rgb[i] = Enc(c.X); rgb[i + 1] = Enc(c.Y); rgb[i + 2] = Enc(c.Z);
            }
        });
        Program.WritePngPublic(path, w, h, rgb);
        Console.WriteLine("wrote " + path);
        return 0;
    }

    static byte Enc(float c) { c = Saturate(c); c = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f; return (byte)Math.Clamp((int)(c * 255f + 0.5f), 0, 255); }
}
