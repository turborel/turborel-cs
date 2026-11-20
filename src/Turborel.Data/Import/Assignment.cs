// The values an import sends for its INSERT's parameters [CLIENT
// Importing]: a string for a character string column assigned to it as
// INSERT assigns it, so one too long is refused, where the cast of a
// parameter's value would cut it with a warning; a string for a character
// large object column held as a large object, as turborel-client's
// assignment makes it; any other value as it is, for the server to cast.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Import;

/// <summary>An import's assignment of its values to its parameters.</summary>
internal static class Assignment
{
    /// <summary>The rule by which INSERT assigns a value to its column.</summary>
    private const string InsertAssigns = "ISO/IEC 9075:1992 §13.8 GR4b iii";

    private const int DataSqlCode = -301;
    private const string RightTruncation = "22001";

    /// <summary>Gives <paramref name="values"/> as they are sent for parameters of <paramref name="parameters"/>.</summary>
    /// <param name="values">A value for each parameter.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The values sent.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22001 for a string, not spaces beyond its column's length, too long for it.</exception>
    internal static IReadOnlyList<object?> Assigned(IReadOnlyList<object?> values, IReadOnlyList<Parameter> parameters) =>
        [.. values.Zip(parameters, (value, parameter) => value is string s ? Assign(s, parameter.Type) : value)];

    private static object Assign(string s, DataType type) => type switch
    {
        CharacterType c => Fit(s, c.Length, c.Units, c.Varying ? null : c, c.Varying ? Written("CHARACTER VARYING", c.Length, c.Units) : Written("CHARACTER", c.Length, c.Units)),
        LargeObjectType { Kind: LargeObjectKind.Character } l => TurborelLargeObject.Whole(LargeObjectKind.Character, Encoding.UTF8.GetBytes(FitLob(s, l))),
        _ => s,
    };

    // A string fitted to a character string type of length units, padded
    // with spaces of a fixed one, cut of spaces beyond its length, or refused.
    private static string Fit(string s, ulong length, CharacterUnits units, CharacterType? fixedType, string typeText)
    {
        ulong count = Count(s, units);
        if (count <= length)
        {
            return fixedType is null ? s : s + new string(' ', (int)(length - count));
        }

        string kept = Prefix(s, units, length);
        if (s[kept.Length..].All(c => c == ' '))
        {
            return fixedType is null ? kept : kept + new string(' ', (int)(length - Count(kept, units)));
        }

        string unit = units == CharacterUnits.Octets ? "octets" : "characters";
        string clause = fixedType is null ? "§9.2 GR3e ii" : "§9.2 GR3b ii";
        throw new TurborelException(
            DataSqlCode,
            RightTruncation,
            string.Create(CultureInfo.InvariantCulture, $"[{InsertAssigns}, {clause}] a value of {count} {unit}, not spaces beyond {length}, does not fit {typeText}"));
    }

    private static string FitLob(string s, LargeObjectType l)
    {
        ulong count = Count(s, l.Units);
        if (count <= l.Length)
        {
            return s;
        }

        string kept = Prefix(s, l.Units, l.Length);
        if (s[kept.Length..].All(c => c == ' '))
        {
            return kept;
        }

        string typeText = l.Units == CharacterUnits.Octets
            ? string.Create(CultureInfo.InvariantCulture, $"CHARACTER LARGE OBJECT({l.Length} OCTETS)")
            : string.Create(CultureInfo.InvariantCulture, $"CHARACTER LARGE OBJECT({l.Length})");
        throw new TurborelException(
            DataSqlCode,
            RightTruncation,
            $"[{InsertAssigns}; ISO/IEC 9075-2:2023 §9.2 GR3e ii] a large object longer than {typeText}, not padding beyond it, does not fit it");
    }

    private static string Written(string name, uint length, CharacterUnits units) => units == CharacterUnits.Octets
        ? string.Create(CultureInfo.InvariantCulture, $"{name}({length} OCTETS)")
        : string.Create(CultureInfo.InvariantCulture, $"{name}({length})");

    // Its characters, Unicode's scalar values, or its UTF-8 octets.
    private static ulong Count(string s, CharacterUnits units) =>
        units == CharacterUnits.Octets ? (ulong)Encoding.UTF8.GetByteCount(s) : (ulong)s.EnumerateRunes().Count();

    // The longest start of s of at most most units, whole characters.
    private static string Prefix(string s, CharacterUnits units, ulong most)
    {
        ulong taken = 0;
        int end = 0;
        foreach (Rune rune in s.EnumerateRunes())
        {
            ulong width = units == CharacterUnits.Octets ? (ulong)rune.Utf8SequenceLength : 1;
            if (taken + width > most)
            {
                break;
            }

            taken += width;
            end += rune.Utf16SequenceLength;
        }

        return s[..end];
    }
}
