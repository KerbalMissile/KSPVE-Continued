using System;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Aurora;

/// <summary>
/// The reference renderer of the aurora: one view ray through the glowing layers of a body, summed as emission (no
/// scattering; the layers are thin enough in column density that the light only meets the air below, which dims and reddens
/// it). Aurora.shader implements the same algorithm line for line; the offline preview calls this class, so what the preview
/// shows is what the game's maths makes.
///
/// Structure is a function of where a point's magnetic field line meets the surface (its footprint: invariant colatitude and
/// magnetic local time), not of the point itself, so every curtain is field-aligned, rays converge at the magnetic zenith
/// (the corona) and a ray running along a curtain shows its full depth. Discrete arcs are thin sheets: the ray's integral
/// through each is done by finding where it crosses the sheet and integrating the Gaussian profile there (Gauss-Hermite), which
/// is exact for any sheet thickness; the smooth kinds (diffuse, pulsating, proton, SAR, global) are marched in fixed steps and
/// the airglow layers analytically between their shells.
/// </summary>
public sealed class AuroraTracer
{
    // ---- inputs ----
    public AuroraFrame F;
    public EmissionLut Lut;
    /// <summary>The camera exposure gain of a dark-adapted view over a daylight one (see <see cref="NightExposure"/>).</summary>
    public double NightGain = 5.0e6;
    public double AuroraBoost = 1.0, GlowBoost = 1.0;
    /// <summary>Rayleigh optical depth of the air straight up at the surface for red, green and blue, and its scale height (m).</summary>
    public Rgb Tau0 = new Rgb(0.04, 0.09, 0.2);
    public double ScaleHeight = 7000.0;
    public bool Arcs = true, Diffuse = true, Pulsating = true, Black = true, Proton = true, Sar = true, Steve = true, Theta = true, Global = true, Crustal = true, Giant = true, Glow = true;

    // ---- derived by Prepare ----
    private uint _seed;
    private double _R, _tA, _tP, _tH, _hw, _lambda, _kp;
    private Double3 _c, _axis, _noon, _dawn, _sun;
    private double _wbExpansion, _arcExists;
    private double _lowArc, _highArc, _lowS1, _highS1, _lowSar, _highSar;
    private readonly double[] _glowLow = new double[4], _glowHigh = new double[4];
    private bool _hasArc, _hasS1, _hasSar;

    private const double Km = 1000.0;

    public void Prepare()
    {
        _seed = AuroraNoise.Hash(F.Seed24 ^ 0xA5A5U);
        _R = F.Radius; _c = F.Centre; _axis = F.Axis; _noon = F.Noon; _dawn = F.Dawn; _sun = F.Sun;
        double ut = F.Ut;
        _tA = Fract(ut / 600.0); _tP = Fract(ut / 300.0); _tH = Fract(ut / 3600.0);
        _hw = Math.Max(F.Oval.HalfWidth, 0.01) * _R;
        _kp = F.Oval.Kp;
        _lambda = 60.0 * Km * (1.0 - 0.4 * Math.Min(_kp, 9.0) / 9.0);
        _lowArc = double.MaxValue; _highArc = 0;
        foreach (EmissionRow r in new[] { EmissionRow.Arc, EmissionRow.ArcHard, EmissionRow.SoftArc, EmissionRow.Steve })
            Widen(r, ref _lowArc, ref _highArc, ref _hasArc);
        _lowS1 = double.MaxValue; _highS1 = 0;
        foreach (EmissionRow r in new[] { EmissionRow.Diffuse, EmissionRow.Proton, EmissionRow.Global, EmissionRow.Giant })
            Widen(r, ref _lowS1, ref _highS1, ref _hasS1);
        _lowSar = double.MaxValue; _highSar = 0;
        Widen(EmissionRow.Sar, ref _lowSar, ref _highSar, ref _hasSar);
        for (int i = 0; i < 4; i++)
        {
            int row = (int)EmissionRow.GlowGreen + i;
            _glowLow[i] = Lut.LowerEdge[row]; _glowHigh[i] = Lut.UpperEdge[row];
        }
    }

    private void Widen(EmissionRow r, ref double low, ref double high, ref bool has)
    {
        double a = Lut.LowerEdge[(int)r], b = Lut.UpperEdge[(int)r];
        if (double.IsNaN(a) || double.IsNaN(b)) return;
        low = Math.Min(low, a); high = Math.Max(high, b); has = true;
    }

    private static double Fract(double x) => x - Math.Floor(x);
    private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
    private static double SS(double a, double b, double x) => AuroraNoise.SmoothStep(a, b, x);

    // ---- geometry of a point ----
    private struct Geo
    {
        public double R;        // distance in radii
        public double ThF;      // footprint colatitude, rad (from the nearer pole)
        public double Nu;       // magnetic local time from midnight, rad, +dawnward
        public double ThO;      // the oval's colatitude here
        public double D;        // metres equatorward of the oval's centre line
        public double Y;        // metres along the oval
        public Double3 Dir;     // unit vector from the body's centre
    }

    private Geo GeoOf(Double3 p)
    {
        var g = new Geo();
        Double3 q = p - _c;
        double rq = Math.Max(q.Length, 1.0);
        double mu = Double3.Dot(q, _axis) / rq;
        double s0 = (1.0 - mu * mu) / Math.Max(rq / _R, 1.0);
        g.R = rq / _R;
        g.ThF = Math.Asin(Math.Sqrt(Math.Min(1.0, Math.Max(s0, 0.0))));
        g.Nu = Math.Atan2(Double3.Dot(q, _dawn), -Double3.Dot(q, _noon));
        g.ThO = F.Oval.ColatitudeAt(g.Nu);
        g.D = (g.ThF - g.ThO) * _R;
        g.Y = g.Nu * _R * Math.Max(Math.Sin(g.ThO), 0.05);
        g.Dir = p / Math.Max(p.Length, 1.0);
        return g;
    }

    /// <summary>Where a point sits in the oval's coordinates (for tools): magnetic local time in hours, metres equatorward of the oval's centre line, and the footprint's colatitude in degrees.</summary>
    public void Locate(Double3 p, out double mltHours, out double metresFromOval, out double footprintColatitudeDegrees)
    {
        Geo g = GeoOf(p);
        mltHours = (g.Nu * 12.0 / Math.PI % 24.0 + 24.0) % 24.0;
        metresFromOval = g.D;
        footprintColatitudeDegrees = g.ThF * 180.0 / Math.PI;
    }

    // ---- night: how dark the sky is, so how far a dark-adapted view raises the exposure ----
    // Sun below 18 degrees: fully dark; above 6 degrees: the sky is brighter than the glows by orders of magnitude.
    private double Darkness(double sinSun) => 1.0 - SS(-0.3090, -0.1045, sinSun);

    private double ViewerDark(Double3 o)
    {
        double alt = o.Length - _R;
        double dark = Darkness(Double3.Dot(o / Math.Max(o.Length, 1.0), _sun));
        double ground = 1.0 - SS(20.0 * Km, 120.0 * Km, alt);
        return 1.0 + (dark - 1.0) * ground;
    }

    private double ExposureAt(Double3 p, double viewerDark)
    {
        double dark = Math.Min(Darkness(Double3.Dot(p / Math.Max(p.Length, 1.0), _sun)), viewerDark);
        return 1.0 + (NightGain - 1.0) * dark;
    }

    // ---- the emission tables ----
    private void LutRgb(EmissionRow row, double h, out double r, out double g, out double b)
    {
        int rowIndex = (int)row;
        double f = (h - Lut.HeightLow) / (Lut.HeightHigh - Lut.HeightLow) * Lut.Samples - 0.5;
        if (f < -0.5 || f > Lut.Samples - 0.5) { r = g = b = 0; return; }
        f = Math.Max(0.0, Math.Min(Lut.Samples - 1.0, f));
        int i = (int)f;
        int j = Math.Min(i + 1, Lut.Samples - 1);
        double t = f - i;
        r = Lut.Rgb[rowIndex, i, 0] + (Lut.Rgb[rowIndex, j, 0] - Lut.Rgb[rowIndex, i, 0]) * t;
        g = Lut.Rgb[rowIndex, i, 1] + (Lut.Rgb[rowIndex, j, 1] - Lut.Rgb[rowIndex, i, 1]) * t;
        b = Lut.Rgb[rowIndex, i, 2] + (Lut.Rgb[rowIndex, j, 2] - Lut.Rgb[rowIndex, i, 2]) * t;
    }

    private Rgb RowColour(EmissionRow row, double h)
    {
        LutRgb(row, h, out double r, out double g, out double b);
        return new Rgb(r, g, b);
    }

    // ---- extinction by the air below ----
    private Rgb Transmittance(Double3 o, Double3 d, double t)
    {
        double sc = Math.Max(0.0, Math.Min(t, -Double3.Dot(o, d)));
        double tau = 0.0;
        // Both sides of the nearest approach, the nodes crowded towards it (s = sc -/+ L u^2).
        for (int side = 0; side < 2; side++)
        {
            double length = side == 0 ? sc : t - sc;
            if (length < 1.0) continue;
            for (int j = 0; j < 6; j++)
            {
                double u = (j + 0.5) / 6.0;
                double s = side == 0 ? sc - length * u * u : sc + length * u * u;
                double h = (o + d * s).Length - _R;
                tau += Math.Exp(-Math.Max(h, 0.0) / ScaleHeight) / ScaleHeight * (2.0 * length * u / 6.0);
            }
        }
        return new Rgb(Math.Exp(-tau * Tau0.R), Math.Exp(-tau * Tau0.G), Math.Exp(-tau * Tau0.B));
    }

    // ---- shells ----
    private static int Shell(Double3 o, Double3 d, double rLow, double rHigh, double tMax, double[] t0, double[] t1)
    {
        double b = Double3.Dot(o, d), oo = Double3.Dot(o, o);
        double discH = b * b - (oo - rHigh * rHigh);
        if (discH <= 0.0) return 0;
        double sh = Math.Sqrt(discH);
        double a1 = Math.Max(-b - sh, 0.0), a2 = Math.Min(-b + sh, tMax);
        if (a2 <= a1) return 0;
        double discL = b * b - (oo - rLow * rLow);
        int n = 0;
        if (discL > 0.0)
        {
            double sl = Math.Sqrt(discL);
            double b1 = -b - sl, b2 = -b + sl;
            if (b1 > a1) { t0[n] = a1; t1[n] = Math.Min(a2, b1); n++; }
            if (b2 < a2) { t0[n] = Math.Max(a1, b2); t1[n] = a2; n++; }
        }
        else { t0[n] = a1; t1[n] = a2; n++; }
        return n;
    }

    // ---- the kinds ----
    private double WindowStorm(double nu)
    {
        SubstormState s = F.Storm;
        if (s.Phase == SubstormPhase.None || s.Phase == SubstormPhase.Growth) return 0.0;
        return SS(s.SurgeAngle - 0.3, s.SurgeAngle + 0.1, nu) * (1.0 - SS(s.EastAngle - 0.1, s.EastAngle + 0.3, nu));
    }

    /// <summary>
    /// How far the arc is pushed sideways (m) at distance <paramref name="y"/> along the oval: slow irregular undulations a
    /// hundred kilometres long, smaller folds, and, in a breakup, big folds and tight curls (a Kelvin-Helmholtz roll-up).
    /// </summary>
    private double Fold(double y, double wb)
    {
        double a1 = 4.0 * Km + 8.0 * Km * _kp / 9.0;
        double breakup = F.Storm.Breakup * F.Storm.Strength * wb;
        double s1 = 2.0 * AuroraNoise.Advected1(y / (150.0 * Km), 0.25 / 150.0, _tP, _seed ^ 0xF01DU) - 1.0;
        double s2 = 2.0 * AuroraNoise.Advected1(y / (45.0 * Km), 0.3 / 45.0, _tP, _seed ^ 0xF02DU) - 1.0;
        double s3 = 2.0 * AuroraNoise.Advected1(y / (16.0 * Km), 0.5 / 16.0, _tP, _seed ^ 0xF03DU) - 1.0;
        double big = Math.Sin(2.0 * Math.PI * (y / (300.0 * Km) - _tA) + 0.4);
        return a1 * s1 + 0.35 * a1 * s2 + (0.4 * Km + 4.0 * Km * breakup) * s3 + 30.0 * Km * breakup * big;
    }

    private double DriftOffset => 25.0 * Km * Math.Sin(2.0 * Math.PI * _tH);

    /// <summary>The arc coordinate: metres equatorward of the oval centre line, with the folds and the slow drift.</summary>
    private double ArcCoordinate(in Geo g, out double wb)
    {
        wb = WindowStorm(g.Nu);
        return g.D - Fold(g.Y, wb) - DriftOffset;
    }

    private void ArcCell(int i, double wb, out bool exists, out double centre, out double width, out double amp)
    {
        uint k = unchecked((uint)(i * 4)) ^ _seed;
        double pa = 0.22 + 0.15 * Math.Min(_kp, 9.0) / 9.0 + 0.3 * F.Storm.Breakup * wb;
        exists = AuroraNoise.Rand(k) < pa;
        centre = i + 0.5 + 0.30 * (AuroraNoise.Rand(k + 1) - 0.5);
        width = _lambda * (0.035 + 0.055 * AuroraNoise.Rand(k + 2));
        double a = AuroraNoise.Rand(k + 3);
        amp = 0.25 + 0.75 * a * Math.Sqrt(a);
    }

    private double ArcZone(in Geo g, double wb)
    {
        double bulge = F.Storm.Bulge * wb * F.Storm.Strength;
        double dc = 0.2 * _hw + 0.5 * _hw * F.Storm.Growth * F.Storm.Strength;
        double sp = _hw * (1.0 + 1.4 * bulge), se = _hw * 0.9;
        double x = (g.D - dc) / (g.D < dc ? sp : se);
        double env = Math.Exp(-0.5 * x * x);
        double mlt = 0.2 + 0.8 * Math.Exp(-0.5 * Math.Pow((g.Nu + 0.2) / 0.95, 2.0)) + 0.3 * Math.Exp(-0.5 * Math.Pow((g.Nu - 1.4) / 0.7, 2.0));
        return env * Math.Min(mlt, 1.0);
    }

    /// <summary>Along-arc brightness: breaks in the arc, the rays, the ripple travelling along it.</summary>
    private double AlongArc(double y, int cell, double wb, double rayScale)
    {
        uint s = _seed ^ unchecked((uint)(cell * 7919 + 13));
        double seg = SS(0.12, 0.4, AuroraNoise.Value1(y / (350.0 * Km) + cell * 17.3, s));
        double ell = Math.Max(1.0 * Km * (0.6 + AuroraNoise.Rand(s)), rayScale);
        double n = AuroraNoise.Advected1(y / ell + cell * 11.7, 0.4 * Km / ell, _tP, s ^ 0x77U);
        double ray = 0.3 + 0.7 * n * n;
        double ripple = 1.0 + (0.15 + 0.4 * F.Storm.Breakup * wb) * Math.Sin(2.0 * Math.PI * (y / (12.0 * Km) - 100.0 * _tA));
        return seg * ray * ripple;
    }

    private double BlackAmount(in Geo g, double wb)
    {
        if (!Black || F.Black <= 0.0) return 0.0;
        double zone = SS(-1.7, -0.9, g.Nu) * (1.0 - SS(1.5, 2.2, g.Nu));
        double u = (g.D - 1.5 * _hw - 0.7 * Fold(g.Y, wb)) / (45.0 * Km);
        int i = (int)Math.Floor(u);
        uint k = unchecked((uint)(i * 4)) ^ _seed ^ 0x3B1AU;
        if (AuroraNoise.Rand(k) > 0.45) return 0.0;
        double c = i + 0.5 + 0.3 * (AuroraNoise.Rand(k + 1) - 0.5);
        double w = 6.0 * Km / (45.0 * Km);
        double x = (u - c) / w;
        double along = SS(0.3, 0.6, AuroraNoise.Value1(g.Y / (200.0 * Km) + i * 5.1, k));
        return F.Black * zone * Math.Exp(-0.5 * x * x) * along;
    }

    // ---- the tracer ----
    private const int CoarseNodes = 40, SmoothSteps = 48, SarSteps = 20, GlowSteps = 14, MaxCrossings = 14;
    private static readonly double[] GhX = { 0.0, 0.9585724646, -0.9585724646, 2.0201828705, -2.0201828705 };
    private static readonly double[] GhW = { 0.9453087205, 0.3936193232, 0.3936193232, 0.0199532421, 0.0199532421 };

    /// <summary>
    /// The scene-linear RGB seen along a ray from <paramref name="o"/> (metres from the body's centre, body frame) in direction
    /// <paramref name="d"/> (unit), up to distance <paramref name="tMax"/>; <paramref name="pixelAngle"/> is the angle one pixel
    /// subtends (rad), <paramref name="jitter"/> in [0,1) offsets the sample positions.
    /// </summary>
    public Rgb Trace(Double3 o, Double3 d, double tMax, double jitter, double pixelAngle)
    {
        double rimBody = _R;
        // The solid body stops the ray.
        double b0 = Double3.Dot(o, d), disc0 = b0 * b0 - (Double3.Dot(o, o) - rimBody * rimBody);
        if (disc0 > 0.0)
        {
            double th = -b0 - Math.Sqrt(disc0);
            if (th > 0.0) tMax = Math.Min(tMax, th);
        }
        double vdark = ViewerDark(o);
        Rgb sum = new Rgb(0, 0, 0);
        double[] t0 = new double[2], t1 = new double[2];
        if (F.Valid && _hasArc && F.HasField && (Arcs && F.Arc > 0.0 || Steve && F.Steve > 0.0) && F.Oval.NightColatitude > 0.0) sum = sum + TraceArcs(o, d, tMax, jitter, pixelAngle, vdark, t0, t1);
        bool smoothAny = Diffuse && F.Diffuse > 0.0 || Proton && F.Proton > 0.0 || Theta && F.Theta > 0.0 || Global && F.Global > 0.0 || Crustal && F.Crustal > 0.0 || Giant && F.Giant > 0.0;
        if (F.Valid && _hasS1 && smoothAny) sum = sum + TraceSmooth(o, d, tMax, jitter, pixelAngle, vdark, t0, t1);
        if (F.Valid && _hasSar && Sar && F.HasField && F.Sar > 0.0) sum = sum + TraceSar(o, d, tMax, jitter, vdark, t0, t1);
        if (F.Valid && Glow) sum = sum + TraceGlow(o, d, tMax, jitter, vdark, t0, t1);
        return sum;
    }

    private Rgb TraceArcs(Double3 o, Double3 d, double tMax, double jitter, double pixelAngle, double vdark, double[] t0, double[] t1)
    {
        int n = Shell(o, d, _R + _lowArc, _R + _highArc, tMax, t0, t1);
        Rgb sum = new Rgb(0, 0, 0);
        int crossings = 0;
        double hard = Math.Min(1.0, Math.Max(0.0, F.HardShare));
        for (int iv = 0; iv < n; iv++)
        {
            double a = t0[iv], b = t1[iv];
            if (b - a < 1.0) continue;
            Rgb trans = Transmittance(o, d, a);
            double dt = (b - a) / CoarseNodes;
            double prevU = 0, prevT = 0, prevS = 0;
            bool havePrev = false, prevInZone = false;
            for (int k = 0; k <= CoarseNodes; k++)
            {
                double t = Math.Min(a + k * dt, b);
                Double3 p = o + d * t;
                Geo g = GeoOf(p);
                bool inZone = Math.Abs(g.D) < 4.5 * _hw + 2.0 * _hw * F.Storm.Bulge;
                double wb = 0.0, u = 0.0;
                // The arc coordinate (with its folds) is only worked out where there can be arcs.
                if (inZone && Arcs && F.Arc > 0.0) u = ArcCoordinate(g, out wb) / _lambda;
                double s = ((g.ThF - (F.Oval.NightColatitude + 0.07)) * _R);   // distance equatorward of STEVE's ribbon
                if (havePrev)
                {
                    if (inZone && prevInZone && Arcs && F.Arc > 0.0)
                    {
                        double lo = Math.Min(u, prevU), hi = Math.Max(u, prevU);
                        for (int i = (int)Math.Floor(lo - 0.7); i <= (int)Math.Floor(hi + 0.7) && crossings < MaxCrossings; i++)
                        {
                            ArcCell(i, wb, out bool exists, out double centre, out double width, out double amp);
                            if (!exists || centre < lo || centre > hi || hi - lo < 1e-9) continue;
                            double tStar = prevT + (centre - prevU) / (u - prevU) * (t - prevT);
                            double dudt = (u - prevU) / (t - prevT);
                            AddArcSheet(o, d, tStar, a, b, width, Math.Abs(dudt) * _lambda, i, amp, hard, pixelAngle, vdark, trans, ref sum);
                            crossings++;
                        }
                    }
                    if (Steve && F.Steve > 0.0 && s * prevS < 0.0)
                    {
                        double tStar = prevT + (0.0 - prevS) / (s - prevS) * (t - prevT);
                        double dsdt = (s - prevS) / (t - prevT);
                        AddSteve(o, d, tStar, a, b, Math.Abs(dsdt), pixelAngle, vdark, trans, ref sum);
                    }
                }
                prevU = u; prevT = t; prevS = s; prevInZone = inZone; havePrev = true;
            }
        }
        return sum;
    }

    private void AddArcSheet(Double3 o, Double3 d, double tStar, double a, double b, double width, double dDdt, int cell, double amp, double hard,
        double pixelAngle, double vdark, Rgb trans, ref Rgb sum)
    {
        double sigmaT = width / Math.Max(dDdt, 1e-6);
        sigmaT = Math.Min(sigmaT, 60.0 * Km);
        double scale = sigmaT * Math.Sqrt(2.0);
        double boost = F.Arc * AuroraBoost;
        for (int j = 0; j < 5; j++)
        {
            double t = tStar + scale * GhX[j];
            if (t < a || t > b) continue;
            Double3 p = o + d * t;
            Geo g = GeoOf(p);
            double h = p.Length - _R;
            ArcCoordinate(g, out double wb);
            double zone = ArcZone(g, wb);
            double along = AlongArc(g.Y, cell, wb, 2.0 * pixelAngle * t);
            double black = 1.0 - 0.6 * BlackAmount(g, wb);
            double ia = boost * amp * zone * along * black * GhW[j] * scale;
            if (ia <= 0.0) continue;
            double e = ExposureAt(p, vdark);
            Rgb c = (1.0 - hard) * RowColour(EmissionRow.Arc, h) + hard * RowColour(EmissionRow.ArcHard, h);
            Rgb soft = RowColour(EmissionRow.SoftArc, h) * F.SoftShare;
            sum = sum + (c + soft) * trans * (ia * e);
        }
    }

    private void AddSteve(Double3 o, Double3 d, double tStar, double a, double b, double dsdt, double pixelAngle, double vdark, Rgb trans, ref Rgb sum)
    {
        const double width = 9.0 * Km, fenceWidth = 3.0 * Km;
        double boost = F.Steve * AuroraBoost;
        for (int pass = 0; pass < 2; pass++)
        {
            double w = pass == 0 ? width : fenceWidth;
            double sigmaT = Math.Min(w / Math.Max(dsdt, 1e-6), 60.0 * Km);
            double scale = sigmaT * Math.Sqrt(2.0);
            for (int j = 0; j < 5; j++)
            {
                double t = tStar + scale * GhX[j];
                if (t < a || t > b) continue;
                Double3 p = o + d * t;
                Geo g = GeoOf(p);
                double h = p.Length - _R;
                double window = SS(-2.4, -1.7, g.Nu) * (1.0 - SS(-0.5, 0.1, g.Nu));
                double along = SS(0.25, 0.5, AuroraNoise.Value1(g.Y / (900.0 * Km), _seed ^ 0x57E7E5U));
                double inten = boost * window * (0.35 + 0.65 * along) * GhW[j] * scale;
                Rgb c;
                if (pass == 0) c = RowColour(EmissionRow.Steve, h) * inten;
                else
                {
                    // The picket fence: green rays a few kilometres apart in the ribbon's brighter stretches.
                    double comb = Math.Pow(0.5 + 0.5 * Math.Cos(2.0 * Math.PI * g.Y / (4.5 * Km) + 2.3 * AuroraNoise.Value1(g.Y / (200.0 * Km), _seed)), 6.0);
                    double on = SS(0.45, 0.7, AuroraNoise.Value1(g.Y / (500.0 * Km), _seed ^ 0xFE5CEU));
                    c = RowColour(EmissionRow.Arc, h) * (0.35 * inten * comb * on);
                }
                sum = sum + c * trans * ExposureAt(p, vdark);
            }
        }
    }

    private Rgb TraceSmooth(Double3 o, Double3 d, double tMax, double jitter, double pixelAngle, double vdark, double[] t0, double[] t1)
    {
        int n = Shell(o, d, _R + _lowS1, _R + _highS1, tMax, t0, t1);
        Rgb sum = new Rgb(0, 0, 0);
        double total = 0.0;
        for (int iv = 0; iv < n; iv++) total += t1[iv] - t0[iv];
        if (total < 1.0) return sum;
        for (int iv = 0; iv < n; iv++)
        {
            double a = t0[iv], b = t1[iv];
            int steps = Math.Max(4, (int)Math.Round(SmoothSteps * (b - a) / total));
            double dt = (b - a) / steps;
            Rgb trans = Transmittance(o, d, a);
            for (int k = 0; k < steps; k++)
            {
                double t = a + (k + jitter) * dt;
                Double3 p = o + d * t;
                double h = p.Length - _R;
                Rgb c = SmoothAt(p, h, pixelAngle * t);
                sum = sum + c * trans * (dt * ExposureAt(p, vdark) * AuroraBoost);
            }
        }
        return sum;
    }

    private Rgb SmoothAt(Double3 p, double h, double footprint)
    {
        Rgb c = new Rgb(0, 0, 0);
        Geo g = GeoOf(p);
        double wb = WindowStorm(g.Nu);
        if (F.HasField && F.Oval.NightColatitude > 0.0 && F.Giant <= 0.0)
        {
            double black = BlackAmount(g, wb);
            // Diffuse and pulsating aurora, equatorward of the oval, strongest from dusk through midnight to morning.
            if (Diffuse && F.Diffuse > 0.0)
            {
                double dz = Math.Exp(-0.5 * Math.Pow((g.D - 1.4 * _hw) / (1.7 * _hw), 2.0));
                double wd = 0.3 + 0.7 * SS(-1.6, -0.7, g.Nu) * (1.0 - SS(1.6, 2.6, g.Nu));
                double nz = 0.55 + 0.45 * AuroraNoise.Advected2(g.Y / (90.0 * Km), g.D / (90.0 * Km), 0.15 / 90.0, 0.0, _tP, _seed ^ 0xD1FFU);
                double pw = 0.0, pulse = 0.0, mask = 0.0;
                if (Pulsating && F.Pulsating > 0.0)
                {
                    double wp = SS(0.0, 0.7, g.Nu) * (1.0 - SS(2.0, 2.7, g.Nu));
                    pw = F.Pulsating * wp;
                    PulsePatch(g, out mask, out pulse);
                }
                double level = (1.0 - mask * pw) + mask * pw * pulse * 2.2;
                double ia = F.Diffuse * dz * wd * nz * level * (1.0 - 0.9 * black);
                if (ia > 0.0) c = c + RowColour(EmissionRow.Diffuse, h) * ia;
            }
            if (Proton && F.Proton > 0.0)
            {
                double pz = Math.Exp(-0.5 * Math.Pow((g.D - 2.5 * _hw) / (1.5 * _hw), 2.0));
                double wpr = 0.35 + 0.65 * SS(-2.2, -1.0, g.Nu) * (1.0 - SS(-0.2, 0.6, g.Nu));
                double nz = 0.8 + 0.2 * AuroraNoise.Value2(g.Y / (200.0 * Km), g.D / (200.0 * Km), _seed ^ 0x9407U);
                c = c + RowColour(EmissionRow.Proton, h) * (F.Proton * pz * wpr * nz);
            }
            if (Theta && F.Theta > 0.0 && F.ThetaPresent)
            {
                double cap = 1.0 - SS(g.ThO - 0.7 * F.Oval.HalfWidth, g.ThO - 0.1 * F.Oval.HalfWidth, g.ThF);
                double nx = g.ThF * _R * -Math.Cos(g.Nu), ny = g.ThF * _R * Math.Sin(g.Nu);
                double y0 = 0.045 * _R * Math.Sin(2.0 * Math.PI * _tH + 1.7);
                double yy = ny - y0 - 30.0 * Km * Math.Sin(2.0 * Math.PI * nx / (1500.0 * Km));
                double th = Math.Exp(-0.5 * Math.Pow(yy / (22.0 * Km), 2.0)) * (0.6 + 0.4 * AuroraNoise.Value1(nx / (300.0 * Km), _seed ^ 0x7E7AU));
                c = c + (RowColour(EmissionRow.Diffuse, h) + RowColour(EmissionRow.SoftArc, h) * 0.6) * (F.Theta * cap * th);
            }
        }
        if (Giant && F.Giant > 0.0 && F.HasField)
        {
            double hwG = _hw;
            double main = Math.Exp(-0.5 * Math.Pow(g.D / hwG, 2.0)) * (0.55 + 0.45 * AuroraNoise.Advected2(g.Y / (800.0 * Km), g.D / (800.0 * Km), 0.1 / 800.0, 0.0, _tP, _seed ^ 0x61A7U))
                * (0.65 + 0.35 * Math.Cos(g.Nu));
            double spots = 0.0;
            for (int i = 0; i < F.SpotCount; i++)
            {
                FootprintSpot sp = F.Spots[i];
                double dn = g.Nu - sp.Angle;
                dn -= 2.0 * Math.PI * Math.Floor((dn + Math.PI) / (2.0 * Math.PI));
                double dth = (g.ThF - sp.Colatitude) * _R;
                double along = dn * _R * Math.Max(Math.Sin(sp.Colatitude), 0.05);
                double spot = Math.Exp(-0.5 * (dth * dth + along * along) / Math.Pow(0.012 * _R, 2.0));
                double tail = dn > 0.0 && dn < sp.Tail ? Math.Exp(-dn / (0.4 * sp.Tail)) * 0.3 * Math.Exp(-0.5 * Math.Pow(dth / (0.02 * _R), 2.0)) : 0.0;
                spots += sp.Brightness * (spot + tail);
            }
            c = c + RowColour(EmissionRow.Giant, h) * (F.Giant * main + spots);
        }
        if (Global && F.Global > 0.0)
        {
            Double3 q = g.Dir * (_R / (220.0 * Km));
            double n = AuroraNoise.Advected3(q.X, q.Y, q.Z, 0.1 / 220.0, 0.0, 0.0, _tP, _seed ^ 0x6104U);
            double patch = 0.04 + 0.96 * SS(0.55, 0.85, n);
            c = c + RowColour(EmissionRow.Global, h) * (F.Global * patch);
        }
        if (Crustal && F.Crustal > 0.0)
        {
            double sumRegion = 0.0;
            for (int j = 0; j < F.CrustalCount; j++)
            {
                CrustalSpot s = F.CrustalSpots[j];
                double ang = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Double3.Dot(g.Dir, s.Direction))));
                double dist = ang * _R;
                double gz = Math.Exp(-0.5 * Math.Pow(dist / Math.Max(0.45 * s.Radius * _R, 1.0), 2.0));
                Double3 q = g.Dir * (_R / (25.0 * Km));
                double ridge = 1.0 - Math.Abs(2.0 * AuroraNoise.Value3(q.X, q.Y, q.Z, _seed ^ 0xC857U) - 1.0);
                double flick = 1.0 + 0.3 * Math.Sin(2.0 * Math.PI * (40.0 * _tA) + j * 2.0);
                sumRegion += s.Strength * gz * (0.2 + 0.8 * ridge * ridge * ridge) * flick;
            }
            c = c + RowColour(EmissionRow.Global, h) * (F.Crustal * sumRegion);
        }
        return c;
    }

    /// <summary>Pulsating patches: round patches in a coarse grid, each blinking with its own period of 2 to 20 s.</summary>
    private void PulsePatch(in Geo g, out double mask, out double pulse)
    {
        const double cell = 70.0 * Km;
        double u = g.Y / cell, v = g.D / cell;
        int iu = (int)Math.Floor(u), iv = (int)Math.Floor(v);
        uint k = unchecked((uint)(iu * 73856093) ^ (uint)(iv * 19349663)) ^ _seed ^ 0x9B15U;
        mask = 0.0; pulse = 0.0;
        if (AuroraNoise.Rand(k) > 0.65) return;
        double cx = (iu + 0.5 + 0.1 * (AuroraNoise.Rand(k + 1) - 0.5)) * cell, cy = (iv + 0.5 + 0.1 * (AuroraNoise.Rand(k + 2) - 0.5)) * cell;
        double radius = (20.0 + 12.0 * AuroraNoise.Rand(k + 3)) * Km;
        // Irregular, a little longer along the oval than across it.
        double warp = 0.65 + 0.7 * AuroraNoise.Value2(g.Y / (22.0 * Km), g.D / (22.0 * Km), k ^ 0x1234U);
        double dist = Math.Sqrt(Math.Pow((g.Y - cx) / 1.6, 2.0) + Math.Pow(g.D - cy, 2.0));
        mask = 1.0 - SS(0.55 * radius * warp, radius * warp, dist);
        double freq = Math.Floor(30.0 + 270.0 * AuroraNoise.Rand(k + 4));   // cycles per 600 s: 20 s to 2 s
        double phase = AuroraNoise.Rand(k + 5) * 2.0 * Math.PI;
        pulse = SS(-0.25, 0.25, Math.Sin(2.0 * Math.PI * freq * _tA + phase));
    }

    private Rgb TraceSar(Double3 o, Double3 d, double tMax, double jitter, double vdark, double[] t0, double[] t1)
    {
        int n = Shell(o, d, _R + _lowSar, _R + _highSar, tMax, t0, t1);
        Rgb sum = new Rgb(0, 0, 0);
        double total = 0.0;
        for (int iv = 0; iv < n; iv++) total += t1[iv] - t0[iv];
        if (total < 1.0) return sum;
        for (int iv = 0; iv < n; iv++)
        {
            double a = t0[iv], b = t1[iv];
            int steps = Math.Max(3, (int)Math.Round(SarSteps * (b - a) / total));
            double dt = (b - a) / steps;
            Rgb trans = Transmittance(o, d, a);
            for (int k = 0; k < steps; k++)
            {
                double t = a + (k + jitter) * dt;
                Double3 p = o + d * t;
                Geo g = GeoOf(p);
                double thSar = F.Oval.NightColatitude + 0.12;
                double x = (g.ThF - thSar) * _R / (280.0 * Km);
                double w = SS(-2.4, -1.2, g.Nu) * (1.0 - SS(1.4, 2.4, g.Nu));
                double nz = 0.7 + 0.3 * AuroraNoise.Value1(g.Y / (400.0 * Km), _seed ^ 0x5A2U);
                double ia = F.Sar * Math.Exp(-0.5 * x * x) * w * nz;
                if (ia <= 0.0) continue;
                sum = sum + RowColour(EmissionRow.Sar, p.Length - _R) * trans * (ia * dt * ExposureAt(p, vdark) * AuroraBoost);
            }
        }
        return sum;
    }

    private Rgb TraceGlow(Double3 o, Double3 d, double tMax, double jitter, double vdark, double[] t0, double[] t1)
    {
        Rgb sum = new Rgb(0, 0, 0);
        double[] strength = { F.GlowGreen, F.GlowSodium, F.GlowHydroxyl, F.GlowRed };
        for (int layer = 0; layer < 4; layer++)
        {
            if (strength[layer] <= 0.0 || double.IsNaN(_glowLow[layer])) continue;
            EmissionRow row = EmissionRow.GlowGreen + layer;
            int n = Shell(o, d, _R + _glowLow[layer], _R + _glowHigh[layer], tMax, t0, t1);
            double total = 0.0;
            for (int iv = 0; iv < n; iv++) total += t1[iv] - t0[iv];
            if (total < 1.0) continue;
            for (int iv = 0; iv < n; iv++)
            {
                double a = t0[iv], b = t1[iv];
                int steps = Math.Max(3, (int)Math.Round(GlowSteps * (b - a) / total));
                double dt = (b - a) / steps;
                Rgb trans = Transmittance(o, d, a);
                for (int k = 0; k < steps; k++)
                {
                    double t = a + (k + jitter) * dt;
                    Double3 p = o + d * t;
                    Double3 u = p / p.Length;
                    double h = p.Length - _R;
                    Rgb c = RowColour(row, h);
                    double w = GlowStructure(u, layer);
                    sum = sum + c * trans * (strength[layer] / 1000.0 * w * dt * ExposureAt(p, vdark) * GlowBoost);
                }
            }
        }
        return sum;
    }

    /// <summary>Airglow structure: patches a couple of hundred kilometres across and gravity waves, bands 25-45 km apart that drift.</summary>
    private double GlowStructure(Double3 u, int layer)
    {
        Double3 q = u * (_R / (200.0 * Km));
        double patch = 1.0 + 0.6 * (AuroraNoise.Value3(q.X, q.Y, q.Z, _seed ^ (0x61A0U + (uint)layer)) - 0.5);
        Double3 q2 = u * (_R / (350.0 * Km));
        double env = SS(0.35, 0.7, AuroraNoise.Value3(q2.X, q2.Y, q2.Z, _seed ^ 0x6A7EU));
        Double3 d1 = new Double3(0.3, 0.2, 0.93).Normalized(), d2 = new Double3(-0.8, 0.1, 0.59).Normalized();
        double w1 = Math.Sin(2.0 * Math.PI * (Math.Asin(Math.Max(-1.0, Math.Min(1.0, Double3.Dot(u, d1)))) * _R / (25.0 * Km) - 1.0 * _tA));
        double w2 = Math.Sin(2.0 * Math.PI * (Math.Asin(Math.Max(-1.0, Math.Min(1.0, Double3.Dot(u, d2)))) * _R / (45.0 * Km) - 2.0 * _tA) + 0.8);
        return Math.Max(0.0, patch * (1.0 + env * (0.18 * w1 + 0.12 * w2)));
    }
}

/// <summary>The exposure of a dark-adapted view relative to daylight, from camera settings.</summary>
public static class NightExposure
{
    /// <summary>EV at ISO 100: log2(N^2 / t x 100 / ISO).</summary>
    public static double Ev100(double fNumber, double shutterSeconds, double iso) => Math.Log(fNumber * fNumber / shutterSeconds * 100.0 / iso, 2.0);

    /// <summary>
    /// The factor by which a night camera collects more light than a daytime one ("sunny 16": f/16, 1/100 s, ISO 100, EV 14.6).
    /// With ISO 6400, f/1.8 and 10 s, a typical aurora photograph, it is about 5 x 10^6.
    /// </summary>
    public static double Gain(double fNumber, double shutterSeconds, double iso) => Math.Pow(2.0, Ev100(16.0, 0.01, 100.0) - Ev100(fNumber, shutterSeconds, iso));
}
