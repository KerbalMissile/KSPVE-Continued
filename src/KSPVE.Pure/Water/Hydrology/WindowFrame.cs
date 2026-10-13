using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>
/// A square window of a sphere's surface as a flat map: the sphere's directions mapped through the tangent plane at its centre
/// (the gnomonic projection), x east and y north in metres from the centre. Over a window much smaller than the planet the
/// distances are true to a fraction of a percent; the same mapping is used to read the terrain, to work out the water and to
/// place it again, so nothing drifts.
/// </summary>
public sealed class WindowFrame
{
    public readonly Double3 Center, East, North;
    public readonly double Radius;

    public WindowFrame(Double3 center, Double3 east, Double3 north, double radius)
    {
        Center = center.Normalized();
        // East and north made exactly perpendicular to the centre and each other.
        Double3 e = east - Center * Double3.Dot(east, Center);
        East = e.Normalized();
        North = Double3.Cross(Center, East).Normalized() * (Double3.Dot(Double3.Cross(Center, East), north) < 0.0 ? -1.0 : 1.0);
        Radius = radius;
    }

    /// <summary>The unit direction of the point <paramref name="east"/> metres east and <paramref name="north"/> metres north of the centre on the map.</summary>
    public Double3 ToDirection(double east, double north) => (Center * Radius + East * east + North * north).Normalized();

    /// <summary>The map position of a direction (the inverse of <see cref="ToDirection"/>).</summary>
    public void FromDirection(Double3 direction, out double east, out double north)
    {
        double c = Math.Max(Double3.Dot(direction, Center), 1e-6);
        east = Radius * Double3.Dot(direction, East) / c;
        north = Radius * Double3.Dot(direction, North) / c;
    }
}
