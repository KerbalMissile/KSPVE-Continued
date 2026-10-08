using System.Globalization;
using System.IO;
using System.Text;
using KSPVE.Core;
using KSPVE.Pure;

namespace KSPVE.Dev;

/// <summary>
/// Writes every celestial body's parent, gravity, orbit and rotation (as KSP holds them after all planet-pack patches) to
/// PluginData/bodies.tsv, for building Principia starting states offline. Enabled with -kspve-dump-bodies; runs once,
/// as soon as the planetary system exists (before Principia takes over any orbits).
/// </summary>
internal static class BodyDump
{
    private static bool _done;

    public static void TryWrite(LaunchOptions options)
    {
        if (_done || options == null || !options.DumpBodies) return;
        if (FlightGlobals.fetch == null || FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
        _done = true;
        try
        {
            Write();
        }
        catch (System.Exception ex)
        {
            Log.Exception("Writing the body dump failed", ex);
        }
    }

    private static void Write()
    {
        var sb = new StringBuilder();
        sb.AppendLine("name\tparent\tgravParameter_m3s2\tradius_m\tsemiMajorAxis_m\teccentricity\tinclination_deg\tLAN_deg\targumentOfPeriapsis_deg\tmeanAnomalyAtEpoch_rad\tepoch_s\tUT_s\trotationPeriod_s\trotates\ttidallyLocked\tinitialRotation_deg");
        double ut = Planetarium.GetUniversalTime();
        foreach (CelestialBody body in FlightGlobals.Bodies)
        {
            if (body == null) continue;
            Orbit o = body.orbit;
            string parent = o?.referenceBody != null ? o.referenceBody.bodyName : string.Empty;
            sb.Append(body.bodyName).Append('\t').Append(parent).Append('\t')
              .Append(R(body.gravParameter)).Append('\t').Append(R(body.Radius)).Append('\t');
            if (o == null || o.referenceBody == null || o.referenceBody == body)
                sb.Append("\t\t\t\t\t\t\t"); // seven empty orbit columns
            else
                sb.Append(R(o.semiMajorAxis)).Append('\t').Append(R(o.eccentricity)).Append('\t').Append(R(o.inclination)).Append('\t')
                  .Append(R(o.LAN)).Append('\t').Append(R(o.argumentOfPeriapsis)).Append('\t').Append(R(o.meanAnomalyAtEpoch)).Append('\t')
                  .Append(R(o.epoch)).Append('\t');
            sb.Append(R(ut)).Append('\t').Append(R(body.rotationPeriod)).Append('\t').Append(body.rotates ? '1' : '0').Append('\t')
              .Append(body.tidallyLocked ? '1' : '0').Append('\t').Append(R(body.initialRotation)).AppendLine();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.BodyDumpPath));
        File.WriteAllText(Paths.BodyDumpPath, sb.ToString());
        Log.Info($"Wrote {FlightGlobals.Bodies.Count} bodies to {Paths.BodyDumpPath}.");
    }

    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
