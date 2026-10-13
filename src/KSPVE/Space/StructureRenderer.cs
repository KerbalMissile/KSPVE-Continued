using System;
using System.Runtime.InteropServices;
using KSPVE.Core;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Particles;
using UnityEngine;
using UnityEngine.Rendering;

namespace KSPVE.Space;

/// <summary>
/// Draws the particle structures (<see cref="StructureWorld"/>) on Camera ScaledSpace, where planets and stars are: one
/// billboard per particle by Structures.shader, read from a structured buffer filled back to front each frame so thick
/// clouds hide what is behind them. Like the black holes, in scaled space: positions are metres times
/// <see cref="ScaledSpace.InverseScaleFactor"/> about the body's scaled model.
/// </summary>
internal sealed class StructureRenderer : MonoBehaviour
{
    private const string ShaderName = "Hidden/KSPVE/Space/Structures";
    private const int Stride = 12 * sizeof(float);

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuParticle
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Reflect;
        public float Soft;
        public Vector3 Emit;
        public float Alpha;
    }

    private static readonly int ParticlesId = Shader.PropertyToID("_KSPVE_St_Particles");
    private static readonly int CenterId = Shader.PropertyToID("_KSPVE_St_Center");
    private static readonly int SunId = Shader.PropertyToID("_KSPVE_St_Sun");
    private static readonly int StarColorId = Shader.PropertyToID("_KSPVE_St_StarColor");
    private static readonly int LookId = Shader.PropertyToID("_KSPVE_St_Look");

    private sealed class Slot
    {
        public ComputeBuffer Buffer;
        public GpuParticle[] Data = new GpuParticle[0];
        public float[] Keys = new float[0];
        public int[] Order = new int[0];
        public readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
    }

    private readonly System.Collections.Generic.Dictionary<StructureInstance, Slot> _slots = new System.Collections.Generic.Dictionary<StructureInstance, Slot>();
    private Camera _camera;
    private Material _material;
    private CommandBuffer _buffer;
    private bool _attached;
    private bool _failed;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnPreRender()
    {
        if (_failed) return;
        Detach();
        if (!StructureWorld.Enabled || StructureWorld.Instances.Count == 0 || FlightGlobals.fetch == null) return;
        try
        {
            if (_material == null)
            {
                _material = ShaderLibrary.CreateMaterial(ShaderName);
                if (_material == null)
                {
                    _failed = true;
                    Log.Error("Structures shader missing from the shader bundle; particle structures are off.");
                    return;
                }
            }
            if (_buffer == null) _buffer = new CommandBuffer { name = "KSPVE Structures" };
            _buffer.Clear();

            Transform view = _camera.transform;
            float pixelAngle = 2f * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(_camera.pixelHeight, 1);
            double inverseScale = ScaledSpace.InverseScaleFactor;
            int drawn = 0;
            foreach (StructureInstance instance in StructureWorld.Instances)
            {
                if (!instance.Config.Enabled || instance.Set.Count == 0 || instance.Context == null) continue;
                CelestialBody body = instance.Body;
                if (body == null || body.scaledBody == null) continue;
                if (Draw(instance, body, view, pixelAngle, inverseScale)) drawn++;
            }
            if (drawn == 0) return;
            _camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
            _attached = true;
        }
        catch (Exception ex)
        {
            _failed = true;
            Detach();
            Log.Exception("Structure rendering failed; disabled", ex);
        }
    }

    private bool Draw(StructureInstance instance, CelestialBody body, Transform view, float pixelAngle, double inverseScale)
    {
        StructureConfig config = instance.Config;
        ParticleSet set = instance.Set;
        int count = set.Count;
        if (!_slots.TryGetValue(instance, out Slot slot)) _slots[instance] = slot = new Slot();
        if (slot.Data.Length < count)
        {
            int capacity = Math.Max(count, slot.Data.Length * 2);
            slot.Data = new GpuParticle[capacity];
            slot.Keys = new float[capacity];
            slot.Order = new int[capacity];
            slot.Buffer?.Release();
            slot.Buffer = new ComputeBuffer(capacity, Stride);
        }

        // Far to near, so what is in front is drawn over what is behind.
        Vector3 center = body.scaledBody.transform.position - view.position;
        Vector3 forward = view.forward;
        float scale = (float)inverseScale;
        for (int i = 0; i < count; i++)
        {
            Double3 p = set.Items[i].Position;
            slot.Keys[i] = -(float)((center.x + p.X * scale) * forward.x + (center.y + p.Y * scale) * forward.y + (center.z + p.Z * scale) * forward.z);
            slot.Order[i] = i;
        }
        Array.Sort(slot.Keys, slot.Order, 0, count);

        for (int k = 0; k < count; k++)
        {
            StructureParticle p = set.Items[slot.Order[k]];
            slot.Data[k] = new GpuParticle
            {
                Position = new Vector3((float)(p.Position.X * inverseScale), (float)(p.Position.Y * inverseScale), (float)(p.Position.Z * inverseScale)),
                Radius = (float)(p.Radius * inverseScale),
                Reflect = new Vector3((float)p.Reflect.X, (float)p.Reflect.Y, (float)p.Reflect.Z),
                Soft = p.Softness,
                Emit = new Vector3((float)p.Emit.X, (float)p.Emit.Y, (float)p.Emit.Z),
                Alpha = (float)p.Opacity
            };
        }
        slot.Buffer.SetData(slot.Data, 0, 0, count);

        Double3 sun = instance.Context.ToStar;
        Double3 star = StructureLight.Blackbody(config.N("starTemperature"));
        double brightness = config.N("brightness") * config.N("visibilityBoost");
        slot.Block.SetBuffer(ParticlesId, slot.Buffer);
        slot.Block.SetVector(CenterId, new Vector4(center.x, center.y, center.z, 0f));
        slot.Block.SetVector(SunId, new Vector4((float)sun.X, (float)sun.Y, (float)sun.Z, (float)config.N("scatterAsymmetry")));
        slot.Block.SetVector(StarColorId, new Vector4((float)star.X, (float)star.Y, (float)star.Z, (float)brightness));
        slot.Block.SetVector(LookId, new Vector4(pixelAngle, (float)config.N("minPixels"), count, 0f));
        _buffer.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, count * 6, 1, slot.Block);
        return true;
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
        foreach (Slot s in _slots.Values) s.Buffer?.Release();
        _slots.Clear();
        if (_material != null) Destroy(_material);
    }

    private void Detach()
    {
        if (!_attached || _camera == null) return;
        _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
        _attached = false;
    }
}
