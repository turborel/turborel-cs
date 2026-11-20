// A zlib stream read whole, as turborel's protocol crate reads one
// (protocol/src/zlib.rs; RFC 1950): a two-byte header of method 8 and no
// preset dictionary, a DEFLATE stream, then the Adler-32 of the data, most
// significant byte first, and nothing after it.

using System;
using System.Buffers.Binary;

namespace Turborel.Data.Protocol.Zlib;

/// <summary>Reads zlib streams.</summary>
internal static class ZlibStream
{
    /// <summary>The compression method deflate, CMF's low four bits.</summary>
    private const int Deflate = 8;

    /// <summary>The bits of CMF's method, below CINFO.</summary>
    private const int MethodMask = 0x0F;

    /// <summary>The shift of CMF's CINFO, the window's base-2 logarithm less 8.</summary>
    private const int CinfoShift = 4;

    /// <summary>The largest CINFO, a 32K window.</summary>
    private const int MaxCinfo = 7;

    /// <summary>FLG's preset dictionary bit.</summary>
    private const int Fdict = 0x20;

    /// <summary>The divisor FCHECK makes the header a multiple of.</summary>
    private const int FcheckDivisor = 31;

    /// <summary>The bytes of the header.</summary>
    private const int HeaderBytes = 2;

    /// <summary>The bytes of the Adler-32 trailer.</summary>
    private const int TrailerBytes = 4;

    /// <summary>Reads the data of <paramref name="stream"/>, which must be all of it, at most <paramref name="limit"/> bytes.</summary>
    /// <param name="stream">The zlib stream.</param>
    /// <param name="limit">The most bytes it may inflate to.</param>
    /// <returns>The data.</returns>
    /// <exception cref="TurborelException">
    /// A connection failure if it is no zlib stream, needs a dictionary,
    /// inflates past the limit, or its Adler-32 is wrong, missing, or
    /// followed by bytes.
    /// </exception>
    internal static byte[] Decompress(ReadOnlySpan<byte> stream, int limit)
    {
        if (stream.Length < HeaderBytes)
        {
            throw Inflater.Malformed("it is shorter than its header");
        }

        int cmf = stream[0];
        int flg = stream[1];
        int check = BinaryPrimitives.ReadUInt16BigEndian(stream) % FcheckDivisor;
        if ((cmf & MethodMask) != Deflate || cmf >> CinfoShift > MaxCinfo || check != 0)
        {
            throw Inflater.Malformed("its header is not deflate's");
        }

        if ((flg & Fdict) != 0)
        {
            throw Inflater.Malformed("it needs a preset dictionary");
        }

        var bits = new InflateBits(stream[HeaderBytes..]);
        byte[] data = Inflater.Inflate(ref bits, limit);
        ReadOnlySpan<byte> trailer = stream[(HeaderBytes + bits.At)..];
        if (trailer.Length != TrailerBytes || BinaryPrimitives.ReadUInt32BigEndian(trailer) != Adler32.Of(data))
        {
            throw Inflater.Malformed("its Adler-32 is wrong, missing or followed by bytes");
        }

        return data;
    }
}
