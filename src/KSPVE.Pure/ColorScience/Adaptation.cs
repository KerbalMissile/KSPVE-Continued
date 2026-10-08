using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>Eye/auto-exposure adaptation in EV space.</summary>
public static class Adaptation
{
    /// <summary>
    /// Exponential approach current + (target - current) * (1 - exp(-dt * speed)).
    /// speedUp applies when targetEv &gt; currentEv (EV rising), speedDown otherwise.
    /// </summary>
    public static double Step(double currentEv, double targetEv, double dtSeconds, double speedUp, double speedDown)
    {
        if (!(dtSeconds > 0.0)) return currentEv;
        double speed = targetEv > currentEv ? speedUp : speedDown;
        if (!(speed > 0.0)) return currentEv;
        double k = 1.0 - Math.Exp(-dtSeconds * speed);
        return currentEv + (targetEv - currentEv) * k;
    }
}
