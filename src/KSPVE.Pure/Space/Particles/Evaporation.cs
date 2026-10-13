using System;

namespace KSPVE.Pure.Space.Particles;

/// <summary>What an evaporating planet is losing, and why: the numbers behind its tails.</summary>
public struct EvaporationState
{
    /// <summary>Share of the envelope left (1: a gas giant; 0: the bare chthonian core) used for this state.</summary>
    public double AtmosphereLeft;
    /// <summary>Planet mass (kg) and radius (m) at that share.</summary>
    public double Mass, Radius;
    /// <summary>Distance to the star (m) and the planet's Hill radius, which is the distance of L1 and L2 (m).</summary>
    public double Distance, HillRadius;
    /// <summary>Roche lobe radius (Eggleton 1983, m) and how much of it the planet fills (radius over lobe: 1 or more overflows).</summary>
    public double LobeRadius, FillFactor;
    /// <summary>The star's X-ray and extreme-ultraviolet luminosity (W), and the flux at the planet (W/m^2).</summary>
    public double XuvLuminosity, XuvFlux;
    /// <summary>Erkaev's correction for the Roche lobe (K, 1 for a planet far inside it).</summary>
    public double ErkaevK;
    /// <summary>Energy-limited photoevaporation (kg/s); Roche lobe overflow (kg/s); dust sublimation of a bare rocky core (kg/s).</summary>
    public double EnergyLimited, RocheOverflow, DustSublimation;
    /// <summary>Substellar temperature of the surface (K).</summary>
    public double SubstellarTemperature;
    /// <summary>The loss that applies: overflow or photoevaporation while there is an envelope, sublimation once it is gone (kg/s).</summary>
    public double Total;
}

/// <summary>
/// How a planet is stripped by its star. Three losses, each from the literature:
/// <list type="bullet">
/// <item>energy-limited photoevaporation, Mdot = eps pi F_XUV R^3 / (G M K) (Watson et al. 1981; Erkaev et al. 2007 for the
/// Roche-lobe factor K), the XUV flux a fixed share of the star's light that falls as it ages (Ribas et al. 2005);</item>
/// <item>Roche-lobe overflow once the planet fills its lobe (Eggleton 1983), in Ritter's (1988) form: the flow through L1
/// from the photosphere, growing as the planet swells past its lobe;</item>
/// <item>sublimation of a bare rocky core's surface (Langmuir evaporation, Perez-Becker and Chiang 2013): what disintegrates
/// KIC 12557548b and K2-22b.</item>
/// </list>
/// </summary>
public static class Evaporation
{
    public const double G = UniversalKepler.G;
    public const double StefanBoltzmann = 5.670374419e-8;
    public const double Boltzmann = 1.380649e-23;
    public const double AtomicMass = 1.66053907e-27;
    public const double LightSpeed = 299792458.0;
    public const double SolarLuminosity = 3.828e26;
    public const double JupiterMass = 1.8982e27, JupiterRadius = 7.1492e7;

    /// <summary>
    /// The share of a star's luminosity that is X-ray and extreme ultraviolet: saturated at 10^-3.5 for the first 100 Myr and
    /// falling as age^-1.23 after (Ribas et al. 2005's decline of a Sun-like star's EUV), which gives the Sun's 3e-6 at 4.6 Gyr.
    /// </summary>
    public static double XuvFraction(double ageGyr)
    {
        double saturated = Math.Pow(10.0, -3.5);
        return ageGyr <= 0.1 ? saturated : saturated * Math.Pow(ageGyr / 0.1, -1.23);
    }

    /// <summary>
    /// Erkaev et al.'s (2007) Roche-lobe factor K = 1 - 3/(2 xi) + 1/(2 xi^3), xi the distance of L1 from the planet over its
    /// radius: the energy that escaping gas no longer needs to climb out of the lobe. Floored at 0.02 (K reaches 0 at xi = 1: the
    /// planet fills its lobe and overflow takes over).
    /// </summary>
    public static double ErkaevFactor(double hillRadius, double planetRadius)
    {
        double xi = Math.Max(hillRadius / Math.Max(planetRadius, 1e-9), 1.0);
        return Math.Max(1.0 - 1.5 / xi + 0.5 / (xi * xi * xi), 0.02);
    }

    /// <summary>The energy-limited loss (kg/s): efficiency <paramref name="epsilon"/> of the XUV flux landing on the planet's disk lifts gas out of its potential well.</summary>
    public static double EnergyLimitedRate(double epsilon, double xuvFlux, double planetRadius, double planetMass, double k) =>
        epsilon * Math.PI * xuvFlux * planetRadius * planetRadius * planetRadius / (G * planetMass * Math.Max(k, 1e-6));

    /// <summary>Eggleton's (1983) Roche lobe radius over the separation, for a planet of mass ratio q = planet / star.</summary>
    public static double LobeFraction(double q) => RocheStream.LobeRadius(q);

    /// <summary>The isothermal sound speed of gas of mean molecular weight <paramref name="mu"/> (amu) at <paramref name="kelvin"/>, with gamma = 1 (m/s).</summary>
    public static double SoundSpeed(double kelvin, double mu) => Math.Sqrt(Boltzmann * kelvin / (mu * AtomicMass));

    /// <summary>
    /// Roche-lobe overflow through L1 in Ritter's (1988) form: Mdot0 (1 + (R - R_L) / H) when the planet fills its lobe,
    /// Mdot0 exp((R - R_L) / H) below it, with Mdot0 = rho c_s Q / sqrt(e) the flow from a photosphere that just touches L1, a
    /// nozzle of area Q = 2 pi (c_s / Omega)^2 (Lubow and Shu 1975) and rho the photospheric density (P_ph / c_s^2, 1 mbar).
    /// The scale height H = c_s^2 / g. Capped at the whole envelope per orbit.
    /// </summary>
    public static double OverflowRate(double planetRadius, double lobeRadius, double planetMass, double temperature, double mu,
        double orbitalOmega, double envelopeMass)
    {
        double cs = SoundSpeed(temperature, mu);
        double g = G * planetMass / (planetRadius * planetRadius);
        double h = Math.Max(cs * cs / g, 1.0);
        double rho = 100.0 / (cs * cs);   // P_ph = 1 mbar = 100 Pa
        double q = 2.0 * Math.PI * (cs / orbitalOmega) * (cs / orbitalOmega);
        double mdot0 = rho * cs * q / Math.Sqrt(Math.E);
        double excess = (planetRadius - lobeRadius) / h;
        double rate = excess >= 0.0 ? mdot0 * (1.0 + excess) : mdot0 * Math.Exp(excess);
        double cap = envelopeMass * orbitalOmega / (2.0 * Math.PI);   // all of it in one orbit
        return Math.Min(rate, Math.Max(cap, 0.0));
    }

    /// <summary>
    /// Saturation vapour pressure of silicate rock (Pa): exp(-A/T + B) with A = 6.5e4 K, the latent-heat scale Perez-Becker and
    /// Chiang (2013) use for olivine and its SiO vapour, B = 30.2 - ln(10) so the pressure is 10^-2 Pa at 2000 K and 500 Pa at
    /// 3000 K, between published fits.
    /// </summary>
    public static double VapourPressure(double kelvin) => Math.Exp(-6.5e4 / kelvin + 30.2 - Math.Log(10.0));

    /// <summary>
    /// Mass lost per second by sublimation from a bare rocky core's dayside (kg/s): Langmuir's rate P sqrt(m / 2 pi k T) per area
    /// (m = 100 amu, silicate vapour) over the effective area 8 pi R^2 T0 / A, the patch round the substellar point where the
    /// temperature, falling as cos^1/4 of the angle from it, keeps the pressure within 1/e.
    /// </summary>
    public static double SublimationRate(double substellarKelvin, double planetRadius, double absorbedFlux = double.PositiveInfinity)
    {
        double t = Math.Max(substellarKelvin, 1.0);
        double flux = VapourPressure(t) * Math.Sqrt(100.0 * AtomicMass / (2.0 * Math.PI * Boltzmann * t));
        double area = 8.0 * Math.PI * planetRadius * planetRadius * t / 6.5e4;
        // Sublimation is paid for by the starlight absorbed (latent heat of silicate, 8e6 J/kg): it cannot exceed that.
        double budget = double.IsInfinity(absorbedFlux) ? double.PositiveInfinity : absorbedFlux * Math.PI * planetRadius * planetRadius / 8e6;
        return Math.Min(flux * area, budget);
    }

    /// <summary>Radiation pressure over gravity for a grain (Burns, Lamy and Soter 1979): 3 L Q / (16 pi G M c rho s), s the grain radius (m).</summary>
    public static double GrainBeta(double starLuminosity, double starMass, double density, double grainRadius, double q = 1.0) =>
        3.0 * starLuminosity * q / (16.0 * Math.PI * G * starMass * LightSpeed * density * Math.Max(grainRadius, 1e-12));

    /// <summary>A star's luminosity from its radius and surface temperature (W).</summary>
    public static double Luminosity(double starRadius, double kelvin) =>
        4.0 * Math.PI * starRadius * starRadius * StefanBoltzmann * Math.Pow(kelvin, 4.0);

    /// <summary>
    /// The planet's radius with <paramref name="left"/> of its envelope: the bare core's plus the rest of the way to the full
    /// radius in proportion to the square root of what is left (the radius of an envelope grows slowly with its mass,
    /// Lopez and Fortney 2014).
    /// </summary>
    public static double RadiusAt(double left, double fullRadius, double coreRadius) =>
        coreRadius + (fullRadius - coreRadius) * Math.Sqrt(Math.Min(Math.Max(left, 0.0), 1.0));

    /// <summary>The radius of a rocky core of the given mass and density (m).</summary>
    public static double CoreRadius(double coreMass, double density) => Math.Pow(3.0 * coreMass / (4.0 * Math.PI * density), 1.0 / 3.0);

    /// <summary>Inputs for <see cref="Evaluate"/>.</summary>
    public struct Inputs
    {
        public double PlanetMass, PlanetRadius;
        public double StarMass, StarRadius, StarTemperature, StarLuminosity;
        public double Distance;
        public double AtmosphereLeft, CoreMassFraction, CoreDensity;
        public double StarAgeGyr, XuvFractionOverride, Efficiency, BondAlbedo, EnvelopeMu;
    }

    /// <summary>All of the losses, for the planet with its envelope share (<c>PlanetMass</c> and <c>PlanetRadius</c> are the full gas giant's).</summary>
    public static EvaporationState Evaluate(Inputs i)
    {
        double left = Math.Min(Math.Max(i.AtmosphereLeft, 0.0), 1.0);
        double coreFraction = Math.Min(Math.Max(i.CoreMassFraction, 1e-3), 1.0);
        double mass = i.PlanetMass * (coreFraction + (1.0 - coreFraction) * left);
        double coreRadius = Math.Min(CoreRadius(i.PlanetMass * coreFraction, Math.Max(i.CoreDensity, 100.0)), i.PlanetRadius);
        double radius = RadiusAt(left, i.PlanetRadius, coreRadius);
        double a = Math.Max(i.Distance, 1.0);
        double lum = i.StarLuminosity > 0.0 ? i.StarLuminosity : Luminosity(i.StarRadius, i.StarTemperature);
        double xuvFraction = i.XuvFractionOverride > 0.0 ? i.XuvFractionOverride : XuvFraction(i.StarAgeGyr);
        var s = new EvaporationState
        {
            AtmosphereLeft = left, Mass = mass, Radius = radius, Distance = a,
            XuvLuminosity = lum * xuvFraction
        };
        s.XuvFlux = s.XuvLuminosity / (4.0 * Math.PI * a * a);
        double q = mass / Math.Max(i.StarMass, 1.0);
        s.HillRadius = a * Math.Pow(q / 3.0, 1.0 / 3.0);
        s.LobeRadius = a * LobeFraction(q);
        s.FillFactor = radius / s.LobeRadius;
        s.ErkaevK = ErkaevFactor(s.HillRadius, radius);
        double envelope = i.PlanetMass * (1.0 - coreFraction) * left;
        s.EnergyLimited = left > 0.0 ? EnergyLimitedRate(i.Efficiency, s.XuvFlux, radius, mass, s.ErkaevK) : 0.0;
        double omega = Math.Sqrt(G * i.StarMass / (a * a * a));
        double teq = i.StarTemperature * Math.Sqrt(i.StarRadius / a) * Math.Pow(Math.Max(1.0 - i.BondAlbedo, 0.0), 0.25);
        s.SubstellarTemperature = teq;
        s.RocheOverflow = left > 0.0 && s.FillFactor > 0.3
            ? OverflowRate(radius, s.LobeRadius, mass, Math.Max(teq * 0.7, 100.0), i.EnvelopeMu > 0.0 ? i.EnvelopeMu : 2.3, omega, envelope)
            : 0.0;
        s.DustSublimation = SublimationRate(teq, Math.Max(coreRadius, 1.0), lum / (4.0 * Math.PI * a * a) * (1.0 - i.BondAlbedo));
        s.Total = left > 1e-3 ? Math.Max(s.EnergyLimited, s.RocheOverflow) : s.DustSublimation;
        return s;
    }

    /// <summary>
    /// The share of the envelope left after <paramref name="seconds"/> of stripping from <paramref name="start"/>, the rate
    /// followed as the planet shrinks (256 steps): a deterministic function of time. Inputs as <see cref="Evaluate"/>
    /// (their AtmosphereLeft is ignored).
    /// </summary>
    public static double AtmosphereAfter(Inputs i, double start, double seconds)
    {
        double left = Math.Min(Math.Max(start, 0.0), 1.0);
        if (seconds <= 0.0 || left <= 0.0) return left;
        double coreFraction = Math.Min(Math.Max(i.CoreMassFraction, 1e-3), 1.0);
        double envelope0 = i.PlanetMass * (1.0 - coreFraction);
        if (envelope0 <= 0.0) return left;
        const int steps = 256;
        double dt = seconds / steps;
        for (int n = 0; n < steps && left > 0.0; n++)
        {
            i.AtmosphereLeft = left;
            EvaporationState s = Evaluate(i);
            double rate = Math.Max(s.EnergyLimited, s.RocheOverflow);
            left = Math.Max(left - rate * dt / envelope0, 0.0);
        }
        return left;
    }
}
