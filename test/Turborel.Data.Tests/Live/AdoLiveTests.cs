// The standard API against a real turboreld (RULES.md R-TEST-8): values
// of each .NET type sent as parameters and read back as their types, a
// Query of several statements, transactions of each isolation level and
// their savepoints, batches, and a data source's pool (RULES.md D2, D18,
// D19, D20).

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of the standard API.</summary>
/// <param name="server">The live server.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Each statement's text is the test's own, of a table name the test makes.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1849:Call async methods when in an async method",
    Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
public sealed class AdoLiveTests(LiveServer server)
{
    private static readonly string Plain = $"host=127.0.0.1:{LiveServer.Port};user=ann;tls_mode=disable;passfile=/nonexistent;connect_timeout=5";

    private static readonly IsolationLevel[] Levels = [IsolationLevel.ReadUncommitted, IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead, IsolationLevel.Serializable];

    /// <summary>Each .NET type a parameter takes is stored as its column's type and read back as .NET's type of it.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D18")]
    [Verifies("[DESIGN] D20")]
    public async Task ValuesGoAndComeBackAsTheirTypes()
    {
        server.Require();
        await using var connection = new TurborelConnection(Plain + ";pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        string t = Live.Table("ADO_VALUES");
        await RunAsync(connection, $"CREATE TABLE {t} (I INTEGER, D DECIMAL(10,2), F DOUBLE PRECISION, S VARCHAR(10), B BOOLEAN, TS TIMESTAMP(6), TZ TIMESTAMP(6) WITH TIME ZONE, DT DATE, TM TIME(6), IV INTERVAL DAY TO SECOND(6), U UUID, BIN VARBINARY(4))");
        var at = new DateTime(2024, 1, 2, 10, 11, 12, 500);
        var zoned = new DateTimeOffset(at, TimeSpan.FromHours(2));
        var id = Guid.Parse("919108f7-52d1-4320-9bac-f847db4148a8");
        object[] sent = [42, 12.34m, 2.5, "text", true, at, zoned, DateOnly.FromDateTime(at), TimeOnly.FromDateTime(at), new TimeSpan(1, 2, 3, 4), id, new byte[] { 1, 2 }];
        using var insert = new TurborelCommand($"INSERT INTO {t} VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", connection);
        foreach (object value in sent)
        {
            _ = insert.Parameters.AddWithValue(null, value);
        }

        Assert.Equal(1, await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        _ = insert.Parameters.AddWithValue(null, null);
        insert.Parameters.RemoveAt(0);
        insert.Parameters.Insert(0, new TurborelParameter(null, DBNull.Value));
        insert.Parameters.RemoveAt(12);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        using var select = new TurborelCommand($"SELECT * FROM {t} ORDER BY I", connection);
        await using TurborelDataReader reader = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        object[] read = new object[reader.FieldCount];
        _ = reader.GetValues(read);
        Assert.Equal([42, 12.34m, 2.5, "text", true, at, zoned, at.Date, at.TimeOfDay, new TimeSpan(1, 2, 3, 4), id, new byte[] { 1, 2 }], read);
        Assert.Equal((42, "TIMESTAMP(6) WITH TIME ZONE", typeof(DateTimeOffset)), (reader.GetInt32(0), reader.GetDataTypeName(6), reader.GetFieldType(6)));
        Assert.Equal(("INTEGER", typeof(int), "DECIMAL(10,2)", typeof(decimal)), (reader.GetDataTypeName(0), reader.GetFieldType(0), reader.GetDataTypeName(1), reader.GetFieldType(1)));
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(reader.IsDBNull(0));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A Query of several statements is read by NextResult; a transaction of
    /// each isolation level commits; one rolled back to a savepoint keeps
    /// what came before it; one disposed is rolled back; outside a
    /// transaction each statement commits.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Running statements]")]
    public async Task TransactionsAndSeveralStatements()
    {
        server.Require();
        await using var connection = new TurborelConnection(Plain + ";pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var watcher = new TurborelConnection(Plain + ";pooling=false");
        await watcher.OpenAsync(TestContext.Current.CancellationToken);
        string t = Live.Table("ADO_TX");
        await RunAsync(connection, $"CREATE TABLE {t} (N INTEGER)");

        using (var both = new TurborelCommand($"INSERT INTO {t} VALUES (1), (2); SELECT N FROM {t} ORDER BY N", connection))
        {
            await using TurborelDataReader reader = await both.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(0, reader.FieldCount);
            Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
            var read = new List<int>();
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                read.Add(reader.GetInt32(0));
            }

            Assert.Equal([1, 2], read);
            Assert.Equal(2, reader.RecordsAffected);
        }

        Assert.Equal(2m, await ScalarAsync(watcher, $"SELECT COUNT(*) FROM {t}"));
        foreach (IsolationLevel level in Levels)
        {
            await using TurborelTransaction transaction = await connection.BeginTransactionAsync(level, TestContext.Current.CancellationToken) as TurborelTransaction ?? throw new InvalidOperationException();
            if (level == IsolationLevel.ReadUncommitted)
            {
                // READ UNCOMMITTED is READ ONLY, as SQL makes it.
                Assert.Equal("25000", (await Assert.ThrowsAsync<TurborelException>(() => RunAsync(connection, $"INSERT INTO {t} VALUES (10)"))).SqlState);
                await transaction.RollbackAsync(TestContext.Current.CancellationToken);
                continue;
            }

            await RunAsync(connection, $"INSERT INTO {t} VALUES (10)");
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(5m, await ScalarAsync(watcher, $"SELECT COUNT(*) FROM {t}"));
        await using (TurborelTransaction transaction = connection.BeginTransaction())
        {
            await RunAsync(connection, $"INSERT INTO {t} VALUES (20)");
            await transaction.SaveAsync("kept", TestContext.Current.CancellationToken);
            await RunAsync(connection, $"INSERT INTO {t} VALUES (30)");
            await transaction.RollbackAsync("kept", TestContext.Current.CancellationToken);
            await transaction.ReleaseAsync("kept", TestContext.Current.CancellationToken);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await using (TurborelTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            await RunAsync(connection, $"DELETE FROM {t}");
        }

        Assert.Equal((6m, 1m, 0m), (await ScalarAsync(watcher, $"SELECT COUNT(*) FROM {t}"), await ScalarAsync(watcher, $"SELECT COUNT(*) FROM {t} WHERE N = 20"), await ScalarAsync(watcher, $"SELECT COUNT(*) FROM {t} WHERE N = 30")));
    }

    /// <summary>
    /// A batch runs its statements at once, a statement of parameters among
    /// them; one failing stops it, what came before kept; a data source's
    /// connections share its pool.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D19")]
    [Verifies("[CLIENT Pipelines]")]
    [Verifies("[CLIENT Pools]")]
    public async Task BatchesAndPools()
    {
        server.Require();
        await using var source = new TurborelDataSource(Plain + ";pool_max=2");
        string t = Live.Table("ADO_BATCH");
        NativeConnection first;
        await using (TurborelConnection connection = await source.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            first = connection.Native!;
            await RunAsync(connection, $"CREATE TABLE {t} (N INTEGER PRIMARY KEY)");
            using TurborelBatch batch = connection.CreateBatch();
            batch.BatchCommands.Add(new TurborelBatchCommand($"INSERT INTO {t} VALUES (1)"));
            var parameter = new TurborelBatchCommand($"INSERT INTO {t} VALUES (?)");
            _ = parameter.Parameters.AddWithValue(null, 2);
            batch.BatchCommands.Add(parameter);
            Assert.Equal(2, await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            using TurborelBatch failing = connection.CreateBatch();
            failing.BatchCommands.Add(new TurborelBatchCommand($"INSERT INTO {t} VALUES (3)"));
            failing.BatchCommands.Add(new TurborelBatchCommand($"INSERT INTO {t} VALUES (1)"));
            failing.BatchCommands.Add(new TurborelBatchCommand($"INSERT INTO {t} VALUES (4)"));
            Assert.Equal("23000", (await Assert.ThrowsAsync<TurborelException>(() => failing.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
        }

        await using (TurborelConnection again = source.CreateConnection())
        {
            await again.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Same(first, again.Native);
            Assert.Equal(3m, await ScalarAsync(again, $"SELECT COUNT(*) FROM {t}"));
        }
    }

    /// <summary>
    /// DeriveParameters gives a CALL's parameters their directions; run, it
    /// sets its OUT and INOUT ones, by a command and by a batch; a direction
    /// not its ?'s mode is refused, TB910.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D22")]
    [Verifies("[PROTOCOL Parameter modes]")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task CallsSetTheirOutputParameters()
    {
        server.Require();
        await using var connection = new TurborelConnection(Plain + ";pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        string p = Live.Table("ADO_DOUBLED");
        await RunAsync(connection, $"CREATE PROCEDURE {p} (IN A INTEGER, OUT B INTEGER, INOUT C VARCHAR(10)) READS SQL DATA SELECT A + 1, C || C INTO B, C FROM (VALUES 1) AS ONE (X)");

        using var call = new TurborelCommand($"CALL {p}(?, ?, ?)", connection);
        call.DeriveParameters();
        Assert.Equal([ParameterDirection.Input, ParameterDirection.Output, ParameterDirection.InputOutput], call.Parameters.Cast<TurborelParameter>().Select(q => q.Direction));
        (call.Parameters[0].Value, call.Parameters[2].Value) = (5, "ab");
        _ = await call.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal((6, "abab"), (call.Parameters[1].Value, call.Parameters[2].Value));
        call.Parameters[2].Direction = ParameterDirection.Input;
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => call.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);

        using TurborelBatch batch = connection.CreateBatch();
        var command = new TurborelBatchCommand($"CALL {p}(?, ?, ?)");
        _ = command.Parameters.AddWithValue("a", 1);
        _ = command.Parameters.Add(new TurborelParameter { Direction = ParameterDirection.Output });
        _ = command.Parameters.Add(new TurborelParameter("c", "x") { Direction = ParameterDirection.InputOutput });
        batch.BatchCommands.Add(command);
        _ = await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal((2, "xx"), (command.Parameters[1].Value, command.Parameters[2].Value));
        await RunAsync(connection, $"DROP PROCEDURE {p} RESTRICT");
    }

    /// <summary>A statement that outruns its command's timeout is cancelled on the server, TB920, the connection kept.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Running statements]")]
    public async Task TimeoutCancelsTheStatement()
    {
        server.Require();
        await using var connection = new TurborelConnection(Plain + ";pooling=false");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        string t = Live.Table("ADO_SLOW");
        await RunAsync(connection, $"CREATE TABLE {t} (N INTEGER)");
        await RunAsync(connection, $"INSERT INTO {t} VALUES {string.Join(", ", Enumerable.Range(0, 200).Select(i => $"({i})"))}");
        using var slow = new TurborelCommand($"SELECT COUNT(*) FROM {t} A, {t} B, {t} C, {t} D", connection) { CommandTimeout = 1 };
        TurborelException cancelled = await Assert.ThrowsAsync<TurborelException>(() => slow.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        Assert.Equal((-920, "TB920"), (cancelled.SqlCode, cancelled.SqlState));
        Assert.Equal(200m, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {t}"));
    }

    private static async Task RunAsync(TurborelConnection connection, string sql)
    {
        using var command = new TurborelCommand(sql, connection);
        _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> ScalarAsync(TurborelConnection connection, string sql)
    {
        using var command = new TurborelCommand(sql, connection);
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}
