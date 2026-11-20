// Every request the client sends, encoded byte for byte: the page's worked
// example first, then each request's fields and tail, a message with an
// empty tail being its first form [PROTOCOL Requests].

using System;
using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="Request"/> and <see cref="LobRequest"/>.</summary>
public sealed class RequestTests
{
    private static readonly KeyValuePair<string, string>[] None = [];

    /// <summary>The worked example's Hello: trust as ANN, asking for ping, 41 bytes.</summary>
    [Fact]
    [Verifies("[PROTOCOL A worked example]")]
    [Verifies("[PROTOCOL Hello 'H']")]
    public void WorkedExampleHelloIsFortyOneBytes()
    {
        byte[] hello = Wire.Encode(new Request.Hello("ANN", "trust", ReadOnlyMemory<byte>.Empty, ["ping"], None));

        Assert.Equal(41, hello.Length);
        Assert.Equal(
            Wire.Hex("48 24000000 01000000 03000000414E4E 05000000747275737400000000 01000000 0400000070696E67"),
            hello);
    }

    /// <summary>The worked example's Ping, a header alone, and its Query of VALUES 1 with ends set.</summary>
    [Fact]
    [Verifies("[PROTOCOL A worked example]")]
    [Verifies("[PROTOCOL Query 'Q']")]
    public void WorkedExamplePingAndQuery()
    {
        Assert.Equal(Wire.Hex("47 00000000"), Wire.Encode(new Request.Ping()));
        Assert.Equal(
            Wire.Hex("51 0D000000 08000000 56414C5545532031 01"),
            Wire.Encode(new Request.Query("VALUES 1", Ends: true, None)));
    }

    /// <summary>Hello's attributes are its tail, tag 1: a count, then each name and value.</summary>
    [Fact]
    [Verifies("[PROTOCOL Hello 'H']")]
    [Verifies("[PROTOCOL The tail]")]
    public void HelloCarriesAttributesInItsTail()
    {
        KeyValuePair<string, string>[] attributes = [new("application", "app")];
        (byte _, byte[] body) = Wire.Unframe(Wire.Encode(new Request.Hello("A", "trust", ReadOnlyMemory<byte>.Empty, [], attributes)));

        Assert.EndsWith(
            Convert.ToHexString(Wire.Hex("01 01000000 0B000000 6170706C69636174696F6E 03000000 617070")),
            Convert.ToHexString(body), StringComparison.Ordinal);
    }

    /// <summary>Query's tail: attributes, tag 1, then multi, tag 2; none, its first form.</summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL The tail]")]
    public void QueryTailIsAttributesThenMulti()
    {
        KeyValuePair<string, string>[] attributes = [new("k", "v")];

        Assert.Equal(Wire.Hex("51 07000000 02000000 4142 01"), Wire.Encode(new Request.Query("AB", Ends: true, None)));
        Assert.Equal(
            Wire.Hex("51 17000000 02000000 4142 00 01 01000000 01000000 6B 01000000 76 02"),
            Wire.Encode(new Request.Query("AB", Ends: false, attributes, Multi: true)));
    }

    /// <summary>Execute: statement, values, rows, ends, then its tail of attributes and cursor.</summary>
    [Fact]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL The tail]")]
    public void ExecuteFieldsThenTail()
    {
        object?[] values = [null, "é", new TurborelDecimal(-5, 1)];

        Assert.Equal(
            Wire.Hex("45 0D000000 01000000 00000000 00000000 01"),
            Wire.Encode(new Request.Execute(new StatementCursor(1), [], 0, Ends: true, None)));
        Assert.Equal(
            Wire.Join(
                Wire.Hex("45 2F000000 02000000 03000000 00 01 02000000 C3A9"),
                Wire.Hex("02 FBFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF 01000000 0A000000 00 03 09000000")),
            Wire.Encode(new Request.Execute(new StatementCursor(2, 9), values, 10, Ends: false, None)));
    }

    /// <summary>Prepare, Fetch, Close and Deallocate, a cursor of 0 leaving no tail.</summary>
    [Fact]
    [Verifies("[PROTOCOL Prepare 'P']")]
    [Verifies("[PROTOCOL Fetch 'F']")]
    [Verifies("[PROTOCOL Close 'C']")]
    [Verifies("[PROTOCOL Deallocate 'D']")]
    [Verifies("[PROTOCOL The tail]")]
    public void StatementRequests()
    {
        Assert.Equal(Wire.Hex("50 09000000 FFFFFFFF 01000000 3F"), Wire.Encode(new Request.Prepare(uint.MaxValue, "?")));
        Assert.Equal(Wire.Hex("46 08000000 02000000 FFFFFFFF"), Wire.Encode(new Request.Fetch(new StatementCursor(2), uint.MaxValue)));
        Assert.Equal(Wire.Hex("46 0D000000 02000000 05000000 03 04000000"), Wire.Encode(new Request.Fetch(new StatementCursor(2, 4), 5)));
        Assert.Equal(Wire.Hex("43 04000000 03000000"), Wire.Encode(new Request.Close(new StatementCursor(3))));
        Assert.Equal(Wire.Hex("43 09000000 03000000 03 01000000"), Wire.Encode(new Request.Close(new StatementCursor(3, 1))));
        Assert.Equal(Wire.Hex("44 04000000 04000000"), Wire.Encode(new Request.Deallocate(4)));
    }

    /// <summary>Answer, Cancel and Terminate; a secret is never in a request's text.</summary>
    [Fact]
    [Verifies("[PROTOCOL Answer 'A']")]
    [Verifies("[PROTOCOL Cancel 'Z']")]
    [Verifies("[PROTOCOL Terminate 'X']")]
    public void AnswerCancelTerminate()
    {
        byte[] key = [.. Enumerable.Repeat((byte)7, 16)];
        byte[] secret = Wire.Ascii("s3cret");

        Assert.Equal(Wire.Join(Wire.Hex("41 0A000000 06000000"), secret), Wire.Encode(new Request.Answer(secret)));
        Assert.Equal(Wire.Join(Wire.Hex("5A 10000000"), key), Wire.Encode(new Request.Cancel(key)));
        Assert.Equal(Wire.Hex("58 00000000"), Wire.Encode(new Request.Terminate()));
        Assert.DoesNotContain("s3cret", new Request.Hello("A", "password", secret, [], None).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("7", new Request.Cancel(key).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", new Request.ChangeUser("A", "password", secret).ToString(), StringComparison.Ordinal);
        Assert.Equal("Answer { .. }", new Request.Answer(secret).ToString());
    }

    /// <summary>Describe of a statement, cursor 0, or of a cursor; Ping, Reset and ChangeUser.</summary>
    [Fact]
    [Verifies("[PROTOCOL Describe 'Y']")]
    [Verifies("[PROTOCOL Reset 'W']")]
    [Verifies("[PROTOCOL ChangeUser 'U']")]
    [Verifies("[PROTOCOL Ping 'G']")]
    public void SessionRequests()
    {
        Assert.Equal(Wire.Hex("59 09000000 00 02000000 00000000"), Wire.Encode(new Request.Describe(new StatementCursor(2, 5), OfCursor: false)));
        Assert.Equal(Wire.Hex("59 09000000 01 02000000 05000000"), Wire.Encode(new Request.Describe(new StatementCursor(2, 5), OfCursor: true)));
        Assert.Equal(Wire.Hex("57 00000000"), Wire.Encode(new Request.Reset()));
        Assert.Equal(Wire.Hex("47 00000000"), Wire.Encode(new Request.Ping()));
        Assert.Equal(
            Wire.Hex("55 12000000 01000000 42 05000000 7472757374 00000000"),
            Wire.Encode(new Request.ChangeUser("B", "trust", ReadOnlyMemory<byte>.Empty)));
    }

    /// <summary>The Lob request by its five ops.</summary>
    [Fact]
    [Verifies("[PROTOCOL Lob 'L'] the request")]
    public void LobRequestsByOp()
    {
        Assert.Equal(
            Wire.Hex("4C 16000000 00 07000000 0100000000000000 0000040000000000 01"),
            Wire.Encode(new LobRequest.Read(7, 1, 262_144, Characters: true)));
        Assert.Equal(Wire.Hex("4C 02000000 01 01"), Wire.Encode(new LobRequest.Create(LargeObjectKind.Binary)));
        Assert.Equal(Wire.Hex("4C 0B000000 02 07000000 02000000 C3A9"), Wire.Encode(new LobRequest.Append(7, Wire.Hex("C3A9"))));
        Assert.Equal(Wire.Hex("4C 05000000 03 07000000"), Wire.Encode(new LobRequest.Free(7)));
        Assert.Equal(Wire.Hex("4C 05000000 04 07000000"), Wire.Encode(new LobRequest.Hold(7)));
    }

    /// <summary>CopyIn, CopyData and CopyEnd.</summary>
    [Fact]
    [Verifies("[PROTOCOL CopyIn 'I']")]
    [Verifies("[PROTOCOL CopyData 'J']")]
    [Verifies("[PROTOCOL CopyEnd 'N']")]
    public void CopyRequests()
    {
        IReadOnlyList<object?>[] rows = [[new TurborelDecimal(7, 0), "N"], [null, null]];

        Assert.Equal(Wire.Hex("49 10000000 01000000 54 01000000 02000000 4944 01"), Wire.Encode(new Request.CopyIn("T", ["ID"], Ends: true)));
        Assert.Equal(Wire.Hex("49 0A000000 01000000 54 00000000 00"), Wire.Encode(new Request.CopyIn("T", [], Ends: false)));
        Assert.Equal(
            Wire.Hex("4A 29000000 02000000 02000000 02 07000000000000000000000000000000 00000000 01 01000000 4E 02000000 00 00"),
            Wire.Encode(new Request.CopyData(rows)));
        Assert.Equal(Wire.Hex("4E 01000000 01"), Wire.Encode(new Request.CopyEnd(Abandon: true)));
    }

    /// <summary>The capabilities' names, in the order of the protocol's table.</summary>
    [Fact]
    [Verifies("[PROTOCOL Capabilities]")]
    public void CapabilitiesAreNamedInTheTablesOrder()
    {
        Assert.Equal(
            ["notices", "status", "diagnostics", "describe", "cursors", "ping", "reset", "change-user", "query-attributes",
             "connection-attributes", "multi-statement", "compression", "commit-position", "copy", "row-counts",
             "generated-values", "fetch-ends", "conditions", "declared-types"],
            Capabilities.All);
    }

    /// <summary>
    /// At most 32 attributes, a name of 1 to 64 bytes and a value of at most
    /// 1024: more are refused before anything is sent, as the server would
    /// fail the connection over them.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL The tail]")]
    public void AttributesAreBounded()
    {
        KeyValuePair<string, string>[] most = [.. Enumerable.Range(0, TailTags.MaxAttributes).Select(i => new KeyValuePair<string, string>(new string('n', TailTags.MaxAttributeName), new string('v', TailTags.MaxAttributeValue)))];
        KeyValuePair<string, string>[] tooMany = [.. most, new("x", "y")];

        _ = Wire.Encode(new Request.Query("Q", Ends: false, most));
        Wire.Fails(() => Wire.Encode(new Request.Query("Q", Ends: false, tooMany)), Sections.Tail);
        Wire.Fails(() => Wire.Encode(new Request.Query("Q", Ends: false, [new(string.Empty, "v")])), Sections.Tail);
        Wire.Fails(() => Wire.Encode(new Request.Query("Q", Ends: false, [new(new string('n', 65), "v")])), Sections.Tail);
        Wire.Fails(() => Wire.Encode(new Request.Query("Q", Ends: false, [new("n", new string('v', 1025))])), Sections.Tail);
    }
}
