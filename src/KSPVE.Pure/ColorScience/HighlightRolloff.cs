namespace KSPVE.Pure.ColorScience;

/// <summary>
/// KSPVE's default tonemapper for KSP's display-referred image: identity below the knee, then the
/// highlight compression and desaturation step of Khronos PBR Neutral without its toe offset. KSP's
/// look is preserved exactly for values up to the knee, while HDR highlights roll off instead of clipping.
/// </summary>
public static class HighlightRolloff
{
    public const double StartCompression = 0.8 - 0.04;
    public const double Desaturation = 0.15;

    public static Rgb Apply(Rgb color)
    {
        double peak = color.MaxComponent;
        if (peak < StartCompression) return color;

        const double d = 1.0 - StartCompression;
        double newPeak = 1.0 - d * d / (peak + d - StartCompression);
        color *= newPeak / peak;

        double g = 1.0 - 1.0 / (Desaturation * (peak - newPeak) + 1.0);
        Rgb white = new Rgb(newPeak, newPeak, newPeak);
        return color + (white - color) * g;
    }
}
