using System.Collections.Generic;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Stars;

/// <summary>
/// A group of sunspots with the faculae round it, at an instant: a bipole whose leading spot (ahead in the star's
/// rotation) is the bigger, closer to the equator by Joy's-law tilt. Directions are unit vectors in the model frame (y is
/// the star's north); lengths are in stellar radii.
/// </summary>
public struct SpotGroup
{
    public Double3 Dir;
    /// <summary>Unit tangent from the following spot to the leading one.</summary>
    public Double3 Axis;
    /// <summary>Half the distance between the leading and following spots.</summary>
    public double HalfSeparation;
    /// <summary>The leading spot's umbra radius (the penumbra is 2.3 times it).</summary>
    public double UmbraRadius;
    /// <summary>Radius of the bright faculae round the group.</summary>
    public double FaculaRadius;
    /// <summary>0..1: how fully formed (young and dying groups are fainter and smaller).</summary>
    public double Strength;
    /// <summary>0..1: a flare's brightening at the footpoints now.</summary>
    public double Flare;
    public double Seed;
    /// <summary>0..1 through the group's life: young groups are a few pores, old ones fragment into many.</summary>
    public double Age;
    /// <summary>+1 or -1: the leading spot's magnetic polarity (Hale's law).</summary>
    public double Polarity;
    /// <summary>Peak area (millionths of a hemisphere), for the UI.</summary>
    public double AreaMsh;
}

public enum StructureKind
{
    /// <summary>Loops rising from two footpoints (an active region's loops, a post-flare arcade, an erupting prominence).</summary>
    Arch = 0,
    /// <summary>A filament: a sheet of cool dense gas standing on the surface along a line (a quiescent prominence at the limb, a dark filament on the disc).</summary>
    Filament = 1
}

/// <summary>A prominence or a loop system, in stellar radii and the model frame.</summary>
public struct StarStructure
{
    public StructureKind Kind;
    /// <summary>Arch: the footpoints' directions (unit). Filament: A is its centre's direction and B the unit tangent along it.</summary>
    public Double3 A, B;
    /// <summary>Arch: the apex's height above the surface. Filament: the gas's scale height.</summary>
    public double Height;
    /// <summary>Arch: the tube's half-thickness. Filament: the sheet's half-thickness.</summary>
    public double Width;
    /// <summary>Arch: the ribbon's half-width across the loop plane. Filament: half its length (rad).</summary>
    public double Ribbon;
    /// <summary>Arch: how many loops are nested (an arcade). Filament: unused.</summary>
    public double Nest;
    /// <summary>0..1.</summary>
    public double Brightness;
    /// <summary>How much of the view it absorbs (dark in front of the disc), 0..1.</summary>
    public double Opacity;
    /// <summary>0..1: an erupting prominence, whose core is bright.</summary>
    public double Erupt;
    public double Seed;
    /// <summary>Why it is here (for the UI).</summary>
    public string Label;
}

/// <summary>A CME close to the star, as the renderer draws it: three parts, the bright front, the dark cavity, the bright core.</summary>
public struct CmeShell
{
    public Double3 Dir;
    /// <summary>The apex's radius (stellar radii).</summary>
    public double Apex;
    public double HalfWidth;
    public double Seed;
    /// <summary>0..1: how bright the erupted prominence at its heart is.</summary>
    public double Core;
    public long Id;
}

/// <summary>Everything about a star's surface and its surroundings at one moment, for the renderer.</summary>
public sealed class StarScene
{
    public double Time, Activity, CyclePhase;
    public readonly List<SpotGroup> Spots = new List<SpotGroup>();
    public readonly List<StarStructure> Structures = new List<StarStructure>();
    public readonly List<CmeShell> Shells = new List<CmeShell>();
    /// <summary>How strong the corona's streamer belt is (0 at minimum, drawn narrow along the equator, 1 at maximum, all round).</summary>
    public double StreamerSpread;
    /// <summary>0..1: the flaring now (the brightest flare's brightening).</summary>
    public double FlareLevel;
    public double PolarityOfCycle = 1.0;
}
