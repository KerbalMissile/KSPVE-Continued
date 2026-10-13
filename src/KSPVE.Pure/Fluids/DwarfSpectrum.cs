using System;
using KSPVE.Pure.ColorScience;

namespace KSPVE.Pure.Fluids;

/// <summary>
/// The visible light of a hot atmosphere: a blackbody at the deck's temperature seen through the gas above it. The
/// colour is worked out by integrating Planck's law against the CIE 1931 colour matching functions (the multi-lobe fits of
/// Wyman, Sloan and Shirley 2013) from 380 to 780 nm, with the gas's absorption in between: the wings of the sodium D lines
/// (589 nm) and of the potassium resonance lines (767 and 770 nm), which are pressure-broadened across hundreds of
/// nanometres in these atmospheres (Burrows, Marley and Sharp 2000; Allard et al. 2003), plus a grey continuum that sets the
/// limb darkening. That is what makes a hot L dwarf a deep orange-red and a T dwarf dull and very dark: the sodium wings take
/// out the yellow-green and the potassium wings the far red (where a cool blackbody would otherwise be brightest), so most of
/// the light in the eye's range is lost and what is left sits in the red window between them.
/// Everything is relative to the Sun's disc: luminance 1 is the visible radiance of a 5772 K blackbody, which the
/// star renderer shows at its disc radiance.
/// </summary>
public static class DwarfSpectrum
{
    public const double SolarTemperature = 5772.0;
    private const double FirstNm = 380.0, LastNm = 780.0, StepNm = 5.0;
    private const double NaD = 589.3, KI = 768.2;

    private static readonly int Samples = (int)Math.Round((LastNm - FirstNm) / StepNm) + 1;
    private static readonly double[] Xbar = new double[Samples], Ybar = new double[Samples], Zbar = new double[Samples];
    private static readonly double SolarY;

    static DwarfSpectrum()
    {
        for (int i = 0; i < Samples; i++)
        {
            double nm = FirstNm + i * StepNm;
            Xbar[i] = 1.056 * Lobe(nm, 599.8, 37.9, 31.0) + 0.362 * Lobe(nm, 442.0, 16.0, 26.7) - 0.065 * Lobe(nm, 501.1, 20.4, 26.2);
            Ybar[i] = 0.821 * Lobe(nm, 568.8, 46.9, 40.5) + 0.286 * Lobe(nm, 530.9, 16.3, 31.1);
            Zbar[i] = 1.217 * Lobe(nm, 437.0, 11.8, 36.0) + 0.681 * Lobe(nm, 459.0, 26.0, 13.8);
        }
        double y = 0.0;
        for (int i = 0; i < Samples; i++) y += ColorTemperature.PlanckSpectralRadiance((FirstNm + i * StepNm) * 1e-9, SolarTemperature) * Ybar[i];
        SolarY = y;
    }

    private static double Lobe(double nm, double mean, double sigmaLow, double sigmaHigh)
    {
        double t = (nm - mean) / (nm < mean ? sigmaLow : sigmaHigh);
        return Math.Exp(-0.5 * t * t);
    }

    /// <summary>
    /// The optical depth of the alkali wings and the continuum at a wavelength for a gas column of the given pressure (bar).
    /// A line's far wing falls as a power law of the distance from its centre with exponent 3/2 (the quasi-static limit), in
    /// units of a core width of 5 nm, cut off beyond 150 nm. <paramref name="alkali"/> scales the sodium and potassium (1 where they are atomic).
    /// </summary>
    public static double GasDepth(double nm, double pressureBar, double alkali)
    {
        const double core = 5.0, strengthNa = 25.0, strengthK = 30.0, continuum = 0.25;
        double dNa = Math.Abs(nm - NaD), dK = Math.Abs(nm - KI);
        // The power-law wing is cut off smoothly beyond about 150 nm, where the line broadening theory stops being valid.
        double na = strengthNa * Math.Pow(core / (dNa + core), 1.5) * Math.Exp(-(dNa / 150.0) * (dNa / 150.0));
        double k = strengthK * Math.Pow(core / (dK + core), 1.5) * Math.Exp(-(dK / 150.0) * (dK / 150.0));
        return pressureBar * (alkali * (na + k) + continuum);
    }

    /// <summary>The share of the alkali metals still in the gas, by kind: the Y dwarfs have lost much of theirs to Na2S and KCl clouds.</summary>
    public static double AlkaliFactor(FluidKind kind)
    {
        switch (kind)
        {
            case FluidKind.L: return 1.0;
            case FluidKind.T: return 1.0;
            case FluidKind.Y: return 0.2;
            case FluidKind.HotJupiter: return 0.6;
            default: return 0.0;
        }
    }

    /// <summary>The visible luminance of a blackbody relative to the Sun's: the integral of Planck against the eye's response over the same for 5772 K.</summary>
    public static double LuminanceRelativeToSun(double kelvin)
    {
        double y = 0.0;
        for (int i = 0; i < Samples; i++) y += ColorTemperature.PlanckSpectralRadiance((FirstNm + i * StepNm) * 1e-9, kelvin) * Ybar[i];
        return y / SolarY;
    }

    /// <summary>
    /// Linear Rec.709 radiance (Y = 1 for the Sun's disc) of a blackbody at <paramref name="kelvin"/> seen at the direction
    /// cosine <paramref name="mu"/> through <paramref name="pressureBar"/> of gas with the given alkali share.
    /// </summary>
    public static Rgb Emission(double kelvin, double pressureBar, double alkali, double mu)
    {
        double x = 0.0, y = 0.0, z = 0.0;
        double slant = 1.0 / Math.Max(mu, 0.02);
        for (int i = 0; i < Samples; i++)
        {
            double nm = FirstNm + i * StepNm;
            double b = ColorTemperature.PlanckSpectralRadiance(nm * 1e-9, kelvin) * Math.Exp(-GasDepth(nm, pressureBar, alkali) * slant);
            x += b * Xbar[i];
            y += b * Ybar[i];
            z += b * Zbar[i];
        }
        Rgb c = ColorTemperature.XyzToLinearRec709(x / SolarY, y / SolarY, z / SolarY);
        return new Rgb(Math.Max(0.0, c.R), Math.Max(0.0, c.G), Math.Max(0.0, c.B));
    }

    /// <summary>The same through no gas: the blackbody itself.</summary>
    public static Rgb Blackbody(double kelvin) => Emission(kelvin, 0.0, 0.0, 1.0);
}

/// <summary>
/// A table of the radiance of each deck (and of the clear gas below) against temperature and viewing angle, for the shader:
/// rows are deck * <see cref="MuBins"/> + mu bin, columns temperature from <see cref="MinKelvin"/> to <see cref="MaxKelvin"/>,
/// log spaced. Each texel holds the colour as chromaticity (linear Rec.709 divided by its luminance, in rgb) and the natural
/// logarithm of the luminance (alpha): the luminance changes by a factor of a few per column, so it is interpolated in its
/// logarithm. Luminance 1 is the Sun's disc. In the thermal-infrared view the table holds the 5 micrometre radiance of a
/// blackbody against the Sun's at the same wavelength (grey: the false colour is applied by the shader).
/// </summary>
public sealed class EmissionLut
{
    public const int Columns = 48, MuBins = 5;
    public const double MinKelvin = 120.0, MaxKelvin = 3600.0;
    /// <summary>The thermal-infrared window, nm: 4.8 to 5 micrometres, where Jupiter's deep, warm layers glow through the clouds (Gemini, Juno) and brown dwarfs are brightest.</summary>
    public const double ThermalInfraredNm = 5000.0;
    /// <summary>
    /// The cloud opacity at 5 micrometres as a multiple of the visible one: ammonia, water and silicate ice absorb strongly there (Jupiter's clouds are optically thick at 5 micrometres), where the extinction of
    /// such small particles falls with wavelength (about as lambda to the -1 to -2), so thin clouds are nearly clear there
    /// and only the thick ones are dark.
    /// </summary>
    public const double ThermalInfraredTauScale = 0.6;

    public readonly int Decks;
    public readonly bool ThermalInfrared;
    public readonly float[] Data;   // RGBA, row-major

    public int Rows => Decks * MuBins;

    /// <summary>Cosine of the viewing angle of a bin: 0.04 (the limb) to 1 (the centre of the disc).</summary>
    public static double MuOf(int bin) => 0.04 + 0.96 * bin / (MuBins - 1);

    public static double KelvinOf(int column) => MinKelvin * Math.Pow(MaxKelvin / MinKelvin, column / (double)(Columns - 1));

    /// <summary>Texture coordinate (0..1 across the row) of a temperature.</summary>
    public static double CoordinateOf(double kelvin) => Math.Log(Math.Max(kelvin, 1.0) / MinKelvin) / Math.Log(MaxKelvin / MinKelvin);

    /// <summary>Builds the table for a kind: <paramref name="decks"/> clouds and the clear gas below them as the last deck.</summary>
    public EmissionLut(FluidKind kind, DeckSpec[] decks, double deepPressureBar, bool nearInfrared = false)
    {
        Decks = decks.Length + 1;
        ThermalInfrared = nearInfrared;
        Data = new float[Rows * Columns * 4];
        double alkali = DwarfSpectrum.AlkaliFactor(kind);
        for (int d = 0; d < Decks; d++)
        {
            double pressure = d < decks.Length ? decks[d].Pressure : deepPressureBar;
            for (int b = 0; b < MuBins; b++)
            {
                double mu = MuOf(b);
                for (int c = 0; c < Columns; c++)
                {
                    Rgb e;
                    if (nearInfrared)
                    {
                        double ratio = ColorTemperature.PlanckSpectralRadiance(ThermalInfraredNm * 1e-9, KelvinOf(c))
                            / ColorTemperature.PlanckSpectralRadiance(ThermalInfraredNm * 1e-9, DwarfSpectrum.SolarTemperature);
                        e = new Rgb(ratio, ratio, ratio);
                    }
                    else
                    {
                        // The hue is that of the face-on view; only the brightness follows the slant path (so the limb is darker, not a different colour).
                        Rgb face = DwarfSpectrum.Emission(KelvinOf(c), pressure, alkali, 1.0);
                        Rgb slant = DwarfSpectrum.Emission(KelvinOf(c), pressure, alkali, mu);
                        double fl = face.Luminance;
                        e = fl > 0.0 ? face * (slant.Luminance / fl) : slant;
                    }
                    int o = ((d * MuBins + b) * Columns + c) * 4;
                    double y = e.Luminance;
                    if (y > 1e-60)
                    {
                        Data[o] = (float)(e.R / y);
                        Data[o + 1] = (float)(e.G / y);
                        Data[o + 2] = (float)(e.B / y);
                        Data[o + 3] = (float)Math.Log(y);
                    }
                    else
                    {
                        Data[o] = 1f; Data[o + 1] = 0f; Data[o + 2] = 0f;
                        Data[o + 3] = (float)Math.Log(1e-60);
                    }
                }
            }
        }
    }

    /// <summary>The colour in a texel (chromaticity times the exponential of the log luminance).</summary>
    public static void Decode(float[] data, int texel, out double r, out double g, out double b)
    {
        int o = texel * 4;
        double y = Math.Exp(data[o + 3]);
        r = data[o] * y; g = data[o + 1] * y; b = data[o + 2] * y;
    }
}
