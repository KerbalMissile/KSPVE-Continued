using System;
using System.Globalization;
using KSPVE.Pure.Space.Tde;

// tde-info [horizonMetres]: the model's numbers and its light curve, for checking the physics without KSP.
internal static class TdeInfo
{
    public static int Run(string[] args)
    {
        double horizon = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 9.6e9;
        var p = new TdeParameters();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var tde = new TidalDisruption(p, horizon, 42);
        Console.WriteLine($"built in {watch.ElapsedMilliseconds} ms");
        Console.WriteLine($"hole {tde.HoleMassSuns:0.###e0} Suns, r_s/c = {tde.TimeUnit:0.0} s");
        Console.WriteLine($"star R = {tde.StarRadius:0.0000} r_s, q = {tde.MassRatio:0.###e0}, r_t = {tde.TidalRadius:0.00} r_s, r_p = {tde.Periapsis:0.00}, beta = {tde.Beta:0.00}");
        Console.WriteLine($"energy shape w {tde.EnergyShape:0.000}, stretch {tde.EnergyStretch:0.000}, target peak {tde.TargetPeakSeconds / 86400:0.0} d at {tde.TargetPeakRate / tde.EddingtonAccretionRate:0.0} Edd");
        Console.WriteLine($"core {tde.CoreRadius:0.000}, stripped {tde.StrippedFraction:0.000}, bound {tde.BoundFraction:0.000}");
        Console.WriteLine($"approach {tde.ApproachSeconds / 3600:0.0} h, t_min {tde.MostBoundPeriodSeconds / 86400:0.0} d, first return {tde.FirstReturnSeconds / 86400:0.0} d, peak {tde.PeakSeconds / 86400:0.0} d");
        Console.WriteLine($"advance {tde.PeriapsisAdvance * 180 / Math.PI:0.0} deg, r_circ {tde.CircularizationRadius:0.0}, L_Edd {tde.EddingtonLuminosity:0.##e0} W, Mdot_Edd {tde.EddingtonAccretionRate:0.##e0} kg/s");
        Console.WriteLine($"peak fallback {tde.PeakFallbackEddington:0.0} Edd, peak L {tde.PeakLuminosityWatts / tde.EddingtonLuminosity:0.00} L_Edd, end {tde.EndSeconds / 86400 / 365:0.0} yr");
        var frame = new TdeFrame();
        foreach (double days in new[] { -0.2, 0.01, 0.1, 1, 5, 20, 40, 50, 55, 60, 65, 70, 75, 80, 90, 100, 110, 120, 130, 140, 150, 175, 200, 300, 500, 1000, 3000, 10000 })
        {
            watch.Restart();
            tde.Evaluate(tde.ApproachSeconds + days * 86400.0, frame);
            long ms = watch.ElapsedMilliseconds;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,7:0.##} d {1,-10} verts {2,3} ext {3,7:0.0} Lum {4,7:0.###} fb {5,7:0.###} acc {6,7:0.###} shock {7} {8,6:0.0} env {9} R_ph {10:0.0} T_ph {11:0} disk {12} ({13} ms)",
                days, frame.Phase, frame.VertexCount, frame.Extent, frame.Luminosity, frame.FallbackEddington, frame.AccretionEddington,
                frame.ShockVisible ? "y" : "n", frame.ShockVisible ? frame.ShockPosition.Length : 0, frame.EnvelopeVisible ? "y" : "n",
                frame.PhotosphereMeters / horizon, frame.PhotosphereTemperature, frame.DiskFormation.ToString("0.00", CultureInfo.InvariantCulture) + (frame.JetAge > 0 ? " jets" : ""), ms));
        }
        return 0;
    }
}
