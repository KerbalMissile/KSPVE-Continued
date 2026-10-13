using KSPVE.Pure.Stars;

namespace KSPVE.Stars;

/// <summary>
/// What KSPVE_STARS says about a star body, for other features (the fluids feature draws brown dwarfs). A brown dwarf is a
/// star whose config says kind = browndwarf, or whose effective temperature is below 2500 K; this feature draws no
/// photosphere, prominences or corona on one (it keeps the space-weather model, since brown dwarfs do flare).
/// </summary>
internal static class StarCatalogue
{
    public static bool IsBrownDwarf(CelestialBody body) => body != null && StarWorld.Configured(body.bodyName).IsBrownDwarf;

    /// <summary>The effective temperature (K) the configs give a star (5772 K, the Sun's, when they do not say).</summary>
    public static double EffectiveTemperature(CelestialBody body) =>
        body == null ? StarWorld.Configured("").Temperature : StarWorld.Configured(body.bodyName).Temperature;
}
