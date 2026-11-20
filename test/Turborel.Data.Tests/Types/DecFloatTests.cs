// DECFLOAT values in turborel's one form: written as it writes them, and
// rounded half to even to 34 digits where a JSON document's node is read
// [PROTOCOL Values].

using System.Numerics;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Types;

/// <summary>Tests of <see cref="TurborelDecFloat"/>.</summary>
public sealed class DecFloatTests
{
    /// <summary>A DECFLOAT is written plainly from an adjusted exponent of -6 to 33, else with an exponent.</summary>
    /// <param name="coefficient">The coefficient.</param>
    /// <param name="exponent">The exponent.</param>
    /// <param name="text">Its text.</param>
    /// <param name="plain">Its plain text, as JSON text writes it.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData(0, 0, "0", "0")]
    [InlineData(-15, -1, "-1.5", "-1.5")]
    [InlineData(1, -6, "0.000001", "0.000001")]
    [InlineData(1, -7, "1E-7", "0.0000001")]
    [InlineData(-123, -9, "-1.23E-7", "-0.000000123")]
    [InlineData(7, 3, "7000", "7000")]
    [InlineData(1, 34, "1E34", "10000000000000000000000000000000000")]
    public void WrittenAsTurborelWritesIt(long coefficient, int exponent, string text, string plain)
    {
        Assert.True(TurborelDecFloat.TryCreate(coefficient, exponent, out TurborelDecFloat value));

        Assert.Equal((text, plain), (value.ToString(), value.Plain()));
    }

    /// <summary>A coefficient past 34 digits rounds half to even, then takes its one form.</summary>
    /// <param name="digits">The coefficient, in decimal.</param>
    /// <param name="coefficient">The coefficient expected.</param>
    /// <param name="exponent">The exponent expected.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData("12345678901234567890123456789012345", "1234567890123456789012345678901234", 1)]
    [InlineData("12345678901234567890123456789012355", "1234567890123456789012345678901236", 1)]
    [InlineData("12345678901234567890123456789012365", "1234567890123456789012345678901236", 1)]
    [InlineData("12345678901234567890123456789012366", "1234567890123456789012345678901237", 1)]
    [InlineData("99999999999999999999999999999999995", "1", 35)]
    [InlineData("1000", "1", 3)]
    public void RoundsHalfToEven(string digits, string coefficient, int exponent)
    {
        Assert.True(TurborelDecFloat.TryRound(BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture), 0, out TurborelDecFloat value));

        Assert.Equal((coefficient, exponent), (value.Coefficient.ToString(System.Globalization.CultureInfo.InvariantCulture), value.Exponent));
    }

    /// <summary>A value out of decimal128's range is none; zero is zero, of exponent 0.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void OutOfRangeIsNone()
    {
        Assert.False(TurborelDecFloat.TryRound(1, 6145, out _));
        Assert.False(TurborelDecFloat.TryRound(1, -6144, out _));
        Assert.False(TurborelDecFloat.TryRound(1, long.MaxValue - 1, out _));
        Assert.True(TurborelDecFloat.TryRound(0, 99, out TurborelDecFloat zero));
        Assert.Equal(TurborelDecFloat.Zero, zero);
        Assert.False(TurborelDecFloat.TryCreate(0, 1, out _));
        Assert.False(TurborelDecFloat.TryCreate(10, 0, out _));
    }
}
