using System;
using KSPVE.Core;
using KSPVE.Pure.Water.Hydrology;
using UnityEngine;

namespace KSPVE.Water;

/// <summary>
/// Optional (the setting is off): KSP's buoyancy knows only the sea, so a craft on a lake or river sinks into the terrain. With the
/// setting on, each part of a loaded craft in the water the rivers system is drawing (a lake, or a river's channel) gets a buoyant
/// force like KSP's own (its mass times its buoyancy factor times the share of it submerged, along the vertical) and a drag against
/// the water's flow, on its own rigid body. Parts in the sea are left to KSP. Approximate: the submerged share is taken from the part's
/// collider bounds along the vertical, not its true shape.
/// </summary>
internal sealed class WaterBuoyancyModule : VesselModule
{
    private const double WaterDensity = 1000.0;
    private const double DragCoefficient = 0.8;

    private void FixedUpdate()
    {
        if (!WaterConfig.Buoyancy || !WaterConfig.Enabled || vessel == null || !vessel.loaded || vessel.packed) return;
        WaterFeature feature = WaterFeature.Instance;
        if (feature == null || !feature.Rivers.Active) return;
        CelestialBody body = vessel.mainBody;
        if (body == null) return;
        double g = body.GeeASL * 9.80665;
        for (int i = 0; i < vessel.parts.Count; i++)
        {
            Part part = vessel.parts[i];
            if (part == null || part.rb == null || part.physicalSignificance == Part.PhysicalSignificance.NONE) continue;
            Bounds bounds = part.collider != null ? part.collider.bounds : new Bounds(part.transform.position, Vector3.one * 0.5f);
            Vector3d center = bounds.center;
            Vector3 up = (Vector3)(center - body.position).normalized;
            double altitude = body.GetAltitude(center);
            if (body.ocean && altitude < 1.0) continue;   // the sea is KSP's
            // The extent of the bounds along the vertical.
            double half = Math.Abs(up.x) * bounds.extents.x + Math.Abs(up.y) * bounds.extents.y + Math.Abs(up.z) * bounds.extents.z;
            if (half < 0.01) continue;
            if (!feature.Rivers.WaterAtWorld(center, altitude, out WaterAt water, out Vector3d flow)) continue;
            double submerged = Math.Max(0.0, Math.Min(1.0, (water.Surface - (altitude - half)) / (2.0 * half)));
            if (submerged <= 0.0) continue;
            // It cannot be more submerged than the water is deep.
            submerged = Math.Min(submerged, Math.Max(water.Depth, 0.0) / (2.0 * half) + 0.05);
            double mass = part.mass + part.GetResourceMass();
            double buoyancy = Math.Max(part.buoyancy, 0.0);
            // Tonnes and metres per second squared give kilonewtons: KSP's force units.
            double lift = mass * g * buoyancy * submerged;
            part.rb.AddForceAtPosition(up * (float)lift, (Vector3)center, ForceMode.Force);
            // Drag against the water (moving with the flow): quadratic, on the part's largest face.
            Vector3 relative = part.rb.GetPointVelocity((Vector3)center) - (Vector3)flow;
            double speed = relative.magnitude;
            if (speed > 0.01)
            {
                double area = Math.Max(bounds.size.x * bounds.size.y, Math.Max(bounds.size.y * bounds.size.z, bounds.size.x * bounds.size.z));
                double force = 0.5 * WaterDensity * 1e-3 * DragCoefficient * area * speed * speed * submerged;   // kN
                double limit = part.rb.mass * speed / Time.fixedDeltaTime * 0.5;                                  // never reverse the part in one step
                part.rb.AddForceAtPosition(-relative.normalized * (float)Math.Min(force, limit), (Vector3)center, ForceMode.Force);
            }
        }
    }
}
