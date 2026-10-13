using System;
using System.Collections.Generic;
using System.Globalization;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Clouds;

/// <summary>
/// Reads configs written for EVE Redux's raymarched volumetrics into KSPVE's model (<see cref="EveCloudsSet"/>): cloud
/// layers (layerRaymarchedVolumeV5, and the older layerRaymarchedVolume that EVE's release 5 no longer reads), the
/// quality node, particle fields, lightning, droplets, wet surfaces, eclipse shadows and PQS overrides. Anything it does
/// not know is reported, not fatal, so one bad key never loses a whole pack.
/// </summary>
public static class EveCloudsReader
{
    public const string CloudsNode = "EVE_CLOUDS", QualityNode = "EVE_RAYMARCHED_CLOUDS_QUALITY", ParticleFieldsNode = "EVE_PARTICLE_FIELD_CONFIG",
        LightningNode = "EVE_LIGHTNING_CONFIG", DropletsNode = "EVE_DROPLETS_CONFIG", WetSurfacesNode = "EVE_WET_SURFACES_CONFIG",
        ShadowsNode = "EVE_SHADOWS", PqsNode = "PQS_MANAGER";

    /// <summary>The layer node EVE's release 5 reads, and the older one (releases 1-4).</summary>
    public const string VolumeNode = "layerRaymarchedVolumeV5", OlderVolumeNode = "layerRaymarchedVolume";

    /// <summary>The EVE nodes among <paramref name="roots"/> (top-level config nodes), read into one set.</summary>
    public static EveCloudsSet Read(IEnumerable<CfgNode> roots)
    {
        var set = new EveCloudsSet();
        foreach (CfgNode root in roots)
        {
            switch (root.Name)
            {
                case CloudsNode:
                    foreach (CfgNode o in Objects(root)) set.Layers.Add(ReadLayer(Reader(o, root.Name, set)));
                    break;
                case QualityNode:
                    foreach (CfgNode o in Objects(root)) set.Quality = ReadQuality(Reader(o, root.Name, set));
                    break;
                case ParticleFieldsNode:
                    foreach (CfgNode o in Objects(root)) { ParticleFieldConfig c = ReadParticleField(Reader(o, root.Name, set)); set.ParticleFields[c.Name] = c; }
                    break;
                case LightningNode:
                    foreach (CfgNode o in Objects(root)) { LightningConfig c = ReadLightning(Reader(o, root.Name, set)); set.Lightning[c.Name] = c; }
                    break;
                case DropletsNode:
                    foreach (CfgNode o in Objects(root)) { DropletsConfig c = ReadDroplets(Reader(o, root.Name, set)); set.Droplets[c.Name] = c; }
                    break;
                case WetSurfacesNode:
                    foreach (CfgNode o in Objects(root)) { WetSurfacesConfig c = ReadWetSurfaces(Reader(o, root.Name, set)); set.WetSurfaces[c.Name] = c; }
                    break;
                case ShadowsNode:
                    foreach (CfgNode o in Objects(root)) set.Shadows.Add(ReadShadows(Reader(o, root.Name, set)));
                    break;
                case PqsNode:
                    foreach (CfgNode o in Objects(root)) set.Pqs.Add(ReadPqs(Reader(o, root.Name, set)));
                    break;
            }
        }
        CheckReferences(set);
        return set;
    }

    private static IEnumerable<CfgNode> Objects(CfgNode root)
    {
        foreach (CfgNode child in root.Children)
            if (string.Equals(child.Name, "OBJECT", StringComparison.OrdinalIgnoreCase)) yield return child;
    }

    private static NodeReader Reader(CfgNode o, string rootName, EveCloudsSet set)
    {
        string name = o.GetLast("name");
        if (string.IsNullOrWhiteSpace(name)) name = o.GetLast("body") ?? "?";
        return new NodeReader(o, rootName + "/" + name, set.Issues);
    }

    // ---------------------------------------------------------------- cloud layers

    private static CloudLayer ReadLayer(NodeReader r)
    {
        var layer = new CloudLayer
        {
            Name = r.Text("name"),
            Body = r.Text("body"),
            Altitude = r.Number("altitude", 0.0),
            KillBodyRotation = r.Flag("killBodyRotation", false),
            Speed = r.Vector("speed", default),
            DetailSpeed = r.Vector("detailSpeed", default),
            SpeedMode = r.Choice("speedMode", CloudSpeedMode.Linear),
            Offset = r.Vector("offset", default),
            Arc = r.Number("arc", 360.0),
        };
        layer.RotationAxis0 = r.Vector("rotationAxis0", layer.RotationAxis0);
        layer.RotationAxis1 = r.Vector("rotationAxis1", layer.RotationAxis1);
        layer.RotationAxis2 = r.Vector("rotationAxis2", layer.RotationAxis2);
        NodeReader settings = r.Sub("settings");
        if (settings != null) layer.Material = ReadMaterial(settings);
        NodeReader time = r.Sub("timeSettings");
        if (time != null) layer.Time = ReadTime(time);
        NodeReader flat = r.Sub("layer2D");
        if (flat != null) layer.Layer2D = ReadLayer2D(flat);
        NodeReader particles = r.Sub("layerVolume");
        if (particles != null) layer.LayerVolume = ReadLayerVolume(particles);
        NodeReader volume = r.Sub(VolumeNode);
        NodeReader older = r.Sub(OlderVolumeNode);
        if (volume != null) layer.Volume = ReadVolume(volume, false);
        else if (older != null)
        {
            layer.Volume = ReadVolume(older, true);
            r.Note(IssueSeverity.Warning, OlderVolumeNode + " is EVE's release 1-4 format (EVE's release 5 ignores it); KSPVE reads it, as near as the formats allow");
        }
        r.Finish();
        return layer;
    }

    private static CloudsMaterial ReadMaterial(NodeReader r)
    {
        var m = new CloudsMaterial
        {
            Color = r.Color("_Color", CfgColor.White),
            MainTex = r.Texture("_MainTex"),
            BumpMap = r.Texture("_BumpMap"),
            DetailTex = r.Texture("_DetailTex"),
            UVNoiseTex = r.Texture("_UVNoiseTex"),
            Lambertian = r.Number("_Lambertian", 1.0),
            BumpScale = r.Number("_BumpScale", 1.0),
            DetailScale = r.Number("_DetailScale", 1.0),
            DetailDist = r.Number("_DetailDist", 0.0),
            DistFade = r.Number("_DistFade", 0.0),
            DistFadeVert = r.Number("_DistFadeVert", 0.0),
            UVNoiseScale = r.Number("_UVNoiseScale", 0.0),
            UVNoiseStrength = r.Number("_UVNoiseStrength", 0.0),
            UVNoiseAnimation = r.Pair("_UVNoiseAnimation", default),
        };
        NodeReader flow = r.Sub("_FlowMap");
        if (flow != null) m.FlowMap = ReadFlowMap(flow);
        r.Finish();
        return m;
    }

    private static FlowMapSettings ReadFlowMap(NodeReader r)
    {
        var f = new FlowMapSettings { Texture = r.Texture("texture"), Speed = r.Number("speed", 0.0), Displacement = r.Number("displacement", 0.0) };
        r.Finish();
        return f.Texture.IsSet || f.Speed != 0.0 || f.Displacement != 0.0 ? f : null;   // an empty node: no flow map
    }

    private static TimeCycle ReadTime(NodeReader r)
    {
        var t = new TimeCycle
        {
            Unit = r.Choice("unit", TimeUnit.Seconds),
            Duration = r.Number("duration", 0.0),
            Offset = r.Number("offset", 0.0),
            RepeatInterval = r.Number("repeatInterval", 0.0),
            FadeTime = r.Number("fadeTime", 0.0),
            FadeMode = r.Choice("fadeMode", TimeFadeMode.Coverage),
        };
        r.Finish();
        return t;
    }

    private static Layer2DSettings ReadLayer2D(NodeReader r)
    {
        var l = new Layer2DSettings();
        NodeReader macro = r.Sub("macroCloudMaterial");
        if (macro != null)
        {
            l.FalloffPow = macro.Number("_FalloffPow", 0.0);
            l.FalloffScale = macro.Number("_FalloffScale", 0.0);
            l.MinLight = macro.Number("_MinLight", 0.0);
            l.RimDist = macro.Number("_RimDist", 0.0);
            l.RimDistSub = macro.Number("_RimDistSub", 0.0);
            l.InvFade = macro.Number("_InvFade", 0.0);
            macro.Ignore("_DetailDist");   // set here by some packs; the material's own is the one used
            macro.Finish();
        }
        NodeReader shadow = r.Sub("shadowMaterial");
        if (shadow != null)
        {
            l.HasShadow = true;
            l.ShadowFactor = shadow.Number("_ShadowFactor", 1.0);
            shadow.Ignore("_VolumetricShadowDensity");
            shadow.Finish();
        }
        r.Finish();
        return l;
    }

    private static LayerVolumeSettings ReadLayerVolume(NodeReader r)
    {
        var v = new LayerVolumeSettings
        {
            Size = r.Pair("size", default),
            MaxTranslation = r.Vector("maxTranslation", default),
            Area = r.Pair("area", default),
            VisibleRange = r.Number("visibleRange", 0.0),
            RotationSpeed = r.Number("rotationSpeed", 0.0),
            FollowDetail = r.Flag("followDetail", false),
            NoiseScale = r.Vector("noiseScale", default),
        };
        NodeReader material = r.Sub("particleMaterial");
        if (material != null)
        {
            v.ParticleTexture = material.Texture("_Tex");
            v.InvFade = material.Number("_InvFade", 0.0);
            v.MinScatter = material.Number("_MinScatter", 0.0);
            v.Opacity = material.Number("_Opacity", 1.0);
            material.Finish();
        }
        r.Finish();
        return v;
    }

    private static VolumetricLayer ReadVolume(NodeReader r, bool older)
    {
        var v = new VolumetricLayer
        {
            OlderFormat = older,
            CoverageMap = r.Texture("coverageMap"),
            CloudTypeMap = r.Texture("cloudTypeMap"),
            CloudColorMap = r.Texture("cloudColorMap"),
            SdfMap = r.Text("sdfMap"),
            Color = r.Color("color", CfgColor.White),
            SkylightMultiplier = r.Number("skylightMultiplier", 1.0),
            SkylightTintMultiplier = r.Number("skylightTintMultiplier", 0.0),
            ReceiveShadowsFromLayer = r.Text("receiveShadowsFromLayer"),
            ReceivedShadowsDensity = r.Number("receivedShadowsDensity", 100.0),
            UpwardsCloudSpeed = r.Number("upwardsCloudSpeed", 11.0),
            ScaledFadeStartAltitude = r.Number("scaledFadeStartAltitude", 30000.0),
            ScaledFadeEndAltitude = r.Number("scaledFadeEndAltitude", 55000.0),
            UseDetailTex = r.Flag("useDetailTex", false),
            UseUntilingIfEnabled = r.Flag("useUntilingIfEnabled", true),
        };
        NodeReader noise = r.Sub("noise");
        if (noise != null) v.Noise = ReadNoise(noise);
        NodeReader curl = r.Sub("curlNoise");
        if (curl != null) v.CurlNoise = ReadCurl(curl);
        NodeReader flow = r.Sub("flowMap");
        if (flow != null) v.FlowMap = ReadFlowMap(flow);
        NodeReader march = r.Sub("raymarchingSettings");
        if (march != null) v.Raymarching = ReadRaymarching(march);
        NodeReader light = r.Sub("lightVolumeSettings");
        if (light != null)
        {
            v.UseLightVolume = light.Flag("useLightVolume", true);
            v.MaxLightVolumeRadius = light.Number("maxLightVolumeRadius", double.PositiveInfinity);
            light.Finish();
        }
        NodeReader phase = r.Sub("phaseFunctions");
        if (phase != null) v.Phase = ReadPhase(phase);
        NodeReader particles = r.Sub("particleField");
        if (particles != null) { v.HasParticleField = true; v.ParticleField = particles.Text("particleFieldConfig"); particles.Finish(); }
        NodeReader droplets = r.Sub("droplets");
        if (droplets != null) { v.HasDroplets = true; v.Droplets = droplets.Text("dropletsConfig"); droplets.Finish(); }
        NodeReader wet = r.Sub("wetSurfaces");
        if (wet != null) { v.HasWetSurfaces = true; v.WetSurfaces = wet.Text("wetSurfacesConfig"); wet.Finish(); }
        NodeReader lightning = r.Sub("lightning");
        if (lightning != null) { v.HasLightning = true; v.Lightning = lightning.Text("lightningConfig"); lightning.Finish(); }
        NodeReader sound = r.Sub("ambientSound");
        if (sound != null)
        {
            v.HasAmbientSound = true;
            v.AmbientSound = sound.Text("soundName");
            v.IvaAmbientSound = sound.Text("ivaSoundName");
            sound.Finish();
        }
        foreach (NodeReader item in r.Items("cloudTypes")) v.CloudTypes.Add(ReadCloudType(item));
        foreach (NodeReader item in r.Items("painterTiles"))
        {
            v.PainterTiles.Add(new PainterTile
            {
                Name = item.Text("tileName"),
                CoverageMap = item.Texture("coverageMap"),
                CloudTypeMap = item.Texture("cloudTypeMap"),
                Size = item.Number("size", 0.0),
                RemapCoverage = item.Pair("remapCoverage", new Double2(0, 1)),
                RemapType = item.Pair("remapType", new Double2(0, 1)),
            });
            item.Finish();
        }
        foreach (NodeReader item in r.Items("painterTileMasks"))
        {
            v.PainterTileMasks.Add(new PainterTileMask { Name = item.Text("tileMaskName"), Texture = item.Texture("texture") });
            item.Finish();
        }
        r.Finish();
        return v;
    }

    private static CloudNoise ReadNoise(NodeReader r)
    {
        var n = new CloudNoise { ErosionDepth = r.Number("erosionDepth", 0.7) };
        NodeReader worley = r.Sub("worley");
        if (worley != null) n.Worley = ReadOctaves(worley, true);
        NodeReader perlin = r.Sub("perlin");
        if (perlin != null) n.Perlin = ReadOctaves(perlin, false);
        r.Finish();
        return n;
    }

    private static NoiseOctaves ReadOctaves(NodeReader r, bool worley)
    {
        var o = new NoiseOctaves
        {
            Octaves = r.Number("octaves", 4.0),
            Periods = r.Number("periods", 1.0),
            Persistence = r.Number("persistence", 0.5),
            Lacunarity = r.Number("lacunarity", 2.0),
        };
        if (worley) o.Spherical = r.Number("spherical", 0.0);
        r.Finish();
        return o;
    }

    private static CurlNoiseSettings ReadCurl(NodeReader r)
    {
        var c = new CurlNoiseSettings
        {
            Octaves = r.Number("octaves", 8.0),
            Periods = r.Number("periods", 1.0),
            Contrast = r.Number("contrast", 1.0),
            Smooth = r.Flag("smooth", false),
            Tiling = r.Number("tiling", 1.0),
            Strength = r.Number("strength", 1.0),
        };
        r.Finish();
        return c;
    }

    private static RaymarchSettings ReadRaymarching(NodeReader r)
    {
        var s = new RaymarchSettings
        {
            LightMarchSteps = r.Number("lightMarchSteps", 2.0),
            LightMarchDistance = r.Number("lightMarchDistance", 400.0),
            BaseStepSize = r.Number("baseStepSize", 45.0),
            AdaptiveStepSizeFactor = r.Number("adaptiveStepSizeFactor", 0.0022),
            MaxStepSize = r.Number("maxStepSize", 180.0),
            FxOnlyLayer = r.Flag("fxOnlyLayer", false),
            OverlapRenderOrder = r.Number("overlapRenderOrder", 1.0),
            ContinuousAccumulationDistance = r.Number("continuousAccumulationDistance", 50000.0),
        };
        r.Finish();
        return s;
    }

    private static CloudPhase ReadPhase(NodeReader r)
    {
        var defaults = new CloudPhase();
        var p = new CloudPhase
        {
            SingleScattering1 = Lobe(r.Pair("singleScattering1", Pair(defaults.SingleScattering1))),
            SingleScattering2 = Lobe(r.Pair("singleScattering2", Pair(defaults.SingleScattering2))),
            MultipleScattering1 = Lobe(r.Pair("multipleScattering1", Pair(defaults.MultipleScattering1))),
            MultipleScattering2 = Lobe(r.Pair("multipleScattering2", Pair(defaults.MultipleScattering2))),
        };
        r.Finish();
        return p;
    }

    private static PhaseLobe Lobe(Double2 v) => new PhaseLobe(v.X, v.Y);
    private static Double2 Pair(PhaseLobe l) => new Double2(l.Eccentricity, l.Brightness);

    private static CloudType ReadCloudType(NodeReader r)
    {
        var t = new CloudType
        {
            Name = r.Text("typeName", "New cloud type"),
            MinAltitude = r.Number("minAltitude", 0.0),
            MaxAltitude = r.Number("maxAltitude", 0.0),
            Density = r.Number("density", 0.1),
            BaseNoiseTiling = r.Number("baseNoiseTiling", 1000.0),
            NoiseEdgeHardness = r.Number("noiseEdgeHardness", 0.0),
            MultipleScatteringBrightness = r.Number("multipleScatteringBrightness", 1.0),
            CurlNoiseStrength = r.Number("curlNoiseStrength", 1.0),
            ParticleFieldDensity = r.Number("particleFieldDensity", 1.0),
            DropletsDensity = r.Number("dropletsDensity", 1.0),
            WetSurfacesIntensity = r.Number("wetSurfacesIntensity", 1.0),
            LightningFrequency = r.Number("lightningFrequency", 1.0),
            AmbientVolume = r.Number("ambientVolume", 1.0),
            InterpolateCloudHeights = r.Flag("interpolateCloudHeights", true),
        };
        t.CoverageCurve = r.Curve("coverageCurve") ?? t.CoverageCurve;
        t.DensityCurve = r.Curve("densityCurve") ?? t.DensityCurve;
        if (t.MaxAltitude < t.MinAltitude) r.Note(IssueSeverity.Warning, $"cloud type {t.Name}: maxAltitude below minAltitude");
        r.Finish();
        return t;
    }

    // ---------------------------------------------------------------- quality and effects

    private static CloudsQuality ReadQuality(NodeReader r)
    {
        var q = new CloudsQuality();
        string upscaling = r.Text("temporalUpscaling");
        if (upscaling.Length > 0)
        {
            string digits = upscaling.TrimStart('x', 'X');
            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int factor) && Array.IndexOf(CloudsQuality.UpscalingFactors, factor) >= 0)
                q.TemporalUpscaling = factor;
            else
                r.Note(IssueSeverity.Warning, $"temporalUpscaling '{upscaling}' is not one of x1, x2, x3, x4, x6, x8, x9, x12, x16, x32; using x{q.TemporalUpscaling}");
        }
        q.NonTiling3DNoise = r.Flag("nonTiling3DNoise", q.NonTiling3DNoise);
        q.RenderCloudsInReflectionProbes = r.Flag("renderCloudsInReflectionProbes", q.RenderCloudsInReflectionProbes);
        q.ScreenshotModeDenoisingIterations = r.Number("screenshotModeDenoisingIterations", q.ScreenshotModeDenoisingIterations);
        NodeReader light = r.Sub("lightVolumeSettings");
        if (light != null)
        {
            q.HorizontalResolution = light.Number("horizontalResolution", q.HorizontalResolution);
            q.VerticalResolution = light.Number("verticalResolution", q.VerticalResolution);
            q.StepCount = light.Number("stepCount", q.StepCount);
            q.DirectLightTimeSlicing = light.Number("directLightTimeSlicing", q.DirectLightTimeSlicing);
            q.AmbientLightTimeSlicing = light.Number("ambientLightTimeSlicing", q.AmbientLightTimeSlicing);
            q.TimewarpRateMultiplier = light.Number("timewarpRateMultiplier", q.TimewarpRateMultiplier);
            light.Finish();
        }
        r.Finish();
        return q;
    }

    private static ParticleFieldConfig ReadParticleField(NodeReader r)
    {
        var c = new ParticleFieldConfig
        {
            Name = r.Text("name"),
            FieldSize = r.Number("fieldSize", 0.0),
            FieldParticleCount = r.Number("fieldParticleCount", 0.0),
            FallSpeed = r.Number("fallSpeed", 0.0),
            TangentialSpeed = r.Number("tangentialSpeed", 0.0),
            RandomDirectionStrength = r.Number("randomDirectionStrength", 0.0),
            Color = r.Color("color", CfgColor.White),
            ParticleSize = r.Number("particleSize", 0.0),
            ParticleSheetCount = r.Pair("particleSheetCount", new Double2(1, 1)),
            ParticleStretch = r.Number("particleStretch", 0.0),
            MinCoverageThreshold = r.Number("minCoverageThreshold", 0.0),
            MaxCoverageThreshold = r.Number("maxCoverageThreshold", 1.0),
            ParticleTexture = r.Texture("particleTexture"),
        };
        NodeReader splashes = r.Sub("splashes");
        if (splashes != null)
        {
            c.Splashes = new SplashesConfig
            {
                SplashesSpeed = splashes.Number("splashesSpeed", 0.0),
                SplashesSize = splashes.Pair("splashesSize", default),
                SplashesSheetCount = splashes.Pair("splashesSheetCount", new Double2(1, 1)),
                Color = splashes.Color("color", CfgColor.White),
                SplashTexture = splashes.Texture("splashTexture"),
            };
            splashes.Finish();
        }
        r.Finish();
        return c;
    }

    private static LightningConfig ReadLightning(NodeReader r)
    {
        var c = new LightningConfig
        {
            Name = r.Text("name"),
            SpawnChancePerSecond = r.Number("spawnChancePerSecond", 0.0),
            MinSpawnRange = r.Number("minSpawnRange", 0.0),
            LifeTime = r.Number("lifeTime", 0.0),
            LightColor = r.Color("lightColor", CfgColor.White),
            LightIntensity = r.Number("lightIntensity", 0.0),
            LightRange = r.Number("lightRange", 0.0),
            SpawnAltitude = r.Number("spawnAltitude", 0.0),
            BoltColor = r.Color("boltColor", CfgColor.White),
            BoltWidth = r.Number("boltWidth", 0.0),
            BoltHeight = r.Number("boltHeight", 0.0),
            BoltTexture = r.Texture("boltTexture"),
            LightningSheetCount = r.Pair("lightningSheetCount", new Double2(1, 1)),
            SoundMinDistance = r.Number("soundMinDistance", 0.0),
            SoundMaxDistance = r.Number("soundMaxDistance", 0.0),
            SoundFarThreshold = r.Number("soundFarThreshold", 0.0),
            RealisticAudioDelayMultiplier = r.Number("realisticAudioDelayMultiplier", 0.0),
        };
        foreach (NodeReader item in r.Items("nearSounds")) { c.NearSounds.Add(item.Text("soundName")); item.Finish(); }
        foreach (NodeReader item in r.Items("farSounds")) { c.FarSounds.Add(item.Text("soundName")); item.Finish(); }
        r.Finish();
        return c;
    }

    private static DropletsConfig ReadDroplets(NodeReader r)
    {
        var c = new DropletsConfig
        {
            Name = r.Text("name"),
            MinCoverageThreshold = r.Number("minCoverageThreshold", 0.0),
            MaxCoverageThreshold = r.Number("maxCoverageThreshold", 1.0),
            FadeOutStartSpeed = r.Number("fadeOutStartSpeed", 0.0),
            FadeOutEndSpeed = r.Number("fadeOutEndSpeed", 0.0),
            RefractionStrength = r.Number("refractionStrength", 0.0),
            Translucency = r.Number("translucency", 0.0),
            Color = r.Vector("color", new Double3(1, 1, 1)),
            SpecularStrength = r.Number("specularStrength", 0.0),
            Scale = r.Number("scale", 1.0),
            TriplanarTransitionSharpness = r.Number("triplanarTransitionSharpness", 0.0),
            DryingSpeed = r.Number("dryingSpeed", 0.0),
            SpeedIncreaseFactor = r.Number("speedIncreaseFactor", 0.0),
            MaxModulationSpeed = r.Number("maxModulationSpeed", 0.0),
            Noise = r.Texture("noise"),
            SideNoiseScale = r.Number("sideNoiseScale", 0.0),
            SideLowSpeedNoiseStrength = r.Number("sideLowSpeedNoiseStrength", 0.0),
            SideHighSpeedNoiseStrength = r.Number("sideHighSpeedNoiseStrength", 0.0),
            LowSpeedStreakRatio = r.Number("lowSpeedStreakRatio", 0.0),
            HighSpeedStreakRatio = r.Number("highSpeedStreakRatio", 0.0),
            LowSpeedTimeRandomness = r.Number("lowSpeedTimeRandomness", 0.0),
            HighSpeedTimeRandomness = r.Number("highSpeedTimeRandomness", 0.0),
            TopNoiseScale = r.Number("topNoiseScale", 0.0),
            TopNoiseStrength = r.Number("topNoiseStrength", 0.0),
        };
        foreach (NodeReader item in r.Items("sideDropletLayers"))
        {
            c.SideDropletLayers.Add(new SideDropletLayer
            {
                Name = item.Text("name"),
                FallSpeed = item.Number("fallSpeed", 0.0),
                Scale = item.Number("scale", 1.0),
                DropletToTrailAspectRatio = item.Number("dropletToTrailAspectRatio", 0.0),
                StreakRatio = item.Number("streakRatio", 0.0),
            });
            item.Finish();
        }
        foreach (NodeReader item in r.Items("topDropletLayers"))
        {
            c.TopDropletLayers.Add(new TopDropletLayer { Name = item.Text("name"), Scale = item.Number("scale", 1.0), Speed = item.Number("speed", 0.0) });
            item.Finish();
        }
        r.Finish();
        return c;
    }

    private static WetSurfacesConfig ReadWetSurfaces(NodeReader r)
    {
        var c = new WetSurfacesConfig
        {
            Name = r.Text("name"),
            Body = r.Text("body"),
            MinCoverageThreshold = r.Number("minCoverageThreshold", 0.0),
            MaxCoverageThreshold = r.Number("maxCoverageThreshold", 1.0),
            PuddlesTexture = r.Texture("puddlesTexture"),
            PuddleTextureScale = r.Number("puddleTextureScale", 1.0),
            RippleSpeed = r.Number("rippleSpeed", 1.0),
            RippleScale = r.Number("rippleScale", 1.0),
        };
        NodeReader scenery = r.Sub("scenery");
        if (scenery != null) c.Scenery = ReadWetting(scenery, true);
        NodeReader terrain = r.Sub("terrain");
        if (terrain != null) c.Terrain = ReadWetting(terrain, true);
        NodeReader craft = r.Sub("craft");
        if (craft != null) c.Craft = ReadWetting(craft, false);
        r.Finish();
        return c;
    }

    private static Wetting ReadWetting(NodeReader r, bool puddles)
    {
        var w = new Wetting
        {
            WetnessAccumulationSpeed = r.Number("wetnessAccumulationSpeed", 0.0),
            WetnessDryingSpeed = r.Number("wetnessDryingSpeed", 0.0),
            WetDiffuse = r.Number("wetDiffuse", 1.0),
            WetSmoothness = r.Number("wetSmoothness", 0.0),
        };
        if (puddles)
        {
            w.PuddleAccumulationSpeed = r.Number("puddleAccumulationSpeed", 0.0);
            w.PuddleDryingSpeed = r.Number("puddleDryingSpeed", 0.0);
            w.MaxPuddleAccumulation = r.Number("maxPuddleAccumulation", 0.0);
        }
        r.Finish();
        return w;
    }

    private static EclipseShadows ReadShadows(NodeReader r)
    {
        var s = new EclipseShadows { Body = r.Text("body") };
        s.Casters.AddRange(r.All("caster"));
        r.Finish();
        return s;
    }

    private static PqsOverride ReadPqs(NodeReader r)
    {
        var p = new PqsOverride { Body = r.Text("body"), DeactivateDistance = r.Number("deactivateDistance", 0.0) };
        NodeReader kill = r.Sub("overrideKillSphere");
        if (kill != null)
        {
            p.OverridesKillSphere = true;
            p.KillSphereAltitude = kill.Number("altitude", 0.0);
            kill.Finish();
        }
        r.Finish();
        return p;
    }

    /// <summary>Layers naming effect configs that are not there are reported (EVE ignores them silently).</summary>
    private static void CheckReferences(EveCloudsSet set)
    {
        foreach (CloudLayer layer in set.Layers)
        {
            VolumetricLayer v = layer.Volume;
            if (v == null) continue;
            string path = CloudsNode + "/" + layer.Name;
            if (v.HasParticleField && v.ParticleField.Length > 0 && !set.ParticleFields.ContainsKey(v.ParticleField))
                set.Issues.Add(new ConfigIssue(IssueSeverity.Warning, path, $"particle field '{v.ParticleField}' is not configured"));
            if (v.HasLightning && v.Lightning.Length > 0 && !set.Lightning.ContainsKey(v.Lightning))
                set.Issues.Add(new ConfigIssue(IssueSeverity.Warning, path, $"lightning '{v.Lightning}' is not configured"));
            if (v.HasDroplets && v.Droplets.Length > 0 && !set.Droplets.ContainsKey(v.Droplets))
                set.Issues.Add(new ConfigIssue(IssueSeverity.Warning, path, $"droplets '{v.Droplets}' are not configured"));
            if (v.HasWetSurfaces && v.WetSurfaces.Length > 0 && !set.WetSurfaces.ContainsKey(v.WetSurfaces))
                set.Issues.Add(new ConfigIssue(IssueSeverity.Warning, path, $"wet surfaces '{v.WetSurfaces}' are not configured"));
        }
    }
}

/// <summary>
/// Reads one config node, remembering which keys and child nodes were used, so <see cref="Finish"/> can report the rest
/// (a misspelt key, or one the format does not have).
/// </summary>
internal sealed class NodeReader
{
    /// <summary>
    /// Keys packs still carry that EVE's release 5 also ignores: its older noise settings (detail noise; Worley noise's
    /// brightness, contrast and lift), the older 2D flow settings, and packs' own bookkeeping (Tag). Not reported.
    /// </summary>
    private static readonly HashSet<string> Leftovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "detailNoise", "detailNoiseTiling", "detailNoiseStrength", "brightness", "contrast", "lift",
        "_speed", "_displacement", "_flowSpeed", "_flowStrength", "Tag",
    };

    private readonly CfgNode _node;
    private readonly List<ConfigIssue> _issues;
    private readonly HashSet<string> _used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public NodeReader(CfgNode node, string path, List<ConfigIssue> issues)
    {
        _node = node;
        Path = path;
        _issues = issues;
    }

    public string Path { get; }

    public void Note(IssueSeverity severity, string message) => _issues.Add(new ConfigIssue(severity, Path, message));

    /// <summary>Keys or nodes the format allows but KSPVE does not need: not reported.</summary>
    public void Ignore(params string[] names)
    {
        foreach (string name in names) _used.Add(name);
    }

    public string Text(string key, string fallback = "")
    {
        _used.Add(key);
        string value = _node.GetLast(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>Every value of a key that may repeat.</summary>
    public List<string> All(string key)
    {
        _used.Add(key);
        var values = new List<string>();
        foreach (KeyValuePair<string, string> v in _node.Values)
            if (string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v.Value)) values.Add(v.Value.Trim());
        return values;
    }

    public double Number(string key, double fallback)
    {
        string text = Text(key, null);
        if (text == null) return fallback;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !double.IsNaN(value)) return value;
        Note(IssueSeverity.Warning, $"{key} '{text}' is not a number; using {fallback.ToString(CultureInfo.InvariantCulture)}");
        return fallback;
    }

    public bool Flag(string key, bool fallback)
    {
        string text = Text(key, null);
        if (text == null) return fallback;
        switch (text.ToLowerInvariant())
        {
            case "true": case "1": case "yes": case "on": return true;
            case "false": case "0": case "no": case "off": return false;
        }
        Note(IssueSeverity.Warning, $"{key} '{text}' is not true or false; using {fallback}");
        return fallback;
    }

    private double[] Numbers(string key, int least, int most)
    {
        string text = Text(key, null);
        if (text == null) return null;
        string[] parts = text.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < least || parts.Length > most)
        {
            Note(IssueSeverity.Warning, $"{key} '{text}' should have {(least == most ? least.ToString(CultureInfo.InvariantCulture) : least + "-" + most)} numbers; ignored");
            return null;
        }
        var numbers = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                Note(IssueSeverity.Warning, $"{key} '{text}' is not a list of numbers; ignored");
                return null;
            }
        }
        return numbers;
    }

    public Double2 Pair(string key, Double2 fallback)
    {
        double[] n = Numbers(key, 2, 2);
        return n == null ? fallback : new Double2(n[0], n[1]);
    }

    public Double3 Vector(string key, Double3 fallback)
    {
        double[] n = Numbers(key, 3, 3);
        return n == null ? fallback : new Double3(n[0], n[1], n[2]);
    }

    public CfgColor Color(string key, CfgColor fallback)
    {
        double[] n = Numbers(key, 3, 4);
        return n == null ? fallback : new CfgColor(n[0], n[1], n[2], n.Length > 3 ? n[3] : 255.0);
    }

    public TEnum Choice<TEnum>(string key, TEnum fallback) where TEnum : struct
    {
        string text = Text(key, null);
        if (text == null) return fallback;
        if (Enum.TryParse(text, true, out TEnum value) && Enum.IsDefined(typeof(TEnum), value)) return value;
        Note(IssueSeverity.Warning, $"{key} '{text}' should be one of {string.Join(", ", Enum.GetNames(typeof(TEnum)))}; using {fallback}");
        return fallback;
    }

    /// <summary>The child node <paramref name="name"/> as a reader, or null.</summary>
    public NodeReader Sub(string name)
    {
        _used.Add(name);
        CfgNode child = _node.Children.Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return child == null ? null : new NodeReader(child, Path + "/" + name, _issues);
    }

    /// <summary>The entries of a list node (its children, usually "Item"), named by their name keys where they have one.</summary>
    public IEnumerable<NodeReader> Items(string listName)
    {
        _used.Add(listName);
        CfgNode list = _node.Children.Find(c => string.Equals(c.Name, listName, StringComparison.OrdinalIgnoreCase));
        if (list == null) yield break;
        int index = 0;
        foreach (CfgNode item in list.Children)
        {
            string label = item.GetLast("typeName") ?? item.GetLast("name") ?? item.GetLast("tileName") ?? index.ToString(CultureInfo.InvariantCulture);
            index++;
            yield return new NodeReader(item, Path + "/" + listName + "/" + label, _issues);
        }
    }

    /// <summary>A texture, written as "key = path" or as a node with value, type, alphaMask, isClamped and format.</summary>
    public TextureRef Texture(string key)
    {
        var t = new TextureRef();
        NodeReader node = Sub(key);
        if (node == null)
        {
            t.Path = Text(key);
            return t;
        }
        t.Path = node.Text("value");
        t.Type = node.Text("type");
        t.IsClamped = node.Flag("isClamped", false);
        t.Format = node.Text("format");
        string mask = node.Text("alphaMask");
        if (mask.Length > 0)
        {
            string channel = mask.StartsWith("ALPHAMAP_", StringComparison.OrdinalIgnoreCase) ? mask.Substring("ALPHAMAP_".Length) : mask;
            if (!Enum.TryParse(channel, true, out t.AlphaMask) || t.AlphaMask == AlphaMaskChannel.None)
                node.Note(IssueSeverity.Warning, $"alphaMask '{mask}' should be ALPHAMAP_R, _G, _B or _A");
        }
        node.Finish();
        return t;
    }

    /// <summary>A curve node's "key" lines, or null when there is none.</summary>
    public FloatCurve Curve(string name)
    {
        NodeReader node = Sub(name);
        if (node == null) return null;
        FloatCurve curve = FloatCurve.FromKeys(node.All("key"), bad => node.Note(IssueSeverity.Warning, $"key '{bad}' should be 'time value' or 'time value in out'"));
        node.Finish();
        return curve;
    }

    /// <summary>Reports the keys and child nodes that were never read.</summary>
    public void Finish()
    {
        var unread = new List<string>();
        foreach (KeyValuePair<string, string> v in _node.Values)
            if (!_used.Contains(v.Key) && !Leftovers.Contains(v.Key) && !unread.Contains(v.Key)) unread.Add(v.Key);
        foreach (CfgNode c in _node.Children)
            if (!_used.Contains(c.Name) && !Leftovers.Contains(c.Name) && !unread.Contains(c.Name + " { }")) unread.Add(c.Name + " { }");
        if (unread.Count > 0) Note(IssueSeverity.Warning, "not part of the format, ignored: " + string.Join(", ", unread));
    }
}
