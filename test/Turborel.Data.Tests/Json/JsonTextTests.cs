// JSON text to and from the stored form, as turborel parses and
// serializes it: numbers by their kind, strings escaped as it escapes
// them, and the items with no text refused (SQLSTATE 22032) [PROTOCOL Values].

using Turborel.Data.Json;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Json;

/// <summary>Tests of <see cref="JsonText"/> and <see cref="JsonTextParser"/>.</summary>
public sealed class JsonTextTests
{
    /// <summary>Text parsed and written back, as turborel writes it: no white space, its numbers as their kind writes them.</summary>
    /// <param name="text">The text parsed.</param>
    /// <param name="written">The text written.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(" [ 1 , true , null ] ", "[1,true,null]")]
    [InlineData("-12.50", "-12.50")]
    [InlineData("0.000", "0.000")]
    [InlineData("1.5E+300", "1.5E300")]
    [InlineData("100e0", "1.0E2")]
    [InlineData("1e-3", "1.0E-3")]
    [InlineData("-0.0e0", "0E0")]
    [InlineData("1234567890123456789", "1234567890123456789")]
    [InlineData("0.1234567890123456789", "0.1234567890123456789")]
    [InlineData("12345678901234567890000000000000000000", "12345678901234567890000000000000000000")]
    [InlineData("\"q\\\"b\\\\s/\\u0001\\n\"", "\"q\\\"b\\\\s/\\u0001\\n\"")]
    [InlineData("{\"b\":1,\"a\":{}}", "{\"b\":1,\"a\":{}}")]
    public void TextReadsBackAsTurborelWritesIt(string text, string written)
    {
        Assert.Equal(written, TurborelJson.Parse(text).ToJsonText());
    }

    /// <summary>A number with an exponent is approximate; others exact, past 18 digits DECFLOAT.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void NumbersTakeTheirKind()
    {
        Assert.IsType<JsonItem.ApproximateItem>(JsonTextParser.Number("1e0"));
        Assert.Equal(new JsonItem.ExactItem(new TurborelDecimal(-1250, 2)), JsonTextParser.Number("-12.50"));
        Assert.Equal(new JsonItem.ExactItem(new TurborelDecimal(123, 0)), JsonTextParser.Number("123.000000000000000000000"));
        JsonItem.DecFloatItem wide = Assert.IsType<JsonItem.DecFloatItem>(JsonTextParser.Number("1234567890123456789"));
        Assert.Equal("1234567890123456789", wide.Value.Plain());
    }

    /// <summary>A number no number of turborel's is, is out of range, SQLSTATE 22003.</summary>
    /// <param name="text">The number.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData("1e400")]
    [InlineData("1e-400")]
    [InlineData("12345678901234567890123456789012345")]
    public void NumbersOutOfRangeAreRefused(string text)
    {
        TurborelException failure = Assert.Throws<TurborelException>(() => TurborelJson.Parse(text));

        Assert.Equal((-301, "22003"), (failure.SqlCode, failure.SqlState));
    }

    /// <summary>Text that is not one JSON value is refused, SQLSTATE 22032.</summary>
    /// <param name="text">The text.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData("")]
    [InlineData("[1,]")]
    [InlineData("1 2")]
    [InlineData("{a:1}")]
    [InlineData("\"\\ud800\"")]
    [InlineData("// c\n1")]
    public void TextNotJsonIsRefused(string text)
    {
        TurborelException failure = Assert.Throws<TurborelException>(() => TurborelJson.Parse(text));

        Assert.Equal((-301, "22032"), (failure.SqlCode, failure.SqlState));
    }

    /// <summary>A datetime, or an approximate number not finite, has no JSON text, SQLSTATE 22032.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ItemsWithNoTextAreRefused()
    {
        JsonItem datetime = new JsonItem.DatetimeItem(new TurborelDatetime(DatetimeMoment.Date, 0, 0, Zoned: false, 0));
        JsonItem nan = new JsonItem.ArrayItem([new JsonItem.ApproximateItem(double.NaN)]);

        foreach (JsonItem item in new[] { datetime, nan })
        {
            TurborelException failure = Assert.Throws<TurborelException>(() => TurborelJson.FromItem(item).ToJsonText());
            Assert.Equal((-301, "22032"), (failure.SqlCode, failure.SqlState));
        }
    }

    /// <summary>The approximate literal is the shortest digits that read back, one before the point.</summary>
    /// <param name="x">The number.</param>
    /// <param name="literal">Its literal.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(1234.5, "1.2345E3")]
    [InlineData(-0.25, "-2.5E-1")]
    [InlineData(1e-5, "1.0E-5")]
    [InlineData(5e-324, "5.0E-324")]
    [InlineData(double.MaxValue, "1.7976931348623157E308")]
    public void ApproximateLiteralIsShortest(double x, string literal)
    {
        Assert.Equal(literal, JsonText.ApproximateLiteral(x));
        Assert.Equal(x, double.Parse(literal, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A program's JSON text reaches the wire as the stored form a value of tag 25 carries.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void JsonParameterIsItsStoredForm()
    {
        byte[] bytes = Wire.Written(w => Turborel.Data.Protocol.ValueCodec.Write(w, TurborelJson.Parse("true")));

        Assert.Equal(Wire.Hex("19 02000000 0102"), bytes);
    }
}
