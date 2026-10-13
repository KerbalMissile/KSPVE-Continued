using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Water;

/// <summary>
/// How the sea's spectrum is cut into cascades: each is a periodic square holding a band of wave numbers, every one
/// <see cref="Ratio"/> times finer than the last, so a few small textures hold waves from the swell (a kilometre long) to
/// ripples a few centimetres long. The squares' sides divide the largest one's, so a position reduced modulo the largest side
/// is exact for all.
/// </summary>
public sealed class OceanLayout
{
    /// <summary>Each cascade is this many times smaller than the one before.</summary>
    public const int Ratio = 8;

    public int N;
    public int Cascades;
    /// <summary>Side (m) of each square.</summary>
    public double[] Lengths;
    /// <summary>The wave numbers (rad/m) each cascade holds.</summary>
    public double[] KMin, KMax;

    public static OceanLayout For(OceanQuality quality)
    {
        int n, cascades;
        double largest;
        switch (quality)
        {
            case OceanQuality.Low: n = 128; cascades = 2; largest = 1000.0; break;
            case OceanQuality.Medium: n = 256; cascades = 3; largest = 2000.0; break;
            case OceanQuality.Ultra: n = 512; cascades = 4; largest = 2000.0; break;
            default: n = 256; cascades = 4; largest = 2000.0; break;
        }
        var l = new OceanLayout { N = n, Cascades = cascades, Lengths = new double[cascades], KMin = new double[cascades], KMax = new double[cascades] };
        for (int c = 0; c < cascades; c++)
        {
            l.Lengths[c] = largest / Math.Pow(Ratio, c);
            // A wavelength is resolved down to four texels (the Nyquist limit is two); the last cascade goes on to 2.5.
            l.KMax[c] = (c + 1 < cascades ? 0.5 : 0.8) * Math.PI * n / l.Lengths[c];
            l.KMin[c] = c == 0 ? 0.5 * 2.0 * Math.PI / largest : l.KMax[c - 1];
        }
        return l;
    }

    /// <summary>The shortest wavelength (m) each cascade holds.</summary>
    public double MinWavelength(int cascade) => 2.0 * Math.PI / KMax[cascade];

    /// <summary>
    /// Builds every cascade's initial spectrum for a sea, four floats a texel, cascade after cascade (what the compute shader's
    /// h0 texture array holds). <paramref name="windAngle"/> is the direction the wind blows, radians from the first wave axis.
    /// </summary>
    public float[] BuildSpectra(double wind, double windAngle, double fetch, double gravity, ulong seed)
    {
        var data = new float[Cascades * N * N * 4];
        for (int c = 0; c < Cascades; c++)
        {
            var cascade = new OceanCascade(N, Lengths[c], KMin[c], KMax[c], wind, windAngle, fetch, gravity, double.PositiveInfinity, seed + (ulong)c * 7919UL);
            cascade.CopyH0(data, c * N * N * 4);
        }
        return data;
    }

    /// <summary>
    /// The Jacobian below which each cascade's whitecaps form, so that together they cover Monahan's share of the sea: each
    /// takes a share of the coverage in proportion to the slope variance (breaking is steepness) of its band.
    /// </summary>
    public double[] FoamThresholds(double wind, double fetch, double gravity, double lambda)
    {
        var th = new double[Cascades];
        var variance = new double[Cascades];
        double total = 0.0;
        for (int c = 0; c < Cascades; c++)
        {
            variance[c] = OceanSpectrum.SlopeVariance(KMin[c], KMax[c], Math.Max(wind, 0.1), fetch, double.PositiveInfinity, gravity, 120);
            total += variance[c];
        }
        double coverage = Whitecaps.Coverage(wind);
        for (int c = 0; c < Cascades; c++)
            th[c] = Whitecaps.JacobianThreshold(Math.Max(coverage * variance[c] / Math.Max(total, 1e-12), 1e-9), lambda, Math.Sqrt(variance[c]));
        return th;
    }

    /// <summary>
    /// A table of the slope variance per axis the cascades do not hold, by log2 of a pixel's footprint on the sea (64 entries,
    /// 0.01 m to about 650 m): Cox and Munk's total (0.003 + 0.00512 U) less the part of the spectrum a footprint that size
    /// still resolves. The glitter uses it as the roughness of waves too small to see, close up (capillary ripples) and far away.
    /// </summary>
    public float[] RoughnessTable(double wind, double fetch, double gravity, double waveScale)
    {
        var table = new float[64];
        double total = CoxMunk.SlopeVariance(wind);
        for (int i = 0; i < table.Length; i++)
        {
            double footprint = 0.01 * Math.Pow(2.0, 16.0 * i / (table.Length - 1));
            double k = Math.Min(Math.PI / footprint, KMax[Cascades - 1]);
            double resolved = k > KMin[0]
                ? OceanSpectrum.SlopeVariance(KMin[0], k, Math.Max(wind, 0.1), fetch, double.PositiveInfinity, gravity, 120) * waveScale * waveScale
                : 0.0;
            table[i] = (float)Math.Max(total - resolved, 0.0) * 0.5f;
        }
        return table;
    }
}

/// <summary>The sky and sunlight colours over the sea, from the sun's height and the atmosphere (single-scattering approximations).</summary>
public static class SkyModel
{
    /// <summary>
    /// Rayleigh optical depth at the zenith through a one-atmosphere (Earth-like) air column at the red, green and blue
    /// wavelengths (610, 550, 465 nm): 0.0972 (550 / lambda)^4.05, which Bodhaine et al. (1999) give as 0.097 at 550 nm.
    /// </summary>
    public static readonly Double3 ZenithOpticalDepth = new Double3(0.0636, 0.0972, 0.1927);

    /// <summary>
    /// The sun's light after the air: exp(-tau m) per channel, tau the optical depth scaled by the atmosphere's density
    /// relative to Earth's sea level, m the relative airmass (Kasten and Young 1989). Straight overhead through Earth's air
    /// it is (0.94, 0.91, 0.82); at 5 degrees it is red.
    /// </summary>
    public static Double3 SunTransmittance(double sunElevationDegrees, double density)
    {
        if (density <= 0.0) return new Double3(1, 1, 1);
        double zenith = 90.0 - Math.Max(sunElevationDegrees, -5.0);
        double airmass = Atmospherics.Airmass.KastenYoung(Math.Min(zenith, 95.0));
        return new Double3(Math.Exp(-ZenithOpticalDepth.X * density * airmass), Math.Exp(-ZenithOpticalDepth.Y * density * airmass), Math.Exp(-ZenithOpticalDepth.Z * density * airmass));
    }

    /// <summary>
    /// The sky's radiance relative to the sun's E / pi (before the air takes its share): blue overhead, whiter at the
    /// horizon, darker as the sun sets (twilight lasts until the sun is 6 degrees below the horizon). Zero with no air.
    /// </summary>
    public static void Colors(double sunElevationDegrees, double density, out Double3 zenith, out Double3 horizon, out Double3 ambient)
    {
        if (density <= 0.0) { zenith = horizon = ambient = Double3.Zero; return; }
        double day = Smooth(-6.0, 12.0, sunElevationDegrees);
        double d = Math.Min(density, 1.5) / 1.0;
        zenith = new Double3(0.060, 0.110, 0.230) * (day * d);
        horizon = new Double3(0.30, 0.38, 0.50) * (day * d);
        ambient = new Double3(0.11, 0.16, 0.25) * (day * d);
    }

    private static double Smooth(double a, double b, double x)
    {
        double t = Math.Min(Math.Max((x - a) / (b - a), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
