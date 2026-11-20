# Pools

[Contents](README.md)

## NAME

`ConnectionPool`: a bounded set of connections a program's tasks share,
handed out checked and given back reset.

## SYNOPSIS

```csharp
namespace Turborel.Data.Pooling;

public interface IConnectionSource
{
    Task<NativeConnection> ConnectAsync(CancellationToken cancellationToken = default);
}
public sealed record PoolConfig
{
    public const int DefaultMax = 10;
    public static TimeSpan DefaultIdleTimeout { get; }
    public static TimeSpan DefaultMaxLifetime { get; }
    public static TimeSpan DefaultWait { get; }
    public int Max { get; init; }
    public int MaxIdle { get; init; }
    public TimeSpan? IdleTimeout { get; init; }
    public TimeSpan? MaxLifetime { get; init; }
    public TimeSpan Wait { get; init; }
    public bool Check { get; init; }
    public IReadOnlyList<string> Reset { get; init; }
}
public sealed class ConnectionPool : IAsyncDisposable
{
    public ConnectionPool(IConnectionSource source, PoolConfig? config = null);
    public ConnectionPool(Func<CancellationToken, Task<NativeConnection>> connect, PoolConfig? config = null);
    public PoolConfig Config { get; }
    public (int Made, int Idle) Counts { get; }
    public Task<PooledConnection> GetAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
public sealed class PooledConnection : IAsyncDisposable
{
    public NativeConnection Connection { get; }
    public ValueTask DisposeAsync();
}
```

## DESCRIPTION

A pool is made of what makes its connections: `ConnectionSettings`,
which is an `IConnectionSource`, or a function of the program's.
`GetAsync` hands out an idle connection that answers, or makes one while
fewer than `Max` are made, or waits for one to come back, at most
`Wait`. Each wait is the waiting task's own; the pool starts no thread.

| `PoolConfig` | What it says | Default |
|---|---|---|
| `Max` | The most connections made at once, handed out and idle | 10 |
| `MaxIdle` | The most kept idle | 10 |
| `IdleTimeout` | How long one may stay idle; null for as long as it lives | 10 minutes |
| `MaxLifetime` | How long one lives from when it was made; null for no limit | 1 hour |
| `Wait` | How long `GetAsync` waits for one to come back | 30 seconds |
| `Check` | Whether an idle connection is checked to answer before it is handed out | yes |
| `Reset` | Statements run as each comes back, after the pool's own reset | none |

A `PooledConnection` gives its connection back as it is disposed, reset
to the session it logged in with, each wait of the reset at most 30
seconds: by the protocol's Reset, granted `reset`, then the `Reset`
statements, autocommit off. Its check is a Ping, granted `ping`. Of a
server that grants neither, the check is ROLLBACK, and the reset one
pipeline: its transaction rolled back, the statements it prepared, 256 at
most, closed and destroyed, its user and schema those of its login and
its time zone local, then the `Reset` statements; a setting a borrower
changed by SET stays as it set it unless `Reset` sets it back. A
connection lost, failing its reset, past its lifetime, or beyond
`MaxIdle` is closed. One idle past `IdleTimeout`, or failing its check,
is closed as `GetAsync` meets it, and another taken or made.
`DisposeAsync` of the pool closes its idle connections, and those handed
out as they come back.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08001 | -919 | `GetAsync`: no connection came back within `Wait` |
| (the source's) | | `GetAsync`: making a connection failed |
| TB910 | -910 | `Connection` of a connection given back |

## EXAMPLES

See [Pipelines and Pools](../user/08-pipelines-and-pools.md).

## STANDARD

[CLIENT Pools] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[Settings](26-settings.md), [Pipelines](27-pipelines.md),
[Appendix C: Settings](../appendix/c-settings.md).
