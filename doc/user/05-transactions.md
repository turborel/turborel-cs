# Transactions

[Contents](README.md)

A transaction begins with a session's first statement and lasts until
COMMIT or ROLLBACK. `InTransaction` tells whether one is active, as the
server said after the last statement. With `Autocommit`, each statement
is its own transaction, ended by the server with it. A cursor is read
within its transaction: under autocommit, once the server grants
`fetch-ends`, the transaction ends as the cursor does, committed by the
fetch of its last row or by its close, rolled back if a fetch fails. A
result that sent a large object's locator keeps its transaction, autocommit or
not, for the locator is valid within it alone.

`ResetAsync` returns the session to its state at login: its transaction
rolled back, told by notice 01T01, its settings, prepared statements,
cursors and temporary tables gone. `ChangeUserAsync` logs in again as
another user and renews the session the same way.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("UPDATE STAFF SET SALARY = SALARY * 2");
Console.WriteLine($"after UPDATE: in a transaction {connection.InTransaction}");
await connection.QueryAsync("ROLLBACK");
Console.WriteLine($"after ROLLBACK: in a transaction {connection.InTransaction}");

connection.Autocommit = true;
await connection.QueryAsync("UPDATE STAFF SET SALARY = SALARY + 1 WHERE NAME = 'Ann'");
Console.WriteLine($"under autocommit: in a transaction {connection.InTransaction}, commit at {connection.CommitPosition is not null}");

PreparedStatement list = await connection.PrepareAsync("SELECT NAME FROM STAFF");
await connection.OpenCursorAsync(list, [], rows: 1);
Console.WriteLine($"cursor open: in a transaction {connection.InTransaction}");
await connection.FetchAsync(list, rows: 0);
Console.WriteLine($"last row read: in a transaction {connection.InTransaction}");

connection.Autocommit = false;
await connection.QueryAsync("SET SESSION.TIME_ZONE = '+02:00'");
await connection.QueryAsync("UPDATE STAFF SET SALARY = 0");
Console.WriteLine($"time zone {connection.Setting("SESSION.TIME_ZONE")}, in a transaction {connection.InTransaction}");
await connection.ResetAsync();
TurborelNoticeLine(connection);
Console.WriteLine($"after Reset: time zone {connection.Setting("SESSION.TIME_ZONE")}, in a transaction {connection.InTransaction}");

static void TurborelNoticeLine(NativeConnection connection)
{
    foreach (Turborel.Data.TurborelNotice notice in connection.TakeNotices())
    {
        Console.WriteLine($"notice {notice.SqlState}: {notice.Message}");
    }
}
```

```
after UPDATE: in a transaction True
after ROLLBACK: in a transaction False
under autocommit: in a transaction False, commit at True
cursor open: in a transaction True
last row read: in a transaction False
time zone +02:00, in a transaction True
notice 01T01: [DESIGN] Reset rolled back the transaction that was active
after Reset: time zone +00:00, in a transaction False
```

Each session setting, and what else the server tells by Status, is kept
as the server last told it: `Setting` reads one with no round trip.
`CommitPosition` is the log position of the last commit, which a
replica's session reads after by REPLICATION.READ_AFTER.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.PingAsync();
connection.SetQueryAttributes([new("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]);
var traced = (RowsOutcome)await connection.QueryAsync("VALUES (QUERY_ATTRIBUTE('traceparent'))");
Console.WriteLine(traced.Rows[0][0]);
await connection.QueryAsync("COMMIT");
await connection.QueryAsync("CREATE USER CLERK PASSWORD 'clerk''s password'");
await connection.QueryAsync("COMMIT");
await connection.ChangeUserAsync("clerk", new TrustMechanism());
Console.WriteLine($"now {connection.Authorization}; owner {connection.Setting(StatusNames.Owner)}");
```

```
00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01
now CLERK; owner NO
```

`PingAsync` asks whether the server answers. `SetQueryAttributes` sets
the attributes each later statement carries, which SQL reads by
QUERY_ATTRIBUTE; `traceparent` is the statement's trace context.

## With ADO.NET

A `TurborelConnection` is under autocommit until `BeginTransaction`
begins a transaction, of the isolation level it names; the transaction's
`CommitAsync` or `RollbackAsync` ends it, and its savepoints are set,
rolled back to and released by name. A transaction disposed before it
ends is rolled back. As SQL makes it, a READ UNCOMMITTED transaction is
READ ONLY:

```csharp
using System.Data;
using Turborel.Data;

await using var connection = new TurborelConnection("host=localhost:17435;user=ann");
await connection.OpenAsync();

async Task RunAsync(string sql)
{
    await using var command = new TurborelCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task<object?> CountAsync()
{
    await using var command = new TurborelCommand("SELECT COUNT(*) FROM TEAM", connection);
    return await command.ExecuteScalarAsync();
}

await using (TurborelTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
{
    await RunAsync("INSERT INTO TEAM VALUES (4, 'Dee', DATE '2025-05-05', 4500.00)");
    await transaction.SaveAsync("after_dee");
    await RunAsync("INSERT INTO TEAM VALUES (5, 'Eve', DATE '2025-06-06', 4600.00)");
    await transaction.RollbackAsync("after_dee");
    await transaction.CommitAsync();
}

Console.WriteLine($"after the commit: {await CountAsync()} members");

await using (TurborelTransaction transaction = await connection.BeginTransactionAsync() as TurborelTransaction
    ?? throw new InvalidOperationException())
{
    await RunAsync("DELETE FROM TEAM");
    Console.WriteLine($"inside, deleted: {await CountAsync()} members");
}

Console.WriteLine($"disposed, rolled back: {await CountAsync()} members");

try
{
    await using TurborelTransaction readOnly = connection.BeginTransaction(IsolationLevel.ReadUncommitted);
    await RunAsync("DELETE FROM TEAM");
}
catch (TurborelException e)
{
    Console.WriteLine($"READ UNCOMMITTED: SQLSTATE {e.SqlState}, {e.Message}");
}
```

```
after the commit: 4 members
inside, deleted: 0 members
disposed, rolled back: 4 members
READ UNCOMMITTED: SQLSTATE 25000, the transaction is READ ONLY
```

[TurborelTransaction](../client/06-api-transaction.md) gives the SET
TRANSACTION each level sends.

## See also

[Connections](../client/21-connections.md) in the API Reference;
[Users and Authentication](https://github.com/turborel/turborel/blob/main/doc/operator/05-users.md)
in Turborel's manual.
