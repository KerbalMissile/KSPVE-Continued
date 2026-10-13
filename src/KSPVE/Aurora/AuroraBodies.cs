using System;
using System.Collections.Generic;
using KSPVE.Clouds;
using KSPVE.Core;
using KSPVE.Pure.Aurora;
using KSPVE.Pure.ColorScience;
using KSPVE.Pure.Config;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.SpaceWeather;
using UnityEngine;

namespace KSPVE.Aurora;

/// <summary>
/// One body's aurora and airglow as the game holds them: its model (from KSPVE_AURORA, or a guess), its atmosphere's pressure
/// table read from KSP, the table of emission by height (which the shader reads as a texture), the extinction of the air
/// below, and the frame of the moment (solved from the solar wind and the clock, once a frame at most).
/// </summary>
internal sealed class AuroraBody
{
    public const int LutSamples = 256;

    public CelestialBody Body;
    public AuroraBodyModel Model;
    public AtmosphereProfile Atmosphere;
    public EmissionLut Lut;
    public Texture2D LutTexture;
    public Rgb Tau0;
    public double ScaleHeight;
    /// <summary>The height (m) above which a camera is drawn by the scaled-space camera: the top of the glowing layers.</summary>
    public double LocalLimit;
    public AuroraFrame Frame;
    public CelestialBody Star;
    public CelestialBody[] Moons = new CelestialBody[0];
    /// <summary>The slabs the shader marches (low and high heights, m): the arcs' (with STEVE's), the smooth kinds', the SAR arcs', and each airglow layer's.</summary>
    public Vector2 SlabArc, SlabSmooth, SlabSar;
    public Vector4 GlowLow, GlowHigh;

    // Cached between frames.
    private int _solvedFrame = -1;
    private double _kpUt = double.NegativeInfinity, _kp;
    private Vector3d _x, _y, _z;
    private readonly List<AuroraSolver.MoonPosition> _moonPositions = new List<AuroraSolver.MoonPosition>();

    /// <summary>The body's turning frame as world axes (x lat 0 lon 0, y north, z lon 90 E), valid for the frame last solved.</summary>
    public void Basis(out Vector3d x, out Vector3d y, out Vector3d z) { x = _x; y = _y; z = _z; }

    /// <summary>Everything needed to draw it, or null when the body has no atmosphere to glow.</summary>
    public static AuroraBody Create(CelestialBody body, AuroraBodyModel model)
    {
        if (body == null || !body.atmosphere || model == null || (!model.Aurora && !model.Airglow)) return null;
        var a = new AuroraBody { Body = body, Model = model };
        // The pressure table: KSP's curve up to the top of the atmosphere (kPa to Pa), extended above by the profile.
        double depth = body.atmosphereDepth > 1000.0 ? body.atmosphereDepth : 70000.0;
        var heights = new List<double>();
        var pressures = new List<double>();
        const int n = 96;
        for (int i = 0; i < n; i++)
        {
            double h = depth * Math.Pow(i / (double)(n - 1), 1.4);
            double p = body.GetPressure(h) * 1000.0;
            heights.Add(h);
            pressures.Add(p);
        }
        double gravity = Math.Max(body.GeeASL, 0.01) * 9.80665;
        a.Atmosphere = new AtmosphereProfile(heights, pressures, body.Radius, gravity, body.atmosphereMolarMass > 0 ? body.atmosphereMolarMass : 0.029,
            model.ExosphereTemperature, model.MesosphereTemperature);
        a.Lut = EmissionLut.Build(a.Atmosphere, model.Composition, LutSamples);
        a.LutTexture = MakeTexture(a.Lut);
        a.ScaleHeight = Math.Max(a.Atmosphere.ScaleHeightAt(0.0), 500.0);
        a.Tau0 = RayleighDepth(a.Atmosphere, model.Composition, body.GetPressure(0.0) * 1000.0);
        double top = 0.0;
        foreach (EmissionRow r in new[] { EmissionRow.Arc, EmissionRow.ArcHard, EmissionRow.SoftArc, EmissionRow.Steve, EmissionRow.Diffuse, EmissionRow.GlowRed })
            if (!double.IsNaN(a.Lut.UpperEdge[(int)r])) top = Math.Max(top, a.Lut.UpperEdge[(int)r]);
        a.LocalLimit = Math.Max(depth, top);
        a.SlabArc = Slab(a.Lut, EmissionRow.Arc, EmissionRow.ArcHard, EmissionRow.SoftArc, EmissionRow.Steve);
        a.SlabSmooth = Slab(a.Lut, EmissionRow.Diffuse, EmissionRow.Proton, EmissionRow.Global, EmissionRow.Giant);
        a.SlabSar = Slab(a.Lut, EmissionRow.Sar);
        var glowLow = new float[4];
        var glowHigh = new float[4];
        for (int i = 0; i < 4; i++)
        {
            int row = (int)EmissionRow.GlowGreen + i;
            if (double.IsNaN(a.Lut.LowerEdge[row])) continue;
            glowLow[i] = (float)a.Lut.LowerEdge[row]; glowHigh[i] = (float)a.Lut.UpperEdge[row];
        }
        a.GlowLow = new Vector4(glowLow[0], glowLow[1], glowLow[2], glowLow[3]);
        a.GlowHigh = new Vector4(glowHigh[0], glowHigh[1], glowHigh[2], glowHigh[3]);
        a.Star = CloudWorld.StarOf(body);
        var moons = new List<CelestialBody>();
        foreach (FootprintMoon m in model.Moons)
        {
            CelestialBody found = FlightGlobals.Bodies?.Find(b => b != null && string.Equals(b.bodyName, m.Name, StringComparison.OrdinalIgnoreCase));
            if (found != null) moons.Add(found);
        }
        a.Moons = moons.ToArray();
        return a;
    }

    /// <summary>
    /// The Rayleigh optical depth of the air straight up from the surface at red, green and blue: tau = sigma N, with N the
    /// column of molecules, p0 / (m g), and sigma the scattering cross-section of a molecule, 5.1e-31 m^2 at 550 nm for
    /// air and scaling as the fourth power of the wavelength (its relative polarisability for other gases).
    /// </summary>
    private static Rgb RayleighDepth(AtmosphereProfile atmosphere, Composition composition, double surfacePressure)
    {
        const double Avogadro = 6.02214076e23;
        double mass = atmosphere.MolarMass / Avogadro;
        double column = surfacePressure / (mass * atmosphere.SurfaceGravity);
        double relative = composition == Composition.CO2 ? 2.9 : composition == Composition.H2He ? 0.3 : composition == Composition.N2CH4 ? 1.4 : 1.0;
        double Tau(double nm) => 5.1e-31 * relative * Math.Pow(550.0 / nm, 4.0) * column;
        return new Rgb(Tau(640.0), Tau(550.0), Tau(460.0));
    }

    private static Vector2 Slab(EmissionLut lut, params EmissionRow[] rows)
    {
        double low = double.MaxValue, high = 0.0;
        foreach (EmissionRow r in rows)
        {
            double a = lut.LowerEdge[(int)r], b = lut.UpperEdge[(int)r];
            if (double.IsNaN(a) || double.IsNaN(b)) continue;
            low = Math.Min(low, a); high = Math.Max(high, b);
        }
        return high > low ? new Vector2((float)low, (float)high) : Vector2.zero;
    }

    private static Texture2D MakeTexture(EmissionLut lut)
    {
        var tex = new Texture2D(lut.Samples, EmissionLut.Rows, TextureFormat.RGBAHalf, false, true)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "KSPVE Aurora emission", hideFlags = HideFlags.HideAndDontSave,
        };
        var pixels = new Color[lut.Samples * EmissionLut.Rows];
        for (int row = 0; row < EmissionLut.Rows; row++)
            for (int i = 0; i < lut.Samples; i++)
                pixels[row * lut.Samples + i] = new Color(lut.Rgb[row, i, 0] * 1e12f, lut.Rgb[row, i, 1] * 1e12f, lut.Rgb[row, i, 2] * 1e12f, 0f);   // stored times 1e12 (a half float holds values of 0.05 to 1)
        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

    /// <summary>Solves the frame for this game frame (at most once per frame), from the clock, the star and the solar wind.</summary>
    public AuroraFrame Solve()
    {
        if (_solvedFrame == Time.frameCount && Frame != null) return Frame;
        _solvedFrame = Time.frameCount;
        double ut = Planetarium.fetch != null ? Planetarium.GetUniversalTime() : 0.0;
        CloudWorld.Basis(Body, out _x, out _y, out _z);
        Vector3d toStar = Star != null && Star != Body ? Star.position - Body.position : new Vector3d(1, 0, 0) * 1e11;
        Double3 sun = CloudWorld.DirectionToBody(Body, toStar.normalized);
        // The wind is asked about in the inertial frame (y the star's north), not the rotated world.
        Vector3d fromStar = QuaternionD.Inverse(Planetarium.Rotation) * -toStar;
        var position = new Double3(fromStar.x, fromStar.y, fromStar.z);
        string starName = Star != null ? Star.bodyName : string.Empty;
        _moonPositions.Clear();
        foreach (CelestialBody m in Moons)
            _moonPositions.Add(new AuroraSolver.MoonPosition { Name = m.bodyName, Position = CloudWorld.ToBody(Body, m.position) });

        // Kp is a slow quantity; the solar wind it comes from is only asked about every half minute of game time.
        double? kpOverride = null;
        if (Model.Oval == OvalMode.Solar && Math.Abs(ut - _kpUt) < 30.0) kpOverride = _kp;
        Frame = AuroraSolver.Solve(Model, Body.Radius, t => SpaceWeather.At(starName, position, t), ut, sun, _moonPositions, kpOverride);
        if (Model.Oval == OvalMode.Solar && !kpOverride.HasValue) { _kp = Frame.Oval.Kp; _kpUt = ut; }
        return Frame;
    }
}

/// <summary>The bodies that can glow: their models, and the runtime data built for each when first needed.</summary>
internal static class AuroraBodies
{
    private static readonly Dictionary<string, AuroraBodyModel> Models = new Dictionary<string, AuroraBodyModel>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<CelestialBody, AuroraBody> Runtime = new Dictionary<CelestialBody, AuroraBody>();
    private static readonly HashSet<CelestialBody> Failed = new HashSet<CelestialBody>();

    /// <summary>Reads the BODY nodes of a KSPVE_AURORA node (later nodes of the same name win by overlay).</summary>
    public static void Load(CfgNode root, List<ConfigIssue> issues)
    {
        Models.Clear();
        foreach (CfgNode child in root.Children)
        {
            if (!child.Name.Equals("BODY", StringComparison.OrdinalIgnoreCase)) continue;
            string name = child.GetLast("name");
            if (string.IsNullOrEmpty(name)) continue;
            Models[name] = AuroraBodyModel.FromConfig(child, issues);
        }
        Reset();
    }

    public static void Reset()
    {
        foreach (AuroraBody b in Runtime.Values) if (b.LutTexture != null) UnityEngine.Object.Destroy(b.LutTexture);
        Runtime.Clear();
        Failed.Clear();
    }

    /// <summary>The model for a body: the config's, or a guess from what the game says about it.</summary>
    public static AuroraBodyModel ModelFor(CelestialBody body)
    {
        if (Models.TryGetValue(body.bodyName, out AuroraBodyModel m)) return m;
        return AuroraBodyModel.Guess(new AuroraBodyModel.Facts
        {
            Name = body.bodyName, Radius = body.Radius, MolarMass = body.atmosphereMolarMass, SurfacePressure = body.atmosphere ? body.GetPressure(0.0) * 1000.0 : 0.0,
            SurfaceGravity = body.GeeASL * 9.80665, RotationPeriod = body.rotationPeriod, HasAtmosphere = body.atmosphere, ContainsOxygen = body.atmosphereContainsOxygen,
            IsStar = body.isStar,
        });
    }

    /// <summary>The body's runtime data, built on first use; null for a body that cannot glow.</summary>
    public static AuroraBody Of(CelestialBody body)
    {
        if (body == null || Failed.Contains(body)) return null;
        if (Runtime.TryGetValue(body, out AuroraBody a)) return a;
        try
        {
            a = AuroraBody.Create(body, ModelFor(body));
        }
        catch (Exception ex)
        {
            Log.Exception("Building the aurora of " + body.bodyName + " failed", ex);
            a = null;
        }
        if (a == null) { Failed.Add(body); return null; }
        Runtime[body] = a;
        Log.Info($"Aurora: {body.bodyName} ({a.Model.Source}): {(a.Model.HasField ? "dipole " + (a.Model.Field.EquatorialField * 1e6).ToString("0.0") + " uT" : "no global field")}, {a.Model.Composition}, " +
            $"arcs at {a.Lut.PeakHeight[(int)EmissionRow.Arc] / 1000.0:0} km, airglow at {a.Lut.PeakHeight[(int)EmissionRow.GlowGreen] / 1000.0:0} km");
        return a;
    }

    public static IEnumerable<AuroraBodyModel> ConfiguredModels => Models.Values;
}
