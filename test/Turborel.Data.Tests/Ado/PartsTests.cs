// The standard API's parts around its commands: connection strings read
// into settings and a pool's configuration, a data source pooling its
// connections, the provider factory, parameters and their collection, and
// each misuse refused, TB910 (RULES.md D2, D17, D19).

using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Configuration;
using Turborel.Data.Pooling;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Ado;

/// <summary>Tests of the standard API's connection strings, sources, factory and parameters.</summary>
public sealed class PartsTests
{
    /// <summary>
    /// A connection string's keys are the settings' names, any case, and the
    /// pool's; a URI is one too; a key of no setting, or a pool's value not a
    /// whole number, is refused, 08001; pooling=false gives no pool.
    /// </summary>
    [Fact]
    [Verifies("[DESIGN] D19")]
    [Verifies("[CLIENT Where settings come from]")]
    public void ConnectionStringsNameTheSettings()
    {
        var builder = new TurborelConnectionStringBuilder("HOST=db1:7000;User=ann;tls_mode=disable;passfile=/nonexistent;pool_max=3;pool_max_idle=2;pool_idle_timeout=0;pool_max_lifetime=60;pool_wait=5;pool_check=false");
        ConnectionSettings settings = builder.ToSettings();
        Assert.Equal([new TcpHost("db1", 7000)], settings.Hosts);
        Assert.Equal("ann", settings.User);
        PoolConfig config = builder.ToPoolConfig()!;
        Assert.Equal((3, 2, null, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5), false), (config.Max, config.MaxIdle, config.IdleTimeout, config.MaxLifetime, config.Wait, config.Check));
        PoolConfig defaults = new TurborelConnectionStringBuilder("user=ann").ToPoolConfig()!;
        Assert.Equal((new PoolConfig().Max, new PoolConfig().IdleTimeout), (defaults.Max, defaults.IdleTimeout));
        Assert.Null(new TurborelConnectionStringBuilder("user=ann;pooling=False").ToPoolConfig());
        var typed = new TurborelConnectionStringBuilder { ["user"] = "ann", ["pool_check"] = false, ["tls_system_roots"] = true, ["tls_mode"] = "disable" };
        Assert.Equal((false, true), (typed.ToPoolConfig()!.Check, typed.ToSettings().Tls.SystemRoots));
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => new TurborelConnectionStringBuilder("pooling=maybe").ToPoolConfig()).SqlState);
        var uri = new TurborelConnectionStringBuilder("turborel://ann@db2:7001/?application_name=payroll");
        Assert.Equal(("ann", "payroll"), (uri["user"], uri["APPLICATION_NAME"]));
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => new TurborelConnectionStringBuilder("colour=red")).SqlState);
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => new TurborelConnectionStringBuilder("pool_max=many").ToPoolConfig()).SqlState);
        Assert.Equal(string.Empty, new TurborelConnectionStringBuilder(null).ConnectionString);
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => new TurborelConnectionStringBuilder("no equals sign")).SqlState);
    }

    /// <summary>
    /// A data source pools its connections: one closed is given back, reset,
    /// and the next open takes it, checked; its pool is disposed with it.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D19")]
    [Verifies("[CLIENT Pools]")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task DataSourcesPoolTheirConnections()
    {
        await using var server = ManyServer.Start(Capabilities.Ping, Capabilities.Reset);
        await using var source = new TurborelDataSource(AdoServer.Of(server.Port, pooled: true) + ";pool_max=1;pool_check=true");
        Assert.Equal(AdoServer.Of(server.Port, pooled: true) + ";pool_max=1;pool_check=true", source.ConnectionString);
        for (int i = 0; i < 2; i++)
        {
            await using TurborelConnection connection = source.CreateConnection();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            using var command = new TurborelCommand("SET SCHEMA S", connection);
            Assert.Equal(-1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        await using (DbConnection plain = await source.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal(ConnectionState.Open, plain.State);
        }

        Assert.Equal((1, (1, 1)), (server.Connections, source.Pool!.Counts));
        Assert.Equal(["SET SCHEMA S", "W", "G", "SET SCHEMA S", "W", "G", "W"], [.. server.Requests]);
        using var unpooled = TurborelDataSource.Create(AdoServer.Of(server.Port));
        Assert.Null(unpooled.Pool);
        using DbConnection opened = ((DbDataSource)unpooled).OpenConnection();
        Assert.Equal(ConnectionState.Open, opened.State);
        await using var nowhere = TurborelDataSource.Create("host=127.0.0.1:1;user=ann;tls_mode=disable;passfile=/nonexistent;connect_timeout=1;pooling=false");
        Assert.Equal("08006", (await Assert.ThrowsAsync<TurborelException>(async () => await nowhere.OpenConnectionAsync(TestContext.Current.CancellationToken))).SqlState);
    }

    /// <summary>The factory makes each of the driver's objects, under its invariant name.</summary>
    [Fact]
    [Verifies("[DESIGN] D2")]
    public void TheFactoryMakesTheDriversObjects()
    {
        TurborelFactory factory = TurborelFactory.Instance;
        DbProviderFactories.RegisterFactory(TurborelFactory.InvariantName, factory);
        Assert.Same(factory, DbProviderFactories.GetFactory("Turborel.Data"));
        using DbConnection connection = factory.CreateConnection();
        using DbCommand command = factory.CreateCommand();
        using DbBatch batch = factory.CreateBatch();
        using DbDataSource source = factory.CreateDataSource("user=ann;pooling=false");
        Assert.Equal(
            (typeof(TurborelConnection), typeof(TurborelCommand), typeof(TurborelParameter), typeof(TurborelConnectionStringBuilder), typeof(TurborelBatch), typeof(TurborelBatchCommand), typeof(TurborelDataSource)),
            (connection.GetType(), command.GetType(), factory.CreateParameter().GetType(), factory.CreateConnectionStringBuilder().GetType(), batch.GetType(), factory.CreateBatchCommand().GetType(), source.GetType()));
        Assert.Equal((true, false, true), (factory.CanCreateBatch, factory.CanCreateDataSourceEnumerator, connection.CanCreateBatch));
        Assert.Same(factory, DbProviderFactories.GetFactory(connection));
    }

    /// <summary>
    /// A parameter's type is as told, else as its value says; it is an input
    /// alone; the collection keeps the driver's parameters in order, found by
    /// place or name.
    /// </summary>
    [Fact]
    [Verifies("[DESIGN] D17")]
    [Verifies("[DESIGN] D18")]
    public void ParametersAreKeptInOrder()
    {
        object[] values = ["s", 'c', 1, 1L, (short)1, (byte)1, true, 1m, 1.0, 1f, new byte[1], Guid.Empty, DateTime.MinValue, DateTimeOffset.MinValue, DateOnly.MinValue, TimeOnly.MinValue, TimeSpan.Zero, new object()];
        DbType[] types = [DbType.String, DbType.String, DbType.Int32, DbType.Int64, DbType.Int16, DbType.Byte, DbType.Boolean, DbType.Decimal, DbType.Double, DbType.Single, DbType.Binary, DbType.Guid, DbType.DateTime2, DbType.DateTimeOffset, DbType.Date, DbType.Time, DbType.Time, DbType.Object];
        Assert.Equal(types, values.Select(v => new TurborelParameter("p", v).DbType));
        var told = new TurborelParameter { DbType = DbType.AnsiString, IsNullable = true, Size = 4, SourceColumn = "C", SourceColumnNullMapping = true };
        Assert.Equal((DbType.AnsiString, string.Empty, ParameterDirection.Input), (told.DbType, told.ParameterName, told.Direction));
        told.ResetDbType();
        Assert.Equal(DbType.Object, told.DbType);

        using var command = new TurborelCommand();
        TurborelParameterCollection parameters = command.Parameters;
        _ = parameters.AddWithValue("a", 1);
        Assert.Equal(1, parameters.Add(new TurborelParameter("b", 2)));
        parameters.AddRange(new[] { new TurborelParameter("c", 3) });
        parameters.Insert(0, new TurborelParameter("z", 0));
        Assert.Equal((4, 1, true, true, false), (parameters.Count, parameters.IndexOf("a"), parameters.Contains("b"), parameters.Contains(parameters[0]), parameters.Contains("none")));
        parameters["b"] = new TurborelParameter("b", 20);
        parameters[0] = new TurborelParameter("y", 0);
        Assert.Equal((20, 2), (parameters["b"].Value, parameters.IndexOf(parameters["b"])));
        parameters.RemoveAt("y");
        parameters.Remove(parameters[0]);
        parameters.RemoveAt(0);
        var copied = new TurborelParameter[1];
        parameters.CopyTo(copied, 0);
        Assert.Equal(("c", 1, -1), (copied[0].ParameterName, parameters.Cast<object>().Count(), parameters.IndexOf("x")));
        Assert.NotNull(parameters.SyncRoot);
        foreach (Action misuse in new Action[]
        {
            () => parameters.Add("not a parameter"), () => _ = parameters["none"], () => _ = parameters[9], () => parameters.RemoveAt("none"),
            () => parameters.Insert(9, new TurborelParameter()), () => parameters.RemoveAt(-1), () => parameters[9] = new TurborelParameter(),
        })
        {
            Assert.Equal("TB910", Assert.Throws<TurborelException>(misuse).SqlState);
        }

        parameters[0].Direction = ParameterDirection.Output;
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => parameters.Values()).SqlState);
        parameters.Clear();
        Assert.Empty(parameters.Values());
        Assert.IsType<TurborelParameter>(command.CreateParameter());
    }

    /// <summary>A batch keeps the driver's commands in order, a place past them refused, TB910.</summary>
    [Fact]
    [Verifies("[DESIGN] D17")]
    public void BatchCommandsAreKeptInOrder()
    {
        using var batch = new TurborelBatch();
        TurborelBatchCommandCollection commands = batch.BatchCommands;
        var first = new TurborelBatchCommand("VALUES 1");
        commands.Add(first);
        commands.Insert(0, new TurborelBatchCommand { CommandText = "VALUES 0" });
        Assert.Equal((2, false, 1, true, -1, CommandType.Text), (commands.Count, commands.IsReadOnly, commands.IndexOf(first), commands.Contains(first), first.RecordsAffected, first.CommandType));
        var copied = new DbBatchCommand[3];
        commands.CopyTo(copied, 1);
        Assert.Same(first, copied[2]);
        ((DbBatchCommandCollection)commands)[1] = new TurborelBatchCommand("VALUES 2");
        Assert.Equal("VALUES 2", ((DbBatchCommandCollection)commands)[1].CommandText);
        Assert.True(commands.Remove(commands[1]));
        commands.RemoveAt(0);
        Assert.Empty(commands);
        Assert.IsType<TurborelBatchCommand>(TurborelFactory.Instance.CreateBatchCommand());
        foreach (Action misuse in new Action[]
        {
            () => _ = commands[0], () => commands.RemoveAt(0), () => commands.Insert(1, first), () => commands.CopyTo(new DbBatchCommand[1], 2),
            () => commands.Add(new OtherBatchCommand()),
        })
        {
            Assert.Equal("TB910", Assert.Throws<TurborelException>(misuse).SqlState);
        }

        commands.Clear();
        Assert.Equal((false, -1), (commands.Contains(new OtherBatchCommand()), commands.IndexOf(new OtherBatchCommand())));
    }

    /// <summary>Each misuse of a connection, command or batch is refused before anything is sent, TB910.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D17")]
    public async Task MisusesAreRefused()
    {
        await using var server = ManyServer.Start();
        await using var connection = new TurborelConnection();
        Assert.Equal((string.Empty, string.Empty), (connection.ConnectionString, connection.DataSource));
        using var noConnection = new TurborelCommand("VALUES 1");
        using var noBatch = new TurborelBatch();
        using TurborelCommand closed = connection.CreateCommand();
        foreach (Func<Task> misuse in new Func<Task>[]
        {
            () => connection.OpenAsync(TestContext.Current.CancellationToken),
            () => noConnection.ExecuteNonQueryAsync(TestContext.Current.CancellationToken),
            () => noBatch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken),
            () => Task.Run(() => connection.ChangeDatabase("OTHER"), TestContext.Current.CancellationToken),
            () => closed.ExecuteScalarAsync(TestContext.Current.CancellationToken),
        })
        {
            Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(misuse)).SqlState);
        }

        connection.ConnectionString = AdoServer.Of(server.Port);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => connection.ConnectionString = "user=bob").SqlState);
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => connection.OpenAsync(TestContext.Current.CancellationToken))).SqlState);
        using var procedure = new TurborelCommand("P", connection) { CommandType = CommandType.StoredProcedure };
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => procedure.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
        using DbCommand plain = connection.CreateCommand();
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => plain.Connection = new OtherConnection()).SqlState);
        foreach (Action refused in new Action[]
        {
            () => connection.GetSchema(), () => connection.GetSchema("Tables"), () => connection.GetSchema("Tables", []), () => connection.EnlistTransaction(null),
        })
        {
            Assert.Equal("TB910", Assert.Throws<TurborelException>(refused).SqlState);
        }

        await connection.CloseAsync();
        await connection.CloseAsync();
        Assert.Null(connection.Native);
    }

    /// <summary>
    /// The synchronous members, and ADO.NET's base classes' own, wait on the
    /// same tasks and give the driver's types; a transaction disposed rolls
    /// back; a connection's data source names a socket by its path.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D15")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task BaseAndSynchronousMembersWaitOnTheSameTasks()
    {
        await using var server = ManyServer.Start();
        DbDataSource source = TurborelDataSource.Create(AdoServer.Of(server.Port));
        await using (DbConnection connection = await source.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "WORK";
            command.Connection = connection;
            command.Transaction = null;
            Assert.IsType<TurborelParameter>(command.CreateParameter());
            using (DbDataReader reader = command.ExecuteReader())
            {
                Assert.Equal(0, reader.FieldCount);
            }

            await using (DbDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            {
                Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
            }

            using DbTransaction transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            Assert.Same(transaction, command.Transaction);
            transaction.Rollback();
            command.Transaction = null;
            using DbTransaction disposed = connection.BeginTransaction(IsolationLevel.Unspecified);
            Assert.Equal(-1, command.ExecuteNonQuery());
            disposed.Dispose();
            await BatchMembersAsync(connection);
            Assert.Equal("TB910", Assert.Throws<TurborelException>(() => command.Connection = new OtherConnection()).SqlState);
        }

        Assert.Equal(["WORK", "WORK", "ROLLBACK", "WORK", "ROLLBACK", "WORK", "WORK", "WORK", "WORK", "WORK", "X"], server.Requests.Where(r => r != "SAVEPOINT TURBOREL_PIPELINE_1" && r != "COMMIT"));
        source.Dispose();
        using var socket = new TurborelConnection("host=/run/turborel/sock;user=ann;pooling=false");
        Assert.Equal("/run/turborel/sock", socket.DataSource);
        using var unpooled = new TurborelCommand();
        unpooled.Cancel();
        using var idle = new TurborelBatch();
        idle.Cancel();
    }

    /// <summary>
    /// A transaction is refused while a reader is open, and once it is not
    /// its connection's; a data source's command and batch each open a
    /// connection of the source and close it with their reader; a batch
    /// told to close its connection does; parameters copy only to an array
    /// that holds them; a setting not set reads as null.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D17")]
    [Verifies("[DESIGN] D19")]
    public async Task ReadersTransactionsAndSourcesKeepTheirOrder()
    {
        await using var server = ManyServer.Start();
        await using var source = TurborelDataSource.Create(AdoServer.Of(server.Port));
        await using (TurborelConnection connection = await source.OpenConnectionAsync(TestContext.Current.CancellationToken))
        {
            using TurborelCommand work = connection.CreateCommand();
            work.CommandText = "WORK";
            TurborelTransaction transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken) as TurborelTransaction ?? throw new InvalidOperationException();
            await using (TurborelDataReader open = await work.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
            {
                Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken))).SqlState);
                Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(async () => await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))).SqlState);
            }

            await connection.CloseAsync();
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => transaction.CommitAsync(TestContext.Current.CancellationToken))).SqlState);
            await using TurborelBatch closing = connection.CreateBatch();
            closing.BatchCommands.Add(new TurborelBatchCommand("WORK"));
            await using (TurborelDataReader reader = await closing.ExecuteReaderAsync(CommandBehavior.CloseConnection, TestContext.Current.CancellationToken))
            {
                Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
            }

            Assert.Equal(ConnectionState.Closed, connection.State);
        }

        int before = server.Connections;
        using TurborelCommand sourced = source.CreateCommand("VALUES 1");
        Assert.Equal(-1, await sourced.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => sourced.PrepareAsync(TestContext.Current.CancellationToken))).SqlState);
        using TurborelBatch batched = source.CreateBatch();
        batched.BatchCommands.Add(new TurborelBatchCommand("VALUES 2"));
        Assert.Equal(-1, await batched.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        using DbCommand plain = ((DbDataSource)source).CreateCommand("VALUES 3");
        Assert.IsType<TurborelCommand>(plain);
        using DbBatch plainBatch = ((DbDataSource)source).CreateBatch();
        Assert.IsType<TurborelBatch>(plainBatch);
        Assert.Equal(before + 2, server.Connections);

        using var holder = new TurborelCommand();
        TurborelParameterCollection parameters = holder.Parameters;
        _ = parameters.AddWithValue("a", 1);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => parameters.CopyTo(new string[1], 0)).SqlState);
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => parameters.CopyTo(new object[1], 1)).SqlState);
        Assert.Null(new TurborelConnectionStringBuilder()["host"]);
    }

    /// <summary>The driver's error is ADO.NET's DbException, its SQLSTATE and SQLCODE carried as ADO.NET names them.</summary>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D9")]
    public void TheErrorIsADbException()
    {
        using var connection = new TurborelConnection();
        DbException failure = Assert.Throws<TurborelException>(() => connection.ChangeDatabase("OTHER"));
        Assert.Equal(("TB910", -910, false), (failure.SqlState, failure.ErrorCode, failure.IsTransient));
    }

    // A batch's synchronous members and its base class's, over a quiet server.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    private static async Task BatchMembersAsync(DbConnection connection)
    {
        using DbBatch batch = connection.CreateBatch();
        batch.Connection = connection;
        batch.Transaction = null;
        batch.BatchCommands.Add(new TurborelBatchCommand("WORK"));
        batch.Prepare();
        await batch.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal((-1, null, 0), (batch.ExecuteNonQuery(), batch.ExecuteScalar(), batch.Timeout));
        Assert.Null(await batch.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        using (DbDataReader reader = batch.ExecuteReader())
        {
            Assert.False(reader.Read());
        }

        await using (DbDataReader reader = await batch.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            Assert.False(reader.HasRows);
        }
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => batch.Connection = new OtherConnection()).SqlState);
    }

    // A batch command of another provider's.
    private sealed class OtherBatchCommand : DbBatchCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override CommandType CommandType { get; set; }

        public override int RecordsAffected => 0;

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
    }

    // A connection of another provider's.
    private sealed class OtherConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
