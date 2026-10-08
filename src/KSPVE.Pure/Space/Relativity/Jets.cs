namespace KSPVE.Pure.Space.Relativity;

/// <summary>Relativistic jet beaming.</summary>
public static class Jets
{
    /// <summary>
    /// D^(2+alpha) for a continuous jet (Blandford and Koenigl 1979), D = 1/(gamma(1 - beta cos)),
    /// cos = cosine of the angle between the jet velocity and the line of sight (towards the observer).
    /// alpha is the spectral index, S_nu ~ nu^(-alpha).
    /// </summary>
    public static double BeamingFactor(double beta, double cosAngleToLineOfSight, double spectralIndexAlpha) =>
        System.Math.Pow(Redshift.Doppler(beta, cosAngleToLineOfSight), 2.0 + spectralIndexAlpha);
}
