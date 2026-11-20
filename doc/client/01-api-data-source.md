# TurborelDataSource

[Contents](README.md)

## NAME

`TurborelDataSource`: the data source a program makes once, of a
connection string, and opens its connections from, pooled unless the
string says not.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelDataSource : DbDataSource
{
    public TurborelDataSource(string connectionString);
    public static TurborelDataSource Create(string connectionString);

    public override string ConnectionString { get; }
    public ConnectionSettings Settings { get; }
    public ConnectionPool? Pool { get; }

    public new TurborelConnection CreateConnection();
    public new TurborelCommand CreateCommand(string? commandText = null);
    public new TurborelBatch CreateBatch();
    public new TurborelConnection OpenConnection();
    public new ValueTask<TurborelConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
```

## DESCRIPTION

A data source is made of a connection string: key=value pairs of the
connection settings, by the names a URI's query gives them, and of the
pool's ([TurborelFactory](08-api-factory.md) describes
`TurborelConnectionStringBuilder`, which reads them); or a whole
`turborel://` URI. The settings are read once, as the source is made,
laid over the environment, the service file and the password file as
[Settings](26-settings.md) lays them, and kept as `Settings`.

Unless the string says `pooling=false`, the source keeps a pool,
`Pool`, of the configuration its `pool_` keys give
([Pools](28-pools.md)). `CreateConnection` gives a connection, closed;
`OpenConnectionAsync` gives one opened, from the pool if one is idle,
else newly made; `Create` is the constructor. `CreateCommand` and
`CreateBatch` give a command and a batch of no connection: each run
opens a connection of the source, closed with its reader. A connection closed goes
back to the pool, its transaction rolled back and its session reset;
unpooled, it ends. Disposing the source disposes its pool, ending every
idle connection.

A `TurborelConnection` made of a connection string shares the data source
of every other connection of the same string in the process, and its
pool, as ADO.NET's pooled providers do (RULES.md D19).

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08001 | -919 | A key that names no setting, a value that cannot be read, or no user named, as the source is made |
| (the connection's) | | A connection that cannot be opened ([Connections](21-connections.md)), as 08006 for a server that does not answer |

## EXAMPLES

```csharp
using Turborel.Data;

await using TurborelDataSource source = TurborelDataSource.Create("host=localhost:17435;user=ann");
await using TurborelConnection connection = await source.OpenConnectionAsync();
Console.WriteLine($"connected to {connection.ServerVersion}");

await using TurborelCommand command = connection.CreateCommand();
command.CommandText = "VALUES CAST(? AS VARCHAR(20)) || ', from turborel'";
command.Parameters.AddWithValue("greeting", "hello");
Console.WriteLine(await command.ExecuteScalarAsync());
```

```
connected to turborel 0.0.0 (alpha)
hello, from turborel
```

## STANDARD

ADO.NET's `DbDataSource`, of `System.Data.Common`; RULES.md D2 and D19.
[CLIENT Connecting by settings], [CLIENT Where settings come from] and
[CLIENT Pools] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[TurborelConnection](02-api-connection.md), [Settings](26-settings.md),
[Pools](28-pools.md), [Appendix C: Settings](../appendix/c-settings.md).
