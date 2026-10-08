using KSPVE.Pure.Pipeline;
using NUnit.Framework;

namespace KSPVE.Pure.Tests.Pipeline;

[TestFixture]
public sealed class RenderTargetLedgerTests
{
    private static RenderTargetDesc D2(int w, int h, RtFormat f = RtFormat.RGBA16F, int mips = 1)
        => new(w, h, 1, f, RtDimension.Tex2D, mips, false);

    [TestCase(RtFormat.RGBA16F, 8), TestCase(RtFormat.R11G11B10F, 4), TestCase(RtFormat.RG16F, 4), TestCase(RtFormat.R16F, 2)]
    [TestCase(RtFormat.R32F, 4), TestCase(RtFormat.RGBA8, 4), TestCase(RtFormat.R8, 1), TestCase(RtFormat.RGBA32F, 16)]
    public void BytesPerPixel(RtFormat f, int bpp) => Assert.That(RtMath.BytesPerPixel(f), Is.EqualTo(bpp));

    [Test]
    public void EstimateBytesMipChain()
    {
        Assert.That(RtMath.EstimateBytes(D2(4, 4, RtFormat.RGBA8, 1)), Is.EqualTo(64));
        Assert.That(RtMath.EstimateBytes(D2(4, 4, RtFormat.RGBA8, 3)), Is.EqualTo(4 * (16 + 4 + 1)));
        Assert.That(RtMath.EstimateBytes(D2(4, 2, RtFormat.R8, 4)), Is.EqualTo(8 + 2 + 1 + 1));
    }

    [Test]
    public void EstimateBytesCubeAnd3D()
    {
        var cube = new RenderTargetDesc(8, 8, 1, RtFormat.R8, RtDimension.Cube, 2, false);
        Assert.That(RtMath.EstimateBytes(cube), Is.EqualTo(6 * (64 + 16)));
        var vol = new RenderTargetDesc(4, 4, 4, RtFormat.R8, RtDimension.Tex3D, 2, true);
        Assert.That(RtMath.EstimateBytes(vol), Is.EqualTo(64 + 8));
    }

    [Test]
    public void EstimateBytesLargeDoesNotOverflow()
    {
        var big = new RenderTargetDesc(16384, 16384, 1, RtFormat.RGBA32F, RtDimension.Tex2D, 1, false);
        Assert.That(RtMath.EstimateBytes(big), Is.EqualTo(16384L * 16384L * 16));
    }

    [Test]
    public void DescValueEquality()
    {
        Assert.That(D2(4, 4), Is.EqualTo(D2(4, 4)));
        Assert.That(D2(4, 4) == D2(4, 4), Is.True);
        Assert.That(D2(4, 4) != D2(4, 5), Is.True);
        Assert.That(D2(4, 4).GetHashCode(), Is.EqualTo(D2(4, 4).GetHashCode()));
        Assert.That(new RenderTargetDesc(4, 4, 1, RtFormat.R8, RtDimension.Tex2D, 1, true),
            Is.Not.EqualTo(new RenderTargetDesc(4, 4, 1, RtFormat.R8, RtDimension.Tex2D, 1, false)));
    }

    [Test]
    public void ReleasedEntryIsReusedOnlyForEqualDesc()
    {
        var l = new RenderTargetLedger();
        int a = l.Acquire(D2(4, 4), 0);
        l.Release(a, 1);
        Assert.That(l.Acquire(D2(8, 8), 2), Is.Not.EqualTo(a));
        Assert.That(l.Acquire(D2(4, 4), 2), Is.EqualTo(a));
    }

    [Test]
    public void ByteAccounting()
    {
        var l = new RenderTargetLedger();
        int a = l.Acquire(D2(4, 4, RtFormat.RGBA8), 0); // 64
        int b = l.Acquire(D2(4, 4, RtFormat.RGBA8), 0); // 64, distinct since a in use
        Assert.That(b, Is.Not.EqualTo(a));
        Assert.That(l.TotalBytes, Is.EqualTo(128));
        Assert.That(l.InUseBytes, Is.EqualTo(128));
        l.Release(a, 1);
        l.Release(a, 1); // double release ignored
        l.Release(999, 1); // unknown ignored
        Assert.That(l.InUseBytes, Is.EqualTo(64));
        Assert.That(l.TotalBytes, Is.EqualTo(128));
    }

    [Test]
    public void TrimReturnsIdleHandlesAndForgetsThem()
    {
        var l = new RenderTargetLedger();
        int a = l.Acquire(D2(4, 4, RtFormat.RGBA8), 0);
        int b = l.Acquire(D2(2, 2, RtFormat.RGBA8), 0);
        l.Acquire(D2(1, 1, RtFormat.RGBA8), 0); // stays in use
        l.Release(a, 1);
        l.Release(b, 5);
        var t = l.Trim(6, 3); // a idle 5 > 3; b idle 1; third in use
        Assert.That(t, Is.EqualTo(new[] { a }));
        Assert.That(l.TotalBytes, Is.EqualTo(16 + 4));
        Assert.That(l.Trim(6, 3), Is.Empty);
        Assert.That(l.Trim(8, 3), Is.Empty);               // idle exactly 3, not longer
        Assert.That(l.Trim(9, 3), Is.EqualTo(new[] { b }));
        Assert.That(l.TotalBytes, Is.EqualTo(4));
    }
}
