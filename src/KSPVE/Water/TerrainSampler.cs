using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSPVE.Core;

namespace KSPVE.Water;

/// <summary>A piece of work that reads the planet's terrain a little at a time on the main thread.</summary>
internal interface ITerrainJob
{
    /// <summary>Works for at most the time the stopwatch allows (it is already running; stop when its elapsed time passes <paramref name="limitMs"/>). True when finished.</summary>
    bool Step(Stopwatch watch, double limitMs);
}

/// <summary>
/// The planet's height function (<c>PQS.GetSurfaceHeight</c>) is only safe on the main thread (the terrain mods share state),
/// and each call costs microseconds to tens of them, so everything that needs many heights (the ocean mask, the hydrology's
/// terrain) queues a job here and is given the settings' milliseconds a frame between them, in order.
/// </summary>
internal static class TerrainSampler
{
    private static readonly List<ITerrainJob> Jobs = new List<ITerrainJob>();
    private static readonly Stopwatch Watch = new Stopwatch();

    public static int Pending => Jobs.Count;

    public static void Enqueue(ITerrainJob job) => Jobs.Add(job);

    public static void Cancel(ITerrainJob job) => Jobs.Remove(job);

    /// <summary>Called once a frame.</summary>
    public static void Update(double budgetMs)
    {
        if (Jobs.Count == 0) return;
        Watch.Restart();
        while (Jobs.Count > 0 && Watch.Elapsed.TotalMilliseconds < budgetMs)
        {
            ITerrainJob job = Jobs[0];
            bool done;
            try { done = job.Step(Watch, budgetMs); }
            catch (Exception ex)
            {
                Log.Exception("A terrain sampling job for the water failed (it is dropped)", ex);
                done = true;
            }
            if (done) Jobs.RemoveAt(0);
        }
    }

    /// <summary>
    /// The terrain's altitude above the sea (m) at a latitude and longitude (degrees), negative on the sea floor: KSP's own
    /// <c>TerrainAltitude</c>, which also serves bodies whose terrain is not currently built.
    /// </summary>
    public static double Altitude(CelestialBody body, double latitudeDegrees, double longitudeDegrees) => body.TerrainAltitude(latitudeDegrees, longitudeDegrees, true);

    /// <summary>The terrain's altitude above the sea (m) in a direction of the body's local (unrotated) frame.</summary>
    public static double Altitude(CelestialBody body, Vector3d radial)
    {
        PQS pqs = body.pqsController;
        return pqs == null ? 0.0 : pqs.GetSurfaceHeight(radial) - body.Radius;
    }
}
