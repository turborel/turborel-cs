// Inflate, DEFLATE's decoder, ported from turborel's protocol crate
// (protocol/src/zlib/inflate.rs; RFC 1951 §3.2): stored, fixed and
// dynamic Huffman blocks, read a bit at a time. Output is bounded: a
// stream that would inflate past its caller's limit is refused as it
// passes it. The stream's end is known exactly, so a caller can refuse
// any byte after it.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Turborel.Data.Protocol.Zlib;

/// <summary>Inflates DEFLATE streams, at most a limit's bytes.</summary>
internal static class Inflater
{
    /// <summary>A stored block's type (§3.2.3).</summary>
    private const int Stored = 0;

    /// <summary>A block of the fixed codes.</summary>
    private const int Fixed = 1;

    /// <summary>A block of dynamic codes.</summary>
    private const int Dynamic = 2;

    /// <summary>The bits of a block's type.</summary>
    private const int TypeBits = 2;

    /// <summary>The bits of HLIT and of HDIST.</summary>
    private const int CountBits = 5;

    /// <summary>The bits of HCLEN.</summary>
    private const int HclenBits = 4;

    /// <summary>The bits of each code length code length.</summary>
    private const int CodeLengthBits = 3;

    /// <summary>The longest code length, 15, the last a code length symbol gives as itself.</summary>
    private const int LastLiteralLength = 15;

    /// <summary>The bytes of a stored block's LEN and NLEN.</summary>
    private const int StoredHeader = 4;

    /// <summary>The output's first size, grown as it fills.</summary>
    private const int FirstOutput = 1024;

    /// <summary>The extra bits and least count of symbol 16, 17 and 18's repeats.</summary>
    private static readonly (int Extra, int Least)[] Repeats = [(2, 3), (3, 3), (7, 11)];

    /// <summary>Makes the failure of a stream that is no DEFLATE stream.</summary>
    /// <param name="why">What is wrong with it.</param>
    /// <returns>A connection failure, for the caller to throw.</returns>
    internal static TurborelException Malformed(string why) =>
        ConnectionFailure.Of($"{Sections.CompressedFrames} a compressed frame's stream is malformed: {why}");

    /// <summary>Inflates the DEFLATE stream <paramref name="bits"/> reads, leaving it after its last block.</summary>
    /// <param name="bits">The stream's bits.</param>
    /// <param name="limit">The most bytes it may inflate to.</param>
    /// <returns>The data.</returns>
    internal static byte[] Inflate(ref InflateBits bits, int limit)
    {
        var output = new Output(limit);
        bool last;
        do
        {
            last = bits.Bits(1) == 1;
            int type = bits.Bits(TypeBits);
            switch (type)
            {
                case Stored:
                    StoredBlock(ref bits, output);
                    break;
                case Fixed:
                    Codes(ref bits, output, Huffman.Of(DeflateTables.FixedLiteralLengths()), Huffman.Of(FixedDistances()));
                    break;
                case Dynamic:
                    (Huffman literals, Huffman distances) = DynamicCodes(ref bits);
                    Codes(ref bits, output, literals, distances);
                    break;
                default:
                    throw Malformed("a block of the reserved type 3");
            }
        }
        while (!last);
        return output.ToArray();
    }

    private static byte[] FixedDistances()
    {
        byte[] lengths = new byte[DeflateTables.Distances];
        Array.Fill(lengths, DeflateTables.FixedDistanceLength);
        return lengths;
    }

    // LEN, its complement NLEN, then LEN bytes (§3.2.4).
    private static void StoredBlock(ref InflateBits bits, Output output)
    {
        bits.Align();
        ReadOnlySpan<byte> header = bits.Bytes(StoredHeader);
        int len = BinaryPrimitives.ReadUInt16LittleEndian(header);
        int nlen = BinaryPrimitives.ReadUInt16LittleEndian(header[sizeof(ushort)..]);
        if (len != (~nlen & ushort.MaxValue))
        {
            throw Malformed("a stored block's length and its complement differ");
        }

        foreach (byte b in bits.Bytes(len))
        {
            output.Push(b);
        }
    }

    // A dynamic block's codes, from its header (§3.2.7).
    private static (Huffman Literals, Huffman Distances) DynamicCodes(ref InflateBits bits)
    {
        int nlen = bits.Bits(CountBits) + DeflateTables.MinLiteralSymbols;
        int ndist = bits.Bits(CountBits) + DeflateTables.MinDistanceSymbols;
        int ncode = bits.Bits(HclenBits) + DeflateTables.MinCodeLengthSymbols;
        if (nlen > DeflateTables.MaxLiteralSymbols || ndist > DeflateTables.MaxDistanceSymbols)
        {
            throw Malformed("a dynamic block counts too many codes");
        }

        byte[] codeLengths = new byte[DeflateTables.CodeLengths];
        for (int i = 0; i < ncode; i++)
        {
            codeLengths[DeflateTables.CodeLengthOrder[i]] = (byte)bits.Bits(CodeLengthBits);
        }

        byte[] lengths = CodeLengths(ref bits, Huffman.Complete(codeLengths), nlen + ndist);
        if (lengths[DeflateTables.EndOfBlock] == 0)
        {
            throw Malformed("a dynamic block's code has no end of block");
        }

        return (Huffman.Of(lengths.AsSpan(0, nlen)), Huffman.Of(lengths.AsSpan(nlen)));
    }

    // The n code lengths of a dynamic block, run-length coded.
    private static byte[] CodeLengths(ref InflateBits bits, Huffman code, int n)
    {
        var lengths = new List<byte>(n);
        while (lengths.Count < n)
        {
            int symbol = code.Decode(ref bits);
            if (symbol <= LastLiteralLength)
            {
                lengths.Add((byte)symbol);
                continue;
            }

            if (symbol == DeflateTables.RepeatPrevious && lengths.Count == 0)
            {
                throw Malformed("a repeat with no length before it");
            }

            byte repeated = symbol == DeflateTables.RepeatPrevious ? lengths[^1] : (byte)0;
            (int extra, int least) = Repeats[symbol - DeflateTables.RepeatPrevious];
            int times = bits.Bits(extra) + least;
            if (lengths.Count + times > n)
            {
                throw Malformed("a repeat runs past the code lengths counted");
            }

            for (int i = 0; i < times; i++)
            {
                lengths.Add(repeated);
            }
        }

        return [.. lengths];
    }

    // A Huffman block's symbols, to its end of block (§3.2.5).
    private static void Codes(ref InflateBits bits, Output output, Huffman literals, Huffman distances)
    {
        while (true)
        {
            int symbol = literals.Decode(ref bits);
            if (symbol < DeflateTables.EndOfBlock)
            {
                output.Push((byte)symbol);
                continue;
            }

            if (symbol == DeflateTables.EndOfBlock)
            {
                return;
            }

            int length = Extra(ref bits, DeflateTables.LengthBase, DeflateTables.LengthExtra, symbol - DeflateTables.FirstLength);
            int distance = Extra(ref bits, DeflateTables.DistanceBase, DeflateTables.DistanceExtra, distances.Decode(ref bits));
            output.Copy(distance, length);
        }
    }

    private static int Extra(ref InflateBits bits, int[] bases, int[] extras, int index)
    {
        return index < bases.Length
            ? bases[index] + bits.Bits(extras[index])
            : throw Malformed("a length or distance symbol out of range");
    }

    // The output so far, at most `limit` bytes, grown as it is written so
    // a stream claiming much holds no more than it gives.
    private sealed class Output(int limit)
    {
        private byte[] _bytes = [];
        private int _length;

        internal void Push(byte b)
        {
            if (_length >= limit)
            {
                throw Malformed("it inflates past its frame's length");
            }

            if (_length == _bytes.Length)
            {
                Array.Resize(ref _bytes, Math.Min(limit, Math.Max(FirstOutput, _bytes.Length * 2)));
            }

            _bytes[_length++] = b;
        }

        // `length` bytes copied from `distance` back, which may overlap
        // what they write.
        internal void Copy(int distance, int length)
        {
            if (distance > _length)
            {
                throw Malformed("a distance reaches before the stream's start");
            }

            int from = _length - distance;
            for (int i = 0; i < length; i++)
            {
                Push(_bytes[from + i]);
            }
        }

        internal byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();
    }
}
