using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Tde;

public enum TdePhase
{
    /// <summary>The star falls in, stretched by tides.</summary>
    Approach,
    /// <summary>Torn apart; the debris spreads into a stream.</summary>
    Disruption,
    /// <summary>Half the debris flies off, the bound half swings out to apoapsis.</summary>
    Stream,
    /// <summary>The bound debris is back: it crosses itself, shocks, and feeds a new disk and an outflow.</summary>
    Flare,
    /// <summary>The fallback dwindles (as t^-5/3).</summary>
    Fading,
    /// <summary>Nothing left worth drawing.</summary>
    Over
}

/// <summary>
/// One moment of a tidal disruption event, ready to draw. Positions are in horizon radii, relative to the hole, in the
/// hole's disk frame (x, y in the disk plane, z along its normal). Velocities are fractions of c, as a static observer
/// measures them. Temperatures in kelvin. Optical depths per horizon radius.
/// </summary>
public sealed class TdeFrame
{
    public const int MaxVertices = 128;
    public const int ChunkSegments = 8;
    public const int MaxChunks = 16;
    public const int EnvelopeBins = 32;

    public bool Active;
    public TdePhase Phase;
    /// <summary>Physical seconds since the star was torn apart (negative before).</summary>
    public double SecondsSinceDisruption;
    /// <summary>Radius (horizon radii) within which everything drawn lies.</summary>
    public double Extent;

    // The intact star (before the disruption), or what survives a partial disruption: an ellipsoid.
    public bool StarVisible;
    public Double3 StarCenter, StarAxis, StarVelocity;
    public double StarLong, StarShort, StarTemperature;
    /// <summary>0..1: fades out as the stream takes over, just after the disruption.</summary>
    public double StarPresence;
    /// <summary>0..1: gas pouring off the star's tips (through L1 and L2) as it nears the tidal radius.</summary>
    public double StarTail;

    // The debris stream: a tube through the vertices, elliptical in cross-section (WidthA in the orbit plane, WidthZ
    // across it), optically thick, glowing at its own temperature.
    public int VertexCount;
    public readonly Double3[] Position = new Double3[MaxVertices];
    public readonly Double3[] Velocity = new Double3[MaxVertices];
    public readonly double[] WidthA = new double[MaxVertices];
    public readonly double[] WidthZ = new double[MaxVertices];
    /// <summary>Material coordinate in [-1, 1] (most bound to most unbound): clumps ride with the gas.</summary>
    public readonly double[] Material = new double[MaxVertices];
    public readonly double[] Opacity = new double[MaxVertices];
    public readonly double[] Temperature = new double[MaxVertices];
    /// <summary>0..1: fades at the ends.</summary>
    public readonly double[] Brightness = new double[MaxVertices];
    /// <summary>No segment joins this vertex to the one before: the stream runs out past the render radius between them.</summary>
    public readonly bool[] Break = new bool[MaxVertices];
    public int ChunkCount;
    public readonly Double3[] ChunkCenter = new Double3[MaxChunks];
    public readonly double[] ChunkRadius = new double[MaxChunks];
    /// <summary>Normal of the star's orbit: the stream's "vertical" and the new disk's axis.</summary>
    public Double3 OrbitNormal;
    /// <summary>0..1: how far the stream has fragmented into clumps.</summary>
    public double ClumpGrowth;

    // The flash as the star is crushed flat at periapsis in a deep encounter (shock breakout): a brief white-hot burst
    // riding with the debris.
    public bool FlashVisible;
    public Double3 FlashPosition;
    public double FlashRadius, FlashStrength;

    // Where the returning stream crosses itself.
    public bool ShockVisible;
    public Double3 ShockPosition;
    /// <summary>Which way the shocked gas sprays: along the returning stream's motion where it hits.</summary>
    public Double3 ShockDirection;
    public double ShockRadius, ShockTemperature, ShockLuminosity; // luminosity in Eddington units

    // The hole's own disk, as the debris builds it (for holes with diskFromTde; see DiskState).
    /// <summary>0..1: how much of the disk has formed.</summary>
    public double DiskFormation;
    /// <summary>Where the gas first settles: the circularisation radius (horizon radii).</summary>
    public double DiskFormRadius;
    /// <summary>Brightening by the flare (1 = as configured).</summary>
    public double DiskGlow = 1.0;
    /// <summary>Thickening while it is fed above Eddington (1 = as configured).</summary>
    public double DiskPuff = 1.0;
    /// <summary>r_s / c since the jets were launched (negative before).</summary>
    public double JetAge = -1.0;
    /// <summary>Physical temperature of the disk's inner edge (K), for the readout.</summary>
    public double DiskInnerTemperature;

    // The outflow launched from the self-intersection: optically thick, it reprocesses the flare into a blue glow.
    public bool EnvelopeVisible;
    public double EnvelopeInner, EnvelopeLogSpan, EnvelopeFunnel;
    public readonly double[] EnvelopeOpacity = new double[EnvelopeBins];
    public readonly double[] EnvelopeTemperature = new double[EnvelopeBins];

    /// <summary>Luminosity of the event in Eddington units (what lights the stream).</summary>
    public double Luminosity;

    // Diagnostics in physical units.
    public double LuminosityWatts, FallbackEddington, AccretionEddington, PhotosphereMeters, PhotosphereTemperature;
    public double AccretedFraction, EjectedFraction;

    public void Clear()
    {
        Active = false;
        StarVisible = ShockVisible = EnvelopeVisible = FlashVisible = false;
        FlashStrength = 0.0;
        DiskFormation = DiskFormRadius = DiskInnerTemperature = 0.0;
        DiskGlow = DiskPuff = 1.0;
        JetAge = -1.0;
        VertexCount = ChunkCount = 0;
        Extent = 0.0;
        Luminosity = LuminosityWatts = FallbackEddington = AccretionEddington = 0.0;
        PhotosphereMeters = PhotosphereTemperature = AccretedFraction = EjectedFraction = 0.0;
        ClumpGrowth = 0.0;
    }
}
