using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Tde;

// tde-svg out.svg: a diagram of the model itself, drawn from its numbers: the orbit plane near the hole, the debris
// out to thousands of horizon radii, and the light curve.
internal static class TdeSvg
{
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    static string F(double v) => v.ToString("0.##", Ci);

    public static int Run(string[] args)
    {
        string path = args.Length > 1 ? args[1] : "tde.svg";
        var p = new TdeParameters { InclinationDeg = 35, NodeDeg = 20, PeriapsisAngleDeg = 60 };
        const double horizon = 9.6e9;
        var tde = new TidalDisruption(p, horizon, 42);
        var frame = new TdeFrame();
        Double3 ex = tde.OrbitX, ey = tde.OrbitY;
        (double, double) Plane(Double3 v) => (Double3.Dot(v, ex), Double3.Dot(v, ey));

        const int W = 1200, H = 1480;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {W} {H}' width='{W}' height='{H}' font-family='Inter, Segoe UI, sans-serif'>");
        sb.Append("<rect width='100%' height='100%' fill='#0a0c13'/>");
        sb.Append("<defs><marker id='arr' viewBox='0 0 10 10' refX='9' refY='5' markerWidth='7' markerHeight='7' orient='auto-start-reverse'><path d='M0,0 L10,5 L0,10 z' fill='#c9d1e3'/></marker></defs>");
        sb.Append("<text x='40' y='52' fill='#eef2fb' font-size='30' font-weight='600'>Tidal disruption at Kcalbeloh: the model</text>");
        sb.Append($"<text x='40' y='84' fill='#9aa4bd' font-size='16'>Hole {tde.HoleMassSuns / 1e6:0.00} million Suns (horizon {horizon:0.0e0} m). Star 1.2 Suns, 1.3 solar radii, n = 3, beta = 2. Orbit-plane view; distances in horizon radii (r_s).</text>");

        // Panel A: the inner region.
        double ax = 40, ay = 110, aw = 540, ah = 540, scaleA = aw / 2 / 36.0;
        double cxA = ax + aw / 2, cyA = ay + ah / 2;
        Panel(sb, ax, ay, aw, ah, "Near the hole (36 r_s across each way)");
        (double, double) A((double X, double Y) q) => (cxA + q.X * scaleA, cyA - q.Y * scaleA);
        Circle(sb, A((0, 0)), 1.0 * scaleA, "#000", "#5b6478", 1, true);
        Circle(sb, A((0, 0)), 1.5 * scaleA, "none", "#5b6478", 1, false, "3 3");
        Circle(sb, A((0, 0)), tde.TidalRadius * scaleA, "none", "#e0b45a", 1.2, false, "6 4");
        Label(sb, A((tde.TidalRadius * 0.71, tde.TidalRadius * 0.71)), $"tidal radius {tde.TidalRadius:0.0}", "#e0b45a", 13, 6, -4);
        Circle(sb, A((0, 0)), tde.CircularizationRadius * scaleA, "none", "#7fb6ff", 1, false, "2 4");
        Label(sb, A((-tde.CircularizationRadius * 0.95, -tde.CircularizationRadius * 0.4)), $"circularisation {tde.CircularizationRadius:0.0}", "#7fb6ff", 13, -150, 16);
        // The star's path (its centre of mass): the parabolic geodesic, solid until it comes apart, dashed after.
        var com = new KSPVE.Pure.Space.Relativity.SchwarzschildOrbit(4.0 * tde.Periapsis, 1.0, 0.5, 400.0);
        var star = new List<(double, double)>();
        var after = new List<(double, double)>();
        double chiT = Math.Acos(Math.Clamp(2.0 * tde.Periapsis / tde.TidalRadius - 1.0, -1.0, 1.0));
        for (double chi = -2.6; chi <= 2.6; chi += 0.005)
        {
            double r = com.Radius(chi), phi = com.AzimuthAt(chi);
            var q = A((r * Math.Cos(phi), r * Math.Sin(phi)));
            if (chi <= -chiT) star.Add(q); else after.Add(q);
        }
        if (star.Count > 0) after.Insert(0, star[star.Count - 1]);
        Poly(sb, star, "#f3e6c8", 1.8, "none");
        sb.Append("<g stroke-dasharray='5 5'>");
        Poly(sb, after, "#f3e6c8", 1.2, "none");
        sb.Append("</g>");
        if (star.Count > 0)
        {
            (double sx, double sy) = star[star.Count * 2 / 3];
            Circle(sb, (sx, sy), 5, "#f6efe0", "none", 0, true);
            Label(sb, (sx, sy), "the star falls in", "#f3e6c8", 13, 10, -8);
        }
        Label(sb, A((tde.Periapsis, 0)), $"periapsis {tde.Periapsis:0.0}", "#c9d1e3", 13, 8, 18);
        // Debris just after the disruption, and the returning stream with its periapsis shift.
        string[] early = { "#ffd9a0", "#ffb36b", "#ff8a4c" };
        double[] earlyDays = { 0.005, 0.02, 0.05 };
        for (int i = 0; i < earlyDays.Length; i++)
        {
            tde.Evaluate(tde.ApproachSeconds + earlyDays[i] * 86400.0, frame);
            var pts = new List<(double, double)>();
            for (int v = 0; v < frame.VertexCount; v++) pts.Add(A(Plane(frame.Position[v])));
            Poly(sb, pts, early[i], 3.2, "none");
        }
        Label(sb, A((-4, -16)), "debris 0.005, 0.02, 0.05 days after: a strand", "#ffb36b", 13, -170, 0);
        tde.Evaluate(tde.ApproachSeconds + 66.0 * 86400.0, frame);
        var ret = new List<(double, double)>();
        for (int v = 0; v < frame.VertexCount; v++)
        {
            (double X, double Y) q = Plane(frame.Position[v]);
            if (Math.Abs(q.X) < 36 && Math.Abs(q.Y) < 36) ret.Add(A(q));
            else if (ret.Count > 0) break;
        }
        Poly(sb, ret, "#8fd0ff", 2.2, "none");
        double adv = tde.PeriapsisAdvance;
        var pa = A((tde.Periapsis * 2.2 * Math.Cos(adv), tde.Periapsis * 2.2 * Math.Sin(adv)));
        Line(sb, A((0, 0)), A((tde.Periapsis * 2.2, 0)), "#c9d1e3", 1, "4 3");
        Line(sb, A((0, 0)), pa, "#8fd0ff", 1, "4 3");
        Label(sb, A((tde.Periapsis * 2.3 * Math.Cos(adv / 2), tde.Periapsis * 2.3 * Math.Sin(adv / 2))), $"{adv * 180 / Math.PI:0} deg periapsis shift", "#8fd0ff", 13, 8, 0);
        Label(sb, A((14, 20)), "returning stream, day 66", "#8fd0ff", 13, 0, 0);
        Legend(sb, ax + 16, ay + ah - 70, new[] { ("#000", "horizon (r = 1), photon sphere 1.5"), ("#e0b45a", "tidal radius"), ("#7fb6ff", "circularisation radius") });

        // Panel B: the whole stream.
        double bx = 620, by = 110, bw = 540, bh = 540, scaleB = bw / 2 / 2600.0;
        double cxB = bx + bw / 2, cyB = by + bh / 2;
        Panel(sb, bx, by, bw, bh, "The whole stream (2,600 r_s each way)");
        (double, double) B((double X, double Y) q) => (cxB + q.X * scaleB, cyB - q.Y * scaleB);
        Circle(sb, B((0, 0)), 3, "#cfe3ff", "none", 0, true);
        double[] days = { 1, 5, 10, 20, 40, 60, 70 };
        string[] colours = { "#ffe2b0", "#ffc98a", "#ffad6a", "#f08f5a", "#d9745a", "#b9655f", "#8fd0ff" };
        for (int i = 0; i < days.Length; i++)
        {
            tde.Evaluate(tde.ApproachSeconds + days[i] * 86400.0, frame);
            var pts = new List<(double, double)>();
            for (int v = 0; v < frame.VertexCount; v++) pts.Add(B(Plane(frame.Position[v])));
            Poly(sb, pts, colours[i], 1.8, "none");
            if (pts.Count > 0) Label(sb, pts[pts.Count - 1], $"day {days[i]:0}", colours[i], 12, 4, -4);
        }
        tde.Evaluate(tde.ApproachSeconds + 70.0 * 86400.0, frame);
        if (frame.ShockVisible)
        {
            var sp = B(Plane(frame.ShockPosition));
            Circle(sb, sp, 6, "#ffffff", "#8fd0ff", 2, true);
            Label(sb, sp, $"self-intersection shock ({frame.ShockPosition.Length:0} r_s)", "#ffffff", 13, 10, 18);
        }
        double rph = 0.0;
        for (double d = 64; d < 130; d += 1)
        {
            tde.Evaluate(tde.ApproachSeconds + d * 86400.0, frame);
            rph = Math.Max(rph, frame.PhotosphereMeters / horizon);
        }
        Circle(sb, B((0, 0)), rph * scaleB, "none", "#6f8cff", 1.2, false, "5 4");
        Label(sb, B((0, -rph)), $"peak photosphere of the outflow, {rph:0} r_s", "#9fb2ff", 13, -110, 18);
        Label(sb, (bx + 16, by + bh - 40), "The bound half swings out and back; the unbound half escapes.", "#9aa4bd", 13, 0, 0);
        Label(sb, (bx + 16, by + bh - 20), "Colours: days after disruption.", "#9aa4bd", 13, 0, 0);

        // Panel C: the light curve.
        double cx = 40, cy = 690, cw = 1120, ch = 420;
        Panel(sb, cx, cy, cw, ch, "Light curve: fallback rate and luminosity (log-log)");
        double t0 = 30, t1 = 6000, y0 = 0.01, y1 = 40;
        double px0 = cx + 70, px1 = cx + cw - 30, py0 = cy + ch - 50, py1 = cy + 50;
        (double, double) C(double t, double y) => (px0 + (Math.Log10(t) - Math.Log10(t0)) / (Math.Log10(t1) - Math.Log10(t0)) * (px1 - px0),
                                                   py0 - (Math.Log10(Math.Max(y, y0)) - Math.Log10(y0)) / (Math.Log10(y1) - Math.Log10(y0)) * (py0 - py1));
        foreach (double t in new double[] { 30, 100, 300, 1000, 3000 })
        {
            Line(sb, C(t, y0), C(t, y1), "#1c2232", 1, null);
            Label(sb, C(t, y0), t.ToString(Ci) + " d", "#9aa4bd", 12, -14, 18);
        }
        foreach (double y in new double[] { 0.01, 0.1, 1, 10 })
        {
            Line(sb, C(t0, y), C(t1, y), y == 1 ? "#3a4560" : "#1c2232", 1, null);
            Label(sb, C(t0, y), y.ToString(Ci), "#9aa4bd", 12, -44, 4);
        }
        Label(sb, C(t1, 1), "Eddington", "#7d88a3", 12, -70, -6);
        var fb = new List<(double, double)>();
        var lum = new List<(double, double)>();
        for (double lt = Math.Log10(t0); lt <= Math.Log10(t1); lt += 0.01)
        {
            double t = Math.Pow(10, lt);
            tde.Evaluate(tde.ApproachSeconds + t * 86400.0, frame);
            if (frame.FallbackEddington > 0) fb.Add(C(t, frame.FallbackEddington));
            if (frame.Luminosity > 0) lum.Add(C(t, frame.Luminosity));
        }
        Poly(sb, fb, "#ffb36b", 2.4, "none");
        Poly(sb, lum, "#8fd0ff", 2.4, "none");
        // t^-5/3 reference through the fallback at 300 days.
        tde.Evaluate(tde.ApproachSeconds + 300 * 86400.0, frame);
        double refY = frame.FallbackEddington;
        Line(sb, C(150, refY * Math.Pow(2, 5.0 / 3)), C(4000, refY * Math.Pow(300.0 / 4000, 5.0 / 3)), "#e6e9f2", 1, "6 5");
        Label(sb, C(1200, refY * Math.Pow(300.0 / 1200, 5.0 / 3)), "t^-5/3", "#e6e9f2", 13, 8, -6);
        Legend(sb, px1 - 330, py1 + 6, new[] { ("#ffb36b", "debris returning to periapsis (Mdot / Mdot_Edd)"), ("#8fd0ff", "luminosity (L / L_Edd)") });
        Label(sb, (cx + 80, cy + ch - 12), $"Calibrated to hydrodynamic simulations (Guillochon & Ramirez-Ruiz 2013): peak {tde.TargetPeakSeconds / 86400:0} days after disruption at {tde.TargetPeakRate / tde.EddingtonAccretionRate:0} x Eddington; first debris back after {tde.MostBoundPeriodSeconds / 86400:0} days.", "#9aa4bd", 13, 0, 0);

        // Key numbers.
        double ky = 1150;
        Panel(sb, 40, ky, 1120, 290, "Key numbers (physical time; the game plays it 10x slower by default)");
        string[] lines =
        {
            $"Tidal radius {tde.TidalRadius:0.0} r_s, periapsis {tde.Periapsis:0.0} r_s: the star swings past at {Math.Sqrt(1.0 / tde.Periapsis):0.00} c.",
            $"Fall from {10 * tde.TidalRadius:0} r_s to the tidal radius: {tde.ApproachSeconds / 3600:0.0} h. Torn apart over about an hour.",
            $"Half the star is bound ({tde.BoundFraction:0.00}); the energy spread is {tde.EnergyStretch:0.00} x the undisturbed star's (it is stretched first).",
            $"Each returning orbit has turned by {tde.PeriapsisAdvance * 180 / Math.PI:0} degrees (exact Schwarzschild geodesics), so the stream crosses itself and shocks.",
            $"Peak: {tde.PeakLuminosityWatts / tde.EddingtonLuminosity:0.0} L_Edd ({tde.PeakLuminosityWatts:0.0e0} W) at day {tde.PeakSeconds / 86400:0}; the outflow's photosphere reaches ~{rph:0} r_s at ~50,000 K.",
            $"The new disk circularises at {tde.CircularizationRadius:0.0} r_s, thick while fed above Eddington, thinning as the fallback fades over {tde.EndSeconds / 86400 / 365:0} years."
        };
        for (int i = 0; i < lines.Length; i++) Label(sb, (64, ky + 70 + i * 36), lines[i], "#dbe2f1", 16, 0, 0);
        sb.Append("</svg>");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine($"wrote {path}");
        return 0;
    }

    static void Panel(StringBuilder sb, double x, double y, double w, double h, string title)
    {
        sb.Append($"<rect x='{F(x)}' y='{F(y)}' width='{F(w)}' height='{F(h)}' rx='10' fill='#0f1320' stroke='#1f2638'/>");
        sb.Append($"<text x='{F(x + 16)}' y='{F(y + 28)}' fill='#c9d1e3' font-size='16' font-weight='600'>{Esc(title)}</text>");
    }

    static void Circle(StringBuilder sb, (double X, double Y) c, double r, string fill, string stroke, double width, bool filled, string dash = null) =>
        sb.Append($"<circle cx='{F(c.X)}' cy='{F(c.Y)}' r='{F(r)}' fill='{(filled ? fill : "none")}' stroke='{stroke}' stroke-width='{F(width)}'{(dash != null ? $" stroke-dasharray='{dash}'" : "")}/>");

    static void Line(StringBuilder sb, (double X, double Y) a, (double X, double Y) b, string stroke, double width, string dash) =>
        sb.Append($"<line x1='{F(a.X)}' y1='{F(a.Y)}' x2='{F(b.X)}' y2='{F(b.Y)}' stroke='{stroke}' stroke-width='{F(width)}'{(dash != null ? $" stroke-dasharray='{dash}'" : "")}/>");

    static void Poly(StringBuilder sb, List<(double X, double Y)> pts, string stroke, double width, string fill)
    {
        if (pts.Count < 2) return;
        sb.Append($"<polyline fill='{fill}' stroke='{stroke}' stroke-width='{F(width)}' stroke-linecap='round' stroke-linejoin='round' points='");
        foreach ((double x, double y) in pts) sb.Append(F(x)).Append(',').Append(F(y)).Append(' ');
        sb.Append("'/>");
    }

    static void Label(StringBuilder sb, (double X, double Y) at, string text, string fill, double size, double dx, double dy) =>
        sb.Append($"<text x='{F(at.X + dx)}' y='{F(at.Y + dy)}' fill='{fill}' font-size='{F(size)}'>{Esc(text)}</text>");

    static void Legend(StringBuilder sb, double x, double y, (string Colour, string Text)[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            string c = items[i].Colour == "#000" ? "#5b6478" : items[i].Colour;
            sb.Append($"<rect x='{F(x)}' y='{F(y + i * 20 - 9)}' width='14' height='4' fill='{c}'/>");
            Label(sb, (x + 22, y + i * 20), items[i].Text, "#c9d1e3", 13, 0, 0);
        }
    }

    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
