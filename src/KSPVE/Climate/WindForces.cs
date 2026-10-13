using System;
using System.Reflection;
using KSPVE.Clouds;
using KSPVE.Core;
using UnityEngine;

namespace KSPVE.Seasonal;

/// <summary>
/// Lets the weather's wind push craft, through Ferram Aerospace Research's wind hook (FARWind.SetWindFunction takes a
/// function from a body, a part and a position to the air's velocity relative to the ground, in world space), found by
/// reflection so nothing is needed when FAR is not installed. Stock aerodynamics has no input for wind (a part's drag is
/// worked out inside the game from its velocity alone), so without FAR this does nothing: pushing parts with forces of our
/// own would need each part's drag area and would double count the drag the game already applies.
/// </summary>
internal static class WindForces
{
    private static bool _installed, _looked, _available;
    private static MethodInfo _set, _reset;
    private static Delegate _function;
    private static Vector3d _cachedPosition;
    private static Vector3 _cachedWind;
    private static int _cachedFrame = -1;
    private static CelestialBody _cachedBody;

    /// <summary>What to tell the player in the settings window.</summary>
    public static string Status =>
        !_looked ? "not checked yet" : !_available ? "Ferram Aerospace Research is not installed (stock aerodynamics has no wind input)" : _installed ? "pushing craft through Ferram Aerospace Research" : "ready (Ferram Aerospace Research found)";

    public static void Update()
    {
        bool want = ClimateConfig.WindPush;
        if (want && !_installed) Install();
        else if (!want && _installed) Remove();
    }

    private static void Look()
    {
        _looked = true;
        foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t;
            try { t = a.GetType("FerramAerospaceResearch.FARWind", false); }
            catch (Exception) { continue; }
            if (t == null) continue;
            _set = t.GetMethod("SetWindFunction", BindingFlags.Public | BindingFlags.Static);
            _reset = t.GetMethod("ResetWindFunction", BindingFlags.Public | BindingFlags.Static);
            if (_set == null || _set.GetParameters().Length != 1) { _set = null; continue; }
            MethodInfo mine = typeof(WindForces).GetMethod(nameof(WindAt), BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                _function = Delegate.CreateDelegate(_set.GetParameters()[0].ParameterType, mine);
                _available = true;
                Log.Info($"Wind: found {t.FullName} in {a.GetName().Name}.");
            }
            catch (Exception ex)
            {
                Log.Warning("Wind: Ferram Aerospace Research's wind function has a signature KSPVE does not know: " + ex.Message);
            }
            return;
        }
    }

    private static void Install()
    {
        if (!_looked) Look();
        if (!_available) return;
        try
        {
            _set.Invoke(null, new object[] { _function });
            _installed = true;
            Log.Info("Wind: craft are pushed by the weather's wind.");
        }
        catch (Exception ex)
        {
            _available = false;
            Log.Exception("Setting Ferram Aerospace Research's wind function failed", ex);
        }
    }

    private static void Remove()
    {
        try
        {
            if (_reset != null) _reset.Invoke(null, null);
        }
        catch (Exception ex) { Log.Exception("Resetting Ferram Aerospace Research's wind function failed", ex); }
        _installed = false;
    }

    public static void Shutdown()
    {
        if (_installed) Remove();
    }

    /// <summary>FAR's WindFunction: the wind at a part (world space, m/s, relative to the ground).</summary>
    private static Vector3 WindAt(CelestialBody body, Part part, Vector3 position)
    {
        if (!ClimateConfig.WindPush || body == null || !body.atmosphere) return Vector3.zero;
        // The wind changes over kilometres and minutes: a physics step's worth of parts share one value.
        int frame = Time.fixedTime > 0f ? Time.frameCount : 0;
        if (frame == _cachedFrame && body == _cachedBody && (_cachedPosition - (Vector3d)position).sqrMagnitude < 400.0) return _cachedWind;
        BodyClouds clouds = CloudWorld.Of(body);
        if (clouds == null || clouds.Weather == null) return Vector3.zero;
        double altitude = ((Vector3d)position - body.position).magnitude - body.Radius;
        double fade = Math.Max(0.0, Math.Min(1.0, (body.atmosphereDepth - altitude) / Math.Max(0.1 * body.atmosphereDepth, 1.0)));
        Vector3d wind = CloudWorld.WindWorld(clouds, position) * fade;
        _cachedFrame = frame;
        _cachedBody = body;
        _cachedPosition = position;
        _cachedWind = (Vector3)wind;
        return _cachedWind;
    }
}
