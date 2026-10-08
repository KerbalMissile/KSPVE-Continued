using System;

namespace KSPVE.Core;

/// <summary>
/// Tracks KSP's floating-origin shifts and the Krakensbane frame velocity. Effects that keep
/// history (TAA, GI, probes, shadow atlas) subscribe to <see cref="OriginShifted"/> to rebase.
/// P0a records KSP's two event arguments unchanged; their exact meaning is pinned down in P2
/// (motion vectors) from observed data.
/// </summary>
public sealed class FrameOfReference : IDisposable
{
    private int _pendingShifts;

    public FrameOfReference()
    {
        GameEvents.onFloatingOriginShift.Add(OnFloatingOriginShift);
    }

    public event Action<Vector3d, Vector3d> OriginShifted;

    public int ShiftsThisFrame { get; private set; }
    public int ShiftsTotal { get; private set; }
    public Vector3d LastShiftArg0 { get; private set; }
    public Vector3d LastShiftArg1 { get; private set; }
    public Vector3d KrakensbaneVelocity { get; private set; }

    /// <summary>Called once per frame by the engine before the scene context is built.</summary>
    public void BeginFrame()
    {
        ShiftsThisFrame = _pendingShifts;
        _pendingShifts = 0;
        KrakensbaneVelocity = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready
            ? Krakensbane.GetFrameVelocity()
            : Vector3d.zero;
    }

    public void Dispose()
    {
        GameEvents.onFloatingOriginShift.Remove(OnFloatingOriginShift);
    }

    private void OnFloatingOriginShift(Vector3d arg0, Vector3d arg1)
    {
        _pendingShifts++;
        ShiftsTotal++;
        LastShiftArg0 = arg0;
        LastShiftArg1 = arg1;
        OriginShifted?.Invoke(arg0, arg1);
    }
}
