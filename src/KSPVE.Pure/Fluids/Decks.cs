using System;

namespace KSPVE.Pure.Fluids;

/// <summary>What kind of atmosphere a body has, which fixes its cloud decks and how it looks.</summary>
public enum FluidKind
{
    /// <summary>L dwarf (about 1300 to 2300 K): iron and silicate clouds at and above the photosphere.</summary>
    L = 0,
    /// <summary>T dwarf (about 500 to 1300 K): the iron and silicates have sunk below the photosphere; sulphide and salt clouds remain.</summary>
    T = 1,
    /// <summary>Y dwarf (below about 500 K): water ice clouds above deeper salt clouds.</summary>
    Y = 2,
    /// <summary>A cool giant planet seen mostly by reflected light: ammonia, ammonium hydrosulphide and water clouds.</summary>
    Giant = 3,
    /// <summary>A tidally locked hot giant: silicate / salt clouds with a hot day side, a cold night side and an eastward equatorial jet.</summary>
    HotJupiter = 4
}

/// <summary>
/// One cloud deck. The numbers are the published ones to within what a lumped model can claim: the temperature of the deck
/// relative to the effective temperature (cloud tops are cooler than the photosphere, the clear gas deep down hotter; Marley
/// &amp; Robinson 2015, Morley et al. 2012), the enthalpy of condensation over the gas constant L/R (K) of the species
/// (so that the vapour pressure goes as exp(-L/RT)), and a mean optical depth.
/// </summary>
public sealed class DeckSpec
{
    public string Name;
    /// <summary>Temperature of the deck as a multiple of the effective temperature.</summary>
    public double TemperatureFactor;
    /// <summary>L/R, K: Clausius-Clapeyron slope of the species' vapour pressure.</summary>
    public double LatentOverR;
    /// <summary>Mean cloud optical depth at the wavelengths we see (the field's value 1).</summary>
    public double Tau;
    /// <summary>
    /// Pressure of the optically relevant level of the deck, bar: the gas column above it for the alkali absorption. These are the
    /// pressures of the layers the visible light comes from, well above the cloud bases, because the alkali wings and the
    /// molecular bands make the deeper gas opaque in the visible.
    /// </summary>
    public double Pressure;
    /// <summary>Time for the condensate to rain out or evaporate towards equilibrium, s.</summary>
    public double CondensationTime;
    /// <summary>Colour of sunlight scattered by the deck (linear), for reflected light.</summary>
    public double ReflectR = 0.8, ReflectG = 0.8, ReflectB = 0.8;
    /// <summary>The deck's share of reflected light: the single-scattering albedo times a phase factor, 0..1.</summary>
    public double Albedo = 0.5;

    /// <summary>
    /// The temperature scale over which the equilibrium amount of condensate changes by a factor e: from
    /// d ln p_sat / dT = L / (R T^2), so Theta = R T^2 / L.
    /// </summary>
    public double TemperatureScale(double kelvin) => kelvin * kelvin / LatentOverR;
}

/// <summary>The decks of each kind, top first. The last entry of every set is shallowest of the clear deep layer: see <see cref="DeepFactor"/>.</summary>
public static class Decks
{
    /// <summary>Temperature of the clear gas under the lowest deck, relative to the effective temperature (the clear gas that shines through gaps in the clouds; the near-infrared windows of a cloudy L/T dwarf show 1.2 to 1.3 T_eff, the visible light comes from shallower, cooler gas, so the visible deck is nearer 1.15).</summary>
    public const double DeepFactor = 1.15;

    /// <summary>The temperature of the clear gas under the decks of a kind, relative to its reference temperature (a giant's decks run to 2.2 T_eff, the gas below them is hotter still).</summary>
    public static double DeepFactorFor(FluidKind kind) => kind == FluidKind.Giant ? 2.6 : DeepFactor;

    public static DeckSpec[] For(FluidKind kind)
    {
        switch (kind)
        {
            case FluidKind.L:
                return new[]
                {
                    new DeckSpec { Name = "silicates (Mg2SiO4, MgSiO3)", TemperatureFactor = 0.93, LatentOverR = 6.0e4, Tau = 3.0, Pressure = 0.3, CondensationTime = 6.0e4, ReflectR = 0.55, ReflectG = 0.45, ReflectB = 0.40, Albedo = 0.4 },
                    new DeckSpec { Name = "iron", TemperatureFactor = 1.0, LatentOverR = 4.2e4, Tau = 2.0, Pressure = 0.7, CondensationTime = 4.0e4, ReflectR = 0.5, ReflectG = 0.4, ReflectB = 0.35, Albedo = 0.3 },
                    new DeckSpec { Name = "corundum, perovskite", TemperatureFactor = 1.06, LatentOverR = 8.0e4, Tau = 1.0, Pressure = 1.4, CondensationTime = 8.0e4, ReflectR = 0.6, ReflectG = 0.55, ReflectB = 0.5, Albedo = 0.3 }
                };
            case FluidKind.T:
                return new[]
                {
                    new DeckSpec { Name = "KCl, ZnS", TemperatureFactor = 0.92, LatentOverR = 2.6e4, Tau = 1.6, Pressure = 0.3, CondensationTime = 5.0e4, ReflectR = 0.6, ReflectG = 0.45, ReflectB = 0.5, Albedo = 0.4 },
                    new DeckSpec { Name = "Na2S", TemperatureFactor = 1.0, LatentOverR = 2.2e4, Tau = 1.2, Pressure = 0.8, CondensationTime = 5.0e4, ReflectR = 0.6, ReflectG = 0.5, ReflectB = 0.45, Albedo = 0.4 },
                    new DeckSpec { Name = "MnS, Cr", TemperatureFactor = 1.07, LatentOverR = 3.8e4, Tau = 1.0, Pressure = 1.6, CondensationTime = 8.0e4, ReflectR = 0.5, ReflectG = 0.4, ReflectB = 0.4, Albedo = 0.3 }
                };
            case FluidKind.Y:
                return new[]
                {
                    new DeckSpec { Name = "water ice", TemperatureFactor = 0.80, LatentOverR = 6.1e3, Tau = 1.2, Pressure = 0.2, CondensationTime = 1.0e5, ReflectR = 0.9, ReflectG = 0.9, ReflectB = 0.95, Albedo = 0.6 },
                    new DeckSpec { Name = "KCl, ZnS", TemperatureFactor = 1.00, LatentOverR = 2.6e4, Tau = 1.0, Pressure = 0.8, CondensationTime = 1.0e5, ReflectR = 0.6, ReflectG = 0.5, ReflectB = 0.5, Albedo = 0.4 },
                    new DeckSpec { Name = "Na2S", TemperatureFactor = 1.14, LatentOverR = 2.2e4, Tau = 0.8, Pressure = 1.6, CondensationTime = 1.0e5, ReflectR = 0.6, ReflectG = 0.5, ReflectB = 0.45, Albedo = 0.4 }
                };
            case FluidKind.HotJupiter:
                // Hot Jupiters are dark: measured geometric albedos are mostly 0.03 to 0.2 (Heng and Demory 2013).
                return new[]
                {
                    new DeckSpec { Name = "silicates", TemperatureFactor = 0.88, LatentOverR = 6.0e4, Tau = 2.5, Pressure = 0.2, CondensationTime = 3.0e4, ReflectR = 0.75, ReflectG = 0.7, ReflectB = 0.65, Albedo = 0.18 },
                    new DeckSpec { Name = "MgSiO3, Mg2SiO4", TemperatureFactor = 1.0, LatentOverR = 6.0e4, Tau = 2.0, Pressure = 0.6, CondensationTime = 3.0e4, ReflectR = 0.7, ReflectG = 0.65, ReflectB = 0.6, Albedo = 0.12 },
                    new DeckSpec { Name = "Fe, MnS", TemperatureFactor = 1.10, LatentOverR = 4.2e4, Tau = 1.0, Pressure = 1.5, CondensationTime = 5.0e4, ReflectR = 0.5, ReflectG = 0.45, ReflectB = 0.4, Albedo = 0.06 }
                };
            default:
                // Jupiter's decks (Atreya et al. 1999): temperatures 150 K, 200 K and 270 K for T_eff = 124 K.
                return new[]
                {
                    new DeckSpec { Name = "ammonia ice", TemperatureFactor = 1.21, LatentOverR = 3.1e3, Tau = 3.0, Pressure = 0.7, CondensationTime = 2.0e5, ReflectR = 0.95, ReflectG = 0.93, ReflectB = 0.88, Albedo = 0.9 },
                    new DeckSpec { Name = "ammonium hydrosulphide", TemperatureFactor = 1.61, LatentOverR = 4.5e3, Tau = 2.0, Pressure = 2.0, CondensationTime = 2.0e5, ReflectR = 0.85, ReflectG = 0.6, ReflectB = 0.45, Albedo = 0.6 },
                    new DeckSpec { Name = "water", TemperatureFactor = 2.18, LatentOverR = 6.1e3, Tau = 2.0, Pressure = 5.0, CondensationTime = 3.0e5, ReflectR = 0.9, ReflectG = 0.9, ReflectB = 0.9, Albedo = 0.5 }
                };
        }
    }

    /// <summary>The kind an effective temperature falls into (Kirkpatrick 2005: L 1300-2300 K, T 500-1300 K, Y below 500 K).</summary>
    public static FluidKind KindForTemperature(double kelvin)
    {
        if (kelvin >= 1300.0) return FluidKind.L;
        if (kelvin >= 500.0) return FluidKind.T;
        if (kelvin >= 250.0) return FluidKind.Y;
        return FluidKind.Giant;
    }

    /// <summary>Whether a kind is lit mostly by its own heat (the dwarfs and the hot planets) rather than by a star.</summary>
    public static bool IsSelfLuminous(FluidKind kind) => kind == FluidKind.L || kind == FluidKind.T || kind == FluidKind.Y || kind == FluidKind.HotJupiter;
}
