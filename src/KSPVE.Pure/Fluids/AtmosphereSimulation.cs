using System;

namespace KSPVE.Pure.Fluids;

/// <summary>
/// One body's whole atmosphere: the barotropic flow (<see cref="BarotropicModel"/>), the temperature anomalies and cloud
/// decks it carries (<see cref="TracerGrid"/>) and the colour table the renderer needs (<see cref="EmissionLut"/>). It is
/// spun up once to a statistically steady state, then <see cref="AdvanceTo"/> follows the game's clock.
/// Time is the model's own, seconds since the spin-up ended; the caller maps game time onto it.
/// </summary>
public sealed class AtmosphereSimulation
{
    public readonly FluidProfile Profile;
    public readonly DeckSpec[] DeckSpecs;
    public readonly BarotropicModel Model;
    public readonly TracerGrid Tracers;
    public readonly EmissionLut Lut, LutThermalInfrared;
    public readonly TracerForcing Forcing;
    public readonly double Radius, Omega, Gravity;
    /// <summary>The temperature of each deck's centre, K, and of the clear gas under them.</summary>
    public readonly double[] DeckKelvin;
    public readonly double DeepKelvin;
    /// <summary>The reference temperature the anomalies are measured from (the effective temperature, or for a hot Jupiter the planet-wide mean).</summary>
    public readonly double ReferenceKelvin;
    /// <summary>Longitude of the sub-stellar point in the planet's frame (a tidally locked body's fixed day side), rad.</summary>
    public readonly double SubstellarLongitude;

    private readonly double _power, _drag;
    private bool _spun;

    public const int DeckCount = 3;

    /// <summary>
    /// A cool giant's weather is driven by far more than 3e-4 of its small internal heat flux: moist convection releases latent
    /// heat and the Sun heats the cloud tops. Jupiter's jets (50 to 150 m/s against a drag time of some 3e7 s) need about
    /// 6e-5 W/kg, a thousand and more times what the heat flux estimate gives at 124 K, so the estimate is raised by this factor.
    /// </summary>
    public const double GiantStirringBoost = 1500.0;

    /// <summary>Elapsed model time, s (zero at the end of the spin-up).</summary>
    public double Time => Model.Time - _t0;
    private double _t0;

    public bool IsSpunUp => _spun;
    public long Steps => Model.Steps;

    /// <param name="profile">The body's resolved profile (kind, temperature, resolution, seed ...).</param>
    /// <param name="radius">m.</param>
    /// <param name="rotationPeriod">The sidereal rotation period, s (its sign is ignored: the flow is computed in the turning frame).</param>
    /// <param name="gravity">Surface gravity, m/s^2.</param>
    /// <param name="substellarLongitude">For a tidally locked body: the longitude below the star, rad.</param>
    public AtmosphereSimulation(FluidProfile profile, double radius, double rotationPeriod, double gravity, double substellarLongitude = 0.0)
    {
        Profile = profile;
        Radius = radius;
        Gravity = gravity > 0.0 ? gravity : FluidPhysics.DwarfGravity;
        Omega = 2.0 * Math.PI / Math.Max(Math.Abs(rotationPeriod), 600.0);
        SubstellarLongitude = substellarLongitude;
        DeckSpecs = Decks.For(profile.Kind);
        bool hot = profile.Kind == FluidKind.HotJupiter;
        double nightKelvin = 0.45 * profile.Temperature;
        // The planet-wide mean emission: (T_day^4 + T_night^4) / 2, to the quarter power, for a hot Jupiter.
        ReferenceKelvin = hot ? Math.Pow(0.5 * (Math.Pow(profile.Temperature, 4.0) + Math.Pow(nightKelvin, 4.0)), 0.25) : profile.Temperature;

        var flow = new FlowParameters
        {
            Radius = radius,
            Omega = Omega,
            DragTime = profile.DragTime > 0.0 ? profile.DragTime : FluidPhysics.DragTime(ReferenceKelvin, Gravity),
            ForcingPower = FluidPhysics.StirringPower(ReferenceKelvin, Gravity) * profile.Forcing * (profile.Kind == FluidKind.Giant ? GiantStirringBoost : 1.0),
            Seed = profile.Seed == 0 ? 1UL : profile.Seed
        };
        // Stirring at the convective scale: well below the Rhines scale, so the inverse cascade has room to make jets.
        int k = Math.Max(12, Math.Min(profile.Resolution, 106));
        flow.ForcingLMin = (int)Math.Round(0.50 * k);
        flow.ForcingLMax = (int)Math.Round(0.68 * k);
        flow.ForcingCorrelation = 2.0e4;
        _power = flow.ForcingPower;
        _drag = flow.DragTime;
        Model = new BarotropicModel(k, flow);

        if (hot)
        {
            double jet = profile.JetSpeed > 0.0 ? profile.JetSpeed : 2000.0 * Math.Sqrt(profile.Temperature / 1500.0);
            Model.SetZonalRelaxation(s =>
            {
                double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, s)));
                return jet * Math.Exp(-(lat / 0.5) * (lat / 0.5));
            }, 2.0e5);
        }

        DeckKelvin = new double[DeckCount];
        for (int d = 0; d < DeckCount; d++) DeckKelvin[d] = DeckSpecs[d].TemperatureFactor * ReferenceKelvin;
        DeepKelvin = Decks.DeepFactorFor(profile.Kind) * ReferenceKelvin;

        var rng = new FluidRandom(flow.Seed ^ 0xA5A5A5A5UL);
        Tracers = new TracerGrid(Model, Math.Max(64, profile.TracerLongitudes), DeckCount, rng, flow.ForcingLMin, flow.ForcingLMax);

        double radiative = FluidPhysics.RadiativeTime(ReferenceKelvin, Gravity);
        // The anomalies are a few percent of T, but never more than about the temperature scale over which the cloud decks
        // respond (cool bodies' clouds respond over a few kelvin, and an anomaly of many scales would clear and fill them completely).
        double scaleGeo = 1.0;
        for (int d = 0; d < DeckCount; d++) scaleGeo *= Math.Pow(DeckSpecs[d].TemperatureScale(DeckKelvin[d]), 1.0 / DeckCount);
        double thetaRms = Math.Min(FluidPhysics.ThetaRms(ReferenceKelvin), 1.2 * scaleGeo) * profile.Patchiness;
        Forcing = new TracerForcing
        {
            RadiativeTime = radiative,
            HeatingRate = thetaRms / radiative * Math.Sqrt((radiative + flow.ForcingCorrelation) / flow.ForcingCorrelation),
            DeckKelvin = (double[])DeckKelvin.Clone(),
            DeckScale = new double[DeckCount],
            CondensationTime = new double[DeckCount],
            DeckShare = new double[DeckCount]
        };
        for (int d = 0; d < DeckCount; d++)
        {
            Forcing.DeckScale[d] = DeckSpecs[d].TemperatureScale(DeckKelvin[d]);
            Forcing.CondensationTime[d] = DeckSpecs[d].CondensationTime;
            Forcing.DeckShare[d] = 1.0 - 0.15 * d;
        }
        if (hot)
        {
            double td = profile.Temperature, tn = nightKelvin, tref = ReferenceKelvin;
            double lon0 = substellarLongitude;
            Forcing.Equilibrium = (sinLat, lon) =>
            {
                double cosLat = Math.Sqrt(Math.Max(0.0, 1.0 - sinLat * sinLat));
                double day = Math.Max(0.0, cosLat * Math.Cos(lon - lon0));
                double t4 = Math.Pow(tn, 4.0) + (Math.Pow(td, 4.0) - Math.Pow(tn, 4.0)) * day;
                return Math.Pow(t4, 0.25) - tref;
            };
            Forcing.HeatingRate *= 0.25;   // the day-night heating dominates; the convective patchiness is a small addition
        }
        Lut = new EmissionLut(profile.Kind, DeckSpecs, 2.0);
        LutThermalInfrared = new EmissionLut(profile.Kind, DeckSpecs, 2.0, true);
    }

    /// <summary>The largest time step the model takes, s: below the rotation's own stability limit and 20 steps to a day for slow rotators.</summary>
    public double MaxDt => Math.Min(6000.0, 0.8 / Omega);

    /// <summary>
    /// Spins the flow and clouds up to a statistically steady state. The flow takes a few drag times to forget its start; to
    /// do that in a few thousand steps the stirring power is multiplied by s and the drag time divided by s (the same
    /// equilibrium energy, which goes as their product) until it has settled, then the real values take over while the clouds
    /// form. The same profile and seed always give the same state. Returns false when cancelled.
    /// </summary>
    public bool SpinUp(Func<bool> cancel = null)
    {
        FlowParameters fp = Model.Parameters;
        double s = Math.Max(1.0, _drag / 1.5e6);
        fp.DragTime = _drag / s;
        fp.ForcingPower = _power * s;
        double flowTime = 4.0 * fp.DragTime;
        int guard = 0;
        while (Model.Time < flowTime && guard++ < 6000)
        {
            if (cancel != null && cancel()) return false;
            Model.Step(Model.CourantDt(0.5, MaxDt));
            if (!Model.IsFinite()) throw new InvalidOperationException("The flow blew up during spin-up.");
        }
        fp.DragTime = _drag;
        fp.ForcingPower = _power;
        double tracerTime = Model.Time + Math.Max(1.0e6, 12.0 * MaxCondensationTime());
        guard = 0;
        while (Model.Time < tracerTime && guard++ < 1200)
        {
            if (cancel != null && cancel()) return false;
            StepOnce();
        }
        _t0 = Model.Time;
        _spun = true;
        return true;
    }

    private double MaxCondensationTime()
    {
        double t = 0.0;
        foreach (DeckSpec d in DeckSpecs) t = Math.Max(t, d.CondensationTime);
        return t;
    }

    private void StepOnce() => StepBy(Model.CourantDt(0.5, MaxDt));

    private void StepBy(double dt)
    {
        Model.Step(dt);
        Tracers.Step(dt, Forcing);
    }

    /// <summary>
    /// Advances the simulation towards model time <paramref name="target"/>, taking at most <paramref name="maxSteps"/> steps
    /// (the caller's cap on the work per call). It takes whole steps only, so it ends at most one step (about a thousand seconds)
    /// behind the target, or further behind when the cap was hit. Returns the model time reached.
    /// </summary>
    public double AdvanceTo(double target, int maxSteps)
    {
        int n = 0;
        while (n < maxSteps)
        {
            // Whole steps only: a partial step to land exactly on the target would be thousands of tiny steps at 1x speed.
            double dt = Model.CourantDt(0.5, MaxDt);
            if (target - Time < dt) break;
            StepBy(dt);
            n++;
        }
        return Time;
    }

    /// <summary>The size of the next step, s: the model only advances when the game clock is at least this far ahead.</summary>
    public double NextDt => Model.CourantDt(0.5, MaxDt);

    /// <summary>Jumps the model clock forward without integrating (the state stays as it is): a long time warp the work cap could not follow.</summary>
    public void SkipTo(double target)
    {
        if (target > Time) _t0 -= target - Time;
    }

    /// <summary>
    /// The fields for the renderer: for each cell of the cloud grid, red = condensate of deck 0, green = deck 1, blue = deck 2,
    /// alpha = the temperature anomaly in K. 4 floats per cell, south to north rows.
    /// </summary>
    public void WriteFields(float[] rgba)
    {
        int n = Tracers.NLat * Tracers.NLon;
        for (int c = 0; c < n; c++)
        {
            rgba[c * 4] = (float)Tracers.Q[0][c];
            rgba[c * 4 + 1] = (float)Tracers.Q[1][c];
            rgba[c * 4 + 2] = (float)Tracers.Q[2][c];
            rgba[c * 4 + 3] = (float)Tracers.Theta[c];
        }
    }
}
