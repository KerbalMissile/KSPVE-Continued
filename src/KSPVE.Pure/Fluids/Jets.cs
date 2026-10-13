using System;

namespace KSPVE.Pure.Fluids;

/// <summary>Counting the zonal jets of a flow.</summary>
public static class Jets
{
    /// <summary>
    /// The number of eastward jets in a zonal-mean wind profile (south to north): maxima of u that stand above the mean of the
    /// profile by at least <paramref name="fraction"/> of the largest excursion, each counted once between the places where
    /// the wind falls back below that level. Retrograde jets are the same number to within one.
    /// </summary>
    public static int Count(double[] u, double fraction)
    {
        if (u == null || u.Length < 3) return 0;
        double mean = 0.0, max = 0.0;
        foreach (double v in u) mean += v;
        mean /= u.Length;
        foreach (double v in u) max = Math.Max(max, Math.Abs(v - mean));
        if (!(max > 0.0)) return 0;
        double level = fraction * max;
        int count = 0;
        bool inside = false;
        foreach (double v in u)
        {
            if (!inside && v - mean > level) { inside = true; count++; }
            else if (inside && v - mean < 0.0) inside = false;
        }
        return count;
    }

    /// <summary>
    /// The energy-weighted mean degree of the zonal-mean flow: the number of half-wavelengths of its jets round a meridian
    /// (a jet pair is two degrees), a smooth measure of how finely the zonal flow is banded. The Rhines scale predicts it
    /// grows as the square root of the rotation rate.
    /// </summary>
    public static double ZonalCentroid(BarotropicModel model)
    {
        double sum = 0.0, weighted = 0.0;
        for (int l = 1; l <= model.K; l++)
        {
            double z = model.ZRe[model.Layout.Index(l, 0)];
            double e = z * z / (l * (l + 1.0));
            sum += e;
            weighted += l * e;
        }
        return sum > 0.0 ? weighted / sum : 0.0;
    }
}
