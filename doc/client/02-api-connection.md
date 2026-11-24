# TurborelConnection

[Contents](README.md)

## NAME

`TurborelConnection`: one session of a server's, opened from its data
source and closed back to it.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelConnection : DbConnection
{
    public TurborelConnection();
    public TurborelConnection(string connectionString);

    public override string ConnectionString { get; set; }
    public override string Database { get; }
    public override string DataSource { get; }
    public override string ServerVersion { get; }
    public override ConnectionState State { get; }
    public override bool CanCreateBatch { get; }
    public NativeConnection? Native { get; }
    public TurborelDebugger Debugger { get; }       // (extension)

    public override Task OpenAsync(CancellationToken cancellationToken);
    public override void Open();
    public override Task CloseAsync();
    public override void Close();
    public override void ChangeDatabase(string databaseName);

    public new TurborelCommand CreateCommand();
    public new TurborelBatch CreateBatch();
    public new TurborelTransaction BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Unspecified);
    // BeginTransactionAsync, from DbConnection, gives a TurborelTransaction.
}
```

## DESCRIPTION

A connection is made by its data source's `CreateConnection`, or of a
connection string, whose data source it shares with every connection of
the same string ([TurborelDataSource](01-api-data-source.md)).
`ConnectionString` is set while it is closed.

`OpenAsync` takes a connection from the pool, or makes one by the
settings, as [Connections](21-connections.md) connects; `CloseAsync`
closes its reader, if one is open, then gives it back to the pool, its
transaction rolled back and its session reset, or, unpooled, rolls back
its transaction and ends it. Disposing it closes it. `State` is `Closed`,
`Connecting` or `Open`.

Outside a transaction the session is under autocommit: each statement is
its own transaction, committed as it succeeds and rolled back as it
fails. `BeginTransaction` turns autocommit off until the transaction it
gives ends ([TurborelTransaction](06-api-transaction.md)). A connection
runs one statement at a time: while a reader is open on it, it takes no
other command (RULES.md D20).

`ServerVersion` is the server's product, as `turborel 0.0.0 (alpha)`;
`Database` the catalog the server told, empty if it told none;
`DataSource` the servers the settings name, as `host:port` or a socket's
path. A turborel server serves one database, so `ChangeDatabase` is
refused. `Native` is the low-level connection beneath, for what ADO.NET
has no place for: notices, the session's status, large objects by
locator, copy, import and export ([Connections](21-connections.md)). It
is null while the connection is closed. `Debugger` is the connection's
routine debugger *(extension)*, as ADO.NET has none
([Debugging Routines](31-debugging.md)); each of its calls runs on the
open connection, refused while it is closed or a reader is open on it.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the connection's) | | `OpenAsync` cannot connect ([Connections](21-connections.md)) |
| TB910 | -910 | Opened while open, or with no connection string; its string set while it is open; `ChangeDatabase`; `GetSchema`, as the driver gives no schema collections, INFORMATION_SCHEMA describing the database; `EnlistTransaction`, as it takes part in no `System.Transactions` transaction; a transaction begun while one is; a command run on it closed; a call of its `Debugger` while it is closed or a reader is open on it |

## EXAMPLES

See [TurborelDataSource](01-api-data-source.md), and
[Transactions](../user/05-transactions.md) of the User's Guide.

## STANDARD

ADO.NET's `DbConnection`, of `System.Data.Common`; RULES.md D2, D17,
D20 and D23. [CLIENT Connecting] and [CLIENT Running statements] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[TurborelDataSource](01-api-data-source.md),
[TurborelCommand](03-api-command.md),
[TurborelTransaction](06-api-transaction.md),
[Connections](21-connections.md),
[Debugging Routines](31-debugging.md).
