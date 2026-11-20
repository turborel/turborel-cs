# Statements and Cursors

[Contents](README.md)

## NAME

`NativeConnection`'s statements: direct statements and their outcomes,
rows streamed to a sink, several statements in one request, prepared
statements, cursors and numbered cursors, and what a completion tells.

## SYNOPSIS

```csharp
namespace Turborel.Data.Session;

public sealed partial class NativeConnection
{
    public bool Autocommit { get; set; }
    public string? Warning { get; }

    public Task<Outcome> QueryAsync(string sql, CancellationToken cancellationToken = default);
    public Task<Streamed> QueryIntoAsync(string sql, IRowSink sink, CancellationToken cancellationToken = default);
    public Task<Ran> QueryManyAsync(string sql, CancellationToken cancellationToken = default);

    public Task<PreparedStatement> PrepareAsync(string sql, CancellationToken cancellationToken = default);
    public Task<Outcome> ExecuteAsync(PreparedStatement statement, IReadOnlyList<object?> values,
        CancellationToken cancellationToken = default);
    public Task<Batch> OpenCursorAsync(PreparedStatement statement, IReadOnlyList<object?> values, uint rows,
        uint cursor = 0, CancellationToken cancellationToken = default);
    public Task<Batch> FetchAsync(PreparedStatement statement, uint rows, uint cursor = 0,
        CancellationToken cancellationToken = default);
    public Task CloseCursorAsync(PreparedStatement statement, uint cursor = 0, CancellationToken cancellationToken = default);
    public Task<PreparedStatement> DescribeAsync(PreparedStatement statement, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<Column>> DescribeCursorAsync(PreparedStatement statement, uint cursor = 0,
        CancellationToken cancellationToken = default);
    public Task DeallocateAsync(PreparedStatement statement, CancellationToken cancellationToken = default);

    public ulong? RowsAffected { get; }
    public IReadOnlyList<string> LastGenerated { get; }
    public Timing? ServerTime { get; }
    public ulong? CommitPosition { get; }
    public IReadOnlyList<TurborelException> Conditions { get; }
    public IReadOnlyList<TurborelException> TakeConditions();
}

public abstract record Outcome;
public sealed record RowsOutcome(IReadOnlyList<Column> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows) : Outcome;
public sealed record DoneOutcome(Completed Completion) : Outcome;
public sealed record Streamed(ulong? RowsSent, Completed? Completion);
public sealed record Batch(IReadOnlyList<IReadOnlyList<object?>> Rows, bool More);
public sealed record Ran(IReadOnlyList<Outcome> Outcomes, TurborelException? Failure);
public interface IRowSink
{
    void Columns(IReadOnlyList<Column> columns);
    void Rows(IReadOnlyList<IReadOnlyList<object?>> rows);
}
public sealed class PreparedStatement
{
    public IReadOnlyList<Parameter> Parameters { get; }
    public IReadOnlyList<Column> Columns { get; }
}
public sealed class Completed
{
    public int SqlCode { get; }
    public string SqlState { get; }
    public bool IsWarning { get; }
    public bool InTransaction { get; }
    public ulong? Position { get; }
    public ulong? RowCount { get; }
    public IReadOnlyList<string> Generated { get; }
    public IReadOnlyList<TurborelException> Conditions { get; }
    public Timing? Time { get; }
}

namespace Turborel.Data.Protocol;

public sealed record Column(string? Name, DataType Type, bool NotNull);
public sealed record Parameter(bool Varying, DataType Type);
public readonly record struct Timing(ulong Total, ulong Queued, ulong Locks);
```

## DESCRIPTION

### Direct statements

`QueryAsync` runs one direct SQL statement by Query. A query is answered
by its Description, its rows in batches and its Complete, given whole as
a `RowsOutcome`; another statement by its Complete, a `DoneOutcome`,
SQLCODE 0, or 100 with SQLSTATE 02000 for a change of no row. Each
Notice and Status before the completion is taken on the way
([Connections](21-connections.md)). `QueryIntoAsync` gives a query's
columns, then each batch of rows as it comes, to `sink`, and returns how
many rows it sent, or another statement's completion. `QueryManyAsync`,
once `multi-statement` is granted, runs several statements in one
request, each in order, and gives each outcome and the failure of the
one that stopped the rest; text of no statement completes once.

With `Autocommit`, each statement is its own transaction, committed as
it succeeds and rolled back as it fails, within the request.
`InTransaction` says whether a transaction is active after each.
`Warning` is the SQLSTATE of the warning the last statement completed
with, of a query once all its rows were read.

### Prepared statements

`PrepareAsync` prepares a statement whose dynamic parameters are written
`?`, numbering it for the connection. `Parameters` are its inputs, in the
order of their `?` marks: a parameter is `Varying` where the server gives
it CHARACTER VARYING of the longest length, as it does of one whose type
the statement does not decide. `Columns` are a query's, or a CALL's
output parameters', one for each `?` of an OUT or INOUT parameter; an
INOUT `?` is both a parameter and a column. The statement is valid
across transactions until it is deallocated or the session reset.

`ExecuteAsync` runs it with one value for each parameter, of the types
[Values](23-values.md) lists: a query's rows, all of them; a CALL's one
row of its output parameters; another statement's completion.
`OpenCursorAsync` opens a query's cursor and gives at most `rows` rows,
all if 0, and whether it has more; `FetchAsync` gives the next;
`CloseCursorAsync` closes it, and the last row's batch closes it too.
Under `Autocommit`, with `fetch-ends` granted, the cursor's transaction
ends as the cursor does: committed by the fetch of its last row or its
close, rolled back by a failing fetch; without it, a cursor left open
keeps its transaction. With `cursors` granted, `cursor` numbers one of
several cursors of the statement open at once; without it, only 0 is
taken. With `describe` granted, `DescribeAsync` describes the statement
again, and `DescribeCursorAsync` an open cursor's columns.
`DeallocateAsync` destroys the statement.

### What a completion tells

`Completed` is a statement's completion. What its tail tells depends on
what was granted: `RowCount` and `Time` with `row-counts`, the rows a
statement changed or a query returned and the server's time in
microseconds, total, queued for a worker before it, and waiting for locks
within it; `Generated` with `generated-values`, each identity value an
INSERT generated, as SQL writes it, in the order of its rows;
`Position` with `commit-position`, the log position of the commit it
made; and `Conditions` with `conditions`, each condition it raised.
The connection keeps the last statement's: `RowsAffected`,
`LastGenerated`, `ServerTime`, `Conditions`, and `CommitPosition`, the
last commit's of any statement. `TakeConditions` takes the conditions,
so each is told once.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the statement's) | | The server's failure of the statement, whole ([Errors](30-errors.md)) |
| 07001 | -921 | The values are not one for each parameter |
| 24000 | -501 | A cursor opened while open, fetched or closed while not open, or its statement deallocated while it is open |
| 26000 | -921 | A statement that is no more, as after Reset or Deallocate |
| 22000 | -301 | A value to send is of no type the protocol carries |
| 08006 | -919 | A method of a capability not granted, before anything is sent, the connection kept; a reply out of place, or the connection lost |
| TB910 | -910 | A null where a value is needed |

## EXAMPLES

See [Running Queries](../user/03-queries.md) and
[Prepared Statements](../user/04-prepared-statements.md).

## STANDARD

[PROTOCOL Query 'Q'], [PROTOCOL Prepare 'P'], [PROTOCOL Execute 'E'],
[PROTOCOL Fetch 'F'], [PROTOCOL Close 'C'], [PROTOCOL Deallocate 'D'],
[PROTOCOL Describe 'Y'], [PROTOCOL Prepared 'P'], [PROTOCOL Complete 'K']
and [PROTOCOL Conversations] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Running statements], [CLIENT Prepared statements],
[CLIENT Controlling the session] and [CLIENT What the server tells] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[Connections](21-connections.md), [Values](23-values.md),
[Errors](30-errors.md).
