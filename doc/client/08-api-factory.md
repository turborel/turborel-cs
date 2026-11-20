# TurborelFactory

[Contents](README.md)

## NAME

`TurborelFactory` and `TurborelConnectionStringBuilder`: the provider
factory ADO.NET's tools make the driver's objects by, and the connection
string it reads.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelFactory : DbProviderFactory
{
    public const string InvariantName = "Turborel.Data";
    public static TurborelFactory Instance { get; }
    // CreateConnection, CreateCommand, CreateParameter, CreateConnectionStringBuilder,
    // CreateBatch, CreateBatchCommand and CreateDataSource make the driver's types.
}

public sealed class TurborelConnectionStringBuilder : DbConnectionStringBuilder
{
    public const string Pooling = "pooling";
    public const string PoolMax = "pool_max";
    public const string PoolMaxIdle = "pool_max_idle";
    public const string PoolIdleTimeout = "pool_idle_timeout";
    public const string PoolMaxLifetime = "pool_max_lifetime";
    public const string PoolWait = "pool_wait";
    public const string PoolCheck = "pool_check";

    public TurborelConnectionStringBuilder();
    public TurborelConnectionStringBuilder(string? connectionString);
    public override object this[string keyword] { get; set; }
    public ConnectionSettings ToSettings();
    public PoolConfig? ToPoolConfig();
}
```

## DESCRIPTION

A program registers the factory by its invariant name,
`DbProviderFactories.RegisterFactory("Turborel.Data",
TurborelFactory.Instance)`, so a tool that names a provider by it makes
the driver's connections, commands, parameters, batches and data sources.

A connection string is key=value pairs, separated by semicolons, of the
connection settings by the names a URI's query gives them
([Appendix C: Settings](../appendix/c-settings.md)), any case, and of
the pool's:

| Key | `PoolConfig` | Value |
|---|---|---|
| `pooling` | | `true` or `false`, any case: whether connections are pooled; `true` |
| `pool_max` | `Max` | the most connections made at once |
| `pool_max_idle` | `MaxIdle` | the most kept idle |
| `pool_idle_timeout` | `IdleTimeout` | whole seconds one may stay idle; 0 for no limit |
| `pool_max_lifetime` | `MaxLifetime` | whole seconds one lives; 0 for no limit |
| `pool_wait` | `Wait` | whole seconds an open waits for one to come back |
| `pool_check` | `Check` | `true` or `false`, any case: whether an idle one is checked before it is handed out |

A key not given takes its default. A setting's value is read as the
settings read it ([Settings](26-settings.md)); a `bool` set through the
builder's indexer is written `true` or `false`, as they write one. A
whole `turborel://` URI is taken as a connection string too (RULES.md
D19). `ToSettings` gives the
settings, laid over the environment and the service file as
`ConnectionSettings.Resolve` lays them; `ToPoolConfig` the pool's
configuration, or null if `pooling` is `false`.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08001 | -919 | A key that names no setting; a URI, or a connection string, that cannot be read; a pool's value not a whole number, or `pooling` or `pool_check` neither `true` nor `false` |

## EXAMPLES

```csharp
using System.Data.Common;
using Turborel.Data;

DbProviderFactories.RegisterFactory(TurborelFactory.InvariantName, TurborelFactory.Instance);
DbProviderFactory factory = DbProviderFactories.GetFactory("Turborel.Data");

DbConnectionStringBuilder builder = factory.CreateConnectionStringBuilder()!;
builder["host"] = "localhost:17435";
builder["user"] = "ann";
builder["pool_max"] = 4;
Console.WriteLine(builder.ConnectionString);

await using DbConnection connection = factory.CreateConnection()!;
connection.ConnectionString = builder.ConnectionString;
await connection.OpenAsync();
await using DbCommand command = connection.CreateCommand();
command.CommandText = "SELECT COUNT(*) FROM TEAM";
Console.WriteLine($"{connection.GetType().Name}: {await command.ExecuteScalarAsync()} members");

try
{
    builder["colour"] = "red";
}
catch (TurborelException e)
{
    Console.WriteLine($"SQLSTATE {e.SqlState}: {e.Message}");
}

try
{
    connection.ChangeDatabase("OTHER");
}
catch (DbException e)
{
    Console.WriteLine($"SQLSTATE {e.SqlState}: {e.Message}");
}
```

```
host=localhost:17435;user=ann;pool_max=4
TurborelConnection: 6 members
SQLSTATE 08001: [DESIGN] colour is no connection setting
SQLSTATE TB910: [DESIGN] a turborel server serves one database
```

## STANDARD

ADO.NET's `DbProviderFactory`, `DbProviderFactories` and
`DbConnectionStringBuilder`, of `System.Data.Common`; RULES.md D2 and
D19. [CLIENT The connection URI] and [CLIENT Where settings come from]
of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[TurborelDataSource](01-api-data-source.md), [Settings](26-settings.md),
[Pools](28-pools.md), [Appendix C: Settings](../appendix/c-settings.md).
