// An exact number as the protocol carries it, units i128 and scale u32, so
// every value turborel sends reaches the program with no loss: .NET's
// decimal holds 96 bits, too few for an i128 [PROTOCOL Values].

using System;
using System.Globalization;
using System.Numerics;

namespace Turborel.Data.Types;

/// <summary>
/// An exact number: <see cref="Units"/> divided by ten to the
/// <see cref="Scale"/>.
/// </summary>
/// <param name="Units">The number's units, any <c>i128</c>.</param>
/// <param name="Scale">The power of ten the units are divided by.</param>
public readonly record struct TurborelDecimal(Int128 Units, uint Scale)
{
    /// <summary>
    /// The greatest scale written out in digits; past it the power of ten
    /// is written, as turborel writes it (its MAX_EXACT_PRECISION).
    /// </summary>
    internal const uint MaxPlainScale = 18;

    /// <summary>The scale <see cref="decimal"/> holds at most.</summary>
    private const int MaxDecimalScale = 28;

    /// <summary>The 32-bit words of <see cref="decimal"/>'s units.</summary>
    private const int DecimalUnitWords = 3;

    /// <summary>The bits of <see cref="decimal"/>'s units.</summary>
    private const int DecimalUnitBits = 96;

    /// <summary>Where <see cref="decimal.GetBits(decimal)"/> keeps the scale, in its flags word.</summary>
    private const int ScaleShift = 16;

    /// <summary>The scale's bits, once shifted down.</summary>
    private const int ScaleMask = 0xFF;

    /// <summary>Gives the number exactly, as <see cref="decimal"/>, if it holds it.</summary>
    /// <param name="value">The number, if it fits.</param>
    /// <returns>Whether <see cref="decimal"/> holds it: 96 bits of units, a scale of at most 28.</returns>
    public bool TryToDecimal(out decimal value)
    {
        value = default;
        var magnitude = BigInteger.Abs((BigInteger)Units);
        if (Scale > MaxDecimalScale || magnitude.GetBitLength() > DecimalUnitBits)
        {
            return false;
        }

        byte[] bits = new byte[DecimalUnitWords * sizeof(int)];
        _ = magnitude.TryWriteBytes(bits, out _, isUnsigned: true, isBigEndian: false);
        int lo = BitConverter.ToInt32(bits, 0);
        int mid = BitConverter.ToInt32(bits, sizeof(int));
        int hi = BitConverter.ToInt32(bits, 2 * sizeof(int));
        value = new decimal(lo, mid, hi, Units < 0, (byte)Scale);
        return true;
    }

    /// <summary>Gives the exact number <paramref name="value"/> is.</summary>
    /// <param name="value">The number.</param>
    /// <returns>Its units and scale, as <see cref="decimal"/> keeps them.</returns>
    public static TurborelDecimal FromDecimal(decimal value)
    {
        Span<int> parts = stackalloc int[DecimalUnitWords + 1];
        _ = decimal.GetBits(value, parts);
        var magnitude = new BigInteger(
            [.. BitConverter.GetBytes(parts[0]), .. BitConverter.GetBytes(parts[1]), .. BitConverter.GetBytes(parts[2])],
            isUnsigned: true);
        uint scale = (uint)((parts[DecimalUnitWords] >> ScaleShift) & ScaleMask);
        var units = (Int128)magnitude;
        return new TurborelDecimal(value < 0 ? -units : units, scale);
    }

    /// <summary>
    /// Writes the number as turborel does: its digits, a point before the
    /// last <see cref="Scale"/> of them; past a scale of 18, its units and
    /// the power of ten.
    /// </summary>
    /// <returns>The number's text.</returns>
    public override string ToString()
    {
        string sign = Units < 0 ? "-" : string.Empty;
        string digits = BigInteger.Abs((BigInteger)Units).ToString(CultureInfo.InvariantCulture);
        if (Scale == 0)
        {
            return sign + digits;
        }

        if (Scale > MaxPlainScale)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{digits}E-{Scale}");
        }

        string padded = digits.PadLeft((int)Scale + 1, '0');
        int point = padded.Length - (int)Scale;
        return string.Concat(sign, padded.AsSpan(0, point), ".", padded.AsSpan(point));
    }
}
