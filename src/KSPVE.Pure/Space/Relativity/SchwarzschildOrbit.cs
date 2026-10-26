using System;

namespace KSPVE.Pure.Space.Relativity;

/// <summary>
/// A timelike geodesic of a Schwarzschild black hole in Darwin's parametrisation (Darwin 1959; Cutler, Kennefick and
/// Poisson 1994, Phys. Rev. D 50, 3816, eqs. 2.5-2.9): r = p m / (1 + e cos chi). The parameter chi grows steadily
/// along the orbit, through the turning points, so bound (e &lt; 1), parabolic (e = 1) and unbound (e &gt; 1) orbits
/// are all handled alike and the relativistic periapsis advance is exact. Coordinate time t (as kept by a distant
/// observer) and azimuth phi are tabulated against chi once, 4-point Gauss-Legendre per cell, on nodes that crowd
/// toward the far end (apoapsis, or the cut-off radius of an unbound orbit) where the integrands are steepest.
/// t, phi and chi are zero at periapsis and odd in chi. Requires p &gt; 6 + 2e (the orbit does not plunge).
/// </summary>
public sealed class SchwarzschildOrbit
{
    public const int Cells = 512;

    private static readonly double[] GaussX = { -0.8611363115940526, -0.3399810435848563, 0.3399810435848563, 0.8611363115940526 };
    private static readonly double[] GaussW = { 0.3478548451374538, 0.6521451548625461, 0.6521451548625461, 0.3478548451374538 };

    /// <summary>Coordinate time at each node, from periapsis.</summary>
    public double[] Time { get; } = new double[Cells + 1];
    /// <summary>Azimuth at each node, from periapsis.</summary>
    public double[] Azimuth { get; } = new double[Cells + 1];

    /// <summary>Semi-latus rectum in units of m.</summary>
    public double P { get; }
    public double Eccentricity { get; }
    /// <summary>Mass m = GM/c^2 in the length unit of the radii (0.5 when lengths are in horizon radii).</summary>
    public double Mass { get; }
    /// <summary>Last tabulated chi: pi for a bound orbit (apoapsis), else where r reaches the cut-off radius.</summary>
    public double ChiEnd { get; }

    public bool Bound => Eccentricity < 1.0;
    /// <summary>Coordinate time from periapsis to periapsis (infinite when unbound).</summary>
    public double RadialPeriod => Bound ? 2.0 * Time[Cells] : double.PositiveInfinity;
    /// <summary>Azimuth swept from periapsis to periapsis: 2 pi plus the periapsis advance (bound orbits only).</summary>
    public double AzimuthPerOrbit => Bound ? 2.0 * Azimuth[Cells] : double.NaN;

    /// <param name="p">Semi-latus rectum in units of m.</param>
    /// <param name="eccentricity">0 circular, below 1 bound, 1 parabolic, above 1 unbound.</param>
    /// <param name="m">GM/c^2 in the length unit used for radii and (divided by c) for time.</param>
    /// <param name="maxRadius">Where an unbound orbit's table ends (ignored for bound orbits).</param>
    public SchwarzschildOrbit(double p, double eccentricity, double m, double maxRadius)
    {
        if (eccentricity < 0.0) throw new ArgumentOutOfRangeException(nameof(eccentricity));
        if (p <= 6.0 + 2.0 * eccentricity) throw new ArgumentOutOfRangeException(nameof(p), "p must exceed 6 + 2e (the orbit would plunge).");
        P = p;
        Eccentricity = eccentricity;
        Mass = m;
        if (eccentricity < 1.0)
        {
            ChiEnd = Math.PI;
        }
        else
        {
            double periapsis = p * m / (1.0 + eccentricity);
            double r = Math.Max(maxRadius, periapsis * 1.0001);
            ChiEnd = Math.Acos(Math.Max(-1.0, Math.Min(1.0, (p * m / r - 1.0) / eccentricity)));
        }

        for (int k = 0; k < Cells; k++)
        {
            double a = Node(k), b = Node(k + 1);
            double half = 0.5 * (b - a), mid = 0.5 * (a + b);
            double dt = 0.0, dphi = 0.0;
            for (int g = 0; g < 4; g++)
            {
                double chi = mid + half * GaussX[g];
                dt += GaussW[g] * DtDchi(chi);
                dphi += GaussW[g] * DphiDchi(chi);
            }
            Time[k + 1] = Time[k] + dt * half;
            Azimuth[k + 1] = Azimuth[k] + dphi * half;
        }
    }

    /// <summary>chi at node k: crowded toward ChiEnd, chi_k = ChiEnd (1 - (1 - k/N)^2).</summary>
    public double Node(int k)
    {
        double u = 1.0 - (double)k / Cells;
        return ChiEnd * (1.0 - u * u);
    }

    public double Radius(double chi) => P * Mass / (1.0 + Eccentricity * Math.Cos(chi));

    public double DphiDchi(double chi) => Math.Sqrt(P / (P - 6.0 - 2.0 * Eccentricity * Math.Cos(chi)));

    public double DtDchi(double chi)
    {
        double c = Math.Cos(chi), e = Eccentricity, p = P;
        double q = 1.0 + e * c;
        return p * p * Mass / ((p - 2.0 - 2.0 * e * c) * q * q)
               * Math.Sqrt(((p - 2.0) * (p - 2.0) - 4.0 * e * e) / (p - 6.0 - 2.0 * e * c));
    }

    public double DrDchi(double chi)
    {
        double q = 1.0 + Eccentricity * Math.Cos(chi);
        return P * Mass * Eccentricity * Math.Sin(chi) / (q * q);
    }

    /// <summary>Specific energy E = -u_t (1 at rest at infinity): E^2 = ((p-2)^2 - 4e^2) / (p (p - 3 - e^2)).</summary>
    public static double Energy(double p, double e) => Math.Sqrt(((p - 2.0) * (p - 2.0) - 4.0 * e * e) / (p * (p - 3.0 - e * e)));

    /// <summary>Specific angular momentum L = u_phi: L = p m / sqrt(p - 3 - e^2).</summary>
    public static double AngularMomentum(double p, double e, double m) => p * m / Math.Sqrt(p - 3.0 - e * e);

    /// <summary>
    /// p and e of the geodesic with specific energy <paramref name="energy"/> and angular momentum
    /// <paramref name="angularMomentum"/>, from the turning points: with u = m/r, E^2 = (1 - 2u)(1 + l^2 u^2), l = L/m,
    /// is the cubic 2 l^2 u^3 - l^2 u^2 + 2u + (E^2 - 1) = 0. Its middle root is periapsis, its smallest apoapsis
    /// (negative when unbound), so p = 2 / (u_p + u_a) and e = (u_p - u_a) / (u_p + u_a). False when the orbit
    /// plunges into the hole (no periapsis outside the unstable circular orbit).
    /// </summary>
    public static bool TryElements(double energy, double angularMomentum, double m, out double p, out double e)
    {
        p = e = double.NaN;
        double l2 = angularMomentum * angularMomentum / (m * m);
        if (l2 <= 12.0) return false; // no turning point: everything below the ISCO's angular momentum plunges
        // Monic: u^3 + a2 u^2 + a1 u + a0 = 0.
        double a2 = -0.5, a1 = 1.0 / l2, a0 = (energy * energy - 1.0) / (2.0 * l2);
        double shift = -a2 / 3.0;
        double dp = a1 - a2 * a2 / 3.0;
        double dq = 2.0 * a2 * a2 * a2 / 27.0 - a2 * a1 / 3.0 + a0;
        if (dp >= 0.0) return false;
        double arg = 1.5 * dq / dp * Math.Sqrt(-3.0 / dp);
        if (arg < -1.0 || arg > 1.0) return false; // one real root: the orbit plunges
        double amp = 2.0 * Math.Sqrt(-dp / 3.0), theta = Math.Acos(arg) / 3.0;
        double r0 = amp * Math.Cos(theta) + shift;
        double r1 = amp * Math.Cos(theta - 2.0 * Math.PI / 3.0) + shift;
        double r2 = amp * Math.Cos(theta - 4.0 * Math.PI / 3.0) + shift;
        double lo = Math.Min(r0, Math.Min(r1, r2)), hi = Math.Max(r0, Math.Max(r1, r2));
        double mid = r0 + r1 + r2 - lo - hi;
        double sum = mid + lo;
        if (sum <= 0.0) return false;
        p = 2.0 / sum;
        e = (mid - lo) / sum;
        return p > 6.0 + 2.0 * e;
    }

    /// <summary>Coordinate time at chi (any chi for a bound orbit, which repeats; clamped to the table when unbound).</summary>
    public double TimeAt(double chi) => Lookup(Time, chi, Bound ? RadialPeriod : 0.0);

    /// <summary>Azimuth at chi, unwrapped (it keeps growing lap after lap).</summary>
    public double AzimuthAt(double chi) => Lookup(Azimuth, chi, Bound ? AzimuthPerOrbit : 0.0);

    private double Lookup(double[] table, double chi, double perOrbit)
    {
        double laps = 0.0;
        if (Bound)
        {
            laps = Math.Floor((chi + Math.PI) / (2.0 * Math.PI));
            chi -= 2.0 * Math.PI * laps;
        }
        double sign = chi < 0.0 ? -1.0 : 1.0;
        double a = Math.Min(Math.Abs(chi), ChiEnd);
        double u = 1.0 - Math.Sqrt(Math.Max(0.0, 1.0 - a / ChiEnd));
        double cell = u * Cells;
        int k = Math.Min((int)cell, Cells - 1);
        double x0 = Node(k), x1 = Node(k + 1);
        double f = x1 > x0 ? (a - x0) / (x1 - x0) : 0.0;
        return laps * perOrbit + sign * (table[k] + (table[k + 1] - table[k]) * f);
    }

    /// <summary>
    /// chi at coordinate time <paramref name="t"/> from periapsis. Unbound orbits stop at the end of the table; then
    /// <paramref name="beyond"/> is true.
    /// </summary>
    public double ChiAtTime(double t, out bool beyond) => ChiAtTime(Time, ChiEnd, Bound, t, out beyond);

    /// <summary>The same inversion on any table with this node layout.</summary>
    public static double ChiAtTime(double[] time, double chiEnd, bool bound, double t, out bool beyond)
    {
        beyond = false;
        double laps = 0.0;
        if (bound)
        {
            double period = 2.0 * time[Cells];
            laps = Math.Floor((t + 0.5 * period) / period);
            t -= laps * period;
        }
        double sign = t < 0.0 ? -1.0 : 1.0;
        double a = Math.Abs(t);
        if (a >= time[Cells])
        {
            beyond = !bound;
            return sign * chiEnd + 2.0 * Math.PI * laps;
        }
        int lo = 0, hi = Cells;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (time[mid] <= a) lo = mid; else hi = mid;
        }
        double t0 = time[lo], t1 = time[lo + 1];
        double f = t1 > t0 ? (a - t0) / (t1 - t0) : 0.0;
        double u0 = 1.0 - (double)lo / Cells, u1 = 1.0 - (double)(lo + 1) / Cells;
        double x0 = chiEnd * (1.0 - u0 * u0), x1 = chiEnd * (1.0 - u1 * u1);
        return sign * (x0 + (x1 - x0) * f) + 2.0 * Math.PI * laps;
    }

    /// <summary>chi in [0, ChiEnd] where the azimuth from periapsis reaches <paramref name="azimuth"/> (clamped).</summary>
    public double ChiAtAzimuth(double azimuth)
    {
        if (azimuth <= 0.0) return 0.0;
        if (azimuth >= Azimuth[Cells]) return ChiEnd;
        int lo = 0, hi = Cells;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (Azimuth[mid] <= azimuth) lo = mid; else hi = mid;
        }
        double f = (azimuth - Azimuth[lo]) / (Azimuth[lo + 1] - Azimuth[lo]);
        return Node(lo) + (Node(lo + 1) - Node(lo)) * f;
    }

    /// <summary>
    /// Velocity measured by a static observer at chi, as fractions of c: radial (positive outward) and azimuthal.
    /// dl = dr / sqrt(1 - 2m/r) and d tau = sqrt(1 - 2m/r) dt.
    /// </summary>
    public void StaticVelocity(double chi, out double radial, out double azimuthal)
    {
        double r = Radius(chi);
        double lapse = 1.0 - 2.0 * Mass / r;
        double dtdchi = DtDchi(chi);
        radial = DrDchi(chi) / dtdchi / lapse;
        azimuthal = r * DphiDchi(chi) / dtdchi / Math.Sqrt(lapse);
    }
}
