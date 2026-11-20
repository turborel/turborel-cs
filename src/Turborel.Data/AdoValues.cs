// The standard API's values (RULES.md D2, D18): a .NET value given as a
// parameter made the value the protocol carries, and a value read given as
// the .NET type its column's data type names. An integer or decimal is an
// exact number, a float a double, a DateTime a timestamp, a DateTimeOffset
// one with time zone, a DateOnly a date, a TimeOnly a time, a TimeSpan a
// day-time interval; the driver's own types pass through. Read back, an
// exact number is a decimal where one holds it, a date or timestamp a
// DateTime, a zoned timestamp a DateTimeOffset, a time a TimeSpan, a
// day-time interval a TimeSpan, JSON and XML their text, and anything
// .NET has no type for, a zoned time among them, the driver's own.

using System;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data;

/// <summary>Values of the standard API.</summary>
internal static class AdoValues
{
    private const long TicksPerMicrosecond = 10;
    private const int MicrosecondsPrecision = 6;
    private const uint IntervalLeading = 9;

    /// <summary>Gives the value the protocol carries for a .NET value given as a parameter.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value sent.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22000 for a value of no type the protocol carries, as the codec refuses it.</exception>
    internal static object? ToWire(object? value) => value switch
    {
        null or DBNull => null,
        int or long or short or sbyte or byte or ushort or uint => new TurborelDecimal(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture), 0),
        ulong u => new TurborelDecimal(u, 0),
        decimal d => TurborelDecimal.FromDecimal(d),
        float f => (double)f,
        char c => c.ToString(),
        DateTime d => new TurborelDatetime(DatetimeMoment.Timestamp, d.Ticks / TicksPerMicrosecond, 0, false, MicrosecondsPrecision),
        DateTimeOffset o => new TurborelDatetime(DatetimeMoment.Timestamp, o.UtcTicks / TicksPerMicrosecond, (short)o.Offset.TotalMinutes, true, MicrosecondsPrecision),
        DateOnly d => new TurborelDatetime(DatetimeMoment.Date, d.DayNumber * TimeSpan.TicksPerDay / TicksPerMicrosecond, 0, false, 0),
        TimeOnly t => new TurborelDatetime(DatetimeMoment.Time, t.Ticks / TicksPerMicrosecond, 0, false, MicrosecondsPrecision),
        TimeSpan s => new TurborelInterval(new IntervalQualifier(IntervalField.Day, IntervalField.Second, IntervalLeading, MicrosecondsPrecision), s.Ticks / TicksPerMicrosecond),
        _ => value,
    };

    /// <summary>
    /// Gives the .NET type a column reads as: of its declared type, if the
    /// server told it, SMALLINT a short, INTEGER an int, BIGINT a long, REAL
    /// a float, as ADO.NET's providers give them; else of its data type.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <returns>The type.</returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal static Type FieldType(Column column) => column.Declared?.Name switch
    {
        "SMALLINT" => typeof(short),
        "INTEGER" => typeof(int),
        "BIGINT" => typeof(long),
        "REAL" => typeof(float),
        _ => FieldType(column.Type),
    };

    /// <summary>Gives a value read of <paramref name="column"/> as the .NET type <see cref="FieldType(Column)"/> names, where it holds the value; else the driver's own value.</summary>
    /// <param name="value">The value the codec gave.</param>
    /// <param name="column">Its column.</param>
    /// <returns>The value.</returns>
    internal static object FromWire(object value, Column column) => (column.Declared?.Name, value) switch
    {
        ("SMALLINT", TurborelDecimal { Scale: 0 } d) when d.Units >= short.MinValue && d.Units <= short.MaxValue => (short)d.Units,
        ("INTEGER", TurborelDecimal { Scale: 0 } d) when d.Units >= int.MinValue && d.Units <= int.MaxValue => (int)d.Units,
        ("BIGINT", TurborelDecimal { Scale: 0 } d) when d.Units >= long.MinValue && d.Units <= long.MaxValue => (long)d.Units,
        ("REAL", double x) => (float)x,
        _ => FromWire(value),
    };

    /// <summary>Gives the .NET type a column of <paramref name="type"/> reads as.</summary>
    /// <param name="type">The column's data type.</param>
    /// <returns>The type.</returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal static Type FieldType(DataType type) => type switch
    {
        CharacterType or JsonType or XmlType => typeof(string),
        ExactType => typeof(decimal),
        ApproximateType => typeof(double),
        BinaryType => typeof(byte[]),
        BooleanType => typeof(bool),
        UuidType => typeof(Guid),
        DatetimeType { Moment: DatetimeMoment.Time, Zoned: false } => typeof(TimeSpan),
        DatetimeType { Moment: DatetimeMoment.Time } => typeof(TurborelDatetime),
        DatetimeType { Zoned: true } => typeof(DateTimeOffset),
        DatetimeType => typeof(DateTime),
        IntervalType { Qualifier.IsYearMonth: false } => typeof(TimeSpan),
        IntervalType => typeof(TurborelInterval),
        DecFloatType => typeof(TurborelDecFloat),
        LargeObjectType => typeof(TurborelLargeObject),
        ArrayType => typeof(TurborelArray),
        RowType => typeof(TurborelRow),
        MultisetType => typeof(TurborelMultiset),
        StructuredType => typeof(TurborelStructuredValue),
        ReferenceType => typeof(TurborelReference),
        _ => typeof(TurborelPluginValue),
    };

    /// <summary>Gives the driver's own type a column of <paramref name="type"/> reads as, as the codec gives it.</summary>
    /// <param name="type">The column's data type.</param>
    /// <returns>The type.</returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal static Type ProviderType(DataType type) => type switch
    {
        ExactType => typeof(TurborelDecimal),
        DatetimeType => typeof(TurborelDatetime),
        IntervalType => typeof(TurborelInterval),
        JsonType => typeof(TurborelJson),
        XmlType => typeof(TurborelXml),
        _ => FieldType(type),
    };

    /// <summary>Gives a value read as the .NET type <see cref="FieldType(DataType)"/> names, where it holds the value; else the driver's own value.</summary>
    /// <param name="value">The value the codec gave.</param>
    /// <returns>The value.</returns>
    internal static object FromWire(object value) => value switch
    {
        TurborelDecimal d => d.TryToDecimal(out decimal m) ? m : d,
        TurborelDatetime d => Datetime(d),
        TurborelInterval i when !i.Qualifier.IsYearMonth && i.Units <= long.MaxValue / TicksPerMicrosecond && i.Units >= long.MinValue / TicksPerMicrosecond =>
            TimeSpan.FromTicks((long)i.Units * TicksPerMicrosecond),
        TurborelJson j => j.ToJsonText(),
        TurborelXml x => x.Text,
        _ => value,
    };

    /// <summary>Gives a datetime as .NET's type of it: a time of day a TimeSpan, a zoned timestamp a DateTimeOffset, another a DateTime of its shown fields; a zoned time, which .NET has no type for, as it is.</summary>
    /// <param name="d">The datetime.</param>
    /// <returns>The value.</returns>
    internal static object Datetime(TurborelDatetime d)
    {
        if (d.Moment == DatetimeMoment.Time && d.Zoned)
        {
            return d;
        }

        long localTicks = (d.UtcMicroseconds + (d.ZoneMinutes * 60_000_000L)) * TicksPerMicrosecond;
        if (d.Moment == DatetimeMoment.Time && !d.Zoned)
        {
            return TimeSpan.FromTicks(((localTicks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay);
        }

        return d.Zoned
            ? new DateTimeOffset(localTicks, TimeSpan.FromMinutes(d.ZoneMinutes))
            : (object)new DateTime(localTicks, DateTimeKind.Unspecified);
    }
}
