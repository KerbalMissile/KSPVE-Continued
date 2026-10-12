using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space;
using KSPVE.Pure.Space.Tde;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Space;

/// <summary>
/// Draws every configured black hole on the ScaledSpace camera after transparents, so the disk sits over
/// Singularity's lensed background (which Singularity keeps drawing) and under nothing nearer than the hole.
/// Each hole is ray-marched only over the screen rectangle its bounding sphere covers (nothing at all when it is
/// off screen), into a reduced-resolution buffer when blackHoleResolution is below 1, and the scaled scene's
/// depth texture is only requested while a body could actually be in front of a hole.
/// </summary>
[DisallowMultipleComponent]
internal sealed class BlackHoleRenderer : MonoBehaviour
{
    private const string ShaderName = "Hidden/KSPVE/Space/BlackHole";
    private const int MarchPass = 0, CompositePass = 1, ObjectPass = 2;
    private const int QuadVertices = 6;
    // The whole disk turns together: 0.75 rad/s (once every ~8 s) at rotationSpeed 1.
    private const double SpinRate = 0.75;
    // Bodies count as possibly in front out to this multiple of their radius plus atmosphere (clouds, rings, glow).
    private const double OccluderMargin = 2.5;
    // Bodies the shader tests as solid spheres, nearest first (BH_OCCLUDERS in the shader).
    private const int MaxOccluders = 8;

    private static readonly int CenterId = Shader.PropertyToID("_KSPVE_BHCenter");
    private static readonly int NormalId = Shader.PropertyToID("_KSPVE_BHNormal");
    private static readonly int JetAxisId = Shader.PropertyToID("_KSPVE_BHJetAxis");
    private static readonly int DiskId = Shader.PropertyToID("_KSPVE_BHDisk");
    private static readonly int MaskId = Shader.PropertyToID("_KSPVE_BHMask");
    private static readonly int OccludersId = Shader.PropertyToID("_KSPVE_BHOccluders");
    private static readonly int LookId = Shader.PropertyToID("_KSPVE_BHLook");
    private static readonly int ExtraId = Shader.PropertyToID("_KSPVE_BHExtra");
    private static readonly int JetId = Shader.PropertyToID("_KSPVE_BHJet");
    private static readonly int Jet2Id = Shader.PropertyToID("_KSPVE_BHJet2");
    private static readonly int JetKnotsId = Shader.PropertyToID("_KSPVE_BHJetKnots");
    private static readonly int RayId = Shader.PropertyToID("_KSPVE_BHRay");
    private static readonly int OptionsId = Shader.PropertyToID("_KSPVE_BHOptions");
    private static readonly int RectId = Shader.PropertyToID("_KSPVE_BHRect");
    private static readonly int RightId = Shader.PropertyToID("_KSPVE_BHRight");
    private static readonly int UpId = Shader.PropertyToID("_KSPVE_BHUp");
    private static readonly int ForwardId = Shader.PropertyToID("_KSPVE_BHForward");
    private static readonly int BufferId = Shader.PropertyToID("_KSPVE_BHBuffer");
    private static readonly int FormId = Shader.PropertyToID("_KSPVE_BHForm");
    private static readonly int StreakId = Shader.PropertyToID("_KSPVE_BHStreak");

    // The other kinds of object (BlackHole.shader pass 2, fragObject).
    private static readonly int ObjKindId = Shader.PropertyToID("_KSPVE_ObjKind");
    private static readonly int ObjShapeId = Shader.PropertyToID("_KSPVE_ObjShape");
    private static readonly int ObjDustId = Shader.PropertyToID("_KSPVE_ObjDust");
    private static readonly int ObjRocksId = Shader.PropertyToID("_KSPVE_ObjRocks");
    private static readonly int ObjCloudId = Shader.PropertyToID("_KSPVE_ObjCloud");
    private static readonly int ObjNeutronId = Shader.PropertyToID("_KSPVE_ObjNeutron");
    private static readonly int ObjCometId = Shader.PropertyToID("_KSPVE_ObjComet");
    private static readonly int ObjComet2Id = Shader.PropertyToID("_KSPVE_ObjComet2");
    private static readonly int ObjColorAId = Shader.PropertyToID("_KSPVE_ObjColorA");
    private static readonly int ObjColorBId = Shader.PropertyToID("_KSPVE_ObjColorB");
    private static readonly int ObjDustColorId = Shader.PropertyToID("_KSPVE_ObjDustColor");
    private static readonly int ObjComaColorId = Shader.PropertyToID("_KSPVE_ObjComaColor");
    private static readonly int ObjSunId = Shader.PropertyToID("_KSPVE_ObjSun");
    private static readonly int ObjVelId = Shader.PropertyToID("_KSPVE_ObjVel");
    private static readonly int ObjExtraId = Shader.PropertyToID("_KSPVE_ObjExtra");
    private static readonly int ObjGapsId = Shader.PropertyToID("_KSPVE_ObjGaps");
    private static readonly int ObjOuterDustId = Shader.PropertyToID("_KSPVE_ObjOuterDust");
    private static readonly int ObjRockColorId = Shader.PropertyToID("_KSPVE_ObjRockColor");
    private readonly Vector4[] _gapVectors = new Vector4[DiskGaps.Max];
    // Each object's gaps, worked out again only when their settings change (DiskGaps parses the hand-placed lists).
    private readonly Dictionary<BlackHoleDefinition, GapCache> _gapCache = new Dictionary<BlackHoleDefinition, GapCache>();

    private sealed class GapCache
    {
        public double Inner, Outer, Rings, Depth, Width;
        public string Radii, Widths, Depths;
        public readonly DiskGap[] Gaps = new DiskGap[DiskGaps.Max];
        public int Count = -1;
    }
    // The disk's parts (both passes: a neutron star's disk is drawn as a black hole's).
    private static readonly int TintId = Shader.PropertyToID("_KSPVE_BHTint");
    private static readonly int TintInnerId = Shader.PropertyToID("_KSPVE_BHTintInner");
    private static readonly int TintOuterId = Shader.PropertyToID("_KSPVE_BHTintOuter");
    private static readonly int JetColorId = Shader.PropertyToID("_KSPVE_BHJetColor");
    private static readonly int RingColorId = Shader.PropertyToID("_KSPVE_BHRingColor");
    private static readonly int AdvancedId = Shader.PropertyToID("_KSPVE_BHAdvanced");
    private static readonly int Advanced2Id = Shader.PropertyToID("_KSPVE_BHAdvanced2");
    private static readonly int Advanced3Id = Shader.PropertyToID("_KSPVE_BHAdvanced3");
    private static readonly int ObjDisk2Id = Shader.PropertyToID("_KSPVE_ObjDisk2");
    private static readonly int ObjDisk3Id = Shader.PropertyToID("_KSPVE_ObjDisk3");
    private static readonly int ObjSpiralId = Shader.PropertyToID("_KSPVE_ObjSpiral");
    private static readonly int ObjCloud2Id = Shader.PropertyToID("_KSPVE_ObjCloud2");
    private static readonly int ObjComet3Id = Shader.PropertyToID("_KSPVE_ObjComet3");
    private static readonly int ObjFieldId = Shader.PropertyToID("_KSPVE_ObjField");
    private static readonly int ObjNeutron2Id = Shader.PropertyToID("_KSPVE_ObjNeutron2");
    private static readonly int ObjGalaxyId = Shader.PropertyToID("_KSPVE_ObjGalaxy");
    // Steps more for an object round a black hole: its rays are bent, in short steps near the horizon.
    private const int HostedExtraSteps = 150;
    // A comet's motion is taken over this long (s of game time), relative to its star.
    private const double CometVelocityStep = 3600.0;

    // The tidal disruption event (BlackHole.shader, TdeSample).
    private const int TdeExtraSteps = 200;
    private static readonly int TdeId = Shader.PropertyToID("_KSPVE_Tde");
    private static readonly int TdeNormalId = Shader.PropertyToID("_KSPVE_TdeNormal");
    private static readonly int TdeStarId = Shader.PropertyToID("_KSPVE_TdeStar");
    private static readonly int TdeStarAxisId = Shader.PropertyToID("_KSPVE_TdeStarAxis");
    private static readonly int TdeStarVelId = Shader.PropertyToID("_KSPVE_TdeStarVel");
    private static readonly int TdeStarLookId = Shader.PropertyToID("_KSPVE_TdeStarLook");
    private static readonly int TdeVertsId = Shader.PropertyToID("_KSPVE_TdeVerts");
    private static readonly int TdeVertLookId = Shader.PropertyToID("_KSPVE_TdeVertLook");
    private static readonly int TdeVertVelId = Shader.PropertyToID("_KSPVE_TdeVertVel");
    private static readonly int TdeChunksId = Shader.PropertyToID("_KSPVE_TdeChunks");
    private static readonly int TdeShockId = Shader.PropertyToID("_KSPVE_TdeShock");
    private static readonly int TdeShockLookId = Shader.PropertyToID("_KSPVE_TdeShockLook");
    private static readonly int TdeEnvId = Shader.PropertyToID("_KSPVE_TdeEnv");
    private static readonly int TdeEnvKId = Shader.PropertyToID("_KSPVE_TdeEnvK");
    private static readonly int TdeEnvTId = Shader.PropertyToID("_KSPVE_TdeEnvT");
    private static readonly int TdeLightId = Shader.PropertyToID("_KSPVE_TdeLight");
    private static readonly int TdeFlashId = Shader.PropertyToID("_KSPVE_TdeFlash");
    private static readonly int TdeFlashLookId = Shader.PropertyToID("_KSPVE_TdeFlashLook");
    private readonly Vector4[] _tdeVerts = new Vector4[TdeFrame.MaxVertices];
    private readonly Vector4[] _tdeVertLook = new Vector4[TdeFrame.MaxVertices];
    private readonly Vector4[] _tdeVertVel = new Vector4[TdeFrame.MaxVertices];
    private readonly Vector4[] _tdeChunks = new Vector4[TdeFrame.MaxChunks];
    private readonly float[] _tdeEnvK = new float[TdeFrame.EnvelopeBins];
    private readonly float[] _tdeEnvT = new float[TdeFrame.EnvelopeBins];
    private readonly Vector4[] _occluders = new Vector4[MaxOccluders];
    private readonly float[] _occluderDistance = new float[MaxOccluders];

    private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
    private readonly List<int> _passes = new List<int>();
    private readonly MaterialPropertyBlock _compositeBlock = new MaterialPropertyBlock();
    private Camera _camera;
    private Material _material;
    private CommandBuffer _buffer;
    private bool _attached;
    private bool _failed;
    private bool _depthAdded;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        if (_failed) return;
        Engine engine = Engine.Instance;
        if (engine == null || FlightGlobals.fetch == null) return;
        try
        {
            if (_material == null)
            {
                _material = ShaderLibrary.CreateMaterial(ShaderName);
                if (_material == null)
                {
                    _failed = true;
                    Log.Error("Black hole shader missing from the shader bundle; black holes are off.");
                    return;
                }
            }
            if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Black Holes" };
            _buffer.Clear();

            Transform view = _camera.transform;
            float tanY = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanX = tanY * _camera.aspect;
            double time = Time.unscaledTime;
            double inverseScale = ScaledSpace.InverseScaleFactor;

            float scale = Mathf.Clamp((float)engine.Post.BlackHoleResolution, 0.05f, 4f);
            bool reduced = Mathf.Abs(scale - 1f) > 0.01f;   // drawn into a buffer at this scale (down, or supersampled)
            int width = reduced ? Mathf.Max(1, (int)(_camera.pixelWidth * scale)) : _camera.pixelWidth;
            int height = reduced ? Mathf.Max(1, (int)(_camera.pixelHeight * scale)) : _camera.pixelHeight;
            // Two target pixels of margin so the bilinear upscale never clips the edge of the hole.
            var margin = new Vector2(4f / Mathf.Max(width, 1), 4f / Mathf.Max(height, 1));

            int drawn = 0;
            bool needDepth = false;
            var union = new Vector4(1f, 1f, -1f, -1f);
            foreach (BlackHoleDefinition hole in BlackHoles.Definitions)
            {
                if (!hole.Enabled) continue;
                CelestialBody body = hole.Body;
                if (body == null || body.scaledBody == null) continue;

                // The shader's unit: the horizon radius for a black hole, the object's length unit (sized to the cloud for
                // a nebula) for the rest.
                bool isHole = hole.IsBlackHole;
                double unitMeters = hole.HorizonMeters(body);
                double shaderMeters = SpaceObjects.ShaderUnitMeters(hole.Kind, unitMeters, hole.DiskOuter);
                float horizon = (float)(shaderMeters * inverseScale);
                if (horizon <= 0f) continue;
                // The star at the centre, in shader units: a neutron star's own radius, the body for the rest.
                double starUnits = hole.Kind == SpaceObjectKind.NeutronStar ? hole.StarRadius : body.Radius / shaderMeters;
                // Round a black hole (another object on the same body): its rays are bent by it and end at its horizon, so
                // the gas behind it is lensed round the shadow rather than showing through it.
                BlackHoleDefinition host = isHole || hole.Kind == SpaceObjectKind.NeutronStar ? null : HoleOn(body);   // a neutron star bends its own
                double holeUnits = host != null ? host.HorizonMeters(body) / shaderMeters : 0.0;
                if (holeUnits > 0.0) starUnits = holeUnits;
                // The hole's disk and jets, in this object's units: what the object's gas beyond them must not dim.
                double holeReach = host != null ? (host.BoundRadius > 0 ? host.BoundRadius : HoleBound(host)) * holeUnits : 0.0;

                Vector3 center = body.scaledBody.transform.position - view.position;
                DiskState disk = BlackHoles.StateOf(hole);
                double kindBound = isHole
                    ? HoleBound(hole)
                    : SpaceObjects.Bound(hole.Kind, hole.DiskOuter, hole.DiskThickness, hole.Flaring, hole.EdgeSoftness, starUnits,
                        hole.FieldLines, hole.FieldShells, hole.FieldShellRatio, hole.Beams, hole.JetLength, hole.ComaSize,
                        hole.IonTailLength, hole.DustTailLength, hole.DustTailCurve);
                float core = (float)(hole.BoundRadius > 0 ? hole.BoundRadius : kindBound);
                TdeFrame tde = isHole ? TidalDisruptions.FrameFor(hole) : null;
                float bound = tde != null ? Mathf.Max(core, (float)tde.Extent) : core;
                if (!ScreenRect(center, bound * horizon, view, tanX, tanY, margin, out Vector4 rect)) continue;
                // A star at the centre of a dusty disk hides the disk behind it.
                bool depth = Occluders(body, center, bound * horizon, view.position, inverseScale, horizon, SpaceObjects.BodyHidesObject(hole.Kind) && host == null) > 0;
                needDepth |= depth;
                union = new Vector4(Mathf.Min(union.x, rect.x), Mathf.Min(union.y, rect.y), Mathf.Max(union.z, rect.z), Mathf.Max(union.w, rect.w));

                Vector3 normal = ((Vector3)(Planetarium.Rotation * hole.Normal)).normalized;
                Vector3 jetAxis = normal;
                if (hole.BeamPeriod > 0.0)
                {
                    // The jets sweep round the spin axis (the normal) like a pulsar's beams, in real time like the disk.
                    Double3 beam = BlackHoleSettings.PulsarBeamAxis(new Double3(normal.x, normal.y, normal.z), hole.BeamTilt,
                        2.0 * Math.PI * (time / hole.BeamPeriod % 1.0));
                    jetAxis = new Vector3((float)beam.X, (float)beam.Y, (float)beam.Z);
                }
                if (hole.Pulsar) normal = jetAxis;   // a pulsar has no disk: its frame follows the beams
                float spin = hole.Spin < 0 ? -1f : 1f;
                float rotation = spin * (float)(hole.RotationSpeed * SpinRate * time % (2.0 * Math.PI));

                if (_blocks.Count <= drawn)
                {
                    _blocks.Add(new MaterialPropertyBlock());
                    _passes.Add(MarchPass);
                }
                MaterialPropertyBlock block = _blocks[drawn];
                _passes[drawn] = isHole ? MarchPass : ObjectPass;
                block.SetVector(RectId, rect);
                block.SetVector(CenterId, new Vector4(center.x, center.y, center.z, horizon));
                block.SetVector(NormalId, new Vector4(normal.x, normal.y, normal.z, spin));
                block.SetVector(JetAxisId, new Vector4(jetAxis.x, jetAxis.y, jetAxis.z, 0f));
                block.SetVector(DiskId, new Vector4((float)disk.ShapeInner, (float)disk.ShapeOuter, (float)disk.Thickness, (float)hole.Turbulence));
                block.SetVectorArray(OccludersId, _occluders);
                block.SetVector(MaskId, new Vector4((float)disk.MaskInner, (float)disk.MaskOuter, (float)disk.MaskSoftness, 0f));
                // How high the camera sits above the disk's plane, seen from the hole: the streak look fades in with it.
                double elevation = Math.Asin(Math.Min(1.0, Math.Abs(Vector3.Dot(-center.normalized, normal)))) * 180.0 / Math.PI;
                block.SetVector(StreakId, new Vector4((float)BlackHoleSettings.StreakView(hole.DiskStreaks, elevation), (float)hole.OuterGlow, 0f, 0f));
                block.SetVector(FormId, new Vector4((float)disk.Presence, (float)disk.Brightness, (float)disk.Spiral, (float)disk.JetHeads));
                block.SetVector(LookId, new Vector4((float)hole.PeakTemperature, (float)(hole.Brightness * engine.Post.BlackHoleBrightness), rotation, (float)(time % 10000.0)));
                // The swirl keeps the configured pitch and flow as a young jet grows out to its full length.
                block.SetVector(JetId, new Vector4((float)disk.Jets, (float)disk.JetLength, (float)hole.JetWidth, (float)hole.JetSpeed));
                block.SetVector(JetKnotsId, new Vector4((float)hole.JetKnots, (float)hole.JetKnotStart, (float)hole.JetKnotSpacing, (float)hole.JetKnotGrowth));
                block.SetVector(Jet2Id, new Vector4((float)hole.JetSpiral, (float)BlackHoleSettings.JetTwistRate(hole.JetTurns, hole.JetLength),
                    (float)BlackHoleSettings.JetFlowRate(hole.JetFlow, hole.JetLength), (float)hole.JetAbsorb));
                block.SetVector(ExtraId, new Vector4((float)hole.ColorSaturation, (float)(hole.Glow * disk.Presence * (1.0 + 3.0 * disk.LaunchFlash)), (float)hole.PhotonRing, (float)(hole.Particles * disk.Presence)));
                block.SetVector(RayId, new Vector4(tanX, tanY, hole.Steps + (isHole ? BlackHoleSettings.ExtraSteps(disk.Outer) : 0) + (tde != null ? TdeExtraSteps : 0)
                    + (holeUnits > 0.0 ? HostedExtraSteps : 0), bound));
                SetTde(block, tde, hole, core);
                block.SetVector(TintId, new Vector4((float)hole.DiskTint, (float)hole.InnerBrightness, (float)hole.OuterBrightness, 0f));
                block.SetVector(TintInnerId, Rgb(hole.InnerColor));
                block.SetVector(TintOuterId, Rgb(hole.OuterColor));
                block.SetVector(JetColorId, Rgb(hole.JetColor));
                block.SetVector(RingColorId, Rgb(hole.PhotonRingColor));
                block.SetVector(AdvancedId, new Vector4((float)hole.DiskDensity, (float)hole.DiskOpacity, (float)hole.Emission, (float)hole.CloudSize));
                block.SetVector(Advanced2Id, new Vector4((float)hole.SelfShadow, (float)hole.InnerLight, (float)hole.StreakWinding, (float)hole.StreakStretch));
                block.SetVector(Advanced3Id, new Vector4((float)hole.StreakLanes, (float)hole.ParticleSpacing, 0f, 0f));
                if (!isHole) SetObject(block, hole, body, starUnits, shaderMeters, holeUnits, holeReach);
                block.SetVector(OptionsId, new Vector4(depth ? 1f : 0f, 2f * tanY / height, (float)(hole.Debris * disk.Presence), hole.Pulsar ? 1f : 0f));
                block.SetVector(RightId, view.right);
                block.SetVector(UpId, view.up);
                block.SetVector(ForwardId, view.forward);
                drawn++;
            }

            SetDepthTexture(needDepth);
            if (drawn == 0) return;

            if (reduced)
            {
                _buffer.GetTemporaryRT(BufferId, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                _buffer.SetRenderTarget(BufferId);
                _buffer.ClearRenderTarget(false, true, Color.clear);
                DrawAll(drawn);
                _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                _compositeBlock.SetVector(RectId, union);
                _buffer.DrawProcedural(Matrix4x4.identity, _material, CompositePass, MeshTopology.Triangles, QuadVertices, 1, _compositeBlock);
                _buffer.ReleaseTemporaryRT(BufferId);
            }
            else
            {
                DrawAll(drawn);
            }

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
            SetDepthTexture(false);
            Log.Exception("Black hole rendering failed; disabled", ex);
        }
    }

    /// <summary>The uniforms of another kind of object (fragObject); sizes in shader units (<paramref name="shaderMeters"/> each).</summary>
    private void SetObject(MaterialPropertyBlock block, BlackHoleDefinition h, CelestialBody body, double starUnits, double shaderMeters,
        double holeUnits, double holeReach)
    {
        if (!_gapCache.TryGetValue(h, out GapCache cache)) _gapCache[h] = cache = new GapCache();
        if (cache.Count < 0 || cache.Inner != h.DiskInner || cache.Outer != h.DiskOuter || cache.Rings != h.Rings || cache.Depth != h.RingDepth
            || cache.Width != h.RingWidth || cache.Radii != h.Gaps || cache.Widths != h.GapWidths || cache.Depths != h.GapDepths)
        {
            cache.Inner = h.DiskInner; cache.Outer = h.DiskOuter; cache.Rings = h.Rings; cache.Depth = h.RingDepth; cache.Width = h.RingWidth;
            cache.Radii = h.Gaps; cache.Widths = h.GapWidths; cache.Depths = h.GapDepths;
            cache.Count = DiskGaps.Fill(h.DiskInner, h.DiskOuter, h.Rings, h.RingDepth, h.RingWidth, h.Gaps, h.GapWidths, h.GapDepths, cache.Gaps);
            if (_gapCache.Count > 256) _gapCache.Clear();   // objects removed or reset leave stale entries
        }
        int gaps = cache.Count;
        for (int i = 0; i < DiskGaps.Max; i++)
            _gapVectors[i] = i < gaps ? new Vector4((float)cache.Gaps[i].Radius, (float)cache.Gaps[i].Width, (float)cache.Gaps[i].Depth, 0f) : Vector4.zero;
        block.SetVectorArray(ObjGapsId, _gapVectors);
        block.SetVector(ObjKindId, new Vector4((float)(int)h.Kind, (float)starUnits, (float)h.StarTemperature, (float)h.SurfaceTemperature));
        block.SetVector(ObjShapeId, new Vector4((float)h.Flaring, gaps, 0f, 0f));
        block.SetVector(ObjOuterDustId, Rgb(h.OuterDustColor));
        block.SetVector(ObjRockColorId, Rgb(h.RockColor));
        block.SetVector(ObjDustId, new Vector4((float)h.Dust, (float)h.LightFalloff, (float)h.StarGlow, (float)(h.RockSpacing * h.DiskOuter)));
        // w: the widest a rock may look, as its radius over its distance (rockMaxAngle; 0 = no limit).
        float rockCap = h.RockMaxAngle > 0.0 ? (float)Math.Tan(0.5 * h.RockMaxAngle * Math.PI / 180.0) : 0f;
        block.SetVector(ObjRocksId, new Vector4((float)h.Rocks, (float)h.RockSize, (float)h.RockAlbedo, rockCap));
        block.SetVector(ObjCloudId, new Vector4((float)h.DustLanes, (float)h.CloudScale, (float)h.CloudWarp, (float)h.Filaments));
        block.SetVector(ObjNeutronId, new Vector4((float)h.FieldLines, (float)h.AccretionColumns, (float)h.Beams, (float)h.JetLength));
        block.SetVector(ObjCometId, new Vector4((float)h.Activity, (float)h.ComaSize, (float)h.IonTailLength, (float)h.DustTailLength));
        block.SetVector(ObjComet2Id, new Vector4((float)h.DustTailCurve, (float)h.DustTailWidth, (float)h.IonTailWidth, (float)h.Streamers));
        block.SetVector(ObjComet3Id, new Vector4((float)h.ComaBrightness, (float)h.IonBrightness, (float)h.DustBrightness, (float)h.NucleusBrightness));
        block.SetVector(ObjDisk2Id, new Vector4((float)h.DensityFalloff, (float)h.EdgeSoftness, (float)h.Clumps, (float)h.ClumpSize));
        block.SetVector(ObjDisk3Id, new Vector4((float)h.ScatterAsymmetry, (float)h.RimGlow, (float)h.RimTemperature, (float)h.RockSpread));
        block.SetVector(ObjSpiralId, new Vector4((float)h.SpiralArms, (float)h.SpiralStrength, (float)h.SpiralWinding, (float)h.RockSizeSpread));
        block.SetVector(ObjCloud2Id, new Vector4((float)h.CloudStretch, (float)h.CloudRagged, (float)h.CloudFill, 0f));
        block.SetVector(ObjFieldId, new Vector4((float)Math.Round(h.FieldLineCount), (float)Math.Round(h.FieldShells), (float)h.FieldShellRatio, (float)h.FieldLineWidth));
        block.SetVector(ObjNeutron2Id, new Vector4((float)h.ColumnWidth, (float)h.BeamWidth, (float)h.Hotspots, 0f));
        block.SetVector(ObjGalaxyId, new Vector4((float)h.BulgeSize, (float)h.BulgeBrightness, (float)h.BarLength, (float)h.StarClusters));
        block.SetVector(ObjColorAId, Rgb(h.ColorA));
        block.SetVector(ObjColorBId, Rgb(h.ColorB));
        block.SetVector(ObjDustColorId, Rgb(h.DustColor));
        block.SetVector(ObjComaColorId, Rgb(h.ComaColor));
        block.SetVector(ObjExtraId, new Vector4((float)h.Shell, (float)h.Striae, (float)holeUnits, (float)holeReach));
        if (h.Kind != SpaceObjectKind.Comet) return;

        // The comet's tails: away from its star, curving back along its motion round it.
        CelestialBody star = BlackHoles.LightSourceOf(h, body);
        Vector3d toStar = star != null && star != body ? star.position - body.position : Vector3d.up;
        Vector3d sun = toStar.normalized;
        Vector3d velocity = Vector3d.zero;
        if (star != null && star != body)
        {
            double ut = Planetarium.GetUniversalTime();
            velocity = (body.getTruePositionAtUT(ut + CometVelocityStep) - body.getTruePositionAtUT(ut))
                - (star.getTruePositionAtUT(ut + CometVelocityStep) - star.getTruePositionAtUT(ut));
        }
        // Only the part across the star's direction bends the dust tail.
        Vector3d across = velocity - Vector3d.Dot(velocity, sun) * sun;
        if (across.sqrMagnitude < 1e-12) across = Vector3d.Cross(sun, Math.Abs(sun.y) < 0.9 ? Vector3d.up : Vector3d.right);
        Vector3d vel = across.normalized;
        block.SetVector(ObjSunId, new Vector4((float)sun.x, (float)sun.y, (float)sun.z, 0f));
        block.SetVector(ObjVelId, new Vector4((float)vel.x, (float)vel.y, (float)vel.z, 0f));
    }

    private static Vector4 Rgb(Double3 c) => new Vector4((float)c.X, (float)c.Y, (float)c.Z, 1f);

    /// <summary>Radius (horizon radii) of the sphere holding a black hole's disk and jets (blobs included).</summary>
    private static double HoleBound(BlackHoleDefinition h) =>
        Math.Max(h.DiskOuter * 1.25 + 8.0, h.Jets > 0
            ? BlackHoleSettings.JetBound(h.JetLength * (h.JetKnots > 0 ? 1.25 : 1.0), h.JetWidth * (h.JetKnots > 0 ? 2.5 + h.JetKnotGrowth : 1.0), h.JetSpiral)
            : 0.0);

    /// <summary>The black hole drawn on a body, if any (the other objects round it are lensed by it).</summary>
    private static BlackHoleDefinition HoleOn(CelestialBody body)
    {
        foreach (BlackHoleDefinition d in BlackHoles.Definitions)
            if (d.Enabled && d.IsBlackHole && !d.Pulsar && d.Body == body) return d;
        return null;
    }

    /// <summary>The black holes first, then the other objects over them (gas in front of a hole veils its shadow).</summary>
    private void DrawAll(int drawn)
    {
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < drawn; i++)
                if ((_passes[i] == MarchPass) == (pass == 0))
                    _buffer.DrawProcedural(Matrix4x4.identity, _material, _passes[i], MeshTopology.Triangles, QuadVertices, 1, _blocks[i]);
    }

    /// <summary>
    /// The event's uniforms. The model works in the hole's inertial disk frame (built from the configured normal, which
    /// is fixed in inertial space); it is carried into the world as the disk normal is, through Planetarium.Rotation.
    /// </summary>
    private void SetTde(MaterialPropertyBlock block, TdeFrame f, BlackHoleDefinition hole, float core)
    {
        if (f == null)
        {
            block.SetVector(TdeId, Vector4.zero);
            return;
        }
        Vector3d ni = hole.Normal.normalized;
        Vector3d ti = Vector3d.Cross(ni, Math.Abs(ni.y) < 0.99 ? new Vector3d(0, 1, 0) : new Vector3d(1, 0, 0)).normalized;
        Vector3d bi = Vector3d.Cross(ni, ti);
        var t = (Vector3)(Planetarium.Rotation * ti);
        var b = (Vector3)(Planetarium.Rotation * bi);
        var n = (Vector3)(Planetarium.Rotation * ni);
        Vector3 World(Double3 d) => t * (float)d.X + b * (float)d.Y + n * (float)d.Z;
        TdeParameters p = TidalDisruptions.Parameters;

        Vector3 normal = World(f.OrbitNormal);
        block.SetVector(TdeId, new Vector4(1f, f.VertexCount, f.ChunkCount, core));
        block.SetVector(TdeNormalId, new Vector4(normal.x, normal.y, normal.z, 0f));
        Vector3 star = World(f.StarCenter), axis = World(f.StarAxis), vel = World(f.StarVelocity);
        block.SetVector(TdeStarId, new Vector4(star.x, star.y, star.z, f.StarVisible ? (float)f.StarPresence : 0f));
        block.SetVector(TdeStarAxisId, new Vector4(axis.x, axis.y, axis.z, (float)f.StarLong));
        block.SetVector(TdeStarVelId, new Vector4(vel.x, vel.y, vel.z, (float)f.StarShort));
        block.SetVector(TdeStarLookId, new Vector4((float)f.StarTemperature, (float)f.StarTail, (float)p.StarGlow, 0f));
        for (int i = 0; i < f.VertexCount; i++)
        {
            Vector3 pos = World(f.Position[i]), v = World(f.Velocity[i]);
            _tdeVerts[i] = new Vector4(pos.x, pos.y, pos.z, (float)f.WidthA[i]);
            _tdeVertLook[i] = new Vector4((float)f.Material[i], (float)f.Opacity[i], (float)f.Temperature[i], (float)f.WidthZ[i]);
            // A negative brightness marks a break: no segment joins this vertex to the one before.
            _tdeVertVel[i] = new Vector4(v.x, v.y, v.z, f.Break[i] ? -(float)f.Brightness[i] - 0.01f : (float)f.Brightness[i]);
        }
        for (int c = 0; c < f.ChunkCount; c++)
        {
            Vector3 cc = World(f.ChunkCenter[c]);
            _tdeChunks[c] = new Vector4(cc.x, cc.y, cc.z, (float)f.ChunkRadius[c]);
        }
        block.SetVectorArray(TdeVertsId, _tdeVerts);
        block.SetVectorArray(TdeVertLookId, _tdeVertLook);
        block.SetVectorArray(TdeVertVelId, _tdeVertVel);
        block.SetVectorArray(TdeChunksId, _tdeChunks);
        Vector3 shock = World(f.ShockPosition);
        block.SetVector(TdeShockId, new Vector4(shock.x, shock.y, shock.z, f.ShockVisible ? Mathf.Max((float)f.ShockLuminosity, 1e-6f) : 0f));
        Vector3 spray = World(f.ShockDirection);
        block.SetVector(TdeShockLookId, new Vector4(spray.x, spray.y, spray.z, (float)f.ShockRadius));
        block.SetVector(TdeEnvId, new Vector4((float)f.EnvelopeInner, (float)f.EnvelopeLogSpan, (float)f.EnvelopeFunnel, f.EnvelopeVisible ? 1f : 0f));
        for (int i = 0; i < TdeFrame.EnvelopeBins; i++)
        {
            _tdeEnvK[i] = (float)(f.EnvelopeOpacity[i] * p.EnvelopeOpacity);
            _tdeEnvT[i] = (float)f.EnvelopeTemperature[i];
        }
        block.SetFloatArray(TdeEnvKId, _tdeEnvK);
        block.SetFloatArray(TdeEnvTId, _tdeEnvT);
        block.SetVector(TdeLightId, new Vector4((float)f.Luminosity, (float)p.StreamGlow, (float)f.ClumpGrowth, (float)p.LineVisibility));
        Vector3 flash = World(f.FlashPosition);
        block.SetVector(TdeFlashId, new Vector4(flash.x, flash.y, flash.z, f.FlashVisible ? (float)f.FlashStrength : 0f));
        block.SetVector(TdeFlashLookId, new Vector4((float)f.FlashRadius, 0f, 0f, 0f));
    }

    /// <summary>
    /// Screen rectangle (NDC, y up) covering a sphere at <paramref name="center"/> (relative to the camera), false when
    /// it is entirely off screen. Rays that miss the sphere are not bent, so nothing outside it needs drawing.
    /// </summary>
    private static bool ScreenRect(Vector3 center, float radius, Transform view, float tanX, float tanY, Vector2 margin, out Vector4 rect)
    {
        rect = new Vector4(-1f, -1f, 1f, 1f);
        if (center.sqrMagnitude <= radius * radius * 1.0201f) return true; // camera inside the sphere: the whole screen
        float cz = Vector3.Dot(center, view.forward);
        if (!AxisRange(Vector3.Dot(center, view.right), cz, radius, tanX, out float x0, out float x1)) return false;
        if (!AxisRange(Vector3.Dot(center, view.up), cz, radius, tanY, out float y0, out float y1)) return false;
        rect = new Vector4(Mathf.Max(-1f, x0 - margin.x), Mathf.Max(-1f, y0 - margin.y), Mathf.Min(1f, x1 + margin.x), Mathf.Min(1f, y1 + margin.y));
        return true;
    }

    /// <summary>
    /// Screen extent along one axis of a sphere whose centre is at <paramref name="c"/> across and <paramref name="z"/>
    /// ahead: the planes through the eye that contain the other screen axis and touch the sphere.
    /// </summary>
    private static bool AxisRange(float c, float z, float radius, float tan, out float min, out float max)
    {
        min = -1f;
        max = 1f;
        float d = Mathf.Sqrt(c * c + z * z);
        if (d <= radius) return true;
        float mid = Mathf.Atan2(c, z), half = Mathf.Asin(radius / d);
        float lo = mid - half, hi = mid + half;
        const float quarter = Mathf.PI * 0.5f;
        if (lo >= quarter || hi <= -quarter) return false; // entirely behind the eye
        if (lo > -quarter) min = Mathf.Max(-1f, Mathf.Tan(lo) / tan);
        if (hi < quarter) max = Mathf.Min(1f, Mathf.Tan(hi) / tan);
        return min < max;
    }

    /// <summary>
    /// Fills <see cref="_occluders"/> with the bodies whose scaled spheres could hide part of the hole's bounding sphere,
    /// nearest first, as the shader's solid spheres (centre relative to the eye and surface radius, in horizon radii);
    /// returns how many there are. <paramref name="includeSelf"/>: the object's own body too (a star inside its disk).
    /// </summary>
    private int Occluders(CelestialBody hole, Vector3 holeCenter, float holeRadius, Vector3 eye, double inverseScale, float horizon, bool includeSelf)
    {
        int count = 0;
        float holeDistance = holeCenter.magnitude;
        float holeAngle = holeDistance > holeRadius ? Mathf.Asin(holeRadius / holeDistance) : Mathf.PI;
        List<CelestialBody> bodies = FlightGlobals.Bodies;
        for (int i = 0; i < bodies.Count; i++)
        {
            CelestialBody b = bodies[i];
            if (b == null || (b == hole && !includeSelf) || b.scaledBody == null) continue;
            Vector3 c = b.scaledBody.transform.position - eye;
            float r = (float)((b.Radius + (b.atmosphere ? b.atmosphereDepth : 0.0)) * inverseScale * OccluderMargin);
            float distance = c.magnitude;
            if (distance - r > holeDistance + holeRadius) continue; // entirely beyond the hole
            if (distance > r && Vector3.Angle(c, holeCenter) * Mathf.Deg2Rad >= holeAngle + Mathf.Asin(r / distance)) continue;

            // Keep the nearest few, sorted by distance.
            int at = count;
            while (at > 0 && _occluderDistance[at - 1] > distance) at--;
            if (at >= MaxOccluders) continue;
            for (int k = Math.Min(count, MaxOccluders - 1); k > at; k--)
            {
                _occluders[k] = _occluders[k - 1];
                _occluderDistance[k] = _occluderDistance[k - 1];
            }
            _occluders[at] = new Vector4(c.x / horizon, c.y / horizon, c.z / horizon, (float)(b.Radius * inverseScale) / horizon);
            _occluderDistance[at] = distance;
            if (count < MaxOccluders) count++;
        }
        for (int k = count; k < MaxOccluders; k++) _occluders[k] = Vector4.zero;
        return count;
    }

    /// <summary>Requests the camera's depth texture only while needed, and only ever removes a request it made itself.</summary>
    private void SetDepthTexture(bool on)
    {
        if (_camera == null) return;
        bool has = (_camera.depthTextureMode & DepthTextureMode.Depth) != 0;
        if (on && !has)
        {
            _camera.depthTextureMode |= DepthTextureMode.Depth;
            _depthAdded = true;
        }
        else if (!on && has && _depthAdded)
        {
            _camera.depthTextureMode &= ~DepthTextureMode.Depth;
            _depthAdded = false;
        }
    }

    private void OnPostRender()
    {
        Detach();
    }

    private void OnDisable()
    {
        Detach();
        SetDepthTexture(false);
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
