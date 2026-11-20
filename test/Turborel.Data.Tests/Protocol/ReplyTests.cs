// Every reply the client receives, decoded from its bytes: the page's
// worked example first, then each reply and its tail, and malformed ones
// failing the connection [PROTOCOL Replies].

using System;
using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="Reply"/>.</summary>
public sealed class ReplyTests
{
    /// <summary>The worked example's Rows: one row of the exact number 1, 34 bytes.</summary>
    [Fact]
    [Verifies("[PROTOCOL A worked example]")]
    [Verifies("[PROTOCOL Rows 'B']")]
    public void WorkedExampleRows()
    {
        byte[] frame = Wire.Hex("42 1D000000 01000000 01000000 02 0100000000000000 0000000000000000 00000000");

        Reply.Rows rows = Assert.IsType<Reply.Rows>(Wire.DecodeReply(frame));
        Assert.Equal(new TurborelDecimal(1, 0), Assert.Single(Assert.Single(rows.Batch)));
    }

    /// <summary>The worked example's Complete, its tail empty, and with row-counts its tags 6 and 18.</summary>
    [Fact]
    [Verifies("[PROTOCOL A worked example]")]
    [Verifies("[PROTOCOL Complete 'K']")]
    public void WorkedExampleComplete()
    {
        byte[] frame = Wire.Hex("4B 0E000000 00000000 05000000 3030303030 00");
        byte[] counted = Wire.Join(
            Wire.Hex("00000000 05000000 3030303030 00"),
            Wire.Hex("06 0100000000000000"),
            Wire.Hex("12 B400000000000000 0400000000000000 0000000000000000"));

        Completion first = Assert.IsType<Reply.Complete>(Wire.DecodeReply(frame)).Completion;
        Assert.Equal(new Completion { SqlCode = 0, SqlState = "00000", Active = false }, first with { Generated = [], Conditions = [] });
        Completion withCounts = Assert.IsType<Reply.Complete>(Wire.Decode('K', counted)).Completion;
        Assert.Equal((1ul, new Timing(180, 4, 0)), (withCounts.Rows, withCounts.Time));
    }

    /// <summary>Complete's whole tail: position, more, rows, generated, conditions, time, in order.</summary>
    [Fact]
    [Verifies("[PROTOCOL Complete 'K']")]
    [Verifies("[PROTOCOL The tail]")]
    public void CompleteTailInOrder()
    {
        var condition = new Failure { SqlCode = 0, SqlState = "01003", Message = "nulls", Diagnostics = new Diagnostics { Severity = "WARNING" } };
        byte[] conditionBody = Wire.Written(condition.Write);
        byte[] body = Wire.Join(
            Wire.Hex("64000000 05000000 3032303030 01"),
            Wire.Hex("04 1267000000000000 05 06 0300000000000000"),
            Wire.Hex("10 02000000 01000000 33 01000000 34"),
            Wire.Hex("11 01000000"),
            Wire.Written(w => w.WriteBytes(conditionBody)),
            Wire.Hex("12 0100000000000000 0200000000000000 0300000000000000"));

        Completion c = Assert.IsType<Reply.Complete>(Wire.Decode('K', body)).Completion;
        Assert.Equal((100, "02000", true, 0x6712ul, true, 3ul), (c.SqlCode, c.SqlState, c.Active, c.Position!.Value, c.More, c.Rows!.Value));
        Assert.Equal(["3", "4"], c.Generated);
        Assert.Equal(condition, Assert.Single(c.Conditions));
        Assert.Equal(new Timing(1, 2, 3), c.Time);
    }

    /// <summary>A tag out of order, repeated, or not the message's fails the connection.</summary>
    /// <param name="tail">The tail's bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL The tail]")]
    [InlineData("06 0100000000000000 04 0100000000000000")]
    [InlineData("05 05")]
    [InlineData("07 01000000 41")]
    [InlineData("13")]
    [InlineData("01 00000000")]
    public void TailOutOfPlaceFails(string tail)
    {
        byte[] body = Wire.Join(Wire.Hex("00000000 05000000 3030303030 00"), Wire.Hex(tail));

        Wire.Fails(() => Wire.Decode('K', body), Sections.Tail);
    }

    /// <summary>An Error's failure: SQLCODE, SQLSTATE, message, rule, position, active, and its diagnostics.</summary>
    [Fact]
    [Verifies("[PROTOCOL Error 'E']")]
    [Verifies("[PROTOCOL Composite fields]")]
    [Verifies("[PROTOCOL Errors and notices]")]
    public void ErrorCarriesTheWholeFailure()
    {
        var diagnostics = new Diagnostics
        {
            Severity = "ERROR",
            Detail = "d",
            Hint = "h",
            Context = "trigger S.T",
            Schema = "S",
            Table = "T",
            Column = "C",
            DataType = "INTEGER",
            Constraint = "K",
        };
        var failure = new Failure
        {
            SqlCode = -103,
            SqlState = "2A000",
            Message = "table ANN.NOPE is not defined",
            Rule = "ISO/IEC 9075:1992 §6.3 SR1",
            Position = 14,
            Active = true,
            Diagnostics = diagnostics,
        };
        byte[] body = Wire.Written(failure.Write);

        Assert.Equal(Wire.Hex("99FFFFFF 05000000 3241303030"), body[..13]);
        Assert.Equal(failure, Assert.IsType<Reply.Error>(Wire.Decode('E', body)).Failure);
        Failure plain = Assert.IsType<Reply.Error>(Wire.Decode('E', Wire.Written((failure with { Rule = null, Position = null, Diagnostics = null }).Write))).Failure;
        Assert.Equal((null, null, null), (plain.Rule, plain.Position, plain.Diagnostics));
    }

    /// <summary>Prepared: statement, parameters in their order, columns; Description: columns.</summary>
    [Fact]
    [Verifies("[PROTOCOL Prepared 'P']")]
    [Verifies("[PROTOCOL Description 'D']")]
    [Verifies("[PROTOCOL Composite fields]")]
    public void PreparedAndDescription()
    {
        byte[] parameter = Wire.Written(new Parameter(true, new CharacterType(uint.MaxValue, CharacterUnits.Characters, true)).Write);
        byte[] id = Wire.Written(new Column("ID", new ExactType(9, 0), true).Write);
        byte[] unnamed = Wire.Written(new Column(null, new BooleanType(), false).Write);

        Assert.Equal(Wire.Hex("01 02000000 4944 02 09000000 00000000 01"), id);
        Reply.Prepared prepared = Assert.IsType<Reply.Prepared>(Wire.Decode('P', Wire.Join(Wire.Hex("07000000 01000000"), parameter, Wire.Hex("02000000"), id, unnamed)));
        Assert.Equal((7u, true), (prepared.Statement, Assert.Single(prepared.Parameters).Varying));
        Assert.Equal(["ID", null], prepared.Columns.Select(c => c.Name));
        Reply.Description description = Assert.IsType<Reply.Description>(Wire.Decode('D', Wire.Join(Wire.Hex("01000000"), unnamed)));
        Assert.Equal(new BooleanType(), Assert.Single(description.Columns).Type);
    }

    /// <summary>Challenge, Ready, Suspended, Status, Pong and the Lob replies.</summary>
    [Fact]
    [Verifies("[PROTOCOL Challenge 'C']")]
    [Verifies("[PROTOCOL Ready 'R']")]
    [Verifies("[PROTOCOL Suspended 'S']")]
    [Verifies("[PROTOCOL Status 'T']")]
    [Verifies("[PROTOCOL Pong 'G']")]
    [Verifies("[PROTOCOL Lob 'L'] the reply")]
    public void TheOtherReplies()
    {
        byte[] key = [.. Enumerable.Repeat((byte)0xAA, 16)];
        byte[] ready = Wire.Join(Wire.Str("ANN"), key, Wire.Hex("01000000"), Wire.Str("ping"), Wire.Str("turborel 0.0.0 (alpha)"));

        Assert.Equal(Wire.Hex("0102"), Assert.IsType<Reply.Challenge>(Wire.Decode('C', Wire.Hex("02000000 0102"))).Bytes.ToArray());
        Reply.Ready r = Assert.IsType<Reply.Ready>(Wire.Decode('R', ready));
        Assert.Equal(("ANN", "turborel 0.0.0 (alpha)"), (r.Authorization, r.Server));
        Assert.Equal(key, r.Key.ToArray());
        Assert.Equal(["ping"], r.Capabilities);
        Assert.DoesNotContain("AA", r.ToString(), StringComparison.Ordinal);
        Assert.IsType<Reply.Suspended>(Wire.Decode('S', []));
        Assert.Equal(new Reply.Status("READ_ONLY", string.Empty), Wire.Decode('T', Wire.Join(Wire.Str("READ_ONLY"), Wire.Str(string.Empty))));
        Assert.IsType<Reply.Pong>(Wire.DecodeReply(Wire.Hex("47 00000000")));
        Assert.Equal(Wire.Hex("C3A9"), Assert.IsType<Reply.LobData>(Wire.Decode('L', Wire.Hex("00 02000000 C3A9"))).Bytes.ToArray());
        Assert.Equal(new Reply.LobLocator(9), Wire.Decode('L', Wire.Hex("01 09000000")));
        Wire.Fails(() => Wire.Decode('L', Wire.Hex("02 09000000")), Sections.Replies);
    }

    /// <summary>A Notice: SQLSTATE, message, and its tail of diagnostics.</summary>
    [Fact]
    [Verifies("[PROTOCOL Notice 'N']")]
    [Verifies("[PROTOCOL The tail]")]
    public void NoticeCarriesItsDiagnostics()
    {
        byte[] body = Wire.Join(Wire.Str("01T01"), Wire.Str("rolled back"), Wire.Hex("07 06000000 4E4F54494345"));

        Assert.Equal(new Reply.Notice("01T01", "rolled back", new Diagnostics { Severity = "NOTICE" }), Wire.Decode('N', body));
        Assert.Null(Assert.IsType<Reply.Notice>(Wire.Decode('N', Wire.Join(Wire.Str("01000"), Wire.Str("w")))).Diagnostics);
    }

    /// <summary>A kind no reply has, a body cut short, or bytes left over fail the connection.</summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    public void MalformedRepliesFail()
    {
        Wire.Fails(() => Wire.Decode('Q', []), Sections.Frames);
        Wire.Fails(() => Wire.Decode('K', Wire.Hex("00000000 05000000 3030")), Sections.Frames);
        Wire.Fails(() => Wire.Decode('G', Wire.Hex("00")), Sections.Frames);
        Wire.Fails(() => Wire.Decode('B', Wire.Hex("01000000")), Sections.Frames);
    }

    /// <summary>A kind is read in the direction it travels: C from the server is Challenge, not Close.</summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    public void KindIsReadInItsDirection()
    {
        Assert.Equal(RequestKinds.Close, ReplyKinds.Challenge);
        Assert.IsType<Reply.Challenge>(Wire.Decode('C', Wire.Hex("00000000")));
    }
}
