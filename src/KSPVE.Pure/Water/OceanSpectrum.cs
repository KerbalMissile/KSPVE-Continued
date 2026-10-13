using System;

namespace KSPVE.Pure.Water;

/// <summary>
/// The statistics of a wind sea: the JONSWAP frequency spectrum (Hasselmann et al. 1973) for the wind speed at 10 m and
/// the fetch, spread over directions as Mitsuyasu et al. (1975) give, with the dispersion
/// relation of waves on water of any depth (gravity and capillarity). The ocean's height field (Tessendorf 2001) is a
/// random sample of this spectrum.
/// </summary>
public static class OceanSpectrum
{
    /// <summary>Standard gravity (m/s2), for Earth-like defaults in tests.</summary>
    public const double EarthGravity = 9.80665;

    /// <summary>Surface tension over density of water (m3/s2): 0.0728 N/m over 1000 kg/m3.</summary>
    public const double WaterSurfaceTension = 7.28e-5;

    /// <summary>JONSWAP's peak enhancement for a sea still growing with fetch (the mean of Hasselmann's data, 3.3).</summary>
    public const double PeakEnhancement = 3.3;

    /// <summary>
    /// The fetch (m) beyond which the sea is fully developed for a wind speed: JONSWAP's peak frequency
    /// 22 (g^2 / (U F))^(1/3) meets Pierson and Moskowitz's 0.877 g / U there.
    /// </summary>
    public static double FullyDevelopedFetch(double wind, double gravity)
    {
        double r = 22.0 / 0.877;
        return r * r * r * wind * wind / gravity;
    }

    /// <summary>The peak angular frequency (rad/s) of the sea: Hasselmann's 22 (g^2 / (U F))^(1/3), at most the fully developed sea's.</summary>
    public static double PeakFrequency(double wind, double fetch, double gravity)
    {
        wind = Math.Max(wind, 0.1);
        fetch = Math.Min(Math.Max(fetch, 100.0), FullyDevelopedFetch(wind, gravity));
        return 22.0 * Math.Pow(gravity * gravity / (wind * fetch), 1.0 / 3.0);
    }

    /// <summary>Phillips' constant alpha = 0.076 (U^2 / (F g))^0.22 (Hasselmann et al. 1973), 0.0081 once fully developed.</summary>
    public static double Alpha(double wind, double fetch, double gravity)
    {
        wind = Math.Max(wind, 0.1);
        double full = FullyDevelopedFetch(wind, gravity);
        return fetch >= full ? 0.0081 : 0.076 * Math.Pow(wind * wind / (Math.Max(fetch, 100.0) * gravity), 0.22);
    }

    /// <summary>
    /// The JONSWAP spectrum S(omega) in m2 s/rad: alpha g^2 omega^-5 exp(-1.25 (wp/w)^4) gamma^r, with
    /// r = exp(-(w - wp)^2 / (2 sigma^2 wp^2)) and sigma 0.07 below the peak, 0.09 above. At a fully developed sea it
    /// is Pierson and Moskowitz's.
    /// </summary>
    public static double Jonswap(double omega, double wind, double fetch, double gravity)
    {
        if (omega <= 1e-6) return 0.0;
        double wp = PeakFrequency(wind, fetch, gravity);
        double alpha = Alpha(wind, fetch, gravity);
        double sigma = omega <= wp ? 0.07 : 0.09;
        double d = (omega - wp) / (sigma * wp);
        double gamma = fetch >= FullyDevelopedFetch(wind, gravity) ? 1.0 : PeakEnhancement;
        double r = Math.Exp(-0.5 * d * d);
        return alpha * gravity * gravity * Math.Pow(omega, -5.0) * Math.Exp(-1.25 * Math.Pow(wp / omega, 4.0)) * Math.Pow(gamma, r);
    }

    /// <summary>
    /// The spreading exponent s of cos^(2s)(theta / 2) (Mitsuyasu et al. 1975, in Goda's form): s_max (w / wp)^5 below the
    /// peak and s_max (w / wp)^-2.5 above it, s_max = 11.5 (U wp / g)^-2.5. Energy near the peak travels with the wind;
    /// shorter and longer waves spread wider.
    /// </summary>
    public static double SpreadExponent(double omega, double peak, double wind, double gravity)
    {
        double ratio = omega / peak;
        double sMax = 11.5 * Math.Pow(Math.Max(wind, 0.1) * peak / gravity, -2.5);
        return sMax * Math.Pow(ratio, omega <= peak ? 5.0 : -2.5);
    }

    /// <summary>
    /// The directional distribution D(theta), theta radians from the wind: cos^(2s)(theta / 2) normalised so its integral over
    /// the circle is 1.
    /// </summary>
    public static double Directional(double theta, double s)
    {
        s = Math.Max(s, 0.5);
        double norm = DirectionalNorm(s);
        double c = Math.Cos(0.5 * theta);
        return c <= 0.0 ? 0.0 : Math.Pow(c, 2.0 * s) / norm;
    }

    /// <summary>The integral of cos^(2s)(theta / 2) over -pi..pi: 2 sqrt(pi) Gamma(s + 1/2) / Gamma(s + 1).</summary>
    public static double DirectionalNorm(double s) => 2.0 * Math.Sqrt(Math.PI) * Math.Exp(LogGamma(s + 0.5) - LogGamma(s + 1.0));

    /// <summary>The wave number k (rad/m) of a wave of angular frequency omega on water of a depth (Newton on omega^2 = (g k + T k^3) tanh(k d)).</summary>
    public static double WaveNumber(double omega, double depth, double gravity, double tension = WaterSurfaceTension)
    {
        double k = omega * omega / gravity;
        if (k <= 0.0) return 0.0;
        bool deep = double.IsInfinity(depth);
        for (int i = 0; i < 40; i++)
        {
            double th = deep ? 1.0 : Math.Tanh(k * depth);
            double sech2 = deep ? 0.0 : depth * (1.0 - th * th);
            double f = (gravity * k + tension * k * k * k) * th - omega * omega;
            double df = (gravity + 3.0 * tension * k * k) * th + (gravity * k + tension * k * k * k) * sech2;
            double next = k - f / Math.Max(df, 1e-12);
            if (next <= 0.0) next = 0.5 * k;
            if (Math.Abs(next - k) < 1e-10 * k) { k = next; break; }
            k = next;
        }
        return k;
    }

    /// <summary>The dispersion relation: omega(k) = sqrt((g k + T k^3) tanh(k d)); <paramref name="depth"/> infinite for deep water.</summary>
    public static double Dispersion(double k, double depth, double gravity, double tension = WaterSurfaceTension)
    {
        double th = double.IsInfinity(depth) ? 1.0 : Math.Tanh(k * depth);
        return Math.Sqrt((gravity * k + tension * k * k * k) * th);
    }

    /// <summary>The group velocity d omega / d k (m/s).</summary>
    public static double GroupVelocity(double k, double depth, double gravity, double tension = WaterSurfaceTension)
    {
        double h = Math.Max(k * 1e-4, 1e-9);
        return (Dispersion(k + h, depth, gravity, tension) - Dispersion(Math.Max(k - h, 0.0), depth, gravity, tension)) / (k + h - Math.Max(k - h, 0.0));
    }

    /// <summary>
    /// The spectrum per wave-number area: E(kx, kz) = S(omega) D(theta) (d omega / d k) / k, in m4, so that the variance of
    /// the surface height is the integral of E over the plane. <paramref name="theta"/> is the wave's direction from the wind.
    /// </summary>
    public static double Spectrum2D(double k, double theta, double wind, double fetch, double depth, double gravity)
    {
        if (k <= 1e-9) return 0.0;
        double w = Dispersion(k, depth, gravity);
        double wp = PeakFrequency(wind, fetch, gravity);
        double dwdk = GroupVelocity(k, depth, gravity);
        return Jonswap(w, wind, fetch, gravity) * Directional(theta, SpreadExponent(w, wp, wind, gravity)) * dwdk / k;
    }

    /// <summary>The variance of the surface height (m2) between two wave numbers: the integral of E over the annulus.</summary>
    public static double HeightVariance(double kMin, double kMax, double wind, double fetch, double depth, double gravity, int steps = 400)
    {
        return Integrate(kMin, kMax, steps, k => AngleIntegral(k, wind, fetch, depth, gravity, 1.0));
    }

    /// <summary>The mean-square slope (both axes) of waves between two wave numbers: the integral of k^2 E over the annulus.</summary>
    public static double SlopeVariance(double kMin, double kMax, double wind, double fetch, double depth, double gravity, int steps = 400)
    {
        return Integrate(kMin, kMax, steps, k => AngleIntegral(k, wind, fetch, depth, gravity, k * k));
    }

    private static double AngleIntegral(double k, double wind, double fetch, double depth, double gravity, double weight)
    {
        // The integral of E(k, theta) k d theta; D integrates to one, so it is S(omega) (d omega / d k) weight.
        double w = Dispersion(k, depth, gravity);
        return Jonswap(w, wind, fetch, gravity) * GroupVelocity(k, depth, gravity) * weight;
    }

    /// <summary>Integrates over wave numbers on a logarithmic grid (the spectrum spans decades).</summary>
    private static double Integrate(double a, double b, int steps, Func<double, double> f)
    {
        double la = Math.Log(a), lb = Math.Log(b), h = (lb - la) / steps, sum = 0.0;
        for (int i = 0; i < steps; i++)
        {
            double k = Math.Exp(la + (i + 0.5) * h);
            sum += f(k) * k * h;
        }
        return sum;
    }

    /// <summary>ln Gamma(x) (Lanczos approximation, g = 7).</summary>
    public static double LogGamma(double x)
    {
        double[] c = { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
        if (x < 0.5) return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1.0 - x);
        x -= 1.0;
        double a = c[0], t = x + 7.5;
        for (int i = 1; i < 9; i++) a += c[i] / (x + i);
        return 0.5 * Math.Log(2.0 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }
}
