using System;

namespace KSPVE.Pure.Materials;

/// <summary>Hapke (1981, 1993) regolith reflectance for airless bodies.</summary>
public static class Hapke
{
    /// <summary>
    /// Bidirectional reflectance r = (w/4π) μ0/(μ0+μ) [(1+B(g)) P(g) + H(μ0)H(μ) - 1].
    /// Note r is radiance per unit irradiance perpendicular to the beam (includes the μ0 factor).
    /// hgAsymmetryG uses Hapke's convention: negative = backscattering.
    /// H uses the Hapke 1981 / Cheng-Domingue approximation (1+2x)/(1+2γx), γ = sqrt(1-w).
    /// </summary>
    public static double Reflectance(double mu0, double mu, double phaseAngleRadians, double singleScatteringAlbedo,
        double oppositionAmplitudeB0, double oppositionWidthH, double hgAsymmetryG)
    {
        double w = singleScatteringAlbedo;
        double g = phaseAngleRadians;
        double b = oppositionAmplitudeB0 / (1.0 + Math.Tan(0.5 * g) / oppositionWidthH);
        // Henyey-Greenstein phase function
        double gg = hgAsymmetryG;
        double p = (1.0 - gg * gg) / Math.Pow(1.0 + 2.0 * gg * Math.Cos(g) + gg * gg, 1.5);
        double gamma = Math.Sqrt(1.0 - w);
        double h0 = (1.0 + 2.0 * mu0) / (1.0 + 2.0 * gamma * mu0);
        double h = (1.0 + 2.0 * mu) / (1.0 + 2.0 * gamma * mu);
        return w / (4.0 * Math.PI) * mu0 / (mu0 + mu) * ((1.0 + b) * p + h0 * h - 1.0);
    }
}

/// <summary>Cloth/sheen lobe, Estevez &amp; Kulla 2017.</summary>
public static class Sheen
{
    /// <summary>Charlie NDF: (2 + 1/α) sin^(1/α)θ / 2π, α = perceptualRoughness^2.</summary>
    public static double CharlieD(double nDotH, double perceptualRoughness)
    {
        double alpha = perceptualRoughness * perceptualRoughness;
        double invAlpha = 1.0 / alpha;
        double sin2 = Math.Max(1.0 - nDotH * nDotH, 0.0);
        return (2.0 + invAlpha) * Math.Pow(sin2, 0.5 * invAlpha) / (2.0 * Math.PI);
    }

    /// <summary>Neubelt &amp; Pettineo 2013 (Ready At Dawn "Crafting a Next-Gen Material Pipeline") visibility.</summary>
    public static double NeubeltVisibility(double nDotV, double nDotL) =>
        1.0 / (4.0 * (nDotL + nDotV - nDotL * nDotV));
}
