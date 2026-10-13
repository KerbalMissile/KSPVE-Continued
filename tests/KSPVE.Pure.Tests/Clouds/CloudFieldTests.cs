using System;
using System.Collections.Generic;
using System.Linq;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Numerics;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Clouds;

public class NoiseVolumeTests
{
    [Test]
    public void TheNoise_Tiles_IsTheSameEveryTime_AndSpansZeroToOne()
    {
        var settings = new CloudNoise { Worley = new NoiseOctaves { Octaves = 3, Periods = 2 }, Perlin = new NoiseOctaves { Octaves = 3, Periods = 2 } };
        NoiseVolume a = NoiseVolume.Generate(settings, 24), b = NoiseVolume.Generate(settings, 24);
        Assert.That(a.Data, Is.EqualTo(b.Data));
        Assert.That(a.Data.Min(), Is.EqualTo(0f).Within(1e-6));
        Assert.That(a.Data.Max(), Is.EqualTo(1f).Within(1e-6));
        for (int i = 0; i < 50; i++)
        {
            double u = i * 0.137, v = i * 0.071, w = i * 0.293;
            Assert.That(a.Sample(u + 1, v - 2, w + 3), Is.EqualTo(a.Sample(u, v, w)).Within(1e-5), "tiles in every direction");
        }
        // Seamless: across the tile's edge it changes no more than inside it.
        double edge = Math.Abs(a.Sample(0.999, 0.4, 0.4) - a.Sample(1.001, 0.4, 0.4));
        Assert.That(edge, Is.LessThan(0.1));
    }

    [Test]
    public void PuffierWorleyCells_AreFuller()
    {
        NoiseVolume flat = NoiseVolume.Generate(new CloudNoise { Worley = new NoiseOctaves { Octaves = 1, Periods = 3, Spherical = 0 } }, 24);
        NoiseVolume puffy = NoiseVolume.Generate(new CloudNoise { Worley = new NoiseOctaves { Octaves = 1, Periods = 3, Spherical = 1 } }, 24);
        Assert.That(puffy.Data.Average(v => v), Is.GreaterThan(flat.Data.Average(v => v)));
        Assert.That(NoiseVolume.Generate(new CloudNoise(), 8).Data.All(v => v == 1f), Is.True, "no noise: the coverage alone");
        Assert.That(NoiseVolume.KeyOf(new CloudNoise { Worley = new NoiseOctaves() }, 32), Is.Not.EqualTo(NoiseVolume.KeyOf(new CloudNoise { Perlin = new NoiseOctaves() }, 32)));
    }
}

public class CloudFieldTests
{
    private sealed class Flat : IGreyMap
    {
        private readonly double _value;
        public Flat(double value) { _value = value; }
        public double Sample(double u, double v) => _value;
    }

    /// <summary>A map that is its u coordinate (so longitude can be told apart).</summary>
    private sealed class Ramp : IGreyMap
    {
        public double Sample(double u, double v) => u - Math.Floor(u);
    }

    private const double Radius = 600000.0;

    private static LayerModel Layer(double coverage, params CloudType[] types)
    {
        var volume = new VolumetricLayer { Noise = new CloudNoise { ErosionDepth = 0.0 } };
        volume.CloudTypes.AddRange(types.Length > 0 ? types : new[] { new CloudType { Name = "Cumulus", MinAltitude = 2000, MaxAltitude = 5000, Density = 0.01 } });
        volume.CoverageMap.Path = "cov";
        var layer = new CloudLayer { Name = "test", Body = "Test", Volume = volume };
        return new LayerModel { Layer = layer, Volume = volume, PlanetRadius = Radius, Coverage = new Flat(coverage), Role = LayerModel.RoleOf(layer) };
    }

    private static Double3 At(double altitude, double lonDeg = 30, double latDeg = 10)
    {
        double lat = latDeg * Math.PI / 180, lon = lonDeg * Math.PI / 180;
        return new Double3(Math.Cos(lat) * Math.Cos(lon), Math.Sin(lat), Math.Cos(lat) * Math.Sin(lon)) * (Radius + altitude);
    }

    [Test]
    public void Clouds_StayInTheirTypesHeightBand()
    {
        LayerModel m = Layer(1.0);
        Assert.That(CloudField.Sample(m, At(1999), 0, default, false).Density, Is.Zero);
        Assert.That(CloudField.Sample(m, At(3500), 0, default, false).Density, Is.EqualTo(0.01).Within(1e-12), "full coverage, no erosion: the type's density");
        Assert.That(CloudField.Sample(m, At(5001), 0, default, false).Density, Is.Zero);
        Assert.That(CloudField.Sample(m, At(3500), 0, default, false).HeightFraction, Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void MoreCoverage_MoreCloud_AndNoneWithoutIt()
    {
        var noisy = new CloudNoise { ErosionDepth = 1.0, Worley = new NoiseOctaves { Octaves = 2, Periods = 2 } };
        NoiseVolume noise = NoiseVolume.Generate(noisy, 16);
        double Filled(double coverage)
        {
            LayerModel m = Layer(coverage);
            m.Volume.Noise = noisy;
            m.Noise = noise;
            int cloudy = 0;
            for (int i = 0; i < 400; i++) if (CloudField.Sample(m, At(3500, i * 0.05, (i % 20) * 0.5), 0, default, false).Density > 0) cloudy++;
            return cloudy / 400.0;
        }
        Assert.That(Filled(0.0), Is.Zero);
        Assert.That(Filled(0.3), Is.LessThan(Filled(0.7)));
        Assert.That(Filled(1.0), Is.GreaterThan(0.95));
    }

    [Test]
    public void TheCoverageCurve_ShapesTheColumn()
    {
        var anvil = new CloudType
        {
            Name = "Anvil", MinAltitude = 0, MaxAltitude = 1000, Density = 0.01,
            CoverageCurve = FloatCurve.FromKeys(new[] { "0 0 0 0", "0.5 0 0 0", "1 1 0 0" }),
        };
        LayerModel m = Layer(1.0, anvil);
        Assert.That(CloudField.Sample(m, At(200), 0, default, false).Density, Is.Zero, "nothing low down");
        Assert.That(CloudField.Sample(m, At(999), 0, default, false).Density, Is.GreaterThan(0), "spreading out at the top");
    }

    [Test]
    public void TheTypeMap_BlendsNeighbouringTypes()
    {
        var low = new CloudType { Name = "Low", MinAltitude = 1000, MaxAltitude = 2000, Density = 0.01 };
        var high = new CloudType { Name = "High", MinAltitude = 1000, MaxAltitude = 6000, Density = 0.03 };
        LayerModel m = Layer(1.0, low, high);
        m.CloudType = new Flat(0.0);
        Assert.That(CloudField.Sample(m, At(1500), 0, default, false).Density, Is.EqualTo(0.01).Within(1e-12));
        Assert.That(CloudField.Sample(m, At(3000), 0, default, false).Density, Is.Zero, "the low type does not reach 3 km");
        m.CloudType = new Flat(1.0);
        Assert.That(CloudField.Sample(m, At(3000), 0, default, false).Density, Is.EqualTo(0.03).Within(1e-12));
        m.CloudType = new Flat(0.5);
        Assert.That(CloudField.Sample(m, At(1300), 0, default, false).Density, Is.EqualTo(0.02).Within(1e-12), "halfway: half of each");
    }

    [Test]
    public void TheLayer_TurnsRoundTheAxis_AtItsSpeed()
    {
        LayerModel m = Layer(1.0);
        m.Coverage = new Ramp();
        m.Layer.Speed = new Double3(0, 100, 0);   // 100 m/s at the surface
        m.Layer.SpeedMode = CloudSpeedMode.LinearSurface;
        double angle = CloudField.LayerAngle(m.Layer, Radius, 600.0);
        Assert.That(angle, Is.EqualTo(100.0 * 600.0 / Radius).Within(1e-12));
        // A point carried round with the layer (east, by the angle it turned) sees the same coverage.
        Double3 p = At(3000, 40);
        Double3 carried = At(3000, 40 + angle * 180 / Math.PI);
        CloudField.MapUv(CloudField.IntoLayer(p, 0.0) / p.Length, out double u0, out _);
        CloudField.MapUv(CloudField.IntoLayer(carried, angle) / carried.Length, out double u1, out _);
        Assert.That(u1, Is.EqualTo(u0).Within(1e-9));
    }

    [Test]
    public void TimersAndWeatherRoles_SetWhenAndWhereLayersAre()
    {
        LayerModel m = Layer(0.8);
        m.Layer.Time = new TimeCycle { Unit = TimeUnit.Seconds, Duration = 10, RepeatInterval = 20, FadeMode = TimeFadeMode.Coverage };
        Assert.That(CloudField.Coverage(m, 0.3, 0.5, default, false, 5.0), Is.EqualTo(0.8).Within(1e-12));
        Assert.That(CloudField.Coverage(m, 0.3, 0.5, default, false, 15.0), Is.Zero, "off half the cycle");

        // A cloud layer that can rain is still ordinary cloud (thicker where it is moist), not only where it rains.
        m.Volume.HasParticleField = true;
        Assert.That(LayerModel.RoleOf(m.Layer), Is.EqualTo(WeatherRole.Base));
        // A layer drawn only for its rain follows where it rains, not its timer, when the weather is on.
        m.Volume.Raymarching.FxOnlyLayer = true;
        m.Role = LayerModel.RoleOf(m.Layer);
        Assert.That(m.Role, Is.EqualTo(WeatherRole.Precipitation));
        var raining = new WeatherSample { Precipitation = 1.0 };
        var dry = new WeatherSample { Precipitation = 0.0 };
        Assert.That(CloudField.Coverage(m, 0.3, 0.5, raining, true, 15.0), Is.EqualTo(0.8).Within(1e-12), "raining here, whatever the timer");
        Assert.That(CloudField.Coverage(m, 0.3, 0.5, dry, true, 5.0), Is.Zero, "dry here");

        LayerModel storm = Layer(0.9, new CloudType { Name = "Cumulonimbus core", MinAltitude = 0, MaxAltitude = 10000 });
        Assert.That(storm.Role, Is.EqualTo(WeatherRole.Convective));
        Assert.That(CloudField.Coverage(storm, 0, 0.5, new WeatherSample { Storm = 0 }, true, 0), Is.Zero);
        LayerModel ordinary = Layer(0.5);
        Assert.That(ordinary.Role, Is.EqualTo(WeatherRole.Base));
        Assert.That(CloudField.Coverage(ordinary, 0, 0.5, new WeatherSample { Humidity = 1 }, true, 0),
            Is.GreaterThan(CloudField.Coverage(ordinary, 0, 0.5, new WeatherSample { Humidity = 0 }, true, 0)), "moist air, more cloud");
    }
}
