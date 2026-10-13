using System;
using System.Globalization;
using KSPVE.Pure.Config;

namespace KSPVE.Pure.Stars;

/// <summary>
/// How a star's magnetic structures (prominences, loops, the corona, CMEs) are shown. This is a named display setting:
/// only <see cref="Eye"/> is what a person would see.
/// </summary>
public enum StarView
{
    /// <summary>
    /// Realistic. In white light the chromosphere, prominences and loops are about a thousandth of the disc or fainter and
    /// the corona a millionth: all of them are drawn at those strengths, so they only show when something eclipses the disc.
    /// </summary>
    Eye = 0,
    /// <summary>
    /// An H-alpha telescope (656.3 nm, a narrow filter, so the line's emission is not drowned by the disc): prominences and
    /// loops bright red-orange and semi-transparent off the limb, dark filaments on the disc, the corona a faint orange
    /// glow. Not what the eye sees; the default, because it is the look the player asked for.
    /// </summary>
    HAlpha = 1,
    /// <summary>An extreme-ultraviolet telescope (Fe IX 17.1 nm, gold in false colour): coronal loops glowing over a dim disc.</summary>
    Euv = 2
}

/// <summary>One star's activity and look (a STAR node of KSPVE_STARS, or the DEFAULT node for a star without one).</summary>
public sealed class StarProfile
{
    public const double Day = 86400.0, Year = 365.25 * 86400.0;

    /// <summary>The body's name; empty for the fallback profile.</summary>
    public string Body = "";
    /// <summary>Effective temperature (K): the Sun's 5772 K (IAU 2015 nominal).</summary>
    public double Temperature = 5772.0;
    /// <summary>Activity relative to the Sun's (1 = the Sun's rate of active regions, flares and CMEs; 0 = quiet).</summary>
    public double Activity = 1.0;
    /// <summary>The activity cycle's length, s: the Sun's 11 years between sunspot minima.</summary>
    public double CyclePeriod = 11.0 * Year;
    /// <summary>Where in the cycle (0..1) the game time zero falls: 0 = minimum, 0.36 = maximum.</summary>
    public double CyclePhase = 0.30;
    public ulong Seed = 1;
    /// <summary>"star" or "browndwarf" (a brown dwarf's surface is drawn by the fluids feature, not here).</summary>
    public string Kind = "star";
    public bool IsBrownDwarf => Temperature < 2500.0 || string.Equals(Kind, "browndwarf", StringComparison.OrdinalIgnoreCase);
    /// <summary>The equator's sidereal rotation period, s: the Sun's 360 / 14.713 days (Snodgrass &amp; Ulrich 1990).</summary>
    public double EquatorialRotationPeriod = 360.0 / 14.713 * Day;
    /// <summary>The wind's ambient speed (m/s) the CMEs relax towards: the slow wind, about 400 km/s.</summary>
    public double WindSpeed = 4.0e5;
    /// <summary>Drag parameter of the drag-based model (1/m): the median of 0.2e-7 per km (Vrsnak et al. 2013).</summary>
    public double Drag = 2.0e-11;
    /// <summary>Scales the CME rate (1 = the observed one at the same activity).</summary>
    public double CmeRate = 1.0;

    public bool Enabled = true, Photosphere = true, Spots = true, Prominences = true, Corona = true, Cmes = true;
    /// <summary>The disc's radiance at its centre (HDR units); the star's own mesh is about this bright.</summary>
    public double DiscRadiance = 2.0;

    public StarProfile Clone() => (StarProfile)MemberwiseClone();

    /// <summary>
    /// Reads a STAR (or DEFAULT) node over <paramref name="baseline"/>: unnamed keys keep the baseline's values.
    /// Keys: body, enabled, temperature, activity, cyclePeriodYears, cyclePhase, seed, rotationPeriodDays, windSpeed,
    /// drag, cmeRate, discRadiance, photosphere, spots, prominences, corona, cmes.
    /// </summary>
    public static StarProfile From(CfgNode node, StarProfile baseline)
    {
        StarProfile p = baseline.Clone();
        string v;
        if ((v = node.GetLast("body")) != null) p.Body = v.Trim();
        if ((v = node.GetLast("kind")) != null) p.Kind = v.Trim();
        Num(node, "temperature", ref p.Temperature);
        Num(node, "activity", ref p.Activity);
        double years = double.NaN;
        Num(node, "cyclePeriodYears", ref years);
        if (!double.IsNaN(years) && years > 0.0) p.CyclePeriod = years * Year;
        Num(node, "cyclePhase", ref p.CyclePhase);
        double days = double.NaN;
        Num(node, "rotationPeriodDays", ref days);
        if (!double.IsNaN(days) && days > 0.0) p.EquatorialRotationPeriod = days * Day;
        Num(node, "windSpeed", ref p.WindSpeed);
        Num(node, "drag", ref p.Drag);
        Num(node, "cmeRate", ref p.CmeRate);
        Num(node, "discRadiance", ref p.DiscRadiance);
        double seed = double.NaN;
        Num(node, "seed", ref seed);
        if (!double.IsNaN(seed)) p.Seed = (ulong)Math.Max(0.0, seed);
        Flag(node, "enabled", ref p.Enabled);
        Flag(node, "photosphere", ref p.Photosphere);
        Flag(node, "spots", ref p.Spots);
        Flag(node, "prominences", ref p.Prominences);
        Flag(node, "corona", ref p.Corona);
        Flag(node, "cmes", ref p.Cmes);
        p.Temperature = Math.Max(p.Temperature, 1500.0);
        p.Activity = Math.Max(p.Activity, 0.0);
        p.CyclePhase = p.CyclePhase - Math.Floor(p.CyclePhase);
        p.WindSpeed = Math.Max(p.WindSpeed, 1.0e5);
        p.Drag = Math.Max(p.Drag, 1.0e-13);
        return p;
    }

    private static void Num(CfgNode node, string key, ref double target)
    {
        string text = node.GetLast(key);
        if (text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v))
            target = v;
    }

    private static void Flag(CfgNode node, string key, ref bool target)
    {
        string text = node.GetLast(key);
        if (text == null) return;
        text = text.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1") target = true;
        else if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0") target = false;
    }
}
