using System.Globalization;
using KSPVE.Core;
using KSPVE.Pure;

namespace KSPVE.Dev;

internal static class SceneContextJson
{
    public static void Write(JsonWriter w, SceneContext c)
    {
        w.BeginObject()
            .Property("frame", c.FrameIndex)
            .Property("scene", c.Scene.ToString())
            .Property("view", c.View.ToString())
            .Property("activeVessel", c.ActiveVesselName)
            .Property("mainBody", c.MainBodyName)
            .Property("altitude", c.AltitudeMeters)
            .Property("inAtmosphere", c.InAtmosphere)
            .Property("atmosphereDensity", c.AtmosphereDensity)
            .Property("mach", c.Mach)
            .Property("originShiftsThisFrame", c.OriginShiftsThisFrame)
            .Property("krakensbaneVelocity", Format(c.KrakensbaneVelocity));

        w.Name("stars").BeginArray();
        foreach (StarInfo s in c.Stars)
        {
            w.BeginObject()
                .Property("name", s.Name)
                .Property("distance", s.DistanceMeters)
                .Property("flux", s.FluxWattsPerM2)
                .Property("illuminanceLux", s.IlluminanceLux)
                .Property("direction", Format(s.DirectionFromVessel))
                .EndObject();
        }
        w.EndArray();

        w.Name("bodies").BeginArray();
        foreach (BodyInfo b in c.Bodies)
        {
            w.BeginObject()
                .Property("name", b.Name)
                .Property("radius", b.RadiusMeters)
                .Property("albedo", b.Albedo)
                .Property("atmosphere", b.HasAtmosphere)
                .Property("atmosphereDepth", b.AtmosphereDepthMeters)
                .Property("isStar", b.IsStar)
                .EndObject();
        }
        w.EndArray();
        w.EndObject();
    }

    private static string Format(Vector3d v) =>
        string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", v.x, v.y, v.z);
}
