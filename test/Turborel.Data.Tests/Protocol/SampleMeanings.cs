// What each server sample frame means, as its file says, checked of the
// reply it decodes to [PROTOCOL Sample frames]: a value frame's one value,
// a description frame's column type as SQL writes it, and each other
// reply's fields.

using System;
using System.Collections.Generic;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>The meanings of the server's sample frames.</summary>
internal static class SampleMeanings
{

    /// <summary>Each description frame's one column, its type as SQL writes it.</summary>
    private static readonly Dictionary<string, string> Types = new()
    {
        ["description-character"] = "CHARACTER(4)",
        ["description-character-octets"] = "CHARACTER(16 OCTETS)",
        ["description-exact"] = "NUMERIC(9,2)",
        ["description-approximate"] = "FLOAT(53)",
        ["description-plugin-type"] = "ST_GEOMETRY",
        ["description-varying"] = "CHARACTER VARYING(20)",
        ["description-binary"] = "BINARY(16)",
        ["description-binary-varying"] = "BINARY VARYING(255)",
        ["description-date"] = "DATE",
        ["description-time"] = "TIME(3)",
        ["description-time-zoned"] = "TIME(0) WITH TIME ZONE",
        ["description-timestamp"] = "TIMESTAMP(6)",
        ["description-timestamp-zoned"] = "TIMESTAMP(6) WITH TIME ZONE",
        ["description-interval-year-month"] = "INTERVAL YEAR(4) TO MONTH",
        ["description-interval-day-second"] = "INTERVAL DAY(2) TO SECOND(6)",
        ["description-boolean"] = "BOOLEAN",
        ["description-decfloat"] = "DECFLOAT(34)",
        ["description-clob"] = "CHARACTER LARGE OBJECT(1048576)",
        ["description-blob"] = "BINARY LARGE OBJECT(65536)",
        ["description-json"] = "JSON",
        ["description-uuid"] = "UUID",
        ["description-array"] = "BOOLEAN ARRAY[3]",
        ["description-row"] = "ROW(X CHARACTER VARYING(3), Y BOOLEAN)",
        ["description-multiset"] = "CHARACTER(2) MULTISET",
        ["description-structured"] = "S.SHAPE",
        ["description-reference"] = "REF(S.SHAPE)",
        ["description-reference-scoped"] = "REF(S.SHAPE) SCOPE S.SHAPES",
        ["description-xml-document"] = "XML(DOCUMENT(UNTYPED))",
        ["description-xml-content"] = "XML(CONTENT(ANY))",
        ["description-xml-sequence"] = "XML(SEQUENCE)",
        ["description-xml-schema"] = "XML(DOCUMENT(XMLSCHEMA ID S.PO NAMESPACE 'urn:po'))",
        ["description-distinct"] = "CHARACTER VARYING(4)",
        ["description-declared-array"] = "INTEGER ARRAY[3]",
    };

    /// <summary>Each value frame's one value, checked.</summary>
    private static readonly Dictionary<string, Action<object?>> Values = new()
    {
        ["value-null"] = v => Assert.Null(v),
        ["value-character"] = v => Assert.Equal("abc", v),
        ["value-character-empty"] = v => Assert.Equal(string.Empty, v),
        ["value-character-utf8"] = v => Assert.Equal("é€", v),
        ["value-exact"] = v => Assert.Equal(new TurborelDecimal(12345, 2), v),
        ["value-exact-negative"] = v => Assert.Equal(new TurborelDecimal(-1, 0), v),
        ["value-exact-largest"] = v => Assert.Equal(new TurborelDecimal(Int128.MaxValue, 0), v),
        ["value-approximate"] = v => Assert.Equal(1.5, v),
        ["value-approximate-negative-zero"] = v => Assert.True(double.IsNegative(Assert.IsType<double>(v)) && (double)v == 0),
        ["value-plugin"] = v => Assert.Equal(("ST_GEOMETRY", "POINT (1 2)"), (Assert.IsType<TurborelPluginValue>(v).TypeName, ((TurborelPluginValue)v).Text)),
        ["value-binary"] = v => Assert.Equal(Convert.FromHexString("DEADBEEF"), v),
        ["value-binary-empty"] = v => Assert.Equal(Array.Empty<byte>(), v),
        ["value-date"] = v => Assert.Equal(new DateTime(2024, 2, 29), AdoValues.Datetime(Assert.IsType<TurborelDatetime>(v))),
        ["value-time"] = v => Assert.Equal(new TimeSpan(0, 12, 34, 56, 500), AdoValues.Datetime(Assert.IsType<TurborelDatetime>(v))),
        ["value-time-zoned"] = v => Assert.Equal((true, (short)330, new TimeSpan(7, 4, 56).Ticks / 10), (((TurborelDatetime)v!).Zoned, ((TurborelDatetime)v).ZoneMinutes, ((TurborelDatetime)v).UtcMicroseconds)),
        ["value-timestamp"] = v => Assert.Equal(new DateTime(2024, 2, 29, 12, 34, 56).AddTicks(1_234_560), AdoValues.Datetime(Assert.IsType<TurborelDatetime>(v))),
        ["value-timestamp-zoned"] = v => Assert.Equal(new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(-8)), AdoValues.Datetime(Assert.IsType<TurborelDatetime>(v))),
        ["value-timestamp-earliest"] = v => Assert.Equal(0, Assert.IsType<TurborelDatetime>(v).UtcMicroseconds),
        ["value-interval-year-month"] = v => Assert.Equal((Int128)(-14), Assert.IsType<TurborelInterval>(v).Units),
        ["value-interval-day-second"] = v => Assert.Equal((Int128)93_784_500_000, Assert.IsType<TurborelInterval>(v).Units),
        ["value-true"] = v => Assert.Equal(true, v),
        ["value-false"] = v => Assert.Equal(false, v),
        ["value-decfloat"] = v => Assert.Equal(((Int128)(-15), -1), (Assert.IsType<TurborelDecFloat>(v).Coefficient, ((TurborelDecFloat)v).Exponent)),
        ["value-array"] = v => Assert.Equal([new TurborelDecimal(1, 0), null], Assert.IsType<TurborelArray>(v).Elements),
        ["value-array-empty"] = v => Assert.Empty(Assert.IsType<TurborelArray>(v).Elements),
        ["value-row"] = v => Assert.Equal(["x", true], Assert.IsType<TurborelRow>(v).Fields),
        ["value-lob-character"] = v => Assert.Equal("aé", Encoding.UTF8.GetString(Assert.IsType<TurborelLargeObject>(v).Data!.Value.Span)),
        ["value-lob-binary"] = v => Assert.Equal(new byte[] { 0, 0xFF }, Assert.IsType<TurborelLargeObject>(v).Data!.Value.ToArray()),
        ["value-lob-locator"] = v => Assert.Equal((true, 300_000UL, 200_000UL), (Assert.IsType<TurborelLargeObject>(v).IsLocator, ((TurborelLargeObject)v).Octets, ((TurborelLargeObject)v).Characters)),
        ["value-multiset"] = v => Assert.Equal(["a", "a"], Assert.IsType<TurborelMultiset>(v).Elements),
        ["value-structured"] = v => Assert.Equal(("S", "SHAPE", 2), (Assert.IsType<TurborelStructuredValue>(v).Schema, ((TurborelStructuredValue)v).Name, ((TurborelStructuredValue)v).Attributes.Count)),
        ["value-reference"] = v => Assert.Equal(Convert.FromHexString("0102030405060708"), Assert.IsType<TurborelReference>(v).Bytes.ToArray()),
        ["value-json"] = v => Assert.Equal("{\"a\":[1,\"x\"]}", Assert.IsType<TurborelJson>(v).ToJsonText()),
        ["value-json-scalars"] = v => Assert.Equal("[null,true,false,-2,1.50,\"\",{}]", Assert.IsType<TurborelJson>(v).ToJsonText()),
        ["value-xml"] = v => Assert.Equal("<a>&lt;</a>", Assert.IsType<TurborelXml>(v).Text),
        ["value-uuid"] = v => Assert.Equal(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), v),
    };

    /// <summary>Checks the reply a server frame of <paramref name="name"/> decoded to.</summary>
    /// <param name="name">The frame's name.</param>
    /// <param name="reply">The reply.</param>
    internal static void Check(string name, Reply reply)
    {
        if (Types.TryGetValue(name, out string? written))
        {
            Assert.Equal(written, Assert.Single(Assert.IsType<Reply.Description>(reply).Columns).TypeWritten);
        }
        else if (Values.TryGetValue(name, out Action<object?>? value))
        {
            value(Assert.Single(Assert.Single(Assert.IsType<Reply.Rows>(reply).Batch)));
        }
        else
        {
            SampleReplies.Check(name, reply);
        }
    }
}
