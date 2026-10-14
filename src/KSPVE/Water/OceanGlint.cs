using System;
using System.Collections.Generic;
using System.Diagnostics;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Textures;
using KSPVE.Pure.Water;
using UnityEngine;
using D3 = KSPVE.Pure.Numerics.Double3;

namespace KSPVE.Water;

/// <summary>
/// The ocean seen from space: for each body with a sea, a sphere at sea level on Camera ScaledSpace drawn by OceanGlint.shader,
/// adding the sun's glint (Cox and Munk's roughness for the wind there) and the sky's grazing reflection over the ocean only. The
/// ocean is found by reading the terrain's height on a coarse map of latitude and longitude (a time-sliced job, once per body); the
/// wind (and the sea ice, when the climate supplies a map) is refreshed a row at a time.
/// </summary>
internal sealed class OceanGlintRenderer
{
    private const string ShaderName = "Hidden/KSPVE/Water/OceanGlint";
    private const string ScaledCameraName = "Camera ScaledSpace";
    private const int ScaledLayer = 10;
    public const int MaskWidth = 256, MaskHeight = 128;

    private sealed class BodyMask : ITerrainJob
    {
        public CelestialBody Body;
        public Texture2D Texture;
        public readonly Color32[] Pixels = new Color32[MaskWidth * MaskHeight];
        public int SampleCursor;
        public bool Ready;
        public int WindRow;
        public float NextWindRefresh;
        public bool Dirty;

        /// <summary>Reads the terrain's height at each texel (the ocean is where it is below the sea).</summary>
        public bool Step(Stopwatch watch, double limitMs)
        {
            while (SampleCursor < Pixels.Length)
            {
                for (int k = 0; k < 16 && SampleCursor < Pixels.Length; k++, SampleCursor++)
                {
                    int x = SampleCursor % MaskWidth, y = SampleCursor / MaskWidth;
                    double lon = (x + 0.5) / MaskWidth * 360.0 - 180.0, lat = (y + 0.5) / MaskHeight * 180.0 - 90.0;
                    bool sea = TerrainSampler.Altitude(Body, lat, lon) <= 0.0;
                    Color32 p = Pixels[SampleCursor];
                    p.r = sea ? (byte)255 : (byte)0;
                    Pixels[SampleCursor] = p;
                }
                Dirty = true;
                if (watch.Elapsed.TotalMilliseconds >= limitMs) return false;
            }
            Ready = true;
            return true;
        }
    }

    private readonly Dictionary<string, BodyMask> _masks = new Dictionary<string, BodyMask>();
    private Shader _shader;
    private Material _material;
    private Mesh _sphere;
    private Camera _scaled;
    private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

    /// <summary>Shows glint over a body when the camera is at least this high above its ocean (the local ocean covers it below).</summary>
    private const double MinAltitude = 15000.0;

    public void Update(bool allowed)
    {
        if (!allowed || !WaterConfig.Enabled || !WaterConfig.OrbitalGlint || !ShaderLibrary.Loaded) return;
        if (_shader == null) _shader = ShaderLibrary.Find(ShaderName);
        if (_shader == null) return;
        if (_scaled == null)
        {
            int count = Camera.allCamerasCount;
            var cams = new Camera[count];
            Camera.GetAllCameras(cams);
            foreach (Camera c in cams) if (c != null && c.name == ScaledCameraName) _scaled = c;
            if (_scaled == null) return;
        }
        if (_material == null) _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave, name = "KSPVE Ocean glint" };
        if (_sphere == null) _sphere = BuildSphere(48, 96);

        CelestialBody current = HighLogic.LoadedSceneIsFlight ? FlightGlobals.currentMainBody : null;
        Light sunLight = Sun.Instance != null ? Sun.Instance.sunLight : null;
        Color sunColor = sunLight != null ? sunLight.color * sunLight.intensity : Color.white;
        foreach (CelestialBody body in FlightGlobals.Bodies)
        {
            if (body == null || !body.ocean || body.scaledBody == null || body.pqsController == null) continue;
            if (!_masks.TryGetValue(body.bodyName, out BodyMask mask))
            {
                mask = new BodyMask { Body = body, Texture = new Texture2D(MaskWidth, MaskHeight, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave, name = "KSPVE Ocean mask " + body.bodyName } };
                _masks[body.bodyName] = mask;
                // Only the bodies whose scaled image is near the camera are worth reading: others wait until they are.
            }
            if (!mask.Ready)
            {
                if (!Queued(mask)) { TerrainSampler.Enqueue(mask); mask.SampleCursor = 0; _queued.Add(mask); }
                continue;
            }
            RefreshWind(mask);
            if (mask.Dirty) { mask.Texture.SetPixels32(mask.Pixels); mask.Texture.Apply(false, false); mask.Dirty = false; }

            Vector3d camPos = _scaled.transform.position;
            Vector3 center = body.scaledBody.transform.position;
            double scaledRadius = body.Radius * ScaledSpace.InverseScaleFactor;
            // The altitude above the sea, in metres, by the camera's distance from the body's centre in scaled space.
            double altitude = ((camPos - (Vector3d)center).magnitude - scaledRadius) * ScaledSpace.ScaleFactor;
            if (body == current && altitude < MinAltitude) continue;

            CloudWorld.Basis(body, out Vector3d bx, out Vector3d by, out Vector3d bz);
            CelestialBody star = CloudWorld.StarOf(body);
            Vector3 toSun = star != null ? (Vector3)((star.position - body.position).normalized) : Vector3.up;
            double density = body.atmosphere ? Math.Min(Math.Max(body.atmospherePressureSeaLevel / 101.325, 0.0), 8.0) : 0.0;
            SkyModel.Colors(45.0, density, out D3 zenith, out D3 horizon, out _);
            _block.Clear();
            _block.SetTexture("_KSPVE_OceanMask", mask.Texture);
            _block.SetVector("_KSPVE_BodyX", new Vector4((float)bx.x, (float)bx.y, (float)bx.z, 0f));
            _block.SetVector("_KSPVE_BodyY", new Vector4((float)by.x, (float)by.y, (float)by.z, 0f));
            _block.SetVector("_KSPVE_BodyZ", new Vector4((float)bz.x, (float)bz.y, (float)bz.z, 0f));
            _block.SetVector("_KSPVE_SunDir", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            _block.SetVector("_KSPVE_SunRadiance", new Vector4(sunColor.r, sunColor.g, sunColor.b, 0f));
            _block.SetVector("_KSPVE_SkyZenith", new Vector4(sunColor.r * (float)zenith.X, sunColor.g * (float)zenith.Y, sunColor.b * (float)zenith.Z, 0f));
            _block.SetVector("_KSPVE_SkyHorizon", new Vector4(sunColor.r * (float)horizon.X, sunColor.g * (float)horizon.Y, sunColor.b * (float)horizon.Z, 0f));
            _block.SetVector("_KSPVE_Deep", new Vector4(0f, 0f, 0f, (float)WaterOptics.SeawaterIndex));
            // Through the air twice (the sun's way down, the view's way up) the light is dimmed by the air's vertical optical
            // depth: Earth's Rayleigh depth at 680, 550 and 440 nm plus a light haze, scaled by the surface pressure. Clouds are not in this.
            float tau = (float)density;
            _block.SetVector("_KSPVE_GlintLook", new Vector4(1f, 1f, 1f, 1f));
            _block.SetVector("_KSPVE_GlintTau", new Vector4(tau * (0.045f + 0.08f), tau * (0.097f + 0.09f), tau * (0.236f + 0.1f), 0f));
            Matrix4x4 m = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * (float)(scaledRadius * 1.0004));
            Graphics.DrawMesh(_sphere, m, _material, ScaledLayer, _scaled, 0, _block, false, false, false);
        }
    }

    private readonly HashSet<BodyMask> _queued = new HashSet<BodyMask>();
    private bool Queued(BodyMask mask) => _queued.Contains(mask);

    /// <summary>Refreshes one row of the mask's wind (and sea ice) channels every few frames, round and round.</summary>
    private void RefreshWind(BodyMask mask)
    {
        if (Time.realtimeSinceStartup < mask.NextWindRefresh) return;
        mask.NextWindRefresh = Time.realtimeSinceStartup + 0.05f;
        BodyClouds clouds = CloudWorld.Of(mask.Body);
        if (clouds?.Weather == null) return;
        if (mask.WindRow == 0) CloudWorld.PrepareWeather(clouds);
        GreyMap ice = null;
        try { ice = OceanSystem.SeaIceMap(mask.Body); } catch (Exception) { }
        int y = mask.WindRow;
        double lat = ((y + 0.5) / MaskHeight - 0.5) * Math.PI;
        for (int x = 0; x < MaskWidth; x++)
        {
            double lon = ((x + 0.5) / MaskWidth - 0.5) * 2.0 * Math.PI;
            WeatherSample w = clouds.Weather.Sample(lat, lon);
            double speed = WaterConfig.WindOverride >= 0.0 ? WaterConfig.WindOverride : Math.Sqrt(w.WindEast * w.WindEast + w.WindNorth * w.WindNorth);
            Color32 p = mask.Pixels[y * MaskWidth + x];
            p.g = (byte)Math.Min(255.0, speed / 40.0 * 255.0);
            p.b = ice != null ? (byte)Math.Min(255.0, ice.Sample((double)(x + 0.5) / MaskWidth, (double)(y + 0.5) / MaskHeight) * 255.0) : (byte)0;
            mask.Pixels[y * MaskWidth + x] = p;
        }
        mask.Dirty = true;
        mask.WindRow = (mask.WindRow + 1) % MaskHeight;
    }

    private static Mesh BuildSphere(int rings, int segments)
    {
        var vertices = new List<Vector3>();
        var tris = new List<int>();
        for (int r = 0; r <= rings; r++)
        {
            float lat = Mathf.PI * (r / (float)rings - 0.5f);
            for (int s = 0; s <= segments; s++)
            {
                float lon = 2f * Mathf.PI * s / segments;
                vertices.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)));
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
        var mesh = new Mesh { name = "KSPVE ocean glint sphere", hideFlags = HideFlags.HideAndDontSave };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public void Shutdown()
    {
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_sphere != null) UnityEngine.Object.Destroy(_sphere);
        foreach (BodyMask m in _masks.Values) { TerrainSampler.Cancel(m); if (m.Texture != null) UnityEngine.Object.Destroy(m.Texture); }
        _masks.Clear();
        _queued.Clear();
    }
}
