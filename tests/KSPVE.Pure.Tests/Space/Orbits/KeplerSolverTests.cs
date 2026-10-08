using System;
using KSPVE.Pure.Space.Orbits;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Orbits;

[TestFixture]
public sealed class KeplerSolverTests
{
    [Test]
    public void ResidualIsTinyAcrossGrid()
    {
        for (int ei = 0; ei <= 10; ei++)
        {
            double e = ei == 10 ? 0.99 : ei / 10.0;
            for (int k = 0; k <= 200; k++)
            {
                double m = -Math.PI + 2.0 * Math.PI * k / 200.0;
                if (k == 0) m = Math.PI; // (-pi, pi]: -pi is the same angle as pi
                double big = KeplerSolver.SolveEccentricAnomaly(m, e);
                Assert.That(Math.Abs(big - e * Math.Sin(big) - m), Is.LessThan(1e-12), $"e={e} M={m}");
            }
        }
    }

    [Test]
    public void ZeroEccentricityReturnsMeanAnomaly()
    {
        Assert.That(KeplerSolver.SolveEccentricAnomaly(1.234, 0.0), Is.EqualTo(1.234).Within(1e-15));
    }

    [Test]
    public void WrapsLargeMeanAnomaly()
    {
        double a = KeplerSolver.SolveEccentricAnomaly(0.7 + 4 * Math.PI, 0.3);
        double b = KeplerSolver.SolveEccentricAnomaly(0.7, 0.3);
        Assert.That(a, Is.EqualTo(b).Within(1e-12));
    }
}
