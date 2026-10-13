using System;
using KSPVE.Pure.Textures;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Textures;

public class DdsImageTests
{
    private const float Tol = 1e-6f;

    // ---- BC1 ----

    [Test]
    public void Dxt1_ParsesHeader_AndDecodesFourColourBlock()
    {
        // Pure red (0xF800) over pure blue (0x001F): c0 > c1, so c2 = (2 c0 + c1) / 3 and c3 = (c0 + 2 c1) / 3.
        byte[] block = Bc1Block(0xF800, 0x001F, 0, 1, 2, 3, 3, 2, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1);
        DdsImage dds = DdsImage.Parse(FourCCFile("DXT1", 4, 4, block));

        Assert.That(dds.Width, Is.EqualTo(4));
        Assert.That(dds.Height, Is.EqualTo(4));
        Assert.That(dds.Format, Is.EqualTo(DdsFormat.BC1));
        Assert.That(dds.MipCount, Is.EqualTo(1));
        Assert.That(dds.Faces, Is.EqualTo(1));
        Assert.That(dds.IsCubemap, Is.False);
        Assert.That(dds.IsSrgb, Is.False);
        Assert.That(dds.Data.Length, Is.EqualTo(8));
        Assert.That(dds.LevelSize(0, 0), Is.EqualTo(8));

        float[] rgba = dds.DecodeRgba(0, 0);
        Assert.That(rgba.Length, Is.EqualTo(64));
        AssertTexel(rgba, 0, 1, 0, 0, 1);
        AssertTexel(rgba, 1, 0, 0, 1, 1);
        AssertTexel(rgba, 2, 2 / 3f, 0, 1 / 3f, 1);
        AssertTexel(rgba, 3, 1 / 3f, 0, 2 / 3f, 1);
        AssertTexel(rgba, 4, 1 / 3f, 0, 2 / 3f, 1);
        AssertTexel(rgba, 7, 1, 0, 0, 1);
        AssertTexel(rgba, 15, 0, 0, 1, 1);
    }

    [Test]
    public void Bc1_ThreeColourMode_HalfwayColourAndPunchThroughAlpha()
    {
        // Pure blue (0x001F) over pure red (0xF800): c0 <= c1, so c2 = (c0 + c1) / 2 and c3 is black.
        byte[] block = Bc1Block(0x001F, 0xF800, 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        float[] rgba = DdsImage.Parse(FourCCFile("DXT1", 4, 4, block)).DecodeRgba(0, 0);
        AssertTexel(rgba, 0, 0, 0, 1, 1);
        AssertTexel(rgba, 1, 1, 0, 0, 1);
        AssertTexel(rgba, 2, 0.5f, 0, 0.5f, 1);
        AssertTexel(rgba, 3, 0, 0, 0, 0);

        var opaque = new float[64];
        BlockDecoder.DecodeBC1(block, 0, opaque, alphaPunchThrough: false);
        AssertTexel(opaque, 2, 0.5f, 0, 0.5f, 1);
        AssertTexel(opaque, 3, 0, 0, 0, 1);

        // Equal endpoints are three-colour mode too.
        var equal = new float[64];
        BlockDecoder.DecodeBC1(Bc1Block(0x07E0, 0x07E0, 3, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, equal);
        AssertTexel(equal, 0, 0, 0, 0, 0);
        AssertTexel(equal, 1, 0, 1, 0, 1);
    }

    [Test]
    public void Bc1_EndpointsExpandByBitReplication()
    {
        // 0x8410: red 16 of 31 -> (16 << 3) | (16 >> 2) = 132; green 32 of 63 -> (32 << 2) | (32 >> 4) = 130; blue 132.
        var texels = new float[64];
        BlockDecoder.DecodeBC1(Bc1Block(0x8410, 0x0000, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, texels);
        AssertTexel(texels, 0, 132 / 255f, 130 / 255f, 132 / 255f, 1);
        AssertTexel(texels, 1, 0, 0, 0, 1);
    }

    // ---- BC2, BC3 ----

    [Test]
    public void Dxt3_ExplicitAlpha_AndColourAlwaysInFourColourMode()
    {
        var alpha = new byte[8];
        for (int k = 0; k < 8; k++) alpha[k] = (byte)(((2 * k + 1) << 4) | (2 * k)); // texel i has alpha i / 15
        // c0 (blue) < c1 (red) would be three-colour mode in BC1, but BC2 is always four-colour.
        byte[] colour = Bc1Block(0x001F, 0xF800, 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        DdsImage dds = DdsImage.Parse(FourCCFile("DXT3", 4, 4, Concat(alpha, colour)));
        Assert.That(dds.Format, Is.EqualTo(DdsFormat.BC2));

        float[] rgba = dds.DecodeRgba(0, 0);
        AssertTexel(rgba, 0, 0, 0, 1, 0);
        AssertTexel(rgba, 1, 1, 0, 0, 1 / 15f);
        AssertTexel(rgba, 2, 1 / 3f, 0, 2 / 3f, 2 / 15f);
        AssertTexel(rgba, 3, 2 / 3f, 0, 1 / 3f, 3 / 15f);
        for (int i = 0; i < 16; i++) Assert.That(rgba[i * 4 + 3], Is.EqualTo(i / 15f).Within(Tol), $"texel {i}");
    }

    [Test]
    public void Dxt5_AlphaBlockPlusColourInFourColourMode()
    {
        byte[] alpha = Bc4Block(70, 7, 0, 1, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] colour = Bc1Block(0x001F, 0xF800, 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        DdsImage dds = DdsImage.Parse(FourCCFile("DXT5", 4, 4, Concat(alpha, colour)));
        Assert.That(dds.Format, Is.EqualTo(DdsFormat.BC3));
        Assert.That(dds.Data.Length, Is.EqualTo(16));

        float[] rgba = dds.DecodeRgba(0, 0);
        AssertTexel(rgba, 0, 0, 0, 1, 70 / 255f);
        AssertTexel(rgba, 1, 1, 0, 0, 7 / 255f);
        AssertTexel(rgba, 2, 1 / 3f, 0, 2 / 3f, 61 / 255f);
        AssertTexel(rgba, 3, 2 / 3f, 0, 1 / 3f, 52 / 255f); // not punched through: BC3 has no transparent colour
        AssertTexel(rgba, 7, 0, 0, 1, 16 / 255f);
    }

    // ---- BC4, BC5 ----

    [Test]
    public void Bc4_EightValueMode_WhenFirstEndpointIsGreater()
    {
        // a0 = 70 > a1 = 7: six values between, (70 - 7) / 7 = 9 apart: 61, 52, 43, 34, 25, 16.
        byte[] block = Bc4Block(70, 7, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0);
        var values = new float[16];
        BlockDecoder.DecodeBC4(block, 0, values, 1, 0);
        int[] expected = { 70, 7, 61, 52, 43, 34, 25, 16, 16, 25, 34, 43, 52, 61, 7, 70 };
        for (int i = 0; i < 16; i++) Assert.That(values[i], Is.EqualTo(expected[i] / 255f).Within(Tol), $"texel {i}");

        // 255 down to 0: sevenths.
        BlockDecoder.DecodeBC4(Bc4Block(255, 0, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, values, 1, 0);
        for (int i = 0; i < 6; i++) Assert.That(values[i], Is.EqualTo((6 - i) / 7f).Within(Tol), $"texel {i}");
    }

    [Test]
    public void Bc4_SixValueMode_WithZeroAnd255()
    {
        // a0 = 10 <= a1 = 60: four values between, 10 apart (20, 30, 40, 50), then 0 and 255.
        byte[] block = Bc4Block(10, 60, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0);
        var values = new float[16];
        BlockDecoder.DecodeBC4(block, 0, values, 1, 0);
        int[] expected = { 10, 60, 20, 30, 40, 50, 0, 255, 255, 0, 50, 40, 30, 20, 60, 10 };
        for (int i = 0; i < 16; i++) Assert.That(values[i], Is.EqualTo(expected[i] / 255f).Within(Tol), $"texel {i}");

        // Equal endpoints are the six-value mode too.
        BlockDecoder.DecodeBC4(Bc4Block(100, 100, 6, 7, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 0, values, 1, 0);
        Assert.That(values[0], Is.EqualTo(0f));
        Assert.That(values[1], Is.EqualTo(1f));
        Assert.That(values[2], Is.EqualTo(100 / 255f).Within(Tol));
    }

    [Test]
    public void Bc4_WritesOnlyItsStridedChannel()
    {
        var texels = new float[64];
        for (int i = 0; i < 64; i++) texels[i] = -1f;
        BlockDecoder.DecodeBC4(Bc4Block(70, 7, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1), 0, texels, 4, 2);
        for (int i = 0; i < 64; i++)
        {
            if (i % 4 != 2) Assert.That(texels[i], Is.EqualTo(-1f), $"entry {i}");
        }
        Assert.That(texels[2], Is.EqualTo(61 / 255f).Within(Tol));
        Assert.That(texels[6], Is.EqualTo(70 / 255f).Within(Tol));
        Assert.That(texels[62], Is.EqualTo(7 / 255f).Within(Tol));
    }

    [Test]
    public void Bc4AndBc5_FillMissingChannels()
    {
        byte[] red = Bc4Block(70, 7, 0, 1, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 0, 0, 0);
        byte[] green = Bc4Block(10, 60, 0, 1, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 0, 0, 0);

        DdsImage bc4 = DdsImage.Parse(FourCCFile("ATI1", 4, 4, red));
        Assert.That(bc4.Format, Is.EqualTo(DdsFormat.BC4));
        float[] r = bc4.DecodeRgba(0, 0);
        AssertTexel(r, 2, 61 / 255f, 0, 0, 1);
        AssertTexel(r, 7, 16 / 255f, 0, 0, 1);

        DdsImage bc5 = DdsImage.Parse(FourCCFile("ATI2", 4, 4, Concat(red, green)));
        Assert.That(bc5.Format, Is.EqualTo(DdsFormat.BC5));
        float[] rg = bc5.DecodeRgba(0, 0);
        AssertTexel(rg, 0, 70 / 255f, 10 / 255f, 0, 1);
        AssertTexel(rg, 2, 61 / 255f, 20 / 255f, 0, 1);
        AssertTexel(rg, 6, 25 / 255f, 0, 0, 1);
        AssertTexel(rg, 7, 16 / 255f, 1, 0, 1);

        Assert.That(DdsImage.Parse(FourCCFile("BC4U", 4, 4, red)).Format, Is.EqualTo(DdsFormat.BC4));
        Assert.That(DdsImage.Parse(FourCCFile("BC5U", 4, 4, Concat(red, green))).Format, Is.EqualTo(DdsFormat.BC5));
    }

    // ---- DX10 header and uncompressed formats ----

    [Test]
    public void Dx10Header_Bc4AndRgba8()
    {
        byte[] block = Bc4Block(70, 7, 0, 1, 2, 3, 4, 5, 6, 7, 0, 0, 0, 0, 0, 0, 0, 0);
        DdsImage bc4 = DdsImage.Parse(Dx10File(80, 4, 4, block));
        Assert.That(bc4.Format, Is.EqualTo(DdsFormat.BC4));
        Assert.That(bc4.IsSrgb, Is.False);
        Assert.That(bc4.Data.Length, Is.EqualTo(8));
        AssertTexel(bc4.DecodeRgba(0, 0), 3, 52 / 255f, 0, 0, 1);

        byte[] pixels = { 10, 20, 30, 40, 50, 60, 70, 80 };
        DdsImage rgba8 = DdsImage.Parse(Dx10File(28, 2, 1, pixels));
        Assert.That(rgba8.Format, Is.EqualTo(DdsFormat.RGBA8));
        Assert.That(rgba8.IsSrgb, Is.False);
        float[] rgba = rgba8.DecodeRgba(0, 0);
        AssertTexel(rgba, 0, 10 / 255f, 20 / 255f, 30 / 255f, 40 / 255f);
        AssertTexel(rgba, 1, 50 / 255f, 60 / 255f, 70 / 255f, 80 / 255f);

        Assert.That(DdsImage.Parse(Dx10File(29, 2, 1, pixels)).IsSrgb, Is.True);
        DdsImage bc1Srgb = DdsImage.Parse(Dx10File(72, 4, 4, new byte[8]));
        Assert.That(bc1Srgb.Format, Is.EqualTo(DdsFormat.BC1));
        Assert.That(bc1Srgb.IsSrgb, Is.True);
    }

    [Test]
    public void Dx9Masks_Bgra8_KeepsChannelOrder()
    {
        byte[] pixels = { 10, 20, 30, 40 }; // B, G, R, A in memory
        DdsImage bgra = DdsImage.Parse(MaskFile(0x41, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000, 1, 1, pixels));
        Assert.That(bgra.Format, Is.EqualTo(DdsFormat.BGRA8));
        AssertTexel(bgra.DecodeRgba(0, 0), 0, 30 / 255f, 20 / 255f, 10 / 255f, 40 / 255f);

        DdsImage bgrx = DdsImage.Parse(MaskFile(0x40, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0, 1, 1, pixels));
        Assert.That(bgrx.Format, Is.EqualTo(DdsFormat.BGRX8));
        AssertTexel(bgrx.DecodeRgba(0, 0), 0, 30 / 255f, 20 / 255f, 10 / 255f, 1);

        DdsImage rgba = DdsImage.Parse(MaskFile(0x41, 32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000, 1, 1, pixels));
        Assert.That(rgba.Format, Is.EqualTo(DdsFormat.RGBA8));
        AssertTexel(rgba.DecodeRgba(0, 0), 0, 10 / 255f, 20 / 255f, 30 / 255f, 40 / 255f);

        // Same byte order but the header says there is no alpha: alpha decodes as 1.
        DdsImage rgbx = DdsImage.Parse(MaskFile(0x40, 32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0, 1, 1, pixels));
        Assert.That(rgbx.Format, Is.EqualTo(DdsFormat.RGBA8));
        AssertTexel(rgbx.DecodeRgba(0, 0), 0, 10 / 255f, 20 / 255f, 30 / 255f, 1);
    }

    [Test]
    public void Dx9Masks_24BitLuminanceAndAlphaFormats()
    {
        byte[] three = { 10, 20, 30 };
        DdsImage bgr = DdsImage.Parse(MaskFile(0x40, 24, 0xFF0000, 0x00FF00, 0x0000FF, 0, 1, 1, three));
        Assert.That(bgr.Format, Is.EqualTo(DdsFormat.BGR8));
        AssertTexel(bgr.DecodeRgba(0, 0), 0, 30 / 255f, 20 / 255f, 10 / 255f, 1);

        DdsImage rgb = DdsImage.Parse(MaskFile(0x40, 24, 0x0000FF, 0x00FF00, 0xFF0000, 0, 1, 1, three));
        Assert.That(rgb.Format, Is.EqualTo(DdsFormat.RGB8));
        AssertTexel(rgb.DecodeRgba(0, 0), 0, 10 / 255f, 20 / 255f, 30 / 255f, 1);

        DdsImage l8 = DdsImage.Parse(MaskFile(0x20000, 8, 0xFF, 0, 0, 0, 2, 1, new byte[] { 51, 204 }));
        Assert.That(l8.Format, Is.EqualTo(DdsFormat.R8));
        float[] l = l8.DecodeRgba(0, 0);
        AssertTexel(l, 0, 0.2f, 0, 0, 1);
        AssertTexel(l, 1, 0.8f, 0, 0, 1);

        DdsImage a8 = DdsImage.Parse(MaskFile(0x2, 8, 0, 0, 0, 0xFF, 1, 1, new byte[] { 51 }));
        Assert.That(a8.Format, Is.EqualTo(DdsFormat.A8));
        AssertTexel(a8.DecodeRgba(0, 0), 0, 0, 0, 0, 0.2f);

        DdsImage la = DdsImage.Parse(MaskFile(0x20001, 16, 0x00FF, 0, 0, 0xFF00, 1, 1, new byte[] { 51, 204 }));
        Assert.That(la.Format, Is.EqualTo(DdsFormat.RG8));
        AssertTexel(la.DecodeRgba(0, 0), 0, 0.2f, 0.8f, 0, 1);
    }

    [Test]
    public void HalfFloats_ConvertExactly()
    {
        // D3DFMT_A16B16G16R16F (FourCC 113): 1.0, -2.0, the smallest subnormal (2^-24), +infinity.
        DdsImage rgba16 = DdsImage.Parse(NumericFourCCFile(113, 1, 1, Halves(0x3C00, 0xC000, 0x0001, 0x7C00)));
        Assert.That(rgba16.Format, Is.EqualTo(DdsFormat.RGBA16F));
        float[] v = rgba16.DecodeRgba(0, 0);
        Assert.That(v[0], Is.EqualTo(1f));
        Assert.That(v[1], Is.EqualTo(-2f));
        Assert.That(v[2], Is.EqualTo((float)Math.Pow(2, -24)));
        Assert.That(v[3], Is.EqualTo(float.PositiveInfinity));

        // DXGI R16_FLOAT: NaN, -infinity, -0, the largest half, the largest subnormal, the smallest normal, a third.
        DdsImage r16 = DdsImage.Parse(Dx10File(54, 7, 1, Halves(0x7E00, 0xFC00, 0x8000, 0x7BFF, 0x03FF, 0x0400, 0x3555)));
        Assert.That(r16.Format, Is.EqualTo(DdsFormat.R16F));
        float[] r = r16.DecodeRgba(0, 0);
        Assert.That(float.IsNaN(r[0]), Is.True);
        Assert.That(r[4], Is.EqualTo(float.NegativeInfinity));
        Assert.That(r[8], Is.EqualTo(0f));
        Assert.That(1f / r[8], Is.EqualTo(float.NegativeInfinity), "negative zero");
        Assert.That(r[12], Is.EqualTo(65504f));
        Assert.That(r[16], Is.EqualTo((float)(1023 * Math.Pow(2, -24))));
        Assert.That(r[20], Is.EqualTo((float)Math.Pow(2, -14)));
        Assert.That(r[24], Is.EqualTo(0.333251953125f));
        AssertTexel(r, 6, 0.333251953125f, 0, 0, 1);

        DdsImage rg16 = DdsImage.Parse(NumericFourCCFile(112, 1, 1, Halves(0x3800, 0xB400)));
        Assert.That(rg16.Format, Is.EqualTo(DdsFormat.RG16F));
        AssertTexel(rg16.DecodeRgba(0, 0), 0, 0.5f, -0.25f, 0, 1);
    }

    [Test]
    public void Floats_DecodeAsStored()
    {
        byte[] data = Concat(BitConverter.GetBytes(0.25f), BitConverter.GetBytes(-3.5f), BitConverter.GetBytes(1e10f),
            BitConverter.GetBytes(2f));
        DdsImage rgba32 = DdsImage.Parse(Dx10File(2, 1, 1, data));
        Assert.That(rgba32.Format, Is.EqualTo(DdsFormat.RGBA32F));
        AssertTexel(rgba32.DecodeRgba(0, 0), 0, 0.25f, -3.5f, 1e10f, 2f);

        DdsImage r32 = DdsImage.Parse(NumericFourCCFile(114, 2, 1, Concat(BitConverter.GetBytes(7.5f), BitConverter.GetBytes(-1f))));
        Assert.That(r32.Format, Is.EqualTo(DdsFormat.R32F));
        float[] r = r32.DecodeRgba(0, 0);
        AssertTexel(r, 0, 7.5f, 0, 0, 1);
        AssertTexel(r, 1, -1f, 0, 0, 1);
    }

    // ---- Layout: cube maps, mips, arrays ----

    [Test]
    public void CubeMap_FacesStoredOneAfterAnother_EachWithAllItsMips()
    {
        // 8x8 BC1, 2 mips: mip 0 is 2x2 blocks (32 bytes), mip 1 one block (8 bytes), 40 bytes a face.
        var parts = new byte[12][];
        for (int face = 0; face < 6; face++)
        {
            byte[] top = SolidBc1(Red(face, 0) << 11);
            parts[face * 2] = Concat(top, top, top, top);
            parts[face * 2 + 1] = SolidBc1(Red(face, 1) << 11);
        }
        DdsImage dds = DdsImage.Parse(FourCCFile("DXT1", 8, 8, Concat(parts), mips: 2, caps2: 0x200 | 0xFC00));

        Assert.That(dds.IsCubemap, Is.True);
        Assert.That(dds.Faces, Is.EqualTo(6));
        Assert.That(dds.MipCount, Is.EqualTo(2));
        Assert.That(dds.Data.Length, Is.EqualTo(240));
        for (int face = 0; face < 6; face++)
        {
            Assert.That(dds.LevelOffset(face, 0), Is.EqualTo(face * 40));
            Assert.That(dds.LevelSize(face, 0), Is.EqualTo(32));
            Assert.That(dds.LevelOffset(face, 1), Is.EqualTo(face * 40 + 32));
            Assert.That(dds.LevelSize(face, 1), Is.EqualTo(8));
            for (int mip = 0; mip < 2; mip++)
            {
                float[] rgba = dds.DecodeRgba(face, mip);
                Assert.That(rgba.Length, Is.EqualTo(mip == 0 ? 256 : 64));
                int red = Red(face, mip);
                float expected = ((red << 3) | (red >> 2)) / 255f;
                for (int i = 0; i < rgba.Length; i += 4) Assert.That(rgba[i], Is.EqualTo(expected).Within(Tol), $"face {face} mip {mip}");
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => dds.LevelOffset(6, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => dds.LevelOffset(0, 2));

        DdsImage dx10Cube = DdsImage.Parse(Dx10File(71, 4, 4, new byte[48], misc: 0x4));
        Assert.That(dx10Cube.IsCubemap, Is.True);
        Assert.That(dx10Cube.Faces, Is.EqualTo(6));
        Assert.That(dx10Cube.LevelOffset(5, 0), Is.EqualTo(40));
    }

    [Test]
    public void MipChain_NonMultipleOfFourSize()
    {
        // 10x6 BC1: 10x6 (3x2 blocks), 5x3 (2x1), 2x1 (1x1), 1x1 (1x1).
        var blocks = new byte[10][];
        for (int k = 0; k < 6; k++) blocks[k] = SolidBc1((ushort)((k + 1) << 11));
        for (int k = 6; k < 10; k++) blocks[k] = SolidBc1(0x001F);
        DdsImage dds = DdsImage.Parse(FourCCFile("DXT1", 10, 6, Concat(blocks), mips: 4));

        Assert.That(dds.MipCount, Is.EqualTo(4));
        Assert.That(dds.Data.Length, Is.EqualTo(80));
        int[] widths = { 10, 5, 2, 1 }, heights = { 6, 3, 1, 1 }, sizes = { 48, 16, 8, 8 }, offsets = { 0, 48, 64, 72 };
        for (int mip = 0; mip < 4; mip++)
        {
            Assert.That(dds.LevelWidth(mip), Is.EqualTo(widths[mip]), $"mip {mip}");
            Assert.That(dds.LevelHeight(mip), Is.EqualTo(heights[mip]), $"mip {mip}");
            Assert.That(dds.LevelSize(0, mip), Is.EqualTo(sizes[mip]), $"mip {mip}");
            Assert.That(dds.LevelOffset(0, mip), Is.EqualTo(offsets[mip]), $"mip {mip}");
            Assert.That(dds.DecodeRgba(0, mip).Length, Is.EqualTo(widths[mip] * heights[mip] * 4), $"mip {mip}");
        }

        // Each texel of mip 0 comes from its own block; the edge blocks are cropped.
        float[] rgba = dds.DecodeRgba(0, 0);
        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                int red = (y / 4) * 3 + x / 4 + 1;
                float expected = ((red << 3) | (red >> 2)) / 255f;
                Assert.That(rgba[(y * 10 + x) * 4], Is.EqualTo(expected).Within(Tol), $"texel {x},{y}");
            }
        }
        AssertTexel(dds.DecodeRgba(0, 2), 1, 0, 0, 1, 1);
    }

    [Test]
    public void ArrayTexture_ReadsTheFirstSlice()
    {
        byte[] slices = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
        DdsImage dds = DdsImage.Parse(Dx10File(61, 2, 2, slices, arraySize: 3));
        Assert.That(dds.Format, Is.EqualTo(DdsFormat.R8));
        Assert.That(dds.Faces, Is.EqualTo(1));
        Assert.That(dds.Data.Length, Is.EqualTo(12));
        float[] rgba = dds.DecodeRgba(0, 0);
        Assert.That(rgba.Length, Is.EqualTo(16));
        AssertTexel(rgba, 3, 40 / 255f, 0, 0, 1);
    }

    [Test]
    public void MipCount_IgnoredWithoutTheFlag()
    {
        byte[] file = FourCCFile("DXT1", 8, 8, new byte[40], mips: 2);
        WriteU32(file, 8, 0x1007); // clear DDSD_MIPMAPCOUNT, keep mipMapCount = 2
        Assert.That(DdsImage.Parse(file).MipCount, Is.EqualTo(1));
    }

    // ---- Bad files ----

    [Test]
    public void BadFiles_ThrowFormatException()
    {
        byte[] good = FourCCFile("DXT1", 8, 8, new byte[32]);
        Assert.DoesNotThrow(() => DdsImage.Parse(good));

        byte[] badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<FormatException>(() => DdsImage.Parse(badMagic));
        Assert.Throws<FormatException>(() => DdsImage.Parse(new byte[0]));

        var shortHeader = new byte[64];
        Array.Copy(good, shortHeader, 64);
        Assert.Throws<FormatException>(() => DdsImage.Parse(shortHeader));

        var truncated = new byte[good.Length - 1];
        Array.Copy(good, truncated, truncated.Length);
        Assert.Throws<FormatException>(() => DdsImage.Parse(truncated));

        byte[] dx10 = Dx10File(71, 4, 4, new byte[8]);
        var dx10Truncated = new byte[130];
        Array.Copy(dx10, dx10Truncated, dx10Truncated.Length);
        Assert.Throws<FormatException>(() => DdsImage.Parse(dx10Truncated));

        Assert.Throws<FormatException>(() => DdsImage.Parse(FourCCFile("DXT1", 8, 8, new byte[32], mips: 2)), "second mip missing");
        Assert.Throws<FormatException>(() => DdsImage.Parse(FourCCFile("DXT1", 4, 4, new byte[64], mips: 4)), "more mips than 4x4 has");
        Assert.Throws<FormatException>(() => DdsImage.Parse(FourCCFile("ETC1", 4, 4, new byte[8])), "unknown FourCC");
        Assert.Throws<FormatException>(() => DdsImage.Parse(Dx10File(95, 4, 4, new byte[16])), "BC6H");
        Assert.Throws<FormatException>(() => DdsImage.Parse(MaskFile(0x40, 16, 0xF800, 0x07E0, 0x001F, 0, 1, 1, new byte[2])), "RGB565");
    }

    // ---- BC7 (expected values worked out by hand from the D3D11 BC7 spec) ----

    [Test]
    public void Bc7_Mode6_SingleSubsetWithPBitsAndFourBitIndices()
    {
        var bits = new BlockBits();
        bits.Put(1 << 6, 7); // mode 6
        bits.Put(127, 7).Put(0, 7); // R0, R1
        bits.Put(0, 7).Put(127, 7); // G0, G1
        bits.Put(64, 7).Put(32, 7); // B0, B1
        bits.Put(127, 7).Put(0, 7); // A0, A1
        bits.Put(1, 1).Put(0, 1); // p-bits: endpoint 0 gets 1, endpoint 1 gets 0
        bits.Put(0, 3).Put(15, 4).Put(8, 4).Put(4, 4); // texel 0 (anchor, 3 bits), texels 1-3
        for (int i = 4; i < 16; i++) bits.Put(0, 4);
        Assert.That(bits.Count, Is.EqualTo(128));

        // Endpoints (7 bits + p-bit = 8 bits): e0 = (255, 1, 129, 255), e1 = (0, 254, 64, 0).
        // Index 8 has weight 34: ((64 - 34) e0 + 34 e1 + 32) >> 6 = (120, 135, 94, 120).
        // Index 4 has weight 17: ((64 - 17) e0 + 17 e1 + 32) >> 6 = (187, 68, 112, 187).
        float[] rgba = DecodeBc7(bits.Bytes);
        AssertTexel255(rgba, 0, 255, 1, 129, 255);
        AssertTexel255(rgba, 1, 0, 254, 64, 0);
        AssertTexel255(rgba, 2, 120, 135, 94, 120);
        AssertTexel255(rgba, 3, 187, 68, 112, 187);
        AssertTexel255(rgba, 15, 255, 1, 129, 255);
    }

    [Test]
    public void Bc7_Mode1_TwoSubsetsWithSharedPBitsAndAnchor()
    {
        var bits = new BlockBits();
        bits.Put(1 << 1, 2); // mode 1
        bits.Put(0, 6); // partition 0: columns 2 and 3 are subset 1, whose anchor is texel 15
        bits.Put(63, 6).Put(0, 6).Put(0, 6).Put(0, 6); // R of e0, e1 (subset 0), e2, e3 (subset 1)
        bits.Put(0, 6).Put(0, 6).Put(63, 6).Put(0, 6); // G
        bits.Put(0, 6).Put(0, 6).Put(0, 6).Put(63, 6); // B
        bits.Put(1, 1).Put(0, 1); // shared p-bits: subset 0 gets 1, subset 1 gets 0
        bits.Put(0, 2).Put(4, 3).Put(7, 3).Put(0, 3); // texels 0 (anchor, 2 bits), 1, 2, 3
        for (int i = 4; i < 15; i++) bits.Put(0, 3);
        bits.Put(3, 2); // texel 15, the anchor of subset 1: 2 bits
        Assert.That(bits.Count, Is.EqualTo(128));

        // Subset 0 (6 bits + p 1 = 7 bits): e0 = (127, 1, 1) -> (255, 2, 2); e1 = (1, 1, 1) -> (2, 2, 2).
        // Subset 1 (p 0): e2 = (0, 126, 0) -> (0, 253, 0); e3 = (0, 0, 253).
        // Texel 1, index 4 (weight 37): ((27 * 255 + 37 * 2 + 32) >> 6, 2, 2) = (109, 2, 2).
        // Texel 15, index 3 (weight 27): (0, (37 * 253 + 32) >> 6, (27 * 253 + 32) >> 6) = (0, 146, 107).
        float[] rgba = DecodeBc7(bits.Bytes);
        AssertTexel255(rgba, 0, 255, 2, 2, 255);
        AssertTexel255(rgba, 1, 109, 2, 2, 255);
        AssertTexel255(rgba, 2, 0, 0, 253, 255);
        AssertTexel255(rgba, 3, 0, 253, 0, 255);
        AssertTexel255(rgba, 4, 255, 2, 2, 255);
        AssertTexel255(rgba, 6, 0, 253, 0, 255);
        AssertTexel255(rgba, 15, 0, 146, 107, 255);
    }

    [Test]
    public void Bc7_Mode5_RotationSwapsRedAndAlpha()
    {
        var bits = new BlockBits();
        bits.Put(1 << 5, 6); // mode 5
        bits.Put(1, 2); // rotation 1: red and alpha swap
        bits.Put(127, 7).Put(0, 7); // R0, R1
        bits.Put(64, 7).Put(64, 7); // G0, G1
        bits.Put(0, 7).Put(0, 7); // B0, B1
        bits.Put(0, 8).Put(200, 8); // A0, A1
        bits.Put(0, 1).Put(2, 2); // colour indices: texel 0 (anchor, 1 bit), texel 1
        for (int i = 2; i < 16; i++) bits.Put(0, 2);
        bits.Put(0, 1).Put(3, 2); // alpha indices: texel 0 (anchor, 1 bit), texel 1
        for (int i = 2; i < 16; i++) bits.Put(0, 2);
        Assert.That(bits.Count, Is.EqualTo(128));

        // Colour 7 bits: R (255, 0), G (129, 129), B 0; alpha 8 bits (0, 200).
        // Texel 1: colour weight 43 -> R (21 * 255 + 32) >> 6 = 84; alpha weight 64 -> 200; then R and A swap.
        float[] rgba = DecodeBc7(bits.Bytes);
        AssertTexel255(rgba, 0, 0, 129, 0, 255);
        AssertTexel255(rgba, 1, 200, 129, 0, 84);
        AssertTexel255(rgba, 2, 0, 129, 0, 255);
    }

    [Test]
    public void Bc7_Mode4_IndexSelectionGivesColourTheThreeBitIndices()
    {
        var bits = new BlockBits();
        bits.Put(1 << 4, 5); // mode 4
        bits.Put(0, 2); // no rotation
        bits.Put(1, 1); // index selection 1: colour uses the 3-bit indices, alpha the 2-bit ones
        bits.Put(31, 5).Put(0, 5); // R0, R1
        bits.Put(0, 5).Put(0, 5); // G0, G1
        bits.Put(0, 5).Put(0, 5); // B0, B1
        bits.Put(63, 6).Put(0, 6); // A0, A1
        bits.Put(0, 1).Put(1, 2).Put(0, 2); // 2-bit indices: texel 0 (anchor, 1 bit), texels 1, 2
        for (int i = 3; i < 16; i++) bits.Put(0, 2);
        bits.Put(0, 2).Put(0, 3).Put(5, 3); // 3-bit indices: texel 0 (anchor, 2 bits), texels 1, 2
        for (int i = 3; i < 16; i++) bits.Put(0, 3);
        Assert.That(bits.Count, Is.EqualTo(128));

        // R (255, 0), A (255, 0). Texel 1: alpha weight 21 -> (43 * 255 + 32) >> 6 = 171.
        // Texel 2: colour weight 46 -> (18 * 255 + 32) >> 6 = 72.
        float[] rgba = DecodeBc7(bits.Bytes);
        AssertTexel255(rgba, 0, 255, 0, 0, 255);
        AssertTexel255(rgba, 1, 255, 0, 0, 171);
        AssertTexel255(rgba, 2, 72, 0, 0, 255);
    }

    [Test]
    public void Bc7_ReservedModeIsTransparentBlack_AndDx10FileDecodes()
    {
        var texels = new float[64];
        for (int i = 0; i < 64; i++) texels[i] = 0.5f;
        BlockDecoder.DecodeBC7(new byte[16], 0, texels);
        for (int i = 0; i < 64; i++) Assert.That(texels[i], Is.EqualTo(0f));

        var bits = new BlockBits();
        bits.Put(1 << 6, 7).Put(127, 7).Put(127, 7).Put(0, 7).Put(0, 7).Put(0, 7).Put(0, 7).Put(127, 7).Put(127, 7).Put(1, 1).Put(1, 1);
        DdsImage dds = DdsImage.Parse(Dx10File(99, 4, 4, bits.Bytes));
        Assert.That(dds.Format, Is.EqualTo(DdsFormat.BC7));
        Assert.That(dds.IsSrgb, Is.True);
        float[] rgba = dds.DecodeRgba(0, 0);
        for (int i = 0; i < 16; i++) AssertTexel(rgba, i, 1, 1 / 255f, 1 / 255f, 1);
    }

    // ---- Helpers ----

    private static void AssertTexel(float[] rgba, int texel, float r, float g, float b, float a)
    {
        string where = $"texel {texel}";
        Assert.That(rgba[texel * 4], Is.EqualTo(r).Within(Tol), where + " red");
        Assert.That(rgba[texel * 4 + 1], Is.EqualTo(g).Within(Tol), where + " green");
        Assert.That(rgba[texel * 4 + 2], Is.EqualTo(b).Within(Tol), where + " blue");
        Assert.That(rgba[texel * 4 + 3], Is.EqualTo(a).Within(Tol), where + " alpha");
    }

    private static void AssertTexel255(float[] rgba, int texel, int r, int g, int b, int a)
        => AssertTexel(rgba, texel, r / 255f, g / 255f, b / 255f, a / 255f);

    private static float[] DecodeBc7(byte[] block)
    {
        var texels = new float[64];
        BlockDecoder.DecodeBC7(block, 0, texels);
        return texels;
    }

    /// <summary>A distinct 5-bit red for each face and mip of the cube map test.</summary>
    private static int Red(int face, int mip) => face * 2 + mip + 1;

    private static byte[] SolidBc1(int colour) => Bc1Block((ushort)colour, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static byte[] Bc1Block(ushort c0, ushort c1, params int[] indices)
    {
        uint bits = 0;
        for (int i = 0; i < 16; i++) bits |= (uint)indices[i] << (2 * i);
        return new[] { (byte)c0, (byte)(c0 >> 8), (byte)c1, (byte)(c1 >> 8), (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24) };
    }

    private static byte[] Bc4Block(byte a0, byte a1, params int[] indices)
    {
        ulong bits = 0;
        for (int i = 0; i < 16; i++) bits |= (ulong)indices[i] << (3 * i);
        var block = new byte[8];
        block[0] = a0;
        block[1] = a1;
        for (int k = 0; k < 6; k++) block[2 + k] = (byte)(bits >> (8 * k));
        return block;
    }

    private static byte[] Halves(params int[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            bytes[2 * i] = (byte)values[i];
            bytes[2 * i + 1] = (byte)(values[i] >> 8);
        }
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int length = 0;
        foreach (byte[] p in parts) length += p.Length;
        var all = new byte[length];
        int at = 0;
        foreach (byte[] p in parts)
        {
            Buffer.BlockCopy(p, 0, all, at, p.Length);
            at += p.Length;
        }
        return all;
    }

    /// <summary>A 128-byte DDS header (magic included) with the given pixel format, followed by any extra bytes.</summary>
    private static byte[] Header(int width, int height, int mips, uint caps2, uint pfFlags, uint fourCC, uint bitCount,
        uint r, uint g, uint b, uint a)
    {
        var h = new byte[128];
        WriteU32(h, 0, 0x20534444); // "DDS "
        WriteU32(h, 4, 124);
        WriteU32(h, 8, 0x1007 | (mips > 0 ? 0x20000u : 0u)); // CAPS | HEIGHT | WIDTH | PIXELFORMAT (| MIPMAPCOUNT)
        WriteU32(h, 12, (uint)height);
        WriteU32(h, 16, (uint)width);
        WriteU32(h, 28, (uint)mips);
        WriteU32(h, 76, 32);
        WriteU32(h, 80, pfFlags);
        WriteU32(h, 84, fourCC);
        WriteU32(h, 88, bitCount);
        WriteU32(h, 92, r);
        WriteU32(h, 96, g);
        WriteU32(h, 100, b);
        WriteU32(h, 104, a);
        WriteU32(h, 108, 0x1000 | (mips > 1 ? 0x400008u : 0u) | (caps2 != 0 ? 0x8u : 0u));
        WriteU32(h, 112, caps2);
        return h;
    }

    private static byte[] FourCCFile(string fourCC, int width, int height, byte[] data, int mips = 0, uint caps2 = 0)
        => Concat(Header(width, height, mips, caps2, 0x4, Code(fourCC), 0, 0, 0, 0, 0), data);

    private static byte[] NumericFourCCFile(uint fourCC, int width, int height, byte[] data)
        => Concat(Header(width, height, 0, 0, 0x4, fourCC, 0, 0, 0, 0, 0), data);

    private static byte[] MaskFile(uint pfFlags, uint bitCount, uint r, uint g, uint b, uint a, int width, int height, byte[] data)
        => Concat(Header(width, height, 0, 0, pfFlags, 0, bitCount, r, g, b, a), data);

    private static byte[] Dx10File(uint dxgi, int width, int height, byte[] data, int mips = 0, uint misc = 0, uint arraySize = 1)
    {
        var dx10 = new byte[20];
        WriteU32(dx10, 0, dxgi);
        WriteU32(dx10, 4, 3); // TEXTURE2D
        WriteU32(dx10, 8, misc);
        WriteU32(dx10, 12, arraySize);
        return Concat(Header(width, height, mips, 0, 0x4, Code("DX10"), 0, 0, 0, 0, 0), dx10, data);
    }

    private static uint Code(string s) => (uint)(s[0] | (s[1] << 8) | (s[2] << 16) | (s[3] << 24));

    private static void WriteU32(byte[] b, int offset, uint v)
    {
        b[offset] = (byte)v;
        b[offset + 1] = (byte)(v >> 8);
        b[offset + 2] = (byte)(v >> 16);
        b[offset + 3] = (byte)(v >> 24);
    }

    /// <summary>Builds a 128-bit BC7 block field by field, lowest bit first.</summary>
    private sealed class BlockBits
    {
        public readonly byte[] Bytes = new byte[16];
        public int Count { get; private set; }

        public BlockBits Put(int value, int bits)
        {
            for (int i = 0; i < bits; i++, Count++)
                if (((value >> i) & 1) != 0) Bytes[Count >> 3] |= (byte)(1 << (Count & 7));
            return this;
        }
    }
}
