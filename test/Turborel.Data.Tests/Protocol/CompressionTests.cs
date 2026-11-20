// Compressed frames: sent as kind z where the client's threshold and a
// shorter result say so, inflated to at most their length, and refused
// nested, too long, short, past their length or not granted
// [PROTOCOL Compressed frames].

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="Compression"/>.</summary>
public sealed class CompressionTests
{
    /// <summary>A long body that compresses goes as kind z, its kind and length first, and reads back.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void LongFrameGoesCompressedAndReadsBack()
    {
        byte[] body = [.. Enumerable.Repeat((byte)'a', 4000)];
        var w = new WireWriter();

        Assert.True(Compression.WriteFrame(w, (byte)'Q', body, Compression.DefaultThreshold));
        (byte kind, byte[] compressed) = Wire.Unframe(w.ToArray());
        Assert.Equal(Compression.Kind, kind);
        Assert.Equal(Wire.Hex("51 A00F0000"), compressed[..5]);
        (byte held, ReadOnlyMemory<byte> inflated) = Compression.Received(kind, compressed, granted: true, FrameHeader.MaxBody);
        Assert.Equal((byte)'Q', held);
        Assert.Equal(body, inflated.ToArray());
    }

    /// <summary>A frame under the threshold, or one compressing would not shorten, goes as it is.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void ShortOrIncompressibleFrameGoesAsItIs()
    {
        byte[] small = [.. Enumerable.Repeat((byte)'a', 100)];
        byte[] noise = [.. Enumerable.Range(0, 2000).Select(i => (byte)((i * 7919) ^ (i >> 3)))];
        var w = new WireWriter();

        Assert.False(Compression.WriteFrame(w, (byte)'Q', small, Compression.DefaultThreshold));
        Assert.Equal(Wire.Frame('Q', small), w.ToArray());
        w.Clear();
        Assert.True(Compression.WriteFrame(w, (byte)'Q', small, threshold: 1));
        w.Clear();
        _ = Compression.WriteFrame(w, (byte)'Q', Deflated(noise), Compression.DefaultThreshold);
        Assert.Equal((byte)'Q', w.ToArray()[0]);
    }

    /// <summary>A frame of another kind is given as it came.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void OtherKindsPassAsTheyCame()
    {
        (byte kind, ReadOnlyMemory<byte> body) = Compression.Received((byte)'G', Array.Empty<byte>(), granted: false, FrameHeader.MaxBody);

        Assert.Equal(((byte)'G', 0), (kind, body.Length));
    }

    /// <summary>
    /// A z frame not granted, holding another z, declaring more than taken,
    /// inflating short of or past its length, or no zlib stream fails the
    /// connection.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void MalformedCompressedFramesFail()
    {
        byte[] stream = Deflated([.. Enumerable.Repeat((byte)'b', 5000)]);

        Wire.Fails(() => Compression.Received(Compression.Kind, Body('B', 5000, stream), granted: false, FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('z', 5000, stream), FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('B', 5000, stream), 4999), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('B', 6000, stream), FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('B', 4000, stream), FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('B', 3, Wire.Ascii("abc")), FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Wire.Hex("42 0100"), FrameHeader.MaxBody), Sections.Frames);
        byte[] damaged = [.. stream];
        damaged[^1] ^= 0xFF;
        Wire.Fails(() => Compression.Decompress(Body('B', 5000, damaged), FrameHeader.MaxBody), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(Body('B', 5000, [.. stream, 0x00]), FrameHeader.MaxBody), Sections.CompressedFrames);
    }

    private static byte[] Body(char kind, uint length, byte[] stream) =>
        Wire.Join([(byte)kind], Wire.Written(w => w.WriteU32(length)), stream);

    private static byte[] Deflated(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
