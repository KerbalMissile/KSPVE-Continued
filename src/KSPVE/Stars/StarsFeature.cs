using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using KSPVE.Pure.Stars;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Stars;

/// <summary>
/// Star activity: sunspots, faculae, prominences and coronal loops, the corona and CMEs on every star, a solar wind with
/// real CMEs in it for the solar wind model (it sets <see cref="SpaceWeather.Source"/>), and the settings for both. The
/// activity itself is <see cref="StarActivity"/>: stateless, from the game time and a seed.
/// </summary>
internal sealed class StarsFeature : Feature
{
    private const string ScaledCameraName = "Camera ScaledSpace";
    private static Camera[] _cameras = new Camera[32];

    private float _referenceChecked = -100f;
    private float _readoutAt = -100f;
    private string[] _readout = new string[0];

    public override string Name => "Stars";
    public override int Order => 50;
    public override string SettingsTitle => "Stars: activity, prominences, corona, CMEs";

    public override void Load(List<ConfigIssue> issues)
    {
        StarWorld.Load(issues);
    }

    public override void Update(bool allowed)
    {
        if (Time.realtimeSinceStartup - _referenceChecked > 5f)
        {
            _referenceChecked = Time.realtimeSinceStartup;
            try { StarWorld.UpdateReference(); }
            catch (Exception ex) { Log.Exception("Setting the wind's reference distance failed", ex); }
        }
        AttachRenderer(allowed && StarSettings.Enabled);
    }

    private static void AttachRenderer(bool enabled)
    {
        int count = Camera.allCamerasCount;
        if (_cameras.Length < count) _cameras = new Camera[count * 2];
        count = Camera.GetAllCameras(_cameras);
        for (int i = 0; i < count; i++)
        {
            Camera cam = _cameras[i];
            if (cam == null || cam.name != ScaledCameraName) continue;
            var renderer = cam.GetComponent<StarRenderer>();
            if (renderer == null)
            {
                if (!enabled) return;
                renderer = cam.gameObject.AddComponent<StarRenderer>();
                Log.Info("Star renderer attached to " + cam.name + ".");
            }
            if (renderer.enabled != enabled) renderer.enabled = enabled;
            return;
        }
    }

    public override CfgNode UserNode() => StarSettings.ToNode();

    public override void ResetToDefaults()
    {
        StarSettings.Reset();
        // Back to what the configs say.
        if (GameDatabase.Instance != null)
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(StarWorld.NodeName)) StarSettings.Read(KspConfig.ToCfg(node));
    }

    // ---------------------------------------------------------------- settings window

    private static readonly string[] ViewNames = { "Eye (realistic)", "H-alpha telescope", "Extreme UV" };

    public override void DrawSettings()
    {
        StarSettings.Enabled = Gui.Toggle("Star effects", StarSettings.Enabled);
        if (!StarSettings.Enabled) return;

        // The view is a display setting. Only "Eye" is realistic: in white light prominences and loops are about a
        // ten-thousandth of the disc and the corona a millionth, visible only when something eclipses the disc.
        int view = Gui.Choice("View of the star's magnetic structures", (int)StarSettings.View, ViewNames, 3);
        if (view >= 0 && view < ViewNames.Length) StarSettings.View = (StarView)view;
        Gui.Note(StarSettings.View == StarView.Eye
            ? "Realistic: the corona is a millionth of the disc and shows only in an eclipse."
            : StarSettings.View == StarView.HAlpha
                ? "A hydrogen-alpha telescope's view (656.3 nm): prominences and loops glow red-orange, filaments are dark on the disc. Not what the eye sees."
                : "An extreme-ultraviolet telescope's view (17.1 nm, shown in gold): hot coronal loops over a dim disc. Not what the eye sees.");
        StarSettings.Visibility = Gui.Slider("Magnetic structures visibility", StarSettings.Visibility, 0.0, 4.0);
        StarSettings.SurfaceDetail = Gui.Slider("Granulation contrast (1 = real)", StarSettings.SurfaceDetail, 0.0, 4.0);
        StarSettings.LoopSize = Gui.Slider("Prominence and loop size (1 = real)", StarSettings.LoopSize, 0.5, 5.0);
        StarSettings.ReplaceSurface = Gui.Toggle("Draw the photosphere over the game's star surface", StarSettings.ReplaceSurface);
        StarSettings.Spots = Gui.Toggle("Sunspots and faculae", StarSettings.Spots);
        StarSettings.Prominences = Gui.Toggle("Prominences, filaments and loops", StarSettings.Prominences);
        StarSettings.Corona = Gui.Toggle("Corona", StarSettings.Corona);
        StarSettings.Cmes = Gui.Toggle("Coronal mass ejections", StarSettings.Cmes);

        Gui.Header("The star now");
        if (FlightGlobals.fetch == null || FlightGlobals.Bodies == null || Planetarium.fetch == null)
        {
            Gui.Note("Available once a game is loaded.");
            return;
        }
        CelestialBody body = FlightGlobals.currentMainBody ?? FlightGlobals.GetHomeBody();
        CelestialBody star = StarWorld.StarOf(body);
        StarEntry entry = StarWorld.Entry(star);
        if (entry == null)
        {
            Gui.Note("No star found.");
            return;
        }
        if (Time.realtimeSinceStartup - _readoutAt > 0.5f)
        {
            _readoutAt = Time.realtimeSinceStartup;
            try { _readout = Readout(entry, body, star); }
            catch (Exception ex) { _readout = new[] { "Could not work out the star's state: " + ex.Message }; }
        }
        foreach (string line in _readout) GUILayout.Label(line);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Launch a CME now (towards " + body.bodyDisplayName.Replace("^N", "") + ")")) LaunchTest(entry, body, star);
        if (GUILayout.Button("Clear test CMEs", GUILayout.Width(130))) entry.Activity.ClearInjected();
        GUILayout.EndHorizontal();
    }

    private string[] Readout(StarEntry entry, CelestialBody body, CelestialBody star)
    {
        var lines = new List<string>();
        double ut = Planetarium.GetUniversalTime();
        StarActivity act = entry.Activity;
        double phase = act.Phase(ut);
        double level = act.Level(ut);
        lines.Add($"{star.bodyName}: {entry.Profile.Temperature:0} K, cycle {act.CycleIndex(ut)} at {phase * 100.0:0}% ({PhaseName(phase)}) of {entry.Profile.CyclePeriod / StarProfile.Year:0.#} years, activity {level:0.00}");
        List<ActiveRegion> regions = act.RegionsAt(ut);
        List<StarFlare> flares = act.FlaresBetween(ut - 86400.0, ut);
        string biggest = "none";
        double flux = 0.0;
        foreach (StarFlare f in flares) if (f.Flux > flux) { flux = f.Flux; biggest = f.Class; }
        lines.Add($"{regions.Count} active regions, {flares.Count} flares in the last day (biggest {biggest})");

        Double3 position = StarWorld.ToModel(star, body.position - star.position);
        double distance = position.Length;
        SolarWindState wind = StarWorld.WindAt(body, ut, out _);
        lines.Add($"Solar wind at {body.bodyName} ({distance / 1.495978707e11:0.00} AU): {wind.Speed / 1e3:0} km/s, {wind.Density / 1e6:0.0} /cm3, B {wind.FieldStrength * 1e9:0.0} nT (north {wind.FieldNorth * 1e9:+0.0;-0.0} nT), particles x{wind.EnergeticParticles:0}{(wind.InCme > 0.5 ? ", INSIDE A CME" : "")}");

        StarWind sw = entry.Wind;
        List<CmeForecast> incoming = sw.Incoming(position, ut, 30.0 * 86400.0);
        if (incoming.Count > 0)
        {
            CmeForecast f = incoming[0];
            string state = f.HasArrived(ut) ? "passing now, ends in " + Span(f.Departure - ut) : "arrives in " + Span(f.Arrival - ut);
            lines.Add($"CME heading for {body.bodyName}: {f.Cme.Source} launched {Span(ut - f.Cme.LaunchTime)} ago at {f.Cme.LaunchSpeed / 1e3:0} km/s, {f.Cme.HalfWidth * 2.0 * 180.0 / Math.PI:0} deg wide, {state} ({f.Speed / 1e3:0} km/s at the shock; expected storm: {f.StormClass})");
        }
        else lines.Add($"No CME on its way to {body.bodyName} within 30 days.");

        List<Cme> next = act.CmesBetween(ut, ut + 20.0 * 86400.0);
        Cme upcoming = null;
        foreach (Cme c in next) if (c.LaunchTime > ut) { upcoming = c; break; }
        if (upcoming != null)
        {
            CmeForecast f = sw.Forecast(upcoming, position);
            string arrival = double.IsNaN(f.Arrival) ? "it misses " + body.bodyName : "arrives at " + body.bodyName + " " + Span(f.Arrival - ut) + " from now";
            lines.Add($"Next CME: in {Span(upcoming.LaunchTime - ut)} from {upcoming.Source}, {upcoming.LaunchSpeed / 1e3:0} km/s, {upcoming.HalfWidth * 2.0 * 180.0 / Math.PI:0} deg wide; {arrival}");
        }
        else lines.Add("Next CME: none in the next 20 days.");
        return lines.ToArray();
    }

    private static string PhaseName(double phase) =>
        phase < 0.08 ? "minimum" : phase < 0.28 ? "rising" : phase < 0.46 ? "maximum" : phase < 0.9 ? "declining" : "approaching minimum";

    private static string Span(double seconds)
    {
        seconds = Math.Max(seconds, 0.0);
        double days = Math.Floor(seconds / 86400.0), hours = Math.Floor((seconds - days * 86400.0) / 3600.0);
        if (days >= 1.0) return days.ToString("0", CultureInfo.InvariantCulture) + " d " + hours.ToString("0", CultureInfo.InvariantCulture) + " h";
        double minutes = Math.Floor((seconds - hours * 3600.0) / 60.0);
        return hours >= 1.0 ? hours.ToString("0", CultureInfo.InvariantCulture) + " h " + minutes.ToString("0", CultureInfo.InvariantCulture) + " min" : minutes.ToString("0", CultureInfo.InvariantCulture) + " min";
    }

    /// <summary>
    /// A test CME launched now straight at a body: 1500 km/s, 70 degrees wide (a fast halo-ish event), with a southward
    /// leaning flux rope so that it drives a storm. It is added to the star's activity model like any other, so the
    /// wind and the star's picture both see it.
    /// </summary>
    private static void LaunchTest(StarEntry entry, CelestialBody body, CelestialBody star)
    {
        double ut = Planetarium.GetUniversalTime();
        Double3 p = StarWorld.ToModel(star, body.position - star.position);
        if (p.Length < 1.0) return;
        Double3 d = p.Normalized();
        var cme = new Cme
        {
            Id = 2_000_000_000L + (long)(ut % 1e7),
            LaunchTime = ut,
            Latitude = Math.Asin(Math.Max(-1.0, Math.Min(1.0, d.Y))),
            Longitude = Math.Atan2(d.Z, d.X),
            LaunchSpeed = 1.5e6,
            HalfWidth = 35.0 * Math.PI / 180.0,
            Drag = entry.Profile.Drag,
            WindSpeed = entry.Profile.WindSpeed,
            StarRadius = star.Radius > 1e6 ? Math.Min(star.Radius, 5e10) : Cme.SolarRadius,
            FlareFlux = 2e-4,
            Helicity = 1.0,
            AxisAngle = -80.0 * Math.PI / 180.0,
            Impact = 0.1,
            CloudField = 2.0e-8,
            Source = "Test launch"
        };
        entry.Activity.Inject(cme);
        Log.Info($"Test CME launched from {star.bodyName} towards {body.bodyName}.");
    }
}
