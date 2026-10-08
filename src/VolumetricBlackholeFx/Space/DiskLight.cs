using System;
using VolumetricBlackholeFx.Core;
using VolumetricBlackholeFx.Pure.ColorScience;
using VolumetricBlackholeFx.Pure.Numerics;
using VolumetricBlackholeFx.Pure.Space;
using VolumetricBlackholeFx.Pure.Space.Relativity;
using UnityEngine;

namespace VolumetricBlackholeFx.Space;

/// <summary>
/// Lights vessels with the glow of a black hole's accretion disk: a key light shining out from the hot inner disk,
/// falling off with distance, and inside the disk a fill from the glowing gas all around, in the gas's own colour.
/// Off unless the active vessel orbits a configured hole.
/// </summary>
internal static class DiskLight
{
    private const int LitLayers = (1 << 0) | (1 << 17); // parts and EVA kerbals (not scaled space, terrain or IVA)
    private const float KeyStrength = 1.6f;   // key light intensity where the inner disk fills the sky
    private const float FillStrength = 0.9f;  // fill light intensity at full disk density

    private static Light _key, _fill;

    public static void Update(bool allowed)
    {
        Vessel vessel = allowed && HighLogic.LoadedSceneIsFlight ? FlightGlobals.ActiveVessel : null;
        BlackHoleDefinition hole = vessel != null ? HoleOf(vessel.mainBody) : null;
        CelestialBody body = hole?.Body;
        if (body == null || hole.DiskLight <= 0.0)
        {
            SetEnabled(false);
            return;
        }

        double horizon = hole.HorizonRadiusMeters > 0 ? hole.HorizonRadiusMeters : body.Radius;
        Vector3d p = vessel.CoMD - body.position;
        double distance = p.magnitude / horizon; // horizon radii
        if (distance <= 0.0)
        {
            SetEnabled(false);
            return;
        }

        // The inner disk is the bright source: its light falls off with the square of the distance once it no
        // longer fills the sky.
        double key = hole.DiskLight * KeyStrength * Math.Min(1.0, Math.Pow(hole.DiskInner * 2.0 / distance, 2.0));
        Vector3d n = (Planetarium.Rotation * hole.Normal.normalized).normalized;
        double density = DiskDrag.Density(new Double3(p.x, p.y, p.z), new Double3(n.x, n.y, n.z), horizon, hole.DiskInner, hole.DiskOuter, hole.DiskThickness);
        double fill = hole.DiskLight * FillStrength * density + 0.2 * key;
        if (key < 0.01 && fill < 0.01)
        {
            SetEnabled(false);
            return;
        }

        EnsureLights();
        Vector3 outward = ((Vector3)p).normalized;
        _key.transform.rotation = Quaternion.LookRotation(outward); // shines from the hole toward the vessel
        _key.color = DiskColor(hole, hole.PeakTemperature);
        _key.intensity = (float)key;
        _fill.transform.rotation = Quaternion.LookRotation(-outward); // the glowing gas beyond the vessel lights its far side
        double gasTemperature = AccretionDisk.ShakuraSunyaevTemperature(Math.Max(distance, hole.DiskInner * 1.01), hole.DiskInner, hole.PeakTemperature);
        _fill.color = DiskColor(hole, Math.Max(gasTemperature, 1700.0));
        _fill.intensity = (float)fill;
        SetEnabled(true);
    }

    private static BlackHoleDefinition HoleOf(CelestialBody body)
    {
        if (body == null) return null;
        foreach (BlackHoleDefinition hole in BlackHoles.Definitions)
            if (hole.Body == body) return hole;
        return null;
    }

    /// <summary>The disk's colour as the shader draws it: blackbody, made vivid, blended toward the custom tint.</summary>
    private static Color DiskColor(BlackHoleDefinition hole, double kelvin)
    {
        Rgb c = ColorTemperature.ToLinearRec709(Math.Min(kelvin, 25000.0));
        var custom = new Double3(BlackHoleSettings.ToLinear(hole.TintRed), BlackHoleSettings.ToLinear(hole.TintGreen), BlackHoleSettings.ToLinear(hole.TintBlue));
        Double3 d = BlackHoleSettings.DiskColor(new Double3(c.R, c.G, c.B), hole.ColorSaturation, hole.Tint, custom);
        double m = Math.Max(Math.Max(d.X, d.Y), Math.Max(d.Z, 1e-6));
        return new Color((float)(d.X / m), (float)(d.Y / m), (float)(d.Z / m));
    }

    private static void EnsureLights()
    {
        if (_key == null) _key = CreateLight("VolumetricBlackholeFx Disk Key Light");
        if (_fill == null) _fill = CreateLight("VolumetricBlackholeFx Disk Fill Light");
    }

    private static Light CreateLight(string name)
    {
        var go = new GameObject(name);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.shadows = LightShadows.None;
        light.cullingMask = LitLayers;
        light.renderMode = LightRenderMode.ForcePixel;
        light.enabled = false;
        return light;
    }

    private static void SetEnabled(bool on)
    {
        if (_key != null && _key.enabled != on) _key.enabled = on;
        if (_fill != null && _fill.enabled != on) _fill.enabled = on;
    }
}
