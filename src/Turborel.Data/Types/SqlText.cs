// A value's text, as turborel shows it (its value.rs Display), and its
// literal, as SQL writes it: what an export writes for each value
// [CLIENT Copying and exporting]. A datetime is shown in its displacement,
// an interval as its literal's string, an approximate number as
// <mantissa>E<exponent>, a truth value as TRUE or FALSE, a binary string
// as its hexadecimal digits, and a composite value as its literal.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Turborel.Data.Types;

/// <summary>The text and literal of a value, as turborel writes them.</summary>
internal static class SqlText
{
    private const long MicrosPerSecond = 1_000_000;
    private const long MicrosPerMinute = 60 * MicrosPerSecond;
    private const long MicrosPerDay = 1440 * MicrosPerMinute;
    private const int MinutesPerHour = 60;
    private const int FractionDigits = 6;

    /// <summary>Gives a value's text, as turborel shows it; empty of null.</summary>
    /// <param name="value">The value, of a type the codec gives.</param>
    /// <returns>The text.</returns>
    internal static string Text(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => s,
        double x => Approximate(x),
        bool truth => truth ? "TRUE" : "FALSE",
        byte[] octets => Convert.ToHexString(octets),
        Guid g => g.ToString(),
        TurborelLargeObject l => LargeObject(l),
        TurborelJson j => Json(j),
        TurborelXml x => x.Text,
        TurborelPluginValue p => p.Text,
        TurborelArray or TurborelMultiset or TurborelRow or TurborelStructuredValue or TurborelReference => Literal(value),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Gives a value's literal, as SQL writes it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    internal static string Literal(object? value) => value switch
    {
        null or DBNull => "NULL",
        string s => Quoted(s),
        byte[] octets => $"X'{Convert.ToHexString(octets)}'",
        TurborelDatetime d => $"{KeyWord(d.Moment)} '{d}'",
        TurborelInterval i => Interval(i),
        TurborelDecFloat f => $"CAST('{f}' AS DECFLOAT)",
        TurborelJson j => $"JSON('{Json(j).Replace("'", "''", StringComparison.Ordinal)}')",
        TurborelLargeObject l when l.Data is { } bytes => l.Kind == LargeObjectKind.Character
            ? Quoted(Encoding.UTF8.GetString(bytes.Span))
            : $"X'{Convert.ToHexString(bytes.Span)}'",
        TurborelArray a => $"ARRAY[{Literals(a.Elements)}]",
        TurborelMultiset m => $"MULTISET[{Literals(m.Elements)}]",
        TurborelRow r => $"ROW({Literals(r.Fields)})",
        TurborelStructuredValue s => $"{s.Schema}.{s.Name}({Literals(s.Attributes)})",
        TurborelReference r => $"REF({Convert.ToHexString(r.Bytes.Span)})",
        Guid g => $"UUID '{g}'",
        _ => Text(value),
    };

    /// <summary>Shows a datetime: its date, time and fraction in its displacement, and the displacement of a zoned one.</summary>
    /// <param name="d">The datetime.</param>
    /// <returns>The text.</returns>
    internal static string Datetime(TurborelDatetime d)
    {
        long local = d.UtcMicroseconds + (d.ZoneMinutes * MicrosPerMinute);
        if (d.Moment == DatetimeMoment.Time)
        {
            local = FloorRem(local, MicrosPerDay);
        }

        var text = new StringBuilder();
        long days = FloorDiv(local, MicrosPerDay);
        long time = FloorRem(local, MicrosPerDay);
        if (d.Moment != DatetimeMoment.Time)
        {
            var date = DateOnly.FromDayNumber((int)days);
            _ = text.Append(CultureInfo.InvariantCulture, $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}");
        }

        if (d.Moment == DatetimeMoment.Date)
        {
            return text.ToString();
        }

        _ = d.Moment == DatetimeMoment.Timestamp ? text.Append(' ') : text;
        long seconds = time / MicrosPerSecond;
        _ = text.Append(CultureInfo.InvariantCulture, $"{seconds / 3600:D2}:{seconds / 60 % 60:D2}:{seconds % 60:D2}");
        if (d.Precision > 0)
        {
            string fraction = (time % MicrosPerSecond).ToString("D6", CultureInfo.InvariantCulture);
            _ = text.Append('.').Append(fraction.AsSpan(0, Math.Min((int)d.Precision, FractionDigits)));
        }

        return d.Zoned ? text.Append(Displacement(d.ZoneMinutes)).ToString() : text.ToString();
    }

    /// <summary>Shows an interval as its literal's string: its sign, its leading field, each other two digits after its separator, the fraction to its precision.</summary>
    /// <param name="i">The interval.</param>
    /// <returns>The text.</returns>
    internal static string IntervalString(TurborelInterval i)
    {
        IntervalQualifier q = i.Qualifier;
        var text = new StringBuilder(i.Units < 0 ? "-" : string.Empty);
        var size = (UInt128)(i.Units < 0 ? -i.Units : i.Units);
        for (IntervalField field = q.Start; field <= q.End; field++)
        {
            UInt128 above = field == q.Start ? size : size % (UInt128)Unit(field - 1);
            UInt128 n = field == IntervalField.Second ? above : above / (UInt128)Unit(field);
            UInt128 whole = field == IntervalField.Second ? n / MicrosPerSecond : n;
            _ = field == q.Start
                ? text.Append(whole.ToString(CultureInfo.InvariantCulture))
                : text.Append(Separator(field)).Append(whole.ToString("D2", CultureInfo.InvariantCulture));
            if (field == IntervalField.Second && q.FractionalPrecision > 0)
            {
                string fraction = (n % MicrosPerSecond).ToString("D6", CultureInfo.InvariantCulture);
                _ = text.Append('.').Append(fraction.AsSpan(0, (int)Math.Min(q.FractionalPrecision, FractionDigits)));
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Writes a data type as SQL writes it, as turborel writes one read from
    /// the protocol: an exact numeric type <c>NUMERIC(10,0)</c>, an
    /// approximate one <c>FLOAT(53)</c>, a timestamp <c>TIMESTAMP(6)</c>,
    /// as a column's declared type is written where none is told.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The text.</returns>
    internal static string Written(DataType type) => type switch
    {
        CharacterType c => Sized(c.Varying ? "CHARACTER VARYING" : "CHARACTER", c.Length, c.Units == CharacterUnits.Octets),
        ExactType e => string.Create(CultureInfo.InvariantCulture, $"NUMERIC({e.Precision},{e.Scale})"),
        ApproximateType a => string.Create(CultureInfo.InvariantCulture, $"FLOAT({a.Precision})"),
        BinaryType b => string.Create(CultureInfo.InvariantCulture, $"{(b.Varying ? "BINARY VARYING" : "BINARY")}({b.Length})"),
        DatetimeType { Moment: DatetimeMoment.Date } => "DATE",
        DatetimeType d => string.Create(CultureInfo.InvariantCulture, $"{(d.Moment == DatetimeMoment.Time ? "TIME" : "TIMESTAMP")}({d.Precision}){(d.Zoned ? " WITH TIME ZONE" : string.Empty)}"),
        IntervalType i => "INTERVAL " + Qualifier(i.Qualifier),
        BooleanType => "BOOLEAN",
        DecFloatType f => string.Create(CultureInfo.InvariantCulture, $"DECFLOAT({f.Precision})"),
        ArrayType a => string.Create(CultureInfo.InvariantCulture, $"{Written(a.Element)} ARRAY[{a.MaxCardinality}]"),
        RowType r => $"ROW({string.Join(", ", r.Fields.Select(f => $"{f.Name} {Written(f.Type)}"))})",
        LargeObjectType { Kind: LargeObjectKind.Character } l => Sized("CHARACTER LARGE OBJECT", l.Length, l.Units == CharacterUnits.Octets),
        LargeObjectType l => string.Create(CultureInfo.InvariantCulture, $"BINARY LARGE OBJECT({l.Length})"),
        MultisetType m => $"{Written(m.Element)} MULTISET",
        StructuredType s => $"{s.Name.Schema}.{s.Name.Name}",
        ReferenceType { Scope: { } scope } r => $"REF({r.Referenced.Name.Schema}.{r.Referenced.Name.Name}) SCOPE {scope.Schema}.{scope.Name}",
        ReferenceType r => $"REF({r.Referenced.Name.Schema}.{r.Referenced.Name.Name})",
        JsonType => "JSON",
        XmlType x => Xml(x),
        UuidType => "UUID",
        PluginType p => p.Name,
        _ => type.ToString(),
    };

    /// <summary>A data type's name, as the Information Schema's DATA_TYPE gives one of a type read from the protocol: NUMERIC, FLOAT, CHARACTER VARYING.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    internal static string Named(DataType type) => type switch
    {
        CharacterType c => c.Varying ? "CHARACTER VARYING" : "CHARACTER",
        ExactType => "NUMERIC",
        ApproximateType => "FLOAT",
        BinaryType b => b.Varying ? "BINARY VARYING" : "BINARY",
        DatetimeType { Moment: DatetimeMoment.Date } => "DATE",
        DatetimeType d => (d.Moment == DatetimeMoment.Time ? "TIME" : "TIMESTAMP") + (d.Zoned ? " WITH TIME ZONE" : string.Empty),
        IntervalType => "INTERVAL",
        BooleanType => "BOOLEAN",
        DecFloatType => "DECFLOAT",
        ArrayType => "ARRAY",
        RowType => "ROW",
        LargeObjectType { Kind: LargeObjectKind.Character } => "CHARACTER LARGE OBJECT",
        LargeObjectType => "BINARY LARGE OBJECT",
        MultisetType => "MULTISET",
        StructuredType => "USER-DEFINED",
        ReferenceType => "REF",
        JsonType => "JSON",
        XmlType => "XML",
        UuidType => "UUID",
        PluginType p => p.Name,
        _ => type.ToString(),
    };

    /// <summary>Writes a qualifier, as <c>DAY(2) TO SECOND(6)</c>.</summary>
    /// <param name="q">The qualifier.</param>
    /// <returns>The text.</returns>
    internal static string Qualifier(IntervalQualifier q)
    {
        string start = q.Start.ToString().ToUpperInvariant();
        if (q.Start == q.End)
        {
            return q.End == IntervalField.Second
                ? string.Create(CultureInfo.InvariantCulture, $"SECOND({q.LeadingPrecision}, {q.FractionalPrecision})")
                : string.Create(CultureInfo.InvariantCulture, $"{start}({q.LeadingPrecision})");
        }

        string end = q.End.ToString().ToUpperInvariant();
        return q.End == IntervalField.Second
            ? string.Create(CultureInfo.InvariantCulture, $"{start}({q.LeadingPrecision}) TO {end}({q.FractionalPrecision})")
            : string.Create(CultureInfo.InvariantCulture, $"{start}({q.LeadingPrecision}) TO {end}");
    }

    /// <summary>
    /// Writes an approximate number as Rust's <c>{:E}</c> writes it, which
    /// turborel shows one by: the shortest digits that read back to it, as
    /// <c>1.5E0</c>, <c>1E-7</c>.
    /// </summary>
    /// <param name="x">The number.</param>
    /// <returns>The text.</returns>
    internal static string Approximate(double x)
    {
        if (double.IsNaN(x))
        {
            return "NaN";
        }

        if (double.IsInfinity(x))
        {
            return x > 0 ? "inf" : "-inf";
        }

        string sign = double.IsNegative(x) ? "-" : string.Empty;
        (string digits, int exponent) = Digits(Math.Abs(x).ToString("R", CultureInfo.InvariantCulture));
        string mantissa = digits.Length == 1 ? digits : $"{digits[0]}.{digits[1..]}";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{mantissa}E{exponent}");
    }

    private static string Quoted(string s) => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string Literals(IEnumerable<object?> parts) => string.Join(", ", parts.Select(Literal));

    private static string KeyWord(DatetimeMoment moment) => moment switch
    {
        DatetimeMoment.Date => "DATE",
        DatetimeMoment.Time => "TIME",
        _ => "TIMESTAMP",
    };

    private static string Interval(TurborelInterval i)
    {
        string text = IntervalString(i);
        (string sign, string body) = text.StartsWith('-') ? ("-", text[1..]) : (string.Empty, text);
        return $"INTERVAL {sign}'{body}' {Qualifier(i.Qualifier)}";
    }

    private static string LargeObject(TurborelLargeObject l) => (l.Data, l.Kind) switch
    {
        ({ } bytes, LargeObjectKind.Character) => Encoding.UTF8.GetString(bytes.Span),
        ({ } bytes, _) => Convert.ToHexString(bytes.Span),
        (null, LargeObjectKind.Character) => string.Create(CultureInfo.InvariantCulture, $"<CHARACTER LARGE OBJECT of {l.Characters} characters>"),
        _ => string.Create(CultureInfo.InvariantCulture, $"<BINARY LARGE OBJECT of {l.Octets} octets>"),
    };

    private static string Json(TurborelJson j)
    {
        try
        {
            return j.ToJsonText();
        }
        catch (TurborelException e)
        {
            return $"<{e.Message}>";
        }
    }

    private static string Displacement(short zone)
    {
        int size = Math.Abs((int)zone);
        return string.Create(CultureInfo.InvariantCulture, $"{(zone < 0 ? '-' : '+')}{size / MinutesPerHour:D2}:{size % MinutesPerHour:D2}");
    }

    private static Int128 Unit(IntervalField field) => field switch
    {
        IntervalField.Year => 12,
        IntervalField.Month => 1,
        IntervalField.Day => MicrosPerDay,
        IntervalField.Hour => 60 * MicrosPerMinute,
        IntervalField.Minute => MicrosPerMinute,
        _ => MicrosPerSecond,
    };

    private static char Separator(IntervalField field) => field switch
    {
        IntervalField.Month => '-',
        IntervalField.Hour => ' ',
        _ => ':',
    };

    // The significant digits of a number's text, and the power of ten of
    // the first.
    private static (string Digits, int Exponent) Digits(string text)
    {
        int e = text.IndexOfAny(['E', 'e']);
        int exponent = e < 0 ? 0 : int.Parse(text.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        string mantissa = e < 0 ? text : text[..e];
        int point = mantissa.IndexOf('.', StringComparison.Ordinal);
        string whole = point < 0 ? mantissa : mantissa[..point];
        string all = whole + (point < 0 ? string.Empty : mantissa[(point + 1)..]);
        string trimmed = all.TrimStart('0');
        if (trimmed.Length == 0)
        {
            return ("0", 0);
        }

        int leading = all.Length - trimmed.Length;
        string digits = trimmed.TrimEnd('0');
        return (digits.Length == 0 ? "0" : digits, exponent + whole.Length - 1 - leading);
    }

    private static long FloorDiv(long a, long b)
    {
        long q = a / b;
        return a % b != 0 && (a < 0) != (b < 0) ? q - 1 : q;
    }

    private static long FloorRem(long a, long b) => a - (FloorDiv(a, b) * b);

    // A type of a length, in characters or octets.
    private static string Sized(string name, ulong length, bool octets) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}({length}{(octets ? " OCTETS" : string.Empty)})");

    // An XML type, as turborel writes it: XML(DOCUMENT(UNTYPED)), XML(SEQUENCE),
    // or of an XML Schema XML(CONTENT(XMLSCHEMA ID S.N ...)).
    private static string Xml(XmlType x)
    {
        string primary = x.Primary.ToString().ToUpperInvariant();
        if (x.Primary == XmlPrimary.Sequence)
        {
            return "XML(SEQUENCE)";
        }

        if (x.Schema is not { } schema)
        {
            return $"XML({primary}({x.Secondary.ToString().ToUpperInvariant()}))";
        }

        var according = new System.Text.StringBuilder($"ID {schema.Name.Schema}.{schema.Name.Name}");
        _ = schema.Namespace switch
        {
            null => according,
            "" => according.Append(" NO NAMESPACE"),
            var uri => according.Append(" NAMESPACE '").Append(uri.Replace("'", "''", StringComparison.Ordinal)).Append('\''),
        };
        if (schema.Element is { } element)
        {
            _ = according.Append(" ELEMENT \"").Append(element.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        }

        return $"XML({primary}(XMLSCHEMA {according}))";
    }
}
