using System;
using KSPVE.Pure.Lighting;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Lighting;

public class PhotometryTests
{
    // Kerbin's semi-major axis around Kerbol, metres.
    private const double KerbinOrbitRadius = 13_599_840_256.0;

    private static double LuminosityGiving1360AtKerbin() => 1360.0 * 4.0 * Math.PI * KerbinOrbitRadius * KerbinOrbitRadius;

    [Test]
    public void FluxAtDistance_RecoversFluxUsedToDeriveLuminosity()
    {
        double flux = Photometry.FluxAtDistance(LuminosityGiving1360AtKerbin(), KerbinOrbitRadius);
        Assert.That(flux, Is.EqualTo(1360.0).Within(1e-9).Percent);
    }

    [Test]
    public void FluxAtDistance_FollowsInverseSquareLaw()
    {
        double near = Photometry.FluxAtDistance(LuminosityGiving1360AtKerbin(), KerbinOrbitRadius);
        double far = Photometry.FluxAtDistance(LuminosityGiving1360AtKerbin(), 2.0 * KerbinOrbitRadius);
        Assert.That(far, Is.EqualTo(near / 4.0).Within(1e-9).Percent);
    }

    [Test]
    public void FluxAtDistance_RejectsNonPositiveDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Photometry.FluxAtDistance(1.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Photometry.FluxAtDistance(1.0, -5.0));
    }

    [Test]
    public void IlluminanceAtKerbin_IsInTheRangeOfRealSunlightAboveAtmosphere()
    {
        // Real extraterrestrial sunlight is roughly 1361 W/m^2 and about 128,000 lux.
        double lux = Photometry.IlluminanceLux(1360.0);
        Assert.That(lux, Is.InRange(120_000.0, 130_000.0));
    }
}
