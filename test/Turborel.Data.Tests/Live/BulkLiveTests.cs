// Copy, export and import against a real turboreld (RULES.md R-TEST-8):
// rows copied into a table, all or nothing, a copy dropped abandoned; a
// query exported as CSV and as native rows read back; a source imported a
// batch at a time, by copy or by INSERT, bad rows skipped if asked, a long
// field for a large object column sent by a locator [CLIENT Copying and
// exporting] [CLIENT Importing] [PROTOCOL CopyEnd 'N'].

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Import;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of copy, export and import.</summary>
/// <param name="server">The live server.</param>
public sealed class BulkLiveTests(LiveServer server)
{
    /// <summary>
    /// Rows copied into the columns named give the rows inserted; a row
    /// refused undoes every row, its detail naming it; abandoned, the copy
    /// counts none; a copy disposed unfinished is abandoned.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL CopyIn 'I']")]
    [Verifies("[PROTOCOL CopyEnd 'N']")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task CopyIsAllOrNothing()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("COPIED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER CHECK (N > 0), S VARCHAR(5))");
        IEnumerable<IReadOnlyList<object?>> Rows(params int[] ns) => ns.Select(n => (IReadOnlyList<object?>)[$"s{n}", new TurborelDecimal(n, 0)]);

        Assert.Equal(3UL, await c.CopyRowsAsync(t, ["S", "N"], Rows(1, 2, 3), TestContext.Current.CancellationToken));
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => c.CopyRowsAsync(t, ["S", "N"], Rows(4, -5, 6), TestContext.Current.CancellationToken));
        Assert.Equal(("23000", "row 2 of the copy"), (refused.SqlState, refused.Detail));
        Copying abandoned = await c.CopyInAsync(t, [], TestContext.Current.CancellationToken);
        await abandoned.SendAsync([new TurborelDecimal(7, 0), "x"], TestContext.Current.CancellationToken);
        await abandoned.AbandonAsync(TestContext.Current.CancellationToken);
        await using (Copying dropped = await c.CopyInAsync(t, [], TestContext.Current.CancellationToken))
        {
            await dropped.SendAsync([new TurborelDecimal(8, 0), "y"], TestContext.Current.CancellationToken);
        }

        Assert.False(c.IsLost);
        Assert.Equal([["3"]], (await c.RowsAsync($"SELECT COUNT(*) FROM {t}")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");

        // Under autocommit, a copy is its own transaction: committed as it
        // ends, rolled back as it fails.
        c.Autocommit = true;
        string u = Live.Table("AUTOCOPIED");
        _ = await c.DoneAsync($"CREATE TABLE {u} (N INTEGER CHECK (N > 0), S VARCHAR(5))");
        Assert.Equal(2UL, await c.CopyRowsAsync(u, ["S", "N"], Rows(9, 10), TestContext.Current.CancellationToken));
        Assert.False(c.InTransaction);
        _ = await Assert.ThrowsAsync<TurborelException>(() => c.CopyRowsAsync(u, ["S", "N"], Rows(11, -12), TestContext.Current.CancellationToken));
        Assert.False(c.InTransaction);
        await using NativeConnection other = await Live.ConnectAsync();
        Assert.Equal([["2"]], (await other.RowsAsync($"SELECT COUNT(*) FROM {u}")).Select(Live.Texts));
    }

    /// <summary>
    /// Granted <c>columns</c>, rows of text go by column: a null and an empty
    /// string kept apart; a cell too long fails at CopyEnd, its row in the
    /// detail, every row undone; text rows and a row of other values mixed
    /// in one copy, by column and by row, all inserted; abandoned, none.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL CopyColumns 'O']")]
    [Verifies("[PROTOCOL CopyEnd 'N']")]
    [Verifies("[CLIENT Copying and exporting]")]
    [Verifies("[DESIGN] D24")]
    public async Task CopyByColumn()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        Assert.True(c.Has(Capabilities.Columns));
        string t = Live.Table("COLUMNS");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, S VARCHAR(5))");

        Assert.Equal(4UL, await c.CopyRowsAsync(t, ["N", "S"], [["1", "a"], [null, string.Empty], [new TurborelDecimal(3, 0), "c"], ["4", null]], TestContext.Current.CancellationToken));
        Assert.Equal(
            [["1", "a"], ["3", "c"], ["4", null], [null, string.Empty]],
            (await c.RowsAsync($"SELECT N, S FROM {t} ORDER BY N NULLS LAST")).Select(Live.Texts));
        TurborelException tooLong = await Assert.ThrowsAsync<TurborelException>(() => c.CopyRowsAsync(t, ["N", "S"], [["5", "e"], ["6", "longer"]], TestContext.Current.CancellationToken));
        Assert.Equal(("row 2 of the copy", false), (tooLong.Detail, c.IsLost));
        Copying abandoned = await c.CopyInAsync(t, ["N", "S"], TestContext.Current.CancellationToken);
        await abandoned.SendAsync(["7", "g"], TestContext.Current.CancellationToken);
        await abandoned.AbandonAsync(TestContext.Current.CancellationToken);
        Assert.Equal([["4"]], (await c.RowsAsync($"SELECT COUNT(*) FROM {t}")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A query exported as CSV writes a header of its columns' names, then a
    /// record for each row, a null empty, a field quoted where it must be; as
    /// native rows, it reads back with every value as it was.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task ExportAsCsvAndNative()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        const string Query = "SELECT * FROM (VALUES (1, 'plain', 1.5E0, DATE '2024-01-02'), (2, 'a,\"b\"', CAST(NULL AS DOUBLE PRECISION), CAST(NULL AS DATE))) AS T (N, S, X, D) ORDER BY N";

        using var csv = new MemoryStream();
        Assert.Equal(2UL, await c.ExportAsync(Query, csv, ExportFormat.Csv, TestContext.Current.CancellationToken));
        Assert.Equal("N,S,X,D\r\n1,plain,1.5E0,2024-01-02\r\n2,\"a,\"\"b\"\"\",,\r\n", Encoding.UTF8.GetString(csv.ToArray()));
        using var native = new MemoryStream();
        Assert.Equal(2UL, await c.ExportAsync(Query, native, ExportFormat.Native, TestContext.Current.CancellationToken));
        native.Position = 0;
        (IReadOnlyList<Column> columns, IReadOnlyList<IReadOnlyList<object?>> rows) = await NativeRows.ReadAsync(native, TestContext.Current.CancellationToken);
        Assert.Equal(["N", "S", "X", "D"], columns.Select(column => column.Name));
        Assert.Equal((object)1.5, rows[0][2]);
        Assert.Null(rows[1][3]);
        TurborelException notQuery = await Assert.ThrowsAsync<TurborelException>(() => c.ExportAsync("COMMIT", csv, ExportFormat.Csv, TestContext.Current.CancellationToken));
        Assert.Contains("an export runs a query", notQuery.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CSV source imports into a table a batch at a time, by copy, its
    /// header naming the columns; a row refused stops it, the batch under way
    /// rolled back, saying where.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task ImportByCopy()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("IMPORTED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID INTEGER CHECK (ID > 0), NAME VARCHAR(5))");
        _ = await c.DoneAsync("COMMIT");

        var how = new ImportOptions(t) { Batch = 2 };
        Imported imported = await c.ImportAsync(Csv("name,id\nann,1\nbob,2\ncy,3\n"), how, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(new Imported(3, 0), imported);
        TurborelException stopped = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("name,id\ndee,4\neve,5\nfay,-6\ngus,7\n"), how, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("23000", stopped.SqlState);
        Assert.Equal(new ImportFailure(new ImportPosition(3, 4), 2, 0), stopped.Import);
        TurborelException tooLong = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("name,id\nhannah,8\n"), how, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("22001", new ImportPosition(1, 2)), (tooLong.SqlState, tooLong.Import?.Position));
        Assert.Equal([["5"]], (await c.RowsAsync($"SELECT COUNT(*) FROM {t}")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// An import skipping bad rows goes by INSERT, each refused row given to
    /// the program; a record of too many fields too; with no header the
    /// fields are the table's columns in order.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task ImportSkippingBadRows()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("SKIPPED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID INTEGER CHECK (ID > 0), NAME VARCHAR(5))");
        _ = await c.DoneAsync("COMMIT");
        var rejected = new List<ImportRejected>();

        Imported imported = await c.ImportAsync(new DelimitedSource(new MemoryStream("1,ann\n-2,bob\n3,cy,extra\n4,dee\n"u8.ToArray()), DelimitedDialect.Csv()), new ImportOptions(t) { Skip = true }, rejected.Add, TestContext.Current.CancellationToken);
        Assert.Equal(new Imported(2, 2), imported);
        Assert.Equal([new ImportPosition(2, 2), new ImportPosition(3, 3)], rejected.Select(r => r.Position));
        Assert.Equal(("23000", "[DESIGN] 3 fields, for 2 columns"), (rejected[0].Cause.SqlState, rejected[1].Cause.Message));
        Assert.Equal([["2"]], (await c.RowsAsync($"SELECT COUNT(*) FROM {t}")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// An import into a large object column goes by INSERT, a field longer
    /// than 64 KiB sent by creating a large object; an import begins with no
    /// transaction active; a table not named right is refused.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    [Verifies("[CLIENT Large objects]")]
    public async Task ImportIntoALargeObjectColumn()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("DOCUMENTS");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID INTEGER, BODY CLOB(1M))");
        _ = await c.DoneAsync("COMMIT");
        string body = new('x', 100_000);

        Assert.Equal(new Imported(2, 0), await c.ImportAsync(Csv($"id,body\n1,short\n2,{body}\n"), new ImportOptions(t), cancellationToken: TestContext.Current.CancellationToken));
        var lob = (TurborelLargeObject)(await c.RowsAsync($"SELECT BODY FROM {t} WHERE ID = 2"))[0][0]!;
        Assert.Equal(100_000UL, lob.Octets);
        TurborelException active = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("id\n1\n"), new ImportOptions(t), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("22000", "[DESIGN] an import begins with no transaction active, as it commits its batches"), (active.SqlState, active.Message));
        _ = await c.DoneAsync("ROLLBACK");
        TurborelException named = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("id\n1\n"), new ImportOptions("a b"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("[ISO/IEC 9075:1992 §5.4] a b is not a <table name>", named.Message);
        TurborelException missing = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("1\n"), new ImportOptions("NO_SUCH_TABLE"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new ImportFailure(null, 0, 0), missing.Import);
        Assert.Null(missing.Position);
    }

    private static DelimitedSource Csv(string text) => new(new MemoryStream(Encoding.UTF8.GetBytes(text)), DelimitedDialect.Csv().WithHeader());
}
