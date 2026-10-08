namespace KSPVE.Pure.Space.Orbits;

/// <summary>Classical Keplerian elements (elliptic). Angles in radians, referenced to a right-handed inertial frame.</summary>
public sealed class OrbitalElements
{
    public double SemiMajorAxisMeters { get; }
    public double Eccentricity { get; }
    public double InclinationRadians { get; }
    public double LongitudeOfAscendingNodeRadians { get; }
    public double ArgumentOfPeriapsisRadians { get; }
    public double MeanAnomalyAtEpochRadians { get; }
    public double EpochSeconds { get; }

    public OrbitalElements(
        double semiMajorAxisMeters,
        double eccentricity,
        double inclinationRadians,
        double longitudeOfAscendingNodeRadians,
        double argumentOfPeriapsisRadians,
        double meanAnomalyAtEpochRadians,
        double epochSeconds)
    {
        SemiMajorAxisMeters = semiMajorAxisMeters;
        Eccentricity = eccentricity;
        InclinationRadians = inclinationRadians;
        LongitudeOfAscendingNodeRadians = longitudeOfAscendingNodeRadians;
        ArgumentOfPeriapsisRadians = argumentOfPeriapsisRadians;
        MeanAnomalyAtEpochRadians = meanAnomalyAtEpochRadians;
        EpochSeconds = epochSeconds;
    }
}
