using KSPVE.Core;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using UnityEngine;

namespace KSPVE.Space;

/// <summary>
/// Applies <see cref="DiskDrag"/> to loaded vessels inside a black hole's accretion disk, with turbulent buffeting
/// (shoves and rocking) so the pull is felt, and a live readout for the active vessel. Call from FixedUpdate.
/// </summary>
internal static class DiskDragDriver
{
    private const double EnterWeight = 0.05;
    private const double LeaveWeight = 0.01;
    private const double BuffetAccel = 4.0;    // m/s^2 of turbulent shove at full density and diskBuffeting 1
    private const double BuffetSpin = 0.12;    // rad/s^2 of turbulent rocking at full density and diskBuffeting 1
    private const float ReadoutInterval = 0.5f; // s between updates of the on-screen readout

    private static bool _caught;
    private static bool _failed;
    private static ScreenMessage _readout;
    private static float _nextReadout;

    public static void FixedUpdate()
    {
        if (_failed) return;
        if (!HighLogic.LoadedSceneIsFlight) return;
        var definitions = BlackHoles.Definitions;
        if (definitions.Count == 0) return;
        Engine engine = Engine.Instance;
        if (engine == null || !engine.Settings.Get(CoreSettings.Enabled)) return;

        try
        {
            Vessel active = FlightGlobals.ActiveVessel;
            DiskDragResult activeDrag = default;
            BlackHoleDefinition activeHole = null;
            double activeRadius = 0.0;
            double time = Planetarium.GetUniversalTime();
            for (int i = 0; i < definitions.Count; i++)
            {
                BlackHoleDefinition hole = definitions[i];
                if (!hole.DiskDrag || !hole.Enabled || !hole.IsBlackHole) continue;   // only a black hole's disk drags
                CelestialBody body = hole.Body;
                if (body == null) continue;

                DiskState disk = BlackHoles.StateOf(hole);
                if (disk.Presence <= 0.0) continue;
                double horizon = hole.HorizonMeters(body);
                Vector3d n = (Planetarium.Rotation * hole.Normal.normalized).normalized;
                var normal = new Double3(n.x, n.y, n.z);
                double spin = hole.Spin < 0 ? -1.0 : 1.0;

                var vessels = FlightGlobals.VesselsLoaded;
                for (int v = 0; v < vessels.Count; v++)
                {
                    Vessel vessel = vessels[v];
                    if (vessel == null || vessel.packed || vessel.mainBody != body) continue;

                    Vector3d p = vessel.CoMD - body.position;
                    Vector3d vel = vessel.obt_velocity;
                    DiskDragResult drag = DiskDrag.Compute(new Double3(p.x, p.y, p.z), new Double3(vel.x, vel.y, vel.z),
                        normal, spin, body.gravParameter, horizon, disk.Inner, disk.Outer, disk.Thickness,
                        hole.DiskDragTimescale, hole.DiskDragMaxAccel * disk.Presence);
                    if (drag.Weight <= 0.0) continue;
                    if (vessel == active)
                    {
                        activeDrag = drag;
                        activeHole = hole;
                        activeRadius = p.magnitude / horizon;
                    }

                    // Turbulence: a smooth random shove plus a rocking about the centre of mass, both scaled by density.
                    int seed = (int)(vessel.persistentId % 100000u);
                    double buffet = hole.DiskBuffeting * drag.Weight;
                    Double3 shove = DiskTurbulence.Sample(time, seed) * (BuffetAccel * buffet);
                    Double3 rock = DiskTurbulence.Sample(time * 0.8, seed + 1) * (BuffetSpin * buffet);
                    var accel = new Vector3((float)(drag.Acceleration.X + shove.X), (float)(drag.Acceleration.Y + shove.Y), (float)(drag.Acceleration.Z + shove.Z));
                    var spinAccel = new Vector3((float)rock.X, (float)rock.Y, (float)rock.Z);
                    Vector3 centre = vessel.CoM;

                    var parts = vessel.parts;
                    for (int k = 0; k < parts.Count; k++)
                    {
                        Rigidbody rb = parts[k].rb;
                        if (rb == null) continue;
                        // Angular acceleration a about the centre of mass moves each part by a x r: the ship rocks as one.
                        rb.AddForce(accel + Vector3.Cross(spinAccel, rb.worldCenterOfMass - centre), ForceMode.Acceleration);
                    }
                }
            }

            UpdateReadout(active, activeHole, activeDrag, activeRadius);
        }
        catch (System.Exception ex)
        {
            _failed = true;
            ClearReadout();
            Log.Exception("Applying accretion disk drag failed; disk drag is off for this session", ex);
        }
    }

    /// <summary>Tells the player they are caught, and how hard the disk is pulling, while the active vessel is inside it.</summary>
    private static void UpdateReadout(Vessel active, BlackHoleDefinition hole, DiskDragResult drag, double radius)
    {
        if (!_caught && drag.Weight > EnterWeight)
        {
            _caught = true;
            _nextReadout = 0f;
            Log.Info($"{active.vesselName} entered {hole.BodyName}'s accretion disk at {radius:F1} horizon radii, " +
                     $"{drag.RelativeSpeed:N0} m/s relative to the gas, pulled at {drag.Acceleration.Length / 9.81:F2} g.");
        }
        else if (_caught && drag.Weight < LeaveWeight)
        {
            _caught = false;
            ClearReadout();
            ScreenMessages.PostScreenMessage("Out of the accretion disk", 3f, ScreenMessageStyle.UPPER_CENTER);
            return;
        }
        if (!_caught || Time.unscaledTime < _nextReadout) return;

        _nextReadout = Time.unscaledTime + ReadoutInterval;
        ClearReadout();
        string text = $"Caught in the accretion disk: pulled at {drag.Acceleration.Length / 9.81:F1} g, " +
                      $"{drag.RelativeSpeed:N0} m/s off the gas flow. Thrust to break free.";
        _readout = ScreenMessages.PostScreenMessage(text, ReadoutInterval * 2.5f, ScreenMessageStyle.UPPER_CENTER);
    }

    private static void ClearReadout()
    {
        if (_readout == null) return;
        ScreenMessages.RemoveMessage(_readout);
        _readout = null;
    }
}
