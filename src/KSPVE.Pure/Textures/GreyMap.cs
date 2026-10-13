using System;
using System.Threading.Tasks;
using KSPVE.Pure.Clouds;

namespace KSPVE.Pure.Textures;

/// <summary>
/// One channel of a texture over a body, kept on the CPU at a reduced size (each texel the mean of the f x f it stands
/// for) and sampled bilinearly, wrapping in u (longitude) and clamped in v (latitude, 0 the south).
/// </summary>
public sealed class GreyMap : IGreyMap
{
    private readonly float[] _values;
    private readonly bool _topFirst;

    public int Width { get; }
    public int Height { get; }

    /// <summary>Its texels, row by row (a procedural map writes them, then calls <see cref="Changed"/>).</summary>
    public float[] Values => _values;

    /// <summary>The first row is the north.</summary>
    public bool TopFirst => _topFirst;

    /// <summary>Goes up each time the texels change, so a GPU copy knows to upload them again.</summary>
    public int Version { get; private set; }

    public void Changed() => Version++;

    /// <param name="values">Row by row, <paramref name="width"/> wide.</param>
    /// <param name="topFirst">The first row is the north (PNG); otherwise the south (raw DDS rows, as Unity loads them).</param>
    public GreyMap(int width, int height, float[] values, bool topFirst)
    {
        if (width <= 0 || height <= 0 || values == null || values.Length < width * height) throw new ArgumentException("bad grey map size");
        Width = width;
        Height = height;
        _values = values;
        _topFirst = topFirst;
    }

    public double Sample(double u, double v)
    {
        u -= Math.Floor(u);
        v = v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
        double x = u * Width - 0.5, y = (_topFirst ? 1.0 - v : v) * Height - 0.5;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        int x1 = ((x0 + 1) % Width + Width) % Width;
        x0 = (x0 % Width + Width) % Width;
        int y1 = Math.Min(Math.Max(y0 + 1, 0), Height - 1);
        y0 = Math.Min(Math.Max(y0, 0), Height - 1);
        double a = _values[y0 * Width + x0] + (_values[y0 * Width + x1] - _values[y0 * Width + x0]) * fx;
        double b = _values[y1 * Width + x0] + (_values[y1 * Width + x1] - _values[y1 * Width + x0]) * fx;
        return a + (b - a) * fy;
    }

    /// <summary>The channel index (0 red .. 3 alpha) of an alpha mask choice.</summary>
    public static int ChannelIndex(AlphaMaskChannel channel) => channel switch
    {
        AlphaMaskChannel.G => 1,
        AlphaMaskChannel.B => 2,
        AlphaMaskChannel.A => 3,
        _ => 0,
    };

    /// <summary>
    /// A DDS's channel (0..3) at most <paramref name="maxWidth"/> texels wide. Block-compressed images are decoded block by
    /// block (in parallel), so even a very large map never needs its full-size pixels in memory.
    /// </summary>
    public static GreyMap FromDds(DdsImage image, int channel, int maxWidth)
    {
        int w = image.LevelWidth(0), h = image.LevelHeight(0), f = 1;
        while (w / f > Math.Max(maxWidth, 1)) f *= 2;
        int ow = Math.Max(1, w / f), oh = Math.Max(1, h / f);
        var sum = new float[ow * oh];
        byte[] data = image.Data;
        int offset = image.LevelOffset(0, 0);
        switch (image.Format)
        {
            case DdsFormat.BC1: case DdsFormat.BC2: case DdsFormat.BC3: case DdsFormat.BC4: case DdsFormat.BC5: case DdsFormat.BC7:
            {
                int blockBytes = image.Format == DdsFormat.BC1 || image.Format == DdsFormat.BC4 ? 8 : 16;
                int bw = (w + 3) / 4, bh = (h + 3) / 4;
                // Output rows are f >= 1 texel rows tall; with f < 4 two block rows can share none, with f >= 4 a block row
                // lies within one output row band of up to f/4 block rows, so blocks are grouped by output row band.
                int rowsPerBand = Math.Max(1, f / 4);
                int bands = (bh + rowsPerBand - 1) / rowsPerBand;
                Parallel.For(0, bands, band =>
                {
                    var texels = new float[64];
                    var values = new float[16];
                    int byEnd = Math.Min(bh, (band + 1) * rowsPerBand);
                    for (int by = band * rowsPerBand; by < byEnd; by++)
                        for (int bx = 0; bx < bw; bx++)
                        {
                            int o = offset + (by * bw + bx) * blockBytes;
                            if (image.Format == DdsFormat.BC4) BlockDecoder.DecodeBC4(data, o, values, 1, 0);
                            else
                            {
                                switch (image.Format)
                                {
                                    case DdsFormat.BC1: BlockDecoder.DecodeBC1(data, o, texels); break;
                                    case DdsFormat.BC2: BlockDecoder.DecodeBC2(data, o, texels); break;
                                    case DdsFormat.BC3: BlockDecoder.DecodeBC3(data, o, texels); break;
                                    case DdsFormat.BC5: BlockDecoder.DecodeBC5(data, o, texels); break;
                                    default: BlockDecoder.DecodeBC7(data, o, texels); break;
                                }
                                for (int i = 0; i < 16; i++) values[i] = texels[4 * i + channel];
                            }
                            for (int i = 0; i < 16; i++)
                            {
                                int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                                if (x >= w || y >= h) continue;
                                sum[Math.Min(y / f, oh - 1) * ow + Math.Min(x / f, ow - 1)] += values[i];
                            }
                        }
                });
                break;
            }
            default:
            {
                float[] rgba = image.DecodeRgba(0, 0);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        sum[Math.Min(y / f, oh - 1) * ow + Math.Min(x / f, ow - 1)] += rgba[4 * (y * w + x) + channel];
                break;
            }
        }
        float scale = 1f / (f * f);
        for (int i = 0; i < sum.Length; i++) sum[i] *= scale;
        return new GreyMap(ow, oh, sum, false);
    }

    /// <summary>A channel of 8-bit RGBA pixels (4 bytes a texel, row by row), at most <paramref name="maxWidth"/> wide.</summary>
    public static GreyMap FromRgba32(byte[] rgba, int w, int h, int channel, bool topFirst, int maxWidth)
    {
        int f = 1;
        while (w / f > Math.Max(maxWidth, 1)) f *= 2;
        int ow = Math.Max(1, w / f), oh = Math.Max(1, h / f);
        var sum = new float[ow * oh];
        float scale = 1f / (255f * f * f);
        for (int y = 0; y < h; y++)
        {
            int row = Math.Min(y / f, oh - 1) * ow;
            for (int x = 0; x < w; x++) sum[row + Math.Min(x / f, ow - 1)] += rgba[4 * (y * w + x) + channel] * scale;
        }
        return new GreyMap(ow, oh, sum, topFirst);
    }
}
