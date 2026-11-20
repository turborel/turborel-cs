// The standard API's transactions and batches against a server of the
// test's own, byte for byte: a transaction's isolation level set, its
// statements out of autocommit, its savepoints, its end by COMMIT or
// ROLLBACK, a connection closed rolling it back; a cursor's transaction
// committed by the client of a server that grants no fetch-ends; a batch
// sent as a pipeline that stops at its first failure (RULES.md D2, D20).

using System;
using System.Data;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Ado;

/// <summary>Tests of <see cref="TurborelTransaction"/> and <see cref="TurborelBatch"/> over the wire.</summary>
public sealed class TransactionTests
{
    private static readonly Parameter[] One = [new(false, new ExactType(10, 0))];

    private static readonly string[] Piped = ["VALUES 1", "SAVEPOINT TURBOREL_PIPELINE_1", "SET SCHEMA S", "SAVEPOINT TURBOREL_PIPELINE_2", "VALUES 2"];

    /// <summary>
    /// A transaction sets its isolation level and runs its statements out of
    /// autocommit, its savepoints set, rolled back to and released, ended by
    /// COMMIT, or by ROLLBACK as it is disposed; one that fails to begin
    /// leaves autocommit on; a misuse is TB910; a cursor's transaction is
    /// committed as its reader closes under autocommit of a server that
    /// grants no fetch-ends; a connection closed rolls back its transaction.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D17")]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Running statements]")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task TransactionsSetTheirLevelAndEnd()
    {
        await using var server = FakeServer.Start(TransactionScriptAsync);
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        connection.Open();
        Assert.True(connection.Native!.Autocommit);

        await using (TurborelTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            Assert.Equal((IsolationLevel.Serializable, true, false), (transaction.IsolationLevel, transaction.SupportsSavepoints, connection.Native.Autocommit));
            Assert.Same(connection, transaction.Connection);
            Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(async () => await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))).SqlState);
            using var update = new TurborelCommand("UPDATE T SET N = 2", connection) { Transaction = transaction };
            Assert.Equal(-1, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            transaction.Save("a");
            await transaction.RollbackAsync("a", TestContext.Current.CancellationToken);
            transaction.Release("a");
            transaction.Commit();
            Assert.True(connection.Native.Autocommit);
            Assert.Equal("TB910", Assert.Throws<TurborelException>(() => transaction.Save("b")).SqlState);
        }

        await using (TurborelTransaction unspecified = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken) as TurborelTransaction ?? throw new InvalidOperationException())
        {
            Assert.Equal(IsolationLevel.Unspecified, unspecified.IsolationLevel);
            using var delete = new TurborelCommand("DELETE FROM T", connection);
            _ = await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(connection.Native.Autocommit);
        Assert.Equal("25001", Assert.Throws<TurborelException>(() => connection.BeginTransaction(IsolationLevel.ReadUncommitted)).SqlState);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => connection.BeginTransaction(IsolationLevel.Chaos)).SqlState);
        Assert.True(connection.Native.Autocommit);
        using var cursor = new TurborelCommand("SELECT N FROM T WHERE N = ?", connection);
        _ = cursor.Parameters.AddWithValue("n", 1);
        Assert.Equal(1m, await cursor.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        TurborelTransaction left = connection.BeginTransaction(IsolationLevel.Snapshot);
        using var other = new TurborelCommand("UPDATE T SET N = 3", connection);
        _ = other.ExecuteNonQuery();
        using var stranger = new TurborelCommand("VALUES 1") { Connection = connection, Transaction = left };
        connection.Close();
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => stranger.ExecuteNonQuery()).SqlState);
        await left.DisposeAsync();
        await server.PlayedAsync();
    }

    /// <summary>
    /// A batch is sent as a pipeline, each statement of parameters prepared
    /// first and deallocated after; its statements' rows changed are each
    /// told and summed; its results are read in turn; it stops at its first
    /// failure, which is thrown.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[CLIENT Pipelines]")]
    public async Task BatchesGoAsPipelines()
    {
        await using var server = FakeServer.Start(BatchScriptAsync);
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using TurborelBatch batch = connection.CreateBatch();
        batch.BatchCommands.Add(new TurborelBatchCommand("DELETE FROM T"));
        var insert = new TurborelBatchCommand("INSERT INTO T VALUES (?)");
        _ = insert.Parameters.AddWithValue("n", 1);
        batch.BatchCommands.Add(insert);
        Assert.Equal(3, await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal((2, 1), (batch.BatchCommands[0].RecordsAffected, insert.RecordsAffected));

        using var values = new TurborelBatch { Connection = connection };
        values.BatchCommands.Add(new TurborelBatchCommand("VALUES 1"));
        values.BatchCommands.Add(new TurborelBatchCommand("SET SCHEMA S"));
        values.BatchCommands.Add(new TurborelBatchCommand("VALUES 2"));
        await using (TurborelDataReader reader = await values.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1, reader.GetInt32(0));
            Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
            Assert.Equal((0, false), (reader.FieldCount, await reader.ReadAsync(TestContext.Current.CancellationToken)));
            Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal((2, -1), (reader.GetInt32(0), reader.RecordsAffected));
            Assert.False(await reader.NextResultAsync(TestContext.Current.CancellationToken));
        }

        using var failing = new TurborelBatch { Connection = connection };
        failing.BatchCommands.Add(new TurborelBatchCommand("UPDATE T SET N = 0"));
        failing.BatchCommands.Add(new TurborelBatchCommand("BAD"));
        Assert.Equal("42000", (await Assert.ThrowsAsync<TurborelException>(() => failing.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
        Assert.Equal((3, -1), (failing.BatchCommands[0].RecordsAffected, failing.BatchCommands[1].RecordsAffected));
        await connection.CloseAsync();
        await server.PlayedAsync();
    }

    // A skipping batch's end: its first savepoint released, its work committed.
    private static async Task SettleAsync(FakeServer.Peer peer)
    {
        Assert.Equal(Query("RELEASE SAVEPOINT TURBOREL_PIPELINE_1", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        Assert.Equal(Query("COMMIT", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete());
    }

    // A Query's body, of no attributes.
    private static byte[] Query(string sql, bool ends) => Wire.Encode(new Request.Query(sql, ends, []))[5..];

    // The server's side of the test.
    private static async Task TransactionScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync();
        foreach ((string sql, bool active) in new[]
        {
            ("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", false), ("UPDATE T SET N = 2", true), ("SAVEPOINT \"a\"", true),
            ("ROLLBACK TO SAVEPOINT \"a\"", true), ("RELEASE SAVEPOINT \"a\"", true), ("COMMIT", false),
            ("DELETE FROM T", true), ("ROLLBACK", false),
        })
        {
            Assert.Equal(Query(sql, ends: false), await peer.ExpectAsync(RequestKinds.Query));
            await peer.WriteAsync('K', Replies.Complete(active: active));
        }

        Assert.Equal(Query("SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('E', Replies.Error(-901, "25001"));
        _ = await peer.ExpectAsync(RequestKinds.Prepare);
        await peer.WriteAsync('P', Replies.Prepared(0, One, Replies.Integer));
        Assert.Equal(Wire.Encode(new Request.Execute(new(0), [new TurborelDecimal(1, 0)], 1000, false, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        _ = await peer.ExpectAsync(RequestKinds.Deallocate);
        await peer.WriteAsync('K', Replies.Complete(active: true));
        Assert.Equal(Query("COMMIT", ends: true), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Query("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Query("UPDATE T SET N = 3", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        Assert.Equal(Query("ROLLBACK", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Terminate);
    }

    // The server's side of the test.
    private static async Task BatchScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync();
        Assert.Equal(Wire.Encode(new Request.Prepare(0, "INSERT INTO T VALUES (?)"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
        await peer.WriteAsync('P', Replies.Prepared(0, One));
        Assert.Equal(Query("DELETE FROM T", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        Assert.Equal(Query("SAVEPOINT TURBOREL_PIPELINE_1", ends: false), await peer.ExpectAsync(RequestKinds.Query));
        Assert.Equal(Wire.Encode(new Request.Execute(new(0), [new TurborelDecimal(1, 0)], 0, false, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('K', Replies.CompleteRows(2));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('K', Replies.CompleteRows(1));
        await SettleAsync(peer);
        Assert.Equal(Wire.Encode(new Request.Deallocate(0))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
        await peer.WriteAsync('K', Replies.Complete());
        foreach (string sql in Piped)
        {
            Assert.Equal(Query(sql, ends: false), await peer.ExpectAsync(RequestKinds.Query));
        }

        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(2));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await SettleAsync(peer);
        _ = await peer.ExpectAsync(RequestKinds.Query);
        _ = await peer.ExpectAsync(RequestKinds.Query);
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('K', Replies.CompleteRows(3));
        await peer.WriteAsync('K', Replies.Complete(active: true));
        await peer.WriteAsync('E', Replies.Error(-104, "42000", active: true));
        await SettleAsync(peer);
        _ = await peer.ExpectAsync(RequestKinds.Terminate);
    }
}
