using System;
using System.Globalization;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Fluids;

/// <summary>
/// One body's atmosphere: a BODY node of KSPVE_FLUIDS. Everything has a default worked out from the effective temperature
/// (the kind of dwarf follows from it) and the body's own radius, gravity and rotation, so a config only names what it
/// wants to change.
/// </summary>
public sealed class FluidProfile
{
    public const double StefanBoltzmann = 5.670374419e-8;

    public string Body = "";
    public bool Enabled = true;
    public FluidKind Kind = FluidKind.L;
    /// <summary>True when the config named the kind (otherwise it follows the temperature).</summary>
    public bool KindSet;
    /// <summary>Effective temperature, K (for a hot Jupiter the day-side equilibrium temperature).</summary>
    public double Temperature = 1500.0;
    /// <summary>The rotation period, h, to use instead of the body's (0 = the body's own; brown dwarfs spin in 1.5 to 10 h).</summary>
    public double RotationPeriodHours;
    /// <summary>Spectral truncation of the flow: the sphere is resolved down to roughly 2 pi a / K.</summary>
    public int Resolution = 42;
    /// <summary>Longitudes of the cloud grid (it has half as many latitudes).</summary>
    public int TracerLongitudes = 256;
    /// <summary>Multiplies the power of the convective stirring (1 = the estimate from the heat flux).</summary>
    public double Forcing = 1.0;
    /// <summary>Multiplies the temperature anomalies that make and clear the clouds (1 = the estimate).</summary>
    public double Patchiness = 1.0;
    /// <summary>The random seed (0 = from the body's name).</summary>
    public ulong Seed;
    /// <summary>Surface gravity, m/s^2 (0 = from the body, else a brown dwarf's 1000).</summary>
    public double Gravity;
    /// <summary>The flow's drag time, s (0 = from the temperature).</summary>
    public double DragTime;
    /// <summary>A tidally locked body has a fixed day side (a hot Jupiter always does).</summary>
    public bool TidallyLocked;
    /// <summary>Hot Jupiter: the eastward equatorial jet, m/s (0 = from the temperature).</summary>
    public double JetSpeed;
    /// <summary>The star lights the clouds (reflected light) for the giants; a dwarf is lit by its own heat.</summary>
    public bool Reflect = true;

    public FluidProfile Clone() => (FluidProfile)MemberwiseClone();

    /// <summary>
    /// Reads a BODY (or DEFAULT) node over <paramref name="baseline"/>. Keys: body, enabled, type (L, T, Y, giant,
    /// hotjupiter), temperature, rotationPeriodHours, resolution, tracerResolution, forcing, patchiness, seed, gravity,
    /// dragTimeDays, tidallyLocked, jetSpeed, reflect.
    /// </summary>
    public static FluidProfile From(CfgNode node, FluidProfile baseline)
    {
        FluidProfile p = baseline.Clone();
        string v;
        if ((v = node.GetLast("body")) != null) p.Body = v.Trim();
        if ((v = node.GetLast("type")) != null && TryParseKind(v, out FluidKind kind)) { p.Kind = kind; p.KindSet = true; }
        Flag(node, "enabled", ref p.Enabled);
        Flag(node, "tidallyLocked", ref p.TidallyLocked);
        Flag(node, "reflect", ref p.Reflect);
        Num(node, "temperature", ref p.Temperature);
        Num(node, "rotationPeriodHours", ref p.RotationPeriodHours);
        Num(node, "forcing", ref p.Forcing);
        Num(node, "patchiness", ref p.Patchiness);
        Num(node, "gravity", ref p.Gravity);
        Num(node, "jetSpeed", ref p.JetSpeed);
        double days = double.NaN, res = double.NaN, tres = double.NaN, seed = double.NaN;
        Num(node, "dragTimeDays", ref days);
        Num(node, "resolution", ref res);
        Num(node, "tracerResolution", ref tres);
        Num(node, "seed", ref seed);
        if (!double.IsNaN(days) && days > 0.0) p.DragTime = days * 86400.0;
        if (!double.IsNaN(res)) p.Resolution = (int)res;
        if (!double.IsNaN(tres)) p.TracerLongitudes = (int)tres;
        if (!double.IsNaN(seed)) p.Seed = (ulong)Math.Max(0.0, seed);
        p.Temperature = Math.Max(Math.Min(p.Temperature, 3500.0), 60.0);
        p.Resolution = Math.Max(12, Math.Min(p.Resolution, 106));
        int lon = 64;
        while (lon < p.TracerLongitudes && lon < 1024) lon <<= 1;
        p.TracerLongitudes = lon;
        p.Forcing = Math.Max(0.0, p.Forcing);
        p.Patchiness = Math.Max(0.0, p.Patchiness);
        if (!p.KindSet) p.Kind = p.TidallyLocked && p.Temperature >= 700.0 ? FluidKind.HotJupiter : Decks.KindForTemperature(p.Temperature);
        return p;
    }

    public static bool TryParseKind(string text, out FluidKind kind)
    {
        text = text.Trim().Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
        switch (text)
        {
            case "l": case "ldwarf": kind = FluidKind.L; return true;
            case "t": case "tdwarf": kind = FluidKind.T; return true;
            case "y": case "ydwarf": kind = FluidKind.Y; return true;
            case "giant": case "gasgiant": case "jupiter": kind = FluidKind.Giant; return true;
            case "hotjupiter": case "hot": kind = FluidKind.HotJupiter; return true;
        }
        kind = FluidKind.L;
        return false;
    }

    private static void Num(CfgNode node, string key, ref double target)
    {
        string text = node.GetLast(key);
        if (text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v))
            target = v;
    }

    private static void Flag(CfgNode node, string key, ref bool target)
    {
        string text = node.GetLast(key);
        if (text == null) return;
        text = text.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1") target = true;
        else if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0") target = false;
    }
}

/// <summary>The body-dependent numbers the atmosphere simulation is built from, with where each comes from.</summary>
public static class FluidPhysics
{
    /// <summary>
    /// The fraction of the internal heat flux that ends up as kinetic energy of the weather layer. Showman and Kaspi (2013)
    /// and Zhang and Showman (2014) force their brown-dwarf and giant-planet models with 1e-4 to 1e-3 of the flux.
    /// </summary>
    public const double HeatEngineEfficiency = 3.0e-4;
    /// <summary>The pressure the weather layer's mass is counted down to, Pa (10 bar).</summary>
    public const double WeatherLayerPressure = 1.0e6;

    /// <summary>Gravity of a brown dwarf of about 30 Jupiter masses and a Jupiter radius, m/s^2 (log g = 5 in cgs).</summary>
    public const double DwarfGravity = 1000.0;

    /// <summary>
    /// The power the convection puts into the weather layer, W/kg: the heat flux sigma T^4 times the efficiency, spread over
    /// the layer's mass per area p / g. For an L dwarf at 1500 K that is 0.09 W/kg.
    /// </summary>
    public static double StirringPower(double kelvin, double gravity)
        => HeatEngineEfficiency * FluidProfile.StefanBoltzmann * Math.Pow(kelvin, 4.0) * gravity / WeatherLayerPressure;

    /// <summary>
    /// The damping time of the flow, s: the radiative time of the deep layers the flow lives in. The radiative time at the 2 bar
    /// level, c_p p / (4 g sigma T^3), grows roughly as p^2 in an optically thick atmosphere, and the deep flow is damped over
    /// several of them: about three million seconds at 1500 K, growing as T^-3. That is inside the 1e5 to 1e7 s range Showman
    /// and Kaspi (2013) and Zhang and Showman (2014) explore, and in the regime where they find strong zonal jets (weaker drag
    /// than about 1e6 s leaves the flow eddy-dominated); the scale factor here was chosen so that, as in their runs, roughly
    /// half the kinetic energy ends up in the zonal jets.
    /// </summary>
    public static double DragTime(double kelvin, double gravity)
    {
        const double cp = 1.3e4;   // H2/He at these temperatures, J/(kg K)
        double t = cp * WeatherLayerPressure / (4.0 * gravity * FluidProfile.StefanBoltzmann * Math.Pow(kelvin, 3.0));
        return Math.Max(5.0e5, Math.Min(t * 175.0, 3.0e7));
    }

    /// <summary>The radiative time of the cloud-forming layers (1 to 3 bar), s: where the temperature anomalies relax.</summary>
    public static double RadiativeTime(double kelvin, double gravity)
    {
        const double cp = 1.3e4;
        double t = cp * 2.0e5 / (4.0 * gravity * FluidProfile.StefanBoltzmann * Math.Pow(kelvin, 3.0));
        return Math.Max(3.0e3, Math.Min(t, 5.0e6));
    }

    /// <summary>The root-mean-square temperature anomaly the stirring leaves in the cloud layers, K: a few percent of T (tens of K, Zhang and Showman 2014; here 2.5%).</summary>
    public static double ThetaRms(double kelvin) => Math.Max(2.0, Math.Min(0.025 * kelvin, 50.0));

    /// <summary>
    /// The Rhines wavenumber l_beta = a sqrt(beta / U) with beta = 2 Omega / a: zonal jets of about this many wavelengths round the
    /// sphere form when the stirring is at smaller scales (Rhines 1975). The number of jets is of order l_beta.
    /// </summary>
    public static double RhinesDegree(double radius, double omega, double speed)
        => radius * Math.Sqrt(2.0 * Math.Abs(omega) / radius / Math.Max(speed, 1e-3));
}
