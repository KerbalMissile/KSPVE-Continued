using System;
using KSPVE.Pure.Numerics;

namespace KSPVE.Pure.Space.Particles;

/// <summary>
/// Fills a <see cref="ParticleSet"/> with a structure's particles at a game time. Every position follows from the settings,
/// the seed and the time (the same on any computer, in any order), by two-body motion (<see cref="UniversalKepler"/>): a
/// particle's launch or release time is fixed by its slot, its orbit begins there, and where it is now is the orbit carried
/// on to the present.
/// </summary>
public static class Structures
{
    public const double Year = 365.25 * 86400.0;

    /// <summary>Generates the structure of <paramref name="config"/> at <paramref name="context"/>'s time into <paramref name="set"/>.</summary>
    public static void Generate(StructureConfig config, StructureContext context, ParticleSet set)
    {
        set.Clear();
        if (config.Kind == StructureKind.ToroidalPlanet && config.N("speculative") < 0.5) return;
        set.Ensure((int)config.N("count") + 8);
        switch (config.Kind)
        {
            case StructureKind.EvaporatingPlanet: Evaporating(config, context, set); break;
            case StructureKind.ImpactDebris: Impact(config, context, set); break;
            case StructureKind.Synestia: SynestiaOf(config, context, set); break;
            case StructureKind.SuperPuff: SuperPuffOf(config, context, set); break;
            case StructureKind.RingingMoon: RingingMoonOf(config, context, set); break;
            case StructureKind.RubblePile: RubblePileOf(config, context, set); break;
            case StructureKind.ToroidalPlanet: Torus(config, context, set); break;
        }
        // Light: the star's side shades the far side, and the body shadows what is behind it.
        double shadow = config.N("selfShadow");
        StructureLight.ApplyShadows(set, context.ToStar, shadow, context.BodyRadius);
    }

    private static double Age(StructureConfig c, StructureContext x) => c.N("startAgeHours") * 3600.0 + c.N("ageRate") * Math.Max(x.Ut, 0.0);

    private static Double3 Perpendicular(Double3 axis, out Double3 second)
    {
        Double3 n = axis.Normalized();
        Double3 seed = Math.Abs(n.X) < 0.9 ? Double3.UnitX : Double3.UnitY;
        Double3 u = Double3.Cross(n, seed).Normalized();
        second = Double3.Cross(n, u);
        return u;
    }

    private static double Smooth(double a, double b, double x)
    {
        double t = Math.Min(Math.Max((x - a) / (b - a), 0.0), 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>A rotation of <paramref name="v"/> by <paramref name="angle"/> about the unit <paramref name="axis"/> (Rodrigues).</summary>
    public static Double3 Rotate(Double3 v, Double3 axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + Double3.Cross(axis, v) * s + axis * (Double3.Dot(axis, v) * (1.0 - c));
    }

    /// <summary>
    /// How long (s) until a particle at (<paramref name="r0"/>, <paramref name="v0"/>) falls to within <paramref name="radius"/>
    /// of the centre of mass <paramref name="mu"/>, or +infinity if its orbit never gets that close (or it is flying away). Exact:
    /// the true anomaly where the orbit meets the sphere, turned into times since pericentre by Kepler's equation.
    /// </summary>
    public static double HitTime(double mu, double radius, Double3 r0, Double3 v0)
    {
        double r = r0.Length;
        if (r <= radius) return 0.0;
        Double3 hv = Double3.Cross(r0, v0);
        double h2 = hv.LengthSquared, p = h2 / mu;
        double energy = 0.5 * v0.LengthSquared - mu / r;
        double e = Math.Sqrt(Math.Max(1.0 + 2.0 * energy * h2 / (mu * mu), 0.0));
        if (e < 1e-12) return double.PositiveInfinity;   // circular, and outside the sphere
        if (p / (1.0 + e) >= radius) return double.PositiveInfinity;
        double vr = Double3.Dot(r0, v0) / r;
        double nu0 = Math.Acos(Math.Min(Math.Max((p / r - 1.0) / e, -1.0), 1.0));
        if (vr < 0.0) nu0 = -nu0;
        double nuHit = Math.Acos(Math.Min(Math.Max((p / radius - 1.0) / e, -1.0), 1.0));   // the outgoing crossing; the incoming is -nuHit
        if (e < 1.0)
        {
            double a = p / (1.0 - e * e), n = Math.Sqrt(mu / (a * a * a)), period = 2.0 * Math.PI / n;
            double tIn = SinceCentre(-nuHit, e) / n, t0 = SinceCentre(nu0, e) / n;
            double k = Math.Ceiling((t0 - tIn) / period);
            return Math.Max(tIn + k * period - t0, 0.0);
        }
        else
        {
            if (nu0 >= 0.0) return double.PositiveInfinity;   // already past pericentre and leaving
            double a = p / (e * e - 1.0), n = Math.Sqrt(mu / (a * a * a));
            double tIn = SinceCentre(-nuHit, e) / n, t0 = SinceCentre(nu0, e) / n;
            return Math.Max(tIn - t0, 0.0);
        }
    }

    // Mean anomaly (elliptic) or hyperbolic mean anomaly at true anomaly nu: time since pericentre times the mean motion.
    private static double SinceCentre(double nu, double e)
    {
        if (e < 1.0)
        {
            double ea = 2.0 * Math.Atan(Math.Sqrt((1.0 - e) / (1.0 + e)) * Math.Tan(nu / 2.0));
            return ea - e * Math.Sin(ea);
        }
        double hh = 2.0 * Atanh(Math.Sqrt((e - 1.0) / (e + 1.0)) * Math.Tan(nu / 2.0));
        return e * Math.Sinh(hh) - hh;
    }

    private static double Atanh(double x) => 0.5 * Math.Log((1.0 + x) / (1.0 - x));

    // ------------------------------------------------------------------------------------------------------- evaporating

    /// <summary>The evaporating planet's inputs, from the settings and the scene.</summary>
    public static Evaporation.Inputs EvaporationInputs(StructureConfig c, StructureContext x, double atmosphereLeft)
    {
        return new Evaporation.Inputs
        {
            PlanetMass = x.BodyMass, PlanetRadius = x.BodyRadius,
            StarMass = Math.Max(x.StarMass, 1.0), StarRadius = Math.Max(x.StarRadius, 1.0), StarTemperature = c.N("starTemperature"),
            StarLuminosity = c.N("starLuminosity") > 0.0 ? c.N("starLuminosity") : x.StarLuminosity,
            Distance = Math.Max(x.StarDistance, 1.0),
            AtmosphereLeft = atmosphereLeft, CoreMassFraction = c.N("coreMassFraction"), CoreDensity = c.N("coreDensity"),
            StarAgeGyr = c.N("starAgeGyr"), XuvFractionOverride = c.N("xuvFraction"), Efficiency = c.N("escapeEfficiency"),
            BondAlbedo = c.N("bondAlbedo"), EnvelopeMu = c.N("envelopeMu")
        };
    }

    /// <summary>The share of the envelope left now: as set, or stripped over the game's time when "evolve" is on.</summary>
    public static double AtmosphereNow(StructureConfig c, StructureContext x)
    {
        double start = c.N("atmosphereLeft");
        if (c.N("evolve") < 0.5 || !x.HasStar) return start;
        double seconds = c.N("ageRate") * (Math.Max(x.Ut, 0.0) + c.N("startAgeHours") * 3600.0);
        return Evaporation.AtmosphereAfter(EvaporationInputs(c, x, start), start, seconds);
    }

    /// <summary>The losses of the evaporating planet now (what the settings window reports).</summary>
    public static EvaporationState EvaporationNow(StructureConfig c, StructureContext x) =>
        Evaporation.Evaluate(EvaporationInputs(c, x, AtmosphereNow(c, x)));

    /// <summary>Distance of L2 from a planet of mass ratio q = planet / star, as a fraction of the separation (the potential's slope beyond the planet).</summary>
    public static double L2Distance(double q)
    {
        double lo = 1.0 + 1e-6, hi = 1.0 + 3.0 * Math.Pow(Math.Max(q, 1e-12) / 3.0, 1.0 / 3.0) + 0.05;
        for (int i = 0; i < 100; i++)
        {
            double mid = 0.5 * (lo + hi);
            RocheStream.Potential(q, new Double3(mid, 0, 0), out Double3 g);
            if (g.X > 0.0) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi) - 1.0;
    }

    private static void Evaporating(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double albedo = c.N("albedo"), boostTail = c.N("tailDensity"), emissionCap = c.N("emissionCap");
        double left = AtmosphereNow(c, x);
        EvaporationState st = Evaporation.Evaluate(EvaporationInputs(c, x, left));
        double rp = st.Radius, ut = x.Ut;
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 12345UL;
        Double3 clumpTint = c.C("clumpColor"), gasTint = c.C("gasColor"), dustTint = c.C("dustColor");
        double sizeScale = c.N("sizeScale");

        // Shares: the bound halo first, then clumps, gas and dust in the proportions asked (the rest is dust).
        double halo = c.N("haloShare"), clump = c.N("clumpShare"), gas = Math.Min(c.N("gasShare"), Math.Max(1.0 - clump, 0.0));
        int nHalo = (int)Math.Round(halo * count), rest = count - nHalo;
        int nClump = (int)Math.Round(clump * rest), nGas = (int)Math.Round(gas * rest), nDust = Math.Max(rest - nClump - nGas, 0);

        double gasA = c.N("gasTailHours") * 3600.0, dustA = c.N("dustTailHours") * 3600.0, clumpA = c.N("streamHours") * 3600.0;
        double mdot = st.Total;
        double mdotDust = left > 1e-3 ? mdot * 1e-3 : st.DustSublimation;
        double mdotGas = left > 1e-3 ? mdot : 0.0;
        double grainRadius = c.N("grainSizeUm") * 1e-6, grainDensity = c.N("grainDensity");

        // Without a star there is no orbit to follow: only the bound halo.
        double mu = x.StarMu;
        Double3 rp0 = x.BodyFromStar, vp0 = x.BodyVelocity;
        bool orbiting = x.HasStar && rp0.LengthSquared > 0.0 && mu > 0.0;
        double q = x.BodyMass / Math.Max(x.StarMass, 1.0);
        double l1 = orbiting ? (1.0 - RocheStream.L1FromCompact(q)) * rp0.Length : 3.0 * rp;
        double l2 = orbiting ? L2Distance(q) * rp0.Length : 3.0 * rp;
        l1 = Math.Max(l1, 1.05 * rp);
        l2 = Math.Max(l2, 1.05 * rp);
        double l1Share = c.N("l1Share"), l2Share = Math.Min(c.N("l2Share"), 1.0 - l1Share);
        double speed = c.N("ejectionSpeed"), spreadSpeed = c.N("speedSpread"), cone = c.N("spreadAngle") * Math.PI / 180.0;
        double clumpBeta = c.N("clumpBeta"), gasBeta = c.N("gasBeta");
        double luminosity = c.N("starLuminosity") > 0.0 ? c.N("starLuminosity") : Evaporation.Luminosity(Math.Max(x.StarRadius, 1.0), c.N("starTemperature"));
        double starMass = Math.Max(x.StarMass, 1.0);

        int index = 0;
        // ---- the bound atmosphere: gas inside the Roche lobe, drawn out towards L1 and L2
        Double3 sun = x.ToStar;
        for (int i = 0; i < nHalo; i++, index++)
        {
            ulong key = Rng.Key(seed, index, 0);
            Double3 dir = Rng.Direction(key, 0);
            double along = Math.Abs(Double3.Dot(dir, sun));
            double lobe = Math.Min(st.LobeRadius, 6.0 * rp);
            double reach = rp * (1.0 + 0.6 * Math.Pow(Rng.U(key, 2), 2.0)) + Math.Max(lobe - rp, 0.0) * along * along * Rng.U(key, 3) * Math.Min(st.FillFactor, 1.0);
            Double3 pos = dir * (Math.Max(reach, rp * 0.98));
            double fillGlow = left > 1e-3 ? 0.6 : 0.0;
            Double3 emit = gasTint * (c.N("gasEmission") * fillGlow * 0.002 * Math.Sqrt(Math.Min(boostTail, 1e8)));
            set.Add(pos, rp * 0.28 * sizeScale, dustTint * (albedo * 0.5 * left), emit, 0.2 * left, 1f);
        }

        // ---- clumps, gas and dust launched in turn
        for (int type = 0; type < 3; type++)
        {
            int n = type == 0 ? nClump : type == 1 ? nGas : nDust;
            double life = type == 0 ? clumpA : type == 1 ? gasA : dustA;
            for (int i = 0; i < n; i++, index++)
            {
                ulong slot = Rng.Key(seed, index, 0);
                double phase = Rng.U(slot, 0);
                double cycles = ut / life + phase;
                long launch = (long)Math.Floor(cycles);
                double age = (cycles - launch) * life, fraction = age / life;
                ulong key = Rng.Key(seed, index, launch + 1);
                if (!orbiting)
                {
                    continue;
                }

                // The planet at launch (back along its own orbit) and the frame there: x to the star, y along the orbit.
                UniversalKepler.Propagate(mu, rp0, vp0, -age, out Double3 rpL, out Double3 vpL);
                Double3 s = (-rpL).Normalized();
                Double3 n3 = Double3.Cross(rpL, vpL).Normalized();
                Double3 along = Double3.Cross(s, n3);
                Double3 omega = Double3.Cross(rpL, vpL) / rpL.LengthSquared;

                Double3 offset, direction;
                double beta, sizeFactor = Math.Exp(0.5 * Rng.Gauss(key, 10)), v = speed * Math.Exp(spreadSpeed * Rng.Gauss(key, 12) * 0.5);
                double kind = Rng.U(slot, 2);
                if (type == 0)
                {
                    // Clumps: L1 (towards the star), L2 (away) or the surface.
                    beta = clumpBeta;
                    if (kind < l1Share) { offset = s * (l1 * (1.0 + 0.04 * Rng.Gauss(key, 14))); direction = s; }
                    else if (kind < l1Share + l2Share) { offset = -s * (l2 * (1.0 + 0.04 * Rng.Gauss(key, 14))); direction = -s; }
                    else
                    {
                        Double3 u = Rng.Direction(key, 14);
                        // Gas and clumps lifted off the surface leave mostly downwind, as the star's wind and light drive them.
                        offset = u * rp;
                        direction = (u * 0.35 + (-s)).Normalized();
                    }
                }
                else if (type == 1)
                {
                    // Gas: from the whole upper atmosphere, pushed away from the star as an ion tail is.
                    beta = gasBeta * Math.Max(1.0 + 0.3 * Rng.Gauss(key, 14), 0.1);
                    Double3 u = Rng.Direction(key, 16);
                    offset = u * rp * (1.0 + 0.3 * Rng.U(key, 18));
                    direction = (u + (-s) * 0.8).Normalized();
                }
                else
                {
                    // Dust: lifted from the star-facing side, slowly; radiation pressure does the rest.
                    double grain = grainRadius * Math.Exp(0.5 * Rng.Gauss(key, 14));
                    beta = Evaporation.GrainBeta(luminosity, starMass, grainDensity, grain);
                    Double3 u = Rng.Direction(key, 16);
                    if (Double3.Dot(u, s) < 0.0) u = u - s * (2.0 * Double3.Dot(u, s));
                    offset = u * rp;
                    direction = u;
                    v *= 0.35;
                    sizeFactor = 1.0;
                }
                // The launch cone: the direction tilted by up to the spread angle in a random sense.
                if (cone > 0.0)
                {
                    Double3 perp = Perpendicular(direction, out Double3 perp2);
                    double tilt = cone * Math.Sqrt(Rng.U(key, 20)), az = 2.0 * Math.PI * Rng.U(key, 21);
                    direction = (direction * Math.Cos(tilt) + (perp * Math.Cos(az) + perp2 * Math.Sin(az)) * Math.Sin(tilt)).Normalized();
                }
                Double3 r0 = rpL + offset;
                Double3 v0 = vpL + Double3.Cross(omega, offset) + direction * v;
                UniversalKepler.Propagate(mu * (1.0 - beta), r0, v0, age, out Double3 rc, out _);
                // Where the planet is now (the body is at the origin).
                Double3 rel = rc - rp0;

                double fade = Smooth(0.0, 0.05, fraction) * (1.0 - Smooth(0.7, 1.0, fraction));
                if (type == 0)
                {
                    double radius = rp * c.N("clumpRadius") * sizeScale * sizeFactor;
                    set.Add(rel, radius, clumpTint * (albedo * fade), Double3.Zero, fade, 0f);
                }
                else if (type == 1)
                {
                    double radius = rp * c.N("gasPuffRadius") * sizeScale * (1.0 + 2.5 * fraction);
                    // Optically thin line emission proportional to the column of gas in the puff (mass over area), 1 at 10 g/m^2.
                    double mass = mdotGas * life / Math.Max(nGas, 1) * boostTail;
                    double column = mass / (Math.PI * radius * radius);
                    double glow = Math.Min(c.N("gasEmission") * column / 0.01, emissionCap) * (1.0 - fraction) * fade;
                    set.Add(rel, radius, Double3.Zero, gasTint * glow, 0.05, 1f);
                }
                else
                {
                    double radius = rp * c.N("dustPuffRadius") * sizeScale * (1.0 + 3.0 * fraction);
                    double mass = mdotDust * life / Math.Max(nDust, 1) * boostTail;
                    double tau = mass / (4.0 / 3.0 * Math.PI * grainRadius * grainDensity * radius * radius);
                    double cover = 1.0 - Math.Exp(-Math.Min(tau, 50.0));
                    set.Add(rel, radius, dustTint * (albedo * cover * fade), Double3.Zero, cover * fade, 1f);
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------- impact debris

    private static void Impact(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double mu = x.BodyMu, rb = x.BodyRadius, age = Age(c, x);
        double vEsc = Math.Sqrt(2.0 * mu / rb);
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 777UL;
        double r0Cloud = c.N("cloudRadius") * rb, spread = c.N("speedSpread"), spin = c.N("impactSpin"), ejecta = c.N("ejectaSpeed");
        double blob = c.N("blobRadius") * rb * c.N("sizeScale"), blobSpread = c.N("blobSizeSpread"), rho = c.N("blobDensity"), cp = c.N("heatCapacity");
        double t0 = c.N("initialTemperature"), eps = c.N("emissivity"), albedo = c.N("albedo"), flux = x.LocalFlux, cap = c.N("emissionCap");
        bool reaccrete = c.N("reaccrete") > 0.5;
        Double3 tint = c.C("clumpColor"), axis = x.Axis.Normalized();
        Double3 site = Rng.Direction(Rng.Key(seed, -1, 0), 0);
        Double3 center = site * (rb + 0.35 * r0Cloud);

        for (int i = 0; i < count; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            Double3 off = Rng.Direction(key, 0) * (r0Cloud * Math.Pow(Rng.U(key, 2), 1.0 / 3.0));
            Double3 r0 = center + off;
            if (r0.Length < rb * 1.02) r0 = r0.Normalized() * (rb * 1.02 + 0.05 * rb * Rng.U(key, 3));
            // Homologous expansion: the speed grows with the distance from the cloud's centre.
            double v = ejecta * vEsc * (0.3 + 0.7 * Math.Min((r0 - center).Length / Math.Max(r0Cloud, 1e-9), 1.0)) * Math.Exp(spread * Rng.Gauss(key, 4));
            Double3 outward = (r0 - center + site * (0.3 * r0Cloud)).Normalized();
            Double3 swirl = Double3.Cross(axis, r0);
            Double3 swirlDir = swirl.LengthSquared > 0.0 ? swirl.Normalized() : outward;
            Double3 v0 = outward * v + swirlDir * (spin * Math.Sqrt(mu / r0.Length));
            double radius = blob * Math.Exp(blobSpread * Rng.Gauss(key, 6));
            double hit = reaccrete ? HitTime(mu, rb, r0, v0) : double.PositiveInfinity;
            if (hit <= age) continue;   // fell back: absorbed
            UniversalKepler.Propagate(mu, r0, v0, age, out Double3 r, out _);

            // Cooling by radiation: T^-3 = T0^-3 + 3 a t, a = 3 sigma eps / (rho c s).
            double temp0 = t0 * (0.8 + 0.4 * Rng.U(key, 8));
            double droplet = c.N("dropletRadius") * Math.Exp(c.N("dropletSpread") * Rng.Gauss(key, 9));
            double a = 3.0 * Evaporation.StefanBoltzmann * eps / (rho * cp * droplet);
            double temp = Math.Pow(Math.Pow(temp0, -3.0) + 3.0 * a * age, -1.0 / 3.0);
            double fadeOut = reaccrete && !double.IsInfinity(hit) ? 1.0 - Smooth(0.7, 1.0, age / hit) : 1.0;
            Double3 glow = StructureLight.Glow(temp, eps, flux, cap, c.N("glowGain")) * fadeOut;
            set.Add(r, radius * fadeOut, tint * albedo, glow, 1.0, 0.35f);
        }
    }

    // ------------------------------------------------------------------------------------------------------------ synestia

    /// <summary>The radius (m) inside which a body turning once in <paramref name="spinPeriod"/> seconds is corotating with its own orbit (Omega_spin = Omega_K).</summary>
    public static double CorotationRadius(double mu, double spinPeriod) =>
        Math.Pow(mu * spinPeriod * spinPeriod / (4.0 * Math.PI * Math.PI), 1.0 / 3.0);

    private static void SynestiaOf(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double mu = x.BodyMu, rb = x.BodyRadius, age = Age(c, x);
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 4242UL;
        double rc = c.N("corotation") * rb, ext = c.N("extent") * rb, omegaSpin = Math.Sqrt(mu / (rc * rc * rc));
        double t0 = c.N("initialTemperature"), eps = c.N("emissivity"), albedo = c.N("albedo"), flux = x.LocalFlux, cap = c.N("emissionCap");
        double tau = c.N("coolingYears") * Year / 0.953, cool = Math.Pow(1.0 + age / tau, -1.0 / 3.0);
        double flare = c.N("flareBoost"), sizeScale = c.N("sizeScale");
        Double3 tint = c.C("clumpColor");
        Double3 axis = x.Axis.Normalized(), e1 = Perpendicular(axis, out Double3 e2);
        bool moon = c.N("moon") > 0.5;
        int nMoon = moon ? count / 6 : 0, nMain = count - nMoon;
        double puff = 0.55 * ext * Math.Pow(1.0 / Math.Max(nMain, 1), 1.0 / 3.0) * sizeScale;
        const double gamma = 1.1, molecularWeight = 30.0;

        for (int i = 0; i < nMain; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            // Surface density falling as r^-3/2 outwards: the radius from the cumulative mass (~ sqrt r).
            double u = Rng.U(key, 0);
            double sq = 1.0 + (Math.Sqrt(ext / rb) - 1.0) * u;
            double r = rb * sq * sq;
            double temp = t0 * Math.Pow(r / rb, -0.5) * cool;
            double cs = Math.Sqrt(gamma * Evaporation.Boltzmann * temp / (molecularWeight * Evaporation.AtomicMass));
            double omegaK = Math.Sqrt(mu / (r * r * r));
            double h = Math.Min(cs / omegaK * flare, 0.7 * r);
            double omega = r < rc ? omegaSpin : omegaK;
            double phi = 2.0 * Math.PI * Rng.U(key, 1) + omega * Math.Max(x.Ut, 0.0) * c.N("ageRate");
            double z = h * Rng.Gauss(key, 2);
            Double3 pos = e1 * (r * Math.Cos(phi)) + e2 * (r * Math.Sin(phi)) + axis * z;
            Double3 glow = StructureLight.Glow(temp, c.N("emissivity"), flux, cap, c.N("glowGain"));
            set.Add(pos, puff * (0.7 + 0.6 * Rng.U(key, 4)) * Math.Sqrt(r / rb) * 0.6, tint * (albedo * 0.5), glow, 0.8, 1f);
        }

        if (moon)
        {
            double tauMoon = c.N("moonYears") * Year;
            double gathered = 1.0 - Math.Exp(-age / tauMoon);
            double dm = (c.N("moonDistance") > 0.0 ? c.N("moonDistance") : c.N("corotation")) * rb;
            double rm = c.N("moonRadius") * rb * Math.Pow(Math.Max(gathered, 1e-6), 1.0 / 3.0);
            double omegaM = Math.Sqrt(mu / (dm * dm * dm));
            double ang = 2.0 * Math.PI * Rng.U(Rng.Key(seed, -2, 0), 0) + omegaM * Math.Max(x.Ut, 0.0) * c.N("ageRate");
            Double3 moonCentre = e1 * (dm * Math.Cos(ang)) + e2 * (dm * Math.Sin(ang));
            double moonTemp = 1800.0 * cool;
            for (int i = 0; i < nMoon; i++)
            {
                ulong key = Rng.Key(seed, nMain + i, 0);
                // Particles join in a random order: at first only a few, a cloud thickening towards a ball.
                if (Rng.U(key, 0) > gathered) continue;
                double spreadFactor = 1.0 + 3.0 * (1.0 - gathered);
                Double3 pos = moonCentre + Rng.Direction(key, 1) * (rm * spreadFactor * Math.Pow(Rng.U(key, 3), 1.0 / 3.0));
                set.Add(pos, rm * 0.22 * sizeScale + 1.0, tint * albedo, StructureLight.Glow(moonTemp, eps, flux, cap, c.N("glowGain")), 1.0, 0.1f);
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------- super-puff

    /// <summary>The mean density of a planet of mass <paramref name="mass"/> and visible radius <paramref name="radius"/> (kg/m^3): below 100 is a super-puff.</summary>
    public static double MeanDensity(double mass, double radius) => mass / (4.0 / 3.0 * Math.PI * radius * radius * radius);

    /// <summary>
    /// The lambda of an isothermal hydrostatic envelope, G M m / (k T r_core): the density falls as exp(-lambda (1 - r_core / r)).
    /// </summary>
    public static double EnvelopeLambda(double mass, double coreRadius, double kelvin, double mu) =>
        UniversalKepler.G * mass * mu * Evaporation.AtomicMass / (Evaporation.Boltzmann * kelvin * coreRadius);

    private static void SuperPuffOf(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double rc = x.BodyRadius, mass = x.BodyMass;
        double lambda = EnvelopeLambda(mass, rc, c.N("atmTemperature"), c.N("envelopeMu"));
        double rMax = c.N("puffRadius") * rc * 1.5;
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 99UL;
        double albedo = c.N("albedo"), haze = c.N("hazeOpacity"), sizeScale = c.N("sizeScale");
        Double3 tint = c.C("hazeColor");
        // The cumulative mass of rho(r) r^2 on a grid, inverted to sample radii.
        const int bins = 512;
        var cdf = new double[bins + 1];
        for (int b = 1; b <= bins; b++)
        {
            double r = rc + (rMax - rc) * (b - 0.5) / bins;
            cdf[b] = cdf[b - 1] + Math.Exp(-lambda * (1.0 - rc / r)) * r * r;
        }
        for (int i = 0; i < count; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            double target = Rng.U(key, 0) * cdf[bins];
            int lo = 0, hi = bins;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cdf[mid] < target) lo = mid; else hi = mid; }
            double r = rc + (rMax - rc) * (lo + (target - cdf[lo]) / Math.Max(cdf[hi] - cdf[lo], 1e-300)) / bins;
            // The local scale height r^2 / (lambda r_core) sizes the puff.
            double h = r * r / (lambda * rc);
            double radius = Math.Min(h * c.N("puffRadiusSpread"), 0.4 * r) * sizeScale;
            Double3 pos = Rng.Direction(key, 2) * r;
            set.Add(pos, radius, tint * (albedo * 0.5 * haze), Double3.Zero, Math.Min(haze, 1.0), 1f);
        }
    }

    // ------------------------------------------------------------------------------------------------------- moon into a ring

    /// <summary>The fluid Roche limit (m): 2.456 R_planet (rho_planet / rho_moon)^(1/3) (Roche 1850), inside which tides outpull a moon's own gravity.</summary>
    public static double RocheLimit(double planetRadius, double planetDensity, double moonDensity) =>
        2.456 * planetRadius * Math.Pow(planetDensity / moonDensity, 1.0 / 3.0);

    private static void RingingMoonOf(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double mu = x.BodyMu, rb = x.BodyRadius, age = Age(c, x), mass = x.BodyMass;
        double rhoPlanet = mass / (4.0 / 3.0 * Math.PI * rb * rb * rb);
        double rm = c.N("moonSizeKm") * 1000.0, rhoMoon = c.N("moonDensity");
        double a = Math.Max(c.N("moonDistanceRoche") * RocheLimit(rb, rhoPlanet, rhoMoon), 1.05 * rb + rm), e = c.N("moonEccentricity");
        double tStart = c.N("disruptStartHours") * 3600.0, span = c.N("disruptSpanHours") * 3600.0;
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 31UL;
        double albedo = c.N("albedo"), blob = c.N("grainRadius") * rm * c.N("sizeScale"), blobSpread = c.N("blobSizeSpread");
        Double3 tint = c.C("clumpColor"), axis = x.Axis.Normalized(), e1 = Perpendicular(axis, out Double3 e2);
        double vEscMoon = Math.Sqrt(2.0 * UniversalKepler.G * (4.0 / 3.0 * Math.PI * rm * rm * rm * rhoMoon) / rm);
        // The moon's orbit: pericentre on e1 at time 0, prograde about the axis.
        double rPeri = a * (1.0 - e), vPeri = Math.Sqrt(mu * (1.0 + e) / rPeri);
        Double3 cr0 = e1 * rPeri, cv0 = e2 * vPeri;
        double moonSpin = Math.Sqrt(mu / (a * a * a));
        bool reaccrete = c.N("reaccrete") > 0.5;
        double now = age;

        for (int i = 0; i < count; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            double rho = Math.Pow(Rng.U(key, 0), 1.0 / 3.0);
            Double3 o0 = Rng.Direction(key, 1) * (rm * rho);
            double release = tStart + span * (1.0 - rho);
            double radius = blob * Math.Exp(blobSpread * Rng.Gauss(key, 3)) * (0.3 + 0.7 * (1.0 - rho * 0.5));
            Double3 pos;
            if (now < release)
            {
                UniversalKepler.Propagate(mu, cr0, cv0, now, out Double3 cr, out _);
                // The moon is tidally locked: it turns with its own orbit about the axis.
                pos = cr + Rotate(o0, axis, moonSpin * now);
            }
            else
            {
                UniversalKepler.Propagate(mu, cr0, cv0, release, out Double3 cr, out Double3 cv);
                Double3 off = Rotate(o0, axis, moonSpin * release);
                Double3 rr = cr + off;
                Double3 vr = cv + Double3.Cross(axis * moonSpin, off) + Rng.Direction(key, 5) * (0.02 * vEscMoon * Rng.U(key, 7));
                double hit = reaccrete ? HitTime(mu, rb, rr, vr) : double.PositiveInfinity;
                if (hit <= now - release) continue;
                UniversalKepler.Propagate(mu, rr, vr, now - release, out pos, out _);
            }
            set.Add(pos, radius, tint * albedo, Double3.Zero, 1.0, 0.1f);
        }
    }

    // ---------------------------------------------------------------------------------------------------------- rubble pile

    private static void RubblePileOf(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double mu = x.BodyMu, rb = x.BodyRadius, age = Age(c, x);
        double wCrit = Math.Sqrt(mu / (rb * rb * rb)), w0 = c.N("spinFraction") * wCrit;
        double doubling = c.N("spinUpYears") * Year;
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 555UL;
        double albedo = c.N("albedo"), blob = c.N("blobRadius") * rb * c.N("sizeScale"), blobSpread = c.N("blobSizeSpread");
        Double3 tint = c.C("coreColor"), axis = x.Axis.Normalized(), e1 = Perpendicular(axis, out Double3 e2);
        bool binary = c.N("contactBinary") > 0.5, reaccrete = c.N("reaccrete") > 0.5;

        // Spin phase after t: omega(t) = w0 2^(t / doubling), its integral.
        double ln2 = Math.Log(2.0);
        double Omega(double t) => w0 * Math.Pow(2.0, Math.Min(t / doubling, 60.0));
        double Phase(double t) => w0 * doubling / ln2 * (Math.Pow(2.0, Math.Min(t / doubling, 60.0)) - 1.0);

        double ratio = c.N("lobeRatio"), rA = rb, rB = rb * ratio;
        double mA = 1.0, mB = ratio * ratio * ratio, dSep = 0.95 * (rA + rB);
        Double3 centreA = e1 * (-dSep * mB / (mA + mB)), centreB = e1 * (dSep * mA / (mA + mB));
        double split = c.N("splitHours") * 3600.0;
        // After the split the lobes' relative orbit about the pair's centre of mass (G M of the whole).
        Double3 relR = e1 * dSep, relV = e2 * (c.N("splitSpeed") * Math.Sqrt(mu / dSep));

        for (int i = 0; i < count; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            bool surface = Rng.U(key, 0) < 0.55;
            double radius = blob * Math.Exp(blobSpread * Rng.Gauss(key, 1));
            if (binary)
            {
                bool second = Rng.U(key, 2) < mB / (mA + mB);
                double lobeR = second ? rB : rA;
                double rr = surface ? lobeR * (0.9 + 0.1 * Rng.U(key, 3)) : lobeR * Math.Pow(Rng.U(key, 3), 1.0 / 3.0);
                Double3 o = Rng.Direction(key, 4) * rr;
                Double3 centre = second ? centreB : centreA;
                double mine = second ? mA / (mA + mB) : -mB / (mA + mB);
                double angle = Phase(Math.Min(age, split));
                Double3 pos;
                if (age <= split) pos = Rotate(centre + o, axis, angle);
                else
                {
                    UniversalKepler.Propagate(mu, relR, relV, age - split, out Double3 rr2, out _);
                    // Each lobe keeps the spin it had at the split.
                    double spinAfter = Omega(split) * (age - split);
                    pos = Rotate(rr2, axis, Phase(split)) * mine + Rotate(o, axis, angle + spinAfter);
                }
                set.Add(pos, radius, tint * albedo, Double3.Zero, 1.0, 0.1f);
                continue;
            }

            double r = surface ? rb * (0.9 + 0.1 * Rng.U(key, 3)) : rb * 0.85 * Math.Pow(Rng.U(key, 3), 1.0 / 3.0);
            Double3 dir = Rng.Direction(key, 4);
            Double3 offset = dir * r;
            double sinTheta = Math.Sqrt(Math.Max(1.0 - Double3.Dot(dir, axis) * Double3.Dot(dir, axis), 1e-4));
            double cohesion = 0.15 * Rng.U(key, 6);
            // A surface boulder leaves when the spin's centrifugal pull beats gravity there.
            double shed = double.PositiveInfinity;
            if (surface)
            {
                double need = Math.Sqrt((1.0 + cohesion) / sinTheta) * wCrit;   // spin rate at which it floats off
                shed = need <= w0 ? 0.0 : doubling * Math.Log(need / w0) / ln2;
            }
            Double3 pos2;
            if (age < shed || double.IsPositiveInfinity(shed))
            {
                pos2 = Rotate(offset, axis, Phase(age));
            }
            else
            {
                Double3 rr = Rotate(offset, axis, Phase(shed));
                double wShed = Omega(shed);
                Double3 vv = Double3.Cross(axis * wShed, rr) + rr.Normalized() * (0.02 * Math.Sqrt(mu / rb));
                double hit = reaccrete ? HitTime(mu, rb * 0.999, rr, vv) : double.PositiveInfinity;
                if (hit <= age - shed)
                {
                    // It came back down: part of the pile again, turning with it.
                    set.Add(Rotate(offset, axis, Phase(age)), radius, tint * albedo, Double3.Zero, 1.0, 0.1f);
                    continue;
                }
                UniversalKepler.Propagate(mu, rr, vv, age - shed, out pos2, out _);
            }
            set.Add(pos2, radius, tint * albedo, Double3.Zero, 1.0, 0.1f);
        }
    }

    // ------------------------------------------------------------------------------------------------------------- torus

    private static void Torus(StructureConfig c, StructureContext x, ParticleSet set)
    {
        int count = (int)c.N("count");
        double rb = x.BodyRadius, major = c.N("majorRadius") * rb, tube = c.N("tubeRadius") * rb;
        double omega = 2.0 * Math.PI / (c.N("torusPeriodHours") * 3600.0) * c.N("ageRate"), spinPhase = omega * Math.Max(x.Ut, 0.0);
        ulong seed = (ulong)c.N("seed") * 0x9E3779B97F4A7C15UL + 7UL;
        Double3 tint = c.C("coreColor"), axis = x.Axis.Normalized(), e1 = Perpendicular(axis, out Double3 e2);
        double albedo = c.N("albedo"), radius = 1.6 * Math.Sqrt(4.0 * Math.PI * Math.PI * major * tube / Math.Max(count, 1)) * c.N("sizeScale") * 0.5;
        for (int i = 0; i < count; i++)
        {
            ulong key = Rng.Key(seed, i, 0);
            double phi = 2.0 * Math.PI * Rng.U(key, 0) + spinPhase, theta = 2.0 * Math.PI * Rng.U(key, 1);
            double rr = tube * Math.Sqrt(Rng.U(key, 2));   // filled tube, denser surface
            if (Rng.U(key, 3) < 0.7) rr = tube;
            double ring = major + rr * Math.Cos(theta);
            Double3 pos = e1 * (ring * Math.Cos(phi)) + e2 * (ring * Math.Sin(phi)) + axis * (rr * Math.Sin(theta));
            set.Add(pos, radius, tint * albedo, Double3.Zero, 1.0, 0.1f);
        }
    }
}
