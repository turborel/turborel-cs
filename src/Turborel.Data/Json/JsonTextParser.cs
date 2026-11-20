// JSON text read as an SQL/JSON item, as turborel parses it (its
// json/text.rs): RFC 8259 text, without unique keys; a number with an
// exponent is approximate, any other exact, past 18 digits the DECFLOAT
// that holds it exactly, and past 34 significant digits out of range.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Turborel.Data.Types;

namespace Turborel.Data.Json;

/// <summary>Reads JSON text as an SQL/JSON item.</summary>
internal static partial class JsonTextParser
{
    /// <summary>The most digits, and the greatest scale, of an exact number (turborel's MAX_EXACT_PRECISION).</summary>
    private const int MaxExactDigits = 18;

    /// <summary>The containers the reader takes nested: the stored form's bound, its root's level among them.</summary>
    private const int MaxDepth = JsonBinary.MaxNesting + 1;

    /// <summary>Reads <paramref name="text"/> as one JSON value.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The item it is.</returns>
    /// <exception cref="TurborelException">
    /// SQLSTATE 22032 if it is no JSON text; 22003 if a number is out of the
    /// range of turborel's numbers.
    /// </exception>
    internal static JsonItem Parse(string text)
    {
        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(text),
            new JsonReaderOptions { MaxDepth = MaxDepth, CommentHandling = JsonCommentHandling.Disallow });
        try
        {
            JsonItem item = reader.Read() ? Value(ref reader) : throw JsonErrors.NotJsonText("the text holds no value", 0);
            return reader.Read() ? throw JsonErrors.NotJsonText("text follows the value", reader.TokenStartIndex) : item;
        }
        catch (JsonException e)
        {
            throw JsonErrors.NotJsonText(e.Message, e.BytePositionInLine ?? 0, e);
        }
        catch (InvalidOperationException e)
        {
            // A string escaping half a surrogate pair, which is no text.
            throw JsonErrors.NotJsonText("a string is not Unicode text", reader.TokenStartIndex, e);
        }
    }

    /// <summary>
    /// Reads the number <paramref name="token"/> is, as turborel does:
    /// approximate with an exponent, else exact or, too long, DECFLOAT.
    /// </summary>
    /// <param name="token">The number's text, as RFC 8259 writes one.</param>
    /// <returns>The item.</returns>
    internal static JsonItem Number(string token)
    {
        Match m = NumberToken().Match(token);
        if (m.Groups["exponent"].Success)
        {
            return Approximate(token, m.Groups["whole"].Value + m.Groups["fraction"].Value);
        }

        return Exact(m.Groups["sign"].Success, m.Groups["whole"].Value, m.Groups["fraction"].Value);
    }

    private static JsonItem Value(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => new JsonItem.NullItem(),
            JsonTokenType.True => new JsonItem.BooleanItem(true),
            JsonTokenType.False => new JsonItem.BooleanItem(false),
            JsonTokenType.String => new JsonItem.StringItem(reader.GetString() ?? string.Empty),
            JsonTokenType.Number => Number(Encoding.UTF8.GetString(reader.ValueSpan)),
            JsonTokenType.StartArray => Elements(ref reader),
            _ => Members(ref reader),
        };
    }

    private static JsonItem.ArrayItem Elements(ref Utf8JsonReader reader)
    {
        var elements = new List<JsonItem>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            elements.Add(Value(ref reader));
        }

        return new JsonItem.ArrayItem(elements);
    }

    private static JsonItem.ObjectItem Members(ref Utf8JsonReader reader)
    {
        var members = new List<JsonMember>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string key = reader.GetString() ?? string.Empty;
            _ = reader.Read();
            members.Add(new JsonMember(key, Value(ref reader)));
        }

        return new JsonItem.ObjectItem(members);
    }

    private static JsonItem.ApproximateItem Approximate(string token, string mantissa)
    {
        double x = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!double.IsFinite(x))
        {
            throw JsonErrors.NumberOutOfRange("the number is too great for an approximate number");
        }

        bool lost = x == 0 && mantissa.AsSpan().IndexOfAnyInRange('1', '9') >= 0;
        return lost
            ? throw JsonErrors.NumberOutOfRange("the number is too small for an approximate number, but not zero")
            : new JsonItem.ApproximateItem(x);
    }

    // Leading zeros, and a fraction's trailing ones, are insignificant: of
    // more digits than an exact number takes it is exact without them if
    // they made it too long, else the DECFLOAT it is.
    private static JsonItem Exact(bool negative, string whole, string fraction)
    {
        string digits = whole + fraction;
        string significant = fraction.TrimEnd('0');
        string trimmed = (whole + significant).TrimStart('0');
        (string units, int scale) = Fits(digits, fraction) ? (digits, fraction.Length) : (trimmed, significant.Length);
        if (Fits(units, string.Empty) && scale <= MaxExactDigits)
        {
            Int128 value = units.Length == 0 ? 0 : Int128.Parse(units, CultureInfo.InvariantCulture);
            return new JsonItem.ExactItem(new TurborelDecimal(negative ? -value : value, (uint)scale));
        }

        if (trimmed.TrimEnd('0').Length > TurborelDecFloat.MaxDigits)
        {
            throw JsonErrors.NumberOutOfRange("the number has more than 34 significant digits");
        }

        var coefficient = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        return TurborelDecFloat.TryRound(negative ? -coefficient : coefficient, -fraction.Length, out TurborelDecFloat d)
            ? new JsonItem.DecFloatItem(d)
            : throw JsonErrors.NumberOutOfRange("the number is out of the range of a decimal floating-point number");
    }

    private static bool Fits(string digits, string fraction) =>
        digits.Length <= MaxExactDigits && fraction.Length <= MaxExactDigits;

    [GeneratedRegex(@"^(?<sign>-)?(?<whole>\d+)(?:\.(?<fraction>\d+))?(?<exponent>[eE][+-]?\d+)?$")]
    private static partial Regex NumberToken();
}
