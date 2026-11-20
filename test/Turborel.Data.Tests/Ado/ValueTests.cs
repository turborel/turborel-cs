// The standard API's values, without a server: each .NET type a parameter
// takes made the protocol's value; each column read as the .NET type its
// data type names, by GetValue and its typed getters, and its schema; the
// driver's own value beside it (RULES.md D18).

using System;
using System.Data;
using System.Linq;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Ado;

/// <summary>Tests of the standard API's values.</summary>
public sealed class ValueTests
{
    private static readonly IntervalQualifier DayToSecond = new(IntervalField.Day, IntervalField.Second, 9, 6);
    private static readonly IntervalQualifier YearToMonth = new(IntervalField.Year, IntervalField.Month, 2, 0);
    private static readonly StructuredType Address = new(new SqlName("S", "ADDRESS"), []);
    private static readonly int[] ProviderOrdinals = [1, 7, 12, 14, 15, 0];

    /// <summary>Each .NET type a parameter takes is sent as the protocol's value; the driver's own pass through.</summary>
    [Fact]
    [Verifies("[DESIGN] D18")]
    public void ParametersAreMappedByTheirType()
    {
        var at = new DateTime(2024, 1, 2, 10, 11, 12, DateTimeKind.Unspecified);
        Assert.Null(AdoValues.ToWire(null));
        Assert.Null(AdoValues.ToWire(DBNull.Value));
        Assert.Equal(new TurborelDecimal(7, 0), AdoValues.ToWire((byte)7));
        Assert.Equal(new TurborelDecimal(-7, 0), AdoValues.ToWire(-7L));
        Assert.Equal(new TurborelDecimal(ulong.MaxValue, 0), AdoValues.ToWire(ulong.MaxValue));
        Assert.Equal(new TurborelDecimal(1234, 2), AdoValues.ToWire(12.34m));
        Assert.Equal(2.5, AdoValues.ToWire(2.5f));
        Assert.Equal("x", AdoValues.ToWire('x'));
        Assert.Equal(new TurborelDatetime(DatetimeMoment.Timestamp, at.Ticks / 10, 0, false, 6), AdoValues.ToWire(at));
        Assert.Equal(new TurborelDatetime(DatetimeMoment.Timestamp, at.Ticks / 10, 60, true, 6), AdoValues.ToWire(new DateTimeOffset(at.AddHours(1), TimeSpan.FromHours(1))));
        Assert.Equal(new TurborelDatetime(DatetimeMoment.Date, at.Date.Ticks / 10, 0, false, 0), AdoValues.ToWire(DateOnly.FromDateTime(at)));
        Assert.Equal(new TurborelDatetime(DatetimeMoment.Time, at.TimeOfDay.Ticks / 10, 0, false, 6), AdoValues.ToWire(TimeOnly.FromDateTime(at)));
        Assert.Equal(new TurborelInterval(DayToSecond, 90_061_000_000), AdoValues.ToWire(new TimeSpan(1, 1, 1, 1)));
        Assert.Equal("text", AdoValues.ToWire("text"));
        Assert.Equal(TurborelDecimal.FromDecimal(1m), AdoValues.ToWire(TurborelDecimal.FromDecimal(1m)));
    }

    /// <summary>
    /// Each column reads as .NET's type of its data type by GetValue and
    /// GetFieldType, the driver's own where .NET has none or the value is
    /// past it; the typed getters convert a number; the codec's value is
    /// given by GetProviderSpecificValue; the schema says each column's
    /// name, type and size; a misuse is TB910.
    /// </summary>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D17")]
    [Verifies("[DESIGN] D18")]
    public void ColumnsReadAsTheirTypesName()
    {
        var at = new DateTime(2024, 1, 2, 10, 11, 12);
        var guid = Guid.NewGuid();
        (DataType Type, object? Value)[] cells = Cells(at, guid);
        Column[] columns = [.. cells.Select((c, i) => new Column($"C{i}", c.Type, NotNull: i == 0))];
        using var connection = new TurborelConnection();
        using var reader = new TurborelDataReader(connection, new GatheredResults([new RowsOutcome(columns, [[.. cells.Select(c => c.Value)]])]), closeConnection: false);

        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => reader.GetValue(0)).SqlState);
        Assert.True(reader.Read());
        Assert.Equal((28, 0, false, true), (reader.FieldCount, reader.Depth, reader.IsClosed, reader.HasRows));
        object[] values = new object[cells.Length];
        Assert.Equal(cells.Length, reader.GetValues(values));
        Assert.Equal((typeof(DateTime), at, DateTimeKind.Unspecified), (values[7].GetType(), (DateTime)values[7], ((DateTime)values[7]).Kind));
        Assert.Equal(
            [
                "abc", 12.34m, new TurborelDecimal(Int128.MaxValue, 0), 2.5, new byte[] { 1, 2, 3, 4 }, true, guid, at,
                new DateTimeOffset(at, TimeSpan.FromHours(1)), at.Date, at.TimeOfDay.Subtract(TimeSpan.FromHours(1)),
                new TurborelDatetime(DatetimeMoment.Time, 0, 60, true, 0), new TimeSpan(1, 1, 1, 1), new TurborelInterval(YearToMonth, 14),
                "{\"a\":1}", "<a/>", DBNull.Value, cells[17].Value!, cells[18].Value!, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, cells[24].Value!, "z", DBNull.Value, 300m,
            ],
            values);
        Assert.Equal(
            [
                typeof(string), typeof(decimal), typeof(decimal), typeof(double), typeof(byte[]), typeof(bool), typeof(Guid), typeof(DateTime),
                typeof(DateTimeOffset), typeof(DateTime), typeof(TimeSpan), typeof(TurborelDatetime), typeof(TimeSpan), typeof(TurborelInterval),
                typeof(string), typeof(string), typeof(TurborelDecFloat), typeof(TurborelLargeObject), typeof(TurborelArray), typeof(TurborelRow),
                typeof(TurborelMultiset), typeof(TurborelStructuredValue), typeof(TurborelReference), typeof(TurborelPluginValue), typeof(TurborelLargeObject),
                typeof(string), typeof(byte[]), typeof(decimal),
            ],
            Enumerable.Range(0, cells.Length).Select(reader.GetFieldType));
        Assert.Equal(
            [
                "CHARACTER VARYING(8)", "DECIMAL(10, 2)", "DECIMAL(38, 0)", "DOUBLE PRECISION", "BINARY(4)", "BOOLEAN", "UUID", "TIMESTAMP(6)",
                "TIMESTAMP(0) WITH TIME ZONE", "DATE", "TIME(6)", "TIME(0) WITH TIME ZONE", "INTERVAL DAY(9) TO SECOND(6)", "INTERVAL YEAR(2) TO MONTH",
                "JSON", "XML", "DECFLOAT(34)", "CHARACTER LARGE OBJECT(100)", "DECIMAL(10, 0) ARRAY[4]", "ROW(A BOOLEAN)", "BOOLEAN MULTISET",
                "S.ADDRESS", "REF(S.ADDRESS)", "GEOMETRY", "BINARY LARGE OBJECT(10)", "CHARACTER(2 OCTETS)", "BINARY VARYING(2)", "DECIMAL(10, 0)",
            ],
            Enumerable.Range(0, cells.Length).Select(reader.GetDataTypeName));
        Assert.Equal(
            [typeof(TurborelDecimal), typeof(TurborelDatetime), typeof(TurborelInterval), typeof(TurborelJson), typeof(TurborelXml), typeof(string)],
            ProviderOrdinals.Select(reader.GetProviderSpecificFieldType));
        Assert.Equal((cells[1].Value, DBNull.Value), (reader.GetProviderSpecificValue(1), reader.GetProviderSpecificValue(16)));

        TypedGettersConvert(reader, at, guid);
        SchemaSaysEachColumn(reader, cells.Length);
        ReadToItsEnd(reader);
    }

    /// <summary>A day-time interval past a TimeSpan, and a reader of no results, are read as they are; a double past a decimal is refused as one, 22003.</summary>
    [Fact]
    [Verifies("[DESIGN] D18")]
    public void WhatDotNetCannotHoldIsReadAsItIs()
    {
        var huge = new TurborelInterval(DayToSecond, Int128.MaxValue);
        Assert.Equal(huge, AdoValues.FromWire(huge));
        using var connection = new TurborelConnection();
        using var empty = new TurborelDataReader(connection, new GatheredResults([]), closeConnection: false);
        Assert.Equal((0, -1, false, false), (empty.FieldCount, empty.RecordsAffected, empty.Read(), empty.NextResult()));
        Column[] doubles = [new("X", new ApproximateType(53), false), new("Y", new ApproximateType(53), false)];
        using var big = new TurborelDataReader(connection, new GatheredResults([new RowsOutcome(doubles, [[double.NaN, 1e30]])]), closeConnection: false);
        Assert.True(big.Read());
        Assert.Equal(("22003", "22003"), (Assert.Throws<TurborelException>(() => big.GetDecimal(0)).SqlState, Assert.Throws<TurborelException>(() => big.GetInt32(1)).SqlState));
    }

    // The columns of the test's row: each data type, and a value of it.
    private static (DataType Type, object? Value)[] Cells(DateTime at, Guid guid)
    {
        long utc = at.AddHours(-1).Ticks / 10;
        return
        [
            (new CharacterType(8, CharacterUnits.Characters, true), "abc"),
            (new ExactType(10, 2), new TurborelDecimal(1234, 2)),
            (new ExactType(38, 0), new TurborelDecimal(Int128.MaxValue, 0)),
            (new ApproximateType(53), 2.5),
            (new BinaryType(4, false), new byte[] { 1, 2, 3, 4 }),
            (new BooleanType(), true),
            (new UuidType(), guid),
            (new DatetimeType(DatetimeMoment.Timestamp, false, 6), new TurborelDatetime(DatetimeMoment.Timestamp, at.Ticks / 10, 0, false, 6)),
            (new DatetimeType(DatetimeMoment.Timestamp, true, 0), new TurborelDatetime(DatetimeMoment.Timestamp, utc, 60, true, 0)),
            (new DatetimeType(DatetimeMoment.Date, false, 0), new TurborelDatetime(DatetimeMoment.Date, at.Date.Ticks / 10, 0, false, 0)),
            (new DatetimeType(DatetimeMoment.Time, false, 6), new TurborelDatetime(DatetimeMoment.Time, at.TimeOfDay.Ticks / 10, -60, false, 6)),
            (new DatetimeType(DatetimeMoment.Time, true, 0), new TurborelDatetime(DatetimeMoment.Time, 0, 60, true, 0)),
            (new IntervalType(DayToSecond), new TurborelInterval(DayToSecond, 90_061_000_000)),
            (new IntervalType(YearToMonth), new TurborelInterval(YearToMonth, 14)),
            (new JsonType(), TurborelJson.Parse("{\"a\":1}")),
            (new XmlType(XmlPrimary.Document, XmlSecondary.Untyped, null), new TurborelXml("<a/>")),
            (new DecFloatType(34), null),
            (new LargeObjectType(LargeObjectKind.Character, 100, CharacterUnits.Characters), TurborelLargeObject.Whole(LargeObjectKind.Character, Encoding.UTF8.GetBytes("long"))),
            (new ArrayType(4, new ExactType(10, 0)), new TurborelArray([new TurborelDecimal(1, 0)])),
            (new RowType([new RowField("A", new BooleanType())]), null),
            (new MultisetType(new BooleanType()), null),
            (Address, null),
            (new ReferenceType(Address, null), null),
            (new PluginType("GEOMETRY", []), null),
            (new LargeObjectType(LargeObjectKind.Binary, 10, CharacterUnits.Octets), TurborelLargeObject.Whole(LargeObjectKind.Binary, new byte[] { 9, 8 })),
            (new CharacterType(2, CharacterUnits.Octets, false), "z"),
            (new BinaryType(2, true), null),
            (new ExactType(10, 0), new TurborelDecimal(300, 0)),
        ];
    }

    // The typed getters of the test's row, each converting what it can.
    private static void TypedGettersConvert(TurborelDataReader reader, DateTime at, Guid guid)
    {
        Assert.Equal(("abc", 'a', "abc", 12.34m, 12, (short)12, (byte)12, 12L), (reader.GetString(0), reader.GetChar(0), reader["C0"], reader.GetDecimal(1), reader.GetInt32(1), reader.GetInt16(1), reader.GetByte(1), reader.GetInt64(1)));
        Assert.Equal((2.5, 2.5f, 12.34, true, guid), (reader.GetDouble(3), reader.GetFloat(3), reader.GetDouble(1), reader.GetBoolean(5), reader.GetGuid(6)));
        Assert.Equal((at, at.AddHours(0), at.Date), (reader.GetDateTime(7), reader.GetDateTime(8), reader.GetDateTime(9)));
        Assert.Equal((12.34m, 2.5m, "long", "{\"a\":1}"), (reader.GetFieldValue<decimal>(1), reader.GetFieldValue<decimal>(3), reader.GetString(17), reader.GetString(14)));
        Assert.Equal((12, new TurborelDecimal(1234, 2), "abc"), (reader.GetFieldValue<int>(1), reader.GetFieldValue<TurborelDecimal>(1), reader.GetFieldValue<string>(0)));
        Assert.Equal("22003", Assert.Throws<TurborelException>(() => reader.GetDecimal(2)).SqlState);
        Assert.Equal("22003", Assert.Throws<TurborelException>(() => reader.GetByte(27)).SqlState);
        Assert.Equal("22003", Assert.Throws<TurborelException>(() => reader.GetFieldValue<byte>(27)).SqlState);
        Assert.Equal(300, reader.GetInt16(27));
        Assert.Equal((true, false, 4L, 2L), (reader.IsDBNull(16), reader.IsDBNull(0), reader.GetBytes(4, 0, null, 0, 0), reader.GetBytes(24, 0, null, 0, 0)));
        byte[] buffer = new byte[3];
        Assert.Equal(2L, reader.GetBytes(4, 2, buffer, 1, 2));
        Assert.Equal(new byte[] { 0, 3, 4 }, buffer);
        char[] chars = new char[2];
        Assert.Equal((2L, 3L), (reader.GetChars(0, 1, chars, 0, 2), reader.GetChars(0, 0, null, 0, 0)));
        Assert.Equal("bc", new string(chars));
        Assert.Equal(4, reader.GetOrdinal("c4"));
        foreach (Action misuse in new Action[]
        {
            () => reader.GetOrdinal("NONE"), () => reader.GetName(99), () => reader.GetString(16), () => reader.GetDecimal(16), () => reader.GetDecimal(0),
            () => reader.GetFieldValue<int>(16), () => reader.GetBoolean(0), () => reader.GetDateTime(0), () => reader.GetBytes(0, 0, null, 0, 0),
            () => reader.GetChar(16), () => reader.GetString(1), () => reader.GetData(0), () => reader.GetBytes(4, -1, new byte[1], 0, 1),
            () => reader.GetBytes(4, 0, new byte[1], 1, 1), () => reader.GetFieldValue<Guid>(1),
        })
        {
            Assert.Equal("TB910", Assert.Throws<TurborelException>(misuse).SqlState);
        }
    }

    // The schema of the test's row.
    private static void SchemaSaysEachColumn(TurborelDataReader reader, int columns)
    {
        DataTable schema = reader.GetSchemaTable();
        Assert.Equal((columns, "C1", 10, 2, typeof(decimal), false), (schema.Rows.Count, schema.Rows[1]["ColumnName"], schema.Rows[1]["NumericPrecision"], schema.Rows[1]["NumericScale"], schema.Rows[1]["DataType"], schema.Rows[0]["AllowDBNull"]));
        Assert.Equal((8, 53, DBNull.Value), (schema.Rows[0]["ColumnSize"], schema.Rows[3]["NumericPrecision"], schema.Rows[5]["ColumnSize"]));
        Assert.Equal(4, reader.GetColumnSchema()[4].ColumnSize);
    }

    // The reader read past its last row, then closed.
    private static void ReadToItsEnd(TurborelDataReader reader)
    {
        Assert.NotNull(reader.GetEnumerator());
        Assert.False(reader.Read());
        Assert.True(reader.HasRows);
        reader.Close();
        Assert.True(reader.IsClosed);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => reader.Read()).SqlState);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => reader.NextResult()).SqlState);
    }
}
