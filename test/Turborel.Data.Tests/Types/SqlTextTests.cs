// A value's text and literal as turborel writes them, which an export
// writes for each value: an approximate number as Rust's {:E} writes it, a
// datetime in its displacement, an interval as its literal's string, a
// composite value as its literal [CLIENT Copying and exporting].

using System;
using System.Text;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Types;

/// <summary>Tests of <see cref="SqlText"/>.</summary>
public sealed class SqlTextTests
{
    /// <summary>An approximate number is written as <c>mantissaEexponent</c>, its shortest digits; not finite as Rust writes it.</summary>
    /// <param name="x">The number.</param>
    /// <param name="text">Its text.</param>
    [Theory]
    [Verifies("[CLIENT Copying and exporting]")]
    [InlineData(1.5, "1.5E0")]
    [InlineData(100.0, "1E2")]
    [InlineData(0.1, "1E-1")]
    [InlineData(1e300, "1E300")]
    [InlineData(123456.789, "1.23456789E5")]
    [InlineData(-2.5e-7, "-2.5E-7")]
    [InlineData(0.0, "0E0")]
    [InlineData(-0.0, "-0E0")]
    [InlineData(5e-324, "5E-324")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "inf")]
    [InlineData(double.NegativeInfinity, "-inf")]
    public void ApproximateIsMantissaAndExponent(double x, string text) => Assert.Equal(text, SqlText.Text(x));

    /// <summary>A datetime is shown in its displacement, its fraction to its precision, a zoned one's displacement after.</summary>
    [Fact]
    [Verifies("[CLIENT Copying and exporting]")]
    public void DatetimeIsShownInItsDisplacement()
    {
        long midnight = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc).Ticks / 10;
        long instant = new DateTime(2024, 1, 2, 10, 11, 12, 500, DateTimeKind.Utc).Ticks / 10;

        Assert.Equal("2024-01-02", new TurborelDatetime(DatetimeMoment.Date, midnight, 0, false, 0).ToString());
        Assert.Equal("2024-01-02 10:11:12.5", new TurborelDatetime(DatetimeMoment.Timestamp, instant, 0, false, 1).ToString());
        Assert.Equal("2024-01-02 15:41:12.500000+05:30", new TurborelDatetime(DatetimeMoment.Timestamp, instant, 330, true, 6).ToString());
        Assert.Equal("23:00:00-01:00", new TurborelDatetime(DatetimeMoment.Time, 0, -60, true, 0).ToString());
        Assert.Equal("TIMESTAMP '2024-01-02 10:11:12'", SqlText.Literal(new TurborelDatetime(DatetimeMoment.Timestamp, instant, 0, false, 0)));
    }

    /// <summary>An interval is its literal's string: its sign, its leading field, each other two digits after its separator, its fraction.</summary>
    [Fact]
    [Verifies("[CLIENT Copying and exporting]")]
    public void IntervalIsItsLiteralsString()
    {
        var dayToSecond = new IntervalQualifier(IntervalField.Day, IntervalField.Second, 2, 1);
        long units = ((((1L * 24) + 2) * 60 + 3) * 60 + 4) * 1_000_000 + 500_000;

        Assert.Equal("-1 02:03:04.5", new TurborelInterval(dayToSecond, -units).ToString());
        Assert.Equal("1-02", new TurborelInterval(new IntervalQualifier(IntervalField.Year, IntervalField.Month, 2, 0), 14).ToString());
        Assert.Equal("3", new TurborelInterval(new IntervalQualifier(IntervalField.Day, IntervalField.Day, 2, 0), 3 * 86_400_000_000L).ToString());
        Assert.Equal("90.25", new TurborelInterval(new IntervalQualifier(IntervalField.Second, IntervalField.Second, 2, 2), 90_250_000).ToString());
        Assert.Equal("INTERVAL -'1 02:03:04.5' DAY(2) TO SECOND(1)", SqlText.Literal(new TurborelInterval(dayToSecond, -units)));
        Assert.Equal("SECOND(2, 2)", new IntervalQualifier(IntervalField.Second, IntervalField.Second, 2, 2).ToString());
        Assert.Equal("HOUR(3) TO MINUTE", new IntervalQualifier(IntervalField.Hour, IntervalField.Minute, 3, 0).ToString());
        Assert.Equal("MONTH(2)", new IntervalQualifier(IntervalField.Month, IntervalField.Month, 2, 0).ToString());
    }

    /// <summary>Each other kind of value is shown as turborel shows it, and a composite value as its literal.</summary>
    [Fact]
    [Verifies("[CLIENT Copying and exporting]")]
    public void ValuesAndLiterals()
    {
        Assert.Equal(("NULL", "TRUE", "FALSE", "0AFF"), (SqlText.Text(null), SqlText.Text(true), SqlText.Text(false), SqlText.Text(new byte[] { 0x0A, 0xFF })));
        Assert.Equal("ARRAY[1, 'it''s', NULL]", SqlText.Text(new TurborelArray([new TurborelDecimal(1, 0), "it's", null])));
        Assert.Equal("ROW(X'01', TRUE)", SqlText.Text(new TurborelRow([new byte[] { 1 }, true])));
        Assert.Equal("MULTISET[2.5E0]", SqlText.Text(new TurborelMultiset([2.5])));
        Assert.Equal("S.POINT(1, 2)", SqlText.Text(new TurborelStructuredValue("S", "POINT", [new TurborelDecimal(1, 0), new TurborelDecimal(2, 0)])));
        Assert.Equal("REF(0102)", SqlText.Text(new TurborelReference(new byte[] { 1, 2 })));
        Assert.Equal("ARRAY[UUID '919108f7-52d1-4320-9bac-f847db4148a8']", SqlText.Text(new TurborelArray([Guid.Parse("919108f7-52d1-4320-9bac-f847db4148a8")])));
        Assert.Equal("ARRAY[JSON('{\"a\":\"''x''\"}'), CAST('1.5' AS DECFLOAT)]", SqlText.Text(new TurborelArray([TurborelJson.Parse("{\"a\":\"'x'\"}"), Dec(15, -1)])));
        Assert.Equal(("ab", "0A"), (SqlText.Text(TurborelLargeObject.Whole(LargeObjectKind.Character, "ab"u8.ToArray())), SqlText.Text(TurborelLargeObject.Whole(LargeObjectKind.Binary, "\n"u8.ToArray()))));
        Assert.Equal("ARRAY['a''b', X'0A']", SqlText.Text(new TurborelArray([TurborelLargeObject.Whole(LargeObjectKind.Character, Encoding.UTF8.GetBytes("a'b")), TurborelLargeObject.Whole(LargeObjectKind.Binary, "\n"u8.ToArray())])));
        Assert.Equal("<CHARACTER LARGE OBJECT of 7 characters>", SqlText.Text(TurborelLargeObject.ByLocator(LargeObjectKind.Character, 1, (9, 7))));
        Assert.Equal("<BINARY LARGE OBJECT of 9 octets>", SqlText.Text(TurborelLargeObject.ByLocator(LargeObjectKind.Binary, 1, (9, 0))));
        Assert.Equal(("t", "<x>"), (SqlText.Text(new TurborelPluginValue("P", "t", Array.Empty<byte>())), SqlText.Text(new TurborelXml("<x>"))));
        Assert.Equal("1.50", SqlText.Text(new TurborelDecimal(150, 2)));
    }

    private static TurborelDecFloat Dec(long coefficient, int exponent) =>
        TurborelDecFloat.TryCreate(coefficient, exponent, out TurborelDecFloat value) ? value : throw new InvalidOperationException();
}
