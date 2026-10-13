using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space;

/// <summary>
/// Mass transfer in a close binary: a companion star filling its Roche lobe spills gas through the inner Lagrange point
/// L1, and the stream falls ballistically round the compact object (Lubow and Shu 1975) until it strikes the edge of the
/// disk, at the hot spot. Worked in the binary's rotating frame in units of the separation a, the total mass and the orbital
/// angular speed (G = M = a = Omega = 1): the compact object (mass 1/(1+q)) at x = -q/(1+q), the companion (q/(1+q)) at
/// x = 1/(1+q), the companion moving towards +y.
/// </summary>
public static class RocheStream
{
    /// <summary>
    /// The inner Lagrange point's distance from the compact object, as a fraction of the separation, for mass ratio
    /// q = companion / compact object (bisection on the effective potential's slope between the two).
    /// </summary>
    public static double L1FromCompact(double q)
    {
        q = Math.Max(q, 1e-9);
        double m1 = 1.0 / (1.0 + q), m2 = q / (1.0 + q);
        double x1 = -m2, x2 = m1;
        double lo = x1 + 1e-9, hi = x2 - 1e-9;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Slope(mid, m1, m2, x1, x2) > 0.0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi) - x1;
    }

    // d(Phi)/dx on the line between them (Phi = -m1/r1 - m2/r2 - x^2/2): falling from +inf by the compact object to -inf by the companion.
    private static double Slope(double x, double m1, double m2, double x1, double x2) =>
        m1 * (x - x1) / Math.Pow(Math.Abs(x - x1), 3) + m2 * (x - x2) / Math.Pow(Math.Abs(x - x2), 3) - x;

    /// <summary>
    /// The effective (Roche) potential in the rotating frame at <paramref name="p"/> (fractions of a), centred on the compact
    /// object with x towards the companion and z along the orbit's axis, and its <paramref name="gradient"/>. A star filling its
    /// Roche lobe is the region round the companion where it is below <see cref="LobePotential"/>: drawn out to a point at L1.
    /// </summary>
    public static double Potential(double q, Double3 p, out Double3 gradient)
    {
        q = Math.Max(q, 1e-9);
        double m1 = 1.0 / (1.0 + q), m2 = q / (1.0 + q);
        Double3 d2 = p - Double3.UnitX, c = new Double3(p.X - m2, p.Y, 0.0);   // from the companion; from the axis through the centre of mass
        double r1 = Math.Max(p.Length, 1e-12), r2 = Math.Max(d2.Length, 1e-12);
        gradient = p * (m1 / (r1 * r1 * r1)) + d2 * (m2 / (r2 * r2 * r2)) - c;
        return -m1 / r1 - m2 / r2 - 0.5 * c.LengthSquared;
    }

    /// <summary>
    /// Where the stream settles into its first ring round the compact object, as a fraction of the separation: the radius
    /// of the circular orbit with the angular momentum the gas had at L1 (it shares the binary's rotation there), (1 + q) b^4
    /// for L1 at b from the compact object. A disk still forming is a ring here, the stream sweeping round to it from L1.
    /// </summary>
    public static double CircularizationRadius(double q)
    {
        double b = L1FromCompact(q);
        return (1.0 + Math.Max(q, 1e-9)) * b * b * b * b;
    }

    /// <summary>
    /// How far out a disk round the compact object can spread before the companion's tides stop it, as a fraction of the
    /// separation (Paczynski 1977: 0.6 / (1 + q), for 0.03 &lt; q &lt; 1): a grown disk's edge, the stream striking it.
    /// </summary>
    public static double TidalRadius(double q) => 0.6 / (1.0 + Math.Max(q, 0.0));

    /// <summary>The Roche potential at L1: the surface of a star just filling its lobe.</summary>
    public static double LobePotential(double q) => Potential(q, new Double3(L1FromCompact(q), 0.0, 0.0), out _);

    /// <summary>The companion's Roche lobe radius over the separation (Eggleton 1983), for q = companion / compact object.</summary>
    public static double LobeRadius(double q)
    {
        q = Math.Max(q, 1e-9);
        double q23 = Math.Pow(q, 2.0 / 3.0);
        return 0.49 * q23 / (0.6 * q23 + Math.Log(1.0 + Math.Pow(q, 1.0 / 3.0)));
    }

    /// <summary>
    /// The stream's path from L1 to where it comes within <paramref name="stopRadius"/> (fraction of a) of the compact
    /// object, or to its closest approach when it never does, as <paramref name="points"/> points evenly spaced along it,
    /// centred on the compact object (x towards the companion, y the companion's motion). The gas leaves L1 at
    /// <paramref name="launchSpeed"/> (its sound speed over a Omega; ~0.01-0.05).
    /// </summary>
    public static List<Double3> Path(double q, double stopRadius, int points, double launchSpeed = 0.02)
    {
        q = Math.Max(q, 1e-9);
        double m1 = 1.0 / (1.0 + q), m2 = q / (1.0 + q);
        double x1 = -m2, x2 = m1;
        double xl1 = x1 + L1FromCompact(q);
        // Start just inside L1, falling towards the compact object.
        var s = new double[] { xl1 - 1e-4, 0.0, -launchSpeed, 0.0 };
        var track = new List<Double3> { new Double3(s[0] - x1, s[1], 0.0) };
        double dt = 2e-4, rPrev = double.MaxValue;
        for (int i = 0; i < 200000; i++)
        {
            Rk4(s, dt, m1, m2, x1, x2);
            double r = Math.Sqrt((s[0] - x1) * (s[0] - x1) + s[1] * s[1]);
            if (i % 10 == 0) track.Add(new Double3(s[0] - x1, s[1], 0.0));
            if (r <= stopRadius) { track.Add(new Double3(s[0] - x1, s[1], 0.0)); break; }
            if (r > rPrev && r < xl1 - x1 - 1e-3) break;   // past its closest approach: it would swing back out
            rPrev = r;
        }
        return Resample(track, Math.Max(points, 2));
    }

    private static void Rk4(double[] s, double h, double m1, double m2, double x1, double x2)
    {
        double[] k1 = Deriv(s, m1, m2, x1, x2);
        double[] k2 = Deriv(Add(s, k1, h / 2), m1, m2, x1, x2);
        double[] k3 = Deriv(Add(s, k2, h / 2), m1, m2, x1, x2);
        double[] k4 = Deriv(Add(s, k3, h), m1, m2, x1, x2);
        for (int i = 0; i < 4; i++) s[i] += h / 6.0 * (k1[i] + 2.0 * k2[i] + 2.0 * k3[i] + k4[i]);
    }

    private static double[] Add(double[] s, double[] k, double h) => new[] { s[0] + h * k[0], s[1] + h * k[1], s[2] + h * k[2], s[3] + h * k[3] };

    // Rotating frame, Omega = 1 along +z: Coriolis -2 Omega x v and centrifugal Omega^2 r with gravity.
    private static double[] Deriv(double[] s, double m1, double m2, double x1, double x2)
    {
        double x = s[0], y = s[1], vx = s[2], vy = s[3];
        double r1 = Math.Sqrt((x - x1) * (x - x1) + y * y), r2 = Math.Sqrt((x - x2) * (x - x2) + y * y);
        double r13 = r1 * r1 * r1, r23 = r2 * r2 * r2;
        double ax = 2.0 * vy + x - m1 * (x - x1) / r13 - m2 * (x - x2) / r23;
        double ay = -2.0 * vx + y - m1 * y / r13 - m2 * y / r23;
        return new[] { vx, vy, ax, ay };
    }

    /// <summary>
    /// The stream winding in after it strikes (<see cref="SpiralOf"/>): on round the compact object the way it was going for
    /// <see cref="Turns"/> turns, closing in from <see cref="R0"/> (where it struck) to <see cref="R1"/> (fractions of a), the
    /// radius R1 + (R0 - R1)(1 - t)^<see cref="Curve"/> a fraction t of the way round (1: steadily; more: fast at first,
    /// settling), from angle <see cref="A0"/> (radians from x, towards y), going round <see cref="Sense"/> (+1 towards +y).
    /// Wound in far, it is the stream building a disk: its windings are the disk's rings.
    /// </summary>
    public readonly struct StreamSpiral
    {
        public readonly double R0, R1, A0, Sense, Turns, Curve;

        public StreamSpiral(double r0, double r1, double a0, double sense, double turns, double curve = 1.0)
        {
            R0 = r0; R1 = r1; A0 = a0; Sense = sense; Turns = turns; Curve = curve;
        }

        /// <summary>The point a fraction <paramref name="t"/> (0..1) of the way round.</summary>
        public Double3 At(double t)
        {
            double angle = A0 + Sense * 2.0 * Math.PI * Turns * t, r = R1 + (R0 - R1) * Math.Pow(Math.Max(1.0 - t, 0.0), Curve);
            return new Double3(r * Math.Cos(angle), r * Math.Sin(angle), 0.0);
        }

        /// <summary>Which way it is going a fraction <paramref name="t"/> of the way round (a unit vector).</summary>
        public Double3 DirectionAt(double t)
        {
            const double dt = 1e-5;
            return (At(Math.Min(t + dt, 1.0)) - At(Math.Max(t + dt - 2.0 * dt, 0.0))).Normalized();
        }

        /// <summary>The rest of it, from a fraction <paramref name="t"/> of the way round on (the same curve).</summary>
        public StreamSpiral From(double t)
        {
            t = Math.Min(Math.Max(t, 0.0), 1.0);
            double r = R1 + (R0 - R1) * Math.Pow(1.0 - t, Curve);
            return new StreamSpiral(r, R1, A0 + Sense * 2.0 * Math.PI * Turns * t, Sense, Turns * (1.0 - t), Curve);
        }
    }

    /// <summary>The most a winding's closing in is curved (<see cref="StreamSpiral.Curve"/>).</summary>
    public const double MaxSpiralCurve = 8.0;

    /// <summary>
    /// The spiral the stream <paramref name="fall"/> (<see cref="Path"/>) winds on in from where it strikes: for
    /// <paramref name="turns"/> turns, in to <paramref name="spiralTo"/> of that radius, closing in as <paramref name="curve"/>
    /// says (<see cref="StreamSpiral.Curve"/>), or for 0 at first as fast as the gas was falling, so the stream runs on into
    /// its winding without a kink.
    /// </summary>
    public static StreamSpiral SpiralOf(List<Double3> fall, double turns, double spiralTo, double curve = 1.0)
    {
        Double3 end = fall[fall.Count - 1], before = fall[fall.Count - 2];
        // The way it goes round: the sign of its angular momentum about the compact object at the end.
        double sense = end.X * (end.Y - before.Y) - end.Y * (end.X - before.X) >= 0.0 ? 1.0 : -1.0;
        double r0 = end.Length, r1 = r0 * spiralTo, total = 2.0 * Math.PI * Math.Max(turns, 0.0);
        if (curve <= 0.0)
        {
            double turned = sense * (Math.Atan2(end.Y, end.X) - Math.Atan2(before.Y, before.X));
            turned -= 2.0 * Math.PI * Math.Round(turned / (2.0 * Math.PI));
            double slope = (r0 - before.Length) / Math.Max(turned, 1e-9);   // dr per radian round as it strikes (falling: < 0)
            curve = r0 - r1 > 1e-12 && total > 0.0 ? Math.Min(Math.Max(-slope * total / (r0 - r1), 1.0), MaxSpiralCurve) : 1.0;
        }
        return new StreamSpiral(r0, r1, Math.Atan2(end.Y, end.X), sense, Math.Max(turns, 0.0), curve);
    }

    /// <summary>How far round the winding a stream is eased into it (<see cref="EaseInto"/>), radians.</summary>
    public const double EaseAngle = Math.PI / 4.0;

    /// <summary>
    /// The falling stream eased into what it winds on as, so there is no kink where it strikes: <paramref name="fall"/>
    /// (<see cref="Path"/>) kept to its first <paramref name="keep"/> points, then a smooth curve of
    /// <paramref name="blendPoints"/> more to <paramref name="to"/>, arriving going <paramref name="toward"/> (a cubic
    /// Hermite curve, leaving the way the gas was falling and arriving the way the winding goes).
    /// </summary>
    public static List<Double3> EaseInto(List<Double3> fall, int keep, Double3 to, Double3 toward, int blendPoints)
    {
        keep = Math.Min(Math.Max(keep, 2), fall.Count);
        var path = fall.GetRange(0, keep);
        Double3 from = path[keep - 1], leaving = (path[keep - 1] - path[keep - 2]).Normalized();
        double span = (to - from).Length;
        Double3 t0 = leaving * span, t1 = toward.Normalized() * span;
        for (int k = 1; k <= blendPoints; k++)
        {
            double s = (double)k / blendPoints, s2 = s * s, s3 = s2 * s;
            path.Add(from * (2.0 * s3 - 3.0 * s2 + 1.0) + t0 * (s3 - 2.0 * s2 + s) + to * (-2.0 * s3 + 3.0 * s2) + t1 * (s3 - s2));
        }
        return path;
    }

    /// <summary>
    /// The stream (<paramref name="points"/> points to where it strikes the disk), then, for <paramref name="turns"/> &gt; 0,
    /// as many again winding on round the compact object the way it was going for that many turns (<see cref="SpiralOf"/>),
    /// in to <paramref name="spiralTo"/> of that radius (0.85: the disk's outer ring). <paramref name="impact"/>: the index
    /// where it strikes.
    /// </summary>
    public static List<Double3> PathWithTurns(double q, double stopRadius, int points, double turns, out int impact, double launchSpeed = 0.02, double spiralTo = 0.85)
    {
        List<Double3> path = Path(q, stopRadius, points, launchSpeed);
        impact = path.Count - 1;
        if (turns <= 0.0) return path;
        StreamSpiral spiral = SpiralOf(path, turns, spiralTo);
        for (int k = 1; k <= points; k++) path.Add(spiral.At((double)k / points));
        return path;
    }

    /// <summary>The track resampled to <paramref name="n"/> points evenly spaced along its length.</summary>
    internal static List<Double3> Resample(List<Double3> track, int n)
    {
        var cumulative = new double[track.Count];
        for (int i = 1; i < track.Count; i++) cumulative[i] = cumulative[i - 1] + (track[i] - track[i - 1]).Length;
        double total = cumulative[track.Count - 1];
        var result = new List<Double3>(n);
        int j = 0;
        for (int k = 0; k < n; k++)
        {
            double want = total * k / (n - 1);
            while (j < track.Count - 2 && cumulative[j + 1] < want) j++;
            double span = cumulative[j + 1] - cumulative[j];
            double t = span > 0.0 ? (want - cumulative[j]) / span : 0.0;
            result.Add(Double3.Lerp(track[j], track[Math.Min(j + 1, track.Count - 1)], Math.Min(Math.Max(t, 0.0), 1.0)));
        }
        return result;
    }
}
