using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>Weighted histogram over log2 luminance, used for auto-exposure.</summary>
public sealed class LuminanceHistogram
{
    private readonly double[] _bins;
    private readonly double _minLog2;
    private readonly double _maxLog2;

    public LuminanceHistogram(int binCount, double minLog2, double maxLog2)
    {
        if (binCount < 1) throw new ArgumentOutOfRangeException(nameof(binCount));
        if (!(maxLog2 > minLog2)) throw new ArgumentException("maxLog2 must exceed minLog2.");
        _bins = new double[binCount];
        _minLog2 = minLog2;
        _maxLog2 = maxLog2;
    }

    public int BinCount => _bins.Length;

    /// <summary>Bin index; luminance at or below 2^minLog2 (or non-positive) maps to 0, above the range to the last bin.</summary>
    public int BinFor(double luminance)
    {
        if (!(luminance > 0.0)) return 0;
        double l2 = Math.Log(luminance, 2.0);
        if (l2 <= _minLog2) return 0;
        double t = (l2 - _minLog2) / (_maxLog2 - _minLog2);
        int bin = (int)(t * _bins.Length);
        return bin >= _bins.Length ? _bins.Length - 1 : bin;
    }

    public void Add(double luminance, double weight = 1.0) => _bins[BinFor(luminance)] += weight;

    public void Clear() => Array.Clear(_bins, 0, _bins.Length);

    public double BinCenterLog2(int bin) => _minLog2 + (bin + 0.5) * (_maxLog2 - _minLog2) / _bins.Length;

    /// <summary>
    /// Weighted mean of bin-centre log2 values after discarding the darkest lowPercent and brightest
    /// (100 - highPercent) of total weight; partially covered bins count fractionally. Empty range returns minLog2.
    /// </summary>
    public double AverageLog2InPercentileRange(double lowPercent, double highPercent)
    {
        double total = 0.0;
        for (int i = 0; i < _bins.Length; i++) total += _bins[i];
        if (!(total > 0.0)) return _minLog2;

        double lo = total * Math.Max(0.0, lowPercent) / 100.0;
        double hi = total * Math.Min(100.0, highPercent) / 100.0;
        double sum = 0.0, weight = 0.0, start = 0.0;
        for (int i = 0; i < _bins.Length; i++)
        {
            double end = start + _bins[i];
            double overlap = Math.Min(end, hi) - Math.Max(start, lo);
            if (overlap > 0.0)
            {
                sum += overlap * BinCenterLog2(i);
                weight += overlap;
            }
            start = end;
        }
        return weight > 0.0 ? sum / weight : _minLog2;
    }
}
