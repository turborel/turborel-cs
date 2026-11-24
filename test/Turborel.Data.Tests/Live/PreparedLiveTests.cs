// Prepared statements against a real turboreld (RULES.md R-TEST-8):
// prepared once, executed with values as often as wanted; a query's
// cursor opened, fetched a batch at a time and closed, several of one
// statement at once; described again; destroyed; and a CALL's output
// parameters [CLIENT Prepared statements] [CLIENT Controlling the
// session].

using System;
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
    /// it does: the last row's fetch commits, close commits, a fetch that
    /// fails rolls back.
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

        // A fetch that fails rolls the cursor's transaction back: a value the
        // server cannot serialize, an attribute standing alone, fails the
        // fetch of its row.
        PreparedStatement mixed = await c.PrepareAsync(
            "VALUES (XMLQUERY('<a/>' RETURNING SEQUENCE BY REF EMPTY ON EMPTY)), (XMLQUERY('attribute a {\"1\"}' RETURNING SEQUENCE BY REF EMPTY ON EMPTY))",
            TestContext.Current.CancellationToken);
        Assert.True((await c.OpenCursorAsync(mixed, None, 1, cancellationToken: TestContext.Current.CancellationToken)).More);
        Assert.True(c.InTransaction);
        TurborelException failed = await Assert.ThrowsAsync<TurborelException>(() => c.FetchAsync(mixed, 0, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("2200W", false, false), (failed.SqlState, c.InTransaction, c.IsLost));
    }

    /// <summary>
    /// Granted declared-types, which connecting asks for, each column says
    /// the type it was declared with, its name and as SQL writes it, so
    /// INTEGER is told apart from NUMERIC(10,0), a VALUES of CASTs and a
    /// UNION of one number type among them; its values are read by their
    /// tags as ever.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Prepared statements]")]
    [Verifies("[PROTOCOL Declared types]")]
    [Verifies("[PROTOCOL Capabilities]")]
    public async Task ColumnsSayTheirDeclaredTypes()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        Assert.True(c.Has(Capabilities.DeclaredTypes));
        string t = Live.Table("DECLARED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (I INTEGER, S SMALLINT, B BIGINT, D DECIMAL(9,2), N NUMERIC(5), R REAL, P DOUBLE PRECISION, F FLOAT(20), A INTEGER ARRAY[3])");
        PreparedStatement select = await c.PrepareAsync($"SELECT * FROM {t}", TestContext.Current.CancellationToken);
        Assert.Equal(["INTEGER", "SMALLINT", "BIGINT", "DECIMAL", "NUMERIC", "REAL", "DOUBLE PRECISION", "FLOAT", "ARRAY"], select.Columns.Select(column => column.Declared?.Name));
        Assert.Equal(["INTEGER", "SMALLINT", "BIGINT", "DECIMAL(9,2)", "NUMERIC(5,0)", "REAL", "DOUBLE PRECISION", "FLOAT(24)", "INTEGER ARRAY[3]"], select.Columns.Select(column => column.TypeWritten));
        _ = await c.DoneAsync($"INSERT INTO {t} (I) VALUES (7)");
        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.QueryAsync($"SELECT I FROM {t}", TestContext.Current.CancellationToken));
        Assert.Equal(("INTEGER", new TurborelDecimal(7, 0)), (Assert.Single(rows.Columns).TypeWritten, rows.Rows[0][0]));

        // Values of one number type written alike keep its declared type.
        RowsOutcome cast = Assert.IsType<RowsOutcome>(await c.QueryAsync("VALUES (CAST(1 AS INTEGER), CAST(2 AS SMALLINT))", TestContext.Current.CancellationToken));
        Assert.Equal(["INTEGER", "SMALLINT"], cast.Columns.Select(column => column.TypeWritten));
        RowsOutcome union = Assert.IsType<RowsOutcome>(await c.QueryAsync($"SELECT I FROM {t} UNION SELECT I FROM {t}", TestContext.Current.CancellationToken));
        Assert.Equal("INTEGER", Assert.Single(union.Columns).TypeWritten);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// An exact number of 38 digits, turborel's most, goes as a parameter and
    /// comes back as a result whole; one of 39 is refused, SQLSTATE 22003, a
    /// literal of 39 digits -103 and 42000, as text cast 22003; a
    /// quotient's scale is at least 6, so 1/3 is 0.333333. Through the
    /// standard API it reads as the driver's own, and GetDecimal refuses it.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    [Verifies("[CLIENT Prepared statements]")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The statement's text is the test's own, of a table name the test makes.")]
    public async Task ExactNumbersOf38Digits()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("WIDE");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N NUMERIC(38,0), D NUMERIC(38,10))");
        var nines = Int128.Parse(new string('9', 38), System.Globalization.CultureInfo.InvariantCulture);
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new TurborelDecimal(-nines, 0), new TurborelDecimal(nines, 10)], TestContext.Current.CancellationToken);
        Assert.Equal([new TurborelDecimal(-nines, 0), new TurborelDecimal(nines, 10)], Assert.Single(await c.RowsAsync($"SELECT N, D FROM {t}")));
        Assert.Equal(new TurborelDecimal(nines, 0), Assert.Single(Assert.Single(await c.RowsAsync($"VALUES CAST('{new string('9', 38)}' AS NUMERIC(38,0))"))));
        TurborelException past = await Assert.ThrowsAsync<TurborelException>(() => c.ExecuteAsync(insert, [new TurborelDecimal(nines + 1, 0), null], TestContext.Current.CancellationToken));
        Assert.Equal("22003", past.SqlState);
        Assert.Equal(new TurborelDecimal(333333, 6), Assert.Single(Assert.Single(await c.RowsAsync("VALUES 1 / 3"))));
        TurborelException literal = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync($"VALUES {new string('9', 39)}", TestContext.Current.CancellationToken));
        Assert.Equal((-103, "42000"), (literal.SqlCode, literal.SqlState));
        TurborelException text = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync($"VALUES CAST('{new string('9', 39)}' AS NUMERIC(38,0))", TestContext.Current.CancellationToken));
        Assert.Equal("22003", text.SqlState);
        _ = await c.DoneAsync("COMMIT");

        await using var connection = new TurborelConnection($"host=127.0.0.1:{LiveServer.Port};user=ann;tls_mode=disable;passfile=/nonexistent;pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var select = new TurborelCommand($"SELECT D FROM {t}", connection);
        await using TurborelDataReader reader = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new TurborelDecimal(nines, 10), reader.GetValue(0));
        Assert.Equal("22003", Assert.Throws<TurborelException>(() => reader.GetDecimal(0)).SqlState);
        System.Data.Common.DbColumn column = (await reader.GetColumnSchemaAsync(TestContext.Current.CancellationToken))[0];
        Assert.Equal((38, 10), (column.NumericPrecision, column.NumericScale));
    }

    /// <summary>A <c>?</c>'s value cut by the cast to its type completes with the warning 01004, the value cut: inserted, or compared with a column, with ORDER BY or without.</summary>
    [Fact]
    [Verifies("[CLIENT Prepared statements]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task AParameterCutWarns()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("CUT");
        _ = await c.DoneAsync($"CREATE TABLE {t} (V VARCHAR(5))");
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?)", TestContext.Current.CancellationToken);
        Completed done = Assert.IsType<DoneOutcome>(await c.ExecuteAsync(insert, ["abcdefgh"], TestContext.Current.CancellationToken)).Completion;
        Assert.Equal(("01004", "01004"), (done.SqlState, c.Warning));
        Assert.Equal([["abcde"]], (await c.RowsAsync($"SELECT V FROM {t}")).Select(Live.Texts));
        foreach (string order in new[] { string.Empty, " ORDER BY V" })
        {
            PreparedStatement compared = await c.PrepareAsync($"SELECT V FROM {t} WHERE V = ?{order}", TestContext.Current.CancellationToken);
            RowsOutcome found = Assert.IsType<RowsOutcome>(await c.ExecuteAsync(compared, ["abcdefgh"], TestContext.Current.CancellationToken));
            Assert.Equal(("abcde", "01004"), (Assert.Single(found.Rows)[0], c.Warning));
        }

        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>A statement prepared under a number in use, with no cursor of it open, replaces the one of that number.</summary>
    [Fact]
    [Verifies("[PROTOCOL Prepare 'P']")]
    public async Task PrepareReplacesTheStatementOfItsNumber()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        c.Autocommit = true;
        PreparedStatement first = await c.PrepareAsync("VALUES 1", TestContext.Current.CancellationToken);
        await c.SendAsync(new Request.Prepare(first.Number, "VALUES 2"), TestContext.Current.CancellationToken);
        Assert.IsType<Reply.Prepared>(await c.ReceiveAsync(TestContext.Current.CancellationToken));
        RowsOutcome rows = Assert.IsType<RowsOutcome>(await c.ExecuteAsync(first, None, TestContext.Current.CancellationToken));
        Assert.Equal([["2"]], rows.Rows.Select(Live.Texts));
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
