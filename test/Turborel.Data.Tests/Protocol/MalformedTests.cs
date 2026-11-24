// Damaged replies, every one of them in a set: each cut short at every
// byte, and each byte changed, is read, fails the connection, SQLCODE -919
// and SQLSTATE 08006, or fails as a value the client cannot take, SQLCODE
// -916 or -900, and nothing else. The changes are every one,
// not a random draw, so the test is the same at each run
// [PROTOCOL Errors and notices].

using System;
using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Json;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests that no damaged reply escapes as anything but a connection failure or a value's.</summary>
public sealed class MalformedTests
{
    /// <summary>The bytes each byte is changed to: its bits flipped, and the edges a field may hold.</summary>
    private static readonly Func<byte, byte>[] Changes = [b => (byte)~b, _ => 0, _ => 1, _ => 0x7F, _ => 0xFF];

    /// <summary>The failures a damaged reply may give: the connection's; a damaged value's; a value's past a limit.</summary>
    private static readonly (int, string)[] Failures = [(Wire.FailedCode, Wire.FailedState), (-916, "TB916"), (-900, "0A000")];

    /// <summary>Replies of every kind, their values of every tag among them.</summary>
    /// <returns>Each reply's kind and body.</returns>
    public static TheoryData<char, string> Replies()
    {
        var data = new TheoryData<char, string>();
        foreach ((char kind, byte[] body) in Bodies())
        {
            data.Add(kind, Convert.ToHexString(body));
        }

        return data;
    }

    /// <summary>A reply cut short at any byte is read or fails the connection, and nothing else.</summary>
    /// <param name="kind">The reply's kind.</param>
    /// <param name="hex">Its body.</param>
    [Theory]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[PROTOCOL Frames]")]
    [MemberData(nameof(Replies))]
    public void EveryCutIsReadOrFailsTheConnection(char kind, string hex)
    {
        byte[] body = Wire.Hex(hex);
        _ = Wire.Decode(kind, body);

        for (int cut = 0; cut < body.Length; cut++)
        {
            ReadOrFail(kind, body[..cut]);
        }
    }

    /// <summary>A reply with any byte changed is read or fails the connection, and nothing else.</summary>
    /// <param name="kind">The reply's kind.</param>
    /// <param name="hex">Its body.</param>
    [Theory]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[PROTOCOL Frames]")]
    [MemberData(nameof(Replies))]
    public void EveryChangedByteIsReadOrFailsTheConnection(char kind, string hex)
    {
        byte[] body = Wire.Hex(hex);

        for (int at = 0; at < body.Length; at++)
        {
            foreach (Func<byte, byte> change in Changes)
            {
                byte[] changed = [.. body];
                changed[at] = change(body[at]);
                ReadOrFail(kind, changed);
            }
        }
    }

    /// <summary>A JSON document with any byte changed is refused, or checked whole and read.</summary>
    [Fact]
    [Verifies("[PROTOCOL JSON values]")]
    [Verifies("[PROTOCOL Values]")]
    public void EveryChangedJsonOctetIsRefusedOrRead()
    {
        byte[] good = TurborelJson.Parse("{\"a\":[1,\"x\",-2.5,1e3,1234567890123456789],\"b\":{\"c\":null,\"c\":true}}").Octets.ToArray();

        for (int at = 0; at < good.Length; at++)
        {
            foreach (Func<byte, byte> change in Changes)
            {
                byte[] changed = [.. good];
                changed[at] = change(good[at]);
                if (JsonBinary.Validate(changed) is null)
                {
                    _ = JsonBinary.Decode(changed);
                }
            }
        }
    }

    private static void ReadOrFail(char kind, byte[] body)
    {
        try
        {
            _ = Wire.Decode(kind, body);
        }
        catch (TurborelException failure)
        {
            Assert.Contains((failure.SqlCode, failure.SqlState), Failures);
        }
    }

    private static IEnumerable<(char Kind, byte[] Body)> Bodies()
    {
        var failure = new Failure
        {
            SqlCode = -103,
            SqlState = "42000",
            Message = "m",
            Rule = "r",
            Position = 3,
            Active = true,
            Diagnostics = new Diagnostics { Severity = "ERROR", Table = "T", Constraint = "K" },
        };
        byte[] condition = Wire.Written(failure.Write);
        yield return ('E', condition);
        yield return ('K', Wire.Join(
            Wire.Hex("64000000 05000000 3032303030 01 04 0100000000000000 05 06 0200000000000000"),
            Wire.Hex("10 01000000 01000000 33 11 01000000"),
            Wire.Written(w => w.WriteBytes(condition)),
            Wire.Hex("12 0100000000000000 0200000000000000 0300000000000000")));
        yield return ('P', Wire.Join(
            Wire.Hex("07000000 01000000 01 05 FFFFFFFF 00 02000000"),
            Wire.Hex("01 02000000 4944 1A 01 02 01000000 53 01000000 4E 01 02000000 6E73 00 01"),
            Wire.Hex("00 11 01000000 01000000 41 10 0A000000 0D 00000000 01000000 02000000 00000000 00")));
        yield return ('B', Rows());
        yield return ('N', Wire.Join(Wire.Str("01T01"), Wire.Str("n"), Wire.Hex("07 06000000 4E4F54494345 0F 01000000 4B")));
        yield return ('R', Wire.Join(Wire.Str("ANN"), new byte[16], Wire.Hex("01000000"), Wire.Str("ping"), Wire.Str("turborel")));
        yield return ('L', Wire.Hex("00 02000000 C3A9"));
        yield return ('T', Wire.Join(Wire.Str("SCHEMA"), Wire.Str("ANN")));
    }

    private static byte[] Rows()
    {
        object?[] row =
        [
            null, "é", new TurborelDecimal(Int128.MinValue, 3), double.NaN, new TurborelPluginValue("G", "t", new byte[] { 1 }),
            new byte[] { 0, 1 }, new TurborelDatetime(DatetimeMoment.Timestamp, -1, -779, Zoned: true, 6),
            new TurborelInterval(new IntervalQualifier(IntervalField.Day, IntervalField.Second, 2, 6), 5), true,
            new TurborelArray([new TurborelRow([1.5, "x"]), new TurborelMultiset([false])]),
            TurborelLargeObject.Whole(LargeObjectKind.Character, Wire.Ascii("ab")),
            TurborelLargeObject.ByLocator(LargeObjectKind.Binary, 7, (3, 3)),
            new TurborelStructuredValue("S", "T", [new TurborelReference("\t"u8.ToArray())]),
            TurborelJson.Parse("[1,{\"k\":\"v\"}]"), new TurborelXml("<a/>"), Guid.Empty,
        ];
        IReadOnlyList<object?>[] rows = [row, [.. row.Reverse()]];
        return Wire.Written(w =>
        {
            w.WriteCount(rows.Length);
            foreach (IReadOnlyList<object?> each in rows)
            {
                ValueCodec.WriteList(w, each);
            }
        });
    }
}
