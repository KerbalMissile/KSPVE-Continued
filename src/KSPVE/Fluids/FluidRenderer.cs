using System;
using System.Collections.Generic;
using KSPVE.Core;
using KSPVE.Pure.Fluids;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Fluids;

/// <summary>
/// Draws the simulated atmospheres on the ScaledSpace camera (BrownDwarf.shader): a sphere mesh a couple of percent over each
/// body's scaled mesh, drawn before the transparents with the depth test so a planet in front hides it and its halos and
/// rings still composite over it. The shader finds the exact sphere in each pixel; the mesh only gives coverage and depth.
/// A body the camera is inside the shell of is left to the game's own mesh (a vessel in a gas giant's atmosphere sees it
/// through the local camera and KSP's own atmosphere, not from the scaled camera).
/// </summary>
[DisallowMultipleComponent]
internal sealed class FluidRenderer : MonoBehaviour
{
    private const string ShaderName = "Hidden/KSPVE/Fluids/BrownDwarf";
    private const float Shell = 1.02f;
    private const double MinPixelRadius = 1.5;

    private static readonly int CenterId = Shader.PropertyToID("_KSPVE_FldCenter");
    private static readonly int AxisXId = Shader.PropertyToID("_KSPVE_FldAxisX");
    private static readonly int AxisYId = Shader.PropertyToID("_KSPVE_FldAxisY");
    private static readonly int AxisZId = Shader.PropertyToID("_KSPVE_FldAxisZ");
    private static readonly int CamId = Shader.PropertyToID("_KSPVE_FldCam");
    private static readonly int DeckTId = Shader.PropertyToID("_KSPVE_FldDeckT");
    private static readonly int DeckSId = Shader.PropertyToID("_KSPVE_FldDeckS");
    private static readonly int TauId = Shader.PropertyToID("_KSPVE_FldTau");
    private static readonly int Refl0Id = Shader.PropertyToID("_KSPVE_FldRefl0");
    private static readonly int Refl1Id = Shader.PropertyToID("_KSPVE_FldRefl1");
    private static readonly int Refl2Id = Shader.PropertyToID("_KSPVE_FldRefl2");
    private static readonly int StarAId = Shader.PropertyToID("_KSPVE_FldStarA");
    private static readonly int StarBId = Shader.PropertyToID("_KSPVE_FldStarB");
    private static readonly int StarCId = Shader.PropertyToID("_KSPVE_FldStarC");
    private static readonly int FieldsId = Shader.PropertyToID("_KSPVE_FldFields");
    private static readonly int ViewId = Shader.PropertyToID("_KSPVE_FldView");
    private static readonly int EmisId = Shader.PropertyToID("_KSPVE_FldEmis");

    private readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
    private readonly Vector3[] _starDirs = new Vector3[3];
    private readonly float[] _starStrengths = new float[3];
    private Camera _camera;
    private Material _material;
    private Mesh _mesh;
    private CommandBuffer _buffer;
    private bool _attached, _failed;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        if (_failed || !FluidSettings.Enabled) return;
        try
        {
            if (_material == null)
            {
                _material = ShaderLibrary.CreateMaterial(ShaderName);
                if (_material == null)
                {
                    _failed = true;
                    Log.Error("The brown dwarf shader is missing from the shader bundle; fluid atmospheres are off.");
                    return;
                }
                _mesh = BuildSphere(48, 32);
            }
            if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Fluids" };
            _buffer.Clear();

            Transform view = _camera.transform;
            float tanY = Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            double inverseScale = ScaledSpace.InverseScaleFactor;
            double ut = Planetarium.GetUniversalTime();
            int drawn = 0;
            IReadOnlyList<FluidBody> bodies = FluidWorld.All;
            for (int i = 0; i < bodies.Count; i++)
            {
                FluidBody fb = bodies[i];
                if (fb.State != FluidState.Ready || fb.FieldTexture == null || fb.EmissionTexture == null || fb.Body == null || fb.Body.scaledBody == null) continue;
                if (Draw(fb, view, tanY, inverseScale, ut, drawn)) drawn++;
            }
            if (drawn == 0) return;
            if (!_attached)
            {
                _camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _buffer);
                _attached = true;
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            Detach();
            Log.Exception("Fluid atmosphere rendering failed; disabled", ex);
        }
    }

    private bool Draw(FluidBody fb, Transform view, float tanY, double inverseScale, double ut, int index)
    {
        CelestialBody body = fb.Body;
        AtmosphereSimulation sim = fb.Sim;
        if (sim == null) return false;
        Vector3 bodyPosition = body.scaledBody.transform.position;
        Vector3 center = bodyPosition - view.position;
        float radius = (float)(body.Radius * inverseScale);
        float distance = center.magnitude;
        if (distance <= radius * Shell * 1.02f) return false;
        float pixels = radius / distance / tanY * _camera.pixelHeight * 0.5f;
        if (pixels < MinPixelRadius) return false;

        FluidWorld.TurnedBasis(body, fb.RotationPeriod, ut, out Vector3 lon0, out Vector3 north, out Vector3 lon90);
        while (_blocks.Count <= index) _blocks.Add(new MaterialPropertyBlock());
        MaterialPropertyBlock block = _blocks[index];

        bool selfLuminous = Decks.IsSelfLuminous(fb.Profile.Kind);
        float discRadiance = (float)(FluidWorld.SunDiscRadiance * (selfLuminous ? FluidSettings.Boost : 1.0));
        block.SetVector(CenterId, new Vector4(center.x, center.y, center.z, radius));
        block.SetVector(AxisXId, lon0);
        block.SetVector(AxisYId, north);
        block.SetVector(AxisZId, lon90);
        block.SetVector(CamId, new Vector4(2f * tanY / Mathf.Max(_camera.pixelHeight, 1), discRadiance, 0.8f, Shell));
        block.SetVector(DeckTId, new Vector4((float)sim.DeckKelvin[0], (float)sim.DeckKelvin[1], (float)sim.DeckKelvin[2], (float)sim.DeepKelvin));
        block.SetVector(DeckSId, new Vector4((float)sim.Forcing.DeckShare[0], (float)sim.Forcing.DeckShare[1], (float)sim.Forcing.DeckShare[2], 0.6f));
        block.SetVector(TauId, new Vector4((float)sim.DeckSpecs[0].Tau, (float)sim.DeckSpecs[1].Tau, (float)sim.DeckSpecs[2].Tau, FluidSettings.View == FluidView.ThermalInfrared ? (float)EmissionLut.ThermalInfraredTauScale : 1f));
        block.SetVector(Refl0Id, Albedo(sim.DeckSpecs[0]));
        block.SetVector(Refl1Id, Albedo(sim.DeckSpecs[1]));
        block.SetVector(Refl2Id, Albedo(sim.DeckSpecs[2]));

        int stars = fb.Profile.Reflect ? FluidWorld.LightOf(body, _starDirs, _starStrengths) : 0;
        block.SetVector(StarAId, Star(0, stars));
        block.SetVector(StarBId, Star(1, stars));
        block.SetVector(StarCId, Star(2, stars));
        block.SetVector(ViewId, new Vector4(FluidSettings.View == FluidView.ThermalInfrared ? 1f : 0f, 0f, 0f, 0f));
        block.SetTexture(FieldsId, fb.FieldTexture);
        Texture emission = FluidSettings.View == FluidView.ThermalInfrared && fb.EmissionInfraredTexture != null ? fb.EmissionInfraredTexture : fb.EmissionTexture;
        block.SetTexture(EmisId, emission);

        float scale = 2f * radius * Shell;
        _buffer.DrawMesh(_mesh, Matrix4x4.TRS(bodyPosition, Quaternion.identity, new Vector3(scale, scale, scale)), _material, 0, 0, block);
        return true;
    }

    private static Vector4 Albedo(DeckSpec d) => new Vector4((float)(d.Albedo * d.ReflectR), (float)(d.Albedo * d.ReflectG), (float)(d.Albedo * d.ReflectB), 0f);

    private Vector4 Star(int i, int count) =>
        i < count ? new Vector4(_starDirs[i].x, _starDirs[i].y, _starDirs[i].z, _starStrengths[i]) : Vector4.zero;

    /// <summary>A UV sphere of radius 0.5 with its triangles wound for Unity's front faces.</summary>
    private static Mesh BuildSphere(int segments, int rings)
    {
        var vertices = new List<Vector3>();
        for (int r = 0; r <= rings; r++)
        {
            float v = r / (float)rings, phi = Mathf.PI * v;
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2f * Mathf.PI * s / segments;
                vertices.Add(0.5f * new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
            }
        }
        var triangles = new List<int>();
        int stride = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                AddOutward(triangles, vertices, a, c, b);
                AddOutward(triangles, vertices, b, c, d);
            }
        var mesh = new Mesh { name = "KSPVE fluid shell" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static void AddOutward(List<int> triangles, List<Vector3> v, int a, int b, int c)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (n.sqrMagnitude < 1e-12f) return;   // a degenerate triangle at a pole
        Vector3 centroid = (v[a] + v[b] + v[c]) / 3f;
        if (Vector3.Dot(n, centroid) >= 0f) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        else { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
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
        if (_mesh != null) Destroy(_mesh);
    }

    private void Detach()
    {
        if (!_attached || _camera == null) return;
        _camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _buffer);
        _attached = false;
    }
}
