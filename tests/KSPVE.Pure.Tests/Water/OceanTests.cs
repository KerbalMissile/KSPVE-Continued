using System;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Water;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Water;

public class OceanSpectrumTests
{
    private const double G = OceanSpectrum.EarthGravity;

    [Test]
    public void FullyDevelopedSea_HasPiersonMoskowitzsHeight_AndGrowsWithTheSquareOfTheWind()
    {
        SeaState a = SeaState.From(10.0, 1e8, G), b = SeaState.From(20.0, 1e8, G);
        // Hs = 0.21 U^2 / g for a fully developed sea (Pierson and Moskowitz 1964): 2.1 m at 10 m/s.
        Assert.That(a.SignificantHeight, Is.EqualTo(0.21 * 100.0 / G).Within(0.3));
        Assert.That(b.SignificantHeight / a.SignificantHeight, Is.EqualTo(4.0).Within(0.4));
        Assert.That(a.PeakFrequency, Is.EqualTo(0.877 * G / 10.0).Within(0.02));
    }

    [Test]
    public void ALimitedFetch_MakesASmallerShorterSea()
    {
        SeaState near = SeaState.From(12.0, 10000.0, G), mid = SeaState.From(12.0, 100000.0, G), far = SeaState.From(12.0, 1e8, G);
        Assert.That(near.SignificantHeight, Is.LessThan(mid.SignificantHeight));
        Assert.That(mid.SignificantHeight, Is.LessThan(far.SignificantHeight));
        Assert.That(near.PeakWavelength, Is.LessThan(far.PeakWavelength));
        // Hasselmann's fetch-limited height, Hs ~ 0.0016 U^2 sqrt(F g / U^2) / g * ... : 1.5 m at 12 m/s over 20 km is the right order.
        Assert.That(SeaState.From(12.0, 20000.0, G).SignificantHeight, Is.InRange(0.5, 2.0));
    }

    [Test]
    public void ACalmSea_IsFlat()
    {
        Assert.That(SeaState.From(0.0, 1e5, G).SignificantHeight, Is.EqualTo(0.0));
        Assert.That(SeaState.From(0.0, 1e5, G).WhitecapCoverage, Is.EqualTo(0.0));
    }

    [Test]
    public void TheDirectionalSpread_IntegratesToOne_AndIsWiderAwayFromThePeak()
    {
        foreach (double s in new[] { 1.0, 5.0, 16.0, 60.0 })
        {
            double sum = 0.0;
            int n = 20000;
            for (int i = 0; i < n; i++) sum += OceanSpectrum.Directional(-Math.PI + (i + 0.5) * 2.0 * Math.PI / n, s) * 2.0 * Math.PI / n;
            Assert.That(sum, Is.EqualTo(1.0).Within(1e-3), "s = " + s);
        }
        double wp = OceanSpectrum.PeakFrequency(10.0, 1e8, G);
        double atPeak = OceanSpectrum.SpreadExponent(wp, wp, 10.0, G);
        Assert.That(OceanSpectrum.SpreadExponent(2.0 * wp, wp, 10.0, G), Is.LessThan(atPeak));
        Assert.That(OceanSpectrum.SpreadExponent(0.6 * wp, wp, 10.0, G), Is.LessThan(atPeak));
        Assert.That(OceanSpectrum.LogGamma(5.0), Is.EqualTo(Math.Log(24.0)).Within(1e-9));
    }

    [Test]
    public void Dispersion_IsTheDeepAndShallowLimits_AndInvertsToTheWaveNumber()
    {
        double k = 0.1;
        Assert.That(OceanSpectrum.Dispersion(k, double.PositiveInfinity, G, 0.0), Is.EqualTo(Math.Sqrt(G * k)).Within(1e-9));
        Assert.That(OceanSpectrum.Dispersion(0.001, 2.0, G, 0.0), Is.EqualTo(0.001 * Math.Sqrt(G * 2.0)).Within(1e-5));
        foreach (double depth in new[] { 1.0, 20.0, 5000.0 })
        {
            double w = OceanSpectrum.Dispersion(k, depth, G);
            Assert.That(OceanSpectrum.WaveNumber(w, depth, G), Is.EqualTo(k).Within(1e-7));
        }
        // Capillary waves run faster than the gravity law says.
        Assert.That(OceanSpectrum.Dispersion(500.0, double.PositiveInfinity, G), Is.GreaterThan(Math.Sqrt(G * 500.0) * 1.5));
    }

    [Test]
    public void AGeneratedCascade_HoldsTheSpectrumsEnergy_AndRepeatsEveryPeriod()
    {
        const double wind = 12.0, fetch = 2e5;
        double kMin = 0.02, kMax = 1.0, length = 300.0;
        double expected = OceanSpectrum.HeightVariance(kMin, kMax, wind, fetch, double.PositiveInfinity, G);
        double mean = 0.0;
        const int seeds = 6;
        for (int s = 1; s <= seeds; s++)
        {
            var cascade = new OceanCascade(128, length, kMin, kMax, wind, 0.0, fetch, G, double.PositiveInfinity, (ulong)s);
            var field = new CascadeField(128);
            double measured = 0.0;
            for (int t = 0; t < 3; t++)
            {
                cascade.Evaluate(17.0 + 91.0 * t, field);
                double sum = 0.0, sq = 0.0;
                foreach (double h in field.Height) { sum += h; sq += h * h; }
                int n = field.Height.Length;
                measured += sq / n - (sum / n) * (sum / n);
            }
            mean += measured / 3.0 / seeds;
        }
        // The random sampling of a few hundred long waves is noisy; the mean over seeds must be near the integral.
        Assert.That(mean / expected, Is.InRange(0.6, 1.5));

        var c1 = new OceanCascade(64, 100.0, 0.05, 2.0, 8.0, 0.4, 1e5, G, double.PositiveInfinity, 3);
        var f1 = new CascadeField(64);
        var f2 = new CascadeField(64);
        c1.Evaluate(5.0, f1);
        c1.Evaluate(5.0 + OceanCascade.Period, f2);
        for (int i = 0; i < f1.Height.Length; i++) Assert.That(f2.Height[i], Is.EqualTo(f1.Height[i]).Within(1e-6));
    }

    [Test]
    public void TheSeaFollowsTheWind_BiggerWavesWithMoreWind_AndLongerCrestsAcrossIt()
    {
        double Rms(double wind)
        {
            var c = new OceanCascade(128, 400.0, 0.02, 1.0, wind, 0.0, 2e5, G, double.PositiveInfinity, 7);
            var f = new CascadeField(128);
            c.Evaluate(3.0, f);
            double sq = 0.0;
            foreach (double h in f.Height) sq += h * h;
            return Math.Sqrt(sq / f.Height.Length);
        }
        Assert.That(Rms(14.0), Is.GreaterThan(Rms(7.0) * 1.5));
        // Waves travel along the wind (x): the slope along x outweighs the slope across it.
        var cascade = new OceanCascade(128, 400.0, 0.02, 1.0, 12.0, 0.0, 2e5, G, double.PositiveInfinity, 11);
        var field = new CascadeField(128);
        cascade.Evaluate(1.0, field);
        double sx = 0.0, sz = 0.0;
        for (int i = 0; i < field.SlopeX.Length; i++) { sx += field.SlopeX[i] * field.SlopeX[i]; sz += field.SlopeZ[i] * field.SlopeZ[i]; }
        Assert.That(sx, Is.GreaterThan(sz));
    }

    [Test]
    public void TheChoppySurface_FoldsWhereTheCrestsSharpen_AndTheJacobianAverageIsOne()
    {
        var cascade = new OceanCascade(128, 200.0, 0.03, 3.0, 18.0, 0.0, 2e5, G, double.PositiveInfinity, 5);
        var field = new CascadeField(128);
        cascade.Evaluate(2.0, field);
        double sum = 0.0, min = double.MaxValue;
        for (int i = 0; i < field.Height.Length; i++)
        {
            double j = OceanCascade.Jacobian(field, i, 1.0);
            sum += j;
            min = Math.Min(min, j);
        }
        Assert.That(sum / field.Height.Length, Is.EqualTo(1.0).Within(0.05));
        Assert.That(min, Is.LessThan(0.8), "the surface is pinched together somewhere (foam forms there)");
        // The displacement toward a crest is opposite to the height's rise: where the surface is highest, it converges.
        double divergenceAtCrests = 0.0;
        int crests = 0;
        double top = 0.0;
        foreach (double h in field.Height) top = Math.Max(top, h);
        for (int i = 0; i < field.Height.Length; i++)
            if (field.Height[i] > 0.7 * top) { divergenceAtCrests += field.Dxx[i] + field.Dzz[i]; crests++; }
        Assert.That(divergenceAtCrests / Math.Max(crests, 1), Is.LessThan(0.0));
    }
}

public class SeaStateTests
{
    private const double G = OceanSpectrum.EarthGravity;

    [Test]
    public void WhitecapCoverage_IsMonahansLaw()
    {
        Assert.That(Whitecaps.Coverage(10.0), Is.EqualTo(3.84e-6 * Math.Pow(10.0, 3.41)).Within(1e-9));
        Assert.That(Whitecaps.Coverage(10.0), Is.InRange(0.008, 0.012));
        Assert.That(Whitecaps.Coverage(20.0), Is.GreaterThan(Whitecaps.Coverage(10.0) * 8.0));
        Assert.That(Whitecaps.Coverage(0.0), Is.EqualTo(0.0));
        Assert.That(Whitecaps.Coverage(200.0), Is.EqualTo(1.0));
    }

    [Test]
    public void TheNormalQuantile_InvertsTheDistribution()
    {
        Assert.That(Whitecaps.NormalQuantile(0.5), Is.EqualTo(0.0).Within(1e-9));
        Assert.That(Whitecaps.NormalQuantile(0.975), Is.EqualTo(1.959964).Within(1e-5));
        Assert.That(Whitecaps.NormalQuantile(0.01), Is.EqualTo(-2.326348).Within(1e-5));
        Assert.That(Whitecaps.NormalQuantile(0.999), Is.EqualTo(3.090232).Within(1e-5));
    }

    [Test]
    public void TheJacobianThreshold_GivesTheCoverageOfTheField()
    {
        const double wind = 16.0;
        var cascade = new OceanCascade(256, 160.0, 0.04, 5.0, wind, 0.0, 3e5, G, double.PositiveInfinity, 9);
        var field = new CascadeField(256);
        double slopeVar = OceanSpectrum.SlopeVariance(0.04, 5.0, wind, 3e5, double.PositiveInfinity, G);
        double coverage = 0.05;
        double threshold = Whitecaps.JacobianThreshold(coverage, 1.0, Math.Sqrt(slopeVar));
        double share = 0.0;
        const int frames = 4;
        for (int t = 0; t < frames; t++)
        {
            cascade.Evaluate(10.0 + 70.0 * t, field);
            int below = 0;
            for (int i = 0; i < field.Height.Length; i++) if (OceanCascade.Jacobian(field, i, 1.0) < threshold) below++;
            share += below / (double)field.Height.Length / frames;
        }
        Assert.That(share, Is.InRange(coverage / 3.0, coverage * 3.0));
    }

    [Test]
    public void CoxMunk_SplitsTheSlopeVarianceIntoWindAndAcrossWind()
    {
        foreach (double u in new[] { 2.0, 7.0, 15.0 })
        {
            Assert.That(CoxMunk.SlopeVariance(u), Is.EqualTo(0.003 + 0.00512 * u).Within(1e-12));
            Assert.That(CoxMunk.UpwindVariance(u) + CoxMunk.CrosswindVariance(u), Is.EqualTo(CoxMunk.SlopeVariance(u)).Within(0.001));
        }
        // The pdf integrates to one and has the stated variances.
        double w = 8.0, step = 0.004, total = 0.0, vu = 0.0, vc = 0.0;
        for (double zu = -1.0; zu <= 1.0; zu += step)
        {
            for (double zc = -1.0; zc <= 1.0; zc += step)
            {
                double p = CoxMunk.Pdf(zu, zc, w) * step * step;
                total += p; vu += p * zu * zu; vc += p * zc * zc;
            }
        }
        Assert.That(total, Is.EqualTo(1.0).Within(0.01));
        Assert.That(vu, Is.EqualTo(CoxMunk.UpwindVariance(w)).Within(0.004));
        Assert.That(vc, Is.EqualTo(CoxMunk.CrosswindVariance(w)).Within(0.004));
    }

    [Test]
    public void TheSunsGlitter_PeaksAtTheMirrorDirection_AndWidensWithTheWind()
    {
        // Sun 40 degrees above the horizon behind the viewer: the mirror direction for the viewer is straight ahead on the opposite side.
        double e = 40.0 * Math.PI / 180.0;
        var sun = new Double3(Math.Cos(e), Math.Sin(e), 0.0);
        var mirror = new Double3(-Math.Cos(e), Math.Sin(e), 0.0);
        var wind = new Double3(1, 0, 0);
        double peak = CoxMunk.GlitterBrdf(sun, mirror, wind, 5.0);
        double away = CoxMunk.GlitterBrdf(sun, new Double3(-Math.Cos(e + 0.6), Math.Sin(e + 0.6), 0.0), wind, 5.0);
        Assert.That(peak, Is.GreaterThan(away * 3.0));
        // A rougher sea spreads the glitter: lower in the middle, higher off to the side.
        Assert.That(CoxMunk.GlitterBrdf(sun, mirror, wind, 15.0), Is.LessThan(peak));
        double awayRough = CoxMunk.GlitterBrdf(sun, new Double3(-Math.Cos(e + 0.6), Math.Sin(e + 0.6), 0.0), wind, 15.0);
        Assert.That(awayRough, Is.GreaterThan(away));
        Assert.That(CoxMunk.GlitterBrdf(new Double3(0, -1, 0), mirror, wind, 5.0), Is.EqualTo(0.0));
    }
}

public class WaterOpticsTests
{
    [Test]
    public void Fresnel_IsTheExactEquations()
    {
        Assert.That(WaterOptics.Fresnel(1.0), Is.EqualTo(WaterOptics.NormalReflectance()).Within(1e-9));
        Assert.That(WaterOptics.NormalReflectance(), Is.EqualTo(0.021).Within(0.001));
        Assert.That(WaterOptics.Fresnel(0.0), Is.EqualTo(1.0).Within(1e-9));
        // Brewster's angle for water (53 degrees from the normal): the reflection is only the perpendicular share, four percent.
        double brewster = Math.Cos(Math.Atan(WaterOptics.SeawaterIndex));
        Assert.That(WaterOptics.Fresnel(brewster), Is.LessThan(0.05));
        Assert.That(WaterOptics.Fresnel(0.5), Is.GreaterThan(WaterOptics.Fresnel(0.9)));
        for (double c = 0.05; c <= 1.0; c += 0.05)
            Assert.That(WaterOptics.Schlick(c), Is.EqualTo(WaterOptics.Fresnel(c)).Within(0.08), "cos " + c);
    }

    [Test]
    public void RedIsAbsorbedFirst_SoDeepWaterIsBlue_AndPlanktonMakesItGreen()
    {
        Double3 a = WaterOptics.Absorption(0.03);
        Assert.That(a.X, Is.GreaterThan(a.Y));
        Assert.That(a.Y, Is.GreaterThan(a.Z));
        Double3 t = WaterOptics.Transmittance(0.1, 10.0);
        Assert.That(t.X, Is.LessThan(0.1), "red is nearly gone in 10 m");
        Assert.That(t.Z, Is.GreaterThan(0.5), "blue goes on");
        Double3 clear = WaterOptics.DeepReflectance(0.03), rich = WaterOptics.DeepReflectance(3.0);
        Assert.That(clear.Z, Is.GreaterThan(clear.Y));
        Assert.That(clear.Y, Is.GreaterThan(clear.X));
        Assert.That(clear.Z, Is.InRange(0.03, 0.07), "clear ocean water returns a few percent of the blue");
        Assert.That(rich.Y / rich.Z, Is.GreaterThan(clear.Y / clear.Z * 2.0), "plankton shifts the colour to green");
    }

    [Test]
    public void TheShallowReflectance_RunsFromTheBottomToTheDeepWater()
    {
        Double3 deep = WaterOptics.DeepReflectance(0.1), a = WaterOptics.Absorption(0.1), bb = WaterOptics.Backscattering(0.1);
        Double3 k = WaterOptics.TwoWayAttenuation(a, bb, 0.8, 0.8);
        var sand = new Double3(0.4, 0.35, 0.25);
        Double3 zero = WaterOptics.ShallowReflectance(deep, k, 0.0, sand);
        Assert.That(zero.X, Is.EqualTo(0.4).Within(1e-9));
        Double3 far = WaterOptics.ShallowReflectance(deep, k, 500.0, sand);
        Assert.That(far.Z, Is.EqualTo(deep.Z).Within(1e-6));
        Double3 mid = WaterOptics.ShallowReflectance(deep, k, 3.0, sand);
        Assert.That(mid.X, Is.LessThan(0.4 * 0.5), "red from the sand fades within a few metres");
        Assert.That(mid.Z, Is.GreaterThan(deep.Z));
    }
}

public class ShoalingTests
{
    private const double G = OceanSpectrum.EarthGravity;

    [Test]
    public void WavesGrowAsTheyShoal_FollowingGreensLaw()
    {
        double w = 2.0 * Math.PI / 8.0;   // an 8 second swell
        Assert.That(Shoaling.HeightFactor(w, 5000.0, G), Is.EqualTo(1.0).Within(1e-3));
        Assert.That(Shoaling.HeightFactor(w, 1.0, G), Is.GreaterThan(1.3));
        // Very shallow: h^(-1/4), so a depth 16 times smaller gives twice the height.
        Assert.That(Shoaling.HeightFactor(w, 0.05, G) / Shoaling.HeightFactor(w, 0.8, G), Is.EqualTo(2.0).Within(0.15));
        // The famous dip to 0.91 where the depth is about a fifth of the wavelength.
        double min = 1.0;
        for (double d = 5.0; d < 400.0; d *= 1.05) min = Math.Min(min, Shoaling.HeightFactor(w, d, G));
        Assert.That(min, Is.EqualTo(0.913).Within(0.01));
    }

    [Test]
    public void WavesShortenInShallowWater_AndBreakAtTheBreakerIndex()
    {
        double w = 2.0 * Math.PI / 8.0;
        Assert.That(Shoaling.Wavelength(w, 3.0, G), Is.LessThan(Shoaling.Wavelength(w, 5000.0, G) * 0.5));
        Assert.That(Shoaling.BreakingDepth(2.0), Is.EqualTo(2.0 / 0.78).Within(1e-9));
        Assert.That(Shoaling.BreakingFraction(1.0, 100.0), Is.EqualTo(0.0));
        Assert.That(Shoaling.BreakingFraction(1.0, 1.0), Is.EqualTo(1.0));
        Assert.That(Shoaling.BreakingFraction(1.0, 3.0), Is.InRange(0.01, 0.99));
    }
}

public class OceanLayoutTests
{
    private const double G = OceanSpectrum.EarthGravity;

    [Test]
    public void TheCascades_TileTheSpectrum_AndTheirSidesDivideTheLargest()
    {
        foreach (OceanQuality q in new[] { OceanQuality.Low, OceanQuality.Medium, OceanQuality.High, OceanQuality.Ultra })
        {
            OceanLayout l = OceanLayout.For(q);
            Assert.That(l.Cascades, Is.InRange(2, 4));
            for (int c = 0; c < l.Cascades; c++)
            {
                Assert.That(l.Lengths[0] / l.Lengths[c], Is.EqualTo(Math.Pow(OceanLayout.Ratio, c)).Within(1e-9));
                Assert.That(l.KMax[c], Is.GreaterThan(l.KMin[c]));
                if (c > 0) Assert.That(l.KMin[c], Is.EqualTo(l.KMax[c - 1]), "no gap and no overlap");
                // At least 2.5 texels to the shortest wave, and more than a few cycles across the square to the longest.
                Assert.That(l.MinWavelength(c), Is.GreaterThanOrEqualTo(2.0 * l.Lengths[c] / l.N * 1.2));
                if (c > 0) Assert.That(l.KMin[c] * l.Lengths[c] / (2.0 * Math.PI), Is.GreaterThan(3.0));
            }
        }
        Assert.That(OceanLayout.For(OceanQuality.High).MinWavelength(3), Is.LessThan(0.1), "centimetre waves");
    }

    [Test]
    public void TheSpectraTexture_HasFourFloatsATexel_AndIsBandLimited()
    {
        OceanLayout l = OceanLayout.For(OceanQuality.Low);
        float[] h0 = l.BuildSpectra(10.0, 0.3, 1e5, G, 5);
        Assert.That(h0.Length, Is.EqualTo(2 * 128 * 128 * 4));
        int nonZero = 0;
        for (int i = 0; i < h0.Length; i += 4) if (h0[i] != 0f || h0[i + 1] != 0f) nonZero++;
        Assert.That(nonZero, Is.GreaterThan(1000));
        Assert.That(h0[0], Is.EqualTo(0f), "no mean level");
        // h0(-k) of the texel at index i is h0 at the mirrored index: the second pair of the first texel's mirror.
        int n = 128;
        for (int probe = 0; probe < 20; probe++)
        {
            int x = 3 + probe, y = 40 + probe;
            int i = (y * n + x) * 4, j = (((n - y) % n) * n + (n - x) % n) * 4;
            Assert.That(h0[i + 2], Is.EqualTo(h0[j]));
            Assert.That(h0[i + 3], Is.EqualTo(h0[j + 1]));
        }
    }

    [Test]
    public void TheFoamThresholds_AreBelowOne_AndRiseWithTheWind()
    {
        OceanLayout l = OceanLayout.For(OceanQuality.High);
        double[] light = l.FoamThresholds(7.0, 2e5, G, 1.0), strong = l.FoamThresholds(20.0, 2e5, G, 1.0);
        for (int c = 0; c < l.Cascades; c++)
        {
            Assert.That(light[c], Is.LessThan(1.0));
            Assert.That(strong[c], Is.GreaterThan(light[c]).Or.GreaterThan(0.0), "more foam in a gale");
        }
    }

    [Test]
    public void TheRoughnessTable_GrowsWithTheFootprint_ToCoxAndMunksTotal()
    {
        OceanLayout l = OceanLayout.For(OceanQuality.High);
        float[] t = l.RoughnessTable(10.0, 2e5, G, 1.0);
        Assert.That(t.Length, Is.EqualTo(64));
        for (int i = 1; i < t.Length; i++) Assert.That(t[i], Is.GreaterThanOrEqualTo(t[i - 1] - 1e-7f));
        Assert.That(t[63], Is.EqualTo(0.5 * CoxMunk.SlopeVariance(10.0)).Within(0.003), "from afar all of it is unresolved");
        Assert.That(t[0], Is.LessThan(t[63] * 0.5), "close up most of it is resolved by the cascades");
        // The waves' own display scale removes resolved variance: a flat sea (scale 0) is all unresolved.
        Assert.That(l.RoughnessTable(10.0, 2e5, G, 0.0)[0], Is.EqualTo(0.5f * (float)CoxMunk.SlopeVariance(10.0)).Within(1e-6));
    }

    [Test]
    public void TheSunIsRedAtTheHorizon_AndTheSkyIsBlueOverhead()
    {
        Double3 noon = SkyModel.SunTransmittance(90.0, 1.0), dusk = SkyModel.SunTransmittance(4.0, 1.0);
        Assert.That(noon.X, Is.GreaterThan(noon.Z));
        Assert.That(noon.Z, Is.InRange(0.75, 0.9));
        Assert.That(dusk.X, Is.GreaterThan(dusk.Z * 3.0), "a low sun is red");
        Assert.That(SkyModel.SunTransmittance(30.0, 0.0).Z, Is.EqualTo(1.0), "no air, no reddening");
        SkyModel.Colors(45.0, 1.0, out Double3 zenith, out Double3 horizon, out Double3 ambient);
        Assert.That(zenith.Z, Is.GreaterThan(zenith.X * 2.0));
        Assert.That(horizon.X, Is.GreaterThan(zenith.X), "the horizon is paler");
        SkyModel.Colors(-20.0, 1.0, out Double3 night, out _, out _);
        Assert.That(night.Z, Is.EqualTo(0.0));
        SkyModel.Colors(45.0, 0.0, out Double3 vacuum, out _, out _);
        Assert.That(vacuum.Z, Is.EqualTo(0.0), "no air, a black sky");
    }
}
