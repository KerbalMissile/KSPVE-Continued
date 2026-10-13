using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Seasons;
using KSPVE.UI;
using UnityEngine;

namespace KSPVE.Seasonal;

/// <summary>
/// Seasons, climate, snow and wind. Each body's real axial tilt and orbit give the sun's latitude through the year (the
/// clouds' weather follows it); a one dimensional energy balance model gives temperatures by latitude and season; snow
/// and ice follow from the temperatures and the weather's precipitation, and are shown on the scaled planets and on the
/// local terrain; the weather's wind can push craft (through Ferram Aerospace Research).
/// </summary>
internal sealed class ClimateFeature : Feature
{
    private string _browsed;
    private string _stat = string.Empty;
    private int _statVersion = -1;
    private string _statBody;

    public override string Name => "Climate";
    public override int Order => -50;   // after the clouds (which own the weather), before the effects that draw it
    public override string SettingsTitle => "Seasons, snow & wind";

    public override void Load(List<ConfigIssue> issues)
    {
        ClimateConfig.Load(issues);
        BodySeasons.Install();
    }

    public override void Update(bool allowed)
    {
        if (Planetarium.fetch != null && FlightGlobals.Bodies != null) SnowCover.Tick();
        ScaledSnow.Update(allowed);
        LocalSnow.Update(allowed);
        WindForces.Update();
    }

    public override CfgNode UserNode() => ClimateConfig.ToUserNode();

    public override void ResetToDefaults() => ClimateConfig.ResetToDefaults();

    public override void Shutdown()
    {
        ScaledSnow.RestoreAll();
        WindForces.Shutdown();
        SnowCover.Forget();
        BodySeasons.Uninstall();
    }

    public override void DrawSettings()
    {
        DrawBody();

        Gui.Header("Snow");
        ClimateConfig.ScaledSnow = Gui.Toggle("Snow, glaciers and sea ice on planets seen from space", ClimateConfig.ScaledSnow);
        ClimateConfig.LocalSnow = Gui.Toggle("Snow on the terrain around the craft", ClimateConfig.LocalSnow);
        if (ClimateConfig.LocalSnow)
            ClimateConfig.KeepVehiclesClear = Gui.Toggle("  Keep craft and kerbals clear of snow (draws the terrain a second time)", ClimateConfig.KeepVehiclesClear);
        ClimateConfig.SnowAlbedo = Gui.Slider("Snow whiteness (albedo; fresh snow is 0.8 to 0.9)", ClimateConfig.SnowAlbedo, 0.5, 1.0);
        ClimateConfig.SnowSparkle = Gui.Slider("Snow sparkle (display only; 0 is none)", ClimateConfig.SnowSparkle, 0.0, 1.0);
        int[] sizes = { 1024, 2048, 4096, 8192 };
        string[] names = { "1024", "2048", "4096", "8192" };
        int index = Array.IndexOf(sizes, ClimateConfig.ScaledResolution);
        int picked = Gui.Choice("Largest scaled-planet texture with snow (texels wide)", index < 0 ? 2 : index, names, 4);
        if (picked != index && picked >= 0) ClimateConfig.ScaledResolution = sizes[picked];
        ClimateConfig.ScaledBodies = Gui.IntSlider("  Planets with snow textures at once (nearest first)", ClimateConfig.ScaledBodies, 1, 8);
        ClimateConfig.RecentWindowDays = Gui.Slider("Recent snowfall looked back over (days of the planet)", ClimateConfig.RecentWindowDays, 0.0, 30.0, "0.0");

        Gui.Header("Wind");
        ClimateConfig.WindPush = Gui.Toggle("Wind pushes craft (gameplay; needs Ferram Aerospace Research)", ClimateConfig.WindPush);
        Gui.Note("Wind: " + WindForces.Status + ".");
    }

    private void DrawBody()
    {
        var bodies = new List<CelestialBody>();
        if (FlightGlobals.Bodies != null)
            foreach (CelestialBody b in FlightGlobals.Bodies)
                if (b != null && !CloudWorld.IsStar(b) && b.referenceBody != null && b.referenceBody != b) bodies.Add(b);
        if (bodies.Count == 0 || Planetarium.fetch == null)
        {
            Gui.Note("Seasons are shown once a game is loaded.");
            return;
        }
        CelestialBody focus = null;
        foreach (CelestialBody b in bodies) if (b.bodyName == _browsed) focus = b;
        if (focus == null) focus = FlightGlobals.currentMainBody != null && bodies.Contains(FlightGlobals.currentMainBody) ? FlightGlobals.currentMainBody : Planetarium.fetch.Home;
        if (focus == null) focus = bodies[0];
        _browsed = focus.bodyName;

        GUILayout.BeginHorizontal();
        int at = bodies.IndexOf(focus);
        if (GUILayout.Button("<", GUILayout.Width(30))) _browsed = bodies[(at + bodies.Count - 1) % bodies.Count].bodyName;
        GUILayout.Label("<b>" + focus.bodyName + "</b>", GUILayout.Width(160));
        if (GUILayout.Button(">", GUILayout.Width(30))) _browsed = bodies[(at + 1) % bodies.Count].bodyName;
        GUILayout.EndHorizontal();

        SnowCover.Request(focus);
        BodySeason season = BodySeasons.Of(focus);
        BodyClimateState state = SnowCover.StateOf(focus);
        if (season == null)
        {
            Gui.Note(focus.bodyName + " does not orbit a star, so it has no seasons.");
            return;
        }
        double year = BodySeasons.YearFraction(season, CloudWorld.Time);
        double tilt = season.Real.RealObliquity * 180.0 / Math.PI;
        double used = season.Active.Obliquity * 180.0 / Math.PI;
        string tiltText = season.FrameFound ? string.Format(CultureInfo.InvariantCulture, "axial tilt {0:0.0} degrees", tilt) : "axial tilt unknown (the orbit could not be matched)";
        Gui.Note(string.Format(CultureInfo.InvariantCulture, "{0}; eccentricity {1:0.000}; year {2}.", tiltText, season.Real.Eccentricity, Duration(season.Host.orbit.period)));
        Gui.Note(string.Format(CultureInfo.InvariantCulture, "Northern hemisphere: {0}.  Southern hemisphere: {1}.  The sun is over latitude {2:+0.0;-0.0} degrees ({3:0}% of the way round its orbit).",
            SeasonText(season.Active.SeasonAt(year, true)), SeasonText(season.Active.SeasonAt(year, false)), CloudWorld.SubsolarLatitudeOf(focus) * 180.0 / Math.PI, year * 100.0));

        // The pretend tilt (a display setting).
        bool pretend = ClimateConfig.TryGetTilt(focus.bodyName, out double pretended);
        bool want = Gui.Toggle("Pretend axial tilt for " + focus.bodyName + " (off: its real tilt)", pretend);
        if (want != pretend) ClimateConfig.SetPretendTilt(focus.bodyName, want ? (double?)(pretended > 0.0 ? pretended : 23.44) : null);
        if (want)
        {
            double t = ClimateConfig.TryGetTilt(focus.bodyName, out double current) ? current : 23.44;
            double changed = Gui.Slider("  Pretend axial tilt (degrees)", t, 0.0, 90.0, "0.0");
            if (Math.Abs(changed - t) > 1e-6) ClimateConfig.SetPretendTilt(focus.bodyName, Math.Max(0.0, Math.Min(changed, 180.0)));
        }
        else if (season.Real.RealObliquity * 180.0 / Math.PI < 0.05)
            Gui.Note(focus.bodyName + " has no axial tilt, so no seasons; the pretend tilt above gives it some.");
        if (Math.Abs(used - tilt) > 0.05) Gui.Note(string.Format(CultureInfo.InvariantCulture, "Using {0:0.0} degrees.", used));

        if (state == null || state.Climate == null)
        {
            Gui.Note(state != null && state.Stage < 0 ? "No climate for this body." : "Working out the climate...");
            return;
        }
        Gui.Note(string.Format(CultureInfo.InvariantCulture, "Mean surface temperature {0:0.0} C (the game's sea level figure where it has one); equator {1:0.0} C, poles {2:0.0} C now.",
            state.Climate.Balance.GlobalMean - 273.15, state.Climate.SeaLevel(0.0, year) - 273.15, state.Climate.SeaLevel(80.0 * Math.PI / 180.0 * Math.Sign(season.Active.DeclinationAt(year) + 1e-9), year) - 273.15));
        Vessel vessel = FlightGlobals.ActiveVessel;
        if (vessel != null && vessel.mainBody == focus)
        {
            double lat = vessel.latitude * Math.PI / 180.0;
            double hour = vessel.longitude * Math.PI / 180.0 - CloudWorld.SubsolarLongitude(focus);
            hour -= 2.0 * Math.PI * Math.Floor((hour + Math.PI) / (2.0 * Math.PI));
            double here = state.TemperatureNow(lat, Math.Max(vessel.altitude, 0.0), hour);
            Gui.Note(string.Format(CultureInfo.InvariantCulture, "Here: {0:0.0} C from the climate model; the game's own figure is {1:0.0} C.", here - 273.15, vessel.atmosphericTemperature - 273.15));
        }
        if (state.Field != null)
        {
            if (_statBody != focus.bodyName || _statVersion != state.Version)
            {
                _statBody = focus.bodyName;
                _statVersion = state.Version;
                _stat = string.Format(CultureInfo.InvariantCulture, "Snow covers {0:0}% of the land, glaciers {1:0}%, sea ice {2:0}% of the sea.",
                    100.0 * Mean(state.Field.Snow.Values, state.Field.Terrain, false), 100.0 * Mean(state.Field.Ice.Values, state.Field.Terrain, false),
                    100.0 * Mean(state.Field.SeaIce.Values, state.Field.Terrain, true));
            }
            Gui.Note(_stat);
        }
        else if (focus.atmosphere) Gui.Note("No snow on " + focus.bodyName + (state.Moisture <= 0.0 ? " (it has no water)." : "."));
    }

    /// <summary>The area weighted mean of a map over land (or over sea) by the terrain's height.</summary>
    private static double Mean(float[] values, Pure.Textures.GreyMap terrain, bool sea)
    {
        int w = 256, h = values.Length / 256;
        double sum = 0.0, weight = 0.0;
        for (int row = 0; row < h; row++)
        {
            double cos = Math.Cos(-0.5 * Math.PI + (row + 0.5) * Math.PI / h);
            for (int col = 0; col < w; col++)
            {
                bool isSea = terrain.Sample((col + 0.5) / w, (row + 0.5) / h) < 0.0;
                if (isSea != sea) continue;
                sum += values[row * w + col] * cos;
                weight += cos;
            }
        }
        return weight > 0.0 ? sum / weight : 0.0;
    }

    private static string SeasonText(SeasonName s) => s == SeasonName.None ? "no seasons" : s.ToString().ToLowerInvariant();

    private static string Duration(double seconds)
    {
        double day = GameSettings.KERBIN_TIME ? 21600.0 : 86400.0;
        double days = seconds / day;
        return days >= 2.0 ? string.Format(CultureInfo.InvariantCulture, "{0:0.0} days", days) : string.Format(CultureInfo.InvariantCulture, "{0:0} hours", seconds / 3600.0);
    }
}
