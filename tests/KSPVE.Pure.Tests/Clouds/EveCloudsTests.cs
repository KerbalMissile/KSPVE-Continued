using System;
using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Clouds;

public class CfgParserTests
{
    [Test]
    public void ReadsNodesValuesAndComments_InKspsLayout()
    {
        List<CfgNode> nodes = CfgParser.Parse(@"
// a comment
TOP
{
    name = first   // trailing comment
    url = a = b
    bare
    child { inline = 1 }
    child
    {
        deep = 2
    }
}
OTHER { }
");
        Assert.That(nodes.Select(n => n.Name), Is.EqualTo(new[] { "TOP", "OTHER" }));
        CfgNode top = nodes[0];
        Assert.That(top.GetLast("name"), Is.EqualTo("first"));
        Assert.That(top.GetLast("url"), Is.EqualTo("a = b"), "split at the first '='");
        Assert.That(top.GetLast("bare"), Is.EqualTo(string.Empty), "a key with no value");
        Assert.That(top.Children, Has.Count.EqualTo(2));
        Assert.That(top.Children[0].GetLast("inline"), Is.EqualTo("1"));
        Assert.That(top.Children[1].GetLast("deep"), Is.EqualTo("2"));
    }
}

public class FloatCurveTests
{
    [Test]
    public void FollowsItsKeysTangents_AndHoldsBeyondTheEnds()
    {
        FloatCurve line = FloatCurve.FromKeys(new[] { "0 0 1 1", "1 1 1 1" });
        Assert.That(line.Evaluate(0.25), Is.EqualTo(0.25).Within(1e-12), "tangents matching the slope: a straight line");
        Assert.That(line.Evaluate(-5.0), Is.EqualTo(0.0));
        Assert.That(line.Evaluate(5.0), Is.EqualTo(1.0));

        FloatCurve flat = FloatCurve.FromKeys(new[] { "0 0 0 0", "1 1 0 0" });
        Assert.That(flat.Evaluate(0.5), Is.EqualTo(0.5).Within(1e-12), "flat tangents: smoothstep through the middle");
        Assert.That(flat.Evaluate(0.25), Is.EqualTo(0.15625).Within(1e-12));
    }

    [Test]
    public void KeysWithoutTangents_GetSmoothOnes()
    {
        FloatCurve curve = FloatCurve.FromKeys(new[] { "0 0", "1 2", "2 4" });
        Assert.That(curve.Evaluate(0.5), Is.EqualTo(1.0).Within(1e-12), "points on a line stay on it");
        Assert.That(curve.Evaluate(1.5), Is.EqualTo(3.0).Within(1e-12));
        int bad = 0;
        Assert.That(FloatCurve.FromKeys(new[] { "nonsense", "1 2 3" }, _ => bad++), Is.Null);
        Assert.That(bad, Is.EqualTo(2));
    }
}

public class TimeCycleTests
{
    [Test]
    public void TwoWeatherLayers_TakeTurns_FadingAtTheirEnds()
    {
        // As a pack alternates its weather: one on for 9.8 h from 0, the other 8.2 h from 9.8 h, every 18 h.
        var first = new TimeCycle { Unit = TimeUnit.Hours, Duration = 9.8, Offset = 0.0, RepeatInterval = 18, FadeTime = 0.5 };
        var second = new TimeCycle { Unit = TimeUnit.Hours, Duration = 8.2, Offset = 9.8, RepeatInterval = 18, FadeTime = 0.5 };
        const double hour = 3600.0;
        Assert.That(first.FactorAt(5 * hour), Is.EqualTo(1.0));
        Assert.That(second.FactorAt(5 * hour), Is.EqualTo(0.0));
        Assert.That(second.FactorAt(14 * hour), Is.EqualTo(1.0));
        Assert.That(first.FactorAt(14 * hour), Is.EqualTo(0.0));
        Assert.That(first.FactorAt(0.25 * hour), Is.EqualTo(0.5).Within(1e-9), "half faded in");
        Assert.That(first.FactorAt((18 + 5) * hour), Is.EqualTo(1.0), "and again next cycle");
        for (double h = 0.0; h < 36.0; h += 0.1)
            Assert.That(first.FactorAt(h * hour) > 0.0 && second.FactorAt(h * hour) >= 1.0, Is.False, "never both fully there");
    }

    [Test]
    public void Seconds_AreTheDefaultUnit_AndAnAlwaysOnLayerStaysOn()
    {
        var seconds = new TimeCycle { Duration = 10, RepeatInterval = 20 };
        Assert.That(seconds.FactorAt(5.0), Is.EqualTo(1.0));
        Assert.That(seconds.FactorAt(15.0), Is.EqualTo(0.0));
        Assert.That(new TimeCycle { Duration = 20, RepeatInterval = 20 }.FactorAt(15.0), Is.EqualTo(1.0));
        Assert.That(new TimeCycle().FactorAt(123.0), Is.EqualTo(1.0), "no cycle: always there");
    }
}

public class CloudTypeBlendTests
{
    [Test]
    public void TypesSitEvenlyAlongTheMap_AndBlendBetween()
    {
        // Four types are stored at 0, 85, 171 and 255.
        int[] stored = { 0, 85, 171, 255 };
        for (int i = 0; i < 4; i++)
        {
            CloudTypeBlend.Pick(stored[i] / 255.0, 4, out int lower, out double towards);
            double type = lower + towards;
            Assert.That(type, Is.EqualTo(i).Within(0.02), $"type {i} (8-bit maps round)");
        }
        CloudTypeBlend.Pick(0.5, 3, out int l, out double t);
        Assert.That(l, Is.EqualTo(1));
        Assert.That(t, Is.EqualTo(0.0).Within(1e-12));
        CloudTypeBlend.Pick(0.25, 3, out l, out t);
        Assert.That((l, t), Is.EqualTo((0, 0.5)));
        CloudTypeBlend.Pick(0.7, 1, out l, out t);
        Assert.That((l, t), Is.EqualTo((0, 0.0)), "one type: always it");
    }
}

public class EveCloudsReaderTests
{
    private const string Pack = @"
EVE_CLOUDS
{
    OBJECT
    {
        name = Testworld-clouds
        body = Testworld
        altitude = 2000
        speed = 0,25,0
        speedMode = LinearSurface
        settings
        {
            _Color = 400,400,400,255
            _DetailTex = MyPack/detail
            _MainTex
            {
                value = MyPack/PluginData/main
                isClamped = True
            }
        }
        timeSettings
        {
            unit = Hours
            duration = 6
            repeatInterval = 12
            fadeTime = 1
            fadeMode = Density
        }
        layer2D
        {
            macroCloudMaterial
            {
                _DetailDist = 2E-06
            }
        }
        layerRaymarchedVolumeV5
        {
            color = 120, 130, 140, 255
            upwardsCloudSpeed = -10
            scaledFadeStartAltitude = 200000
            scaledFadeEndAltitude = 260000
            raymarchingSettings
            {
                baseStepSize = 75
                maxStepSize = 1500
                lightMarchSteps = 4
                overlapRenderOrder = 2
            }
            noise
            {
                erosionDepth = 0.65
                worley
                {
                    octaves = 3
                    spherical = 1
                }
            }
            coverageMap
            {
                value = MyPack/PluginData/coverage
                type = AlphaMap
                alphaMask = ALPHAMAP_R
            }
            phaseFunctions
            {
                singleScattering1 = 0.9, 0.05
            }
            particleField
            {
                particleFieldConfig = rain-Testworld
            }
            lightning
            {
                lightningConfig = missing-lightning
            }
            cloudTypes
            {
                Item
                {
                    typeName = Stratus
                    minAltitude = 1750
                    maxAltitude = 3450
                    density = 0.0015
                    coverageCurve
                    {
                        key = 0 0 0 0
                        key = 1 1 0 0
                    }
                }
                Item
                {
                    typeName = Cumulus
                    minAltitude = 1750
                    maxAltitude = 4450
                    misspeltKey = 3
                }
            }
        }
    }
}
EVE_PARTICLE_FIELD_CONFIG
{
    OBJECT
    {
        name = rain-Testworld
        fieldParticleCount = 200000
        particleSheetCount = 6,1
        color = 255,255,255,80
        splashes
        {
            splashesSize = 0.4,0.4
        }
    }
}
EVE_RAYMARCHED_CLOUDS_QUALITY
{
    OBJECT
    {
        temporalUpscaling = x12
        lightVolumeSettings
        {
            horizontalResolution = 224
            directLightTimeSlicing = 12
            maxTimewarpUpdateRateIncrease = 3
        }
    }
}
";

    private static EveCloudsSet Read(string text) => EveCloudsReader.Read(CfgParser.Parse(text));

    [Test]
    public void ALayer_IsReadWithEveryPart()
    {
        EveCloudsSet set = Read(Pack);
        CloudLayer layer = set.Layers.Single();
        Assert.That(layer.Name, Is.EqualTo("Testworld-clouds"));
        Assert.That(layer.Body, Is.EqualTo("Testworld"));
        Assert.That(layer.Altitude, Is.EqualTo(2000));
        Assert.That(layer.Speed, Is.EqualTo(new Double3(0, 25, 0)));
        Assert.That(layer.SpeedMode, Is.EqualTo(CloudSpeedMode.LinearSurface));
        Assert.That(layer.Material.Color, Is.EqualTo(new CfgColor(400, 400, 400, 255)));
        Assert.That(layer.Material.DetailTex.Path, Is.EqualTo("MyPack/detail"), "a texture as a plain value");
        Assert.That(layer.Material.MainTex.Path, Is.EqualTo("MyPack/PluginData/main"), "...or as a node");
        Assert.That(layer.Material.MainTex.IsClamped, Is.True);
        Assert.That(layer.Time.Unit, Is.EqualTo(TimeUnit.Hours));
        Assert.That(layer.Time.FadeMode, Is.EqualTo(TimeFadeMode.Density));
        Assert.That(layer.Layer2D, Is.Not.Null);

        VolumetricLayer v = layer.Volume;
        Assert.That(v.OlderFormat, Is.False);
        Assert.That(v.Color, Is.EqualTo(new CfgColor(120, 130, 140, 255)));
        Assert.That(v.UpwardsCloudSpeed, Is.EqualTo(-10));
        Assert.That(v.ScaledFadeStartAltitude, Is.EqualTo(200000));
        Assert.That(v.Raymarching.BaseStepSize, Is.EqualTo(75));
        Assert.That(v.Raymarching.AdaptiveStepSizeFactor, Is.EqualTo(0.0022), "unset: the format's default");
        Assert.That(v.Raymarching.OverlapRenderOrder, Is.EqualTo(2));
        Assert.That(v.Noise.ErosionDepth, Is.EqualTo(0.65));
        Assert.That(v.Noise.Worley.Spherical, Is.EqualTo(1));
        Assert.That(v.Noise.Perlin, Is.Null);
        Assert.That(v.CoverageMap.AlphaMask, Is.EqualTo(AlphaMaskChannel.R));
        Assert.That(v.CloudTypeMap.ChannelOr(AlphaMaskChannel.R), Is.EqualTo(AlphaMaskChannel.R), "type maps default to red");
        Assert.That(v.Phase.SingleScattering1.Eccentricity, Is.EqualTo(0.9));
        Assert.That(v.Phase.MultipleScattering1.Brightness, Is.EqualTo(3.0), "unset lobes keep their defaults");
        Assert.That(v.ParticleField, Is.EqualTo("rain-Testworld"));
        Assert.That(v.CloudTypes.Select(t => t.Name), Is.EqualTo(new[] { "Stratus", "Cumulus" }));
        Assert.That(v.CloudTypes[0].CoverageCurve.Evaluate(0.5), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(v.CloudTypes[1].Density, Is.EqualTo(0.1), "unset density: the default");
        Assert.That(v.MinAltitude, Is.EqualTo(1750));
        Assert.That(v.MaxAltitude, Is.EqualTo(4450));

        Assert.That(set.ParticleFields["rain-Testworld"].ParticleSheetCount, Is.EqualTo(new Double2(6, 1)));
        Assert.That(set.ParticleFields["rain-Testworld"].Splashes.SplashesSize, Is.EqualTo(new Double2(0.4, 0.4)));
    }

    [Test]
    public void WhatCannotBeUsed_IsReported_NotFatal()
    {
        EveCloudsSet set = Read(Pack);
        string[] issues = set.Issues.Select(i => i.ToString()).ToArray();
        Assert.That(issues, Has.Some.Contains("misspeltKey"), "an unknown key in a cloud type");
        Assert.That(issues, Has.Some.Contains("missing-lightning"), "a layer naming lightning that is not configured");
        Assert.That(issues, Has.Some.Contains("maxTimewarpUpdateRateIncrease"), "a key the quality node does not have");
        Assert.That(set.Issues, Has.Count.EqualTo(3), string.Join("\n", issues));
    }

    [Test]
    public void TheQualityNode_SetsUpscalingAndTheLightVolume()
    {
        CloudsQuality q = Read(Pack).Quality;
        Assert.That(q.TemporalUpscaling, Is.EqualTo(12));
        Assert.That(q.HorizontalResolution, Is.EqualTo(224));
        Assert.That(q.DirectLightTimeSlicing, Is.EqualTo(12));
        Assert.That(q.VerticalResolution, Is.EqualTo(32), "unset: the format's default");
        Assert.That(Read("EVE_RAYMARCHED_CLOUDS_QUALITY { OBJECT { temporalUpscaling = x5 } }").Quality.TemporalUpscaling, Is.EqualTo(9),
            "a factor the format does not have: the default");
    }

    [Test]
    public void TheOlderLayerName_IsStillRead_WithANote()
    {
        EveCloudsSet set = Read(@"
EVE_CLOUDS
{
    OBJECT
    {
        name = Old-layer
        body = Oldworld
        layerRaymarchedVolume
        {
            detailNoiseTiling = 5000
            noise
            {
                worley
                {
                    octaves = 3
                    brightness = 1.2
                }
            }
            cloudTypes
            {
                Item
                {
                    typeName = Low
                    minAltitude = 100
                    maxAltitude = 900
                    detailNoiseStrength = 0.3
                }
            }
        }
    }
}");
        VolumetricLayer v = set.Layers.Single().Volume;
        Assert.That(v, Is.Not.Null);
        Assert.That(v.OlderFormat, Is.True);
        Assert.That(v.CloudTypes.Single().MaxAltitude, Is.EqualTo(900));
        Assert.That(set.Issues, Has.Count.EqualTo(1), string.Join("\n", set.Issues));
        Assert.That(set.Issues[0].Message, Does.Contain("release 1-4"));
    }

    [Test]
    public void EffectsShadowsAndPqs_AreRead()
    {
        EveCloudsSet set = Read(@"
EVE_LIGHTNING_CONFIG
{
    OBJECT
    {
        name = bolts
        spawnChancePerSecond = 2
        lightningSheetCount = 4,3
        nearSounds
        {
            Item { soundName = A/near1 }
            Item { soundName = A/near2 }
        }
        farSounds
        {
            Item { soundName = A/far }
        }
    }
}
EVE_DROPLETS_CONFIG
{
    OBJECT
    {
        name = drops
        sideDropletLayers
        {
            Item
            {
                name = Fast
                fallSpeed = 0.15
            }
        }
        topDropletLayers
        {
            Item
            {
                name = Top
                speed = 0.1
            }
        }
    }
}
EVE_WET_SURFACES_CONFIG
{
    OBJECT
    {
        name = wet
        body = Testworld
        scenery { maxPuddleAccumulation = 0.75 }
        craft { wetSmoothness = 0.2 }
    }
}
EVE_SHADOWS
{
    OBJECT
    {
        body = Moon
        caster = Planet
        caster = OtherMoon
    }
}
PQS_MANAGER
{
    OBJECT
    {
        body = Giant
        deactivateDistance = 175000
        overrideKillSphere { altitude = -75000 }
    }
}");
        Assert.That(set.Lightning["bolts"].NearSounds, Is.EqualTo(new[] { "A/near1", "A/near2" }));
        Assert.That(set.Lightning["bolts"].LightningSheetCount, Is.EqualTo(new Double2(4, 3)));
        Assert.That(set.Droplets["drops"].SideDropletLayers.Single().FallSpeed, Is.EqualTo(0.15));
        Assert.That(set.Droplets["drops"].TopDropletLayers.Single().Speed, Is.EqualTo(0.1));
        Assert.That(set.WetSurfaces["wet"].Scenery.MaxPuddleAccumulation, Is.EqualTo(0.75));
        Assert.That(set.WetSurfaces["wet"].Craft.WetSmoothness, Is.EqualTo(0.2));
        Assert.That(set.Shadows.Single().Casters, Is.EqualTo(new[] { "Planet", "OtherMoon" }));
        Assert.That(set.Pqs.Single().KillSphereAltitude, Is.EqualTo(-75000));
        Assert.That(set.Issues, Is.Empty, string.Join("\n", set.Issues));
    }
}
