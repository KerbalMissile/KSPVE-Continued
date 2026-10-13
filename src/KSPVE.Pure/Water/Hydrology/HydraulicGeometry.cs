using System;

namespace KSPVE.Pure.Water.Hydrology;

/// <summary>A river's cross-section and flow at one place.</summary>
public struct ChannelState
{
    /// <summary>Discharge (m3/s).</summary>
    public double Discharge;
    /// <summary>Surface width (m) and mean depth (m).</summary>
    public double Width, Depth;
    /// <summary>Mean flow speed (m/s).</summary>
    public double Speed;
    /// <summary>Froude number v / sqrt(g d): above about 0.8 the flow is broken white water.</summary>
    public double Froude;
    /// <summary>Slope of the bed (drop over run).</summary>
    public double Slope;
}

/// <summary>
/// How big a river is for the water it carries (hydraulic geometry, Leopold and Maddock 1953) and how fast it runs
/// for the slope it falls (Manning's equation), and how much water falls on the land.
/// </summary>
public static class HydraulicGeometry
{
    /// <summary>Width coefficient: w = a Q^0.5 (Q in m3/s, w in m). 6 gives 780 m at the Mississippi's 17,000 m3/s mean flow.</summary>
    public const double WidthCoefficient = 6.0;
    /// <summary>The exponent of width with discharge, 0.5 downstream (Leopold and Maddock).</summary>
    public const double WidthExponent = 0.5;
    /// <summary>Depth coefficient: d = a Q^0.4 (0.4 gives 20 m at the Mississippi's flow, 0.4 m for 1 m3/s).</summary>
    public const double DepthCoefficient = 0.4;
    /// <summary>The exponent of depth with discharge, about 0.4.</summary>
    public const double DepthExponent = 0.4;
    /// <summary>Manning's roughness n of a natural channel with some stones and weeds (s/m^(1/3)); Chow 1959: 0.030 to 0.050.</summary>
    public const double ManningN = 0.035;

    /// <summary>The surface width a river of this discharge has: 6 Q^0.5.</summary>
    public static double Width(double discharge) => WidthCoefficient * Math.Pow(Math.Max(discharge, 0.0), WidthExponent);

    /// <summary>The mean depth a river of this discharge has by hydraulic geometry alone: 0.4 Q^0.4.</summary>
    public static double GeometricDepth(double discharge) => DepthCoefficient * Math.Pow(Math.Max(discharge, 0.0), DepthExponent);

    /// <summary>
    /// The depth and speed the flow takes on a bed of a given slope: Manning's v = d^(2/3) sqrt(S) / n for a wide channel with
    /// the width from hydraulic geometry, with continuity Q = w d v, so d = (Q n / (w sqrt(S)))^(3/5). The slope is kept above
    /// 1e-5 (a lake's outflow still runs), and the speed within what real water does, 0.05 to 12 m/s.
    /// </summary>
    public static ChannelState At(double discharge, double slope, double gravity, double manning = ManningN)
    {
        double w = Width(discharge);
        double s = Math.Max(slope, 1e-5);
        double d = Math.Pow(Math.Max(discharge, 1e-9) * manning / (Math.Max(w, 1e-3) * Math.Sqrt(s)), 0.6);
        double v = discharge / Math.Max(w * d, 1e-6);
        v = Math.Min(Math.Max(v, 0.05), 12.0);
        d = discharge / Math.Max(w * v, 1e-6);
        return new ChannelState
        {
            Discharge = discharge, Width = w, Depth = d, Speed = v, Slope = slope,
            Froude = v / Math.Sqrt(gravity * Math.Max(d, 1e-3))
        };
    }

    /// <summary>
    /// Mean annual precipitation (mm/year) for the climate's humidity (0 to 1): 3000 h^2.2 follows Earth's zonal means,
    /// about 1800 mm at the wet equator (h 0.8), 1200 mm in the storm tracks (0.66), 600 mm at 0.48, a desert's 70 mm at 0.18.
    /// </summary>
    public static double AnnualPrecipitation(double humidity) => 3000.0 * Math.Pow(Math.Min(Math.Max(humidity, 0.0), 1.0), 2.2);

    /// <summary>
    /// Mean annual runoff (mm/year) from the precipitation <paramref name="precipitation"/> (mm/year) and the mean annual
    /// air temperature (C): precipitation minus evaporation, with Turc's evaporation P / sqrt(0.9 + (P / L)^2),
    /// L = 300 + 25 T + 0.05 T^3 (Turc 1954).
    /// </summary>
    public static double AnnualRunoff(double precipitation, double temperature)
    {
        double t = Math.Min(Math.Max(temperature, -10.0), 30.0);
        double l = 300.0 + 25.0 * t + 0.05 * t * t * t;
        double evaporation = precipitation / Math.Sqrt(0.9 + (precipitation / l) * (precipitation / l));
        return Math.Max(precipitation - evaporation, 0.0);
    }

    /// <summary>A rough mean annual temperature (C) at a latitude (rad) for an Earth-like climate: 33 cos^2(lat) - 8, from 25 C at the equator to -8 C at the poles.</summary>
    public static double MeanTemperature(double latitude) => 33.0 * Math.Pow(Math.Cos(latitude), 2.0) - 8.0;

    /// <summary>
    /// Evaporation from an open water surface (mm/year) at a latitude (rad): 1750 cos^2(lat) + 150, about 1900 mm in the tropics,
    /// 1000 mm at 45 degrees, 150 mm at the poles (the range of measured lake evaporation, Shuttleworth 1993).
    /// </summary>
    public static double LakeEvaporation(double latitude) => 1750.0 * Math.Pow(Math.Cos(latitude), 2.0) + 150.0;

    /// <summary>Millimetres of runoff per year over an area, as a discharge (m3/s).</summary>
    public static double DischargeFromRunoff(double runoffMmPerYear, double areaSquareMetres) => runoffMmPerYear * 1e-3 * areaSquareMetres / (365.25 * 86400.0);
}
