// An import against a server of the test's own, byte for byte: through a
// prepared INSERT a batch at a time, bad rows skipped or stopping it, the
// batch under way rolled back; by copy, a refused row named by the copy's
// failure; a long field for a large object column sent by a locator; and
// what a failure kept [CLIENT Importing].

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Import;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Import;

/// <summary>Tests of <see cref="NativeConnection.ImportAsync"/> against a scripted server.</summary>
public sealed class ImportTests
{
    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    private static readonly Parameter[] IdAndName = [new(false, new ExactType(10, 0)), new(false, new CharacterType(5, CharacterUnits.Characters, true))];

    /// <summary>
    /// Through a prepared INSERT of the header's columns, rows are executed
    /// and committed a batch at a time; a refused row, or one of too many
    /// fields, is skipped and given to the program; the INSERT is deallocated.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task InsertsABatchAtATimeSkippingBadRows()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            Assert.Contains("INSERT INTO \"T\" (\"ID\", \"NAME\") VALUES (?, ?)", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Prepare)), StringComparison.Ordinal);
            await peer.WriteAsync('P', Replies.Prepared(0, IdAndName));
            await RowAsync(peer, ok: true);
            await RowAsync(peer, ok: false);
            await RowAsync(peer, ok: true);
            await CommitAsync(peer);
            await RowAsync(peer, ok: true);
            await CommitAsync(peer);
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);
        var rejected = new List<ImportRejected>();

        Imported imported = await c.ImportAsync(Csv("id,name\n1,a\n-2,b\n3,c,x\n4,d\n5,e\n"), new ImportOptions("t") { Batch = 2, Skip = true }, rejected.Add, TestContext.Current.CancellationToken);
        Assert.Equal(new Imported(3, 2), imported);
        Assert.Equal([new ImportPosition(2, 3), new ImportPosition(3, 4)], rejected.Select(r => r.Position));
        Assert.Equal("23000", rejected[0].Cause.SqlState);
        Assert.Equal(new ImportPosition(2, 3), rejected[0].Cause.Import?.Position);
        await server.PlayedAsync();
    }

    /// <summary>
    /// With no header, the table's columns are described by a query of them;
    /// a refused row not skipped stops the import, the batch under way rolled
    /// back, saying where and what was kept.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task AFailureStopsAndRollsTheBatchBack()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            Assert.Contains("SELECT * FROM \"T\"", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Prepare)), StringComparison.Ordinal);
            await peer.WriteAsync('P', Replies.Prepared(0, [], new Column("ID", new ExactType(10, 0), false), new Column("NAME", new CharacterType(5, CharacterUnits.Characters, true), false)));
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Contains("(\"ID\", \"NAME\")", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Prepare)), StringComparison.Ordinal);
            await peer.WriteAsync('P', Replies.Prepared(1, IdAndName));
            await RowAsync(peer, ok: true);
            await RowAsync(peer, ok: true);
            await CommitAsync(peer);
            await RowAsync(peer, ok: true);
            await RowAsync(peer, ok: false);
            Assert.Contains("ROLLBACK", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Query)), StringComparison.Ordinal);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException stopped = await Assert.ThrowsAsync<TurborelException>(
            () => c.ImportAsync(new DelimitedSource(Of("1,a\n2,b\n3,c\n-4,d\n5,e\n"), DelimitedDialect.Csv()), new ImportOptions("T") { Batch = 2 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("23000", new ImportFailure(new ImportPosition(4, 4), 2, 1)), (stopped.SqlState, stopped.Import));
        Assert.Equal("record 4, line 4: 2 rows committed, 1 rolled back", stopped.Import!.ToString());
        await server.PlayedAsync();
    }

    /// <summary>
    /// Where copy is granted, the rows go a batch to a copy, each committing
    /// as it ends; a row the copy refuses, named by its detail, stops it,
    /// the rows before it in the batch counted rolled back.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task CopiesABatchAtATime()
    {
        var refused = new Failure { SqlCode = -201, SqlState = "23000", Message = "refused", Diagnostics = new Diagnostics { Detail = "row 2 of the copy" } };
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Copy);
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(0, IdAndName));
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.CopyIn("\"T\"", ["\"ID\"", "\"NAME\""], Ends: true))[5..], await peer.ExpectAsync(RequestKinds.CopyIn));
            _ = await peer.ExpectAsync(RequestKinds.CopyData);
            _ = await peer.ExpectAsync(RequestKinds.CopyEnd);
            await peer.WriteAsync('K', Replies.CompleteRows(2));
            _ = await peer.ExpectAsync(RequestKinds.CopyIn);
            _ = await peer.ExpectAsync(RequestKinds.CopyData);
            _ = await peer.ExpectAsync(RequestKinds.CopyEnd);
            await peer.WriteAsync('E', Wire.Written(refused.Write));
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException stopped = await Assert.ThrowsAsync<TurborelException>(
            () => c.ImportAsync(Csv("id,name\n1,a\n2,b\n3,c\n4,d\n"), new ImportOptions("T") { Batch = 2 }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new ImportFailure(new ImportPosition(4, 5), 2, 1), stopped.Import);
        Assert.Equal(2, CopiedImporter.RowOf(stopped));
        Assert.Null(CopiedImporter.RowOf(new TurborelException(-1, "X", "m") { }));
        await server.PlayedAsync();
    }

    /// <summary>
    /// By copy, a record of too many fields, a string too long for its
    /// column, or input that cannot be read stop the import before its copy
    /// is sent, the copy under way abandoned.
    /// </summary>
    /// <param name="text">The CSV.</param>
    /// <param name="state">The failure's SQLSTATE.</param>
    [Theory]
    [Verifies("[CLIENT Importing]")]
    [InlineData("id,name\n1,a\n2,b,c\n", "22000")]
    [InlineData("id,name\n1,a\n2,toolong\n", "22001")]
    [InlineData("id,name\n1,a\n\"2,b\n", "22000")]
    public async Task CopiedRecordsAreChecked(string text, string state)
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Copy);
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(0, IdAndName));
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.CopyIn);
            Assert.Equal([1], await peer.ExpectAsync(RequestKinds.CopyEnd));
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException stopped = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv(text), new ImportOptions("T"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((state, 0UL, 1UL), (stopped.SqlState, stopped.Import!.Committed, stopped.Import.RolledBack));
        await server.PlayedAsync();
    }

    /// <summary>
    /// Into a large object column, a field longer than 64 KiB is sent by
    /// creating a large object, given by its locator; a commit that fails
    /// stops the import with what it kept.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    [Verifies("[CLIENT Large objects]")]
    public async Task LongFieldsGoByALocator()
    {
        Parameter[] lob = [new(false, new LargeObjectType(LargeObjectKind.Character, 1 << 20, CharacterUnits.Characters))];
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Copy);
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(0, lob));
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await peer.WriteAsync('L', Replies.LobLocator(3));
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await peer.WriteAsync('K', Replies.Complete());
            byte[] execute = await peer.ExpectAsync(RequestKinds.Execute);
            Assert.Equal(Tags.LargeObjectLocator, execute[8]);
            await peer.WriteAsync('K', Replies.Complete(active: true));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-913, "40002"));
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException stopped = await Assert.ThrowsAsync<TurborelException>(
            () => c.ImportAsync(Csv("body\n" + new string('x', 70_000) + "\n"), new ImportOptions("T"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("40002", new ImportFailure(null, 0, 1)), (stopped.SqlState, stopped.Import));
        await server.PlayedAsync();
    }

    /// <summary>A source that cannot be read stops the import where it says; a lost connection stops it with nothing more sent.</summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task SourcesAndConnectionsThatFailStopIt()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(0, IdAndName));
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(1, IdAndName));
            _ = await peer.ExpectAsync(RequestKinds.Execute);
            await peer.WriteAsync('E', Replies.Error(-919, "08006"));
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException unread = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("id,name\n\"open\n"), new ImportOptions("T"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((new ImportPosition(1, 2), "22000"), (unread.Import?.Position, unread.SqlState));
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("id,name\n1,a\n"), new ImportOptions("T") { Skip = true }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("08006", true), (lost.SqlState, c.IsLost));
        await server.PlayedAsync();
    }

    /// <summary>
    /// A source that fails after rows of its batch were inserted stops the
    /// import with that batch rolled back and the INSERT deallocated, a
    /// failure of the driver's or of a program's own source alike; the
    /// default batch is 1000 rows, and a batch of 0 commits once, at the end.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task ASourceFailingMidBatchRollsItBack()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            for (uint n = 0; n < 2; n++)
            {
                _ = await peer.ExpectAsync(RequestKinds.Prepare);
                await peer.WriteAsync('P', Replies.Prepared(n, IdAndName));
                await RowAsync(peer, ok: true);
                Assert.Contains("ROLLBACK", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Query)), StringComparison.Ordinal);
                await peer.WriteAsync('K', Replies.Complete());
                _ = await peer.ExpectAsync(RequestKinds.Deallocate);
                await peer.WriteAsync('K', Replies.Complete());
            }

            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(2, IdAndName));
            for (int i = 0; i < 3; i++)
            {
                await RowAsync(peer, ok: true);
            }

            await CommitAsync(peer);
            _ = await peer.ExpectAsync(RequestKinds.Deallocate);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        TurborelException unread = await Assert.ThrowsAsync<TurborelException>(() => c.ImportAsync(Csv("id,name\n1,a\n\"open\n"), new ImportOptions("T"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(("22000", new ImportFailure(new ImportPosition(2, 3), 0, 1), false), (unread.SqlState, unread.Import, c.InTransaction));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.ImportAsync(new FailingSource(), new ImportOptions("T"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((1000UL, 1000UL), (ImportOptions.DefaultBatch, new ImportOptions("T").Batch));
        Assert.Equal(new Imported(3, 0), await c.ImportAsync(Csv("id,name\n1,a\n2,b\n3,c\n"), new ImportOptions("T") { Batch = 0 }, cancellationToken: TestContext.Current.CancellationToken));
        await server.PlayedAsync();
    }

    private static MemoryStream Of(string text) => new(Encoding.UTF8.GetBytes(text));

    private static DelimitedSource Csv(string text) => new(Of(text), DelimitedDialect.Csv().WithHeader());

    // A program's source of one record, then a failure of its own.
    private sealed class FailingSource : IImportSource
    {
        private int _read;

        public ValueTask<IReadOnlyList<ImportHeading>?> HeadingsAsync(System.Threading.CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ImportHeading>?>([new ImportHeading("ID"), new ImportHeading("NAME")]);

        public ValueTask<ImportRecord?> NextAsync(System.Threading.CancellationToken cancellationToken = default) =>
            _read++ == 0
                ? ValueTask.FromResult<ImportRecord?>(new ImportRecord(["1", "a"], new ImportPosition(1, 1)))
                : throw new InvalidOperationException("the program's source failed");
    }

    private static async Task RowAsync(FakeServer.Peer peer, bool ok)
    {
        _ = await peer.ExpectAsync(RequestKinds.Execute);
        await (ok ? peer.WriteAsync('K', Replies.Complete(active: true)) : peer.WriteAsync('E', Replies.Error(-201, "23000", active: true)));
    }

    private static async Task CommitAsync(FakeServer.Peer peer)
    {
        Assert.Contains("COMMIT", Encoding.UTF8.GetString(await peer.ExpectAsync(RequestKinds.Query)), StringComparison.Ordinal);
        await peer.WriteAsync('K', Replies.Complete());
    }

    private static Task<NativeConnection> ConnectAsync(FakeServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick);
}
