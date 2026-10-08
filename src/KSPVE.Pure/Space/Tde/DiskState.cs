using System;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// A black hole's disk and jets as they are right now. Most holes simply have the disk their KSPVE_BLACKHOLE node
/// configures. A hole with diskFromTde has none until a tidal disruption builds it: the returning debris shocks and
/// settles into a narrow ring at the circularisation radius, which spreads inward to the configured inner edge and
/// outward to the outer edge as more gas comes back, puffed up and brightened while the flare lasts. It is drawn as
/// the configured disk seen only where the gas has reached, so the young ring has the faint, wispy look of the disk's
/// outer parts and the disk brightens as the gas works its way in. The jets launch once the disk is half built, their
/// heads running out at half the speed of light.
/// </summary>
public readonly struct DiskState
{
    /// <summary>0..1: how much disk there is (scales its density, glow, sparks and drag).</summary>
    public readonly double Presence;
    /// <summary>Where the gas is now (horizon radii).</summary>
    public readonly double Inner, Outer;
    public readonly double Thickness;
    /// <summary>The configured disk the gas is drawn as.</summary>
    public readonly double ShapeInner, ShapeOuter;
    /// <summary>Soft-edged band the gas fills so far, and the width of its edges (0 = the whole disk).</summary>
    public readonly double MaskInner, MaskOuter, MaskSoftness;
    /// <summary>Emission multiplier (1 = as configured).</summary>
    public readonly double Brightness;
    public readonly double Jets, JetLength;
    /// <summary>0..1: spiral shocks swirling through a disk still forming.</summary>
    public readonly double Spiral;
    /// <summary>Glow of the jet heads as they punch out, and a flash at the hole when they launch.</summary>
    public readonly double JetHeads, LaunchFlash;

    public DiskState(double presence, double inner, double outer, double thickness, double brightness, double jets, double jetLength,
        double spiral = 0.0, double jetHeads = 0.0, double launchFlash = 0.0,
        double shapeInner = double.NaN, double shapeOuter = double.NaN, double maskInner = 0.0, double maskOuter = 0.0, double maskSoftness = 0.0)
    {
        Presence = presence;
        Inner = inner;
        Outer = outer;
        Thickness = thickness;
        ShapeInner = double.IsNaN(shapeInner) ? inner : shapeInner;
        ShapeOuter = double.IsNaN(shapeOuter) ? outer : shapeOuter;
        MaskInner = maskInner;
        MaskOuter = maskOuter;
        MaskSoftness = maskSoftness;
        Brightness = brightness;
        Jets = jets;
        JetLength = jetLength;
        Spiral = spiral;
        JetHeads = jetHeads;
        LaunchFlash = launchFlash;
    }

    /// <summary>Jet heads advance at this fraction of c.</summary>
    public const double JetHeadSpeed = 0.5;

    /// <summary>Half-width of the first ring, as a fraction of its radius.</summary>
    public const double RingHalfWidth = 0.08;

    public static DiskState Configured(double inner, double outer, double thickness, double jets, double jetLength) =>
        new DiskState(1.0, inner, outer, thickness, 1.0, jets, jetLength);

    /// <summary>The disk a tidal disruption has built so far (none at all when there is no event).</summary>
    public static DiskState Forming(TdeFrame frame, double inner, double outer, double thickness, double jets, double jetLength)
    {
        if (frame == null || !frame.Active) return new DiskState(0.0, inner, outer, thickness, 1.0, 0.0, jetLength);
        double f = Clamp(frame.DiskFormation, 0.0, 1.0);
        double ring = Clamp(frame.DiskFormRadius > 0.0 ? frame.DiskFormRadius : 0.5 * (inner + outer), inner * 1.2, outer * 0.9);
        double half = RingHalfWidth * ring;
        double soft = 0.5 * half;
        // The band's edges run from the ring to just past the configured disk's own edges, where the mask stops mattering.
        double spread = SmoothStep(0.0, 1.0, f);
        double inEnd = 0.8 * inner - soft, outEnd = 1.1 * outer + soft;
        double maskIn = ring - half + (inEnd - (ring - half)) * spread;
        double maskOut = ring + half + (outEnd - (ring + half)) * spread;
        double reach = frame.JetAge > 0.0 ? Clamp(JetHeadSpeed * frame.JetAge / Math.Max(jetLength, 1.0), 0.0, 1.0) : 0.0;
        double heads = reach > 0.0 ? 1.0 - SmoothStep(0.85, 1.0, reach) : 0.0;
        double flash = frame.JetAge > 0.0 ? Math.Exp(-frame.JetAge / 40.0) : 0.0;
        return new DiskState(SmoothStep(0.0, 0.5, f), Math.Max(maskIn, inner), Math.Min(maskOut, outer), thickness * frame.DiskPuff,
            frame.DiskGlow, jets * SmoothStep(0.0, 0.15, reach), Math.Max(jetLength * reach, 5.0), (1.0 - f) * SmoothStep(0.0, 0.1, f),
            heads, flash, inner, outer, maskIn, maskOut, soft);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    private static double SmoothStep(double e0, double e1, double x)
    {
        double t = Clamp((x - e0) / (e1 - e0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }
}
