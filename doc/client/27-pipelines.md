# Pipelines

[Contents](README.md)

## NAME

`Pipeline`: a connection's requests sent without waiting for each reply,
one outcome each, in order.

## SYNOPSIS

```csharp
namespace Turborel.Data.Session;

public sealed partial class NativeConnection
{
    public Pipeline Pipeline();
}
public sealed class Pipeline
{
    public const int Window = 65_536;
    public PipelineOnError OnError { get; set; }
    public int Count { get; }
    public Pipeline Query(string sql);
    public Pipeline Execute(PreparedStatement statement, IReadOnlyList<object?> values);
    public Task<IReadOnlyList<Piped>> RunAsync(CancellationToken cancellationToken = default);
}
public enum PipelineOnError { Continue, Skip }
public abstract record Piped;
public sealed record PipedDone(Outcome Outcome) : Piped;
public sealed record PipedFailed(TurborelException Failure) : Piped;
public sealed record PipedSkipped : Piped;
```

## DESCRIPTION

`Query` queues a statement, run as `QueryAsync` runs it; `Execute` an
execution of a prepared statement, as `ExecuteAsync` runs it, all of a
query's rows. `RunAsync` writes the requests a window of at most
`Window` bytes at a time, a request longer than that alone in its own,
and reads each window's replies before the next is written, so client
and server never each wait for the other to read. Each request has its
outcome, in the order queued: `PipedDone`, or `PipedFailed`, a failure
of its request alone, the requests after it run. Under `Autocommit` each
request is its own transaction.

Under `PipelineOnError.Skip` the batch stops at its first failure, the
requests before it kept: a savepoint, `SAVEPOINT TURBOREL_PIPELINE_n`, is
set before each request but the first; the requests after a failed one
are rolled back to the savepoint set after it, and those of a window not
yet written never sent; each is `PipedSkipped`. The first savepoint is
then released, and under autocommit, of a batch that began no
transaction, the batch is committed, no request of it having ended it.
What a rollback undoes is undone; a session's settings are not.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the server's) | | A request's failure, its own outcome |
| 08006 | -919 | The connection lost: `RunAsync` gives no outcome |
| (the server's) | | A skipping batch's savepoints that cannot be rolled back or released, or its commit |

## EXAMPLES

See [Pipelines and Pools](../user/08-pipelines-and-pools.md).

## STANDARD

[PROTOCOL Requests] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Pipelines] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[Statements and Cursors](22-statements.md), [Pools](28-pools.md).
