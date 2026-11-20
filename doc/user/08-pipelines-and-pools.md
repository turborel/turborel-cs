# Pipelines and Pools

[Contents](README.md)

## Pipelines

A pipeline sends a connection's requests without waiting for each reply,
so a batch costs one round trip, not one a request. `Pipeline()` gives
one; `Query` and `Execute` queue requests; `RunAsync` writes them and
gives one outcome each, in order. The server answers each request alone
and in turn, so a failure is its request's own, and the requests after it
run. Requests are written a window of 64 KiB at a time, its replies read
before the next window is written. Under `Autocommit` each request is its
own transaction.

With `OnError` set to `PipelineOnError.Skip`, a batch stops at its first
failure: the client sets a savepoint before each request but the first,
the requests after a failure are rolled back to the savepoint after it,
those of a window not yet written are never sent, and each is
`PipedSkipped`. Such a batch is one transaction, committed as it ends
under autocommit if it began none.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE TABLE CREW (ID INTEGER PRIMARY KEY, NAME VARCHAR(10))");
await connection.QueryAsync("COMMIT");
connection.Autocommit = true;

PreparedStatement insert = await connection.PrepareAsync("INSERT INTO CREW VALUES (?, ?)");
Pipeline batch = connection.Pipeline()
    .Execute(insert, [new TurborelDecimal(1, 0), "Ann"])
    .Execute(insert, [new TurborelDecimal(1, 0), "Again"])
    .Execute(insert, [new TurborelDecimal(2, 0), "Bob"])
    .Query("SELECT COUNT(*) FROM CREW");
foreach (Piped outcome in await batch.RunAsync())
{
    Console.WriteLine(outcome switch
    {
        PipedDone { Outcome: RowsOutcome rows } => $"rows: {rows.Rows[0][0]}",
        PipedDone { Outcome: DoneOutcome done } => $"done: {done.Completion.RowCount} row",
        PipedFailed failed => $"failed: SQLSTATE {failed.Failure.SqlState}",
        _ => "skipped",
    });
}

Pipeline skipping = connection.Pipeline()
    .Execute(insert, [new TurborelDecimal(3, 0), "Cy"])
    .Execute(insert, [new TurborelDecimal(2, 0), "Again"])
    .Execute(insert, [new TurborelDecimal(4, 0), "Dee"]);
skipping.OnError = PipelineOnError.Skip;
Console.WriteLine(string.Join(", ", (await skipping.RunAsync()).Select(o => o.GetType().Name)));
```

```
done: 1 row
failed: SQLSTATE 23000
done: 1 row
rows: 2
PipedDone, PipedFailed, PipedSkipped
```

## Pools

A `ConnectionPool` keeps a bounded set of connections that a program's
tasks share. `GetAsync` hands out an idle connection that answers, or
makes one while fewer than `Max` are made, or waits for one to come back,
at most `Wait`. A `PooledConnection` goes back to the pool as it is
disposed, reset to the session it logged in with: by the protocol's
Reset, its transaction, prepared statements, cursors, temporary tables
and settings gone, its user kept, autocommit off, then the pool's
`Reset` statements. Its check, as it is handed out, is a Ping. A pool is
made of `ConnectionSettings`, or of any function that makes a
connection; it starts no thread.

```csharp
using Turborel.Data.Configuration;
using Turborel.Data.Pooling;
using Turborel.Data.Session;

ConnectionSettings settings = ConnectionSettings.FromUri("turborel://ann@127.0.0.1:17435/?tls_mode=disable");
await using var pool = new ConnectionPool(settings, new PoolConfig { Max = 2 });
async Task<string> Work(int n)
{
    await using PooledConnection lent = await pool.GetAsync();
    await lent.Connection.QueryAsync($"SET SESSION.TIME_ZONE = '+0{n}:00'");
    var rows = (RowsOutcome)await lent.Connection.QueryAsync("SELECT COUNT(*) FROM CREW");
    return $"worker {n}: {rows.Rows[0][0]} crew, zone {lent.Connection.Setting("SESSION.TIME_ZONE")}";
}

foreach (string line in await Task.WhenAll(Enumerable.Range(1, 5).Select(Work)))
{
    Console.WriteLine(line);
}

Console.WriteLine($"made and idle: {pool.Counts}");
await using PooledConnection after = await pool.GetAsync();
Console.WriteLine($"given back reset: zone {after.Connection.Setting("SESSION.TIME_ZONE")}");
```

```
worker 1: 3 crew, zone +01:00
worker 2: 3 crew, zone +02:00
worker 3: 3 crew, zone +03:00
worker 4: 3 crew, zone +04:00
worker 5: 3 crew, zone +05:00
made and idle: (2, 2)
given back reset: zone +00:00
```

## The SQL-agent

An `SqlAgent` holds a program's connections, each named, one current.
CONNECT, SET CONNECTION and DISCONNECT it runs itself, through an
`IConnector` the program gives, which makes a connection to the server a
CONNECT's address names, as its user; every other statement goes to the
current connection. A connection's transaction is its own, so the agent
refuses to leave an active one, SQLSTATE 0A001.

```csharp
using Turborel.Data.Agent;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

var agent = new SqlAgent(new Connector());
await agent.QueryAsync("CONNECT TO 'localhost:17435' AS 'crew' USER 'ann'");
await agent.QueryAsync("CONNECT TO 'localhost:17435' AS 'audit' USER 'ann'");
Console.WriteLine($"current: {agent.CurrentName}");
await agent.QueryAsync("SET CONNECTION 'crew'");
var rows = (RowsOutcome)await agent.QueryAsync("SELECT COUNT(*) FROM CREW");
Console.WriteLine($"{agent.CurrentName}: {rows.Rows[0][0]} crew");
await agent.QueryAsync("ROLLBACK");
await agent.QueryAsync("DISCONNECT ALL");
Console.WriteLine($"connections left: {(agent.CurrentConnection is null ? "none" : "some")}");

sealed class Connector : IConnector
{
    public Task<NativeConnection> ConnectAsync(string? server, string? user, CancellationToken cancellationToken = default)
    {
        string[] at = (server ?? "localhost:7422").Split(':');
        return NativeConnection.ConnectAsync(new TcpTarget(at[0], int.Parse(at[1])), user ?? "ann", new TrustMechanism(), cancellationToken: cancellationToken);
    }
}
```

```
current: AUDIT
CREW: 3 crew
connections left: none
```

## See also

[Pipelines](../client/27-pipelines.md), [Pools](../client/28-pools.md)
and [The SQL-agent](../client/29-sql-agent.md) in the API Reference.
