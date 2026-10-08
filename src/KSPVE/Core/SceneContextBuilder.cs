using KSPVE.Pure.Lighting;

namespace KSPVE.Core;

internal static class SceneContextBuilder
{
    public static void Fill(SceneContext ctx, long frameIndex, FrameOfReference frame)
    {
        ctx.FrameIndex = frameIndex;
        ctx.Scene = HighLogic.LoadedScene;
        ctx.View = ResolveView(ctx.Scene);
        ctx.OriginShiftsThisFrame = frame.ShiftsThisFrame;
        ctx.KrakensbaneVelocity = frame.KrakensbaneVelocity;
        ctx.Stars.Clear();

        FillBodies(ctx);

        Vessel vessel = HighLogic.LoadedSceneIsFlight && FlightGlobals.ready ? FlightGlobals.ActiveVessel : null;
        if (vessel == null || vessel.mainBody == null)
        {
            ctx.ActiveVesselName = null;
            ctx.MainBodyName = null;
            ctx.AltitudeMeters = double.NaN;
            ctx.InAtmosphere = false;
            ctx.AtmosphereDensity = 0.0;
            ctx.Mach = 0.0;
            return;
        }

        CelestialBody body = vessel.mainBody;
        ctx.ActiveVesselName = vessel.vesselName;
        ctx.MainBodyName = body.bodyName;
        ctx.AltitudeMeters = vessel.altitude;
        ctx.InAtmosphere = body.atmosphere && vessel.altitude < body.atmosphereDepth;
        ctx.AtmosphereDensity = vessel.atmDensity;
        ctx.Mach = vessel.mach;
        FillStars(ctx, vessel.GetWorldPos3D());
    }

    private static ViewMode ResolveView(GameScenes scene)
    {
        switch (scene)
        {
            case GameScenes.FLIGHT:
                if (MapView.MapIsEnabled) return ViewMode.Map;
                CameraManager cameras = CameraManager.Instance;
                if (cameras != null &&
                    (cameras.currentCameraMode == CameraManager.CameraMode.IVA ||
                     cameras.currentCameraMode == CameraManager.CameraMode.Internal))
                    return ViewMode.Iva;
                return ViewMode.Flight;
            case GameScenes.EDITOR: return ViewMode.Editor;
            case GameScenes.SPACECENTER: return ViewMode.SpaceCenter;
            case GameScenes.TRACKSTATION: return ViewMode.TrackingStation;
            case GameScenes.MAINMENU: return ViewMode.MainMenu;
            default: return ViewMode.Other;
        }
    }

    private static void FillBodies(SceneContext ctx)
    {
        if (FlightGlobals.fetch == null)
        {
            ctx.Bodies.Clear();
            return;
        }
        var bodies = FlightGlobals.Bodies;
        if (ctx.Bodies.Count == bodies.Count) return; // body list is static within a session

        ctx.Bodies.Clear();
        foreach (CelestialBody b in bodies)
        {
            if (b == null) continue;
            ctx.Bodies.Add(new BodyInfo
            {
                Name = b.bodyName,
                RadiusMeters = b.Radius,
                Albedo = b.albedo,
                HasAtmosphere = b.atmosphere,
                AtmosphereDepthMeters = b.atmosphereDepth,
                IsStar = b.isStar
            });
        }
    }

    private static void FillStars(SceneContext ctx, Vector3d from)
    {
        CelestialBody home = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
        foreach (CelestialBody body in FlightGlobals.Bodies)
        {
            if (body == null || !body.isStar) continue;
            Vector3d offset = body.position - from;
            double distance = offset.magnitude;
            bool isHome = body == home;
            double flux = isHome && distance > 0.0 ? Photometry.FluxAtDistance(PhysicsGlobals.SolarLuminosity, distance) : double.NaN;
            var info = new StarInfo
            {
                Name = body.bodyName,
                DirectionFromVessel = distance > 0.0 ? offset / distance : Vector3d.zero,
                DistanceMeters = distance,
                FluxWattsPerM2 = flux,
                IlluminanceLux = double.IsNaN(flux) ? double.NaN : Photometry.IlluminanceLux(flux)
            };
            if (isHome) ctx.Stars.Insert(0, info);
            else ctx.Stars.Add(info);
        }
    }
}
