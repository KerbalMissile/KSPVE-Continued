namespace KSPVE.Pure.Space.Relativity;

/// <summary>
/// Frequency ratios g = nu_observed / nu_emitted. Multiply emitted temperature by g, bolometric intensity by g^4
/// (or I_nu / nu^3, which is invariant).
/// </summary>
public static class Redshift
{
    /// <summary>sqrt(1 - 2m/r) for a static emitter at areal radius r seen from infinity. Zero at or inside the horizon.</summary>
    public static double Gravitational(double radius, double m)
    {
        double f = 1.0 - 2.0 * m / radius;
        return f > 0 ? System.Math.Sqrt(f) : 0.0;
    }

    /// <summary>
    /// 1 / (gamma (1 - beta cos)). beta is the emitter speed in units of c as measured by a local static
    /// observer; cosAngleVelocityToPhoton is the cosine of the angle between the emitter velocity and the
    /// photon propagation direction (towards the observer), both measured in that static frame.
    /// Head-on (cos = 1) gives sqrt((1+beta)/(1-beta)). Rybicki and Lightman 1979, eq. 4.11.
    /// </summary>
    public static double Doppler(double beta, double cosAngleVelocityToPhoton)
    {
        double gamma = 1.0 / System.Math.Sqrt(1.0 - beta * beta);
        return 1.0 / (gamma * (1.0 - beta * cosAngleVelocityToPhoton));
    }

    /// <summary>Gravitational * Doppler; the angle is in the emitter's local static frame (see <see cref="Doppler"/>).</summary>
    public static double Combined(double radius, double m, double beta, double cosAngleVelocityToPhoton) =>
        Gravitational(radius, m) * Doppler(beta, cosAngleVelocityToPhoton);
}
