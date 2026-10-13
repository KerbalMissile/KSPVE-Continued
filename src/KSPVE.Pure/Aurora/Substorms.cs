using System;

namespace KSPVE.Pure.Aurora;

public enum SubstormPhase { None, Growth, Expansion, Recovery }

/// <summary>Where a magnetospheric substorm stands at a moment: the growth, onset and expansion, and recovery of the aurora.</summary>
public struct SubstormState
{
    public SubstormPhase Phase;
    /// <summary>Seconds since the phase began.</summary>
    public double PhaseTime;
    /// <summary>0..1 through the current phase.</summary>
    public double Progress;
    /// <summary>The substorm's strength, 0..1 (more activity, stronger).</summary>
    public double Strength;
    /// <summary>
    /// Magnetic local time as an angle from midnight (rad; 0 = midnight, +pi/2 = 6 MLT dawn, -pi/2 = 18 MLT dusk) of the onset.
    /// </summary>
    public double OnsetAngle;
    /// <summary>Angle of the westward travelling surge's head (rad); it starts at the onset and moves towards dusk (falls).</summary>
    public double SurgeAngle;
    /// <summary>How far east (towards dawn) the expansion has spread from the onset (rad, rises).</summary>
    public double EastAngle;
    /// <summary>Growth-phase loading: 0 before, rising to 1 at onset (arcs thin, move equatorward and brighten).</summary>
    public double Growth;
    /// <summary>Breakup brightness: 0..1, jumps up at onset and falls through the expansion.</summary>
    public double Breakup;
    /// <summary>How far the aurora has bulged poleward (0..1 of its largest).</summary>
    public double Bulge;
    /// <summary>Pulsating aurora: grows through the recovery on the morning side, 0..1.</summary>
    public double Pulsating;
    public double OnsetUt;
}

/// <summary>
/// Substorms, deterministic from game time and a seed. Time is cut into two-hour slots, each of which may hold one onset
/// (more likely the higher the activity); a substorm has a growth phase of 40 minutes, an expansion of 30 (onset near 23 MLT,
/// a westward travelling surge, poleward bulge) and a recovery of an hour, in which pulsating patches take over on the
/// morning side (Akasofu 1964; Frey and Mende 2007 for the occurrence rate, one per three to six hours quiet and one an hour in storms).
/// </summary>
public static class Substorms
{
    public const double SlotSeconds = 7200.0;
    public const double GrowthSeconds = 2400.0;
    public const double ExpansionSeconds = 1800.0;
    public const double RecoverySeconds = 3600.0;

    /// <summary>The chance that a slot holds a substorm at an activity index.</summary>
    public static double Chance(double kp) => Math.Max(0.0, Math.Min(0.95, 0.30 + 0.07 * kp));

    public static SubstormState At(double ut, double kp, ulong seed)
    {
        long baseSlot = (long)Math.Floor(ut / SlotSeconds);
        SubstormState best = default;
        double bestOnset = double.NegativeInfinity;
        for (long k = baseSlot - 1; k <= baseSlot + 1; k++)
        {
            ulong h = Hash(seed, (ulong)(k + 1_000_000_000L));
            double chance = Unit(h, 1);
            if (chance > Chance(kp)) continue;
            double onset = (k + 0.25 + 0.7 * Unit(h, 2)) * SlotSeconds;
            double t = ut - onset;
            if (t < -GrowthSeconds || t > ExpansionSeconds + RecoverySeconds) continue;
            if (onset <= bestOnset) continue;
            bestOnset = onset;
            best = Describe(h, ut, onset, kp);
        }
        return best;
    }

    private static SubstormState Describe(ulong h, double ut, double onset, double kp)
    {
        double strength = Math.Max(0.1, Math.Min(1.0, (0.35 + 0.65 * Unit(h, 3)) * (0.45 + 0.07 * kp)));
        double t = ut - onset;
        var s = new SubstormState
        {
            Strength = strength,
            OnsetAngle = -0.26 + (Unit(h, 4) - 0.5) * 0.7,   // 23 MLT +- 1.3 h
            OnsetUt = onset,
        };
        if (t < 0.0)
        {
            s.Phase = SubstormPhase.Growth;
            s.PhaseTime = t + GrowthSeconds;
            s.Progress = s.PhaseTime / GrowthSeconds;
            s.Growth = Smooth(s.Progress);
            s.SurgeAngle = s.OnsetAngle; s.EastAngle = s.OnsetAngle;
        }
        else if (t < ExpansionSeconds)
        {
            s.Phase = SubstormPhase.Expansion;
            s.PhaseTime = t;
            s.Progress = t / ExpansionSeconds;
            s.Growth = 1.0;
            // Breakup: a few tens of seconds to flare, then a slow fall.
            s.Breakup = Smooth(Math.Min(1.0, t / 90.0)) * (1.0 - 0.6 * s.Progress);
            s.Bulge = Smooth(Math.Min(1.0, t / 600.0)) * (1.0 - 0.2 * s.Progress);
            double travel = Smooth(Math.Min(1.0, t / 900.0));
            s.SurgeAngle = s.OnsetAngle - 1.15 * travel;       // toward dusk (westward), about 4 h of local time
            s.EastAngle = s.OnsetAngle + 0.85 * travel;        // and a smaller spread toward dawn
            s.Pulsating = 0.0;
        }
        else
        {
            s.Phase = SubstormPhase.Recovery;
            s.PhaseTime = t - ExpansionSeconds;
            s.Progress = s.PhaseTime / RecoverySeconds;
            s.Growth = 0.0;
            s.Breakup = 0.4 * (1.0 - Smooth(Math.Min(1.0, s.PhaseTime / 900.0))) * (1.0 - 0.6);
            s.Bulge = 0.8 * (1.0 - Smooth(s.Progress));
            s.SurgeAngle = s.OnsetAngle - 1.15; s.EastAngle = s.OnsetAngle + 0.85;
            // Pulsations come on within minutes of the breakup's fading, peak mid-recovery and last about two hours.
            s.Pulsating = Smooth(Math.Min(1.0, s.PhaseTime / 600.0)) * (1.0 - 0.5 * s.Progress);
        }
        return s;
    }

    private static double Smooth(double x)
    {
        x = Math.Max(0.0, Math.Min(1.0, x));
        return x * x * (3.0 - 2.0 * x);
    }

    // SplitMix64-style hashing: no state, so any time can be asked about in any order.
    private static ulong Hash(ulong seed, ulong value)
    {
        unchecked
        {
            ulong z = seed * 0x9E3779B97F4A7C15UL + value * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A uniform number in [0,1) from a hash and a stream index.</summary>
    private static double Unit(ulong h, int stream)
    {
        ulong v = Hash(h, (ulong)stream);
        return (v >> 11) / 9007199254740992.0;
    }
}
