using System;
using KSPVE.Core;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using KSPVE.Pure.Space.Relativity;
using UnityEngine;

namespace KSPVE.Space;

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

        double horizon = hole.HorizonMeters(body);
        Vector3d p = vessel.CoMD - body.position;
        double distance = p.magnitude / horizon; // horizon radii
        if (distance <= 0.0)
        {
            SetEnabled(false);
            return;
        }

        // The inner disk is the bright source: its light falls off with the square of the distance once it no
        // longer fills the sky.
        DiskState disk = BlackHoles.StateOf(hole);
        double key = hole.DiskLight * disk.Presence * disk.Brightness * KeyStrength * Math.Min(1.0, Math.Pow(disk.Inner * 2.0 / distance, 2.0));
        Vector3d n = (Planetarium.Rotation * hole.Normal.normalized).normalized;
        double density = DiskDrag.Density(new Double3(p.x, p.y, p.z), new Double3(n.x, n.y, n.z), horizon, disk.Inner, disk.Outer, disk.Thickness);
        double fill = hole.DiskLight * disk.Presence * FillStrength * density + 0.2 * key;
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

    /// <summary>The disk's blackbody colour with its colour saturation, as the shader draws it.</summary>
    private static Color DiskColor(BlackHoleDefinition hole, double kelvin)
    {
        Rgb c = ColorTemperature.ToLinearRec709(Math.Min(kelvin, 25000.0));
        double l = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        double s = hole.ColorSaturation;
        var boosted = new Rgb(Math.Max(0.0, l + (c.R - l) * s), Math.Max(0.0, l + (c.G - l) * s), Math.Max(0.0, l + (c.B - l) * s));
        double m = Math.Max(boosted.MaxComponent, 1e-6);
        return new Color((float)(boosted.R / m), (float)(boosted.G / m), (float)(boosted.B / m));
    }

    private static void EnsureLights()
    {
        if (_key == null) _key = CreateLight("KSPVE Disk Key Light");
        if (_fill == null) _fill = CreateLight("KSPVE Disk Fill Light");
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
