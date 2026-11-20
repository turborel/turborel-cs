// A connection's control of its session against a server of the test's
// own: ping, reset, a change of user, several statements in one request,
// the attributes each statement carries, what a completion's tail tells,
// and notices kept or handed to a handler [CLIENT Controlling the
// session] [CLIENT What the server tells] [PROTOCOL Complete 'K'].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of the session control of <see cref="NativeConnection"/>.</summary>
public sealed class ControlTests
{
    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    private static readonly KeyValuePair<string, string>[] Trace = [new("traceparent", "00-1-2-01")];

    /// <summary>
    /// Ping is answered by Pong; Reset by its notice and Statuses, then a
    /// Complete saying no transaction is active; ChangeUser by the steps of
    /// a login, under a new key, no transaction active after it.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Ping 'G']")]
    [Verifies("[PROTOCOL Reset 'W']")]
    [Verifies("[PROTOCOL ChangeUser 'U']")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task PingResetAndChangeUser()
    {
        byte[] key = [.. Enumerable.Repeat((byte)9, WireWriter.KeyBytes)];
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Ping, Capabilities.Reset, Capabilities.ChangeUser);
            _ = await peer.ExpectAsync(RequestKinds.Ping);
            await peer.WriteAsync('G', []);
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete(active: true));
            _ = await peer.ExpectAsync(RequestKinds.Reset);
            await peer.WriteAsync('N', Replies.Notice("01T01", "the transaction was rolled back"));
            await peer.WriteAsync('T', Wire.Join(Wire.Str("SESSION.TIME_ZONE"), Wire.Str("+00:00")));
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete(0, "01003", active: true));
            Assert.Equal(Wire.Encode(new Request.ChangeUser("bob", "echo", Encoding.ASCII.GetBytes("hi")))[5..], await peer.ExpectAsync(RequestKinds.ChangeUser));
            await peer.WriteAsync('C', Wire.Written(w => w.WriteBytes("go"u8)));
            Assert.Equal("GO", Encoding.ASCII.GetString((await peer.ExpectAsync(RequestKinds.Answer))[4..]));
            await peer.WriteAsync('N', Replies.Notice("01T01", "the transaction was rolled back"));
            await peer.ReadyAsync(key);
            _ = await peer.ExpectAsync(RequestKinds.ChangeUser);
            await peer.WriteAsync('E', Replies.Error(-919, "28000"));
        });
        await using NativeConnection c = await ConnectAsync(server);

        await c.PingAsync(TestContext.Current.CancellationToken);
        _ = await c.QueryAsync("INSERT", TestContext.Current.CancellationToken);
        Assert.True(c.InTransaction);
        await c.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Equal((false, "+00:00"), (c.InTransaction, c.Setting("SESSION.TIME_ZONE")));
        _ = await c.QueryAsync("INSERT", TestContext.Current.CancellationToken);
        Assert.Equal("01003", c.Warning);
        await c.ChangeUserAsync("bob", new Echo(), TestContext.Current.CancellationToken);
        Assert.Equal((false, null), (c.InTransaction, c.Warning));
        Assert.Equal(["01T01", "01T01"], c.TakeNotices().Select(n => n.SqlState));
        Assert.Equal(key, c.Key.ToArray());
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => c.ChangeUserAsync("ann", new TrustMechanism(), TestContext.Current.CancellationToken));
        Assert.Equal("28000", refused.SqlState);
        Assert.True(c.IsLost);
        await server.PlayedAsync();
    }

    /// <summary>
    /// Several statements in one request give each outcome in turn, by the
    /// Complete that says more follow, until one fails; attributes, once
    /// granted, ride each Query and Execute, none before.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task SeveralStatementsAndAttributes()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.MultiStatement, Capabilities.QueryAttributes);
            Assert.Equal(Wire.Encode(new Request.Query("A; B; C", false, Trace, Multi: true))[5..], await peer.ExpectAsync(RequestKinds.Query));
            await peer.WriteAsync('K', Replies.Complete(tail: w => w.WriteU8(TailTags.More)));
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('B', Replies.Rows(1));
            await peer.WriteAsync('K', Replies.Complete(tail: w => w.WriteU8(TailTags.More)));
            await peer.WriteAsync('E', Replies.Error(-104, "42000"));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.SetQueryAttributes(Trace);

        Ran ran = await c.QueryManyAsync("A; B; C", TestContext.Current.CancellationToken);
        Assert.Equal(2, ran.Outcomes.Count);
        Assert.IsType<RowsOutcome>(ran.Outcomes[1]);
        Assert.Equal("42000", ran.Failure?.SqlState);
        Assert.Null((await c.QueryManyAsync("D", TestContext.Current.CancellationToken)).Failure);
        await server.PlayedAsync();
    }

    /// <summary>
    /// A completion's tail tells the commit's log position, the rows, the
    /// identity values generated, the conditions and the server's time;
    /// conditions taken are told once; a warning is kept until the next
    /// statement.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Complete 'K']")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task CompletionTailIsTold()
    {
        var condition = new Failure { SqlCode = 0, SqlState = "01003", Message = "null value eliminated" };
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete(0, "01003", tail: w =>
            {
                w.WriteU8(TailTags.Position);
                w.WriteU64(1146);
                w.WriteU8(TailTags.Rows);
                w.WriteU64(2);
                w.WriteU8(TailTags.Generated);
                w.WriteStrings(["7", "8"]);
                w.WriteU8(TailTags.Conditions);
                w.WriteU32(1);
                w.WriteBytes(Wire.Written(condition.Write));
                w.WriteU8(TailTags.Time);
                w.WriteU64(30);
                w.WriteU64(5);
                w.WriteU64(10);
            }));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        _ = await c.QueryAsync("INSERT", TestContext.Current.CancellationToken);
        Assert.Equal((1146UL, 2UL, "01003"), (c.CommitPosition, c.RowsAffected, c.Warning));
        Assert.Equal(["7", "8"], c.LastGenerated);
        Assert.Equal(new Timing(30, 5, 10), c.ServerTime);
        Assert.Equal("01003", Assert.Single(c.TakeConditions()).SqlState);
        Assert.Empty(c.TakeConditions());
        _ = await c.QueryAsync("COMMIT", TestContext.Current.CancellationToken);
        Assert.Equal((1146UL, null, 0), (c.CommitPosition, c.Warning, c.LastGenerated.Count));
        await server.PlayedAsync();
    }

    /// <summary>With no handler the last 64 notices are kept, the oldest dropped; a handler takes each as it comes.</summary>
    [Fact]
    [Verifies("[CLIENT What the server tells]")]
    [Verifies("[PROTOCOL Errors and notices]")]
    public async Task NoticesAreKeptOrHanded()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Query);
            foreach (int i in Enumerable.Range(0, 65))
            {
                await peer.WriteAsync('N', Replies.Notice("01000", $"n{i}"));
            }

            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('N', Replies.Notice("01000", "heard"));
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        _ = await c.QueryAsync("A", TestContext.Current.CancellationToken);
        IReadOnlyList<TurborelNotice> kept = c.TakeNotices();
        Assert.Equal((64, "n1", "n64"), (kept.Count, kept[0].Message, kept[^1].Message));
        var heard = new List<string>();
        c.NoticeHandler = notice => heard.Add(notice.Message);
        Assert.NotNull(c.NoticeHandler);
        _ = await c.QueryAsync("B", TestContext.Current.CancellationToken);
        Assert.Equal(["heard"], heard);
        Assert.Empty(c.TakeNotices());
        await server.PlayedAsync();
    }

    /// <summary>A notice carries the diagnostics its Notice told.</summary>
    [Fact]
    [Verifies("[PROTOCOL Notice 'N']")]
    public void NoticeCarriesItsDiagnostics()
    {
        var diagnostics = new Diagnostics { Severity = "WARNING", Detail = "d", Hint = "h", Context = "c", Schema = "S", Table = "T", Column = "C", DataType = "D", Constraint = "K" };

        var notice = TurborelNotice.Of(new Reply.Notice("01003", "m", diagnostics));
        Assert.Equal(("01003", "m", "WARNING", "d", "h", "c"), (notice.SqlState, notice.Message, notice.Severity, notice.Detail, notice.Hint, notice.Context));
        Assert.Equal(("S", "T", "C", "D", "K"), (notice.SchemaName, notice.TableName, notice.ColumnName, notice.DataTypeName, notice.ConstraintName));
        Assert.Null(TurborelNotice.Of(new Reply.Notice("01000", "m", null)).Severity);
    }

    private static Task<NativeConnection> ConnectAsync(FakeServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick);

    // A mechanism that answers each challenge by its letters in upper case.
    private sealed class Echo : Mechanism
    {
        public override string Name => "echo";

        public override byte[] Initial() => "hi"u8.ToArray();

        public override byte[] Answer(ReadOnlySpan<byte> challenge) => Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(challenge).ToUpperInvariant());
    }
}
