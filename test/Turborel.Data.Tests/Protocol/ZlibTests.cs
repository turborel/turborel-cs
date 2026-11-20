// The driver's zlib reader, ported from turborel's: RFC 1950's Adler-32
// vectors, streams zlib itself wrote, the inflate bound, malformed
// streams, and the stream's exact end, so a byte after it fails however
// it is made [PROTOCOL Compressed frames].

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Protocol.Zlib;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="ZlibStream"/>, <see cref="Inflater"/>, <see cref="Huffman"/> and <see cref="Adler32"/>.</summary>
public sealed class ZlibTests
{
    private const int Megabyte = 1 << 20;

    /// <summary>Adler-32 is 1 of no bytes, and zlib's of "Wikipedia" and of inputs past NMAX.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1950 §8.2]")]
    public void Adler32MatchesRfc1950()
    {
        byte[] repeated = [.. Enumerable.Range(0, 20).SelectMany(_ => Enumerable.Range(0, 256).Select(b => (byte)b))];

        Assert.Equal(1u, Adler32.Of([]));
        Assert.Equal(0x11E6_0398u, Adler32.Of(Wire.Ascii("Wikipedia")));
        Assert.Equal(0x149A_302Cu, Adler32.Of([.. Enumerable.Repeat((byte)0xFF, 100_000)]));
        Assert.Equal(0x23C4_F688u, Adler32.Of(repeated));
    }

    /// <summary>Streams zlib 1.2.13 wrote, of a stored block, fixed blocks and a dynamic block, inflate to their data.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1951 §3.2]")]
    public void InflatesStreamsZlibWrote()
    {
        byte[] dynamic = Wire.Hex(
            "78da1d8f510a04310c42afd2ab092324502a4cecfdd7d9f6cf18f3c4e87977cbdd609e80da30e9439167cae2921efbfa4e347c26dd81d455a6bc1ccdda"
            + "c667dea06d81739277e9184fa2142953aafa54c594df245ab875a585f9a41c46b589e1e0c1f924aed5ee69d7127b88ed07f848bab6fed79b5e1af835"
            + "976767256197152ee2453cbaf13e507a9eb49c10255c4a817314e40a4db6b3997e0c7cf986dbe8d065be7ff66d7a07");

        Assert.Empty(ZlibStream.Decompress(Wire.Hex("789c030000000001"), Megabyte));
        Assert.Equal(Wire.Ascii("a"), ZlibStream.Decompress(Wire.Hex("789c4b040000620062"), Megabyte));
        Assert.Equal(Wire.Ascii("hello hello hello hello"), ZlibStream.Decompress(Wire.Hex("789ccb48cdc9c957c8402701680308b1"), Megabyte));
        Assert.Equal(Wire.Ascii("turborel stored"), ZlibStream.Decompress(Wire.Hex("7801010f00f0ff747572626f72656c2073746f72656431df0621"), Megabyte));
        Assert.Equal(2, (dynamic[2] >> 1) & 3);
        Assert.Equal(Skewed(), ZlibStream.Decompress(dynamic, Megabyte));
    }

    /// <summary>
    /// What the BCL's ZLibStream writes, at each level, the driver's reader
    /// reads back; of no bytes the BCL writes no stream, and zlib's own
    /// empty stream is read above.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1951 §3.2]")]
    public void ReadsWhatTheBclWrites()
    {
        byte[] text = [.. Enumerable.Repeat(Wire.Ascii("SELECT NAME, SALARY FROM STAFF WHERE SALARY > 50000; "), 40).SelectMany(b => b)];
        byte[] runs = [.. Enumerable.Range(0, 200_000).Select(i => (byte)(i / 1000 % 7))];
        byte[][] cases = [Wire.Ascii("x"), [.. Enumerable.Range(0, 256).Select(b => (byte)b)], text, runs, Noise(100_000), Skewed()];

        foreach (byte[] data in cases)
        {
            foreach (CompressionLevel level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize, CompressionLevel.NoCompression })
            {
                Assert.Equal(data, ZlibStream.Decompress(Deflated(data, level), data.Length));
            }
        }
    }

    /// <summary>Streams zlib wrote of a match 32,100 bytes back, near the window's end, inflate to their data.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1951 §3.2]")]
    public void InflatesFarMatches()
    {
        byte[] a = Noise(100);
        byte[] data = [.. a, .. new byte[32_000], .. a];

        Assert.Equal(data, ZlibStream.Decompress(Deflated(data, CompressionLevel.SmallestSize), data.Length));
        Assert.Equal(data, ZlibStream.Decompress(Deflated(data, CompressionLevel.Fastest), data.Length));
    }

    /// <summary>Inflation stops at its limit: a megabyte of zeros is refused under a smaller limit, and read under its own.</summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void InflateIsBounded()
    {
        byte[] zeros = new byte[Megabyte];
        byte[] bomb = Deflated(zeros, CompressionLevel.Optimal);

        Assert.True(bomb.Length < 2048);
        Wire.Fails(() => ZlibStream.Decompress(bomb, 4096), Sections.CompressedFrames);
        Assert.Equal(zeros.Length, ZlibStream.Decompress(bomb, zeros.Length).Length);
        Wire.Fails(() => ZlibStream.Decompress(bomb, zeros.Length - 1), Sections.CompressedFrames);
        Wire.Fails(() => ZlibStream.Decompress(Wire.Hex("7801010f00f0ff747572626f72656c2073746f72656431df0621"), 14), Sections.CompressedFrames);
    }

    /// <summary>
    /// A stream is refused for another method, a window past 32K, a failed
    /// header check, a preset dictionary, a wrong or missing Adler-32, bytes
    /// after it, a cut body, the reserved block type, a stored length unlike
    /// its complement, and a distance before the data's start.
    /// </summary>
    /// <param name="hex">The stream.</param>
    [Theory]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1950 §2.2]")]
    [InlineData("79")]
    [InlineData("799c4b040000620062")]
    [InlineData("889c4b040000620062")]
    [InlineData("789d4b040000620062")]
    [InlineData("78bb4b040000620062")]
    [InlineData("789c4b040000620063")]
    [InlineData("789c4b04000062006200")]
    [InlineData("789c4b0400")]
    [InlineData("789c07000000000001")]
    [InlineData("7801010f00f1ff747572626f72656c2073746f72656431df0621")]
    [InlineData("789c4b04420000000000")]
    public void MalformedStreamsAreRefused(string hex)
    {
        Wire.Fails(() => ZlibStream.Decompress(Wire.Hex(hex), Megabyte), Sections.CompressedFrames);
    }

    /// <summary>
    /// Regression: bytes after a stream that repeat its Adler-32 are refused,
    /// where a check of the last four bytes alone took them, since the
    /// reader knows exactly where the stream ends.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void TrailingBlockRepeatingTheChecksumIsRefused()
    {
        byte[] data = [.. Enumerable.Repeat((byte)'b', 5000)];
        byte[] stream = Deflated(data, CompressionLevel.Optimal);
        byte[] crafted = [.. stream, .. stream[^4..]];
        byte[] frame = [(byte)'B', .. BitConverter.GetBytes(5000u), .. crafted];

        Assert.Equal(stream[^4..], crafted[^4..]);
        Wire.Fails(() => ZlibStream.Decompress(crafted, data.Length), Sections.CompressedFrames);
        Wire.Fails(() => Compression.Decompress(frame, FrameHeader.MaxBody), Sections.CompressedFrames);
    }

    /// <summary>
    /// A Huffman code with more codes than its lengths allow is refused, a
    /// complete one and one of a single code of one bit are taken, and the
    /// code length code must be complete.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Compressed frames] [RFC 1951 §3.2.2]")]
    public void HuffmanCodesAreChecked()
    {
        Wire.Fails(() => Huffman.Of([1, 1, 1]));
        _ = Huffman.Of([1, 2, 2]);
        _ = Huffman.Of([0, 1, 0]);
        _ = Huffman.Of([0, 0, 0]);
        Wire.Fails(() => Huffman.Of([2, 2, 2]));
        Wire.Fails(() => Huffman.Of([16]));
        Wire.Fails(() => Huffman.Complete([0, 1, 0]));
        _ = Huffman.Complete([1, 1]);
    }

    // 300 letters of a fixed LCG, skewed to e, t and a, as turborel's tests
    // make them: text a dynamic Huffman block suits.
    private static byte[] Skewed()
    {
        byte[] letters = Wire.Ascii("eeeeeeeetttttaaaaooooiinnsshhrdlu ");
        ulong x = 12345;
        byte[] text = new byte[300];
        for (int i = 0; i < text.Length; i++)
        {
            x = unchecked((x * 1_103_515_245) + 12345) & 0x7fff_ffff;
            text[i] = letters[(int)((x >> 16) % 34)];
        }

        return text;
    }

    // Bytes of a fixed xorshift, which do not compress.
    private static byte[] Noise(int n)
    {
        uint x = 0x9E37_79B9;
        byte[] bytes = new byte[n];
        for (int i = 0; i < n; i++)
        {
            x ^= unchecked(x << 13);
            x ^= x >> 17;
            x ^= unchecked(x << 5);
            bytes[i] = (byte)x;
        }

        return bytes;
    }

    private static byte[] Deflated(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, level, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
