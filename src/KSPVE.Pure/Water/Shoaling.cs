using System;

namespace KSPVE.Pure.Water;

/// <summary>
/// Waves running into shallow water: they slow, shorten and grow (shoaling, from conservation of wave energy flux), then
/// break once they are too tall for the depth.
/// </summary>
public static class Shoaling
{
    /// <summary>McCowan's breaker index: a wave breaks when its height reaches 0.78 of the depth (a solitary wave limit; real breakers range 0.6 to 1.1).</summary>
    public const double DefaultBreakerIndex = 0.78;

    /// <summary>
    /// The factor by which a wave's height grows going from deep water into water of depth <paramref name="depth"/>, for a
    /// wave of angular frequency <paramref name="omega"/>: sqrt(cg0 / cg) (linear wave theory, no refraction or friction).
    /// 1 in deep water, about 0.91 at its minimum (just before the depth is half a wavelength), 1/sqrt(2 k h) in very
    /// shallow water, so growing like h^(-1/4) (Green's law).
    /// </summary>
    public static double HeightFactor(double omega, double depth, double gravity)
    {
        if (depth <= 0.0) return 1.0;
        double cgDeep = 0.5 * gravity / omega;
        double k = OceanSpectrum.WaveNumber(omega, depth, gravity, 0.0);
        double kh = k * depth;
        double n = 0.5 * (1.0 + 2.0 * kh / Math.Sinh(2.0 * kh));
        double cg = n * omega / k;
        return Math.Sqrt(cgDeep / cg);
    }

    /// <summary>The wavelength (m) of a wave of angular frequency omega at a depth: it shortens as the depth falls.</summary>
    public static double Wavelength(double omega, double depth, double gravity) => 2.0 * Math.PI / OceanSpectrum.WaveNumber(omega, depth, gravity, 0.0);

    /// <summary>
    /// How near a sea of significant height <paramref name="hs"/> is to breaking at a depth, 0 to 1: zero well out in deep
    /// water, 1 when the largest waves (about 1.8 Hs) reach the breaker index times the depth.
    /// </summary>
    public static double BreakingFraction(double hs, double depth, double breakerIndex = DefaultBreakerIndex)
    {
        if (depth <= 0.0) return 1.0;
        double ratio = 1.8 * hs / (breakerIndex * depth);
        return Math.Min(Math.Max((ratio - 0.5) / 0.5, 0.0), 1.0);
    }

    /// <summary>
    /// The depth (m) at which waves of height <paramref name="waveHeight"/> break: H / h = breaker index, so h = H / 0.78.
    /// </summary>
    public static double BreakingDepth(double waveHeight, double breakerIndex = DefaultBreakerIndex) => waveHeight / breakerIndex;
}
