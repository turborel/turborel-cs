// An SQL/JSON item written as JSON text, as turborel serializes one (its
// json/serialize.rs): no insignificant white space; members in the order
// kept; a string's quotation marks, reverse solidi and control characters
// escaped, nothing else; exact numbers as CAST writes them, approximate
// ones as their shortest literal, DECFLOAT plainly; and no text of a
// datetime or of an approximate number that is not finite (SQLSTATE 22032).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Turborel.Data.Json;

/// <summary>Writes SQL/JSON items as JSON text.</summary>
internal static class JsonText
{
    /// <summary>The last of the control characters, U+0000 to U+001F, which are escaped.</summary>
    private const char LastControl = '\u001F';

    /// <summary>Writes <paramref name="item"/> as JSON text.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its text.</returns>
    /// <exception cref="TurborelException">
    /// SQLSTATE 22032 if it is or holds a datetime, or an approximate number
    /// that is not finite; SQLCODE -900 if it nests too deep.
    /// </exception>
    internal static string Serialize(JsonItem item)
    {
        var text = new StringBuilder();
        Write(text, item, 0);
        return text.ToString();
    }

    /// <summary>
    /// Writes <paramref name="x"/> as turborel's shortest approximate
    /// literal: the shortest digits that read back as it, one before the
    /// point and at least one after, then <c>E</c> and the exponent.
    /// </summary>
    /// <param name="x">A finite number.</param>
    /// <returns>Its literal, as <c>1.5E0</c>; zero as <c>0E0</c>.</returns>
    internal static string ApproximateLiteral(double x)
    {
        if (x == 0)
        {
            return "0E0";
        }

        string shortest = Math.Abs(x).ToString("R", CultureInfo.InvariantCulture);
        string[] parts = shortest.Split('E');
        int exponent = parts.Length > 1 ? int.Parse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0;
        int point = parts[0].IndexOf('.', StringComparison.Ordinal);
        string digits = parts[0].Replace(".", string.Empty, StringComparison.Ordinal);
        int whole = point < 0 ? digits.Length : point;
        string significant = digits.TrimStart('0');
        exponent += whole - 1 - (digits.Length - significant.Length);
        significant = significant.TrimEnd('0');
        string rest = significant.Length > 1 ? significant[1..] : "0";
        string sign = x < 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{significant[0]}.{rest}E{exponent}");
    }

    private static void Write(StringBuilder text, JsonItem item, int depth)
    {
        if (depth > JsonBinary.MaxNesting)
        {
            throw JsonErrors.Beyond("arrays and objects nest too deep");
        }

        _ = item switch
        {
            JsonItem.NullItem => text.Append("null"),
            JsonItem.BooleanItem b => text.Append(b.Value ? "true" : "false"),
            JsonItem.ExactItem e => text.Append(e.Value.ToString()),
            JsonItem.DecFloatItem f => text.Append(f.Value.Plain()),
            JsonItem.ApproximateItem a => text.Append(Approximate(a.Value)),
            JsonItem.StringItem s => WriteString(text, s.Value),
            JsonItem.ArrayItem array => WriteElements(text, array.Elements, depth + 1),
            JsonItem.ObjectItem o => WriteMembers(text, o.Members, depth + 1),
            _ => throw JsonErrors.NoText("a datetime"),
        };
    }

    private static string Approximate(double x) =>
        double.IsFinite(x) ? ApproximateLiteral(x) : throw JsonErrors.NoText("an approximate number that is not finite");

    private static StringBuilder WriteElements(StringBuilder text, IReadOnlyList<JsonItem> elements, int depth)
    {
        _ = text.Append('[');
        for (int i = 0; i < elements.Count; i++)
        {
            _ = i > 0 ? text.Append(',') : text;
            Write(text, elements[i], depth);
        }

        return text.Append(']');
    }

    private static StringBuilder WriteMembers(StringBuilder text, IReadOnlyList<JsonMember> members, int depth)
    {
        _ = text.Append('{');
        for (int i = 0; i < members.Count; i++)
        {
            _ = i > 0 ? text.Append(',') : text;
            _ = WriteString(text, members[i].Key).Append(':');
            Write(text, members[i].Value, depth);
        }

        return text.Append('}');
    }

    private static StringBuilder WriteString(StringBuilder text, string value)
    {
        _ = text.Append('"');
        foreach (char c in value)
        {
            _ = c switch
            {
                '"' => text.Append("\\\""),
                '\\' => text.Append("\\\\"),
                '\b' => text.Append("\\b"),
                '\f' => text.Append("\\f"),
                '\n' => text.Append("\\n"),
                '\r' => text.Append("\\r"),
                '\t' => text.Append("\\t"),
                <= LastControl => text.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"),
                _ => text.Append(c),
            };
        }

        return text.Append('"');
    }
}
