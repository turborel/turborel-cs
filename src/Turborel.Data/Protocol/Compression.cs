// Compressed frames, kind z, once the capability `compression` is
// granted: the held frame's kind, its body's length, then a zlib stream of
// the body, which the BCL's ZLibStream writes and the driver's own
// inflater, ported from turborel's, reads, as it knows where a stream
// ends. A frame is sent compressed only where it is at least the
// client's threshold and comes out shorter; one received is inflated to
// at most its length, itself at most the longest body taken, and no z
// holds another [PROTOCOL Compressed frames].

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Turborel.Data.Protocol.Zlib;

namespace Turborel.Data.Protocol;

/// <summary>Compresses frames the client sends, and inflates those it receives.</summary>
internal static class Compression
{
    /// <summary>The kind of a compressed frame, the same both ways.</summary>
    internal const byte Kind = (byte)'z';

    /// <summary>
    /// The client's threshold unless the program chooses another: a frame
    /// shorter than this, header included, is sent as it is, as the server's
    /// own default, SERVER.COMPRESSION_MIN_BYTES, is.
    /// </summary>
    internal const int DefaultThreshold = 1024;

    /// <summary>The bytes of a compressed frame's body before its stream: the held kind and length.</summary>
    private const int Prefix = sizeof(byte) + sizeof(uint);

    /// <summary>
    /// Writes the frame of <paramref name="kind"/> and <paramref name="body"/>,
    /// compressed where it is at least <paramref name="threshold"/> bytes and
    /// comes out shorter, else as it is.
    /// </summary>
    /// <param name="w">The writer.</param>
    /// <param name="kind">The frame's kind.</param>
    /// <param name="body">The frame's body.</param>
    /// <param name="threshold">The least bytes, header included, of a frame compressed.</param>
    /// <returns>Whether it was sent compressed.</returns>
    internal static bool WriteFrame(WireWriter w, byte kind, ReadOnlySpan<byte> body, int threshold)
    {
        byte[]? stream = FrameHeader.Size + body.Length >= threshold ? Deflate(body) : null;
        bool shorter = stream is not null && stream.Length + Prefix < body.Length;
        int start = w.BeginFrame(shorter ? Kind : kind);
        if (shorter)
        {
            w.WriteU8(kind);
            w.WriteU32((uint)body.Length);
            w.WriteRaw(stream);
        }
        else
        {
            w.WriteRaw(body);
        }

        w.EndFrame(start);
        return shorter;
    }

    /// <summary>
    /// Gives the frame a received frame holds: a <c>z</c> frame inflated,
    /// once <c>compression</c> was granted, any other as it came.
    /// </summary>
    /// <param name="kind">The received frame's kind.</param>
    /// <param name="body">Its body.</param>
    /// <param name="granted">Whether <c>compression</c> was granted.</param>
    /// <param name="maxBody">The longest body taken.</param>
    /// <returns>The kind and body of the message.</returns>
    /// <exception cref="TurborelException">A connection failure for a <c>z</c> frame not granted, or as <see cref="Decompress"/>.</exception>
    internal static (byte Kind, ReadOnlyMemory<byte> Body) Received(byte kind, ReadOnlyMemory<byte> body, bool granted, uint maxBody)
    {
        if (kind != Kind)
        {
            return (kind, body);
        }

        if (!granted)
        {
            throw ConnectionFailure.Of($"{Sections.CompressedFrames} a compressed frame came, compression not granted");
        }

        (byte held, byte[] inflated) = Decompress(body, maxBody);
        return (held, inflated);
    }

    /// <summary>Inflates the body of a compressed frame.</summary>
    /// <param name="body">The <c>z</c> frame's body.</param>
    /// <param name="maxBody">The longest body taken.</param>
    /// <returns>The held frame's kind and body.</returns>
    /// <exception cref="TurborelException">
    /// A connection failure if it holds another <c>z</c> frame, declares more
    /// than <paramref name="maxBody"/>, is no zlib stream, or inflates to
    /// other than it declares.
    /// </exception>
    internal static (byte Kind, byte[] Body) Decompress(ReadOnlyMemory<byte> body, uint maxBody)
    {
        var r = new WireReader(body, "compressed frame");
        byte kind = r.ReadU8();
        uint length = r.ReadU32();
        if (kind == Kind)
        {
            throw r.Malformed(Sections.CompressedFrames, "a compressed frame holds another");
        }

        if (length > maxBody)
        {
            throw r.Malformed(
                Sections.CompressedFrames,
                string.Create(CultureInfo.InvariantCulture, $"a compressed message of {length} bytes is longer than the {maxBody} taken"));
        }

        return (kind, Inflate(r, (int)length));
    }

    private static byte[] Deflate(ReadOnlySpan<byte> body)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(body);
        }

        return output.ToArray();
    }

    // Inflated by the driver's own inflater, at most `length` bytes, so no
    // stream makes the reader hold more, and to its exact end, so a byte
    // after the stream, which the BCL's inflater would leave unread, fails.
    private static byte[] Inflate(WireReader r, int length)
    {
        byte[] inflated = ZlibStream.Decompress(r.Rest().Span, length);
        return inflated.Length == length
            ? inflated
            : throw r.Malformed(Sections.CompressedFrames, "a compressed frame inflates short of its length");
    }
}
