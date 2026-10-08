using System.Collections.Generic;

namespace VolumetricBlackholeFx.Pure.Space.Relativity;

/// <summary>Result of <see cref="NullGeodesic.Trace"/>.</summary>
public sealed class GeodesicTrace
{
    public GeodesicTrace(bool captured, double deflectionRadians, double minRadius,
        IReadOnlyList<(double PhiRadians, double Radius)> path)
    {
        Captured = captured;
        DeflectionRadians = deflectionRadians;
        MinRadius = minRadius;
        Path = path;
    }

    public bool Captured { get; }
    public double DeflectionRadians { get; }
    public double MinRadius { get; }
    public IReadOnlyList<(double PhiRadians, double Radius)> Path { get; }
}

/// <summary>
/// Photon orbit in the Schwarzschild equatorial plane via the Binet equation
/// d2u/dphi2 = 3m u^2 - u, u = 1/r (Misner, Thorne, Wheeler 1973, ch. 25; Weinberg 1972, ch. 8).
/// </summary>
public static class NullGeodesic
{
    private const double MaxPhi = 400.0 * System.Math.PI;

    private static readonly IReadOnlyList<(double PhiRadians, double Radius)> Empty =
        new List<(double PhiRadians, double Radius)>();

    /// <summary>
    /// Traces an incoming ray from startRadius (which must exceed impactParameter).
    /// Deflection is the total swept angle minus pi, plus 2*asin(b/startRadius): the angle a straight line
    /// sweeps short of pi when it starts and ends at a finite radius. This makes the result the turning
    /// angle of the ray direction, independent of startRadius as it grows.
    /// A ray that neither escapes nor crosses 2m within 400 pi of azimuth is reported as captured.
    /// </summary>
    public static GeodesicTrace Trace(double impactParameter, double m, double startRadius,
        double stepRadians, bool recordPath)
    {
        double b = impactParameter;
        double u0 = 1.0 / startRadius;
        double uHorizon = 1.0 / (2.0 * m);
        double w0sq = 1.0 / (b * b) - u0 * u0 + 2.0 * m * u0 * u0 * u0;
        double u = u0;
        double w = System.Math.Sqrt(w0sq > 0 ? w0sq : 0.0); // du/dphi, incoming so > 0
        double phi = 0.0;
        double uMax = u0;
        bool passedPeriapsis = false;
        var path = new List<(double PhiRadians, double Radius)>();
        if (recordPath) path.Add((phi, 1.0 / u));
        double h = stepRadians;

        while (phi < MaxPhi)
        {
            double k1u = w, k1w = Acc(u, m);
            double k2u = w + 0.5 * h * k1w, k2w = Acc(u + 0.5 * h * k1u, m);
            double k3u = w + 0.5 * h * k2w, k3w = Acc(u + 0.5 * h * k2u, m);
            double k4u = w + h * k3w, k4w = Acc(u + h * k3u, m);
            double un = u + h / 6.0 * (k1u + 2 * k2u + 2 * k3u + k4u);
            double wn = w + h / 6.0 * (k1w + 2 * k2w + 2 * k3w + k4w);
            double phin = phi + h;

            if (un >= uHorizon)
            {
                if (recordPath) path.Add((phin, 2.0 * m));
                return new GeodesicTrace(true, double.PositiveInfinity, 2.0 * m, recordPath ? path : Empty);
            }
            if (un > uMax) uMax = un;
            if (w > 0 && wn <= 0) passedPeriapsis = true;

            if (passedPeriapsis && un <= u0)
            {
                double f = (u - u0) / (u - un); // linear interpolation of the crossing u = u0
                double phiExit = phi + f * h;
                if (recordPath) path.Add((phiExit, startRadius));
                double defl = phiExit - System.Math.PI + 2.0 * System.Math.Asin(System.Math.Min(1.0, b / startRadius));
                return new GeodesicTrace(false, defl, 1.0 / uMax, recordPath ? path : Empty);
            }
            u = un; w = wn; phi = phin;
            if (recordPath) path.Add((phi, 1.0 / u));
        }
        return new GeodesicTrace(true, double.PositiveInfinity, 1.0 / uMax, recordPath ? path : Empty);
    }

    private static double Acc(double u, double m) => 3.0 * m * u * u - u;
}
