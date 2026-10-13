using System;
using KSPVE.Pure.Fluids;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Fluids;

public class BarotropicModelTests
{
    private const double A = 7.0e7, Omega = 2.0 * Math.PI / (5.0 * 3600.0);

    private static BarotropicModel Free(int k, double omega, ulong seed = 1)
    {
        var p = new FlowParameters { Radius = A, Omega = omega, DragTime = 0.0, ForcingPower = 0.0, HyperEfoldSteps = 0.0, Seed = seed };
        return new BarotropicModel(k, p);
    }

    /// <summary>A smooth random start with energy at the large scales only (so the truncation does not matter).</summary>
    private static void Randomise(BarotropicModel m, int lMax, double speed, ulong seed)
    {
        var rng = new FluidRandom(seed);
        for (int mm = 0; mm <= lMax; mm++)
            for (int l = Math.Max(mm, 1); l <= lMax; l++)
            {
                double re = rng.NextGaussian(), im = mm == 0 ? 0.0 : rng.NextGaussian();
                m.SetCoefficient(l, mm, re, im);
            }
        double scale = speed / m.RmsSpeed();
        for (int i = 0; i < m.ZRe.Length; i++) { m.ZRe[i] *= scale; m.ZIm[i] *= scale; }
    }

    [Test]
    public void TheTransformRoundTrips_AFieldOfLowDegrees()
    {
        var layout = new SpectralLayout(21);
        SphereGrid grid = SphereGrid.Gaussian(layout);
        var rng = new FluidRandom(5);
        var re = new double[layout.Size];
        var im = new double[layout.Size];
        for (int m = 0; m <= 21; m++)
            for (int l = m; l <= 21; l++)
            {
                re[layout.Index(l, m)] = rng.NextGaussian();
                im[layout.Index(l, m)] = m == 0 ? 0.0 : rng.NextGaussian();
            }
        var field = new double[grid.NLat * grid.NLon];
        grid.Synthesize(re, im, field);
        var re2 = new double[layout.Size];
        var im2 = new double[layout.Size];
        grid.Analyse(field, re2, im2);
        for (int m = 0; m <= 21; m++)
            for (int l = m; l <= 21; l++)
            {
                Assert.That(re2[layout.Index(l, m)], Is.EqualTo(re[layout.Index(l, m)]).Within(1e-9));
                Assert.That(im2[layout.Index(l, m)], Is.EqualTo(im[layout.Index(l, m)]).Within(1e-9));
            }
    }

    [Test]
    public void TheLegendreFunctionsAreOrthonormal()
    {
        var layout = new SpectralLayout(12);
        SphereGrid grid = SphereGrid.Gaussian(layout);
        for (int m = 0; m <= 4; m++)
            for (int l1 = m; l1 <= 12; l1 += 3)
                for (int l2 = m; l2 <= 12; l2 += 2)
                {
                    double sum = 0.0;
                    for (int j = 0; j < grid.NLat; j++)
                        sum += grid.Weights[j] * grid.Table.P[j * layout.Size + layout.Index(l1, m)] * grid.Table.P[j * layout.Size + layout.Index(l2, m)];
                    Assert.That(sum, Is.EqualTo(l1 == l2 ? 1.0 : 0.0).Within(1e-12));
                }
    }

    [Test]
    public void SolidBodyRotation_StaysSteady()
    {
        // zeta = c * mu is the l = 1, m = 0 harmonic: a solid rotation, an exact steady solution of the equation.
        BarotropicModel model = Free(21, Omega);
        model.SetCoefficient(1, 0, 3.0e-5, 0.0);
        double[] before = (double[])model.ZRe.Clone();
        for (int i = 0; i < 100; i++) model.Step(900.0);
        for (int i = 0; i < before.Length; i++)
        {
            Assert.That(model.ZRe[i], Is.EqualTo(before[i]).Within(1e-12), "coefficient " + i);
            Assert.That(model.ZIm[i], Is.EqualTo(0.0).Within(1e-12));
        }
    }

    [Test]
    public void ASingleHarmonic_IsARossbyWaveAtTheAnalyticSpeed()
    {
        // A single harmonic (l, m) has zeta proportional to psi, so the nonlinear term vanishes and the wave drifts west at
        // omega = -2 Omega m / (l (l+1)) (the Rossby-Haurwitz wave): its phase turns at that rate.
        BarotropicModel model = Free(21, Omega);
        const int l = 5, m = 3;
        model.SetCoefficient(l, m, 2.0e-5, 0.0);
        double t = 0.0;
        for (int i = 0; i < 80; i++) { model.Step(600.0); t += 600.0; }
        int index = model.Layout.Index(l, m);
        double phase = Math.Atan2(model.ZIm[index], model.ZRe[index]);
        double expected = 2.0 * Omega * m / (l * (l + 1.0)) * t;
        expected = Math.IEEERemainder(expected, 2.0 * Math.PI);
        Assert.That(phase, Is.EqualTo(expected).Within(1e-4));
        double amplitude = Math.Sqrt(model.ZRe[index] * model.ZRe[index] + model.ZIm[index] * model.ZIm[index]);
        Assert.That(amplitude, Is.EqualTo(2.0e-5).Within(1e-9));
    }

    [Test]
    public void ATurbulentFlow_ConservesEnergyAndAbsoluteEnstrophy()
    {
        BarotropicModel model = Free(31, Omega, 3);
        Randomise(model, 14, 200.0, 11);
        double e0 = model.Energy(), z0 = model.AbsoluteEnstrophy();
        double dt = model.CourantDt(0.4, 600.0);
        for (int i = 0; i < 300; i++)
        {
            model.Step(Math.Min(dt, model.CourantDt(0.4, 600.0)));
            Assert.That(model.IsFinite());
        }
        Assert.That(model.Energy(), Is.EqualTo(e0).Within(e0 * 2e-4), "energy");
        Assert.That(model.AbsoluteEnstrophy(), Is.EqualTo(z0).Within(Math.Abs(z0) * 2e-4 + 1e-24), "absolute enstrophy");
        Assert.That(model.Steps, Is.EqualTo(300));
    }

    [Test]
    public void WithoutRotation_EnergyAndRelativeEnstrophyAreBothConserved()
    {
        BarotropicModel model = Free(31, 0.0, 4);
        Randomise(model, 12, 150.0, 21);
        double e0 = model.Energy(), z0 = model.Enstrophy();
        for (int i = 0; i < 300; i++) model.Step(Math.Min(model.CourantDt(0.4, 600.0), 600.0));
        Assert.That(model.Energy(), Is.EqualTo(e0).Within(e0 * 2e-4));
        Assert.That(model.Enstrophy(), Is.EqualTo(z0).Within(z0 * 2e-4));
    }

    [Test]
    public void DragAlone_DecaysTheFlowExponentially()
    {
        var p = new FlowParameters { Radius = A, Omega = Omega, DragTime = 1.0e5, ForcingPower = 0.0, HyperEfoldSteps = 0.0 };
        var model = new BarotropicModel(21, p);
        model.SetCoefficient(1, 0, 3.0e-5, 0.0);
        double e0 = model.Energy();
        for (int i = 0; i < 100; i++) model.Step(1000.0);
        // 1e5 s of damping of the vorticity: the energy falls as exp(-2 t / tau).
        Assert.That(model.Energy() / e0, Is.EqualTo(Math.Exp(-2.0)).Within(1e-9));
    }
}
