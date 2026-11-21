// Direct statements against a real turboreld (RULES.md R-TEST-8): a
// query's rows, another statement's completion, a failure all it says,
// the transaction a statement begins or autocommit ends, every kind of
// value, rows streamed to a sink, and several statements in one request
// [PROTOCOL Query 'Q'] [CLIENT Running statements].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of direct statements.</summary>
/// <param name="server">The live server.</param>
public sealed class StatementLiveTests(LiveServer server)
{
    /// <summary>
    /// A query gives its columns and rows; another statement its completion;
    /// a query or a change of no row completes with SQLCODE 100, SQLSTATE 02000.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[CLIENT Running statements]")]
    public async Task QueryGivesRowsOrACompletion()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("QUERIED");

        Assert.Equal((0, "00000"), ((await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, S VARCHAR(10))")).SqlCode, "00000"));
        Assert.Equal(3UL, (await c.DoneAsync($"INSERT INTO {t} VALUES (1, 'a'), (2, NULL), (3, 'c')")).RowCount);
        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.QueryAsync($"SELECT N, S FROM {t} ORDER BY N", TestContext.Current.CancellationToken));
        Assert.Equal(["N", "S"], rows.Columns.Select(column => column.Name));
        Assert.Equal(new ExactType(10, 0), rows.Columns[0].Type);
        Assert.Equal(new CharacterType(10, CharacterUnits.Characters, Varying: true), rows.Columns[1].Type);
        Assert.Equal([["1", "a"], ["2", null], ["3", "c"]], rows.Rows.Select(Live.Texts));
        Completed none = await c.DoneAsync($"UPDATE {t} SET N = 0 WHERE N > 100");
        Assert.Equal((100, "02000"), (none.SqlCode, none.SqlState));
        Assert.Empty(await c.RowsAsync($"SELECT N FROM {t} WHERE N > 100"));
        await c.SendAsync(new Request.Query($"SELECT N FROM {t} WHERE N > 100", Ends: false, []), TestContext.Current.CancellationToken);
        Assert.IsType<Reply.Description>(await c.ReceiveAsync(TestContext.Current.CancellationToken));
        Completion empty = Assert.IsType<Reply.Complete>(await c.ReceiveAsync(TestContext.Current.CancellationToken)).Completion;
        Assert.Equal((100, "02000"), (empty.SqlCode, empty.SqlState));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A statement that fails before its first batch is sent is answered by
    /// its Error alone, with no Description or Rows; the standard API's
    /// ExecuteReader throws it, a Query's and a prepared cursor's alike,
    /// rather than give a reader of columns.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[DESIGN] D20")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The statement's text is the test's own, of a table name the test makes.")]
    public async Task AFailureBeforeTheFirstBatchIsItsErrorAlone()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        c.Autocommit = true;
        string t = Live.Table("DIVIDED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER)");
        _ = await c.DoneAsync($"INSERT INTO {t} VALUES (1), (2)");
        await c.SendAsync(new Request.Query($"SELECT 1 / (N - 1) FROM {t}", Ends: true, []), TestContext.Current.CancellationToken);
        Reply.Error error = Assert.IsType<Reply.Error>(await c.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("22012", false), (error.Failure.SqlState, c.IsLost));

        await using var connection = new TurborelConnection($"host=127.0.0.1:{LiveServer.Port};user=ann;tls_mode=disable;passfile=/nonexistent;pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var query = new TurborelCommand($"SELECT 1 / (N - 1) FROM {t}", connection);
        Assert.Equal("22012", (await Assert.ThrowsAsync<TurborelException>(() => query.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))).SqlState);
        using var prepared = new TurborelCommand($"SELECT 1 / (N - ?) FROM {t}", connection);
        _ = prepared.Parameters.AddWithValue("n", 1);
        Assert.Equal("22012", (await Assert.ThrowsAsync<TurborelException>(() => prepared.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))).SqlState);
        Assert.Null(connection.Reader);
        using var after = new TurborelCommand($"DELETE FROM {t} WHERE N = 1", connection);
        Assert.Equal(1, await after.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A failure gives its SQLCODE, SQLSTATE, message, the rule it cites apart, its position and its diagnostics.</summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    [Verifies("[CLIENT What the server tells]")]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[PROTOCOL Error 'E']")]
    public async Task FailureSaysAllTheServerSaid()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("CHECKED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (S VARCHAR(10) CONSTRAINT {t}_OK CHECK (S <> 'bad'))");

        TurborelException missing = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT * FROM NOWHERE_AT_ALL", TestContext.Current.CancellationToken));
        Assert.Equal((-103, "2A000", 14L, "ERROR"), (missing.SqlCode, missing.SqlState, missing.Position, missing.Severity));
        Assert.Equal("X3.135-1986 §5.4 SR5", missing.Rule);
        TurborelException broken = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync($"INSERT INTO {t} VALUES ('bad')", TestContext.Current.CancellationToken));
        Assert.Equal((-201, "23000"), (broken.SqlCode, broken.SqlState));
        Assert.Equal((t, $"{t}_OK", "ANN"), (broken.TableName, broken.ConstraintName, broken.SchemaName));
        Assert.True(broken.TransactionActive);
        Assert.False(c.IsLost);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// Without autocommit a transaction begins with the first statement and
    /// lasts until COMMIT or ROLLBACK; with it, each statement is its own,
    /// committed as it succeeds, rolled back as it fails, no COMMIT sent.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[CLIENT Running statements]")]
    public async Task AutocommitEndsEachStatementsTransaction()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("COMMITTED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER PRIMARY KEY)");
        Assert.True(c.InTransaction);
        _ = await c.DoneAsync("COMMIT");
        Assert.False(c.InTransaction);

        c.Autocommit = true;
        Assert.False((await c.DoneAsync($"INSERT INTO {t} VALUES (1)")).InTransaction);
        Assert.False(c.InTransaction);
        TurborelException duplicate = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync($"INSERT INTO {t} VALUES (1), (2)", TestContext.Current.CancellationToken));
        Assert.False(duplicate.TransactionActive);
        Assert.False(c.InTransaction);
        c.Autocommit = false;
        _ = await c.DoneAsync($"INSERT INTO {t} VALUES (3)");
        Assert.True(c.InTransaction);
        _ = await c.DoneAsync("ROLLBACK");
        Assert.Equal([["1"]], (await c.RowsAsync($"SELECT N FROM {t}")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>A result that sent a locator keeps its transaction, autocommit or not, for the locator is valid within it alone.</summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[CLIENT Running statements]")]
    [Verifies("[CLIENT Large objects]")]
    public async Task LocatorKeepsItsTransaction()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("LOCATED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (C CLOB(1M))");
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new string('x', 70_000)], TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("COMMIT");

        c.Autocommit = true;
        TurborelLargeObject located = Assert.IsType<TurborelLargeObject>(Assert.Single(await c.RowsAsync($"SELECT C FROM {t}"))[0]);
        Assert.True(located.IsLocator);
        Assert.True(c.InTransaction);
        c.Autocommit = false;
        _ = await c.DoneAsync("ROLLBACK");
        Assert.False(c.InTransaction);
    }

    /// <summary>Every kind of value comes as its type: exact, approximate, character, binary, datetime, interval, truth value, UUID and null.</summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    [Verifies("[CLIENT UUIDs]")]
    public async Task EveryValueComesAsItsType()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();

        IReadOnlyList<object?> row = Assert.Single(await c.RowsAsync(
            "VALUES (CAST(1.5 AS DECIMAL(3,1)), 1.5E0, 'x', X'0AFF', DATE '2024-01-02', TIMESTAMP '2024-01-02 10:11:12.5', "
            + "INTERVAL '3' DAY, TRUE, CAST('919108f7-52d1-4320-9bac-f847db4148a8' AS UUID), CAST(NULL AS INTEGER))"));
        Assert.Equal(new TurborelDecimal(15, 1), row[0]);
        Assert.Equal(1.5, row[1]);
        Assert.Equal("x", row[2]);
        Assert.Equal(new byte[] { 0x0A, 0xFF }, row[3]);
        TurborelDatetime date = Assert.IsType<TurborelDatetime>(row[4]);
        Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc).Ticks / 10, date.UtcMicroseconds);
        TurborelDatetime timestamp = Assert.IsType<TurborelDatetime>(row[5]);
        Assert.Equal(new DateTime(2024, 1, 2, 10, 11, 12, 500, DateTimeKind.Utc).Ticks / 10, timestamp.UtcMicroseconds);
        Assert.Equal(3 * 86_400_000_000L, (long)Assert.IsType<TurborelInterval>(row[6]).Units);
        Assert.Equal(true, row[7]);
        Guid uuid = Assert.IsType<Guid>(row[8]);
        Assert.Equal("919108f7-52d1-4320-9bac-f847db4148a8", uuid.ToString());
        Assert.Equal(Convert.FromHexString("919108f752d143209bacf847db4148a8"), uuid.ToByteArray(bigEndian: true));
        Assert.Null(row[9]);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>A query's rows go to a sink a batch at a time, as they come; another statement gives its completion.</summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    public async Task RowsStreamToASink()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        var sink = new Sink();
        string values = string.Join(", ", Enumerable.Range(0, 5000).Select(i => $"({i})"));

        Streamed streamed = await c.QueryIntoAsync($"VALUES {values}", sink, TestContext.Current.CancellationToken);
        Assert.Equal((5000UL, null), (streamed.RowsSent, streamed.Completion));
        Assert.Single(sink.Columns);
        Assert.True(sink.Batches > 1);
        Assert.Equal(5000, sink.Rows);
        Streamed done = await c.QueryIntoAsync("COMMIT", sink, TestContext.Current.CancellationToken);
        Assert.Equal(0, done.Completion?.SqlCode);
        await Assert.ThrowsAsync<TurborelException>(() => c.QueryIntoAsync("SELECT * FROM NOWHERE_AT_ALL", sink, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Several statements run one by one in one request, each outcome given,
    /// the first failure stopping the rest; text of no statement completes once.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Query 'Q']")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task SeveralStatementsRunAsOneRequest()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("MANY");

        Ran ran = await c.QueryManyAsync($"CREATE TABLE {t} (N INTEGER); INSERT INTO {t} VALUES (1), (2); SELECT COUNT(*) FROM {t}", TestContext.Current.CancellationToken);
        Assert.Null(ran.Failure);
        Assert.Equal(3, ran.Outcomes.Count);
        Assert.Equal("2", Live.Texts(Assert.Single(Assert.IsType<RowsOutcome>(ran.Outcomes[2]).Rows))[0]);
        Ran stopped = await c.QueryManyAsync($"INSERT INTO {t} VALUES (3); SELECT * FROM NOWHERE_AT_ALL; INSERT INTO {t} VALUES (4)", TestContext.Current.CancellationToken);
        Assert.Single(stopped.Outcomes);
        Assert.Equal("2A000", stopped.Failure?.SqlState);
        Assert.Equal([["3"]], (await c.RowsAsync($"SELECT COUNT(*) FROM {t}")).Select(Live.Texts));
        Assert.IsType<DoneOutcome>(Assert.Single((await c.QueryManyAsync(" ; ", TestContext.Current.CancellationToken)).Outcomes));
        _ = await c.DoneAsync("ROLLBACK");
    }

    // Counts what it is given.
    private sealed class Sink : IRowSink
    {
        internal IReadOnlyList<Column> Columns { get; private set; } = [];

        internal int Batches { get; private set; }

        internal int Rows { get; private set; }

        void IRowSink.Columns(IReadOnlyList<Column> columns) => Columns = columns;

        void IRowSink.Rows(IReadOnlyList<IReadOnlyList<object?>> rows) => (Batches, Rows) = (Batches + 1, Rows + rows.Count);
    }
}
