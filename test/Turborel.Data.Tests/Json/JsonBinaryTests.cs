// A JSON value's stored form, Turborel's binary tree: each node written
// and read back, an object's directory sorted with its members in written
// order, and damaged octets refused, never read [PROTOCOL Values].

using System;
using System.Linq;
using Turborel.Data.Json;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Json;

/// <summary>Tests of <see cref="JsonBinary"/>, <see cref="JsonChecker"/> and <see cref="JsonBinaryWriter"/>.</summary>
public sealed class JsonBinaryTests
{
    /// <summary>Each node of the format, its tag and body, as Turborel's RULES.md D89 lays them out.</summary>
    /// <returns>Each node's JSON text and its document's bytes.</returns>
    public static TheoryData<string, string> Nodes() => new()
    {
        { "null", "01 00" },
        { "false", "01 01" },
        { "true", "01 02" },
        { "7", "01 03 0700000000000000" },
        { "-12.50", "01 04 1EFBFFFFFFFFFFFF 02" },
        { "1234567890123456789", "01 06 1581E97DF41022110000000000000000 00000000" },
        { "1.5E300", "01 07 355800662DEB417E" },
        { "\"é\"", "01 08 02000000 C3A9" },
        { "[]", "01 0A 00000000 00000000" },
        { "[1,true]", "01 0A 02000000 0A000000 00000000 09000000 03 0100000000000000 02" },
        { "{\"b\":1,\"a\":2,\"b\":3}", "01 0B 03000000 2A000000 0E000000 00000000 1C000000 01000000 62 03 0100000000000000 01000000 61 03 0200000000000000 01000000 62 03 0300000000000000" },
    };

    /// <summary>Every node is written as the format lays it out, and checked and read back.</summary>
    /// <param name="text">The item's JSON text.</param>
    /// <param name="hex">The document's bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [MemberData(nameof(Nodes))]
    public void EveryNodeIsWrittenAsLaidOut(string text, string hex)
    {
        byte[] document = Wire.Hex(hex);

        Assert.Equal(document, TurborelJson.Parse(text).Octets.ToArray());
        Assert.Null(JsonBinary.Validate(document));
        Assert.Equal(TurborelJson.Parse(text).ToJsonText(), TurborelJson.Trusted(document).ToJsonText());
    }

    /// <summary>
    /// An object's directory is sorted by key octets, members of one key in
    /// written order, and the members keep the order written.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void ObjectKeepsWrittenOrderAndSortsItsDirectory()
    {
        var json = TurborelJson.Parse("{\"b\":1,\"a\":2,\"b\":3}");
        JsonItem.ObjectItem item = Assert.IsType<JsonItem.ObjectItem>(json.ToItem());

        Assert.Equal(["b", "a", "b"], item.Members.Select(m => m.Key));
        Assert.Equal("{\"b\":1,\"a\":2,\"b\":3}", json.ToJsonText());
        uint[] directory = [.. Enumerable.Range(0, 3).Select(i => JsonBinary.WordAt(json.Octets.Span, 10 + (i * 4)))];
        Assert.Equal([14u, 0u, 28u], directory);
    }

    /// <summary>An exact number is stored in the narrowest of its three forms; DECFLOAT and datetime nodes too.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void NumbersAndDatetimesKeepTheirKind()
    {
        Assert.True(TurborelDecFloat.TryCreate(15, -1, out TurborelDecFloat decfloat));
        var datetime = new TurborelDatetime(DatetimeMoment.Timestamp, -1, -779, Zoned: true, 6);
        JsonItem[] items =
        [
            new JsonItem.ExactItem(new TurborelDecimal(7, 0)),
            new JsonItem.ExactItem(new TurborelDecimal(-125, 2)),
            new JsonItem.ExactItem(new TurborelDecimal((Int128)long.MaxValue * 1000, 3)),
            new JsonItem.ExactItem(new TurborelDecimal(1, 256)),
            new JsonItem.DecFloatItem(decfloat),
            new JsonItem.ApproximateItem(-0.25),
            new JsonItem.DatetimeItem(datetime),
        ];
        byte[] tags = [JsonBinary.Integer, JsonBinary.Decimal, JsonBinary.Wide, JsonBinary.Wide, JsonBinary.DecFloat, JsonBinary.Approximate, JsonBinary.Datetime];

        for (int i = 0; i < items.Length; i++)
        {
            var json = TurborelJson.FromItem(items[i]);
            Assert.Equal(tags[i], json.Octets.Span[1]);
            Assert.Null(JsonBinary.Validate(json.Octets.Span));
            Assert.Equal(items[i], json.ToItem());
        }
    }

    /// <summary>Every cut of a document, an octet after it, another version or tag, is refused.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void DamagedDocumentsAreRefused()
    {
        byte[] good = TurborelJson.Parse("{\"a\":[1,\"x\"],\"b\":true}").Octets.ToArray();

        Assert.Null(JsonBinary.Validate(good));
        for (int cut = 0; cut < good.Length; cut++)
        {
            Assert.NotNull(JsonBinary.Validate(good.AsSpan(0, cut)));
        }

        Assert.NotNull(JsonBinary.Validate([.. good, 0]));
        Assert.NotNull(JsonBinary.Validate([2, .. good[1..]]));
        Assert.NotNull(JsonBinary.Validate([1, 12]));
    }

    /// <summary>Damaged nodes are refused, each saying why.</summary>
    /// <param name="hex">A damaged document.</param>
    /// <param name="why">What is wrong with it.</param>
    [Theory]
    [Verifies("[PROTOCOL Values]")]
    [InlineData("01 08 01000000 FF", "a string is not UTF-8")]
    [InlineData("01 09 03 0000000000000000 0000 00 00", "a datetime's moment is no moment")]
    [InlineData("01 09 02 0000000000000000 0000 02 00", "a datetime's zoned flag is neither 0 nor 1")]
    [InlineData("01 06 01000000000000000000000000000000 01180000", "a number is out of range")]
    [InlineData("01 0A 01000000 01000000 01000000 00", "an element's offset is not where it is")]
    [InlineData("01 0A 01000000 02000000 00000000 00 00", "an array's size is not its elements'")]
    [InlineData("01 0B 02000000 0C000000 06000000 00000000 01000000 61 00 01000000 62 00", "an object's key directory is not sorted")]
    [InlineData("01 0B 02000000 0C000000 00000000 00000000 01000000 61 00 01000000 62 00", "an object's key directory is not sorted")]
    [InlineData("01 0B 01000000 07000000 00000000 02000000 C328 00", "a key is not UTF-8")]
    public void DamagedNodesAreRefused(string hex, string why)
    {
        Assert.Equal(why, JsonBinary.Validate(Wire.Hex(hex)));
    }

    /// <summary>Arrays and objects nest at most 128 deep, as turborel bounds them, written and read.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void NestingIsBounded()
    {
        JsonItem within = Enumerable.Range(0, JsonBinary.MaxNesting).Aggregate((JsonItem)new JsonItem.NullItem(), (inner, _) => new JsonItem.ObjectItem([new JsonMember("k", inner)]));
        JsonItem deep = Enumerable.Range(0, JsonBinary.MaxNesting + 1).Aggregate((JsonItem)new JsonItem.NullItem(), (inner, _) => new JsonItem.ArrayItem([inner]));

        var json = TurborelJson.FromItem(within);
        Assert.Null(JsonBinary.Validate(json.Octets.Span));
        TurborelException failure = Assert.Throws<TurborelException>(() => TurborelJson.FromItem(deep));
        Assert.Equal((-900, "0A000"), (failure.SqlCode, failure.SqlState));
        byte[] deeper = [1, .. Enumerable.Range(0, JsonBinary.MaxNesting + 1).SelectMany(_ => Wire.Hex("0A 01000000 00000000 00000000")), 0];
        Assert.Equal("arrays and objects nest too deep", JsonBinary.Validate(deeper));
    }
}
