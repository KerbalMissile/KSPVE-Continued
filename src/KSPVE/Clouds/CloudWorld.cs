using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KSPVE.Core;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Textures;
using UnityEngine;

namespace KSPVE.Clouds;

/// <summary>What one volumetric layer is at a point: for the effects (rain, snow, lightning, droplets, wet ground, sounds).</summary>
internal struct LayerHere
{
    public LayerModel Model;
    /// <summary>The layer's coverage over the point's column (0..1: map, weather and timers applied).</summary>
    public double Coverage;
    /// <summary>The two cloud types either side of the type map's value there, and how far between them (0 = Lower).</summary>
    public CloudType Lower, Upper;
    public double TypeBlend;
    /// <summary>The cloud's extinction at the point itself (1/m; 0 outside a cloud).</summary>
    public double Density;
    /// <summary>The bottom and top (m above sea level) of the column's clouds there.</summary>
    public double CloudBase, CloudTop;
    /// <summary>The point is between <see cref="CloudBase"/> and <see cref="CloudTop"/>.</summary>
    public bool Inside;
    /// <summary>The point is below the clouds (where rain falls and lightning is seen from).</summary>
    public bool Below;

    public CloudLayer Layer => Model.Layer;
    public VolumetricLayer Volume => Model.Volume;

    /// <summary>A per-type multiplier (particleFieldDensity, dropletsDensity, ...) blended between the two types.</summary>
    public double TypeValue(Func<CloudType, double> of) => Lower == null ? 1.0 : of(Lower) + (of(Upper) - of(Lower)) * TypeBlend;
}

/// <summary>
/// Something that adds cloud layers of its own to bodies (KSPVE's procedural cloud kinds, dust storms, ...): called once
/// when a body's clouds are first built (and again after <see cref="CloudWorld.Reset"/>). A procedural layer's maps should
/// be <see cref="GreyMap"/>s: when their texels change (then <see cref="GreyMap.Changed"/>), the renderer uploads them again.
/// It may also add particle field, lightning, droplets or wet surface configs to <see cref="CloudWorld.Set"/> for its layers
/// to name.
/// </summary>
internal interface ICloudLayerSource
{
    void AddLayers(BodyClouds clouds);
}

/// <summary>One body's clouds: its volumetric layers (with CPU copies of their maps for the effects), its weather.</summary>
internal sealed class BodyClouds
{
    public CelestialBody Body;
    public string Name;
    public double Radius;
    /// <summary>Every EVE layer on the body (volumetric, 2D, the older particle volumes).</summary>
    public readonly List<CloudLayer> Layers = new List<CloudLayer>();
    /// <summary>Its volumetric layers, ready to sample (EVE's, then any from <see cref="ICloudLayerSource"/>s).</summary>
    public readonly List<LayerModel> Volumetric = new List<LayerModel>();
    /// <summary>Which of <see cref="Volumetric"/> came from a <see cref="ICloudLayerSource"/>.</summary>
    public readonly HashSet<LayerModel> Procedural = new HashSet<LayerModel>();
    public Weather Weather;
    /// <summary>The CPU maps still loading (on a worker thread); until then coverage reads as none.</summary>
    public int MapsLoading;
}

/// <summary>
/// The EVE cloud configs the player has installed, read once (KSPVE reads EVE's own nodes so the packs work unchanged), and
/// each body's clouds and weather as the game needs them. Positions are turned into the body's turning frame through
/// KSP's own latitude and longitude (x towards latitude 0 longitude 0, y north, z towards longitude 90 east), so the maps
/// line up with the game's.
/// </summary>
internal static class CloudWorld
{
    /// <summary>The CPU copies of coverage and type maps are at most this wide (texels).</summary>
    public const int CpuMapWidth = 1024;

    private static readonly Dictionary<string, BodyClouds> Bodies = new Dictionary<string, BodyClouds>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, GreyMap> CpuMaps = new Dictionary<string, GreyMap>(StringComparer.OrdinalIgnoreCase);
    private static readonly object CpuMapsLock = new object();

    public static EveCloudsSet Set { get; private set; } = new EveCloudsSet();

    /// <summary>Features adding layers of their own (register in their Load).</summary>
    public static readonly List<ICloudLayerSource> Sources = new List<ICloudLayerSource>();

    /// <summary>
    /// The latitude (rad) the sun is overhead at on a body, for its seasons: from the body's real axial tilt and where it
    /// is in its orbit (none for KSP's untilted planets), unless a feature replaces it (a "pretend tilt" display setting).
    /// </summary>
    public static Func<CelestialBody, double> SubsolarLatitudeOf = RealSubsolarLatitude;
    public static bool Loaded { get; private set; }

    /// <summary>Dynamic weather on (the clouds feature's setting): weather-driven layers follow it instead of their timers.</summary>
    public static bool DynamicWeather = true;

    /// <summary>How strongly the weather moves ordinary clouds (0 = as painted, 1 = fully).</summary>
    public static double WeatherStrength = 1.0;

    /// <summary>Game time (s).</summary>
    public static double Time => Planetarium.fetch != null ? Planetarium.GetUniversalTime() : 0.0;

    /// <summary>EVE's own cloud plugin (Atmosphere.dll) is installed and drawing the packs' clouds itself.</summary>
    public static bool EveActive { get; private set; }

    /// <summary>The player has KSPVE draw the EVE packs' clouds in EVE's place (the clouds feature's setting).</summary>
    public static bool ReplaceEve;

    /// <summary>
    /// KSPVE draws the EVE packs' layers (and their rain, lightning, 2D layers, city lights...): when EVE itself is not
    /// installed, or the player chose to replace it. Otherwise only KSPVE's own procedural layers are drawn, so nothing
    /// is drawn twice.
    /// </summary>
    public static bool UseEveLayers => !EveActive || ReplaceEve;

    /// <summary>Reads every EVE node from the game database (after ModuleManager).</summary>
    public static void Load(List<ConfigIssue> issues)
    {
        var roots = new List<CfgNode>();
        foreach (string name in new[]
                 {
                     EveCloudsReader.CloudsNode, EveCloudsReader.QualityNode, EveCloudsReader.ParticleFieldsNode, EveCloudsReader.LightningNode,
                     EveCloudsReader.DropletsNode, EveCloudsReader.WetSurfacesNode, EveCloudsReader.ShadowsNode, EveCloudsReader.PqsNode,
                 })
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(name))
                roots.Add(KspConfig.ToCfg(node));
        Set = EveCloudsReader.Read(roots);
        Bodies.Clear();
        Loaded = true;
        EveActive = false;
        foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
            if (string.Equals(a.name, "Atmosphere", StringComparison.OrdinalIgnoreCase)) EveActive = true;
        int volumetric = 0;
        foreach (CloudLayer l in Set.Layers) if (l.Volume != null) volumetric++;
        Log.Info($"EVE configs: {Set.Layers.Count} cloud layers ({volumetric} volumetric), {Set.ParticleFields.Count} particle fields, "
                 + $"{Set.Lightning.Count} lightning, {Set.Droplets.Count} droplets, {Set.WetSurfaces.Count} wet surfaces, "
                 + $"{Set.Shadows.Count} eclipse shadows, {Set.Pqs.Count} PQS overrides; {Set.Issues.Count} notes.");
        foreach (ConfigIssue issue in Set.Issues) Log.Debug("EVE config " + issue);
    }

    /// <summary>A body's clouds (built the first time it is asked for; its CPU maps load in the background).</summary>
    public static BodyClouds Of(CelestialBody body)
    {
        if (body == null) return null;
        if (Bodies.TryGetValue(body.bodyName, out BodyClouds clouds) && clouds.Body == body) return clouds;
        clouds = new BodyClouds { Body = body, Name = body.bodyName, Radius = body.Radius };
        foreach (CloudLayer layer in Set.Layers)
        {
            if (!UseEveLayers) break;
            if (!string.Equals(layer.Body, body.bodyName, StringComparison.OrdinalIgnoreCase)) continue;
            clouds.Layers.Add(layer);
            if (layer.Volume == null) continue;
            var model = new LayerModel
            {
                Layer = layer,
                Volume = layer.Volume,
                PlanetRadius = body.Radius,
                BodyRotationPeriod = body.rotates ? body.rotationPeriod : 0.0,
                Role = LayerModel.RoleOf(layer),
                WeatherStrength = WeatherStrength,
            };
            clouds.Volumetric.Add(model);
            LoadCpuMap(clouds, model, layer.Volume.CoverageMap, AlphaMaskChannel.A, true);
            LoadCpuMap(clouds, model, layer.Volume.CloudTypeMap, AlphaMaskChannel.R, false);
        }
        ulong seed = 0xC10D5;
        foreach (char c in body.bodyName) seed = seed * 1099511628211UL ^ c;
        clouds.Weather = new Weather(Climate.For(body.Radius, body.rotationPeriod * (body.rotates ? 1.0 : 0.0), seed));
        Bodies[body.bodyName] = clouds;
        foreach (ICloudLayerSource source in Sources)
        {
            int before = clouds.Volumetric.Count;
            try { source.AddLayers(clouds); }
            catch (Exception ex) { Log.Exception($"Adding {source.GetType().Name}'s cloud layers to {body.bodyName} failed", ex); }
            for (int i = before; i < clouds.Volumetric.Count; i++) clouds.Procedural.Add(clouds.Volumetric[i]);
        }
        return clouds;
    }

    /// <summary>
    /// The body's turning frame as world-space axes: x towards latitude 0 longitude 0, y north, z towards longitude 90 east.
    /// </summary>
    public static void Basis(CelestialBody body, out Vector3d x, out Vector3d y, out Vector3d z)
    {
        x = body.GetSurfaceNVector(0.0, 0.0).normalized;
        y = body.GetSurfaceNVector(90.0, 0.0).normalized;
        z = body.GetSurfaceNVector(0.0, 90.0).normalized;
    }

    /// <summary>A world point in the body's turning frame (m from its centre).</summary>
    public static Double3 ToBody(CelestialBody body, Vector3d world)
    {
        Basis(body, out Vector3d x, out Vector3d y, out Vector3d z);
        Vector3d r = world - body.position;
        return new Double3(Vector3d.Dot(r, x), Vector3d.Dot(r, y), Vector3d.Dot(r, z));
    }

    /// <summary>A world direction in the body's turning frame.</summary>
    public static Double3 DirectionToBody(CelestialBody body, Vector3d world)
    {
        Basis(body, out Vector3d x, out Vector3d y, out Vector3d z);
        return new Double3(Vector3d.Dot(world, x), Vector3d.Dot(world, y), Vector3d.Dot(world, z));
    }

    /// <summary>The weather at a point of the body's turning frame (prepared for the current time and sun).</summary>
    public static WeatherSample WeatherAt(BodyClouds clouds, Double3 p)
    {
        double r = Math.Max(p.Length, 1.0);
        PrepareWeather(clouds);
        return clouds.Weather.Sample(Math.Asin(Math.Max(-1.0, Math.Min(1.0, p.Y / r))), Math.Atan2(p.Z, p.X));
    }

    /// <summary>Brings a body's weather to now, with the sun over its current subsolar point (its season too).</summary>
    public static void PrepareWeather(BodyClouds clouds)
    {
        clouds.Weather.Prepare(Time, SubsolarLongitude(clouds.Body), SubsolarLatitudeOf(clouds.Body));
    }

    /// <summary>The star a body orbits (directly or through its planet), or the stock sun.</summary>
    public static CelestialBody StarOf(CelestialBody body)
    {
        for (CelestialBody b = body; b != null && b.referenceBody != null && b.referenceBody != b; b = b.referenceBody)
            if (IsStar(b.referenceBody)) return b.referenceBody;
        return Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
    }

    /// <summary>The latitude the body's star is overhead at now: its real season.</summary>
    public static double RealSubsolarLatitude(CelestialBody body)
    {
        CelestialBody star = StarOf(body);
        if (star == null || star == body) return 0.0;
        Double3 d = DirectionToBody(body, (star.position - body.position).normalized);
        return Math.Asin(Math.Max(-1.0, Math.Min(1.0, d.Y / Math.Max(d.Length, 1e-12))));
    }

    /// <summary>
    /// The wind at a point of the body's turning frame (m/s; x east, y north, z up): the weather's wind at the surface
    /// (10 m), growing with height as a boundary layer's does (a power law, exponent 1/7, up to 1 km) and then to about
    /// twice that at the tropopause's jet (two scale heights higher).
    /// </summary>
    public static Double3 WindAt(BodyClouds clouds, Double3 p)
    {
        WeatherSample w = WeatherAt(clouds, p);
        double altitude = Math.Max(p.Length - clouds.Radius, 0.0);
        double scaleHeight = clouds.Body.atmosphere ? Math.Max(clouds.Body.atmosphereDepth / 7.0, 1000.0) : 5000.0;
        double boundary = Math.Pow(Math.Min(Math.Max(altitude, 2.0), 1000.0) / 10.0, 1.0 / 7.0);
        double jet = 1.0 + Math.Min(Math.Max((altitude - 1000.0) / (2.0 * scaleHeight), 0.0), 1.0);
        double factor = boundary * jet;
        return new Double3(w.WindEast * factor, w.WindNorth * factor, 0.0);
    }

    /// <summary>The wind at a world point as a world-space velocity (m/s), relative to the turning ground.</summary>
    public static Vector3d WindWorld(BodyClouds clouds, Vector3d world)
    {
        Double3 p = ToBody(clouds.Body, world);
        Double3 w = WindAt(clouds, p);
        double r = Math.Max(p.Length, 1.0), horizontal = Math.Sqrt(p.X * p.X + p.Z * p.Z);
        if (horizontal < 1e-6 * r) return Vector3d.zero;   // at a pole east and north are undefined
        // In the turning frame: east = d/d(longitude), north = d/d(latitude) of the point's direction.
        double cosLon = p.X / horizontal, sinLon = p.Z / horizontal, sinLat = p.Y / r, cosLat = horizontal / r;
        var east = new Double3(-sinLon, 0.0, cosLon);
        var north = new Double3(-sinLat * cosLon, cosLat, -sinLat * sinLon);
        Double3 v = east * w.X + north * w.Y;
        Basis(clouds.Body, out Vector3d x, out Vector3d y, out Vector3d z);
        return x * v.X + y * v.Y + z * v.Z;
    }

    /// <summary>The longitude (rad, the body's turning frame) the brightest star is overhead at.</summary>
    public static double SubsolarLongitude(CelestialBody body)
    {
        CelestialBody star = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
        for (CelestialBody b = body; b != null && b.referenceBody != null && b.referenceBody != b; b = b.referenceBody)
            if (b.referenceBody.scaledBody != null && IsStar(b.referenceBody)) { star = b.referenceBody; break; }
        if (star == null || star == body) return 0.0;
        Double3 d = DirectionToBody(body, (star.position - body.position).normalized);
        return Math.Atan2(d.Z, d.X);
    }

    /// <summary>A body that shines (a Kopernicus star, or the stock sun).</summary>
    public static bool IsStar(CelestialBody body) =>
        body != null && (Planetarium.fetch != null && body == Planetarium.fetch.Sun || body.scaledBody != null && body.scaledBody.GetComponentInChildren<SunShaderController>() != null);

    /// <summary>
    /// Every volumetric layer of <paramref name="clouds"/> at a world point: its coverage over the column, cloud types, the
    /// density at the point, the column's base and top. Returns how many were added to <paramref name="into"/>.
    /// </summary>
    public static int Here(BodyClouds clouds, Vector3d world, List<LayerHere> into)
    {
        if (clouds == null || clouds.Volumetric.Count == 0) return 0;
        double time = Time;
        Double3 p = ToBody(clouds.Body, world);
        double radius = Math.Max(p.Length, 1.0);
        double altitude = radius - clouds.Radius;
        WeatherSample weather = WeatherAt(clouds, p);
        int added = 0;
        foreach (LayerModel m in clouds.Volumetric)
        {
            var here = new LayerHere { Model = m };
            m.WeatherStrength = WeatherStrength;
            Double3 local = CloudField.IntoLayer(p, CloudField.LayerAngle(m.Layer, m.PlanetRadius, time, m.BodyRotationPeriod));
            CloudField.MapUv(local / radius, out double u, out double v);
            here.Coverage = CloudField.Coverage(m, u, v, weather, DynamicWeather, time);
            List<CloudType> types = m.Volume.CloudTypes;
            if (types.Count > 0)
            {
                double typeValue = m.CloudType != null ? m.CloudType.Sample(m.MapsInLatLon ? CloudField.LatLonU(u) : u, v) : 0.0;
                CloudTypeBlend.Pick(typeValue, types.Count, out int lower, out double blend);
                here.Lower = types[lower];
                here.Upper = types[Math.Min(lower + 1, types.Count - 1)];
                here.TypeBlend = blend;
                bool interpolate = here.Lower.InterpolateCloudHeights || here.Upper.InterpolateCloudHeights;
                CloudType nearest = blend < 0.5 ? here.Lower : here.Upper;
                here.CloudBase = interpolate ? here.Lower.MinAltitude + (here.Upper.MinAltitude - here.Lower.MinAltitude) * blend : nearest.MinAltitude;
                here.CloudTop = interpolate ? here.Lower.MaxAltitude + (here.Upper.MaxAltitude - here.Lower.MaxAltitude) * blend : nearest.MaxAltitude;
            }
            here.Inside = altitude >= here.CloudBase && altitude <= here.CloudTop;
            here.Below = altitude < here.CloudBase;
            here.Density = CloudField.Sample(m, p, time, weather, DynamicWeather).Density;
            into.Add(here);
            added++;
        }
        return added;
    }

    /// <summary>Forgets every body's clouds and CPU maps (after the configs or settings change).</summary>
    public static void Reset()
    {
        Bodies.Clear();
        lock (CpuMapsLock) CpuMaps.Clear();
    }

    private static void LoadCpuMap(BodyClouds clouds, LayerModel model, TextureRef texture, AlphaMaskChannel fallback, bool coverage)
    {
        if (texture == null || !texture.IsSet) return;
        int channel = GreyMap.ChannelIndex(texture.ChannelOr(fallback));
        string key = texture.Path + "|" + channel;
        lock (CpuMapsLock)
        {
            if (CpuMaps.TryGetValue(key, out GreyMap known))
            {
                Assign(model, known, coverage);
                return;
            }
        }
        string basePath = Path.Combine(Paths.GameDataDir, texture.Path.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(basePath + ".dds"))
        {
            Interlocked.Increment(ref clouds.MapsLoading);
            string file = basePath + ".dds";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    GreyMap map = GreyMap.FromDds(DdsImage.Parse(File.ReadAllBytes(file)), channel, CpuMapWidth);
                    lock (CpuMapsLock) CpuMaps[key] = map;
                    Assign(model, map, coverage);
                }
                catch (Exception ex)
                {
                    Log.Warning($"Reading the cloud map {texture.Path} on the CPU failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Decrement(ref clouds.MapsLoading);
                }
            });
            return;
        }
        // PNG and the rest go through Unity's loader, on this (the main) thread.
        Texture2D readable = null;
        try
        {
            foreach (string extension in new[] { ".png", ".truecolor", ".jpg", "" })
            {
                if (!File.Exists(basePath + extension)) continue;
                readable = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                if (ImageConversion.LoadImage(readable, File.ReadAllBytes(basePath + extension), false)) break;
                UnityEngine.Object.Destroy(readable);
                readable = null;
            }
            if (readable == null)
            {
                Log.Warning("Cloud map not found for the effects: " + texture.Path);
                return;
            }
            Color32[] pixels = readable.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[4 * i] = pixels[i].r;
                bytes[4 * i + 1] = pixels[i].g;
                bytes[4 * i + 2] = pixels[i].b;
                bytes[4 * i + 3] = pixels[i].a;
            }
            // Unity's loader puts the image's bottom row first: row 0 is the south.
            GreyMap map = GreyMap.FromRgba32(bytes, readable.width, readable.height, channel, false, CpuMapWidth);
            lock (CpuMapsLock) CpuMaps[key] = map;
            Assign(model, map, coverage);
        }
        finally
        {
            if (readable != null) UnityEngine.Object.Destroy(readable);
        }
    }

    private static void Assign(LayerModel model, GreyMap map, bool coverage)
    {
        if (coverage) model.Coverage = map;
        else model.CloudType = map;
    }
}
