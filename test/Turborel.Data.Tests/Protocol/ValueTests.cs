// Every value, tags 0 to 27, written and read back byte for byte at its
// edges, and malformed ones failing the connection [PROTOCOL Values].

using System;
using System.Collections.Generic;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="ValueCodec"/>.</summary>
public sealed class ValueTests
{
    /// <summary>Each value's tag and body, as the page's table gives them.</summary>
    /// <returns>Each value and its bytes.</returns>
    public static TheoryData<string, string> Values() => new()
    {
        { "null", "00" },
        { "character", "01 02000000 C3A9" },
        { "exact", "02 C7CFFFFFFFFFFFFFFFFFFFFFFFFFFFFF 02000000" },
        { "approximate", "03 000000000000F8BF" },
        { "plugin", "04 03000000 474556 03000000 312C32 02000000 0102" },
        { "binary", "06 03000000 000180" },
        { "date", "08 00 00 00000000 0000000000000000" },
        { "time", "09 01 06 3C000000 00E1F50500000000" },
        { "timestamp", "0B 01 06 F5FC0000 FFFFFFFFFFFFFFFF" },
        { "interval", "0D 00000000 01000000 02000000 00000000 0E000000000000000000000000000000" },
        { "truth", "0E 01" },
        { "decfloat", "0F 0F000000000000000000000000000000 FFFFFFFF" },
        { "array", "10 02000000 10 01000000 0E 00 00" },
        { "row", "11 02000000 0E 01 01 01000000 41" },
        { "lob whole", "14 00 02000000 C3A9" },
        { "lob locator", "15 01 07000000 0000010000000000 0000000000000000" },
        { "multiset", "16 02000000 0E 01 0E 01" },
        { "structured", "17 01000000 53 01000000 54 01000000 0E 00" },
        { "reference", "18 02000000 ABCD" },
        { "json", "19 02000000 0100" },
        { "xml", "1A 0A000000 3C613E783C2F613E2020" },
        { "uuid", "1B 10000000 919108F752D143209BACF847DB4148A8" },
    };

    /// <summary>Every value reads back as written, byte for byte.</summary>
    /// <param name="name">The value, for the test's name.</param>
    /// <param name="hex">Its bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [MemberData(nameof(Values))]
    public void EveryValueReadsBackAsWritten(string name, string hex)
    {
        byte[] bytes = Wire.Hex(hex);
        object? value = Read(bytes);

        Assert.Equal(bytes, Wire.Written(w => ValueCodec.Write(w, value)));
        Assert.NotNull(name);
    }

    /// <summary>
    /// An exact number keeps its units, an <c>i128</c>, and its scale with no
    /// loss, at the limits of both.
    /// </summary>
    /// <param name="units">The units, in decimal.</param>
    /// <param name="scale">The scale.</param>
    /// <param name="text">The number as turborel writes it.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData("170141183460469231731687303715884105727", 0u, "170141183460469231731687303715884105727")]
    [InlineData("-170141183460469231731687303715884105728", 0u, "-170141183460469231731687303715884105728")]
    [InlineData("-12345", 2u, "-123.45")]
    [InlineData("5", 3u, "0.005")]
    [InlineData("1", 4294967295u, "1E-4294967295")]
    public void ExactNumbersKeepTheirUnitsAndScale(string units, uint scale, string text)
    {
        var number = new TurborelDecimal(Int128.Parse(units, System.Globalization.CultureInfo.InvariantCulture), scale);

        TurborelDecimal back = Assert.IsType<TurborelDecimal>(Read(Wire.Written(w => ValueCodec.Write(w, number))));
        Assert.Equal(number, back);
        Assert.Equal(text, back.ToString());
    }

    /// <summary>An exact number reaches <see cref="decimal"/> exactly where it holds it, and not otherwise.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ExactNumbersReachDecimalWhereItHoldsThem()
    {
        Assert.True(new TurborelDecimal(-12345, 2).TryToDecimal(out decimal small));
        Assert.Equal(-123.45m, small);
        Assert.Equal(new TurborelDecimal(-12345, 2), TurborelDecimal.FromDecimal(-123.45m));
        Assert.Equal(new TurborelDecimal(Int128.Parse("79228162514264337593543950335", System.Globalization.CultureInfo.InvariantCulture), 0), TurborelDecimal.FromDecimal(decimal.MaxValue));
        Assert.False(new TurborelDecimal(Int128.MaxValue, 0).TryToDecimal(out _));
        Assert.False(new TurborelDecimal(1, 29).TryToDecimal(out _));
    }

    /// <summary>An approximate number keeps its bits: NaN, -0.0 and the infinities.</summary>
    /// <param name="bits">The double's bits.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(0x8000_0000_0000_0000ul)]
    [InlineData(0x7FF8_0000_0000_0001ul)]
    [InlineData(0xFFF0_0000_0000_0000ul)]
    [InlineData(0x7FEF_FFFF_FFFF_FFFFul)]
    public void ApproximateNumbersKeepTheirBits(ulong bits)
    {
        double x = BitConverter.UInt64BitsToDouble(bits);

        double back = Assert.IsType<double>(Read(Wire.Written(w => ValueCodec.Write(w, x))));
        Assert.Equal(bits, BitConverter.DoubleToUInt64Bits(back));
    }

    /// <summary>
    /// A datetime's displacement is an <c>i16</c> in a <c>u32</c>'s low half,
    /// -12:59 to +14:00; its instant is UTC microseconds; its zoned byte says WITH TIME ZONE.
    /// </summary>
    /// <param name="minutes">The displacement.</param>
    /// <param name="hex">The displacement's field.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(-779, "F5FC0000")]
    [InlineData(840, "48030000")]
    [InlineData(0, "00000000")]
    public void DatetimeDisplacementIsAnI16(short minutes, string hex)
    {
        var timestamp = new TurborelDatetime(DatetimeMoment.Timestamp, -1, minutes, Zoned: true, 6);

        byte[] bytes = Wire.Written(w => ValueCodec.Write(w, timestamp));
        Assert.Equal(Wire.Join(Wire.Hex("0B 01 06"), Wire.Hex(hex), Wire.Hex("FFFFFFFFFFFFFFFF")), bytes);
        Assert.Equal(timestamp, Read(bytes));
    }

    /// <summary>A displacement past a <c>u16</c> fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void DisplacementPastI16Fails()
    {
        Wire.Fails(() => Read(Wire.Hex("0B 00 00 00000100 0000000000000000")), Sections.Values);
    }

    /// <summary>Every interval qualifier, its fields 0 YEAR to 5 SECOND, with its units.</summary>
    /// <param name="start">The leading field's number.</param>
    /// <param name="end">The last field's number.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 4)]
    [InlineData(5, 5)]
    public void EveryIntervalQualifierReadsBack(int start, int end)
    {
        var qualifier = new IntervalQualifier((IntervalField)start, (IntervalField)end, 9, 6);
        var interval = new TurborelInterval(qualifier, Int128.MinValue);

        Assert.Equal(interval, Read(Wire.Written(w => ValueCodec.Write(w, interval))));
        Assert.Equal(end <= (int)IntervalField.Month, interval.Qualifier.IsYearMonth);
    }

    /// <summary>A DECFLOAT in its one form reads back; one not in it fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void DecFloatIsInItsOneForm()
    {
        Assert.True(TurborelDecFloat.TryCreate(Int128.Parse("9999999999999999999999999999999999", System.Globalization.CultureInfo.InvariantCulture), 6111, out TurborelDecFloat greatest));
        Assert.Equal(greatest, Read(Wire.Written(w => ValueCodec.Write(w, greatest))));
        Assert.Equal("9.999999999999999999999999999999999E6144", greatest.ToString());
        Assert.Equal("1.5", Assert.IsType<TurborelDecFloat>(Read(Wire.Hex("0F 0F000000000000000000000000000000 FFFFFFFF"))).ToString());
        Wire.Fails(() => Read(Wire.Hex("0F 0A000000000000000000000000000000 00000000")), Sections.Values);
        Wire.Fails(() => Read(Wire.Hex("0F 00000000000000000000000000000000 01000000")), Sections.Values);
        Wire.Fails(() => Read(Wire.Written(w => WriteDecFloat(w, 1, 6145))), Sections.Values);
        Wire.Fails(() => Read(Wire.Written(w => WriteDecFloat(w, Int128.MaxValue, 0))), Sections.Values);
    }

    /// <summary>Arrays nest, rows hold a value for each field, multisets hold theirs in no set order.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ConstructedValuesNest()
    {
        var nested = new TurborelArray([new TurborelArray([1.5, null]), new TurborelRow([true, "x"]), new TurborelMultiset([])]);
        var structured = new TurborelStructuredValue("S", "POINT", [new TurborelDecimal(3, 0), nested]);

        byte[] bytes = Wire.Written(w => ValueCodec.Write(w, structured));
        Assert.Equal(bytes, Wire.Written(w => ValueCodec.Write(w, Read(bytes))));
        TurborelStructuredValue back = Assert.IsType<TurborelStructuredValue>(Read(bytes));
        Assert.Equal(("S", "POINT", 2), (back.Schema, back.Name, back.Attributes.Count));
    }

    /// <summary>A multiset past 65,535 elements fails the connection, as turborel bounds it.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void MultisetPastItsCardinalityFails()
    {
        byte[] elements = Wire.Join(Wire.Hex("16"), Wire.Written(w => w.WriteU32(ValueCodec.MaxMultisetCardinality + 1)), new byte[ValueCodec.MaxMultisetCardinality + 1]);

        Wire.Fails(() => Read(elements), Sections.Values);
    }

    /// <summary>Values nest at most 16 deep; deeper fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ValuesNestAtMostSixteenDeep()
    {
        Assert.IsType<TurborelArray>(Read(Deep(WireReader.MaxNesting)));
        Wire.Fails(() => Read(Deep(WireReader.MaxNesting + 1)), Sections.Values);
    }

    /// <summary>A large object comes whole, its kind then its octets, or by its locator and lengths.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void LargeObjectsComeWholeOrByLocator()
    {
        TurborelLargeObject whole = Assert.IsType<TurborelLargeObject>(Read(Wire.Hex("14 01 02000000 FF00")));
        TurborelLargeObject located = Assert.IsType<TurborelLargeObject>(Read(Wire.Hex("15 00 07000000 0300000000000000 0100000000000000")));

        Assert.Equal((LargeObjectKind.Binary, false), (whole.Kind, whole.IsLocator));
        Assert.Equal(new byte[] { 0xFF, 0 }, whole.Data?.ToArray());
        Assert.Equal((LargeObjectKind.Character, 7u, 3ul, 1ul), (located.Kind, located.Locator, located.Octets, located.Characters));
        Wire.Fails(() => Read(Wire.Hex("14 00 01000000 FF")), Sections.Values);
        Wire.Fails(() => Read(Wire.Hex("14 02 00000000")), Sections.Values);
    }

    /// <summary>A UUID is bytes, a count of 16 then the octets, in RFC 4122's order.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void UuidIsSixteenOctets()
    {
        Guid id = Assert.IsType<Guid>(Read(Wire.Hex("1B 10000000 919108F752D143209BACF847DB4148A8")));

        Assert.Equal(Guid.Parse("919108f7-52d1-4320-9bac-f847db4148a8"), id);
        Wire.Fails(() => Read(Wire.Hex("1B 0F000000 919108F752D143209BACF847DB4148")), Sections.Values);
    }

    /// <summary>A plug-in's value, which the driver need not know, is kept and sent back unchanged.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void PluginValueIsKeptUnchanged()
    {
        byte[] bytes = Wire.Hex("04 03000000 474556 03000000 312C32 02000000 0102");
        TurborelPluginValue value = Assert.IsType<TurborelPluginValue>(Read(bytes));

        Assert.Equal(("GEV", "1,2"), (value.TypeName, value.Text));
        Assert.Equal(bytes, Wire.Written(w => ValueCodec.Write(w, value)));
    }

    /// <summary>
    /// A value's tag says how it is written, not its column's type: a
    /// CHARACTER VARYING value is written with tag 1, and the tags of the
    /// varying and zoned types are read as theirs, as turborel reads them.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ValueTagIsHowItIsWritten()
    {
        Assert.Equal("é", Read(Wire.Hex("05 02000000 C3A9")));
        Assert.Equal(new byte[] { 1 }, Read(Wire.Hex("07 01000000 01")));
        TurborelDatetime zoned = Assert.IsType<TurborelDatetime>(Read(Wire.Hex("0C 01 00 3C000000 0000000000000000")));
        Assert.Equal((DatetimeMoment.Timestamp, true, (short)60), (zoned.Moment, zoned.Zoned, zoned.ZoneMinutes));
        Assert.Equal(Wire.Hex("01 01000000 41"), Wire.Written(w => ValueCodec.Write(w, "A")));
    }

    /// <summary>A JSON value's octets are checked as they come; damaged ones fail the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void JsonValueIsChecked()
    {
        TurborelJson json = Assert.IsType<TurborelJson>(Read(Wire.Hex("19 02000000 0102")));

        Assert.Equal("true", json.ToJsonText());
        Wire.Fails(() => Read(Wire.Hex("19 02000000 020C")), Sections.Values);
    }

    /// <summary>Unknown tags fail the connection; a value of no type the protocol carries is refused, SQLSTATE 22000.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void UnknownValuesFail()
    {
        Wire.Fails(() => Read(Wire.Hex("12")), Sections.Values);
        Wire.Fails(() => Read(Wire.Hex("1C")), Sections.Values);
        Wire.Fails(() => Read(Wire.Hex("0E 02")), Sections.Values);
        TurborelException refused = Assert.Throws<TurborelException>(() => Wire.Written(w => ValueCodec.Write(w, new StringBuilder())));
        Assert.Equal((-301, "22000"), (refused.SqlCode, refused.SqlState));
        Assert.Equal(Wire.Hex("00"), Wire.Written(w => ValueCodec.Write(w, DBNull.Value)));
    }

    private static object? Read(byte[] bytes)
    {
        var r = new WireReader(bytes, "value");
        object? value = ValueCodec.Read(r);
        r.Finish();
        return value;
    }

    private static void WriteDecFloat(WireWriter w, Int128 coefficient, int exponent)
    {
        w.WriteU8(Tags.DecFloat);
        w.WriteI128(coefficient);
        w.WriteI32(exponent);
    }

    private static byte[] Deep(int arrays)
    {
        var parts = new List<byte[]>();
        for (int i = 0; i < arrays; i++)
        {
            parts.Add(Wire.Hex("10 01000000"));
        }

        parts.Add(Wire.Hex("00"));
        return Wire.Join([.. parts]);
    }
}
