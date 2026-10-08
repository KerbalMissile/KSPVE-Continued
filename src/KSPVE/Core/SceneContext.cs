using System.Collections.Generic;

namespace KSPVE.Core;

public enum ViewMode
{
    Other,
    MainMenu,
    SpaceCenter,
    Editor,
    TrackingStation,
    Flight,
    Map,
    Iva
}

public struct StarInfo
{
    public string Name;
    public Vector3d DirectionFromVessel;   // unit vector, world space
    public double DistanceMeters;
    public double FluxWattsPerM2;          // NaN when unknown (non-home stars until the Kopernicus adapter exists)
    public double IlluminanceLux;          // NaN when unknown
}

public struct BodyInfo
{
    public string Name;
    public double RadiusMeters;
    public double Albedo;
    public bool HasAtmosphere;
    public double AtmosphereDepthMeters;
    public bool IsStar;
}

/// <summary>
/// Per-frame snapshot of the game state effects need. Rebuilt in place every LateUpdate;
/// effects read it during rendering and never poll KSP themselves.
/// </summary>
public sealed class SceneContext
{
    public long FrameIndex;
    public GameScenes Scene;
    public ViewMode View;
    public string ActiveVesselName;
    public string MainBodyName;
    public double AltitudeMeters = double.NaN;
    public bool InAtmosphere;
    public double AtmosphereDensity;
    public double Mach;
    public int OriginShiftsThisFrame;
    public Vector3d KrakensbaneVelocity;
    public readonly List<StarInfo> Stars = new List<StarInfo>();
    public readonly List<BodyInfo> Bodies = new List<BodyInfo>();
}
