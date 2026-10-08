using VolumetricBlackholeFx.Pure.Numerics;
using VolumetricBlackholeFx.Pure.Space;
using NUnit.Framework;

namespace VolumetricBlackholeFx.Pure.Tests.Space;

public class DiskDragTests
{
    private const double Horizon = 1000.0;
    private const double Mu = 1e12;
    private static readonly Double3 Up = Double3.UnitY;

    private static DiskDragResult At(Double3 p, Double3 v, double spin = 1.0, double timescale = 30.0, double maxAccel = 20.0) =>
        DiskDrag.Compute(p, v, Up, spin, Mu, Horizon, 3.0, 14.0, 0.12, timescale, maxAccel);

    private static Double3 MidPlane(double radii) => new Double3(radii * Horizon, 0, 0);

    [Test]
    public void ZeroOutsideTheDisk()
    {
        Assert.That(At(MidPlane(30), Double3.Zero).Weight, Is.EqualTo(0.0), "too far");
        Assert.That(At(MidPlane(1.5), Double3.Zero).Weight, Is.EqualTo(0.0), "inside the inner edge");
        var high = new Double3(5 * Horizon, 5 * Horizon, 0);
        DiskDragResult r = At(high, Double3.Zero);
        Assert.That(r.Weight, Is.EqualTo(0.0), "far above the plane");
        Assert.That(r.Acceleration, Is.EqualTo(Double3.Zero));
        Assert.That(At(Double3.Zero, Double3.Zero).Weight, Is.EqualTo(0.0), "on the axis");
    }

    [Test]
    public void WeightIsOneAtTheMidplaneMidRadius()
    {
        Assert.That(At(MidPlane(5), Double3.Zero).Weight, Is.EqualTo(1.0).Within(0.01));
    }

    [Test]
    public void FullPullAcrossTheVisibleDisk_FadingOnlyNearTheOuterEdge()
    {
        Assert.That(At(MidPlane(9.3), Double3.Zero).Weight, Is.EqualTo(1.0).Within(1e-9), "outer disk, where the user tested");
        Assert.That(At(MidPlane(10), Double3.Zero).Weight, Is.EqualTo(1.0).Within(1e-9));
        double edge = At(MidPlane(13), Double3.Zero).Weight;
        Assert.That(edge, Is.GreaterThan(0.0).And.LessThan(1.0), "fading near the outer edge");
        Assert.That(At(MidPlane(15), Double3.Zero).Weight, Is.EqualTo(0.0), "beyond the outer edge");
    }

    [Test]
    public void PullReachesUpThroughThePuffySurface()
    {
        // Half-thickness at r = 5 is 0.6 horizon radii; the puffy surface bulges to about twice that.
        Assert.That(At(new Double3(5 * Horizon, 0.6 * Horizon, 0), Double3.Zero).Weight, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(At(new Double3(5 * Horizon, 1.0 * Horizon, 0), Double3.Zero).Weight, Is.GreaterThan(0.3));
        Assert.That(At(new Double3(5 * Horizon, 1.5 * Horizon, 0), Double3.Zero).Weight, Is.EqualTo(0.0));
    }

    [Test]
    public void ReportsTheSpeedRelativeToTheGas()
    {
        Double3 p = MidPlane(5);
        double gasSpeed = System.Math.Sqrt(Mu / p.Length);
        Assert.That(At(p, Double3.Zero).RelativeSpeed, Is.EqualTo(gasSpeed).Within(1e-9));
    }

    [Test]
    public void Turbulence_IsBoundedSmoothAndDiffersBetweenSeeds()
    {
        double max = 0.0, maxStep = 0.0;
        Double3 previous = DiskTurbulence.Sample(0.0, 7);
        for (int i = 1; i < 20000; i++)
        {
            Double3 s = DiskTurbulence.Sample(i * 0.02, 7);
            max = System.Math.Max(max, System.Math.Max(System.Math.Abs(s.X), System.Math.Max(System.Math.Abs(s.Y), System.Math.Abs(s.Z))));
            maxStep = System.Math.Max(maxStep, (s - previous).Length);
            previous = s;
        }
        Assert.That(max, Is.LessThanOrEqualTo(1.0 + 1e-12));
        Assert.That(max, Is.GreaterThan(0.5), "actually moves");
        Assert.That(maxStep, Is.LessThan(0.3), "smooth at 50 Hz physics");
        Assert.That(DiskTurbulence.Sample(3.0, 1), Is.Not.EqualTo(DiskTurbulence.Sample(3.0, 2)));
    }

    [Test]
    public void ZeroWhenCoMovingWithTheGas()
    {
        Double3 p = MidPlane(5);
        // cross(up, +X) = -Z, so prograde gas at +X moves along -Z.
        var gas = new Double3(0, 0, -System.Math.Sqrt(Mu / p.Length));
        Assert.That(At(p, gas).Acceleration.Length, Is.LessThan(1e-9));
    }

    [Test]
    public void AtRest_AcceleratesAlongTheGasDirection()
    {
        DiskDragResult r = At(MidPlane(5), Double3.Zero, maxAccel: 1000.0);
        Assert.That(r.Acceleration.Z, Is.LessThan(0.0));
        Assert.That(r.Acceleration.X, Is.EqualTo(0.0).Within(1e-12));
        double expected = System.Math.Sqrt(Mu / (5 * Horizon)) / 30.0 * r.Weight;
        Assert.That(r.Acceleration.Length, Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void MagnitudeIsCappedAtMaxAccelAtTheEdge_RisingInward()
    {
        var fast = new Double3(1e7, 0, 0);
        Assert.That(At(MidPlane(10.5), fast).Acceleration.Length, Is.EqualTo(20.0).Within(1e-9), "at the edge: the cap");
        // Inside, the cap rises like 1/r (the gas gets denser): at r = 5 it is 10.5 / 5 times the edge cap.
        Assert.That(At(MidPlane(5), fast).Acceleration.Length, Is.EqualTo(20.0 * 10.5 / 5.0).Within(1e-9));
        // Past the edge it fades with the density.
        DiskDragResult outside = At(MidPlane(13), fast);
        Assert.That(outside.Acceleration.Length, Is.EqualTo(20.0 * outside.Weight).Within(1e-9));
    }

    [Test]
    public void NegativeSpin_ReversesTheGasDirection()
    {
        DiskDragResult pro = At(MidPlane(5), Double3.Zero, 1.0);
        DiskDragResult retro = At(MidPlane(5), Double3.Zero, -1.0);
        Assert.That(retro.Acceleration.Z, Is.GreaterThan(0.0));
        Assert.That(retro.Acceleration.Z, Is.EqualTo(-pro.Acceleration.Z).Within(1e-12));
    }
}
