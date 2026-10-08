namespace VolumetricBlackholeFx.Pure.Space.Relativity;

/// <summary>
/// Characteristic radii of a Schwarzschild black hole in geometric units (G = c = 1),
/// with mass m = GM/c^2 expressed in metres. Ref: Misner, Thorne, Wheeler, Gravitation (1973).
/// </summary>
public static class Schwarzschild
{
    public const double SpeedOfLightMetersPerSecond = 299792458.0;

    /// <summary>m = mu / c^2, in metres.</summary>
    public static double GeometricMassMeters(double gravitationalParameterM3PerS2) =>
        gravitationalParameterM3PerS2 / (SpeedOfLightMetersPerSecond * SpeedOfLightMetersPerSecond);

    public static double EventHorizonRadius(double m) => 2.0 * m;

    public static double PhotonSphereRadius(double m) => 3.0 * m;

    public static double IscoRadius(double m) => 6.0 * m;

    /// <summary>Photon capture impact parameter b_crit = 3*sqrt(3)*m.</summary>
    public static double CriticalImpactParameter(double m) => 3.0 * System.Math.Sqrt(3.0) * m;
}
