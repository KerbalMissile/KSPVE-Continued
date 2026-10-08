using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>Colour transfer functions (EOTF/OETF).</summary>
public static class Transfer
{
    /// <summary>Exact IEC 61966-2-1 sRGB decode (encoded [0,1] to linear).</summary>
    public static double SrgbToLinear(double encoded) =>
        encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);

    /// <summary>Exact IEC 61966-2-1 sRGB encode (linear to encoded).</summary>
    public static double LinearToSrgb(double linear) =>
        linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;

    public static double GammaToLinear(double encoded, double gamma = 2.2) =>
        encoded <= 0.0 ? 0.0 : Math.Pow(encoded, gamma);

    public static double LinearToGamma(double linear, double gamma = 2.2) =>
        linear <= 0.0 ? 0.0 : Math.Pow(linear, 1.0 / gamma);
}
