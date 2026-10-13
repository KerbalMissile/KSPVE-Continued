using System;
using System.Collections.Generic;
using KSPVE.Pure.ColorScience;

namespace KSPVE.Pure.Aurora;

/// <summary>The kinds of glowing layer the renderer looks up in its table; each has its own spectrum and height profile.</summary>
public enum EmissionRow
{
    /// <summary>Discrete arcs: electrons of a few keV, green 557.7 over a blue-violet N2+ border, pink fringe low down.</summary>
    Arc = 0,
    /// <summary>Energetic arcs (breakup, storms): 12 keV electrons reach lower, giving blue-violet and a pink lower fringe.</summary>
    ArcHard,
    /// <summary>The red tops of arcs: soft electrons of about 150 eV exciting 630.0 nm above 200 km.</summary>
    SoftArc,
    /// <summary>Diffuse and pulsating aurora: harder, broader electrons, mostly 557.7 and N2+ from lower down.</summary>
    Diffuse,
    /// <summary>Proton aurora: hydrogen H-alpha (656.3) and H-beta, spread wide because protons are neutralised and scatter.</summary>
    Proton,
    /// <summary>STEVE: a broad mauve continuum high up (130-270 km at Earth).</summary>
    Steve,
    /// <summary>SAR arcs: 630.0 nm only, in the F region (400 km at Earth).</summary>
    Sar,
    /// <summary>Airglow: oxygen 557.7 nm layer.</summary>
    GlowGreen,
    /// <summary>Airglow: sodium D 589 nm layer.</summary>
    GlowSodium,
    /// <summary>Airglow: hydroxyl Meinel bands (red, 600-720 nm).</summary>
    GlowHydroxyl,
    /// <summary>Airglow: oxygen 630.0 nm layer high up.</summary>
    GlowRed,
    /// <summary>Global aurora on a body with no field: particles falling over its whole night side (Mars, Venus).</summary>
    Global,
    /// <summary>A gas giant's main oval and moon footprints: electrons of tens of keV in an H2 atmosphere.</summary>
    Giant,
    Count
}

/// <summary>One spectral line (or a band stood for by a few lines) of a row.</summary>
public sealed class EmissionLine
{
    public EmissionLine(double wavelengthNm, double ratio, double quenchRate = 0.0, double lifetime = 0.0)
    {
        WavelengthNm = wavelengthNm; Ratio = ratio; QuenchRate = quenchRate; Lifetime = lifetime;
    }

    public double WavelengthNm { get; }
    /// <summary>Its column intensity relative to the row's reference line.</summary>
    public double Ratio { get; }
    /// <summary>Sum over the air's quenching molecules of mixing ratio times rate coefficient (cm^3/s); 0 = not quenched.</summary>
    public double QuenchRate { get; }
    /// <summary>The excited state's radiative lifetime (s); a long one is quenched by collisions at high pressure.</summary>
    public double Lifetime { get; }
}

/// <summary>A row's recipe.</summary>
public sealed class RowSpec
{
    public RowSpec(EmissionRow row, string reference, double energyKeV, double spread, EmissionLine[] lines)
    {
        Row = row; Reference = reference; EnergyKeV = energyKeV; Spread = spread; Lines = lines;
    }

    /// <summary>A layer fixed at a pressure level (airglow, SAR arcs, STEVE): the level is that of <paramref name="earthHeightKm"/> on Earth.</summary>
    public RowSpec(EmissionRow row, string reference, double earthHeightKm, double sigmaLnP, EmissionLine[] lines, bool shell)
    {
        Row = row; Reference = reference; ShellEarthHeightKm = earthHeightKm; Spread = sigmaLnP; Lines = lines; IsShell = shell;
    }

    public EmissionRow Row { get; }
    /// <summary>What the row's unit brightness is, e.g. "OI 557.7 nm".</summary>
    public string Reference { get; }
    /// <summary>Electron energy (keV) whose stopping depth sets the layer (precipitation rows).</summary>
    public double EnergyKeV { get; }
    /// <summary>Gaussian width in ln(pressure) the layer is smeared by (precipitation) or has (shell).</summary>
    public double Spread { get; }
    public bool IsShell { get; }
    public double ShellEarthHeightKm { get; }
    public EmissionLine[] Lines { get; }
}

/// <summary>
/// Emission heights and spectra of aurora and airglow by pressure level. The numbers are Earth's, measured; other
/// atmospheres use the same pressure levels (an electron stops where the column mass above it equals its range, so the same
/// pressure on any planet), and each composition changes the lines and what quenches them.
/// </summary>
public static class EmissionTables
{
    // Quenching: Sum of mixing ratio x rate coefficient (cm^3 s^-1), from Earth's lower thermosphere.
    // O(1S) by O2: ~5e-13 (Kopp et al.; 557.7 half quenched near 100 km, 0.03 Pa), at an O2 mixing ratio of 0.2.
    private const double QuenchOneS = 0.2 * 5.0e-13;
    // O(1D) by N2 (2.6e-11, 78%) and O2 (4e-11, 21%), with atomic oxygen taking over higher up: 2e-11 effective.
    private const double QuenchOneD = 2.0e-11;
    private const double LifetimeOneS = 0.76;    // s (557.7 nm)
    private const double LifetimeOneD = 107.0;   // s (630.0 and 636.4 nm)

    public static RowSpec[] For(Composition composition)
    {
        switch (composition)
        {
            case Composition.CO2: return Co2();
            case Composition.H2He: return H2He();
            case Composition.N2CH4: return Airglow(Composition.N2CH4, false);
            default: return N2O2();
        }
    }

    // O(1S) green and N2 first positive etc. Ratios are column ratios to the 557.7 nm line (Vallance Jones 1974, Rees and Luckey 1974).
    private static RowSpec[] N2O2() => Concat(new[]
    {
        // Arc: 3 keV electrons. 391.4/557.7 about 0.75, 427.8/391.4 about 0.3 (N2+ first negative band structure); N2 first positive
        // (570-700 nm) stood for by three bands, which are not quenched and so show as the pink fringe below the green.
        new RowSpec(EmissionRow.Arc, "OI 557.7 nm", 3.0, 0.0, new[]
        {
            new EmissionLine(557.7, 1.0, QuenchOneS, LifetimeOneS),
            new EmissionLine(391.4, 0.75), new EmissionLine(427.8, 0.23),
            new EmissionLine(580.0, 0.05), new EmissionLine(625.0, 0.10), new EmissionLine(670.0, 0.10),
        }),
        // Hard arcs: 12 keV, 557.7/391.4 falls to about 0.45 as the oxygen is quenched deeper.
        new RowSpec(EmissionRow.ArcHard, "OI 557.7 nm", 12.0, 0.0, new[]
        {
            new EmissionLine(557.7, 1.0, QuenchOneS, LifetimeOneS),
            new EmissionLine(391.4, 1.9), new EmissionLine(427.8, 0.57),
            new EmissionLine(580.0, 0.15), new EmissionLine(625.0, 0.30), new EmissionLine(670.0, 0.30),
        }),
        // Soft electrons (150 eV): 630.0 and 636.4 (branching 3:1) with a little 557.7 from the same population.
        new RowSpec(EmissionRow.SoftArc, "OI 630.0 nm", 0.15, 0.0, new[]
        {
            new EmissionLine(630.0, 1.0, QuenchOneD, LifetimeOneD),
            new EmissionLine(636.4, 0.31, QuenchOneD, LifetimeOneD),
            new EmissionLine(557.7, 0.18, QuenchOneS, LifetimeOneS),
        }),
        // Diffuse: 7 keV electrons, the green and N2+ of a weak broad aurora.
        new RowSpec(EmissionRow.Diffuse, "OI 557.7 nm", 7.0, 0.35, new[]
        {
            new EmissionLine(557.7, 1.0, QuenchOneS, LifetimeOneS),
            new EmissionLine(391.4, 1.2), new EmissionLine(427.8, 0.36),
            new EmissionLine(580.0, 0.08), new EmissionLine(625.0, 0.14), new EmissionLine(670.0, 0.14),
        }),
        // Proton aurora: H-alpha (Doppler broadened) and H-beta at 0.26, from ~30 keV protons (the depth of 3 keV electrons).
        new RowSpec(EmissionRow.Proton, "H-alpha 656.3 nm", 3.0, 0.9, new[]
        {
            new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.26),
        }),
        // STEVE: continuum with 427.8, 470-700 nm, strongest at the red end and in the blue; no 557.7 in the ribbon.
        new RowSpec(EmissionRow.Steve, "visible continuum", 200.0, 1.05, new[]
        {
            new EmissionLine(427.8, 0.55), new EmissionLine(470.0, 0.35), new EmissionLine(520.0, 0.30),
            new EmissionLine(575.0, 0.45), new EmissionLine(630.0, 0.85), new EmissionLine(665.0, 0.85), new EmissionLine(705.0, 0.65),
        }, true),
        // SAR arc: pure 630.0 nm from the F region, centred near 420 km.
        new RowSpec(EmissionRow.Sar, "OI 630.0 nm", 420.0, 0.70, new[]
        {
            new EmissionLine(630.0, 1.0), new EmissionLine(636.4, 0.31),
        }, true),
        // Global aurora: a soft, thin spectrum like a diffuse one, from 500 eV electrons.
        new RowSpec(EmissionRow.Global, "OI 557.7 nm", 0.5, 0.5, new[]
        {
            new EmissionLine(557.7, 1.0, QuenchOneS, LifetimeOneS),
            new EmissionLine(630.0, 0.5, QuenchOneD, LifetimeOneD),
            new EmissionLine(391.4, 0.4),
        }),
        // Giant: not used for an N2/O2 atmosphere (a copy of the arcs so a body with a strong field has something).
        new RowSpec(EmissionRow.Giant, "OI 557.7 nm", 3.0, 0.0, new[]
        {
            new EmissionLine(557.7, 1.0, QuenchOneS, LifetimeOneS), new EmissionLine(391.4, 0.75), new EmissionLine(427.8, 0.23),
        }),
    }, Airglow(Composition.N2O2, true));

    // Mars and Venus: oxygen from CO2, the CO2+ Fox-Duffendack-Barker bands in violet (Bertaux et al. 2005; Slanger et al. 2001).
    private static RowSpec[] Co2() => Concat(new[]
    {
        new RowSpec(EmissionRow.Arc, "OI 557.7 nm", 1.5, 0.0, new[]
        {
            new EmissionLine(557.7, 1.0, 0.95 * 5.0e-13, LifetimeOneS),
            new EmissionLine(630.0, 0.35, 0.95 * 1.0e-10, LifetimeOneD), new EmissionLine(636.4, 0.11, 0.95 * 1.0e-10, LifetimeOneD),
            new EmissionLine(385.0, 0.45), new EmissionLine(410.0, 0.40),
        }),
        new RowSpec(EmissionRow.ArcHard, "OI 557.7 nm", 8.0, 0.0, new[]
        {
            new EmissionLine(557.7, 1.0, 0.95 * 5.0e-13, LifetimeOneS),
            new EmissionLine(385.0, 1.0), new EmissionLine(410.0, 0.9), new EmissionLine(630.0, 0.1, 0.95 * 1.0e-10, LifetimeOneD),
        }),
        new RowSpec(EmissionRow.SoftArc, "OI 630.0 nm", 0.15, 0.0, new[]
        {
            new EmissionLine(630.0, 1.0, 0.95 * 1.0e-10, LifetimeOneD), new EmissionLine(636.4, 0.31, 0.95 * 1.0e-10, LifetimeOneD),
        }),
        new RowSpec(EmissionRow.Diffuse, "OI 557.7 nm", 5.0, 0.4, new[]
        {
            new EmissionLine(557.7, 1.0, 0.95 * 5.0e-13, LifetimeOneS), new EmissionLine(385.0, 0.5), new EmissionLine(410.0, 0.45),
        }),
        new RowSpec(EmissionRow.Proton, "H-alpha 656.3 nm", 3.0, 0.9, new[] { new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.26) }),
        new RowSpec(EmissionRow.Steve, "visible continuum", 200.0, 1.05, new[] { new EmissionLine(630.0, 1.0), new EmissionLine(430.0, 0.5) }, true),
        new RowSpec(EmissionRow.Sar, "OI 630.0 nm", 420.0, 0.70, new[] { new EmissionLine(630.0, 1.0), new EmissionLine(636.4, 0.31) }, true),
        // Global aurora of Mars and Venus: energetic electrons and ions fall on the whole night side; the green line is seen.
        new RowSpec(EmissionRow.Global, "OI 557.7 nm", 2.0, 0.55, new[]
        {
            new EmissionLine(557.7, 1.0, 0.95 * 5.0e-13, LifetimeOneS),
            new EmissionLine(385.0, 0.35), new EmissionLine(410.0, 0.30),
            new EmissionLine(630.0, 0.25, 0.95 * 1.0e-10, LifetimeOneD),
        }),
        new RowSpec(EmissionRow.Giant, "OI 557.7 nm", 1.5, 0.0, new[] { new EmissionLine(557.7, 1.0, 0.95 * 5.0e-13, LifetimeOneS), new EmissionLine(410.0, 0.5) }),
    }, Airglow(Composition.CO2, true));

    // Jupiter and the other giants: electrons of tens of keV in H2 excite H2 bands and H-alpha: pink and red, with some violet.
    private static RowSpec[] H2He() => Concat(new[]
    {
        new RowSpec(EmissionRow.Arc, "H-alpha 656.3 nm", 20.0, 0.3, new[]
        {
            new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.30), new EmissionLine(610.0, 0.55), new EmissionLine(585.0, 0.20), new EmissionLine(434.0, 0.12),
        }),
        new RowSpec(EmissionRow.ArcHard, "H-alpha 656.3 nm", 60.0, 0.3, new[]
        {
            new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.30), new EmissionLine(610.0, 0.55), new EmissionLine(434.0, 0.2),
        }),
        new RowSpec(EmissionRow.SoftArc, "H-alpha 656.3 nm", 4.0, 0.3, new[] { new EmissionLine(656.3, 1.0), new EmissionLine(610.0, 0.4) }),
        new RowSpec(EmissionRow.Diffuse, "H-alpha 656.3 nm", 30.0, 0.5, new[] { new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.3), new EmissionLine(610.0, 0.4) }),
        new RowSpec(EmissionRow.Proton, "H-alpha 656.3 nm", 20.0, 0.9, new[] { new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.26) }),
        new RowSpec(EmissionRow.Steve, "H-alpha 656.3 nm", 20.0, 0.9, new[] { new EmissionLine(656.3, 1.0) }),
        new RowSpec(EmissionRow.Sar, "H-alpha 656.3 nm", 20.0, 0.9, new[] { new EmissionLine(656.3, 1.0) }),
        new RowSpec(EmissionRow.Global, "H-alpha 656.3 nm", 20.0, 0.6, new[] { new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.3), new EmissionLine(610.0, 0.4) }),
        new RowSpec(EmissionRow.Giant, "H-alpha 656.3 nm", 30.0, 0.35, new[]
        {
            new EmissionLine(656.3, 1.0), new EmissionLine(486.1, 0.30), new EmissionLine(610.0, 0.50), new EmissionLine(585.0, 0.20), new EmissionLine(434.0, 0.15),
        }),
    }, Airglow(Composition.H2He, false));

    private static RowSpec[] Concat(RowSpec[] a, RowSpec[] b)
    {
        var all = new RowSpec[a.Length + b.Length];
        a.CopyTo(all, 0);
        b.CopyTo(all, a.Length);
        return all;
    }

    // Airglow, as measured on Earth (heights are pressure levels, so they land where they should on any atmosphere):
    // OI 557.7 at 96 km, ~250 R, FWHM 10 km; Na D at 92 km, ~30 R, FWHM 6 km; OH Meinel at 87 km, FWHM 8 km, ~200 R in the visible;
    // OI 630.0 at 270 km, ~15 R, FWHM ~80 km (Hines and Tarasick 1994; Khomich et al. 2008; Slanger et al.).
    private static RowSpec[] Airglow(Composition composition, bool withOxygen)
    {
        if (!withOxygen) return new RowSpec[0];
        double quench630 = composition == Composition.CO2 ? 0.95 * 1.0e-10 : QuenchOneD;
        return new[]
        {
            new RowSpec(EmissionRow.GlowGreen, "OI 557.7 nm", 96.0, 0.74, new[] { new EmissionLine(557.7, 1.0) }, true),
            new RowSpec(EmissionRow.GlowSodium, "Na D 589 nm", 92.0, 0.45, new[] { new EmissionLine(589.0, 1.0) }, true),
            new RowSpec(EmissionRow.GlowHydroxyl, "OH Meinel (visible)", 87.0, 0.60, new[]
            {
                new EmissionLine(600.0, 0.15), new EmissionLine(625.0, 0.25), new EmissionLine(650.0, 0.25), new EmissionLine(685.0, 0.20), new EmissionLine(715.0, 0.15),
            }, true),
            new RowSpec(EmissionRow.GlowRed, "OI 630.0 nm", 270.0, 0.75, new[] { new EmissionLine(630.0, 1.0), new EmissionLine(636.4, 0.31) }, true),
        };
    }
}

/// <summary>
/// A table of linear RGB emission per metre of path against height, for each <see cref="EmissionRow"/>, for a given
/// atmosphere, as the renderer's texture holds it. Entry (row, i) is the scene radiance added per metre travelled through the
/// layer at height i when the row's reference line has a column brightness of 1 kR, before any night-exposure gain.
/// </summary>
public sealed class EmissionLut
{
    public const int Rows = (int)EmissionRow.Count;

    public int Samples { get; }
    public double HeightLow { get; }
    public double HeightHigh { get; }
    /// <summary>[row, sample, channel] scene radiance per metre per kR.</summary>
    public float[,,] Rgb { get; }
    /// <summary>For each row, the height of its peak emission (m), or NaN.</summary>
    public double[] PeakHeight { get; }
    /// <summary>For each row, the lowest and highest height holding 5% of the peak (m).</summary>
    public double[] LowerEdge { get; }
    public double[] UpperEdge { get; }

    private EmissionLut(int samples, double low, double high)
    {
        Samples = samples; HeightLow = low; HeightHigh = high;
        Rgb = new float[Rows, samples, 3];
        PeakHeight = new double[Rows];
        LowerEdge = new double[Rows];
        UpperEdge = new double[Rows];
    }

    public double HeightOf(int sample) => HeightLow + (HeightHigh - HeightLow) * (sample + 0.5) / Samples;

    /// <summary>Builds the table for an atmosphere.</summary>
    public static EmissionLut Build(AtmosphereProfile atmosphere, Composition composition, int samples = 256, double lowPressure = 1.0e-6, double highPressure = 40.0)
    {
        double low = Math.Max(0.0, atmosphere.HeightOf(highPressure)), high = atmosphere.HeightOf(lowPressure);
        if (high < low + 1000.0) high = low + 1000.0;
        var lut = new EmissionLut(samples, low, high);
        AtmosphereProfile earth = AtmosphereProfile.Earth();
        double unit = AuroraPhotometry.SceneUnitLuminance;

        // The dense grid in x = ln p over which each line's profile is made and normalised.
        const int N = 1600;
        double xTop = Math.Log(3000.0), xBottom = Math.Log(1.0e-10);
        double dx = (xBottom - xTop) / (N - 1);
        var x = new double[N];
        var z = new double[N];
        var g = new double[N];
        for (int i = 0; i < N; i++)
        {
            x[i] = xTop + dx * i;
            z[i] = atmosphere.HeightOf(Math.Exp(x[i]));
            g[i] = atmosphere.GravityAt(Math.Max(z[i], 0.0));
        }

        foreach (RowSpec spec in EmissionTables.For(composition))
        {
            int row = (int)spec.Row;
            var xs = new double[samples];
            var hs = new double[samples];
            var scaleHeight = new double[samples];
            for (int s = 0; s < samples; s++)
            {
                hs[s] = lut.HeightOf(s);
                xs[s] = Math.Log(atmosphere.PressureAt(hs[s]));
                scaleHeight[s] = atmosphere.ScaleHeightAt(hs[s]);
            }

            // The pre-quench shape of the row along x.
            var shape = new double[N];
            if (spec.IsShell)
            {
                double x0 = Math.Log(earth.PressureAt(spec.ShellEarthHeightKm * 1000.0));
                for (int i = 0; i < N; i++) { double t = (x[i] - x0) / spec.Spread; shape[i] = Math.Exp(-0.5 * t * t); }
            }
            else
            {
                double range = EmissionModel.RangeColumnMass(spec.EnergyKeV);   // kg/m^2
                for (int i = 0; i < N; i++) shape[i] = EmissionModel.DepositionPerLnP(Math.Exp(x[i]) / (g[i] * range));
                if (spec.Spread > 0.0) shape = Smooth(shape, spec.Spread / Math.Abs(dx));
            }

            var xyzAt = new double[samples, 3];
            foreach (EmissionLine line in spec.Lines)
            {
                var profile = new double[N];
                double column = 0.0;
                for (int i = 0; i < N; i++)
                {
                    double p = Math.Exp(x[i]);
                    profile[i] = shape[i] * EmissionModel.QuenchEfficiency(line, p, atmosphere.TemperatureAt(p));
                    column += profile[i] * Math.Abs(dx);
                }
                if (!(column > 1e-30)) continue;
                // Scale so the line's column is Ratio x 1 kR (1000 R); per ln p that is the profile, per metre it is /H.
                double scale = line.Ratio * 1000.0 / column;
                CieColor.XyzOfLine(line.WavelengthNm, out double cx, out double cy, out double cz);
                double perRayleigh = AuroraPhotometry.EnergyRadiance(1.0, line.WavelengthNm) * AuroraPhotometry.MaximumLuminousEfficacy;
                for (int s = 0; s < samples; s++)
                {
                    double v = Interpolate(x, profile, xs[s]) * scale / scaleHeight[s];   // R per metre
                    double w = v * perRayleigh;
                    xyzAt[s, 0] += cx * w; xyzAt[s, 1] += cy * w; xyzAt[s, 2] += cz * w;
                }
            }

            double peak = 0.0;
            int peakAt = -1;
            var lum = new double[samples];
            for (int s = 0; s < samples; s++)
            {
                Rgb c = CieColor.XyzToLinearRgb(xyzAt[s, 0], xyzAt[s, 1], xyzAt[s, 2], true) / unit;
                lut.Rgb[row, s, 0] = (float)c.R; lut.Rgb[row, s, 1] = (float)c.G; lut.Rgb[row, s, 2] = (float)c.B;
                lum[s] = xyzAt[s, 1];
                if (lum[s] > peak) { peak = lum[s]; peakAt = s; }
            }
            lut.PeakHeight[row] = peakAt >= 0 ? hs[peakAt] : double.NaN;
            lut.LowerEdge[row] = double.NaN; lut.UpperEdge[row] = double.NaN;
            for (int s = 0; s < samples && peak > 0; s++)
            {
                if (lum[s] < 0.05 * peak) continue;
                if (double.IsNaN(lut.LowerEdge[row])) lut.LowerEdge[row] = hs[s];
                lut.UpperEdge[row] = hs[s];
            }
        }
        return lut;
    }

    private static double[] Smooth(double[] a, double sigmaSamples)
    {
        int radius = (int)Math.Ceiling(3.0 * sigmaSamples);
        var kernel = new double[2 * radius + 1];
        double sum = 0.0;
        for (int i = -radius; i <= radius; i++) { double v = Math.Exp(-0.5 * i * i / (sigmaSamples * sigmaSamples)); kernel[i + radius] = v; sum += v; }
        var result = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            double acc = 0.0;
            for (int k = -radius; k <= radius; k++)
            {
                int j = i + k;
                if (j >= 0 && j < a.Length) acc += a[j] * kernel[k + radius];
            }
            result[i] = acc / sum;
        }
        return result;
    }

    private static double Interpolate(double[] x, double[] y, double at)
    {
        // x descending (ln p falls with the index).
        int n = x.Length;
        if (at >= x[0] || at <= x[n - 1]) return 0.0;
        double f = (x[0] - at) / (x[0] - x[n - 1]) * (n - 1);
        int i = (int)f;
        if (i >= n - 1) i = n - 2;
        double t = f - i;
        return y[i] + (y[i + 1] - y[i]) * t;
    }
}

/// <summary>Particle precipitation: where electrons stop, and what collisions do to an excited state.</summary>
public static class EmissionModel
{
    /// <summary>
    /// The column mass (kg/m^2) an electron of <paramref name="energyKeV"/> penetrates (its range in air): R = 4.57e-5 E^1.75
    /// g/cm^2 (Rees 1989, from Grun 1957), valid from 0.2 to 50 keV and a fair guide beyond.
    /// </summary>
    public static double RangeColumnMass(double energyKeV) => 4.57e-5 * Math.Pow(energyKeV, 1.75) * 10.0;

    /// <summary>
    /// Energy deposition per unit ln(pressure) for a beam stopped over column mass m_R, against s = m / m_R: s Lambda(s) with
    /// the Rees (1989) energy dissipation function Lambda(s) = 0.1 + 6.2 s - 12.3 s^2 + 7.1 s^3 brought to zero at the end of the
    /// range, normalised to integrate to about 1 (the beam's energy lands in the atmosphere). The profile peaks at s of about 0.7.
    /// </summary>
    private static readonly double DissipationIntegral = ComputeDissipationIntegral();

    private static double ComputeDissipationIntegral()
    {
        // The integral of Lambda(s) (with its fall to zero) over s from 0 to 1, so that the profile per ln p integrates to one.
        double sum = 0.0;
        const int n = 4000;
        for (int i = 0; i < n; i++)
        {
            double s = (i + 0.5) / n;
            double lambda = 0.1 + 6.2 * s - 12.3 * s * s + 7.1 * s * s * s;
            sum += Math.Max(lambda, 0.0) * (1.0 - AuroraNoise.SmoothStep(0.72, 1.0, s)) / n;
        }
        return sum;
    }

    public static double DepositionPerLnP(double s)
    {
        if (s <= 0.0 || s >= 1.0) return 0.0;
        double lambda = 0.1 + 6.2 * s - 12.3 * s * s + 7.1 * s * s * s;
        // The cubic stops at s = 1 with a value of 1.1; the real function falls to zero there, so it is brought down smoothly.
        double fall = 1.0 - AuroraNoise.SmoothStep(0.72, 1.0, s);
        return Math.Max(lambda, 0.0) * s * fall / DissipationIntegral;
    }

    /// <summary>
    /// The fraction of a line's excited states that radiate rather than being quenched: 1 / (1 + k n tau), with n the number
    /// density of quenchers at the pressure and temperature (p / kT). Not quenched when <see cref="EmissionLine.QuenchRate"/> is 0.
    /// </summary>
    public static double QuenchEfficiency(EmissionLine line, double pressure, double temperature)
    {
        if (line.QuenchRate <= 0.0 || line.Lifetime <= 0.0) return 1.0;
        const double Boltzmann = 1.380649e-23;
        double n = pressure / (Boltzmann * Math.Max(temperature, 20.0));   // 1/m^3
        double k = line.QuenchRate * 1e-6;                                  // cm^3/s -> m^3/s
        return 1.0 / (1.0 + k * n * line.Lifetime);
    }
}
