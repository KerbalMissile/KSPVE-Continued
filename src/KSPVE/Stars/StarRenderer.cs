using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Stars;
using KSPVE.Space;
using KSPVE.Pure.Space;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Stars;

/// <summary>
/// Draws every star's surface and surroundings on the ScaledSpace camera after transparents (Star.shader): one quad over
/// the star's bounding sphere, premultiplied, with the star's photosphere, prominences, corona and CMEs worked out by
/// <see cref="StarActivity"/> for the game time. Stars on screen smaller than a couple of pixels are left to the game
/// (its sprite and lens flare). Bodies in front (a planet eclipsing the star, a moon) are tested as analytic spheres; the
/// disc's visible fraction under them (Eclipse) tells the eye view how much of the corona can be seen.
/// </summary>
[DisallowMultipleComponent]
internal sealed class StarRenderer : MonoBehaviour
{
    private const string ShaderName = "Hidden/KSPVE/Stars/Star";
    private const int QuadVertices = 6, SpotMax = 24, StructureMax = 20, ShellMax = 3, OccluderMax = 8;
    private const double MinPixelRadius = 2.0;
    private const int MaxStars = 4;

    private static readonly int RectId = Shader.PropertyToID("_KSPVE_StarRect");
    private static readonly int CenterId = Shader.PropertyToID("_KSPVE_StarCenter");
    private static readonly int RightId = Shader.PropertyToID("_KSPVE_StarRight");
    private static readonly int UpId = Shader.PropertyToID("_KSPVE_StarUp");
    private static readonly int ForwardId = Shader.PropertyToID("_KSPVE_StarForward");
    private static readonly int MXId = Shader.PropertyToID("_KSPVE_StarMX");
    private static readonly int MYId = Shader.PropertyToID("_KSPVE_StarMY");
    private static readonly int MZId = Shader.PropertyToID("_KSPVE_StarMZ");
    private static readonly int RayId = Shader.PropertyToID("_KSPVE_StarRay");
    private static readonly int LookId = Shader.PropertyToID("_KSPVE_StarLook");
    private static readonly int Look2Id = Shader.PropertyToID("_KSPVE_StarLook2");
    private static readonly int FlagsId = Shader.PropertyToID("_KSPVE_StarFlags");
    private static readonly int CountsId = Shader.PropertyToID("_KSPVE_StarCounts");
    private static readonly int TextureId = Shader.PropertyToID("_KSPVE_StarTexture");
    private static readonly int SpotAId = Shader.PropertyToID("_KSPVE_StarSpotA");
    private static readonly int SpotBId = Shader.PropertyToID("_KSPVE_StarSpotB");
    private static readonly int SpotCId = Shader.PropertyToID("_KSPVE_StarSpotC");
    private static readonly int SpotDId = Shader.PropertyToID("_KSPVE_StarSpotD");
    private static readonly int StrAId = Shader.PropertyToID("_KSPVE_StarStrA");
    private static readonly int StrBId = Shader.PropertyToID("_KSPVE_StarStrB");
    private static readonly int StrCId = Shader.PropertyToID("_KSPVE_StarStrC");
    private static readonly int StrDId = Shader.PropertyToID("_KSPVE_StarStrD");
    private static readonly int ShellAId = Shader.PropertyToID("_KSPVE_StarShellA");
    private static readonly int ShellBId = Shader.PropertyToID("_KSPVE_StarShellB");
    private static readonly int OccId = Shader.PropertyToID("_KSPVE_StarOcc");

    private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
    private readonly Vector4[] _spotA = new Vector4[SpotMax], _spotB = new Vector4[SpotMax], _spotC = new Vector4[SpotMax], _spotD = new Vector4[SpotMax];
    private readonly Vector4[] _strA = new Vector4[StructureMax], _strB = new Vector4[StructureMax], _strC = new Vector4[StructureMax], _strD = new Vector4[StructureMax];
    private readonly Vector4[] _shellA = new Vector4[ShellMax], _shellB = new Vector4[ShellMax];
    private readonly Vector4[] _occ = new Vector4[OccluderMax];
    private readonly List<StarEntry> _stars = new List<StarEntry>();
    private readonly Dictionary<StarEntry, SceneCache> _scenes = new Dictionary<StarEntry, SceneCache>();
    private float _starsListed = -100f;
    private Camera _camera;
    private Material _material;
    private CommandBuffer _buffer;
    private bool _attached, _failed;

    private sealed class SceneCache
    {
        public StarScene Scene;
        public float RealTime;
        public double Ut;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        if (_failed || !StarSettings.Enabled) return;
        Engine engine = Engine.Instance;
        if (engine == null || FlightGlobals.fetch == null || Planetarium.fetch == null) return;
        try
        {
            if (_material == null)
            {
                _material = ShaderLibrary.CreateMaterial(ShaderName);
                if (_material == null)
                {
                    _failed = true;
                    Log.Error("Star shader missing from the shader bundle; star effects are off.");
                    return;
                }
            }
            if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Stars" };
            _buffer.Clear();
            ListStars();

            Transform view = _camera.transform;
            float tanY = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanX = tanY * _camera.aspect;
            double inverseScale = ScaledSpace.InverseScaleFactor;
            double ut = Planetarium.GetUniversalTime();
            var margin = new Vector2(4f / Mathf.Max(_camera.pixelWidth, 1), 4f / Mathf.Max(_camera.pixelHeight, 1));

            // The biggest on screen first.
            var order = new List<KeyValuePair<float, StarEntry>>();
            foreach (StarEntry e in _stars)
            {
                if (e.Body == null || e.Body.scaledBody == null) continue;
                Vector3 center = e.Body.scaledBody.transform.position - view.position;
                float distance = center.magnitude;
                float radius = (float)(e.Body.Radius * inverseScale);
                if (distance <= radius * 1.01f) continue;
                float pixels = radius / distance / tanY * _camera.pixelHeight * 0.5f;
                if (pixels < MinPixelRadius) continue;
                order.Add(new KeyValuePair<float, StarEntry>(-pixels, e));
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));

            int drawn = 0;
            for (int s = 0; s < order.Count && drawn < MaxStars; s++)
            {
                if (Draw(order[s].Value, view, tanX, tanY, margin, inverseScale, ut, drawn)) drawn++;
            }
            if (drawn == 0) return;
            if (!_attached)
            {
                _camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
                _attached = true;
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            Detach();
            Log.Exception("Star rendering failed; disabled", ex);
        }
    }

    private void ListStars()
    {
        if (Time.realtimeSinceStartup - _starsListed < 3f && _stars.Count > 0) return;
        _starsListed = Time.realtimeSinceStartup;
        _stars.Clear();
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            if (b == null || b.scaledBody == null || !StarWorld.IsStar(b) || OwnedByAnotherObject(b)) continue;
            StarEntry e = StarWorld.Entry(b);
            if (e != null && e.Profile.Enabled && !e.Profile.IsBrownDwarf) _stars.Add(e);
        }
    }

    /// <summary>A body that the black hole feature draws as a hole, a neutron star, a galaxy or a nebula is not drawn as a star.</summary>
    private static bool OwnedByAnotherObject(CelestialBody body)
    {
        foreach (BlackHoleDefinition d in BlackHoles.Definitions)
        {
            if (!d.Enabled || d.BodyName != body.bodyName) continue;
            if (d.Kind == SpaceObjectKind.BlackHole || d.Kind == SpaceObjectKind.NeutronStar || d.Kind == SpaceObjectKind.Galaxy || d.Kind == SpaceObjectKind.Nebula) return true;
        }
        return false;
    }

    private bool Draw(StarEntry entry, Transform view, float tanX, float tanY, Vector2 margin, double inverseScale, double ut, int index)
    {
        CelestialBody body = entry.Body;
        StarProfile profile = entry.Profile;
        float radius = (float)(body.Radius * inverseScale);
        Vector3 center = body.scaledBody.transform.position - view.position;
        float distance = center.magnitude;

        StarView viewMode = StarSettings.View;
        bool prominences = profile.Prominences && StarSettings.Prominences;
        bool spots = profile.Spots && StarSettings.Spots;
        bool cmes = profile.Cmes && StarSettings.Cmes;
        bool corona = profile.Corona && StarSettings.Corona;

        StarWorld.Basis(body, out Vector3 mx, out Vector3 my, out Vector3 mz);
        Vector3 toViewer = -center.normalized;
        var viewerModel = new Double3(Vector3.Dot(toViewer, mx), Vector3.Dot(toViewer, my), Vector3.Dot(toViewer, mz));

        if (!_scenes.TryGetValue(entry, out SceneCache cache)) _scenes[entry] = cache = new SceneCache();
        float now = Time.realtimeSinceStartup;
        if (cache.Scene == null || now - cache.RealTime > 0.25f || Math.Abs(ut - cache.Ut) > 3600.0 * 6)
        {
            cache.Scene = entry.Activity.Snapshot(ut, viewerModel, SpotMax, StructureMax, ShellMax);
            cache.RealTime = now;
            cache.Ut = ut;
        }
        StarScene scene = cache.Scene;
        if (!spots) scene = WithoutSpots(scene);

        // The bounding sphere: the corona's reach in this view, or a CME's shell, whichever is more.
        float bound = viewMode == StarView.Eye ? 3.5f : 5.0f;
        float structures = 0f;
        foreach (StarStructure st in scene.Structures) structures = Mathf.Max(structures, (float)(st.Height * StarSettings.LoopSize + (st.A - st.B).Length) + 1.2f);
        bound = Mathf.Max(bound, structures);
        if (cmes)
            foreach (CmeShell sh in scene.Shells) bound = Mathf.Max(bound, (float)sh.Apex * 1.4f + 1f);
        if (!ScreenRect(center, bound * radius, view, tanX, tanY, margin, out Vector4 rect)) return false;

        // Bodies in front of the star, nearest first; and how much of the disc they leave.
        int occluders = FillOccluders(body, center, radius, view.position, inverseScale, profile.Temperature, out double visibleFraction);
        float veil = (float)StarOptics.CoronaVisibility(visibleFraction);

        while (_blocks.Count <= index) _blocks.Add(new MaterialPropertyBlock());
        MaterialPropertyBlock block = _blocks[index];
        block.SetVector(RectId, rect);
        block.SetVector(CenterId, new Vector4(center.x, center.y, center.z, radius));
        block.SetVector(RightId, view.right);
        block.SetVector(UpId, view.up);
        block.SetVector(ForwardId, view.forward);
        block.SetVector(MXId, mx);
        block.SetVector(MYId, my);
        block.SetVector(MZId, mz);
        block.SetVector(RayId, new Vector4(tanX, tanY, bound, (float)(Time.unscaledTime % 100000.0)));
        block.SetVector(LookId, new Vector4((float)profile.Temperature, (float)profile.DiscRadiance, (float)(int)viewMode, (float)StarSettings.Visibility));
        block.SetVector(Look2Id, new Vector4((float)Math.Min(scene.Activity, 1.5), corona ? 1f : 0f, (float)StarSettings.SurfaceDetail, veil));
        block.SetVector(FlagsId, new Vector4(StarSettings.ReplaceSurface && profile.Photosphere ? 1f : 0f, prominences ? 1f : 0f, cmes ? 1f : 0f, (float)scene.StreamerSpread));
        double turn = 2.0 * Math.PI * ((ut / profile.EquatorialRotationPeriod) % 1.0);
        block.SetVector(TextureId, new Vector4((float)turn, (float)scene.FlareLevel, (float)StarActivity.MeanLatitude(scene.CyclePhase), (float)(profile.Seed % 997)));

        int spotCount = Mathf.Min(scene.Spots.Count, SpotMax);
        for (int i = 0; i < spotCount; i++)
        {
            SpotGroup g = scene.Spots[i];
            _spotA[i] = new Vector4((float)g.Dir.X, (float)g.Dir.Y, (float)g.Dir.Z, (float)g.HalfSeparation);
            _spotB[i] = new Vector4((float)g.Axis.X, (float)g.Axis.Y, (float)g.Axis.Z, (float)g.UmbraRadius);
            _spotC[i] = new Vector4((float)g.FaculaRadius, (float)g.Strength, (float)g.Flare, (float)(g.Seed % 997.0));
            _spotD[i] = new Vector4((float)g.Age, 0f, 0f, 0f);
        }
        int structureCount = prominences ? Mathf.Min(scene.Structures.Count, StructureMax) : 0;
        for (int i = 0; i < structureCount; i++)
        {
            StarStructure st = scene.Structures[i];
            float size = (float)StarSettings.LoopSize;
            st.Height *= size;
            st.Width *= Mathf.Sqrt(size);
            _strA[i] = new Vector4((float)st.A.X, (float)st.A.Y, (float)st.A.Z, st.Kind == StructureKind.Arch ? 0f : 1f);
            _strB[i] = new Vector4((float)st.B.X, (float)st.B.Y, (float)st.B.Z, (float)st.Height);
            _strC[i] = new Vector4((float)st.Width, (float)st.Ribbon, (float)st.Nest, (float)st.Brightness);
            _strD[i] = new Vector4((float)st.Opacity, (float)st.Erupt, (float)(st.Seed % 997.0), 0f);
        }
        int shellCount = cmes ? Mathf.Min(scene.Shells.Count, ShellMax) : 0;
        for (int i = 0; i < shellCount; i++)
        {
            CmeShell sh = scene.Shells[i];
            _shellA[i] = new Vector4((float)sh.Dir.X, (float)sh.Dir.Y, (float)sh.Dir.Z, (float)sh.Apex);
            _shellB[i] = new Vector4((float)sh.HalfWidth, (float)(sh.Seed % 997.0), (float)sh.Core, 0f);
        }
        block.SetVector(CountsId, new Vector4(spotCount, structureCount, shellCount, 2f * tanY / Mathf.Max(_camera.pixelHeight, 1)));
        block.SetVectorArray(SpotAId, _spotA);
        block.SetVectorArray(SpotBId, _spotB);
        block.SetVectorArray(SpotCId, _spotC);
        block.SetVectorArray(SpotDId, _spotD);
        block.SetVectorArray(StrAId, _strA);
        block.SetVectorArray(StrBId, _strB);
        block.SetVectorArray(StrCId, _strC);
        block.SetVectorArray(StrDId, _strD);
        block.SetVectorArray(ShellAId, _shellA);
        block.SetVectorArray(ShellBId, _shellB);
        block.SetVectorArray(OccId, _occ);
        _buffer.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, QuadVertices, 1, block);
        return true;
    }

    private static StarScene WithoutSpots(StarScene scene)
    {
        var copy = new StarScene { Time = scene.Time, Activity = scene.Activity, CyclePhase = scene.CyclePhase, StreamerSpread = scene.StreamerSpread, FlareLevel = 0.0 };
        copy.Structures.AddRange(scene.Structures);
        copy.Shells.AddRange(scene.Shells);
        return copy;
    }

    /// <summary>
    /// Fills <see cref="_occ"/> with the bodies in front of the star whose spheres could cover some of it or its corona
    /// (centre relative to the eye and radius, scaled units), nearest first, and returns how many there are. The disc's visible
    /// fraction is the smallest left by any one of them (an Eclipse lens integral with the star's limb darkening).
    /// </summary>
    private int FillOccluders(CelestialBody star, Vector3 starCenter, float starRadius, Vector3 eye, double inverseScale, double temperature, out double visibleFraction)
    {
        visibleFraction = 1.0;
        for (int i = 0; i < OccluderMax; i++) _occ[i] = Vector4.zero;
        float starDistance = starCenter.magnitude;
        double starAngle = Math.Asin(Math.Min(1.0, starRadius / starDistance));
        var found = new List<KeyValuePair<float, Vector4>>();
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            if (b == null || b == star || b.scaledBody == null) continue;
            Vector3 c = b.scaledBody.transform.position - eye;
            float r = (float)(b.Radius * inverseScale);
            float d = c.magnitude;
            if (d <= r || d >= starDistance) continue;
            if (Vector3.Dot(c, starCenter) <= 0f) continue;   // behind the camera, away from the star: it cannot cover it
            double angle = Math.Asin(Math.Min(1.0, r / d));
            double separation = Math.Acos(Math.Max(-1.0, Math.Min(1.0, Vector3.Dot(c, starCenter) / (d * starDistance))));
            if (separation > starAngle * 4.5 + angle) continue;   // well clear of the disc and its inner corona
            found.Add(new KeyValuePair<float, Vector4>(d, new Vector4(c.x, c.y, c.z, r)));
            if (separation < starAngle + angle)
                visibleFraction = Math.Min(visibleFraction, StarOptics.DiscVisibleFraction(starAngle, angle, separation, temperature));
        }
        found.Sort((a, b) => a.Key.CompareTo(b.Key));
        int count = Math.Min(found.Count, OccluderMax);
        for (int i = 0; i < count; i++) _occ[i] = found[i].Value;
        return count;
    }

    private static bool ScreenRect(Vector3 center, float radius, Transform view, float tanX, float tanY, Vector2 margin, out Vector4 rect)
    {
        rect = new Vector4(-1f, -1f, 1f, 1f);
        if (center.sqrMagnitude <= radius * radius * 1.0201f) return true;
        float cz = Vector3.Dot(center, view.forward);
        if (!AxisRange(Vector3.Dot(center, view.right), cz, radius, tanX, out float x0, out float x1)) return false;
        if (!AxisRange(Vector3.Dot(center, view.up), cz, radius, tanY, out float y0, out float y1)) return false;
        rect = new Vector4(Mathf.Max(-1f, x0 - margin.x), Mathf.Max(-1f, y0 - margin.y), Mathf.Min(1f, x1 + margin.x), Mathf.Min(1f, y1 + margin.y));
        return true;
    }

    private static bool AxisRange(float c, float z, float radius, float tan, out float min, out float max)
    {
        min = -1f;
        max = 1f;
        float d = Mathf.Sqrt(c * c + z * z);
        if (d <= radius) return true;
        float mid = Mathf.Atan2(c, z), half = Mathf.Asin(radius / d);
        float lo = mid - half, hi = mid + half;
        const float quarter = Mathf.PI * 0.5f;
        if (lo >= quarter || hi <= -quarter) return false;
        if (lo > -quarter) min = Mathf.Max(-1f, Mathf.Tan(lo) / tan);
        if (hi < quarter) max = Mathf.Min(1f, Mathf.Tan(hi) / tan);
        return min < max;
    }

    private void OnPostRender()
    {
        Detach();
    }

    private void OnDisable()
    {
        Detach();
    }

    private void OnDestroy()
    {
        Detach();
        _buffer?.Release();
        if (_material != null) Destroy(_material);
    }

    private void Detach()
    {
        if (!_attached || _camera == null) return;
        _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
        _attached = false;
    }
}
