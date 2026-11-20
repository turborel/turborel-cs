# TurborelBatch

[Contents](README.md)

## NAME

`TurborelBatch`: several statements sent at once, over a pipeline,
stopping at the first that fails.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelBatch : DbBatch
{
    public new TurborelBatchCommandCollection BatchCommands { get; }
    public new TurborelConnection? Connection { get; set; }
    public new TurborelTransaction? Transaction { get; set; }
    public override int Timeout { get; set; }   // 0, no limit

    public new Task<TurborelDataReader> ExecuteReaderAsync(CommandBehavior behavior = CommandBehavior.Default,
        CancellationToken cancellationToken = default);
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default);
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default);
    public override void Cancel();
}

public sealed class TurborelBatchCommand : DbBatchCommand
{
    public TurborelBatchCommand();
    public TurborelBatchCommand(string commandText);
    public override string CommandText { get; set; }
    public new TurborelParameterCollection Parameters { get; }
    public override int RecordsAffected { get; }
}
```

## DESCRIPTION

A batch's statements are sent as one pipeline, a window of requests at a
time ([Pipelines](27-pipelines.md)). Each statement of parameters is
prepared first, before any statement runs, executed in the pipeline, and
deallocated after: one that fails to prepare stops the batch with none
run. Once it runs, the batch stops at its first failure: a savepoint is set before each
statement but the first, the statements after the one that failed are
undone or never sent, and its failure is thrown, those before it kept.
Its statements are of one transaction; outside a transaction of the
connection's, it is committed as the batch ends.

`ExecuteReaderAsync` runs the batch and gives a reader of each
statement's result in turn, by `NextResult`: a query's rows, or another
statement's rows changed; `CommandBehavior.CloseConnection` closes the
connection with it. A batch made by a data source's `CreateBatch` runs
on a connection of its own, opened from the source and closed with its
reader. `ExecuteNonQueryAsync` returns the rows the
statements changed, summed, or -1 if none counted any; each command's
`RecordsAffected` is its own, -1 of a query or of one not run.
`ExecuteScalarAsync` returns the first column of the first statement's
first row. A batch's results are read whole as it runs, so it suits
statements of few rows. `Timeout` cancels the statement running once its
seconds run out, as `Cancel` does.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the statement's) | | The first statement's failure, the statements after it skipped |
| TB920 | -920 | The batch cancelled, by `Cancel` or its timeout |
| TB910 | -910 | No connection, or a closed one; a reader open on the connection; a transaction not the connection's; a command other than a `TurborelBatchCommand` added, or a place not in the collection |

## EXAMPLES

See [Pipelines and Pools](../user/08-pipelines-and-pools.md) of the
User's Guide.

## STANDARD

ADO.NET's `DbBatch` and `DbBatchCommand`, of `System.Data.Common`;
RULES.md D2. [CLIENT Pipelines] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[TurborelCommand](03-api-command.md), [Pipelines](27-pipelines.md).
