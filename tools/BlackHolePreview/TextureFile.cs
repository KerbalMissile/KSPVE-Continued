using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;

/// <summary>
/// Preview only: a PNG read into linear RGBA, sampled bilinearly with clamped edges, standing in for the custom textures
/// the game loads (BlackHoleSettings.Texture). Reads 8-bit greyscale, grey+alpha, RGB and RGBA, not interlaced.
/// </summary>
internal sealed class TextureFile
{
    public readonly int Width, Height;
    private readonly Vector4[] _pixels;

    private TextureFile(int w, int h, Vector4[] pixels)
    {
        Width = w;
        Height = h;
        _pixels = pixels;
    }

    /// <summary>The image at <paramref name="path"/> (or path.png), or null when there is none; colours made linear unless
    /// <paramref name="srgb"/> is false (data maps: coverage, cloud types).</summary>
    public static TextureFile Load(string path, bool srgb = true)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!File.Exists(path) && File.Exists(path + ".png")) path += ".png";
        if (!File.Exists(path)) { Console.WriteLine($"texture {path}: not found"); return null; }
        byte[] data = File.ReadAllBytes(path);
        int pos = 8, w = 0, h = 0, colorType = 0, bitDepth = 0, interlace = 0;
        var idat = new MemoryStream();
        while (pos + 8 <= data.Length)
        {
            int len = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int body = pos + 8;
            if (type == "IHDR")
            {
                w = (data[body] << 24) | (data[body + 1] << 16) | (data[body + 2] << 8) | data[body + 3];
                h = (data[body + 4] << 24) | (data[body + 5] << 16) | (data[body + 6] << 8) | data[body + 7];
                bitDepth = data[body + 8];
                colorType = data[body + 9];
                interlace = data[body + 12];
            }
            else if (type == "IDAT") idat.Write(data, body, len);
            else if (type == "IEND") break;
            pos = body + len + 4;
        }
        int channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
        if (bitDepth != 8 || channels == 0 || interlace != 0)
        {
            Console.WriteLine($"texture {path}: only 8-bit grey, RGB or RGBA PNGs without interlacing");
            return null;
        }
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        byte[] bytes = raw.ToArray();
        int stride = w * channels;
        var image = new byte[h * stride];
        var prev = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            int filter = bytes[y * (stride + 1)];
            var line = new byte[stride];
            Array.Copy(bytes, y * (stride + 1) + 1, line, 0, stride);
            for (int i = 0; i < stride; i++)
            {
                int left = i >= channels ? line[i - channels] : 0, up = prev[i], upLeft = i >= channels ? prev[i - channels] : 0;
                int add = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, upLeft),
                    _ => 0
                };
                line[i] = (byte)(line[i] + add);
            }
            Array.Copy(line, 0, image, y * stride, stride);
            prev = line;
        }
        var pixels = new Vector4[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int o = i * channels;
            float r, g, b, a = 1f;
            if (channels <= 2) { r = g = b = image[o] / 255f; if (channels == 2) a = image[o + 1] / 255f; }
            else { r = image[o] / 255f; g = image[o + 1] / 255f; b = image[o + 2] / 255f; if (channels == 4) a = image[o + 3] / 255f; }
            pixels[i] = srgb ? new Vector4(ToLinear(r), ToLinear(g), ToLinear(b), a) : new Vector4(r, g, b, a);
        }
        return new TextureFile(w, h, pixels);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>The texel in column <paramref name="x"/>, row <paramref name="y"/> (row 0 the top, as stored).</summary>
    public Vector4 Pixel(int x, int y) => _pixels[y * Width + x];

    /// <summary>The image at (u, v), v up as in Unity, bilinear, edges clamped.</summary>
    public Vector4 Sample(float u, float v)
    {
        float x = Math.Clamp(u, 0f, 1f) * (Width - 1), y = (1f - Math.Clamp(v, 0f, 1f)) * (Height - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float fx = x - x0, fy = y - y0;
        Vector4 a = Vector4.Lerp(_pixels[y0 * Width + x0], _pixels[y0 * Width + x1], fx);
        Vector4 b = Vector4.Lerp(_pixels[y1 * Width + x0], _pixels[y1 * Width + x1], fx);
        return Vector4.Lerp(a, b, fy);
    }
}
