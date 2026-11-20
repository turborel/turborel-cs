// A canonical Huffman code read from its code lengths (RFC 1951 §3.2.2),
// decoded a bit at a time: the codes of each length are consecutive, so a
// code of length n is the (code - first)th symbol of that length.

using System;

namespace Turborel.Data.Protocol.Zlib;

/// <summary>A canonical code: how many codes have each length, and the symbols in code order.</summary>
internal sealed class Huffman
{
    private readonly int[] _count;
    private readonly int[] _symbol;

    private Huffman(int[] count, int[] symbol)
    {
        _count = count;
        _symbol = symbol;
    }

    /// <summary>
    /// Makes the code of <paramref name="lengths"/>. One using more codes
    /// than its lengths allow is refused; one using fewer only if it has a
    /// single code, or none, as §3.2.7 allows a distance code.
    /// </summary>
    /// <param name="lengths">Each symbol's code length, 0 for one left out.</param>
    /// <returns>The code.</returns>
    internal static Huffman Of(ReadOnlySpan<byte> lengths)
    {
        (Huffman code, long left) = Build(lengths);
        int used = 0;
        for (int length = 1; length <= DeflateTables.MaxBits; length++)
        {
            used += code._count[length];
        }

        return left > 0 && used > 1 ? throw Inflater.Malformed("an incomplete Huffman code") : code;
    }

    /// <summary>Makes the code of <paramref name="lengths"/>, complete unless empty: the code length code's rule.</summary>
    /// <param name="lengths">Each symbol's code length.</param>
    /// <returns>The code.</returns>
    internal static Huffman Complete(ReadOnlySpan<byte> lengths)
    {
        (Huffman code, long left) = Build(lengths);
        return left > 0 && code._symbol.Length > 0 ? throw Inflater.Malformed("an incomplete code length code") : code;
    }

    /// <summary>Reads the next symbol.</summary>
    /// <param name="bits">The stream's bits.</param>
    /// <returns>The symbol.</returns>
    internal int Decode(ref InflateBits bits)
    {
        int code = 0;
        int first = 0;
        int index = 0;
        for (int length = 1; length <= DeflateTables.MaxBits; length++)
        {
            code |= bits.Bits(1);
            int count = _count[length];
            if (code - first < count)
            {
                return _symbol[index + code - first];
            }

            index += count;
            first = (first + count) << 1;
            code <<= 1;
        }

        throw Inflater.Malformed("a code its block's Huffman code does not have");
    }

    private static (Huffman Code, long Left) Build(ReadOnlySpan<byte> lengths)
    {
        int[] count = new int[DeflateTables.MaxBits + 1];
        foreach (byte length in lengths)
        {
            if (length > DeflateTables.MaxBits)
            {
                throw Inflater.Malformed("a code length past 15");
            }

            count[length]++;
        }

        long left = 1;
        for (int length = 1; length <= DeflateTables.MaxBits; length++)
        {
            left = (left * 2) - count[length];
            if (left < 0)
            {
                throw Inflater.Malformed("an over-subscribed Huffman code");
            }
        }

        return (new Huffman(count, Symbols(lengths, count)), left);
    }

    private static int[] Symbols(ReadOnlySpan<byte> lengths, int[] count)
    {
        int[] offsets = new int[DeflateTables.MaxBits + 2];
        for (int length = 1; length <= DeflateTables.MaxBits; length++)
        {
            offsets[length + 1] = offsets[length] + count[length];
        }

        int[] symbol = new int[offsets[DeflateTables.MaxBits + 1]];
        for (int s = 0; s < lengths.Length; s++)
        {
            if (lengths[s] != 0)
            {
                symbol[offsets[lengths[s]]++] = s;
            }
        }

        return symbol;
    }
}
