using System;
using System.Collections.Generic;
using KSPVE.Pure.Numerics;
using KSPVE.Pure.Space.Procedural;
using KSPVE.Pure.Space.Relativity;

namespace KSPVE.Pure.Space.Tde;

/// <summary>
/// A tidal disruption event: a star on a parabolic orbit is torn apart by a Schwarzschild black hole.
/// <para>
/// Frame and units. Lengths are in horizon radii r_s (so GM/c^2 = 1/2) and the hole's mass is r_s c^2 / 2G, the hole
/// the renderer draws and lenses. Time runs in r_s / c internally; every timescale is physical.
/// </para>
/// <para>
/// Physics.
/// The star is a polytrope (<see cref="Polytrope"/>). Its centre of mass follows the parabolic (E = 1) geodesic with
/// periapsis r_t / beta, tidal radius r_t = R* (M_bh / M*)^(1/3). Until it reaches r_t it is stretched by the
/// equilibrium tide. There it comes apart: in the frozen-in approximation (Lacy, Townes and Hollenbach 1982; Rees 1988;
/// Stone, Sari and Loeb 2013) every parcel keeps the star's velocity, so its energy differs from the centre's by the
/// potential across the star, a spread of ~ G M_bh R* / r_t^2. Each parcel then follows its own exact Schwarzschild
/// geodesic (<see cref="SchwarzschildOrbit"/>): the bound half comes back, the rest escapes.
/// Applied to the undisturbed star this returns the debris too late and too slowly, because tides distort the star
/// before it breaks up. So the energy spread and the distribution of mass over it, dM/ds ~ (1 - s^2)^w, are set from
/// hydrodynamic simulations (<see cref="DisruptionFits"/>, Guillochon and Ramirez-Ruiz 2013): the fallback peaks when
/// and as high as they find for this star, hole and beta, then declines as t^-5/3. Partial disruptions lose the mass
/// those simulations give, and the rest carries on as a core.
/// The stream is followed as a family of parcels ordered by energy, interpolated to any resolution. Its width comes
/// from tracer parcels on either side, which converge in the vertical "nozzle" at periapsis, and is otherwise held by
/// self-gravity to H ~ r^(1/4) (Coughlin et al. 2016).
/// After its first return each parcel's orbit has advanced by the relativistic periapsis shift, so its outgoing leg
/// crosses the incoming stream where its azimuth from periapsis reaches pi (Dai, McKinney and Miller 2015). There it
/// shocks. A share of the shocked gas is blown out as a quasi-spherical outflow at the local escape speed (Lu and
/// Bonnerot 2020), whose optically thick photosphere reprocesses the flare (Strubbe and Quataert 2009; Dai et al.
/// 2018: thinner along the poles). The rest forms an eccentric disk at the circularisation radius that circularises
/// and drains on its viscous time; its luminosity is capped near Eddington by advection (L = L_Edd (1 + ln mdot)).
/// </para>
/// <para>
/// The stream's surface cools from the star's photosphere to the hydrogen recombination temperature and below
/// (Kasen and Ramirez-Ruiz 2010). Returning gas is shock-heated in the nozzle.
/// </para>
/// </summary>
public sealed class TidalDisruption
{
    public const double G = 6.6743e-11;
    public const double C = 299792458.0;
    public const double SolarMass = 1.98847e30;
    public const double SolarRadius = 6.957e8;
    public const double StefanBoltzmann = 5.670374e-8;
    /// <summary>Electron-scattering opacity of solar gas, 0.2 (1 + X) cm^2/g with X = 0.7, in m^2/kg.</summary>
    public const double ScatteringOpacity = 0.034;

    private const double M = 0.5;                    // GM/c^2 in horizon radii
    private const double MinPeriapsis = 3.0;         // a parabolic orbit plunges below 2 r_s; keep clear of it
    private const int CentreMembers = 256;
    private const int WidthSlices = 64;
    private const double WidthFactor = 0.6;          // tracer offset as a fraction of the slab's radius
    private const double RecombinationTemperature = 6000.0;
    private const double ColdTemperature = 4500.0;
    private const double ReturnTemperature = 20000.0;
    private const double YoungDiskAspect = 0.5;      // H/R of a super-Eddington disk
    private const double FunnelDensity = 0.1;
    private const double MaxOpacity = 1e4;           // per horizon radius: opaque either way
    private const int GridPoints = 2048;
    private const int GridPerShell = 8;
    private const double GridSpan = 4e4;             // the light curve is followed to 80 most-bound periods
    private const int MaxSamples = 6000;
    // The outflow leaves the collision with a range of speeds around the local escape speed (Lu and Bonnerot 2020).
    private static readonly double[] SpeedSpread = { 0.35, 0.55, 0.75, 0.95, 1.15, 1.35 };
    private static readonly double[] SpeedWeight = { 0.1, 0.18, 0.22, 0.22, 0.18, 0.1 };
    private const int MaxDepth = 9;

    private sealed class Member
    {
        public double S, Mass;
        public bool Valid;
        public SchwarzschildOrbit Orbit;
        public double T0, Phi0;
        public Double3 X, Y, Z;
        public double AbsorbTime = double.PositiveInfinity;
        public double ReturnTime = double.PositiveInfinity;
        public double IntersectionRadius, RelativeSpeed;
    }

    private struct Sample
    {
        public double S, R, Heat;
        public Double3 Pos, Vel;
        public bool Valid;
        /// <summary>Which unbroken piece of the stream it is in (pieces part where it runs out past the render radius).</summary>
        public int Run;
    }

    private readonly TdeParameters _p;
    private readonly Polytrope _star;
    private readonly SchwarzschildOrbit _com;
    private Member[] _centre;
    private Member[][] _tracers;
    private readonly double _starMass, _rs;
    private readonly double _comStart, _approach;
    private readonly double _periapsisTime;   // r_s / c from the disruption to the centre of mass's periapsis
    private readonly Double3 _ex, _ey, _ez;

    // Light curve on a grid of time since disruption (r_s / c).
    private double[] _gridTime, _gridDisk, _gridAccreted, _gridEjected, _gridAbsorbed, _gridLum, _gridShockLum, _gridAccretion, _gridFallback, _gridRadius;
    private double _formationMass = double.PositiveInfinity, _jetLaunch = double.PositiveInfinity;
    private double[] _shellTime, _shellMass, _shellRadius, _shellSpeed;
    private readonly double _viscousTime;
    private readonly double _energyShape, _shapeNorm;

    private readonly List<Sample> _dense = new List<Sample>(MaxSamples);
    private readonly List<int> _keep = new List<int>(TdeFrame.MaxVertices);
    private readonly double[] _shellBins = new double[TdeFrame.EnvelopeBins];

    /// <summary>The star's orbit in the disk frame: towards periapsis, along the motion there, and the normal.</summary>
    public Double3 OrbitX => _ex;
    public Double3 OrbitY => _ey;
    public Double3 OrbitZ => _ez;
    public double HoleMassSuns { get; }
    /// <summary>Seconds per r_s / c.</summary>
    public double TimeUnit { get; }
    public double StarRadius { get; }
    public double MassRatio { get; }
    public double TidalRadius { get; }
    public double Periapsis { get; }
    public double Beta { get; }
    public double DisruptionRadius { get; }
    /// <summary>Radius (fraction of the star's) of the core that survives; 0 for a full disruption.</summary>
    public double CoreRadius { get; }
    public double StrippedFraction { get; }
    /// <summary>How much wider the debris' energy spread is than the undisturbed star's frozen-in G M R* / r_t^2: the
    /// star is stretched before it comes apart. Set so the fallback peaks when the simulations say.</summary>
    public double EnergyStretch { get; private set; }
    /// <summary>Exponent w of the debris' energy distribution, dM/ds ~ (1 - s^2)^w.</summary>
    public double EnergyShape => _energyShape;
    /// <summary>Peak fallback rate (kg/s) and its time after disruption (s) from the simulation fits.</summary>
    public double TargetPeakRate { get; }
    public double TargetPeakSeconds { get; }
    public double BoundFraction { get; private set; }
    /// <summary>Physical seconds from the start to the disruption.</summary>
    public double ApproachSeconds { get; }
    public double MostBoundPeriodSeconds { get; private set; }
    public double FirstReturnSeconds { get; private set; }
    public double PeriapsisAdvance { get; private set; }
    public double CircularizationRadius { get; }
    public double EddingtonLuminosity { get; }
    public double EddingtonAccretionRate { get; }
    public double PeakFallbackEddington { get; private set; }
    public double PeakLuminosityWatts { get; private set; }
    public double PeakSeconds { get; private set; }
    /// <summary>Physical seconds since the disruption after which nothing is left to draw.</summary>
    public double EndSeconds { get; private set; }

    public TidalDisruption(TdeParameters parameters, double horizonRadiusMeters, ulong seed)
    {
        if (horizonRadiusMeters <= 0.0) throw new ArgumentOutOfRangeException(nameof(horizonRadiusMeters));
        _p = parameters;
        _rs = horizonRadiusMeters;
        TimeUnit = _rs / C;
        double holeMass = _rs * C * C / (2.0 * G);
        HoleMassSuns = holeMass / SolarMass;
        _starMass = parameters.StarMassSuns * SolarMass;
        StarRadius = parameters.StarRadiusSuns * SolarRadius / _rs;
        MassRatio = _starMass / holeMass;
        TidalRadius = StarRadius * Math.Pow(MassRatio, -1.0 / 3.0);
        Periapsis = Math.Max(parameters.Periapsis > 0.0 ? parameters.Periapsis : TidalRadius / parameters.Beta, MinPeriapsis);
        Beta = TidalRadius / Periapsis;
        DisruptionRadius = Math.Max(TidalRadius, Periapsis);
        _star = new Polytrope(parameters.PolytropeIndex);
        double n = parameters.PolytropeIndex;
        StrippedFraction = DisruptionFits.MassLost(Beta, n);
        CoreRadius = StrippedFraction >= 0.999 ? 0.0 : RadiusEnclosing(1.0 - StrippedFraction);
        EddingtonLuminosity = 4.0 * Math.PI * G * holeMass * C / ScatteringOpacity;
        EddingtonAccretionRate = EddingtonLuminosity / (parameters.Efficiency * C * C);

        // What the simulations give for this star, hole and beta: when the fallback peaks and how high.
        double holeMillions = HoleMassSuns / 1e6;
        double m = parameters.StarMassSuns, rStar = parameters.StarRadiusSuns;
        const double year = 365.25 * 86400.0;
        double peakRateCoefficient = DisruptionFits.PeakRateCoefficient(Beta, n);
        double peakTimeCoefficient = DisruptionFits.PeakTimeCoefficient(Beta, n);
        TargetPeakRate = peakRateCoefficient * Math.Pow(holeMillions, -0.5) * m * m * Math.Pow(rStar, -1.5) * SolarMass / year;
        TargetPeakSeconds = peakTimeCoefficient * Math.Sqrt(holeMillions) / m * Math.Pow(rStar, 1.5) * year;
        _energyShape = DisruptionFits.EnergyShape(peakRateCoefficient * peakTimeCoefficient / Math.Max(StrippedFraction, 1e-6));
        _shapeNorm = DisruptionFits.ShapeNorm(_energyShape);

        // The orbit's orientation in the disk frame: Rz(node) Rx(inclination) Rz(argument of periapsis); random ones
        // are isotropic and fixed by the seed.
        var rng = new Pcg32(seed, 0x7DE5u);
        double inc = parameters.InclinationDeg >= 0.0 ? parameters.InclinationDeg * Math.PI / 180.0 : Math.Acos(1.0 - 2.0 * rng.NextDouble());
        double node = parameters.NodeDeg >= 0.0 ? parameters.NodeDeg * Math.PI / 180.0 : 2.0 * Math.PI * rng.NextDouble();
        double argp = parameters.PeriapsisAngleDeg >= 0.0 ? parameters.PeriapsisAngleDeg * Math.PI / 180.0 : 2.0 * Math.PI * rng.NextDouble();
        double co = Math.Cos(node), so = Math.Sin(node), ci = Math.Cos(inc), si = Math.Sin(inc), cw = Math.Cos(argp), sw = Math.Sin(argp);
        _ex = new Double3(co * cw - so * sw * ci, so * cw + co * sw * ci, sw * si);
        _ey = new Double3(-co * sw - so * cw * ci, -so * sw + co * cw * ci, cw * si);
        _ez = new Double3(so * si, -co * si, ci);

        // The centre of mass: parabolic, from the start radius to the disruption radius.
        double startRadius = Math.Max(parameters.StartDistance * TidalRadius, DisruptionRadius * 1.05);
        double tableRadius = Math.Max(startRadius, parameters.RenderRadius) * 1.5;
        _com = new SchwarzschildOrbit(4.0 * Periapsis, 1.0, M, tableRadius);
        double chiStart = -Math.Acos(Clamp(2.0 * Periapsis / startRadius - 1.0, -1.0, 1.0));
        double chiDisrupt = -Math.Acos(Clamp(2.0 * Periapsis / DisruptionRadius - 1.0, -1.0, 1.0));
        _comStart = _com.TimeAt(chiStart);
        _approach = _com.TimeAt(chiDisrupt) - _comStart;
        _periapsisTime = -_com.TimeAt(chiDisrupt);
        ApproachSeconds = _approach * TimeUnit;
        CircularizationRadius = CircularRadius(_com.P, M);
        double omegaC = Math.Sqrt(M / Math.Pow(CircularizationRadius, 3.0));
        _viscousTime = 1.0 / (parameters.ViscousAlpha * YoungDiskAspect * YoungDiskAspect * omegaC);
        if (StrippedFraction <= 1e-3)
        {
            EndSeconds = 50.0 * ApproachSeconds;
            AllocateGrids();
            return;
        }

        // The energy spread: the undisturbed star's frozen-in spread (energy linear in the offset along the line to the
        // hole), widened by the stretch the star has suffered, chosen so that the fallback peaks at the simulated time.
        // Newtonian first guess, then corrections from the geodesic model itself.
        double tPeakSim = TargetPeakSeconds / TimeUnit;
        double aNominal = DisruptionRadius * DisruptionRadius / (2.0 * StarRadius);
        double tMinNominal = 2.0 * Math.PI * Math.Sqrt(aNominal * aNominal * aNominal / M);
        double sPeak = DisruptionFits.PeakEnergy(_energyShape);
        double stretch = Math.Max(0.2, Math.Pow(tMinNominal / (tPeakSim * Math.Pow(sPeak, 1.5)), 2.0 / 3.0));
        for (int pass = 0; pass < 3; pass++)
        {
            BuildDebris(stretch, chiDisrupt);
            double measured = PeakFallbackTime();
            if (measured <= 0.0 || Math.Abs(measured / tPeakSim - 1.0) < 0.02) break;
            stretch *= Math.Pow(measured / tPeakSim, 2.0 / 3.0);
        }
        EnergyStretch = stretch;
        BuildLightCurve();
    }

    /// <summary>Radius (fraction of the star's) enclosing this fraction of its mass.</summary>
    private double RadiusEnclosing(double massFraction)
    {
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 50; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (_star.EnclosedMass(mid) < massFraction) lo = mid; else hi = mid;
        }
        return 0.5 * (lo + hi);
    }

    /// <summary>Debris mass per unit s: the stripped mass spread as (1 - s^2)^w.</summary>
    private double DebrisPerS(double s) => StrippedFraction * _shapeNorm * Math.Pow(Math.Max(0.0, 1.0 - s * s), _energyShape);

    /// <summary>The parcel families at the moment of disruption: centres along the line to the hole, and tracers across.</summary>
    private void BuildDebris(double stretch, double chiDisrupt)
    {
        double phiD = _com.AzimuthAt(chiDisrupt);
        var rhat = new Double3(Math.Cos(phiD), Math.Sin(phiD), 0.0);
        var phat = new Double3(-Math.Sin(phiD), Math.Cos(phiD), 0.0);
        _com.StaticVelocity(chiDisrupt, out double vr, out double vphi);
        Double3 xcm = rhat * DisruptionRadius;
        Double3 vcm = rhat * vr + phat * vphi;
        double debrisRadius = _p.RenderRadius * 1.25;
        double along = stretch * StarRadius;

        double tMin = double.PositiveInfinity, advance = 0.0, bound = 0.0;
        _centre = new Member[CentreMembers];
        double ds = 2.0 / CentreMembers;
        for (int j = 0; j < CentreMembers; j++)
        {
            double s = -1.0 + (j + 0.5) * ds;
            Member member = MakeMember(s, DebrisPerS(s) * ds, xcm + rhat * (s * along), vcm, debrisRadius);
            _centre[j] = member;
            if (!member.Valid || !member.Orbit.Bound) continue;
            bound += member.Mass;
            if (member.Orbit.RadialPeriod < tMin)
            {
                tMin = member.Orbit.RadialPeriod;
                advance = member.Orbit.AzimuthPerOrbit - 2.0 * Math.PI;
            }
        }
        BoundFraction = bound;
        PeriapsisAdvance = advance;
        MostBoundPeriodSeconds = double.IsInfinity(tMin) ? 0.0 : tMin * TimeUnit;

        _tracers = new Member[4][];
        for (int f = 0; f < 4; f++) _tracers[f] = new Member[WidthSlices];
        double dw = 2.0 / WidthSlices;
        for (int k = 0; k < WidthSlices; k++)
        {
            double s = -1.0 + (k + 0.5) * dw;
            double w = WidthFactor * StarRadius * Math.Sqrt(1.0 - s * s);
            Double3 x = xcm + rhat * (s * along);
            _tracers[0][k] = MakeMember(s, 0.0, x + phat * w, vcm, debrisRadius);
            _tracers[1][k] = MakeMember(s, 0.0, x - phat * w, vcm, debrisRadius);
            _tracers[2][k] = MakeMember(s, 0.0, x + Double3.UnitZ * w, vcm, debrisRadius);
            _tracers[3][k] = MakeMember(s, 0.0, x - Double3.UnitZ * w, vcm, debrisRadius);
        }
    }

    private List<Member> BoundMembers(bool byAbsorption)
    {
        var list = new List<Member>();
        foreach (Member m in _centre)
            if (m.Valid && m.Orbit.Bound && m.Mass > 0.0) list.Add(m);
        if (byAbsorption) list.Sort((a, b) => a.AbsorbTime.CompareTo(b.AbsorbTime));
        else list.Sort((a, b) => a.ReturnTime.CompareTo(b.ReturnTime));
        return list;
    }

    /// <summary>When (r_s / c after disruption) the mass coming back to periapsis peaks.</summary>
    private double PeakFallbackTime()
    {
        List<Member> returns = BoundMembers(false);
        if (returns.Count < 3) return 0.0;
        double t0 = returns[0].ReturnTime, best = 0.0, bestTime = 0.0;
        for (int i = 0; i <= 600; i++)
        {
            double t = t0 * Math.Pow(30.0, i / 600.0);
            double rate = Rate(returns, t, false);
            if (rate > best) { best = rate; bestTime = t; }
        }
        return bestTime;
    }

    private void AllocateGrids()
    {
        _gridTime = new double[GridPoints];
        _gridDisk = new double[GridPoints];
        _gridAccreted = new double[GridPoints];
        _gridEjected = new double[GridPoints];
        _gridAbsorbed = new double[GridPoints];
        _gridLum = new double[GridPoints];
        _gridShockLum = new double[GridPoints];
        _gridAccretion = new double[GridPoints];
        _gridFallback = new double[GridPoints];
        _gridRadius = new double[GridPoints];
        _shellTime = new double[GridPoints / GridPerShell];
        _shellMass = new double[GridPoints / GridPerShell];
        _shellRadius = new double[GridPoints / GridPerShell];
        _shellSpeed = new double[GridPoints / GridPerShell];
    }

    /// <summary>
    /// Mass reaching the self-intersection shock (and back at periapsis) against time since disruption, and what it
    /// feeds: the disk (draining on the viscous time) and the outflow's shells.
    /// </summary>
    private void BuildLightCurve()
    {
        AllocateGrids();
        List<Member> absorb = BoundMembers(true);
        List<Member> returns = BoundMembers(false);
        if (absorb.Count < 2)
        {
            EndSeconds = 50.0 * ApproachSeconds;
            return;
        }

        double tMin = MostBoundPeriodSeconds / TimeUnit;
        double first = Math.Min(absorb[0].AbsorbTime, returns[0].ReturnTime);
        FirstReturnSeconds = returns[0].ReturnTime * TimeUnit;
        double span = 0.002 * tMin;
        double fo = _p.OutflowFraction;
        double massScale = _starMass / TimeUnit; // mass fraction per r_s/c -> kg/s
        double peakL = 0.0, peakFallback = 0.0, peakTime = 0.0;
        double disk = 0.0, accreted = 0.0, absorbed = 0.0, prevRate = 0.0;
        for (int k = 0; k < GridPoints; k++)
        {
            double t = first + span * (Math.Pow(1.0 + GridSpan, (double)k / (GridPoints - 1)) - 1.0);
            _gridTime[k] = t;
            double rate = Rate(absorb, t, true);
            if (k > 0)
            {
                double dt = t - _gridTime[k - 1];
                double shockedMass = 0.5 * (rate + prevRate) * dt;
                absorbed += shockedMass;
                double inflow = shockedMass * (1.0 - fo);
                double decay = Math.Exp(-dt / _viscousTime);
                double newDisk = disk * decay + inflow / dt * _viscousTime * (1.0 - decay);
                accreted += inflow - (newDisk - disk);
                disk = newDisk;
            }
            prevRate = rate;
            _gridDisk[k] = disk;
            _gridAccreted[k] = accreted;
            _gridEjected[k] = absorbed * fo;
            _gridAbsorbed[k] = absorbed;
            _gridRadius[k] = Interpolate(absorb, t, mm => mm.IntersectionRadius);
            _gridAccretion[k] = disk / _viscousTime * massScale;

            double shocked = rate * massScale;
            double fallback = Rate(returns, t, false) * massScale;
            double v = Interpolate(absorb, t, mm => mm.RelativeSpeed) * C;
            _gridShockLum[k] = 0.25 * shocked * v * v;
            _gridFallback[k] = fallback;
            double lum = AccretionLuminosity(_gridAccretion[k]) + _gridShockLum[k];
            _gridLum[k] = lum;
            if (lum > peakL) { peakL = lum; peakTime = t; }
            peakFallback = Math.Max(peakFallback, fallback);
        }
        // The disk is whole once as much gas has come back and settled as arrives in the first most-bound period after
        // the first returns (a couple of months); the jets launch when it is half built.
        _formationMass = Math.Max(Grid(_gridAbsorbed, first + tMin), 1e-12);
        for (int k = 0; k < GridPoints; k++)
            if (_gridAbsorbed[k] >= 0.5 * _formationMass) { _jetLaunch = _gridTime[k]; break; }
        PeakLuminosityWatts = peakL;
        PeakSeconds = peakTime * TimeUnit;
        PeakFallbackEddington = peakFallback / EddingtonAccretionRate;
        EndSeconds = _gridTime[GridPoints - 1] * TimeUnit;

        for (int i = 0; i < _shellTime.Length; i++)
        {
            int a = i * GridPerShell, b = Math.Min(GridPoints - 1, a + GridPerShell);
            _shellTime[i] = 0.5 * (_gridTime[a] + _gridTime[b]);
            _shellMass[i] = _gridEjected[b] - _gridEjected[a];
            double radius = Math.Max(_gridRadius[(a + b) / 2], 2.0 * Periapsis);
            _shellRadius[i] = radius;
            _shellSpeed[i] = Math.Sqrt(2.0 * M / radius); // the local escape speed
        }
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    private static double SmoothStep(double e0, double e1, double x)
    {
        double t = Clamp((x - e0) / (e1 - e0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>Radius of the circular orbit with the angular momentum of the parabolic orbit p: L^2 = m r^2 / (r - 3m).</summary>
    private static double CircularRadius(double p, double m)
    {
        double l2 = Math.Pow(SchwarzschildOrbit.AngularMomentum(p, 1.0, m), 2.0);
        double a = l2 / m;
        return 0.5 * (a + Math.Sqrt(Math.Max(0.0, a * a - 12.0 * l2)));
    }

    private double AccretionLuminosity(double accretionKgPerS)
    {
        double l = _p.Efficiency * accretionKgPerS * C * C;
        return l <= EddingtonLuminosity ? l : EddingtonLuminosity * (1.0 + Math.Log(l / EddingtonLuminosity));
    }

    /// <summary>
    /// Mass (fraction of the star per r_s / c) reaching the shock, or periapsis, at t: dM/ds |ds/dt|. Between
    /// neighbouring parcels s(t) is taken as a local power law (late on t ~ |s|^-3/2), so the rate is smooth.
    /// </summary>
    private double Rate(List<Member> sorted, double t, bool absorption)
    {
        for (int i = 1; i < sorted.Count; i++)
        {
            double tb = absorption ? sorted[i].AbsorbTime : sorted[i].ReturnTime;
            if (t >= tb) continue;
            double ta = absorption ? sorted[i - 1].AbsorbTime : sorted[i - 1].ReturnTime;
            if (t < ta) return 0.0;
            double sa = Math.Abs(sorted[i - 1].S), sb = Math.Abs(sorted[i].S);
            double sNow, dsdt;
            if (ta > 0.0 && sa > 0.0 && sb > 0.0 && Math.Abs(Math.Log(sb / sa)) > 1e-9 && tb > ta)
            {
                double k = Math.Log(tb / ta) / Math.Log(sb / sa);
                sNow = sa * Math.Pow(t / ta, 1.0 / k);
                dsdt = Math.Abs(sNow / (k * t));
            }
            else
            {
                sNow = sa + (sb - sa) * (t - ta) / Math.Max(tb - ta, 1e-12);
                dsdt = Math.Abs(sb - sa) / Math.Max(tb - ta, 1e-12);
            }
            return DebrisPerS(sNow) * dsdt;
        }
        return 0.0;
    }

    private static double Interpolate(List<Member> sorted, double t, Func<Member, double> value)
    {
        if (t <= sorted[0].AbsorbTime) return value(sorted[0]);
        for (int i = 1; i < sorted.Count; i++)
        {
            if (t > sorted[i].AbsorbTime) continue;
            double f = (t - sorted[i - 1].AbsorbTime) / Math.Max(sorted[i].AbsorbTime - sorted[i - 1].AbsorbTime, 1e-12);
            return value(sorted[i - 1]) + (value(sorted[i]) - value(sorted[i - 1])) * f;
        }
        return value(sorted[sorted.Count - 1]);
    }

    private Member MakeMember(double s, double mass, Double3 x, Double3 v, double maxRadius)
    {
        var member = new Member { S = s, Mass = mass };
        double r = x.Length;
        double gamma = 1.0 / Math.Sqrt(1.0 - v.LengthSquared);
        double energy = gamma * Math.Sqrt(1.0 - 2.0 * M / r);
        Double3 rhat = x / r;
        double vr = Double3.Dot(v, rhat);
        double angularMomentum = gamma * r * (v - rhat * vr).Length;
        if (!SchwarzschildOrbit.TryElements(energy, angularMomentum, M, out double p, out double e)) return member;
        member.Orbit = new SchwarzschildOrbit(p, e, M, maxRadius);
        double chi0 = Math.Acos(Clamp((p * M / r - 1.0) / e, -1.0, 1.0)) * (vr < 0.0 ? -1.0 : 1.0);
        member.T0 = member.Orbit.TimeAt(chi0);
        member.Phi0 = member.Orbit.AzimuthAt(chi0);
        member.X = rhat;
        member.Z = Double3.Cross(x, v).Normalized();
        member.Y = Double3.Cross(member.Z, member.X);
        member.Valid = true;
        if (member.Orbit.Bound)
        {
            double chiCross = member.Orbit.ChiAtAzimuth(Math.PI);
            member.ReturnTime = member.Orbit.RadialPeriod - member.T0;
            member.AbsorbTime = member.Orbit.RadialPeriod + member.Orbit.TimeAt(chiCross) - member.T0;
            member.IntersectionRadius = member.Orbit.Radius(chiCross);
            member.Orbit.StaticVelocity(chiCross, out double vrs, out _);
            member.RelativeSpeed = 2.0 * Math.Abs(vrs);
        }
        return member;
    }

    private Double3 ToDisk(Double3 v) => _ex * v.X + _ey * v.Y + _ez * v.Z;

    /// <summary>Where the star's centre of mass is t (r_s / c) after the start, and its velocity (orbit frame).</summary>
    private void CentreOfMass(double t, out Double3 pos, out Double3 vel)
    {
        double chi = _com.ChiAtTime(_comStart + t, out _);
        double r = _com.Radius(chi), phi = _com.AzimuthAt(chi);
        var rhat = new Double3(Math.Cos(phi), Math.Sin(phi), 0.0);
        var phat = new Double3(-Math.Sin(phi), Math.Cos(phi), 0.0);
        _com.StaticVelocity(chi, out double vr, out double vphi);
        pos = rhat * r;
        vel = rhat * vr + phat * vphi;
    }

    /// <summary>
    /// A parcel at material coordinate s, tau (r_s / c) after the disruption, interpolated between the two family
    /// members around s: their tabulated t(chi) and phi(chi) are blended node by node, then inverted.
    /// </summary>
    private static Sample Evaluate(Member[] family, double s, double tau)
    {
        var sample = new Sample { S = s };
        int n = family.Length;
        double x = (s + 1.0) * n / 2.0 - 0.5;
        int j = (int)Math.Floor(x);
        if (j < 0) j = 0;
        if (j > n - 2) j = n - 2;
        double f = Clamp(x - j, 0.0, 1.0);
        Member a = family[j], b = family[j + 1];
        if (!a.Valid || !b.Valid) return sample;
        if (a.Orbit.Bound != b.Orbit.Bound)
        {
            // Either side of zero energy the tables do not blend (one repeats, one runs off): take the nearer parcel.
            if (f < 0.5) b = a; else a = b;
            f = 0.0;
        }
        SchwarzschildOrbit oa = a.Orbit, ob = b.Orbit;
        double p = oa.P + (ob.P - oa.P) * f;
        double e = oa.Eccentricity + (ob.Eccentricity - oa.Eccentricity) * f;
        double chiEnd = oa.ChiEnd + (ob.ChiEnd - oa.ChiEnd) * f;
        bool bound = oa.Bound && ob.Bound;
        double[] ta = oa.Time, tb = ob.Time, pa = oa.Azimuth, pb = ob.Azimuth;
        int cells = SchwarzschildOrbit.Cells;
        double tEnd = ta[cells] + (tb[cells] - ta[cells]) * f;
        double phiEnd = pa[cells] + (pb[cells] - pa[cells]) * f;

        double target = a.T0 + (b.T0 - a.T0) * f + tau;
        double laps = 0.0;
        if (bound)
        {
            double period = 2.0 * tEnd;
            laps = Math.Floor((target + 0.5 * period) / period);
            target -= laps * period;
        }
        double sign = target < 0.0 ? -1.0 : 1.0;
        double at = Math.Abs(target);
        if (at >= tEnd)
        {
            if (!bound) return sample; // escaped past the end of the table
            at = tEnd;
        }
        int lo = 0, hi = cells;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            double tm = ta[mid] + (tb[mid] - ta[mid]) * f;
            if (tm <= at) lo = mid; else hi = mid;
        }
        double t0 = ta[lo] + (tb[lo] - ta[lo]) * f, t1 = ta[lo + 1] + (tb[lo + 1] - ta[lo + 1]) * f;
        double w = t1 > t0 ? Clamp((at - t0) / (t1 - t0), 0.0, 1.0) : 0.0;
        double u0 = 1.0 - (double)lo / cells, u1 = 1.0 - (double)(lo + 1) / cells;
        double chiR = sign * chiEnd * ((1.0 - u0 * u0) + ((1.0 - u1 * u1) - (1.0 - u0 * u0)) * w);
        double phi0 = pa[lo] + (pb[lo] - pa[lo]) * f, phi1 = pa[lo + 1] + (pb[lo + 1] - pa[lo + 1]) * f;
        double phi = laps * 2.0 * phiEnd + sign * (phi0 + (phi1 - phi0) * w);
        double chi = chiR + 2.0 * Math.PI * laps;

        double angle = phi - (a.Phi0 + (b.Phi0 - a.Phi0) * f);
        Double3 z = (a.Z + (b.Z - a.Z) * f).Normalized();
        Double3 xa = a.X + (b.X - a.X) * f;
        Double3 xb = (xa - z * Double3.Dot(xa, z)).Normalized();
        Double3 yb = Double3.Cross(z, xb);
        double cosChi = Math.Cos(chi);
        double r = p * M / (1.0 + e * cosChi);
        Double3 rhat = xb * Math.Cos(angle) + yb * Math.Sin(angle);
        sample.Pos = rhat * r;
        sample.R = r;

        // Static-frame velocity from Darwin's dt/dchi, dphi/dchi and dr/dchi.
        double q = 1.0 + e * cosChi;
        double denom = p - 6.0 - 2.0 * e * cosChi;
        double dtdchi = p * p * M / ((p - 2.0 - 2.0 * e * cosChi) * q * q) * Math.Sqrt(((p - 2.0) * (p - 2.0) - 4.0 * e * e) / denom);
        double dphidchi = Math.Sqrt(p / denom);
        double drdchi = p * M * e * Math.Sin(chi) / (q * q);
        double lapse = 1.0 - 2.0 * M / r;
        double vr = drdchi / dtdchi / lapse;
        double vphi = r * dphidchi / dtdchi / Math.Sqrt(lapse);
        sample.Vel = rhat * vr + Double3.Cross(z, rhat) * vphi;
        // Back through periapsis after the first return: shock-heated in the nozzle.
        sample.Heat = bound ? SmoothStep(2.0 * Math.PI - 0.25, 2.0 * Math.PI + 0.05, chi) : 0.0;
        sample.Valid = true;
        return sample;
    }

    private double AbsorbTimeAt(double s)
    {
        int n = _centre.Length;
        double x = (s + 1.0) * n / 2.0 - 0.5;
        int j = (int)Math.Floor(x);
        if (j < 0) j = 0;
        if (j > n - 2) j = n - 2;
        double f = Clamp(x - j, 0.0, 1.0);
        double a = _centre[j].AbsorbTime, b = _centre[j + 1].AbsorbTime;
        if (double.IsInfinity(a) || double.IsInfinity(b)) return f < 0.5 ? a : b;
        return a + (b - a) * f;
    }

    /// <summary>The event <paramref name="seconds"/> of physical time after the star started falling in.</summary>
    public void Evaluate(double seconds, TdeFrame frame)
    {
        frame.Clear();
        frame.OrbitNormal = _ez;
        double t = seconds / TimeUnit;
        if (t < 0.0) return;
        double tau = t - _approach;
        frame.Active = true;
        frame.SecondsSinceDisruption = tau * TimeUnit;
        frame.OrbitNormal = _ez;
        double extent = 0.0;

        if (tau < 0.0)
        {
            frame.Phase = TdePhase.Approach;
            CentreOfMass(t, out Double3 pos, out Double3 vel);
            double r = pos.Length;
            // The equilibrium tide stretches the star along the line to the hole, volume conserved.
            double stretch = Math.Min(0.5 * Math.Pow(TidalRadius / r, 3.0), 0.6);
            frame.StarVisible = true;
            frame.StarCenter = ToDisk(pos);
            frame.StarAxis = ToDisk(pos / r);
            frame.StarVelocity = ToDisk(vel);
            frame.StarLong = StarRadius * Math.Pow(1.0 + stretch, 2.0 / 3.0);
            frame.StarShort = StarRadius * Math.Pow(1.0 + stretch, -1.0 / 3.0);
            frame.StarTemperature = _p.StarTemperature;
            frame.StarPresence = 1.0;
            // Gas starts pouring off the tips (through the inner and outer Lagrange points) just before the tidal radius.
            frame.StarTail = SmoothStep(0.75, 1.0, TidalRadius / r);
            frame.Extent = r + 8.0 * frame.StarLong + 2.0;
            return;
        }

        double dynamical = Math.Sqrt(Math.Pow(DisruptionRadius, 3.0) / M);
        double handover = 0.4 * dynamical;
        if (tau < handover && CoreRadius <= 0.02)
        {
            // The star dissolves into the stream over a fraction of a dynamical time.
            CentreOfMass(t, out Double3 pos, out Double3 vel);
            double r = pos.Length;
            double grow = 1.0 + 2.0 * tau / handover;
            frame.StarVisible = true;
            frame.StarCenter = ToDisk(pos);
            frame.StarAxis = ToDisk(pos / r);
            frame.StarVelocity = ToDisk(vel);
            frame.StarLong = StarRadius * Math.Pow(1.5, 2.0 / 3.0) * grow;
            frame.StarShort = StarRadius * Math.Pow(1.5, -1.0 / 3.0) / Math.Sqrt(grow);
            frame.StarTemperature = _p.StarTemperature;
            frame.StarPresence = 1.0 - SmoothStep(0.0, handover, tau);
            frame.StarTail = 1.0;
            extent = Math.Max(extent, r + 8.0 * frame.StarLong);
        }
        if (CoreRadius > 0.02)
        {
            // A partial disruption: the core carries on along the star's orbit.
            CentreOfMass(t, out Double3 pos, out Double3 vel);
            double radius = CoreRadius * StarRadius;
            frame.StarVisible = true;
            frame.StarCenter = ToDisk(pos);
            frame.StarAxis = ToDisk(pos.Normalized());
            frame.StarVelocity = ToDisk(vel);
            frame.StarLong = frame.StarShort = radius;
            frame.StarTemperature = _p.StarTemperature;
            frame.StarPresence = 1.0;
            frame.StarTail = 1.0 - SmoothStep(0.0, 4.0 * dynamical, tau);
            extent = Math.Max(extent, pos.Length + 4.0 * radius);
        }

        if (_centre != null) BuildStream(tau, dynamical, frame, ref extent);
        Flash(t, tau, frame, ref extent);
        LightCurve(tau, frame, ref extent);

        double tMin = MostBoundPeriodSeconds / TimeUnit;
        if (tau < 5.0 * dynamical) frame.Phase = TdePhase.Disruption;
        else if (_gridTime.Length == 0 || tMin <= 0.0 || tau < _gridTime[0]) frame.Phase = TdePhase.Stream;
        else if (frame.LuminosityWatts > 0.05 * PeakLuminosityWatts) frame.Phase = TdePhase.Flare;
        else if (tau * TimeUnit < EndSeconds) frame.Phase = TdePhase.Fading;
        else frame.Phase = TdePhase.Over;

        frame.Extent = extent + 2.0;
    }

    private void BuildStream(double tau, double dynamical, TdeFrame frame, ref double extent)
    {
        // The most bound debris is gone once it has hit the incoming stream: the stream starts at that crossing.
        double sStart = double.NaN;
        bool cut = false;
        int first = 0;
        while (first < _centre.Length && (!_centre[first].Valid || _centre[first].Mass <= 0.0)) first++;
        if (first >= _centre.Length - 1) return;
        int j = first;
        while (j < _centre.Length && _centre[j].Valid && _centre[j].AbsorbTime <= tau) j++;
        if (j >= _centre.Length - 1) return; // everything bound has been absorbed and the rest is gone
        if (j > first)
        {
            double ta = _centre[j - 1].AbsorbTime, tb = _centre[j].AbsorbTime;
            double f = double.IsInfinity(tb) ? 1.0 : Clamp((tau - ta) / Math.Max(tb - ta, 1e-12), 0.0, 1.0);
            sStart = _centre[j - 1].S + (_centre[j].S - _centre[j - 1].S) * f;
            cut = true;
        }
        else
        {
            sStart = _centre[first].S;
        }

        // Sample it from that end. Where it runs out past the render radius (the bound debris swings out further than
        // that), it breaks into pieces: the part coming back past the hole stays drawn.
        _dense.Clear();
        double renderRadius = _p.RenderRadius;
        Sample start = Evaluate(_centre, sStart, tau);
        Sample previous = default;
        bool havePrevious = false, broken = false;
        int run = 0;
        for (int i = Math.Max(j, first) - 1; i < _centre.Length && _dense.Count < MaxSamples; i++)
        {
            Sample next;
            if (i < Math.Max(j, first)) next = start;
            else if (_centre[i].S <= sStart) continue;
            else next = Evaluate(_centre, _centre[i].S, tau);
            if (!next.Valid || next.R > renderRadius)
            {
                broken = true;
                continue;
            }
            if (havePrevious && broken) run++;
            next.Run = run;
            if (havePrevious && !broken) Refine(previous, next, tau, 0);
            _dense.Add(next);
            previous = next;
            havePrevious = true;
            broken = false;
        }
        if (_dense.Count < 2) return;

        Compress();
        int count = _keep.Count;
        double appear = CoreRadius > 0.02 ? 1.0 : SmoothStep(0.0, 0.4 * dynamical, tau);
        double tMin = MostBoundPeriodSeconds / TimeUnit;
        double surface = ColdTemperature + (RecombinationTemperature - ColdTemperature) * Math.Exp(-tau / Math.Max(0.5 * tMin, dynamical))
                         + (_p.StarTemperature - RecombinationTemperature) * Math.Exp(-tau / (1.5 * dynamical));
        for (int v = 0; v < count; v++)
        {
            int d = _keep[v];
            Sample sm = _dense[d];
            int dl = d > 0 && _dense[d - 1].Run == sm.Run ? d - 1 : d;
            int dr = d + 1 < _dense.Count && _dense[d + 1].Run == sm.Run ? d + 1 : d;
            Double3 tangent = (_dense[dr].Pos - _dense[dl].Pos);
            double dsdl = _dense[dr].S - _dense[dl].S;
            double stretch = dsdl > 0.0 ? tangent.Length / dsdl : StarRadius; // horizon radii per unit s
            tangent = tangent.Normalized();
            if (tangent.LengthSquared == 0.0) tangent = sm.Vel.Normalized();

            // Width: the tracers' spread across the stream, held to H ~ r^(1/4) by self-gravity.
            double slab = WidthFactor * StarRadius * Math.Sqrt(Math.Max(1.0 - sm.S * sm.S, 0.02));
            double held = slab * Math.Pow(sm.R / DisruptionRadius, 0.25);
            Double3 vertical = (Double3.UnitZ - tangent * tangent.Z).Normalized();
            Double3 across = Double3.Cross(tangent, vertical);
            double wa = held, wz = held;
            Sample p0 = Evaluate(_tracers[0], sm.S, tau), p1 = Evaluate(_tracers[1], sm.S, tau);
            Sample p2 = Evaluate(_tracers[2], sm.S, tau), p3 = Evaluate(_tracers[3], sm.S, tau);
            if (p0.Valid && p1.Valid && p2.Valid && p3.Valid)
            {
                Double3 ot = (p0.Pos - p1.Pos) * 0.5, oz = (p2.Pos - p3.Pos) * 0.5;
                double ballisticA = Math.Sqrt(Sq(Double3.Dot(ot, across)) + Sq(Double3.Dot(oz, across)));
                double ballisticZ = Math.Sqrt(Sq(Double3.Dot(ot, vertical)) + Sq(Double3.Dot(oz, vertical)));
                wa = Clamp(ballisticA, 0.05 * held, held);
                wz = Clamp(ballisticZ, 0.03 * held, held);
            }

            // Mass per length -> density -> optical depth (electron scattering), in real units.
            double perS = DebrisPerS(sm.S);
            double lineDensity = perS / Math.Max(stretch, 1e-9) * _starMass / _rs; // kg per metre
            double density = lineDensity / (Math.PI * wa * wz * _rs * _rs);
            double opacity = Math.Min(ScatteringOpacity * density * _rs, MaxOpacity);

            double temperature = surface;
            if (sm.Heat > 0.0) temperature = Math.Max(temperature, ReturnTemperature * sm.Heat);
            temperature *= Math.Min(Math.Pow(held / wz, 1.0 / 3.0), 5.0); // compression in the nozzle

            double fade = (1.0 - SmoothStep(0.75 * renderRadius, renderRadius, sm.R)) * appear;
            if (cut && d == 0 && start.Valid && start.R <= renderRadius) fade = 0.0;
            frame.Break[v] = v > 0 && _dense[_keep[v - 1]].Run != sm.Run;
            opacity *= appear;

            frame.Position[v] = ToDisk(sm.Pos);
            frame.Velocity[v] = ToDisk(sm.Vel);
            frame.WidthA[v] = wa;
            frame.WidthZ[v] = wz;
            frame.Material[v] = sm.S;
            frame.Opacity[v] = opacity;
            frame.Temperature[v] = temperature;
            frame.Brightness[v] = fade;
            extent = Math.Max(extent, sm.R + 3.0 * Math.Max(wa, wz));
        }
        frame.VertexCount = count;
        frame.ClumpGrowth = 1.0 - Math.Exp(-tau / (10.0 * dynamical));

        // Chunks of segments, each in a bounding sphere, so the shader only tests segments near the ray.
        int segments = count - 1;
        frame.ChunkCount = Math.Min(TdeFrame.MaxChunks, (segments + TdeFrame.ChunkSegments - 1) / TdeFrame.ChunkSegments);
        for (int c = 0; c < frame.ChunkCount; c++)
        {
            int v0 = c * TdeFrame.ChunkSegments, v1 = Math.Min(v0 + TdeFrame.ChunkSegments, count - 1);
            Double3 lo = frame.Position[v0], hi = frame.Position[v0];
            double widest = 0.0;
            for (int v = v0; v <= v1; v++)
            {
                Double3 q = frame.Position[v];
                lo = new Double3(Math.Min(lo.X, q.X), Math.Min(lo.Y, q.Y), Math.Min(lo.Z, q.Z));
                hi = new Double3(Math.Max(hi.X, q.X), Math.Max(hi.Y, q.Y), Math.Max(hi.Z, q.Z));
                widest = Math.Max(widest, Math.Max(frame.WidthA[v], frame.WidthZ[v]));
            }
            Double3 centre = (lo + hi) * 0.5;
            double radius = 0.0;
            for (int v = v0; v <= v1; v++) radius = Math.Max(radius, (frame.Position[v] - centre).Length);
            frame.ChunkCenter[c] = centre;
            frame.ChunkRadius[c] = radius + 4.0 * widest; // the stream's faint halo reaches four widths
        }

        if (cut && start.Valid && start.R <= renderRadius)
        {
            frame.ShockVisible = true;
            frame.ShockPosition = ToDisk(start.Pos);
            frame.ShockDirection = ToDisk(start.Vel).Normalized();
        }
    }

    private static double Sq(double v) => v * v;

    /// <summary>
    /// A deep encounter crushes the star into a pancake as it passes periapsis; the compression drives a shock out
    /// through it, which breaks out as a brief, very bright flash (Carter and Luminet 1983; Guillochon et al. 2009).
    /// It lasts about the periapsis crossing time and comes from gas the size of the star. Only for beta above ~3.
    /// </summary>
    private void Flash(double t, double tau, TdeFrame frame, ref double extent)
    {
        double depth = Clamp((Beta - 2.0) / 3.0, 0.0, 1.0);
        if (depth <= 0.0 || _centre == null) return;
        double crossing = Math.Sqrt(Math.Pow(Periapsis, 3.0) / M);
        double since = tau - _periapsisTime;
        double burst = Math.Exp(-Sq(since / crossing));
        if (burst < 1e-3) return;
        CentreOfMass(t, out Double3 pos, out _);
        frame.FlashVisible = true;
        frame.FlashPosition = ToDisk(pos);
        frame.FlashRadius = StarRadius;
        frame.FlashStrength = 4.0 * depth * burst;
        extent = Math.Max(extent, pos.Length + 4.0 * StarRadius + 2.0);
    }

    /// <summary>Adds samples between a and b until neighbours are close compared with their distance from the hole.</summary>
    private void Refine(Sample a, Sample b, double tau, int depth)
    {
        if (depth >= MaxDepth || _dense.Count >= MaxSamples) return;
        double separation = (a.Pos - b.Pos).Length;
        double near = Math.Min(a.R, b.R);
        if (separation <= 0.03 * near + 0.25 * StarRadius) return;
        Sample mid = Evaluate(_centre, 0.5 * (a.S + b.S), tau);
        if (!mid.Valid || mid.R > _p.RenderRadius) return;
        Refine(a, mid, tau, depth + 1);
        _dense.Add(mid);
        Refine(mid, b, tau, depth + 1);
    }

    /// <summary>
    /// Picks at most MaxVertices of the dense samples, top-down: keep the ends, then keep splitting the span whose
    /// worst sample strays furthest from its chord (measured against its distance from the hole, so it reads as an
    /// angle from nearby viewpoints).
    /// </summary>
    private void Compress()
    {
        _keep.Clear();
        int n = _dense.Count;
        var spans = new List<(int A, int B, int Worst, double Error)>();
        int runs = 0;
        for (int a = 0; a < n;)
        {
            int b = a;
            while (b + 1 < n && _dense[b + 1].Run == _dense[a].Run) b++;
            if (b > a)
            {
                spans.Add(Span(a, b));
                runs++;
            }
            a = b + 1;
        }
        while (spans.Count + runs < TdeFrame.MaxVertices)
        {
            int best = -1;
            double error = 0.0;
            for (int i = 0; i < spans.Count; i++)
                if (spans[i].Error > error) { error = spans[i].Error; best = i; }
            if (best < 0 || error < 1e-4) break;
            (int a, int b, int w, _) = spans[best];
            spans[best] = Span(a, w);
            spans.Insert(best + 1, Span(w, b));
        }
        spans.Sort((x, y) => x.A.CompareTo(y.A));
        for (int i = 0; i < spans.Count; i++)
        {
            _keep.Add(spans[i].A);
            bool lastOfRun = i + 1 == spans.Count || _dense[spans[i + 1].A].Run != _dense[spans[i].A].Run;
            if (lastOfRun) _keep.Add(spans[i].B);
        }
    }

    private (int, int, int, double) Span(int a, int b)
    {
        Double3 pa = _dense[a].Pos, pb = _dense[b].Pos;
        Double3 ab = pb - pa;
        double ab2 = ab.LengthSquared;
        int worst = (a + b) / 2;
        double error = 0.0;
        for (int i = a + 1; i < b; i++)
        {
            Double3 pi = _dense[i].Pos;
            double t = ab2 > 0.0 ? Clamp(Double3.Dot(pi - pa, ab) / ab2, 0.0, 1.0) : 0.0;
            double d = (pa + ab * t - pi).Length / (0.01 * _dense[i].R + StarRadius);
            if (d > error) { error = d; worst = i; }
        }
        return b - a < 2 ? (a, b, a, 0.0) : (a, b, worst, error);
    }

    private double Grid(double[] table, double tau)
    {
        if (tau <= _gridTime[0]) return tau < _gridTime[0] ? 0.0 : table[0];
        int last = _gridTime.Length - 1;
        if (tau >= _gridTime[last]) return table[last];
        int lo = 0, hi = last;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (_gridTime[mid] <= tau) lo = mid; else hi = mid;
        }
        double f = (tau - _gridTime[lo]) / (_gridTime[hi] - _gridTime[lo]);
        return table[lo] + (table[hi] - table[lo]) * f;
    }

    private void LightCurve(double tau, TdeFrame frame, ref double extent)
    {
        if (_gridTime.Length == 0 || _gridTime[0] <= 0.0 || tau < _gridTime[0]) return;
        double accretion = Grid(_gridAccretion, tau);
        double shockLum = Grid(_gridShockLum, tau);
        double lum = Grid(_gridLum, tau);
        frame.LuminosityWatts = lum;
        frame.Luminosity = lum / EddingtonLuminosity;
        frame.AccretionEddington = accretion / EddingtonAccretionRate;
        frame.FallbackEddington = Grid(_gridFallback, tau) / EddingtonAccretionRate;
        frame.AccretedFraction = Grid(_gridAccreted, tau);
        frame.EjectedFraction = Grid(_gridEjected, tau);
        double radiusI = Grid(_gridRadius, tau);

        if (frame.ShockVisible && shockLum > 0.0)
        {
            double w = frame.VertexCount > 0 ? Math.Max(frame.WidthA[0], frame.WidthZ[0]) : StarRadius;
            frame.ShockRadius = Math.Max(4.0 * w, 0.015 * radiusI);
            frame.ShockLuminosity = shockLum / EddingtonLuminosity;
            double area = 4.0 * Math.PI * Sq(frame.ShockRadius * _rs);
            frame.ShockTemperature = Clamp(Math.Pow(shockLum / (StefanBoltzmann * area), 0.25), 6000.0, 3e5);
        }
        else
        {
            frame.ShockVisible = false;
        }

        // The disk: the shocked gas settles at the circularisation radius and spreads in and out from there; it is
        // whole once the first most-bound period's worth of gas has come back. Radiation pressure puffs it up while it
        // is fed above Eddington (H/R ~ mdot, a slim disk), and the flare brightens it.
        frame.DiskFormation = Clamp(Grid(_gridAbsorbed, tau) / _formationMass, 0.0, 1.0);
        frame.DiskFormRadius = CircularizationRadius;
        frame.DiskGlow = 1.0 + 0.4 * Math.Log(1.0 + AccretionLuminosity(accretion) / EddingtonLuminosity);
        frame.DiskPuff = 1.0 + 0.8 * Clamp(Math.Log10(1.0 + frame.AccretionEddington), 0.0, 1.5);
        frame.JetAge = tau - _jetLaunch;
        double effective = AccretionLuminosity(accretion) / (_p.Efficiency * C * C); // advection caps what radiates
        double rin = 3.0 * _rs;
        frame.DiskInnerTemperature = Math.Pow(3.0 * G * (_rs * C * C / (2.0 * G)) * effective / (8.0 * Math.PI * StefanBoltzmann * rin * rin * rin), 0.25);

        Envelope(tau, lum, frame, ref extent);
    }

    /// <summary>
    /// The outflow: shells launched from the self-intersection at the local escape speed, binned by radius into a
    /// density profile; electron scattering gives its optical depth, and an Eddington grey atmosphere its temperature
    /// (T^4 = 3/4 T_eff^4 (tau + 2/3), T_eff at the photosphere tau = 2/3 from the event's luminosity).
    /// </summary>
    private void Envelope(double tau, double lum, TdeFrame frame, ref double extent)
    {
        int bins = TdeFrame.EnvelopeBins;
        double outer = 0.0;
        for (int i = 0; i < _shellTime.Length && _shellTime[i] <= tau; i++)
            if (_shellMass[i] > 0.0) outer = Math.Max(outer, _shellRadius[i] + _shellSpeed[i] * SpeedSpread[SpeedSpread.Length - 1] * (tau - _shellTime[i]));
        if (outer <= 0.0) return;
        outer = Math.Min(outer * 1.1, _p.RenderRadius);
        double inner = Math.Max(2.0, 0.5 * Periapsis);
        if (outer <= inner * 1.5) return;
        double span = Math.Log(outer / inner);
        Array.Clear(_shellBins, 0, bins);
        for (int i = 0; i < _shellTime.Length && _shellTime[i] <= tau; i++)
        {
            if (_shellMass[i] <= 0.0) continue;
            for (int v = 0; v < SpeedSpread.Length; v++)
            {
                double r = _shellRadius[i] + _shellSpeed[i] * SpeedSpread[v] * (tau - _shellTime[i]);
                if (r >= outer) continue;
                double x = Math.Log(r / inner) / span * (bins - 1);
                int b = (int)Math.Floor(x);
                if (b < 0) continue;
                double f = x - b, mass = _shellMass[i] * SpeedWeight[v];
                _shellBins[b] += mass * (1.0 - f);
                if (b + 1 < bins) _shellBins[b + 1] += mass * f;
            }
        }
        // Mass per bin -> density -> scattering opacity per horizon radius, smoothed over neighbouring bins.
        double[] k = frame.EnvelopeOpacity;
        for (int b = 0; b < bins; b++)
        {
            double r = inner * Math.Exp(span * b / (bins - 1));
            double dr = r * span / (bins - 1);
            double density = _shellBins[b] * _starMass / (4.0 * Math.PI * r * r * dr * _rs * _rs * _rs);
            k[b] = ScatteringOpacity * density * _rs;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            double prev = k[0];
            for (int b = 0; b < bins; b++)
            {
                double next = b + 1 < bins ? k[b + 1] : k[b];
                double here = k[b];
                k[b] = 0.25 * prev + 0.5 * here + 0.25 * next;
                prev = here;
            }
        }
        // Optical depth inward from the outside; the photosphere where it reaches 2/3.
        double depth = 0.0, photosphere = inner;
        double[] temperature = frame.EnvelopeTemperature;
        var depthAt = new double[bins];
        for (int b = bins - 1; b >= 0; b--)
        {
            double r = inner * Math.Exp(span * b / (bins - 1));
            double dr = r * span / (bins - 1);
            depth += k[b] * dr;
            depthAt[b] = depth;
            if (depth >= 2.0 / 3.0 && photosphere == inner) photosphere = r;
        }
        double tEff = Math.Pow(lum / (4.0 * Math.PI * StefanBoltzmann * Sq(photosphere * _rs)), 0.25);
        for (int b = 0; b < bins; b++)
            temperature[b] = Clamp(tEff * Math.Pow(0.75 * (depthAt[b] + 2.0 / 3.0), 0.25), 2000.0, 1e6);
        frame.PhotosphereMeters = depth >= 2.0 / 3.0 ? photosphere * _rs : 0.0;
        frame.PhotosphereTemperature = tEff;
        frame.EnvelopeVisible = depth > 1e-3;
        frame.EnvelopeInner = inner;
        frame.EnvelopeLogSpan = span;
        frame.EnvelopeFunnel = FunnelDensity;
        if (frame.EnvelopeVisible) extent = Math.Max(extent, outer);
    }
}
