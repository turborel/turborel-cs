// A decimal floating-point value as the protocol carries it, coefficient
// i128 and exponent i32 [PROTOCOL Values], held in turborel's one form: at
// most 34 digits, no trailing zero, and an adjusted exponent in
// decimal128's range, as turborel's value/decfloat.rs keeps it.

using System;
using System.Globalization;
using System.Numerics;

namespace Turborel.Data.Types;

/// <summary>
/// A DECFLOAT value: <see cref="Coefficient"/> times ten to the
/// <see cref="Exponent"/>.
/// </summary>
public readonly record struct TurborelDecFloat
{
    /// <summary>The most digits a coefficient has (turborel's MAX_DECFLOAT_PRECISION).</summary>
    internal const int MaxDigits = 34;

    /// <summary>The greatest adjusted exponent, the exponent of the leading digit: decimal128's.</summary>
    internal const long MaxAdjustedExponent = 6144;

    /// <summary>The least adjusted exponent: decimal128's.</summary>
    internal const long MinAdjustedExponent = -6143;

    /// <summary>The adjusted exponents written plainly, without an E, from this one up.</summary>
    private const long PlainLeast = -6;

    /// <summary>The radix of the coefficient's digits.</summary>
    private const int Radix = 10;

    private TurborelDecFloat(Int128 coefficient, int exponent)
    {
        Coefficient = coefficient;
        Exponent = exponent;
    }

    /// <summary>Gets zero.</summary>
    internal static TurborelDecFloat Zero => default;

    /// <summary>Gets the signed digits, with no trailing zero; 0 of zero.</summary>
    public Int128 Coefficient { get; }

    /// <summary>Gets the power of ten the coefficient is multiplied by; 0 of zero.</summary>
    public int Exponent { get; }

    /// <summary>
    /// Gives the value of the coefficient and exponent if they are in
    /// turborel's one form, as a server sends them.
    /// </summary>
    /// <param name="coefficient">The signed digits.</param>
    /// <param name="exponent">The power of ten.</param>
    /// <param name="value">The value, if they are.</param>
    /// <returns>Whether they are: at most 34 digits, no trailing zero, in range.</returns>
    public static bool TryCreate(Int128 coefficient, int exponent, out TurborelDecFloat value)
    {
        value = default;
        if (coefficient == 0)
        {
            return exponent == 0;
        }

        int digits = Digits(coefficient);
        bool trailingZero = coefficient % Radix == 0;
        long adjusted = (long)exponent + digits - 1;
        if (digits > MaxDigits || trailingZero || adjusted < MinAdjustedExponent || adjusted > MaxAdjustedExponent)
        {
            return false;
        }

        value = new TurborelDecFloat(coefficient, exponent);
        return true;
    }

    /// <summary>
    /// Gives <paramref name="coefficient"/> times ten to <paramref name="exponent"/>,
    /// rounded half to even to 34 digits and put in its one form, as turborel
    /// reads a JSON document's DECFLOAT; none if out of range.
    /// </summary>
    /// <param name="coefficient">The signed digits, any number of them.</param>
    /// <param name="exponent">The power of ten.</param>
    /// <param name="value">The value, if in range.</param>
    /// <returns>Whether it is in range.</returns>
    internal static bool TryRound(BigInteger coefficient, long exponent, out TurborelDecFloat value)
    {
        value = default;
        bool negative = coefficient.Sign < 0;
        var magnitude = BigInteger.Abs(coefficient);
        int excess = magnitude.IsZero ? 0 : magnitude.ToString(CultureInfo.InvariantCulture).Length - MaxDigits;
        if (excess > 0)
        {
            var divisor = BigInteger.Pow(Radix, excess);
            var quotient = BigInteger.DivRem(magnitude, divisor, out BigInteger remainder);
            int half = (remainder * 2).CompareTo(divisor);
            magnitude = half > 0 || (half == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
            exponent += excess;
        }

        return TryCanonical(negative ? -magnitude : magnitude, exponent, out value);
    }

    /// <summary>
    /// Writes the value as turborel writes a DECFLOAT: plainly for an
    /// adjusted exponent from -6 to 33, else its leading digit, its other
    /// digits and the adjusted exponent.
    /// </summary>
    /// <returns>The value's text.</returns>
    public override string ToString()
    {
        int digits = Coefficient == 0 ? 1 : Digits(Coefficient);
        long adjusted = (long)Exponent + digits - 1;
        if (Coefficient == 0 || (adjusted >= PlainLeast && adjusted < MaxDigits))
        {
            return Plain();
        }

        string sign = Coefficient < 0 ? "-" : string.Empty;
        string all = Magnitude().ToString(CultureInfo.InvariantCulture);
        string point = all.Length > 1 ? "." : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{all[0]}{point}{all.AsSpan(1)}E{adjusted}");
    }

    /// <summary>Writes the value plainly, without an exponent, as turborel writes it in JSON text.</summary>
    /// <returns>Its digits, a point where its exponent puts one.</returns>
    internal string Plain()
    {
        string sign = Coefficient < 0 ? "-" : string.Empty;
        string digits = Magnitude().ToString(CultureInfo.InvariantCulture);
        if (Exponent >= 0)
        {
            return sign + digits + new string('0', Exponent);
        }

        int places = -Exponent;
        string padded = digits.PadLeft(places + 1, '0');
        int point = padded.Length - places;
        return string.Concat(sign, padded.AsSpan(0, point), ".", padded.AsSpan(point));
    }

    private static bool TryCanonical(BigInteger coefficient, long exponent, out TurborelDecFloat value)
    {
        value = default;
        if (coefficient.IsZero)
        {
            return true;
        }

        while (coefficient % Radix == 0)
        {
            coefficient /= Radix;
            exponent++;
        }

        if (exponent < int.MinValue || exponent > int.MaxValue)
        {
            return false;
        }

        return TryCreate((Int128)coefficient, (int)exponent, out value);
    }

    private static int Digits(Int128 coefficient) =>
        BigInteger.Abs((BigInteger)coefficient).ToString(CultureInfo.InvariantCulture).Length;

    private BigInteger Magnitude() => BigInteger.Abs((BigInteger)Coefficient);
}
