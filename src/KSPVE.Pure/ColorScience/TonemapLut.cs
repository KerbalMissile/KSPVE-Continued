using System;

namespace KSPVE.Pure.ColorScience;

/// <summary>Bakes tonemap/grading transforms into a 3D LUT indexed through a logarithmic shaper.</summary>
public static class TonemapLut
{
    /// <summary>Maps linear [2^minLog2, 2^maxLog2] to [0,1] logarithmically.</summary>
    public sealed class Shaper
    {
        private readonly double _minLog2;
        private readonly double _maxLog2;

        public Shaper(double minLog2, double maxLog2)
        {
            if (!(maxLog2 > minLog2)) throw new ArgumentException("maxLog2 must exceed minLog2.");
            _minLog2 = minLog2;
            _maxLog2 = maxLog2;
        }

        /// <summary>Linear to [0,1]; inputs at or below 2^minLog2 (and non-positive) give 0, above the top give 1.</summary>
        public double Encode(double linear)
        {
            if (!(linear > 0.0)) return 0.0;
            double u = (Math.Log(linear, 2.0) - _minLog2) / (_maxLog2 - _minLog2);
            return u < 0.0 ? 0.0 : (u > 1.0 ? 1.0 : u);
        }

        public double Decode(double u) => Math.Pow(2.0, _minLog2 + u * (_maxLog2 - _minLog2));
    }

    /// <summary>
    /// RGBA float LUT of size^3 texels, red fastest: index = ((b * size + g) * size + r) * 4, alpha = 1.
    /// Each lattice point (r,g,b)/(size-1) is decoded through the shaper to linear scene values and passed to transform.
    /// </summary>
    public static float[] Bake(int size, Func<Rgb, Rgb> transform, double minLog2, double maxLog2)
    {
        if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
        if (transform == null) throw new ArgumentNullException(nameof(transform));
        var shaper = new Shaper(minLog2, maxLog2);
        var data = new float[size * size * size * 4];
        double inv = 1.0 / (size - 1);
        for (int b = 0; b < size; b++)
            for (int g = 0; g < size; g++)
                for (int r = 0; r < size; r++)
                {
                    Rgb o = transform(new Rgb(shaper.Decode(r * inv), shaper.Decode(g * inv), shaper.Decode(b * inv)));
                    int i = ((b * size + g) * size + r) * 4;
                    data[i] = (float)o.R;
                    data[i + 1] = (float)o.G;
                    data[i + 2] = (float)o.B;
                    data[i + 3] = 1f;
                }
        return data;
    }
}
