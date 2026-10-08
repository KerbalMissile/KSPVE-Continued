using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Pipeline;

public enum RtFormat
{
    RGBA16F,
    R11G11B10F,
    RG16F,
    R16F,
    R32F,
    RGBA8,
    R8,
    RGBA32F,
}

public enum RtDimension
{
    Tex2D,
    Tex3D,
    Cube,
}

public readonly struct RenderTargetDesc : IEquatable<RenderTargetDesc>
{
    public RenderTargetDesc(int width, int height, int volumeDepth, RtFormat format, RtDimension dimension, int mipCount, bool randomWrite)
    {
        Width = width;
        Height = height;
        VolumeDepth = volumeDepth;
        Format = format;
        Dimension = dimension;
        MipCount = mipCount;
        RandomWrite = randomWrite;
    }

    public int Width { get; }
    public int Height { get; }
    public int VolumeDepth { get; }
    public RtFormat Format { get; }
    public RtDimension Dimension { get; }
    public int MipCount { get; }
    public bool RandomWrite { get; }

    public bool Equals(RenderTargetDesc o) =>
        Width == o.Width && Height == o.Height && VolumeDepth == o.VolumeDepth && Format == o.Format &&
        Dimension == o.Dimension && MipCount == o.MipCount && RandomWrite == o.RandomWrite;

    public override bool Equals(object obj) => obj is RenderTargetDesc o && Equals(o);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = Width;
            h = h * 31 + Height;
            h = h * 31 + VolumeDepth;
            h = h * 31 + (int)Format;
            h = h * 31 + (int)Dimension;
            h = h * 31 + MipCount;
            h = h * 31 + (RandomWrite ? 1 : 0);
            return h;
        }
    }

    public static bool operator ==(RenderTargetDesc a, RenderTargetDesc b) => a.Equals(b);
    public static bool operator !=(RenderTargetDesc a, RenderTargetDesc b) => !a.Equals(b);
}

public static class RtMath
{
    public static int BytesPerPixel(RtFormat format)
    {
        switch (format)
        {
            case RtFormat.RGBA16F: return 8;
            case RtFormat.R11G11B10F: return 4;
            case RtFormat.RG16F: return 4;
            case RtFormat.R16F: return 2;
            case RtFormat.R32F: return 4;
            case RtFormat.RGBA8: return 4;
            case RtFormat.R8: return 1;
            case RtFormat.RGBA32F: return 16;
            default: return 4;
        }
    }

    /// <summary>Approximate GPU memory: sum over the mip chain, ignoring driver padding.</summary>
    public static long EstimateBytes(RenderTargetDesc desc)
    {
        long w = Math.Max(1, desc.Width);
        long h = Math.Max(1, desc.Height);
        long d = desc.Dimension == RtDimension.Tex3D ? Math.Max(1, desc.VolumeDepth) : 1;
        int mips = Math.Max(1, desc.MipCount);
        long pixels = 0;
        for (int m = 0; m < mips; m++)
        {
            pixels += w * h * d;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            if (desc.Dimension == RtDimension.Tex3D) d = Math.Max(1, d / 2);
        }
        if (desc.Dimension == RtDimension.Cube) pixels *= 6;
        return pixels * BytesPerPixel(desc.Format);
    }
}

/// <summary>Bookkeeping for pooled render targets; the Unity layer maps handles to real textures.</summary>
public sealed class RenderTargetLedger
{
    private sealed class Entry
    {
        public int Handle;
        public RenderTargetDesc Desc;
        public long Bytes;
        public bool InUse;
        public long ReleasedFrame;
    }

    private readonly List<Entry> _entries = new();
    private readonly List<int> _trimmed = new();
    private int _nextHandle = 1;

    public long TotalBytes { get; private set; }
    public long InUseBytes { get; private set; }

    public int Acquire(RenderTargetDesc desc, long frame)
    {
        foreach (var e in _entries)
        {
            if (e.InUse || e.Desc != desc) continue;
            e.InUse = true;
            InUseBytes += e.Bytes;
            return e.Handle;
        }
        var n = new Entry { Handle = _nextHandle++, Desc = desc, Bytes = RtMath.EstimateBytes(desc), InUse = true };
        _entries.Add(n);
        TotalBytes += n.Bytes;
        InUseBytes += n.Bytes;
        return n.Handle;
    }

    public void Release(int handle, long frame)
    {
        foreach (var e in _entries)
        {
            if (e.Handle != handle) continue;
            if (!e.InUse) return;
            e.InUse = false;
            e.ReleasedFrame = frame;
            InUseBytes -= e.Bytes;
            return;
        }
    }

    /// <summary>Removes released entries idle for more than <paramref name="maxIdleFrames"/>; the list is reused between calls.</summary>
    public IReadOnlyList<int> Trim(long currentFrame, int maxIdleFrames)
    {
        _trimmed.Clear();
        long limit = Math.Max(0, maxIdleFrames);
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (e.InUse || currentFrame - e.ReleasedFrame <= limit) continue;
            _trimmed.Add(e.Handle);
            TotalBytes -= e.Bytes;
            _entries.RemoveAt(i--);
        }
        return _trimmed;
    }
}
