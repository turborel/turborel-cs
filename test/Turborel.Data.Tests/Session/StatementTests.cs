// A connection's statements against a server of the test's own, byte for
// byte: a query's replies read in their order, the Notices and Statuses
// before its completion taken, rows streamed, a prepared statement's
// requests, and any reply out of place losing the connection
// [PROTOCOL Query 'Q'] [PROTOCOL Conversations] [CLIENT Running
// statements] [CLIENT Prepared statements].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of the statement methods of <see cref="NativeConnection"/>.</summary>
public sealed class StatementTests
{
    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    /// <summary>
    /// A query's Description, Rows and Complete, the Notice and Status
    /// before its completion taken, give its rows; another statement's
    /// completion; a failure not of the connection leaves it working.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[CLIENT Running statements]")]
    public async Task QueryRepliesAreReadInTheirOrder()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            byte[] query = await peer.ExpectAsync(RequestKinds.Query);
            Assert.Equal(Wire.Encode(new Request.Query("VALUES 1", Ends: true, []))[5..], query);
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('B', Replies.Rows(1, 2));
            await peer.WriteAsync('B', Replies.Rows(3));
            await peer.WriteAsync('N', Replies.Notice("01003", "null value eliminated"));
            await peer.WriteAsync('T', Wire.Join(Wire.Str("SCHEMA"), Wire.Str("S")));
            await peer.WriteAsync('K', Replies.Complete(tail: w => { w.WriteU8(TailTags.Rows); w.WriteU64(3); }));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete(100, "02000", active: true));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-920, "TBR01", active: true));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('S', []);
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;

        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.QueryAsync("VALUES 1", TestContext.Current.CancellationToken));
        Assert.Equal(Replies.Integer, Assert.Single(rows.Columns));
        Assert.Equal(["1", "2", "3"], rows.Rows.Select(row => row[0]?.ToString()));
        Assert.Equal((3UL, "S", false), (c.RowsAffected, c.Setting("SCHEMA"), c.InTransaction));
        Assert.Equal("01003", Assert.Single(c.TakeNotices()).SqlState);
        Completed none = Assert.IsType<DoneOutcome>(await c.QueryAsync("UPDATE T SET N = 0", TestContext.Current.CancellationToken)).Completion;
        Assert.Equal((100, "02000", true, false), (none.SqlCode, none.SqlState, c.InTransaction, none.IsWarning));
        Assert.Equal("Completed(SQLCODE 100, SQLSTATE 02000)", none.ToString());
        TurborelException replica = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Equal((-920, "TBR01", false), (replica.SqlCode, replica.SqlState, c.IsLost));
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT 2", TestContext.Current.CancellationToken));
        Assert.Contains("no result", lost.Message, StringComparison.Ordinal);
        Assert.True(c.IsLost);
        await server.PlayedAsync();
    }

    /// <summary>
    /// A reply holding a value the client cannot take, in a frame read whole,
    /// fails its statement with the value's error, SQLCODE -916, the rest of
    /// the statement's replies read past first, so the next statement takes
    /// its own; the connection goes on. A Complete whose condition cannot be
    /// read has its head noted; a Describe of a cursor, answered by one
    /// reply, reads none past it; a malformed frame loses the connection.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values and fields]")]
    [Verifies("[CLIENT Running statements]")]
    public async Task AValueThatCannotBeTakenFailsItsStatementAlone()
    {
        byte[] badTruth = Wire.Hex("01000000 01000000 0E 02");
        byte[] badCondition = Wire.Hex("00000000 05000000 3030303030 01 05 11 01000000 03000000 010203");
        byte[] badUnits = Replies.Description(new Column("B", new CharacterType(10, CharacterUnits.Characters, false), false));
        badUnits[badUnits.AsSpan().IndexOf(Wire.Hex("01 0A000000")) + 5] = 2;
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Describe, Capabilities.MultiStatement);
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('D', Replies.Description(new Column("B", new BooleanType(), false)));
            await peer.WriteAsync('B', badTruth);
            await peer.WriteAsync('B', badTruth);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.CompleteRows(2));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', badCondition);
            await peer.WriteAsync('K', Replies.Complete(active: true));
            _ = await peer.ExpectAsync(RequestKinds.Describe);
            await peer.WriteAsync('D', badUnits);
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Wire.Hex("00"));
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;

        TurborelException value = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT B FROM T", TestContext.Current.CancellationToken));
        Assert.Equal((-916, "TB916", false), (value.SqlCode, value.SqlState, c.IsLost));
        Assert.Equal(2UL, Assert.IsType<DoneOutcome>(await c.QueryAsync("UPDATE T SET B = TRUE", TestContext.Current.CancellationToken)).Completion.RowCount);
        Ran ran = await c.QueryManyAsync("SET SCHEMA S; UPDATE T SET B = FALSE", TestContext.Current.CancellationToken);
        Assert.Equal(("TB916", true, false), (ran.Failure?.SqlState, c.InTransaction, c.IsLost));
        PreparedStatement select = new(0, [], []);
        TurborelException described = await Assert.ThrowsAsync<TurborelException>(() => c.DescribeCursorAsync(select, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("TB916", false), (described.SqlState, c.IsLost));
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("COMMIT", TestContext.Current.CancellationToken));
        Assert.Equal(("08006", true), (lost.SqlState, c.IsLost));
        await server.PlayedAsync();
    }

    /// <summary>
    /// Under ends, a commit that fails sends its Error in place of the
    /// Complete, no transaction active after it; a failing statement's
    /// conditions come as Notices before its Error, conditions granted or not.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Errors and notices]")]
    public async Task AFailingCommitAndConditionsOfAFailure()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Notices, Capabilities.Conditions);
            Assert.Equal(Wire.Encode(new Request.Query("UPDATE T SET N = 1", Ends: true, []))[5..], await peer.ExpectAsync(RequestKinds.Query));
            await peer.WriteAsync('E', Replies.Error(-913, "40002"));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('N', Replies.Notice("01003", "null value eliminated"));
            await peer.WriteAsync('E', Replies.Error(-301, "22012"));
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;

        TurborelException commit = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("UPDATE T SET N = 1", TestContext.Current.CancellationToken));
        Assert.Equal(("40002", false, false), (commit.SqlState, c.InTransaction, c.IsLost));
        TurborelException failed = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT SUM(N) / 0 FROM T", TestContext.Current.CancellationToken));
        Assert.Equal("22012", failed.SqlState);
        Assert.Equal("01003", Assert.Single(c.TakeNotices()).SqlState);
        Assert.Empty(c.TakeConditions());
        await server.PlayedAsync();
    }

    /// <summary>
    /// A reply's body is bounded by MaxBody, 16 MiB unless the program says
    /// another: one longer than the bound set loses the connection; one longer
    /// than .NET's longest array is refused as it is set, TB910; the port is
    /// 7422 unless told another.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Frames]")]
    [Verifies("[PROTOCOL The Native Protocol]")]
    public async Task RepliesAreBoundedByMaxBody()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            for (int i = 0; i < 2; i++)
            {
                _ = await peer.ExpectAsync(RequestKinds.Query);
                await peer.WriteAsync('K', Replies.Complete());
            }
        });
        await using NativeConnection c = await ConnectAsync(server);
        Assert.Equal(FrameHeader.MaxBody, c.MaxBody);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => c.MaxBody = uint.MaxValue).SqlState);
        c.MaxBody = 64;
        _ = await c.QueryAsync("COMMIT", TestContext.Current.CancellationToken);
        c.MaxBody = 4;
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("COMMIT", TestContext.Current.CancellationToken));
        Assert.Equal(("08006", true), (lost.SqlState, c.IsLost));
        Assert.Equal((7422, 7422), (new TcpTarget("db").Port, Turborel.Data.Configuration.ServerHost.DefaultPort));
    }

    /// <summary>
    /// A Query's rows end at a Suspended as at a Complete, as
    /// turborel-client's query reads them; of several statements, a
    /// Suspended is out of place and loses the connection.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Suspended 'S']")]
    public async Task AQuerysRowsEndAtSuspended()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.MultiStatement);
            for (int i = 0; i < 2; i++)
            {
                _ = await peer.ExpectAsync(RequestKinds.Query);
                await peer.WriteAsync('D', Replies.Description(Replies.Integer));
                await peer.WriteAsync('B', Replies.Rows(1, 2));
                await peer.WriteAsync('S', []);
            }
        });
        await using NativeConnection c = await ConnectAsync(server);

        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.QueryAsync("VALUES 1", TestContext.Current.CancellationToken));
        Assert.Equal(2, rows.Rows.Count);
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => c.QueryManyAsync("VALUES 1; VALUES 2", TestContext.Current.CancellationToken));
        Assert.Equal(("08006", true), (lost.SqlState, c.IsLost));
        await server.PlayedAsync();
    }

    /// <summary>
    /// Rows go to a sink a batch at a time; a statement not a query gives its
    /// completion; a failure among the rows is the statement's; a reply out
    /// of place loses the connection.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    public async Task RowsStreamToASink()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('B', Replies.Rows(1, 2));
            await peer.WriteAsync('B', Replies.Rows(3));
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-104, "42000"));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('E', Replies.Error(-301, "22012"));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('S', []);
        });
        await using NativeConnection c = await ConnectAsync(server);
        var batches = new List<int>();
        var sink = new Sink(batches);

        Assert.Equal(new Streamed(3, null), await c.QueryIntoAsync("VALUES 1", sink, TestContext.Current.CancellationToken));
        Assert.Equal([2, 1], batches);
        Assert.Equal(0, (await c.QueryIntoAsync("COMMIT", sink, TestContext.Current.CancellationToken)).Completion?.SqlCode);
        Assert.Equal("42000", (await Assert.ThrowsAsync<TurborelException>(() => c.QueryIntoAsync("BAD", sink, TestContext.Current.CancellationToken))).SqlState);
        Assert.Equal("22012", (await Assert.ThrowsAsync<TurborelException>(() => c.QueryIntoAsync("VALUES 1/0", sink, TestContext.Current.CancellationToken))).SqlState);
        Assert.Contains("broke off", (await Assert.ThrowsAsync<TurborelException>(() => c.QueryIntoAsync("VALUES 1", sink, TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        await server.PlayedAsync();
    }

    /// <summary>
    /// Prepare numbers its statements; Execute carries values, the cursor
    /// and, under autocommit with <c>fetch-ends</c>, ends; Fetch, Close,
    /// Describe and Deallocate name the statement and cursor.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Prepare 'P']")]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL Fetch 'F']")]
    [Verifies("[PROTOCOL Close 'C']")]
    [Verifies("[PROTOCOL Describe 'Y']")]
    [Verifies("[PROTOCOL Deallocate 'D']")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task PreparedStatementsNameTheirStatementAndCursor()
    {
        Parameter[] one = [new(false, new ExactType(10, 0))];
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Cursors, Capabilities.Describe, Capabilities.FetchEnds);
            Assert.Equal(Wire.Encode(new Request.Prepare(0, "INSERT"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
            await peer.WriteAsync('P', Replies.Prepared(0, one));
            Assert.Equal(Wire.Encode(new Request.Prepare(1, "SELECT"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
            await peer.WriteAsync('P', Replies.Prepared(1, [], Replies.Integer));
            Assert.Equal(Wire.Encode(new Request.Execute(new(0), [new TurborelDecimal(4, 0)], 0, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
            await peer.WriteAsync('K', Replies.Complete(tail: w => { w.WriteU8(TailTags.Rows); w.WriteU64(1); }));
            Assert.Equal(Wire.Encode(new Request.Execute(new(1), [], 0, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
            await peer.WriteAsync('B', Replies.Rows(7));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Execute(new(1, 2), [], 1, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
            await peer.WriteAsync('B', Replies.Rows(7));
            await peer.WriteAsync('S', []);
            Assert.Equal(Wire.Encode(new Request.Fetch(new(1, 2), 0))[5..], await peer.ExpectAsync(RequestKinds.Fetch));
            await peer.WriteAsync('B', Replies.Rows(8));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Describe(new(1, 2), true))[5..], await peer.ExpectAsync(RequestKinds.Describe));
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            Assert.Equal(Wire.Encode(new Request.Describe(new(1), false))[5..], await peer.ExpectAsync(RequestKinds.Describe));
            await peer.WriteAsync('P', Replies.Prepared(1, [], Replies.Integer));
            Assert.Equal(Wire.Encode(new Request.Close(new(1, 2)))[5..], await peer.ExpectAsync(RequestKinds.Close));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Deallocate(1))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
            await peer.WriteAsync('E', Replies.Error(-514, "24000", active: true));
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;

        PreparedStatement insert = await c.PrepareAsync("INSERT", TestContext.Current.CancellationToken);
        PreparedStatement select = await c.PrepareAsync("SELECT", TestContext.Current.CancellationToken);
        Assert.Equal("PreparedStatement(0, 1 parameters, 0 columns)", insert.ToString());
        Assert.Equal(one, insert.Parameters);
        Assert.Equal(1UL, Assert.IsType<DoneOutcome>(await c.ExecuteAsync(insert, [new TurborelDecimal(4, 0)], TestContext.Current.CancellationToken)).Completion.RowCount);
        Assert.Single(Assert.IsType<RowsOutcome>(await c.ExecuteAsync(select, [], TestContext.Current.CancellationToken)).Rows);
        Assert.Equal(new Batch([[new TurborelDecimal(7, 0)]], true).More, (await c.OpenCursorAsync(select, [], 1, cursor: 2, cancellationToken: TestContext.Current.CancellationToken)).More);
        Assert.False((await c.FetchAsync(select, 0, cursor: 2, cancellationToken: TestContext.Current.CancellationToken)).More);
        Assert.Equal(Replies.Integer, Assert.Single(await c.DescribeCursorAsync(select, cursor: 2, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(select.Columns, (await c.DescribeAsync(select, TestContext.Current.CancellationToken)).Columns);
        await c.CloseCursorAsync(select, cursor: 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("24000", (await Assert.ThrowsAsync<TurborelException>(() => c.DeallocateAsync(select, TestContext.Current.CancellationToken))).SqlState);
        Assert.True(c.InTransaction);
        await server.PlayedAsync();
    }

    /// <summary>A reply a request does not take loses the connection, saying what was wanted.</summary>
    /// <param name="method">The method, by name.</param>
    /// <param name="wanted">What its failure says was wanted.</param>
    [Theory]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[CLIENT Prepared statements]")]
    [InlineData("prepare", "no Prepared")]
    [InlineData("execute", "no completion")]
    [InlineData("rows", "broke off")]
    [InlineData("describe", "no Description")]
    [InlineData("ping", "no Pong")]
    [InlineData("change", "no Ready")]
    [InlineData("many", "no result")]
    public async Task ReplyOutOfPlaceLosesTheConnection(string method, string wanted)
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Describe, Capabilities.Ping, Capabilities.ChangeUser, Capabilities.MultiStatement);
            (byte kind, _) = await peer.ReadAsync();
            await (kind == RequestKinds.Ping ? peer.WriteAsync('K', Replies.Complete()) : peer.WriteAsync('G', []));
        });
        await using NativeConnection c = await ConnectAsync(server);
        var statement = new PreparedStatement(0, [], method == "rows" ? [Replies.Integer] : []);
        Func<Task> act = method switch
        {
            "prepare" => () => c.PrepareAsync("X"),
            "execute" or "rows" => () => c.ExecuteAsync(statement, []),
            "describe" => () => c.DescribeCursorAsync(statement),
            "ping" => () => c.PingAsync(),
            "change" => () => c.ChangeUserAsync("bob", new TrustMechanism()),
            _ => () => c.QueryManyAsync("X"),
        };

        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(act);
        Assert.Contains(wanted, failure.Message, StringComparison.Ordinal);
        Assert.True(c.IsLost);
    }

    /// <summary>A null where a value is needed is the API's misuse, SQLCODE -910, SQLSTATE TB910, before anything is sent.</summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    public async Task NullArgumentIsMisuse()
    {
        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(null!, "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken));
        Assert.Equal((-910, "TB910"), (failure.SqlCode, failure.SqlState));
        Assert.Contains("target is null", failure.Message, StringComparison.Ordinal);
    }

    private static Task<NativeConnection> ConnectAsync(FakeServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick);

    // Counts each batch's rows.
    private sealed class Sink(List<int> batches) : IRowSink
    {
        public void Columns(IReadOnlyList<Column> columns) => Assert.Single(columns);

        public void Rows(IReadOnlyList<IReadOnlyList<object?>> rows) => batches.Add(rows.Count);
    }
}
