// Prepared statements against a real turboreld (RULES.md R-TEST-8):
// prepared once, executed with values as often as wanted; a query's
// cursor opened, fetched a batch at a time and closed, several of one
// statement at once; described again; destroyed; and a CALL's output
// parameters [CLIENT Prepared statements] [CLIENT Controlling the
// session].

using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of prepared statements and cursors.</summary>
/// <param name="server">The live server.</param>
public sealed class PreparedLiveTests(LiveServer server)
{
    private static readonly object?[] None = [];

    /// <summary>
    /// A statement prepared once, its parameters' types and its columns
    /// described, executes with values as many times as wanted, across
    /// transactions; deallocated, it is no more.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Prepare 'P']")]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL Deallocate 'D']")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task PreparedOnceExecutedOften()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("PREPARED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, S VARCHAR(8))");

        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?)", TestContext.Current.CancellationToken);
        Assert.Equal([new ExactType(10, 0), new CharacterType(8, CharacterUnits.Characters, true)], insert.Parameters.Select(p => p.Type));
        Assert.Empty(insert.Columns);
        foreach (int n in Enumerable.Range(1, 3))
        {
            Completed done = Assert.IsType<DoneOutcome>(await c.ExecuteAsync(insert, [new TurborelDecimal(n, 0), $"s{n}"], TestContext.Current.CancellationToken)).Completion;
            Assert.Equal(1UL, done.RowCount);
            _ = await c.DoneAsync("COMMIT");
        }

        PreparedStatement select = await c.PrepareAsync($"SELECT S FROM {t} WHERE N >= ? ORDER BY N", TestContext.Current.CancellationToken);
        Assert.Equal("S", Assert.Single(select.Columns).Name);
        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.ExecuteAsync(select, [new TurborelDecimal(2, 0)], TestContext.Current.CancellationToken));
        Assert.Equal([["s2"], ["s3"]], rows.Rows.Select(Live.Texts));
        TurborelException count = await Assert.ThrowsAsync<TurborelException>(() => c.ExecuteAsync(insert, [null], TestContext.Current.CancellationToken));
        Assert.Equal("07001", count.SqlState);
        await c.DeallocateAsync(insert, TestContext.Current.CancellationToken);
        TurborelException gone = await Assert.ThrowsAsync<TurborelException>(() => c.DeallocateAsync(insert, TestContext.Current.CancellationToken));
        Assert.Equal("26000", gone.SqlState);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A cursor opened gives its first rows and says it has more; a fetch the
    /// next batch, Complete with the last, the cursor closed; one not open
    /// fails, as one opened twice does, SQLSTATE 24000.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL Fetch 'F']")]
    [Verifies("[PROTOCOL Close 'C']")]
    [Verifies("[PROTOCOL Prepare 'P']")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task CursorsAreReadABatchAtATime()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        PreparedStatement select = await c.PrepareAsync("VALUES (1), (2), (3), (4), (5)", TestContext.Current.CancellationToken);

        Batch first = await c.OpenCursorAsync(select, None, 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((2, true), (first.Rows.Count, first.More));
        Assert.Equal("24000", (await Assert.ThrowsAsync<TurborelException>(() => c.OpenCursorAsync(select, None, 1, cancellationToken: TestContext.Current.CancellationToken))).SqlState);
        await c.SendAsync(new Request.Prepare(select.Number, "VALUES 2"), TestContext.Current.CancellationToken);
        Assert.Equal("24000", Assert.IsType<Reply.Error>(await c.ReceiveAsync(TestContext.Current.CancellationToken)).Failure.SqlState);
        Batch second = await c.FetchAsync(select, 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((2, true), (second.Rows.Count, second.More));
        Batch last = await c.FetchAsync(select, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal((1, false), (last.Rows.Count, last.More));
        Assert.Equal("24000", (await Assert.ThrowsAsync<TurborelException>(() => c.FetchAsync(select, 1, cancellationToken: TestContext.Current.CancellationToken))).SqlState);
        Assert.Equal("24000", (await Assert.ThrowsAsync<TurborelException>(() => c.CloseCursorAsync(select, cancellationToken: TestContext.Current.CancellationToken))).SqlState);
        _ = await c.OpenCursorAsync(select, None, 1, cancellationToken: TestContext.Current.CancellationToken);
        await c.CloseCursorAsync(select, cancellationToken: TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// Several numbered cursors of one statement are open at once; a statement
    /// is described again, and an open cursor's columns; a cursor left open
    /// keeps its statement from being deallocated.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL Describe 'Y']")]
    [Verifies("[PROTOCOL Deallocate 'D']")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task NumberedCursorsAndDescriptions()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        PreparedStatement select = await c.PrepareAsync("VALUES (1, 'a'), (2, 'b'), (3, 'c')", TestContext.Current.CancellationToken);

        Batch one = await c.OpenCursorAsync(select, None, 1, cursor: 1, cancellationToken: TestContext.Current.CancellationToken);
        Batch two = await c.OpenCursorAsync(select, None, 2, cursor: 2, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(("1", "2"), (Live.Texts(one.Rows[0])[0], Live.Texts(two.Rows[1])[0]));
        Assert.Equal("2", Live.Texts((await c.FetchAsync(select, 1, cursor: 1, cancellationToken: TestContext.Current.CancellationToken)).Rows[0])[0]);
        Assert.Equal(2, (await c.DescribeCursorAsync(select, cursor: 2, cancellationToken: TestContext.Current.CancellationToken)).Count);
        PreparedStatement again = await c.DescribeAsync(select, TestContext.Current.CancellationToken);
        Assert.Equal(select.Columns, again.Columns);
        Assert.Equal("24000", (await Assert.ThrowsAsync<TurborelException>(() => c.DeallocateAsync(select, TestContext.Current.CancellationToken))).SqlState);
        await c.CloseCursorAsync(select, cursor: 1, cancellationToken: TestContext.Current.CancellationToken);
        await c.CloseCursorAsync(select, cursor: 2, cancellationToken: TestContext.Current.CancellationToken);
        await c.DeallocateAsync(select, TestContext.Current.CancellationToken);
        Assert.Equal("26000", (await Assert.ThrowsAsync<TurborelException>(() => c.DescribeAsync(select, TestContext.Current.CancellationToken))).SqlState);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// Under autocommit with <c>fetch-ends</c>, a cursor's transaction ends as
    /// it does: the last row's fetch commits, close commits.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task FetchEndsTheCursorsTransaction()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        c.Autocommit = true;
        PreparedStatement select = await c.PrepareAsync("VALUES (1), (2), (3)", TestContext.Current.CancellationToken);

        Batch first = await c.OpenCursorAsync(select, None, 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.More);
        Assert.True(c.InTransaction);
        Batch rest = await c.FetchAsync(select, 0, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rest.More);
        Assert.False(c.InTransaction);
        _ = await c.OpenCursorAsync(select, None, 1, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(c.InTransaction);
        await c.CloseCursorAsync(select, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(c.InTransaction);
    }

    /// <summary>
    /// A CALL's parameters are its inputs alone, its columns one for each
    /// <c>?</c> of an OUT or INOUT parameter; executed, it gives one row of
    /// their values.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Prepared 'P']")]
    [Verifies("[PROTOCOL Execute 'E']")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task CallGivesItsOutputParameters()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string p = Live.Table("DOUBLED");
        _ = await c.DoneAsync($"CREATE PROCEDURE {p} (IN A INTEGER, OUT B INTEGER, INOUT C INTEGER) READS SQL DATA SELECT A + 1, C * 2 INTO B, C FROM (VALUES 1) AS ONE (X)");

        PreparedStatement call = await c.PrepareAsync($"CALL {p}(?, ?, ?)", TestContext.Current.CancellationToken);
        Assert.Equal(2, call.Parameters.Count);
        Assert.Equal(["B", "C"], call.Columns.Select(column => column.Name));
        RowsOutcome outputs = Assert.IsType<RowsOutcome>(await c.ExecuteAsync(call, [new TurborelDecimal(5, 0), new TurborelDecimal(7, 0)], TestContext.Current.CancellationToken));
        Assert.Equal([["6", "14"]], outputs.Rows.Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }
}
