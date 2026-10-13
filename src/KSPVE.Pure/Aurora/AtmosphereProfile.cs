using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Aurora;

/// <summary>What an atmosphere is made of, which sets which lines the aurora and airglow emit.</summary>
public enum Composition
{
    /// <summary>Nitrogen and oxygen (Earth, Kerbin): N2+ blue-violet, green and red oxygen, pink N2 first positive.</summary>
    N2O2,
    /// <summary>Carbon dioxide (Venus, Mars): green and red oxygen, CO2+ violet, little else.</summary>
    CO2,
    /// <summary>Hydrogen and helium (gas giants): H-alpha and H2 bands, pink to red.</summary>
    H2He,
    /// <summary>Nitrogen and methane (Titan): no aurora is drawn there; airglow only.</summary>
    N2CH4,
}

/// <summary>
/// How the air's pressure falls with height above a body's surface: the table of a body's pressure curve, extended above
/// its top by the exponential of its last scale height (KSP's curves end where the air does, but aurora and airglow
/// glow at pressures far below that, so heights are found by pressure level instead of a fixed number of kilometres).
/// </summary>
public sealed class AtmosphereProfile
{
    private readonly double[] _height;
    private readonly double[] _lnPressure;
    private readonly double _topScaleHeight;

    /// <summary>The body's surface gravity (m/s^2) and radius (m); gravity falls as the inverse square with height.</summary>
    public double SurfaceGravity { get; }
    public double Radius { get; }
    /// <summary>Mean molar mass of the air (kg/mol).</summary>
    public double MolarMass { get; }
    /// <summary>The exospheric temperature (K): about 1000 K for Earth, 800 K for Jupiter's upper atmosphere.</summary>
    public double ExosphereTemperature { get; }
    /// <summary>Temperature of the lower thermosphere and mesosphere (K), about 190 K at Earth's 100 km.</summary>
    public double MesosphereTemperature { get; }

    /// <summary>
    /// A profile from sample points (metres above the surface, pressure in Pa), ascending in height. Pressures at or below
    /// zero (a curve that ends in zero) are taken as the tiny value below which the table ends.
    /// </summary>
    public AtmosphereProfile(IList<double> heights, IList<double> pressures, double radius, double surfaceGravity, double molarMass,
        double exosphereTemperature = 1000.0, double mesosphereTemperature = 190.0)
    {
        if (heights.Count < 2 || heights.Count != pressures.Count) throw new ArgumentException("A profile needs at least two samples.");
        var h = new List<double>();
        var lp = new List<double>();
        for (int i = 0; i < heights.Count; i++)
        {
            double p = pressures[i];
            if (!(p > 1e-12)) { if (i == 0) p = 1e-12; else break; }
            double lnp = Math.Log(p);
            // Keep it strictly decreasing so pressure can be inverted to a height.
            if (lp.Count > 0 && lnp >= lp[lp.Count - 1] - 1e-9) continue;
            h.Add(heights[i]);
            lp.Add(lnp);
        }
        if (h.Count < 2) { h = new List<double> { heights[0], heights[0] + 10000.0 }; lp = new List<double> { Math.Log(Math.Max(pressures[0], 1e-6)), Math.Log(Math.Max(pressures[0], 1e-6)) - 1.0 }; }
        _height = h.ToArray();
        _lnPressure = lp.ToArray();
        int n = _height.Length;
        // The scale height of the top segment (a few samples, so one coarse step cannot make it silly).
        int from = Math.Max(0, n - 4);
        double dz = _height[n - 1] - _height[from], dl = _lnPressure[from] - _lnPressure[n - 1];
        _topScaleHeight = Math.Max(dl > 1e-9 ? dz / dl : 8000.0, 200.0);
        Radius = radius;
        SurfaceGravity = surfaceGravity;
        MolarMass = molarMass;
        ExosphereTemperature = exosphereTemperature;
        MesosphereTemperature = mesosphereTemperature;
    }

    /// <summary>A single-scale-height atmosphere of <paramref name="surfacePressure"/> Pa (for tests and bodies with no table).</summary>
    public static AtmosphereProfile Isothermal(double surfacePressure, double scaleHeight, double radius, double surfaceGravity, double molarMass,
        double exosphereTemperature = 1000.0)
    {
        var heights = new List<double>();
        var pressures = new List<double>();
        for (int i = 0; i <= 64; i++)
        {
            double z = i * scaleHeight * 0.25;
            heights.Add(z);
            pressures.Add(surfacePressure * Math.Exp(-z / scaleHeight));
        }
        return new AtmosphereProfile(heights, pressures, radius, surfaceGravity, molarMass, exosphereTemperature);
    }

    /// <summary>
    /// Earth's real atmosphere (US Standard Atmosphere 1976 and the NRLMSISE-00 mean thermosphere), for the real Solar System
    /// bodies and the tests.
    /// </summary>
    public static AtmosphereProfile Earth()
    {
        double[] z = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 150, 200, 250, 300, 400, 500, 700, 1000 };
        double[] p = { 101325, 26500, 5529, 1197, 287, 79.8, 22.0, 5.22, 1.052, 0.1836, 0.03201, 0.00710, 0.00254, 0.001255, 4.5e-4, 8.47e-5, 2.4e-5, 8.77e-6, 1.45e-6, 3.0e-7, 2.5e-8, 7.5e-10 };
        var heights = new List<double>();
        for (int i = 0; i < z.Length; i++) heights.Add(z[i] * 1000.0);
        return new AtmosphereProfile(heights, p, 6371010.0, 9.80665, 0.0289644, 1000.0, 190.0);
    }

    /// <summary>Pressure (Pa) at <paramref name="height"/> metres above the surface.</summary>
    public double PressureAt(double height)
    {
        int n = _height.Length;
        if (height <= _height[0]) return Math.Exp(_lnPressure[0]);
        if (height >= _height[n - 1]) return Math.Exp(_lnPressure[n - 1] - (height - _height[n - 1]) / _topScaleHeight);
        int i = Array.BinarySearch(_height, height);
        if (i < 0) i = ~i - 1;
        double t = (height - _height[i]) / (_height[i + 1] - _height[i]);
        return Math.Exp(_lnPressure[i] + (_lnPressure[i + 1] - _lnPressure[i]) * t);
    }

    /// <summary>The height (m) at which the pressure is <paramref name="pressure"/> Pa (negative below the surface level if lower down).</summary>
    public double HeightOf(double pressure)
    {
        double lp = Math.Log(Math.Max(pressure, 1e-300));
        int n = _height.Length;
        if (lp >= _lnPressure[0]) return _height[0] - (lp - _lnPressure[0]) * ScaleHeightAt(_height[0]);
        if (lp <= _lnPressure[n - 1]) return _height[n - 1] + (_lnPressure[n - 1] - lp) * _topScaleHeight;
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_lnPressure[mid] > lp) lo = mid; else hi = mid;
        }
        double t = (_lnPressure[lo] - lp) / (_lnPressure[lo] - _lnPressure[hi]);
        return _height[lo] + (_height[hi] - _height[lo]) * t;
    }

    /// <summary>The pressure scale height -dz/dln(p) (m) at a height.</summary>
    public double ScaleHeightAt(double height)
    {
        int n = _height.Length;
        if (height >= _height[n - 1]) return _topScaleHeight;
        double a = Math.Max(_height[0], height - 0.5 * Math.Max(1.0, _topScaleHeight * 0.2)), b = a + Math.Max(1.0, _topScaleHeight * 0.4);
        double dl = Math.Log(PressureAt(a)) - Math.Log(PressureAt(Math.Min(b, _height[n - 1] + 1.0)));
        double dz = Math.Min(b, _height[n - 1] + 1.0) - a;
        return dl > 1e-12 ? Math.Max(dz / dl, 100.0) : _topScaleHeight;
    }

    /// <summary>Gravity (m/s^2) at a height above the surface.</summary>
    public double GravityAt(double height)
    {
        double f = Radius / (Radius + Math.Max(height, 0.0));
        return SurfaceGravity * f * f;
    }

    /// <summary>
    /// Temperature (K) at a pressure level: the mesosphere's below about 0.03 Pa (100 km at Earth), rising through the
    /// thermosphere to the exospheric temperature by about 10^-4 Pa. A smooth stand-in for a body's real profile, enough to
    /// set where collisions quench a line.
    /// </summary>
    public double TemperatureAt(double pressure)
    {
        double x = Math.Log10(Math.Max(pressure, 1e-30));
        double t = Math.Min(Math.Max((-1.5 - x) / 2.5, 0.0), 1.0);
        double s = t * t * (3.0 - 2.0 * t);
        return MesosphereTemperature + (ExosphereTemperature - MesosphereTemperature) * s;
    }

    /// <summary>Mass density (kg/m^3) at a height, from the pressure and the scale height (hydrostatic: rho = p / (g H)).</summary>
    public double DensityAt(double height) => PressureAt(height) / (GravityAt(height) * ScaleHeightAt(height));
}
