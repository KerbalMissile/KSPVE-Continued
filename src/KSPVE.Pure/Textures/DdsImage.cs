using System;
using System.Runtime.InteropServices;

namespace KSPVE.Pure.Textures;

/// <summary>
/// The texel formats <see cref="DdsImage"/> reads. Uncompressed names give the byte order in memory (BGRA8: blue first);
/// 16F and 32F are half and single floats.
/// </summary>
public enum DdsFormat { Unknown, BC1, BC2, BC3, BC4, BC5, BC7, RGBA8, BGRA8, BGRX8, RGB8, BGR8, R8, A8, RG8, R16F, RG16F, RGBA16F, R32F, RG32F, RGBA32F }

/// <summary>
/// A .dds texture read into memory: its size, mip levels, faces and format, the raw texel data, and a CPU decoder to RGBA
/// floats. Reads the DX9 header and the DX10 extension header; of an array texture only the first slice (or cube) is used.
/// </summary>
public sealed class DdsImage
{
    private const uint Magic = 0x20534444; // "DDS "
    private const int HeaderSize = 124;
    private const int Dx10HeaderSize = 20;

    private const uint FlagMipMapCount = 0x20000; // DDSD_MIPMAPCOUNT
    private const uint FlagDepth = 0x800000; // DDSD_DEPTH
    private const uint PfAlphaPixels = 0x1; // DDPF_ALPHAPIXELS
    private const uint PfAlpha = 0x2; // DDPF_ALPHA
    private const uint PfFourCC = 0x4; // DDPF_FOURCC
    private const uint PfRgb = 0x40; // DDPF_RGB
    private const uint PfLuminance = 0x20000; // DDPF_LUMINANCE
    private const uint Caps2Cubemap = 0x200; // DDSCAPS2_CUBEMAP
    private const uint Caps2Volume = 0x200000; // DDSCAPS2_VOLUME
    private const uint Dx10MiscTextureCube = 0x4; // D3D10_RESOURCE_MISC_TEXTURECUBE
    private const uint Dx10DimensionTexture3D = 4; // D3D10_RESOURCE_DIMENSION_TEXTURE3D

    private readonly int _blockBytes; // bytes per 4x4 block, 0 if uncompressed
    private readonly int _pixelBytes; // bytes per texel, 0 if block-compressed
    private readonly int[] _mipOffsets; // byte offset of each mip within a face
    private readonly int _faceSize; // bytes in one face with all its mips
    private readonly bool _opaque; // RGBA8 whose header says it has no alpha: decode alpha as 1

    private DdsImage(int width, int height, int mipCount, int faces, bool cubemap, DdsFormat format, bool srgb, byte[] data,
        bool opaque)
    {
        Width = width;
        Height = height;
        MipCount = mipCount;
        Faces = faces;
        IsCubemap = cubemap;
        Format = format;
        IsSrgb = srgb;
        Data = data;
        _opaque = opaque;
        _blockBytes = BlockBytes(format);
        _pixelBytes = PixelBytes(format);
        _mipOffsets = new int[mipCount];
        int offset = 0;
        for (int mip = 0; mip < mipCount; mip++)
        {
            _mipOffsets[mip] = offset;
            offset += (int)LevelBytes(format, LevelDimension(width, mip), LevelDimension(height, mip));
        }
        _faceSize = offset;
    }

    /// <summary>Width of the top mip level, in texels.</summary>
    public int Width { get; }

    /// <summary>Height of the top mip level, in texels.</summary>
    public int Height { get; }

    /// <summary>Number of mip levels, at least 1.</summary>
    public int MipCount { get; }

    /// <summary>6 for a cube map, else 1 (of an array texture only the first slice is read).</summary>
    public int Faces { get; }

    /// <summary>Whether the texture is a cube map (faces +X, -X, +Y, -Y, +Z, -Z in that order).</summary>
    public bool IsCubemap { get; }

    /// <summary>The texel format.</summary>
    public DdsFormat Format { get; }

    /// <summary>Whether the colour channels are sRGB-encoded (the DXGI *_SRGB formats). The decoder does not convert them.</summary>
    public bool IsSrgb { get; }

    /// <summary>Everything in the file after the header(s): the faces one after another, each with all its mips.</summary>
    public byte[] Data { get; }

    /// <summary>
    /// Parses a .dds file (DX9 header, and the DX10 extension header). Throws <see cref="FormatException"/> with a clear
    /// message for files that are not DDS, are truncated, are volume textures, or use a format <see cref="DdsFormat"/> does
    /// not list.
    /// </summary>
    public static DdsImage Parse(byte[] file)
    {
        if (file == null) throw new ArgumentNullException(nameof(file));
        if (file.Length < 4 || ReadU32(file, 0) != Magic)
            throw new FormatException("Not a DDS file: it does not start with the magic number \"DDS \".");
        if (file.Length < 4 + HeaderSize)
            throw new FormatException($"Truncated DDS file: {file.Length} bytes is shorter than the {4 + HeaderSize}-byte header.");
        uint headerSize = ReadU32(file, 4);
        if (headerSize != HeaderSize)
            throw new FormatException($"Not a valid DDS file: the header size is {headerSize}, not {HeaderSize}.");

        uint flags = ReadU32(file, 8);
        uint height = ReadU32(file, 12), width = ReadU32(file, 16);
        uint depth = ReadU32(file, 24), mipMapCount = ReadU32(file, 28);
        uint pfFlags = ReadU32(file, 80), fourCC = ReadU32(file, 84), bitCount = ReadU32(file, 88);
        uint rMask = ReadU32(file, 92), gMask = ReadU32(file, 96), bMask = ReadU32(file, 100), aMask = ReadU32(file, 104);
        uint caps2 = ReadU32(file, 112);

        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
            throw new FormatException($"Not a valid DDS file: its size is {width}x{height}.");

        int dataStart = 4 + HeaderSize;
        bool volume = depth > 1 && ((flags & FlagDepth) != 0 || (caps2 & Caps2Volume) != 0);
        bool cubemap = (caps2 & Caps2Cubemap) != 0;
        bool srgb = false, opaque = false;
        DdsFormat format;
        if ((pfFlags & PfFourCC) != 0 && fourCC == FourCC("DX10"))
        {
            if (file.Length < dataStart + Dx10HeaderSize)
                throw new FormatException($"Truncated DDS file: {file.Length} bytes is shorter than the DX10 header.");
            uint dxgi = ReadU32(file, dataStart), dimension = ReadU32(file, dataStart + 4), misc = ReadU32(file, dataStart + 8);
            dataStart += Dx10HeaderSize;
            format = FromDxgi(dxgi, out srgb);
            if (format == DdsFormat.Unknown)
                throw new FormatException($"Unsupported DDS format: DXGI format {dxgi}.");
            cubemap = (misc & Dx10MiscTextureCube) != 0;
            if (dimension == Dx10DimensionTexture3D) volume = true;
        }
        else if ((pfFlags & PfFourCC) != 0)
        {
            format = FromFourCC(fourCC);
            if (format == DdsFormat.Unknown)
                throw new FormatException($"Unsupported DDS format: FourCC {DescribeFourCC(fourCC)}.");
        }
        else
        {
            format = FromMasks(pfFlags, bitCount, rMask, gMask, bMask, aMask, out opaque);
            if (format == DdsFormat.Unknown)
                throw new FormatException($"Unsupported DDS pixel format: {bitCount} bits, flags 0x{pfFlags:X}, masks " +
                    $"R 0x{rMask:X8} G 0x{gMask:X8} B 0x{bMask:X8} A 0x{aMask:X8}.");
        }
        if (volume)
            throw new FormatException("Unsupported DDS texture: volume (3D) textures are not supported.");

        int w = (int)width, h = (int)height;
        int maxMips = 1;
        for (int size = Math.Max(w, h); size > 1; size >>= 1) maxMips++;
        uint mips = (flags & FlagMipMapCount) != 0 && mipMapCount > 0 ? mipMapCount : 1;
        if (mips > maxMips)
            throw new FormatException($"Not a valid DDS file: {mips} mip levels is more than a {w}x{h} texture can have ({maxMips}).");

        int faces = cubemap ? 6 : 1;
        long faceBytes = 0;
        for (int mip = 0; mip < (int)mips; mip++)
            faceBytes += LevelBytes(format, LevelDimension(w, mip), LevelDimension(h, mip));
        long needed = faceBytes * faces, have = file.Length - dataStart;
        if (needed > have)
            throw new FormatException($"Truncated DDS file: the {w}x{h} {format} texture ({faces} face(s), {mips} mip(s)) needs " +
                $"{needed} bytes of texel data, but the file has {have}.");

        var data = new byte[have];
        Buffer.BlockCopy(file, dataStart, data, 0, data.Length);
        return new DdsImage(w, h, (int)mips, faces, cubemap, format, srgb, data, opaque);
    }

    /// <summary>Width of a mip level: the top width halved per level, at least 1.</summary>
    public int LevelWidth(int mip)
    {
        CheckMip(mip);
        return LevelDimension(Width, mip);
    }

    /// <summary>Height of a mip level: the top height halved per level, at least 1.</summary>
    public int LevelHeight(int mip)
    {
        CheckMip(mip);
        return LevelDimension(Height, mip);
    }

    /// <summary>
    /// Byte offset into <see cref="Data"/> of one face's mip level (faces stored one after another, each with all its mips,
    /// as DDS does).
    /// </summary>
    public int LevelOffset(int face, int mip)
    {
        CheckFace(face);
        CheckMip(mip);
        return face * _faceSize + _mipOffsets[mip];
    }

    /// <summary>Byte size of one face's mip level: whole 4x4 blocks for block-compressed formats, else texels.</summary>
    public int LevelSize(int face, int mip)
    {
        CheckFace(face);
        CheckMip(mip);
        return (int)LevelBytes(Format, LevelDimension(Width, mip), LevelDimension(Height, mip));
    }

    /// <summary>
    /// The level decoded to RGBA floats (0..1 for unorm formats; as stored for float formats), width * height * 4, rows in
    /// file order (row 0 first). Missing colour channels are 0 and a missing alpha is 1: R8 gives (r,0,0,1), A8 (0,0,0,a),
    /// RG8 (r,g,0,1), BC4 (r,0,0,1), BC5 (r,g,0,1). sRGB data is returned as stored, not linearised.
    /// </summary>
    public float[] DecodeRgba(int face, int mip)
    {
        int w = LevelWidth(mip), h = LevelHeight(mip);
        int offset = LevelOffset(face, mip);
        var rgba = new float[checked(w * h * 4)];
        if (_blockBytes > 0) DecodeBlocks(offset, w, h, rgba);
        else DecodeTexels(offset, w * h, rgba);
        return rgba;
    }

    private void DecodeBlocks(int offset, int width, int height, float[] rgba)
    {
        int blocksX = Math.Max(1, (width + 3) / 4), blocksY = Math.Max(1, (height + 3) / 4);
        var block = new float[64];
        if (Format == DdsFormat.BC4)
            for (int i = 0; i < 16; i++) block[i * 4 + 3] = 1f; // BC4 fills only red; green and blue stay 0
        int src = offset;
        for (int by = 0; by < blocksY; by++)
        {
            int y0 = by * 4, rows = Math.Min(4, height - y0);
            for (int bx = 0; bx < blocksX; bx++, src += _blockBytes)
            {
                switch (Format)
                {
                    case DdsFormat.BC1: BlockDecoder.DecodeBC1(Data, src, block); break;
                    case DdsFormat.BC2: BlockDecoder.DecodeBC2(Data, src, block); break;
                    case DdsFormat.BC3: BlockDecoder.DecodeBC3(Data, src, block); break;
                    case DdsFormat.BC4: BlockDecoder.DecodeBC4(Data, src, block, 4, 0); break;
                    case DdsFormat.BC5: BlockDecoder.DecodeBC5(Data, src, block); break;
                    default: BlockDecoder.DecodeBC7(Data, src, block); break;
                }
                int x0 = bx * 4, columns = Math.Min(4, width - x0);
                for (int y = 0; y < rows; y++)
                    Array.Copy(block, y * 16, rgba, ((y0 + y) * width + x0) * 4, columns * 4);
            }
        }
    }

    private void DecodeTexels(int offset, int count, float[] rgba)
    {
        byte[] d = Data;
        float[] u8 = Unorm8;
        int s = offset;
        for (int i = 0, o = 0; i < count; i++, o += 4, s += _pixelBytes)
        {
            float r = 0f, g = 0f, b = 0f, a = 1f;
            switch (Format)
            {
                case DdsFormat.RGBA8: r = u8[d[s]]; g = u8[d[s + 1]]; b = u8[d[s + 2]]; a = _opaque ? 1f : u8[d[s + 3]]; break;
                case DdsFormat.BGRA8: b = u8[d[s]]; g = u8[d[s + 1]]; r = u8[d[s + 2]]; a = u8[d[s + 3]]; break;
                case DdsFormat.BGRX8: b = u8[d[s]]; g = u8[d[s + 1]]; r = u8[d[s + 2]]; break;
                case DdsFormat.RGB8: r = u8[d[s]]; g = u8[d[s + 1]]; b = u8[d[s + 2]]; break;
                case DdsFormat.BGR8: b = u8[d[s]]; g = u8[d[s + 1]]; r = u8[d[s + 2]]; break;
                case DdsFormat.R8: r = u8[d[s]]; break;
                case DdsFormat.A8: a = u8[d[s]]; break;
                case DdsFormat.RG8: r = u8[d[s]]; g = u8[d[s + 1]]; break;
                case DdsFormat.R16F: r = Half(d, s); break;
                case DdsFormat.RG16F: r = Half(d, s); g = Half(d, s + 2); break;
                case DdsFormat.RGBA16F: r = Half(d, s); g = Half(d, s + 2); b = Half(d, s + 4); a = Half(d, s + 6); break;
                case DdsFormat.R32F: r = BitConverter.ToSingle(d, s); break;
                case DdsFormat.RG32F: r = BitConverter.ToSingle(d, s); g = BitConverter.ToSingle(d, s + 4); break;
                default:
                    r = BitConverter.ToSingle(d, s); g = BitConverter.ToSingle(d, s + 4);
                    b = BitConverter.ToSingle(d, s + 8); a = BitConverter.ToSingle(d, s + 12);
                    break;
            }
            rgba[o] = r;
            rgba[o + 1] = g;
            rgba[o + 2] = b;
            rgba[o + 3] = a;
        }
    }

    private static readonly float[] Unorm8 = BuildUnorm8();

    private static float[] BuildUnorm8()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++) table[i] = i / 255f;
        return table;
    }

    /// <summary>An IEEE 754 binary16 value (little-endian) to a float, exactly: subnormals, infinities and NaN included.</summary>
    private static float Half(byte[] d, int s)
    {
        int h = d[s] | (d[s + 1] << 8);
        int exponent = (h >> 10) & 0x1F, mantissa = h & 0x3FF;
        bool negative = (h & 0x8000) != 0;
        if (exponent == 0)
        {
            float v = mantissa * (1f / 16777216f); // zero or subnormal: mantissa * 2^-24, exact in single precision
            return negative ? -v : v;
        }
        var bits = new FloatBits
        {
            U = (negative ? 0x80000000u : 0u) | ((uint)mantissa << 13) |
                (exponent == 31 ? 0x7F800000u : (uint)(exponent - 15 + 127) << 23), // infinity or NaN (payload kept), else normal
        };
        return bits.F;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FloatBits
    {
        [FieldOffset(0)] public uint U;
        [FieldOffset(0)] public float F;
    }

    private static DdsFormat FromFourCC(uint fourCC)
    {
        switch (fourCC)
        {
            case 111: return DdsFormat.R16F; // D3DFMT_R16F
            case 112: return DdsFormat.RG16F; // D3DFMT_G16R16F
            case 113: return DdsFormat.RGBA16F; // D3DFMT_A16B16G16R16F
            case 114: return DdsFormat.R32F; // D3DFMT_R32F
            case 115: return DdsFormat.RG32F; // D3DFMT_G32R32F
            case 116: return DdsFormat.RGBA32F; // D3DFMT_A32B32G32R32F
        }
        if (fourCC == FourCC("DXT1")) return DdsFormat.BC1;
        if (fourCC == FourCC("DXT2") || fourCC == FourCC("DXT3")) return DdsFormat.BC2;
        if (fourCC == FourCC("DXT4") || fourCC == FourCC("DXT5")) return DdsFormat.BC3;
        if (fourCC == FourCC("ATI1") || fourCC == FourCC("BC4U")) return DdsFormat.BC4;
        if (fourCC == FourCC("ATI2") || fourCC == FourCC("BC5U")) return DdsFormat.BC5;
        return DdsFormat.Unknown;
    }

    private static DdsFormat FromDxgi(uint dxgi, out bool srgb)
    {
        srgb = dxgi == 72 || dxgi == 75 || dxgi == 78 || dxgi == 99 || dxgi == 29 || dxgi == 91 || dxgi == 93;
        switch (dxgi)
        {
            case 71: case 72: return DdsFormat.BC1;
            case 74: case 75: return DdsFormat.BC2;
            case 77: case 78: return DdsFormat.BC3;
            case 80: case 81: return DdsFormat.BC4;
            case 83: case 84: return DdsFormat.BC5;
            case 98: case 99: return DdsFormat.BC7;
            case 28: case 29: return DdsFormat.RGBA8;
            case 87: case 91: return DdsFormat.BGRA8;
            case 88: case 93: return DdsFormat.BGRX8;
            case 61: return DdsFormat.R8;
            case 65: return DdsFormat.A8;
            case 49: return DdsFormat.RG8;
            case 54: return DdsFormat.R16F;
            case 34: return DdsFormat.RG16F;
            case 10: return DdsFormat.RGBA16F;
            case 41: return DdsFormat.R32F;
            case 16: return DdsFormat.RG32F;
            case 2: return DdsFormat.RGBA32F;
            default: return DdsFormat.Unknown;
        }
    }

    private static DdsFormat FromMasks(uint flags, uint bitCount, uint r, uint g, uint b, uint a, out bool opaque)
    {
        opaque = false;
        bool hasAlpha = (flags & PfAlphaPixels) != 0;
        if ((flags & PfRgb) != 0)
        {
            if (bitCount == 32 && r == 0x00FF0000 && g == 0x0000FF00 && b == 0x000000FF)
                return hasAlpha && a == 0xFF000000 ? DdsFormat.BGRA8 : DdsFormat.BGRX8;
            if (bitCount == 32 && r == 0x000000FF && g == 0x0000FF00 && b == 0x00FF0000)
            {
                opaque = !(hasAlpha && a == 0xFF000000);
                return DdsFormat.RGBA8;
            }
            if (bitCount == 24 && r == 0x00FF0000 && g == 0x0000FF00 && b == 0x000000FF) return DdsFormat.BGR8;
            if (bitCount == 24 && r == 0x000000FF && g == 0x0000FF00 && b == 0x00FF0000) return DdsFormat.RGB8;
            return DdsFormat.Unknown;
        }
        if ((flags & PfLuminance) != 0)
        {
            if (bitCount == 8 && !hasAlpha) return DdsFormat.R8;
            if (bitCount == 16 && hasAlpha && r == 0x00FF && a == 0xFF00) return DdsFormat.RG8;
            return DdsFormat.Unknown;
        }
        if ((flags & PfAlpha) != 0 && bitCount == 8) return DdsFormat.A8;
        return DdsFormat.Unknown;
    }

    private static int BlockBytes(DdsFormat format) => format switch
    {
        DdsFormat.BC1 or DdsFormat.BC4 => 8,
        DdsFormat.BC2 or DdsFormat.BC3 or DdsFormat.BC5 or DdsFormat.BC7 => 16,
        _ => 0,
    };

    private static int PixelBytes(DdsFormat format) => format switch
    {
        DdsFormat.R8 or DdsFormat.A8 => 1,
        DdsFormat.RG8 or DdsFormat.R16F => 2,
        DdsFormat.RGB8 or DdsFormat.BGR8 => 3,
        DdsFormat.RGBA8 or DdsFormat.BGRA8 or DdsFormat.BGRX8 or DdsFormat.RG16F or DdsFormat.R32F => 4,
        DdsFormat.RGBA16F or DdsFormat.RG32F => 8,
        DdsFormat.RGBA32F => 16,
        _ => 0,
    };

    private static long LevelBytes(DdsFormat format, int width, int height)
    {
        int blockBytes = BlockBytes(format);
        if (blockBytes > 0) return (long)Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes;
        return (long)width * height * PixelBytes(format);
    }

    private static int LevelDimension(int size, int mip) => Math.Max(1, size >> mip);

    private void CheckMip(int mip)
    {
        if (mip < 0 || mip >= MipCount) throw new ArgumentOutOfRangeException(nameof(mip), mip, $"The texture has {MipCount} mip level(s).");
    }

    private void CheckFace(int face)
    {
        if (face < 0 || face >= Faces) throw new ArgumentOutOfRangeException(nameof(face), face, $"The texture has {Faces} face(s).");
    }

    private static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static uint FourCC(string code) => (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));

    private static string DescribeFourCC(uint fourCC)
    {
        var chars = new char[4];
        for (int i = 0; i < 4; i++)
        {
            int c = (int)(fourCC >> (8 * i)) & 0xFF;
            if (c < 32 || c > 126) return fourCC.ToString();
            chars[i] = (char)c;
        }
        return "\"" + new string(chars) + "\"";
    }
}
