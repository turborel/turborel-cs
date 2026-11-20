// The frame header read back as written, and refused where the protocol
// fails the connection [PROTOCOL Frames].

using System;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="FrameHeader"/>.</summary>
public sealed class FrameHeaderTests
{
    /// <summary>
    /// The headers of the page's worked example: Hello, 41 bytes in all,
    /// and Ping, a header alone.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    [Verifies("[PROTOCOL A worked example]")]
    public void WorkedExampleHeadersReadBackAsWritten()
    {
        byte[] hello = [0x48, 0x24, 0x00, 0x00, 0x00];
        byte[] ping = [0x47, 0x00, 0x00, 0x00, 0x00];

        Assert.Equal(new FrameHeader((byte)'H', 36), FrameHeader.Decode(hello, FrameHeader.MaxBody));
        Assert.Equal(new FrameHeader((byte)'G', 0), FrameHeader.Decode(ping, FrameHeader.MaxBody));
        Assert.Equal(hello, Encoded(new FrameHeader((byte)'H', 36)));
        Assert.Equal(ping, Encoded(new FrameHeader((byte)'G', 0)));
    }

    /// <summary>
    /// The length is a u32 written least significant byte first, at its
    /// boundaries, and the kind is any u8.
    /// </summary>
    /// <param name="kind">The kind written.</param>
    /// <param name="length">The length written.</param>
    /// <param name="bytes">The header expected, in hexadecimal.</param>
    [Theory]
    [Verifies("[PROTOCOL Frames]")]
    [InlineData(0x00, 0u, "0000000000")]
    [InlineData(0xFF, 1u, "FF01000000")]
    [InlineData(0x51, 0x0403_0201u, "5101020304")]
    [InlineData(0x42, uint.MaxValue, "42FFFFFFFF")]
    public void LengthIsLittleEndian(byte kind, uint length, string bytes)
    {
        var header = new FrameHeader(kind, length);

        Assert.Equal(bytes, Convert.ToHexString(Encoded(header)));
        Assert.Equal(header, FrameHeader.Decode(Convert.FromHexString(bytes), uint.MaxValue));
    }

    /// <summary>
    /// A body of the longest length taken is read; one byte longer fails
    /// the connection, SQLCODE -919 and SQLSTATE 08006.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    public void BodyLongerThanTakenFailsTheConnection()
    {
        byte[] longest = Encoded(new FrameHeader((byte)'B', FrameHeader.MaxBody));
        byte[] longer = Encoded(new FrameHeader((byte)'B', FrameHeader.MaxBody + 1));

        Assert.Equal(FrameHeader.MaxBody, FrameHeader.Decode(longest, FrameHeader.MaxBody).Length);
        TurborelException failure = Assert.Throws<TurborelException>(() => FrameHeader.Decode(longer, FrameHeader.MaxBody));
        Assert.Equal((-919, "08006"), (failure.SqlCode, failure.SqlState));
        Assert.Equal(-919, failure.ErrorCode);
        Assert.StartsWith("[PROTOCOL Frames]", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A header cut short fails the connection; one written where there is
    /// no room for it writes nothing.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    public void HeaderCutShortIsRefused()
    {
        byte[] cut = [0x4B, 0x0E, 0x00, 0x00];
        byte[] room = new byte[FrameHeader.Size - 1];

        TurborelException failure = Assert.Throws<TurborelException>(() => FrameHeader.Decode(cut, FrameHeader.MaxBody));
        Assert.Equal((-919, "08006"), (failure.SqlCode, failure.SqlState));
        Assert.False(new FrameHeader((byte)'K', 14).TryEncode(room));
        Assert.All(room, octet => Assert.Equal(0, octet));
    }

    private static byte[] Encoded(FrameHeader header)
    {
        byte[] bytes = new byte[FrameHeader.Size];
        Assert.True(header.TryEncode(bytes));
        return bytes;
    }
}
