using System;

namespace KSPVE.Pure.Textures;

/// <summary>
/// CPU decoders for the block-compressed texture formats (BC1 to BC5 and BC7), following the Direct3D 11 specification.
/// Each 4x4 block decodes to 16 texels, row-major within the block, as RGBA floats in 0..1.
/// </summary>
public static class BlockDecoder
{
    private static readonly float[] Unorm8 = BuildUnorm8();

    /// <summary>
    /// Decodes a BC1 (DXT1) block: two RGB565 endpoints and 2-bit indices. When the first endpoint is not greater than the
    /// second the block has three colours and the fourth index is black, transparent if <paramref name="alphaPunchThrough"/>
    /// (as Direct3D always decodes it), else opaque.
    /// </summary>
    public static void DecodeBC1(byte[] src, int offset, float[] texels, bool alphaPunchThrough = true)
        => DecodeColour(src, offset, texels, true, alphaPunchThrough);

    /// <summary>Decodes a BC2 (DXT3) block: explicit 4-bit alpha, then a colour block always in four-colour mode.</summary>
    public static void DecodeBC2(byte[] src, int offset, float[] texels)
    {
        DecodeColour(src, offset + 8, texels, false, false);
        for (int i = 0; i < 16; i++)
        {
            int b = src[offset + (i >> 1)];
            int a = (i & 1) == 0 ? b & 0xF : b >> 4;
            texels[i * 4 + 3] = a / 15f;
        }
    }

    /// <summary>Decodes a BC3 (DXT5) block: a BC4-style alpha block, then a colour block always in four-colour mode.</summary>
    public static void DecodeBC3(byte[] src, int offset, float[] texels)
    {
        DecodeColour(src, offset + 8, texels, false, false);
        DecodeBC4(src, offset, texels, 4, 3);
    }

    /// <summary>
    /// Decodes a BC4 (unsigned) block, one channel: two 8-bit endpoints and 3-bit indices. Writes its 16 values to
    /// <paramref name="channel"/>[<paramref name="channelOffset"/> + i * <paramref name="stride"/>].
    /// </summary>
    public static void DecodeBC4(byte[] src, int offset, float[] channel, int stride, int channelOffset)
    {
        int a0 = src[offset], a1 = src[offset + 1];
        ulong bits = src[offset + 2] | ((ulong)src[offset + 3] << 8) | ((ulong)src[offset + 4] << 16)
            | ((ulong)src[offset + 5] << 24) | ((ulong)src[offset + 6] << 32) | ((ulong)src[offset + 7] << 40);

        // The eight values, interpolated in floats from the 8-bit endpoints as the D3D spec does (BC4_UNORM).
        float v0 = a0 / 255f, v1 = a1 / 255f, v2, v3, v4, v5, v6, v7;
        if (a0 > a1)
        {
            v2 = (6 * a0 + 1 * a1) / (7f * 255f);
            v3 = (5 * a0 + 2 * a1) / (7f * 255f);
            v4 = (4 * a0 + 3 * a1) / (7f * 255f);
            v5 = (3 * a0 + 4 * a1) / (7f * 255f);
            v6 = (2 * a0 + 5 * a1) / (7f * 255f);
            v7 = (1 * a0 + 6 * a1) / (7f * 255f);
        }
        else
        {
            v2 = (4 * a0 + 1 * a1) / (5f * 255f);
            v3 = (3 * a0 + 2 * a1) / (5f * 255f);
            v4 = (2 * a0 + 3 * a1) / (5f * 255f);
            v5 = (1 * a0 + 4 * a1) / (5f * 255f);
            v6 = 0f;
            v7 = 1f;
        }

        int dst = channelOffset;
        for (int i = 0; i < 16; i++, bits >>= 3, dst += stride)
        {
            switch ((int)(bits & 7))
            {
                case 0: channel[dst] = v0; break;
                case 1: channel[dst] = v1; break;
                case 2: channel[dst] = v2; break;
                case 3: channel[dst] = v3; break;
                case 4: channel[dst] = v4; break;
                case 5: channel[dst] = v5; break;
                case 6: channel[dst] = v6; break;
                default: channel[dst] = v7; break;
            }
        }
    }

    /// <summary>Decodes a BC5 (unsigned) block: two BC4 blocks, red then green; blue is 0 and alpha 1.</summary>
    public static void DecodeBC5(byte[] src, int offset, float[] texels)
    {
        DecodeBC4(src, offset, texels, 4, 0);
        DecodeBC4(src, offset + 8, texels, 4, 1);
        for (int i = 0; i < 16; i++)
        {
            texels[i * 4 + 2] = 0f;
            texels[i * 4 + 3] = 1f;
        }
    }

    /// <summary>
    /// Decodes a BC7 block (all eight modes: partitions, p-bits, channel rotation and index selection). A block in the
    /// reserved mode (first byte zero) decodes to transparent black, as the spec says.
    /// </summary>
    public static void DecodeBC7(byte[] src, int offset, float[] texels)
    {
        int first = src[offset];
        int mode = 0;
        while (mode < 8 && (first & (1 << mode)) == 0) mode++;
        if (mode == 8)
        {
            Array.Clear(texels, 0, 64);
            return;
        }

        var bits = new Bits128(ReadU64(src, offset), ReadU64(src, offset + 8), mode + 1);
        int subsets = Bc7Subsets[mode];
        int partition = bits.Read(Bc7PartitionBits[mode]);
        int rotation = bits.Read(Bc7RotationBits[mode]);
        int indexSelection = bits.Read(Bc7IndexSelectionBits[mode]);
        int colourBits = Bc7ColourBits[mode], alphaBits = Bc7AlphaBits[mode];
        int endpointCount = subsets * 2;
        int channels = alphaBits > 0 ? 4 : 3;

        // Endpoints, four ints each (RGBA): stored channel by channel, every endpoint's red first.
        int[] e = t_bc7Endpoints ?? (t_bc7Endpoints = new int[24]);
        for (int c = 0; c < channels; c++)
        {
            int n = c < 3 ? colourBits : alphaBits;
            for (int k = 0; k < endpointCount; k++) e[k * 4 + c] = bits.Read(n);
        }

        // P-bits: one per endpoint, or one per subset shared by its two endpoints; a low bit under every channel.
        int pBit = 0;
        if (Bc7EndpointPBits[mode] != 0)
        {
            pBit = 1;
            for (int k = 0; k < endpointCount; k++)
            {
                int p = bits.Read(1);
                for (int c = 0; c < channels; c++) e[k * 4 + c] = (e[k * 4 + c] << 1) | p;
            }
        }
        else if (Bc7SharedPBits[mode] != 0)
        {
            pBit = 1;
            for (int s = 0; s < subsets; s++)
            {
                int p = bits.Read(1);
                for (int k = s * 2; k < s * 2 + 2; k++)
                    for (int c = 0; c < channels; c++) e[k * 4 + c] = (e[k * 4 + c] << 1) | p;
            }
        }

        // Each channel to 8 bits by replicating its top bits into the low ones; no alpha bits means opaque.
        int colourPrecision = colourBits + pBit, alphaPrecision = alphaBits + pBit;
        for (int k = 0; k < endpointCount; k++)
        {
            for (int c = 0; c < 3; c++) e[k * 4 + c] = Expand(e[k * 4 + c], colourPrecision);
            e[k * 4 + 3] = channels == 4 ? Expand(e[k * 4 + 3], alphaPrecision) : 255;
        }

        // Indices: the first texel of each subset's anchor has one bit fewer (its top bit is implicitly 0). Modes 4 and 5
        // have a second set of indices (only texel 0 the anchor) following the first.
        int indexBits = Bc7IndexBits[mode], indexBits2 = Bc7IndexBits2[mode];
        int anchor1 = subsets == 2 ? Bc7Anchor2[partition] : subsets == 3 ? Bc7Anchor3First[partition] : -1;
        int anchor2 = subsets == 3 ? Bc7Anchor3Second[partition] : -1;
        Bits128 bits2 = bits;
        bits2.Skip(16 * indexBits - subsets);
        int[] weights = Bc7Weights(indexBits), weights2 = Bc7Weights(indexBits2);

        for (int i = 0; i < 16; i++)
        {
            int s = subsets == 1 ? 0 : subsets == 2 ? (Bc7Partition2[partition] >> i) & 1 : Bc7Partition3[partition * 16 + i];
            bool anchor = i == 0 || i == anchor1 || i == anchor2;
            int w1 = weights[bits.Read(anchor ? indexBits - 1 : indexBits)];
            int wc = w1, wa = w1;
            if (indexBits2 > 0)
            {
                int w2 = weights2[bits2.Read(i == 0 ? indexBits2 - 1 : indexBits2)];
                // Index selection 0: the first indices are colour's, the second alpha's; 1 swaps them.
                if (indexSelection == 0) wa = w2;
                else wc = w2;
            }

            int k0 = s * 8, k1 = k0 + 4;
            int r = Interpolate(e[k0], e[k1], wc);
            int g = Interpolate(e[k0 + 1], e[k1 + 1], wc);
            int b = Interpolate(e[k0 + 2], e[k1 + 2], wc);
            int a = Interpolate(e[k0 + 3], e[k1 + 3], wa);
            int t;
            switch (rotation)
            {
                case 1: t = r; r = a; a = t; break;
                case 2: t = g; g = a; a = t; break;
                case 3: t = b; b = a; a = t; break;
            }

            int o = i * 4;
            texels[o] = Unorm8[r];
            texels[o + 1] = Unorm8[g];
            texels[o + 2] = Unorm8[b];
            texels[o + 3] = Unorm8[a];
        }
    }

    /// <summary>
    /// The BC1 colour block. With <paramref name="threeColour"/> false the block is always in four-colour mode (BC2, BC3).
    /// The interpolated colours are computed in floats from the 8-bit expanded endpoints, as the D3D10+ spec does.
    /// </summary>
    private static void DecodeColour(byte[] src, int offset, float[] texels, bool threeColour, bool punchThrough)
    {
        int c0 = src[offset] | (src[offset + 1] << 8);
        int c1 = src[offset + 2] | (src[offset + 3] << 8);
        int r0 = Expand(c0 >> 11, 5), g0 = Expand((c0 >> 5) & 0x3F, 6), b0 = Expand(c0 & 0x1F, 5);
        int r1 = Expand(c1 >> 11, 5), g1 = Expand((c1 >> 5) & 0x3F, 6), b1 = Expand(c1 & 0x1F, 5);

        float pr0 = Unorm8[r0], pg0 = Unorm8[g0], pb0 = Unorm8[b0];
        float pr1 = Unorm8[r1], pg1 = Unorm8[g1], pb1 = Unorm8[b1];
        float pr2, pg2, pb2, pr3, pg3, pb3, pa3 = 1f;
        if (c0 > c1 || !threeColour)
        {
            pr2 = (2 * r0 + r1) / (3f * 255f);
            pg2 = (2 * g0 + g1) / (3f * 255f);
            pb2 = (2 * b0 + b1) / (3f * 255f);
            pr3 = (r0 + 2 * r1) / (3f * 255f);
            pg3 = (g0 + 2 * g1) / (3f * 255f);
            pb3 = (b0 + 2 * b1) / (3f * 255f);
        }
        else
        {
            pr2 = (r0 + r1) / (2f * 255f);
            pg2 = (g0 + g1) / (2f * 255f);
            pb2 = (b0 + b1) / (2f * 255f);
            pr3 = pg3 = pb3 = 0f;
            if (punchThrough) pa3 = 0f;
        }

        uint bits = ReadU32(src, offset + 4);
        for (int i = 0; i < 16; i++, bits >>= 2)
        {
            int o = i * 4;
            switch (bits & 3)
            {
                case 0: texels[o] = pr0; texels[o + 1] = pg0; texels[o + 2] = pb0; texels[o + 3] = 1f; break;
                case 1: texels[o] = pr1; texels[o + 1] = pg1; texels[o + 2] = pb1; texels[o + 3] = 1f; break;
                case 2: texels[o] = pr2; texels[o + 1] = pg2; texels[o + 2] = pb2; texels[o + 3] = 1f; break;
                default: texels[o] = pr3; texels[o + 1] = pg3; texels[o + 2] = pb3; texels[o + 3] = pa3; break;
            }
        }
    }

    /// <summary>An n-bit value (n from 4 to 8) widened to 8 bits by bit replication.</summary>
    private static int Expand(int value, int bits) => (value << (8 - bits)) | (value >> (2 * bits - 8));

    /// <summary>BC7's integer interpolation between two 8-bit endpoints with a 6-bit weight.</summary>
    private static int Interpolate(int e0, int e1, int weight) => ((64 - weight) * e0 + weight * e1 + 32) >> 6;

    private static int[] Bc7Weights(int indexBits) => indexBits == 2 ? Bc7Weights2 : indexBits == 3 ? Bc7Weights3 : Bc7Weights4;

    private static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    private static ulong ReadU64(byte[] b, int o) => ReadU32(b, o) | ((ulong)ReadU32(b, o + 4) << 32);

    private static float[] BuildUnorm8()
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++) table[i] = i / 255f;
        return table;
    }

    /// <summary>Reads a 128-bit little-endian block a few bits at a time (at most 8 per read), lowest bits first.</summary>
    private struct Bits128
    {
        private readonly ulong _lo, _hi;
        private int _pos;

        public Bits128(ulong lo, ulong hi, int pos)
        {
            _lo = lo;
            _hi = hi;
            _pos = pos;
        }

        public int Read(int count)
        {
            if (count == 0) return 0;
            ulong v;
            if (_pos >= 64) v = _hi >> (_pos - 64);
            else if (_pos + count <= 64) v = _lo >> _pos;
            else v = (_lo >> _pos) | (_hi << (64 - _pos));
            _pos += count;
            return (int)v & ((1 << count) - 1);
        }

        public void Skip(int count) => _pos += count;
    }

    [ThreadStatic] private static int[] t_bc7Endpoints;

    // BC7 mode table (D3D11 spec): subsets, partition bits, rotation bits, index selection bits, colour bits, alpha bits,
    // p-bits per endpoint, shared p-bits per subset, index bits, second index bits.
    private static readonly byte[] Bc7Subsets = { 3, 2, 3, 2, 1, 1, 1, 2 };
    private static readonly byte[] Bc7PartitionBits = { 4, 6, 6, 6, 0, 0, 0, 6 };
    private static readonly byte[] Bc7RotationBits = { 0, 0, 0, 0, 2, 2, 0, 0 };
    private static readonly byte[] Bc7IndexSelectionBits = { 0, 0, 0, 0, 1, 0, 0, 0 };
    private static readonly byte[] Bc7ColourBits = { 4, 6, 5, 7, 5, 7, 7, 5 };
    private static readonly byte[] Bc7AlphaBits = { 0, 0, 0, 0, 6, 8, 7, 5 };
    private static readonly byte[] Bc7EndpointPBits = { 1, 0, 0, 1, 0, 0, 1, 1 };
    private static readonly byte[] Bc7SharedPBits = { 0, 1, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] Bc7IndexBits = { 3, 3, 2, 2, 2, 2, 4, 2 };
    private static readonly byte[] Bc7IndexBits2 = { 0, 0, 0, 0, 3, 2, 0, 0 };

    private static readonly int[] Bc7Weights2 = { 0, 21, 43, 64 };
    private static readonly int[] Bc7Weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    private static readonly int[] Bc7Weights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

    /// <summary>Two-subset partitions: bit i is texel i's subset.</summary>
    private static readonly ushort[] Bc7Partition2 =
    {
        0xCCCC, 0x8888, 0xEEEE, 0xECC8, 0xC880, 0xFEEC, 0xFEC8, 0xEC80, 0xC800, 0xFFEC, 0xFE80, 0xE800, 0xFFE8, 0xFF00, 0xFFF0, 0xF000,
        0xF710, 0x008E, 0x7100, 0x08CE, 0x008C, 0x7310, 0x3100, 0x8CCE, 0x088C, 0x3110, 0x6666, 0x366C, 0x17E8, 0x0FF0, 0x718E, 0x399C,
        0xAAAA, 0xF0F0, 0x5A5A, 0x33CC, 0x3C3C, 0x55AA, 0x9696, 0xA55A, 0x73CE, 0x13C8, 0x324C, 0x3BDC, 0x6996, 0xC33C, 0x9966, 0x0660,
        0x0272, 0x04E4, 0x4E40, 0x2720, 0xC936, 0x936C, 0x39C6, 0x639C, 0x9336, 0x9CC6, 0x817E, 0xE718, 0xCCF0, 0x0FCC, 0x7744, 0xEE22,
    };

    /// <summary>Three-subset partitions: 16 subset numbers per partition, texel by texel.</summary>
    private static readonly byte[] Bc7Partition3 =
    {
        0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 1, 2, 2, 2, 2,
        0, 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 2, 0, 0, 1, 2, 2, 1, 1, 2, 2, 1, 1,
        0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 1, 0, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2,
        0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 2, 2,
        0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 0, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2,
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2,
        0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2,
        0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2,
        0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2, 1, 2, 2, 2,
        0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0, 2, 2, 2, 0,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 2, 1, 1, 2, 2,
        0, 1, 1, 1, 0, 0, 1, 1, 2, 0, 0, 1, 2, 2, 0, 0,
        0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2,
        0, 0, 2, 2, 0, 0, 2, 2, 0, 0, 2, 2, 1, 1, 1, 1,
        0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2, 0, 2, 2, 2,
        0, 0, 0, 1, 0, 0, 0, 1, 2, 2, 2, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2,
        0, 0, 0, 0, 1, 1, 0, 0, 2, 2, 1, 0, 2, 2, 1, 0,
        0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1, 0, 0, 0, 0,
        0, 0, 1, 2, 0, 0, 1, 2, 1, 1, 2, 2, 2, 2, 2, 2,
        0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1, 0, 1, 1, 0,
        0, 0, 0, 0, 0, 1, 1, 0, 1, 2, 2, 1, 1, 2, 2, 1,
        0, 0, 2, 2, 1, 1, 0, 2, 1, 1, 0, 2, 0, 0, 2, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 2, 0, 0, 2, 2, 2, 2, 2,
        0, 0, 1, 1, 0, 1, 2, 2, 0, 1, 2, 2, 0, 0, 1, 1,
        0, 0, 0, 0, 2, 0, 0, 0, 2, 2, 1, 1, 2, 2, 2, 1,
        0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 2, 2, 2,
        0, 2, 2, 2, 0, 0, 2, 2, 0, 0, 1, 2, 0, 0, 1, 1,
        0, 0, 1, 1, 0, 0, 1, 2, 0, 0, 2, 2, 0, 2, 2, 2,
        0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0, 0, 1, 2, 0,
        0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0,
        0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0, 1, 2, 0,
        0, 1, 2, 0, 2, 0, 1, 2, 1, 2, 0, 1, 0, 1, 2, 0,
        0, 0, 1, 1, 2, 2, 0, 0, 1, 1, 2, 2, 0, 0, 1, 1,
        0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 1, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1,
        0, 0, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2, 1, 1, 2, 2,
        0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 2, 2, 0, 0, 1, 1,
        0, 2, 2, 0, 1, 2, 2, 1, 0, 2, 2, 0, 1, 2, 2, 1,
        0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1,
        0, 0, 0, 0, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 2, 2, 2, 2,
        0, 2, 2, 2, 0, 1, 1, 1, 0, 2, 2, 2, 0, 1, 1, 1,
        0, 0, 0, 2, 1, 1, 1, 2, 0, 0, 0, 2, 1, 1, 1, 2,
        0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2, 2, 1, 1, 2,
        0, 2, 2, 2, 0, 1, 1, 1, 0, 1, 1, 1, 0, 2, 2, 2,
        0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1, 2, 0, 0, 0, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2, 2, 1, 1, 2,
        0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2,
        0, 0, 2, 2, 1, 1, 2, 2, 1, 1, 2, 2, 0, 0, 2, 2,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1, 1, 2,
        0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 1,
        0, 2, 2, 2, 1, 2, 2, 2, 0, 2, 2, 2, 1, 2, 2, 2,
        0, 1, 0, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        0, 1, 1, 1, 2, 0, 1, 1, 2, 2, 0, 1, 2, 2, 2, 0,
    };

    /// <summary>The anchor texel of subset 1 in each two-subset partition (subset 0's is always texel 0).</summary>
    private static readonly byte[] Bc7Anchor2 =
    {
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
    };

    /// <summary>The anchor texel of subset 1 in each three-subset partition.</summary>
    private static readonly byte[] Bc7Anchor3First =
    {
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3,
        3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15,
        8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15,
        3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3,
    };

    /// <summary>The anchor texel of subset 2 in each three-subset partition.</summary>
    private static readonly byte[] Bc7Anchor3Second =
    {
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8,
        15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8,
        15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8,
    };
}
