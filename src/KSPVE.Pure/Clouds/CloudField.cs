using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Clouds;

/// <summary>How a layer follows the dynamic weather.</summary>
public enum WeatherRole
{
    /// <summary>Unaffected (aurorae, haze, effects-only layers, layers without a coverage map).</summary>
    None,
    /// <summary>Ordinary clouds: thicker where the air is moist, thinner where it is dry.</summary>
    Base,
    /// <summary>Storm clouds (cumulonimbus and the like): only where there are storms.</summary>
    Convective,
    /// <summary>Rain, snow, fog with rain, lightning: where precipitation is falling, instead of on a timer everywhere at once.</summary>
    Precipitation,
}

/// <summary>A one-channel map over a body (a coverage or cloud type map), sampled at u (longitude, wrapping) and v (latitude, 0 south).</summary>
public interface IGreyMap
{
    double Sample(double u, double v);
}

/// <summary>One volumetric layer, ready to sample: its config, its maps, its noise, its body's radius, how it follows the weather.</summary>
public sealed class LayerModel
{
    public CloudLayer Layer;
    public VolumetricLayer Volume;
    /// <summary>Its coverage and cloud type maps (null: full coverage; type 0 everywhere).</summary>
    public IGreyMap Coverage, CloudType;
    public NoiseVolume Noise;
    public double PlanetRadius;
    /// <summary>The body's rotation period (s; 0 = not turning), for layers that keep still in space (killBodyRotation).</summary>
    public double BodyRotationPeriod;
    public WeatherRole Role;
    public double WeatherStrength = 1.0;

    /// <summary>
    /// Its maps are on KSPVE's latitude-longitude grid (column 0 at longitude -180, running east), as KSPVE's procedural
    /// layers are, rather than laid out as EVE's and KSP's planet textures (<see cref="CloudField.MapUv"/>).
    /// </summary>
    public bool MapsInLatLon;

    /// <summary>The lowest and highest altitude (m above sea level) any of its clouds reach.</summary>
    public double Bottom => Volume.MinAltitude;
    public double Top => Volume.MaxAltitude;

    /// <summary>
    /// How a layer follows the weather, judged from what is in it: storm cloud types (cumulonimbus, thunder, storm, anvil,
    /// pyrocumulus) are convective, only where there are storms; a layer drawn only for its effects (rain, snow,
    /// lightning, droplets, wet ground) follows where precipitation falls; any other layer with a coverage map is ordinary
    /// cloud, thicker where the air is moist (that it can also rain, as most of the packs' layers can, does not confine it
    /// to where it rains); anything else (aurorae, haze, layers without a map) none.
    /// </summary>
    public static WeatherRole RoleOf(CloudLayer layer)
    {
        VolumetricLayer v = layer.Volume;
        if (v == null) return WeatherRole.None;
        bool effects = v.HasParticleField || v.HasLightning || v.HasDroplets || v.HasWetSurfaces;
        if (v.Raymarching.FxOnlyLayer) return effects ? WeatherRole.Precipitation : WeatherRole.None;
        if (!v.CoverageMap.IsSet) return WeatherRole.None;
        if (v.ScaledFadeStartAltitude < 0.0) return WeatherRole.None;   // a stub drawn flat from everywhere (aurorae)
        string name = layer.Name.ToLowerInvariant();
        if (IsStormy(name)) return WeatherRole.Convective;
        foreach (CloudType t in v.CloudTypes)
            if (IsStormy(t.Name.ToLowerInvariant())) return WeatherRole.Convective;
        return WeatherRole.Base;
    }

    private static bool IsStormy(string name) =>
        name.Contains("nimbus") || name.Contains("thunder") || name.Contains("storm") || name.Contains("anvil") || name.Contains("pyro");
}

/// <summary>What the clouds are at a point: their density and where in their column it is.</summary>
public struct CloudPoint
{
    /// <summary>Extinction (1/m): light falls by e over 1/Density metres.</summary>
    public double Density;
    /// <summary>0 at the cloud's base, 1 at its top.</summary>
    public double HeightFraction;
    /// <summary>How bright its multiply-scattered light is (cloud type's multipleScatteringBrightness).</summary>
    public double MultipleScattering;
}

/// <summary>
/// The density of a volumetric layer's clouds at a point (KSPVE's reading of EVE's raymarched clouds): its coverage map
/// says how much of the sky is cloud there (moved by the dynamic weather), its cloud type map which kinds (blending the two
/// either side), each kind's height band and coverage curve shape the column, the layer's 3D noise carves the clouds out of
/// it (deeper the more erosion), and the kind's density and density curve set how thick they are. Positions are in the
/// body's turning frame (m from its centre, y its axis). The shader follows this exactly; the preview and tests use it.
/// </summary>
public static class CloudField
{
    /// <summary>
    /// The map coordinates of a direction from the body's centre (its turning frame), as EVE's maps and KSP's own planet
    /// textures are laid out (both as Unity loads a DDS, raw rows): u = 0.25 - longitude / 360 degrees (longitude 0 a
    /// quarter of the way across, running west), v = 0.5 + latitude / 180 (row 0 the south pole). Found from the data:
    /// the Sol pack's EVE city lights put all eight test cities (Tokyo, New York, Delhi...) on lit pixels only this way,
    /// and Kopernicus's Earth colour map puts all sixteen test points on land or sea correctly only this way.
    /// </summary>
    public static void MapUv(Double3 direction, out double u, out double v)
    {
        double longitude = Math.Atan2(direction.Z, direction.X);
        u = 0.25 - longitude / (2.0 * Math.PI);
        u -= Math.Floor(u);
        v = 0.5 + Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.Y))) / Math.PI;
    }

    /// <summary>A map u (<see cref="MapUv"/>) on KSPVE's latitude-longitude grid instead (column 0 at -180, running east).</summary>
    public static double LatLonU(double u)
    {
        double w = 0.75 - u;
        return w - Math.Floor(w);
    }

    /// <summary>
    /// How far the layer has turned round the body's axis by game time <paramref name="time"/> (radians): its speed (m/s
    /// at the surface, at its own altitude, or degrees a second) about its axis, plus its starting offset.
    /// </summary>
    /// <remarks>
    /// With killBodyRotation the layer does not turn with the body at all: it keeps its own speed in space, so in the
    /// body's turning frame it also slips back by the body's rotation (<paramref name="bodyRotationPeriod"/>, s; 0 when
    /// unknown).
    /// </remarks>
    public static double LayerAngle(CloudLayer layer, double planetRadius, double time, double bodyRotationPeriod = 0.0)
    {
        double speed = layer.Speed.Y;
        double rate = layer.SpeedMode switch
        {
            CloudSpeedMode.LinearSurface => speed / Math.Max(planetRadius, 1.0),
            CloudSpeedMode.Angular => speed * Math.PI / 180.0,
            _ => speed / Math.Max(planetRadius + layer.Altitude, 1.0),
        };
        double angle = layer.Offset.Y * Math.PI / 180.0 + rate * time;
        if (layer.KillBodyRotation && bodyRotationPeriod != 0.0) angle -= 2.0 * Math.PI * time / bodyRotationPeriod;
        return angle;
    }

    /// <summary>A point carried back round the body's axis by <paramref name="angle"/>: where it sits in the layer's turning frame.</summary>
    public static Double3 IntoLayer(Double3 p, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Double3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
    }

    /// <summary>The layer's coverage at map coordinates (u, v), moved by the weather there, and by its time cycle.</summary>
    public static double Coverage(LayerModel m, double u, double v, in WeatherSample weather, bool dynamicWeather, double time)
    {
        double c = m.Coverage != null ? m.Coverage.Sample(m.MapsInLatLon ? LatLonU(u) : u, v) : 1.0;
        if (dynamicWeather && m.Role != WeatherRole.None)
        {
            double moved = m.Role switch
            {
                WeatherRole.Base => c * (0.5 + weather.Humidity),   // as painted at average moisture (0.5)
                WeatherRole.Convective => c * Math.Min(weather.Storm * 1.5, 1.0),
                _ => c * Math.Min(weather.Precipitation * 1.3, 1.0),
            };
            c += (Math.Min(moved, 1.0) - c) * Math.Min(Math.Max(m.WeatherStrength, 0.0), 1.0);
        }
        // Weather-driven layers come and go with the weather; the rest keep their timers.
        TimeCycle cycle = m.Layer.Time;
        bool timed = cycle != null && !(dynamicWeather && (m.Role == WeatherRole.Convective || m.Role == WeatherRole.Precipitation));
        if (timed && cycle.FadeMode == TimeFadeMode.Coverage) c *= cycle.FactorAt(time);
        return Math.Min(Math.Max(c, 0.0), 1.0);
    }

    /// <summary>
    /// The clouds at <paramref name="p"/> (the body's turning frame, m from its centre) at game time <paramref name="time"/>,
    /// with the weather of its column.
    /// </summary>
    public static CloudPoint Sample(LayerModel m, Double3 p, double time, in WeatherSample weather, bool dynamicWeather)
    {
        var result = new CloudPoint();
        VolumetricLayer v = m.Volume;
        if (v.CloudTypes.Count == 0 || v.Raymarching.FxOnlyLayer) return result;
        double radius = p.Length;
        double altitude = radius - m.PlanetRadius;
        if (altitude < m.Bottom || altitude > m.Top) return result;

        double angle = LayerAngle(m.Layer, m.PlanetRadius, time, m.BodyRotationPeriod);
        Double3 local = IntoLayer(p, angle);
        Double3 direction = local / radius;
        MapUv(direction, out double u, out double mapV);
        double coverage = Coverage(m, u, mapV, weather, dynamicWeather, time);
        if (coverage <= 0.0) return result;

        // The two cloud types either side of the map's value, and how far between them.
        double typeValue = m.CloudType != null ? m.CloudType.Sample(m.MapsInLatLon ? LatLonU(u) : u, mapV) : 0.0;
        CloudTypeBlend.Pick(typeValue, v.CloudTypes.Count, out int lower, out double blend);
        CloudType a = v.CloudTypes[lower], b = v.CloudTypes[Math.Min(lower + 1, v.CloudTypes.Count - 1)];
        bool interpolate = a.InterpolateCloudHeights || b.InterpolateCloudHeights;
        CloudType nearest = blend < 0.5 ? a : b;
        double bottom = interpolate ? Lerp(a.MinAltitude, b.MinAltitude, blend) : nearest.MinAltitude;
        double top = interpolate ? Lerp(a.MaxAltitude, b.MaxAltitude, blend) : nearest.MaxAltitude;
        if (top <= bottom || altitude < bottom || altitude > top) return result;
        double h = (altitude - bottom) / (top - bottom);

        double shapeCoverage = coverage * Clamp01(Lerp(a.CoverageCurve.Evaluate(h), b.CoverageCurve.Evaluate(h), blend));
        if (shapeCoverage <= 0.0) return result;

        // Carved from the noise, scrolling up through the layer and drifting with it.
        double tiling = Math.Max(Lerp(a.BaseNoiseTiling, b.BaseNoiseTiling, blend), 1.0);
        Double3 drift = direction * (v.UpwardsCloudSpeed * time) + m.Layer.DetailSpeed * time;
        Double3 n = (local - drift) / tiling;
        double noise = m.Noise != null ? m.Noise.Sample(n.X, n.Y, n.Z) : 1.0;
        double eroded = 1.0 - v.Noise.ErosionDepth * (1.0 - noise);
        double shape = Clamp01((eroded - (1.0 - shapeCoverage)) / Math.Max(shapeCoverage, 1e-3));
        double hardness = Lerp(a.NoiseEdgeHardness, b.NoiseEdgeHardness, blend);
        shape = Clamp01(shape / Math.Max(1.0 - 0.95 * hardness, 0.05));
        if (shape <= 0.0) return result;

        double density = Lerp(a.Density * a.DensityCurve.Evaluate(h), b.Density * b.DensityCurve.Evaluate(h), blend);
        TimeCycle cycle = m.Layer.Time;
        bool timed = cycle != null && !(dynamicWeather && (m.Role == WeatherRole.Convective || m.Role == WeatherRole.Precipitation));
        if (timed && cycle.FadeMode == TimeFadeMode.Density) density *= cycle.FactorAt(time);
        result.Density = Math.Max(density, 0.0) * shape;
        result.HeightFraction = h;
        result.MultipleScattering = Lerp(a.MultipleScatteringBrightness, b.MultipleScatteringBrightness, blend);
        return result;
    }

    /// <summary>
    /// How much of a layer is drawn as its flat 2D layer rather than volumetrically, seen from <paramref name="cameraAltitude"/>
    /// (m above sea level): none below its scaledFadeStartAltitude, all above its scaledFadeEndAltitude, smoothly between
    /// (the volumetric clouds fade out as the 2D layer fades in). A layer without both kinds is always just the one it has.
    /// </summary>
    public static double FlatShare(CloudLayer layer, double cameraAltitude)
    {
        if (layer.Volume == null) return 1.0;
        if (layer.Layer2D == null) return 0.0;
        double start = layer.Volume.ScaledFadeStartAltitude, end = layer.Volume.ScaledFadeEndAltitude;
        if (start < 0.0) return 1.0;   // a stub drawn flat from everywhere
        if (end <= start) return cameraAltitude >= start ? 1.0 : 0.0;
        double t = Clamp01((cameraAltitude - start) / (end - start));
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Clamp01(double x) => x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
}
