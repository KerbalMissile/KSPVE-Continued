using System;
using KSPVE.Pure.Space.Relativity;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space.Relativity;

[TestFixture]
public sealed class SchwarzschildTests
{
    [Test]
    public void GeometricMass_OfSun_IsAbout1477Metres()
    {
        // GM_sun = 1.32712440018e20 m^3/s^2 (IAU 2015); GM/c^2 = 1476.6 m.
        Assert.That(Schwarzschild.GeometricMassMeters(1.32712440018e20), Is.EqualTo(1476.625).Within(0.01));
    }

    [Test]
    public void CharacteristicRadii_AreMultiplesOfM()
    {
        // MTW (1973) ch. 25 and 33: r_s = 2M, r_photon = 3M, r_isco = 6M, b_crit = 3*sqrt(3) M.
        Assert.That(Schwarzschild.EventHorizonRadius(2), Is.EqualTo(4));
        Assert.That(Schwarzschild.PhotonSphereRadius(2), Is.EqualTo(6));
        Assert.That(Schwarzschild.IscoRadius(2), Is.EqualTo(12));
        Assert.That(Schwarzschild.CriticalImpactParameter(1), Is.EqualTo(5.196152422706632).Within(1e-12));
    }
}

[TestFixture]
public sealed class NullGeodesicTests
{
    [Test]
    public void WeakField_DeflectionMatchesEinstein()
    {
        // Einstein 1915: alpha = 4GM/(c^2 b) = 4m/b for b >> m.
        var t = NullGeodesic.Trace(1000, 1, 1e6, 1e-3, false);
        Assert.That(t.Captured, Is.False);
        Assert.That(t.DeflectionRadians, Is.EqualTo(4.0 / 1000).Within(4.0 / 1000 * 0.01));
    }

    [Test]
    public void BelowCriticalImpactParameter_IsCaptured()
    {
        var t = NullGeodesic.Trace(5.0, 1, 1000, 1e-3, false);
        Assert.That(t.Captured, Is.True);
    }

    [Test]
    public void JustAboveCritical_WrapsAroundPhotonSphere()
    {
        // Darwin 1959 / Luminet 1979: deflection diverges logarithmically as b -> 3*sqrt(3) M.
        double b = Schwarzschild.CriticalImpactParameter(1) * 1.0001;
        var t = NullGeodesic.Trace(b, 1, 1000, 1e-3, true);
        Assert.That(t.Captured, Is.False);
        Assert.That(t.DeflectionRadians, Is.GreaterThan(Math.PI));
        Assert.That(t.MinRadius, Is.InRange(3.0, 3.1));
        Assert.That(t.Path.Count, Is.GreaterThan(10));
    }

    [Test]
    public void PathIsOnlyRecordedWhenRequested()
    {
        Assert.That(NullGeodesic.Trace(20, 1, 1000, 1e-3, false).Path.Count, Is.EqualTo(0));
    }
}

[TestFixture]
public sealed class CurvedRayTests
{
    private static RayResult Shoot(double b, double m, double sign = 1, double R = 1000)
    {
        var origin = new KSPVE.Pure.Numerics.Double3(-Math.Sqrt(R * R - b * b), sign * b, 0);
        return CurvedRay.Integrate(origin, KSPVE.Pure.Numerics.Double3.UnitX, m, 0.25, 200000, R);
    }

    private static double Deflection(RayResult r) =>
        Math.Acos(Math.Max(-1, Math.Min(1, r.ExitDirection.X)));

    [TestCase(10.0)]
    [TestCase(20.0)]
    public void AgreesWithBinetTrace(double b)
    {
        var ray = Shoot(b, 1);
        var trace = NullGeodesic.Trace(b, 1, 1000, 1e-3, false);
        Assert.That(ray.Captured, Is.False);
        Assert.That(Deflection(ray), Is.EqualTo(trace.DeflectionRadians).Within(trace.DeflectionRadians * 0.01));
    }

    [Test]
    public void BelowCritical_IsCaptured() => Assert.That(Shoot(4.5, 1).Captured, Is.True);

    [Test]
    public void SymmetricRays_GiveMirrorExits()
    {
        var up = Shoot(10, 1, 1);
        var down = Shoot(10, 1, -1);
        Assert.That(up.ExitDirection.Y, Is.EqualTo(-down.ExitDirection.Y).Within(1e-9));
        Assert.That(up.ExitDirection.X, Is.EqualTo(down.ExitDirection.X).Within(1e-9));
        Assert.That(up.ExitPosition.Y, Is.EqualTo(-down.ExitPosition.Y).Within(1e-9));
        Assert.That(up.Steps, Is.EqualTo(down.Steps));
    }
}

[TestFixture]
public sealed class RedshiftTests
{
    [Test]
    public void Gravitational_FarFieldIsOne() =>
        Assert.That(Redshift.Gravitational(1e9, 1), Is.EqualTo(1).Within(1e-8));

    [Test]
    public void Gravitational_AtIsco()
    {
        // sqrt(1 - 2/6) = sqrt(2/3) (Luminet 1979).
        Assert.That(Redshift.Gravitational(6, 1), Is.EqualTo(Math.Sqrt(2.0 / 3)).Within(1e-12));
    }

    [Test]
    public void Doppler_ZeroBetaIsOne() =>
        Assert.That(Redshift.Doppler(0, 0.3), Is.EqualTo(1).Within(1e-12));

    [Test]
    public void Doppler_HeadOnAndRecedingMatchRelativisticFormula()
    {
        // Rybicki & Lightman (1979) eq. 4.11 longitudinal case.
        double beta = 0.6;
        Assert.That(Redshift.Doppler(beta, 1), Is.EqualTo(Math.Sqrt((1 + beta) / (1 - beta))).Within(1e-12));
        Assert.That(Redshift.Doppler(beta, -1), Is.EqualTo(Math.Sqrt((1 - beta) / (1 + beta))).Within(1e-12));
    }

    [Test]
    public void Combined_IsProduct() =>
        Assert.That(Redshift.Combined(6, 1, 0.5, 0.2),
            Is.EqualTo(Redshift.Gravitational(6, 1) * Redshift.Doppler(0.5, 0.2)).Within(1e-12));
}

[TestFixture]
public sealed class AccretionDiskTests
{
    [Test]
    public void OrbitalSpeed_AtIscoIsHalfC() =>
        Assert.That(AccretionDisk.OrbitalSpeedStaticFrame(6, 1), Is.EqualTo(0.5).Within(1e-12));

    [Test]
    public void Temperature_ZeroAtInnerEdge() =>
        Assert.That(AccretionDisk.ShakuraSunyaevTemperature(6, 6, 1e7), Is.EqualTo(0).Within(1e-9));

    [Test]
    public void Temperature_PeaksAt49Over36()
    {
        // Shakura & Sunyaev 1973; Page & Thorne 1974 (Newtonian limit): T_max at r = 49/36 r_in.
        double rin = 6, peak = 1e7;
        double tPeak = AccretionDisk.ShakuraSunyaevTemperature(49.0 / 36 * rin, rin, peak);
        Assert.That(tPeak, Is.EqualTo(peak).Within(1e-6));
        Assert.That(AccretionDisk.ShakuraSunyaevTemperature(49.0 / 36 * rin * 0.95, rin, peak), Is.LessThan(peak));
        Assert.That(AccretionDisk.ShakuraSunyaevTemperature(49.0 / 36 * rin * 1.05, rin, peak), Is.LessThan(peak));
    }

    [Test]
    public void Temperature_FarOutFollowsMinusThreeQuarters()
    {
        double a = AccretionDisk.ShakuraSunyaevTemperature(1e8, 6, 1e7);
        double b = AccretionDisk.ShakuraSunyaevTemperature(2e8, 6, 1e7);
        Assert.That(b / a, Is.EqualTo(Math.Pow(2, -0.75)).Within(1e-4));
    }

    [Test]
    public void ObservedQuantities_ScaleWithG()
    {
        Assert.That(AccretionDisk.ObservedTemperature(1000, 0.8), Is.EqualTo(800).Within(1e-9));
        Assert.That(AccretionDisk.ObservedBolometricIntensityScale(0.5), Is.EqualTo(0.0625).Within(1e-12));
    }
}

[TestFixture]
public sealed class JetsTests
{
    [Test]
    public void ApproachingJetIsBrighter()
    {
        double app = Jets.BeamingFactor(0.9, Math.Cos(0.3), 0.7);
        double rec = Jets.BeamingFactor(0.9, -Math.Cos(0.3), 0.7);
        Assert.That(app, Is.GreaterThan(rec * 100));
    }

    [Test]
    public void ZeroBeta_IsUnity() =>
        Assert.That(Jets.BeamingFactor(0, 0.5, 1), Is.EqualTo(1).Within(1e-12));
}
