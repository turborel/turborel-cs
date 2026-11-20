// The fields every message is made of, written and read back, at their
// edges, and malformed ones failing the connection [PROTOCOL Values and fields].

using System;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="WireReader"/> and <see cref="WireWriter"/>.</summary>
public sealed class FieldTests
{
    /// <summary>Integers are least significant byte first; a signed one is its bits in the unsigned field.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void IntegersAreLittleEndian()
    {
        byte[] bytes = Wire.Written(w =>
        {
            w.WriteU8(0xAB);
            w.WriteU32(0x0403_0201);
            w.WriteU64(0x0807_0605_0403_0201);
            w.WriteI32(-2);
            w.WriteI64(-2);
        });

        Assert.Equal(Wire.Hex("AB 01020304 0102030405060708 FEFFFFFF FEFFFFFFFFFFFFFF"), bytes);
        var r = new WireReader(bytes, "test");
        Assert.Equal(0xAB, r.ReadU8());
        Assert.Equal(0x0403_0201u, r.ReadU32());
        Assert.Equal(0x0807_0605_0403_0201ul, r.ReadU64());
        Assert.Equal(-2, r.ReadI32());
        Assert.Equal(-2L, r.ReadI64());
        r.Finish();
    }

    /// <summary>An <c>i128</c> is 16 bytes, two's complement, at its limits.</summary>
    /// <param name="hex">The bytes expected.</param>
    /// <param name="text">The number, in decimal.</param>
    [Theory]
    [Verifies("[PROTOCOL Values and fields]")]
    [InlineData("01000000000000000000000000000000", "1")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "-1")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFF7F", "170141183460469231731687303715884105727")]
    [InlineData("00000000000000000000000000000080", "-170141183460469231731687303715884105728")]
    public void I128IsTwosComplement(string hex, string text)
    {
        var value = Int128.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(Wire.Hex(hex), Wire.Written(w => w.WriteI128(value)));
        Assert.Equal(value, new WireReader(Wire.Hex(hex), "test").ReadI128());
    }

    /// <summary>A flag is 0 or 1; any other byte fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void FlagIsZeroOrOne()
    {
        byte[] flags = [0, 1, 2];
        var r = new WireReader(flags, "test");

        Assert.False(r.ReadFlag());
        Assert.True(r.ReadFlag());
        Wire.Fails(() => r.ReadFlag(), Sections.Fields);
    }

    /// <summary>A string is its length in bytes, then its UTF-8; bytes are their length, then the bytes.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void StringsAndBytesFollowTheirLength()
    {
        byte[] bytes = Wire.Written(w =>
        {
            w.WriteString("é");
            w.WriteBytes([9, 8]);
            w.WriteStrings(["a", string.Empty]);
        });

        Assert.Equal(Wire.Hex("02000000 C3A9 02000000 0908 02000000 01000000 61 00000000"), bytes);
        var r = new WireReader(bytes, "test");
        Assert.Equal("é", r.ReadString());
        Assert.Equal(new byte[] { 9, 8 }, r.ReadBytes().ToArray());
        Assert.Equal(["a", string.Empty], r.ReadStrings());
        r.Finish();
    }

    /// <summary>A string that is not UTF-8 fails the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void StringNotUtf8Fails()
    {
        Wire.Fails(() => new WireReader(Wire.Hex("02000000 C328"), "test").ReadString(), Sections.Fields);
    }

    /// <summary>A key is 16 bytes with no length before them.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void KeyHasNoLength()
    {
        byte[] key = [.. new byte[16]];
        key[0] = 7;
        var r = new WireReader(key, "test");

        Assert.Equal(key, r.ReadKey());
        Assert.True(r.AtEnd);
    }

    /// <summary>
    /// A count, or a length, larger than what follows fails as the body runs
    /// out, without reserving memory for it.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    public void CountPastTheBodyFailsWithoutReserving()
    {
        long before = GC.GetAllocatedBytesForCurrentThread();

        Wire.Fails(() => new WireReader(Wire.Hex("FFFFFFFF 00000000"), "test").ReadStrings(), Sections.Frames);
        Wire.Fails(() => new WireReader(Wire.Hex("FFFFFFFF 41"), "test").ReadBytes());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20, "nothing reserved for the count");
    }

    /// <summary>A field cut short, or bytes left over after the last field, fail the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    public void CutShortOrLeftOverFails()
    {
        Wire.Fails(() => new WireReader(Wire.Hex("010203"), "test").ReadU32(), Sections.Frames);
        var r = new WireReader(Wire.Hex("01 02"), "test");
        _ = r.ReadU8();
        Wire.Fails(r.Finish, Sections.Frames);
    }
}
