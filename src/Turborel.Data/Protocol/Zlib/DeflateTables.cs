// The constants of DEFLATE's format the inflater reads by (RFC 1951
// §3.2.5, §3.2.6, §3.2.7), as turborel's protocol/src/zlib/tables.rs
// gives them.

namespace Turborel.Data.Protocol.Zlib;

/// <summary>DEFLATE's alphabets, code lengths and length and distance tables.</summary>
internal static class DeflateTables
{
    /// <summary>The end-of-block symbol of the literal/length alphabet.</summary>
    internal const int EndOfBlock = 256;

    /// <summary>The first length symbol.</summary>
    internal const int FirstLength = 257;

    /// <summary>Symbols of the literal/length alphabet, 286 and 287 in the fixed code alone.</summary>
    internal const int Literals = 288;

    /// <summary>Symbols of the distance alphabet, 30 and 31 in the fixed code alone.</summary>
    internal const int Distances = 32;

    /// <summary>Symbols of the code length alphabet.</summary>
    internal const int CodeLengths = 19;

    /// <summary>The longest code of the literal/length and distance codes.</summary>
    internal const int MaxBits = 15;

    /// <summary>Literal/length symbols a dynamic header may count.</summary>
    internal const int MaxLiteralSymbols = 286;

    /// <summary>Distance symbols a dynamic header may count.</summary>
    internal const int MaxDistanceSymbols = 30;

    /// <summary>The fewest literal/length symbols a dynamic header counts, HLIT plus this.</summary>
    internal const int MinLiteralSymbols = 257;

    /// <summary>The fewest distance symbols, HDIST plus this.</summary>
    internal const int MinDistanceSymbols = 1;

    /// <summary>The fewest code length symbols, HCLEN plus this.</summary>
    internal const int MinCodeLengthSymbols = 4;

    /// <summary>The code length symbol repeating the previous length 3 to 6 times.</summary>
    internal const int RepeatPrevious = 16;

    /// <summary>The code length symbol repeating a zero 3 to 10 times.</summary>
    internal const int RepeatZero = 17;

    /// <summary>The fixed distance code's length: five bits each.</summary>
    internal const byte FixedDistanceLength = 5;

    /// <summary>The order the code length code's lengths are sent in (§3.2.7).</summary>
    internal static readonly int[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    /// <summary>Each length symbol's base length, from 257 (§3.2.5).</summary>
    internal static readonly int[] LengthBase =
        [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];

    /// <summary>Each length symbol's extra bits.</summary>
    internal static readonly int[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];

    /// <summary>Each distance symbol's base distance.</summary>
    internal static readonly int[] DistanceBase =
    [
        1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097,
        6145, 8193, 12289, 16385, 24577,
    ];

    /// <summary>Each distance symbol's extra bits.</summary>
    internal static readonly int[] DistanceExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    /// <summary>The first symbol of 9-bit fixed codes, 144 to 255 (§3.2.6).</summary>
    private const int FixedNineFrom = 144;

    /// <summary>The first symbol of 8-bit fixed codes again, 280 to 287.</summary>
    private const int FixedEightAgainFrom = 280;

    /// <summary>The length of fixed codes 0 to 143 and 280 to 287.</summary>
    private const byte FixedEight = 8;

    /// <summary>The length of fixed codes 144 to 255.</summary>
    private const byte FixedNine = 9;

    /// <summary>The length of fixed codes 256 to 279.</summary>
    private const byte FixedSeven = 7;

    /// <summary>The fixed literal/length code's lengths (§3.2.6).</summary>
    /// <returns>8 bits for 0-143, 9 for 144-255, 7 for 256-279, 8 for 280-287.</returns>
    internal static byte[] FixedLiteralLengths()
    {
        byte[] lengths = new byte[Literals];
        for (int symbol = 0; symbol < Literals; symbol++)
        {
            lengths[symbol] = symbol switch
            {
                < FixedNineFrom => FixedEight,
                < EndOfBlock => FixedNine,
                < FixedEightAgainFrom => FixedSeven,
                _ => FixedEight,
            };
        }

        return lengths;
    }
}
