using System;
using KSPVE.Pure.Water;
using UnityEngine;
using D3 = KSPVE.Pure.Numerics.Double3;

namespace KSPVE.Water;

/// <summary>
/// The light the water is shaded with, put in the shaders' globals once a frame (shared by the ocean and the rivers and lakes):
/// the sun's direction and light after the air has taken its share (SkyModel), and the sky's colours over the water. Brightness is in
/// KSP's own lighting units: the sun's Light colour times its intensity is what a white Lambert surface facing it reflects.
/// </summary>
internal static class WaterLight
{
    public static Vector3 SunDirection { get; private set; } = Vector3.up;
    public static Vector3 SunRadiance { get; private set; } = Vector3.one;

    /// <summary>Sets the globals for a body, <paramref name="up"/> being the local vertical where the water is seen.</summary>
    public static void Apply(CelestialBody body, Vector3 up)
    {
        Light sunLight = Sun.Instance != null ? Sun.Instance.sunLight : null;
        Vector3 sunDir = sunLight != null ? -sunLight.transform.forward : Vector3.up;
        double elevation = Math.Asin(Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(sunDir, up)))) * 180.0 / Math.PI;
        double density = body.atmosphere ? Math.Min(Math.Max(body.atmospherePressureSeaLevel / 101.325, 0.0), 8.0) : 0.0;
        D3 transmittance = SkyModel.SunTransmittance(elevation, density);
        Color sun = sunLight != null ? sunLight.color * sunLight.intensity : Color.white;
        SkyModel.Colors(elevation, density, out D3 zenith, out D3 horizon, out D3 ambient);
        SunDirection = sunDir;
        SunRadiance = new Vector3(sun.r * (float)transmittance.X, sun.g * (float)transmittance.Y, sun.b * (float)transmittance.Z);
        Shader.SetGlobalVector("_KSPVE_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        Shader.SetGlobalVector("_KSPVE_SunRadiance", new Vector4(SunRadiance.x, SunRadiance.y, SunRadiance.z, 0f));
        Shader.SetGlobalVector("_KSPVE_SkyZenith", new Vector4(sun.r * (float)zenith.X, sun.g * (float)zenith.Y, sun.b * (float)zenith.Z, 0f));
        Shader.SetGlobalVector("_KSPVE_SkyHorizon", new Vector4(sun.r * (float)horizon.X, sun.g * (float)horizon.Y, sun.b * (float)horizon.Z, 0f));
        Shader.SetGlobalVector("_KSPVE_SkyAmbient", new Vector4(sun.r * (float)ambient.X, sun.g * (float)ambient.Y, sun.b * (float)ambient.Z, 0f));
    }
}
