using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Space;

public class StreamShapeTests
{
    [Test]
    public void ADrawing_RoundTripsThroughItsText_AndJunkIsSkipped()
    {
        var drawn = new List<Double3> { new Double3(1.0, 0.0, 0.0), new Double3(0.25, -0.5, 0.0), new Double3(-0.125, 0.875, 0.0) };
        string text = StreamShape.Format(drawn);
        Assert.That(text, Is.EqualTo("1 0; 0.25 -0.5; -0.125 0.875"));
        Assert.That(StreamShape.Parse(text), Is.EqualTo(drawn));
        Assert.That(StreamShape.Parse("1 0; nonsense; 2; 0.5,0.5"), Has.Count.EqualTo(2), "pairs only, commas allowed");
        Assert.That(StreamShape.Parse(""), Is.Empty);
    }

    [Test]
    public void Resampling_SpacesThePointsEvenlyAlongTheLine()
    {
        var drawn = new List<Double3> { new Double3(0, 0, 0), new Double3(0.1, 0, 0), new Double3(1, 0, 0), new Double3(1, 1, 0) };
        List<Double3> even = StreamShape.Resample(drawn, 9);
        Assert.That(even, Has.Count.EqualTo(9));
        for (int i = 1; i < even.Count; i++) Assert.That((even[i] - even[i - 1]).Length, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(even[8], Is.EqualTo(new Double3(1, 1, 0)));
    }

    [Test]
    public void Smoothing_EasesAShakyLine_AndKeepsItsEnds()
    {
        var shaky = new List<Double3>();
        for (int i = 0; i <= 10; i++) shaky.Add(new Double3(i * 0.1, i % 2 == 0 ? 0.05 : -0.05, 0.0));
        List<Double3> smooth = StreamShape.Smooth(shaky, 3);
        Assert.That(smooth[0], Is.EqualTo(shaky[0]));
        Assert.That(smooth[10], Is.EqualTo(shaky[10]));
        Assert.That(System.Math.Abs(smooth[5].Y), Is.LessThan(0.01), "the wobble mostly gone");
    }

    [Test]
    public void TheComputedWinding_CanBeTheStartOfADrawing()
    {
        var spiral = new RocheStream.StreamSpiral(0.4, 0.2, 0.0, 1.0, 1.0);
        List<Double3> start = StreamShape.FromSpiral(spiral, 5);
        Assert.That(start[0].X, Is.EqualTo(1.0).Within(1e-9), "in units of the radius where it strikes");
        Assert.That(start[4].Length, Is.EqualTo(0.5).Within(1e-9));
    }
}
