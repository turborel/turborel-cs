# TurborelCommand

[Contents](README.md)

## NAME

`TurborelCommand`: a statement to run on a connection, its dynamic
parameters written `?`, run directly or prepared, read by a reader or
as a scalar.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelCommand : DbCommand
{
    public TurborelCommand();
    public TurborelCommand(string commandText, TurborelConnection? connection = null);

    public override string CommandText { get; set; }
    public override int CommandTimeout { get; set; }        // 30
    public override CommandType CommandType { get; set; }   // Text
    public new TurborelConnection? Connection { get; set; }
    public new TurborelTransaction? Transaction { get; set; }
    public new TurborelParameterCollection Parameters { get; }

    public override Task PrepareAsync(CancellationToken cancellationToken = default);
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken);
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken);
    public new Task<TurborelDataReader> ExecuteReaderAsync(CommandBehavior behavior = CommandBehavior.Default,
        CancellationToken cancellationToken = default);
    public override void Cancel();

    // Prepare, ExecuteNonQuery, ExecuteScalar and ExecuteReader wait on the same tasks.
}
```

## DESCRIPTION

`CommandText` is a statement whose dynamic parameters are written `?`,
given by `Parameters` in the order of their marks; a parameter's name is
the program's own ([TurborelParameter](05-api-parameter.md)). A command
of no parameters, not prepared, runs as one Query: its text one statement
or, where the server grants `multi-statement`, several, split as the
server splits them, each statement's result read in turn by the reader's
`NextResult`. A command of parameters is prepared, executed and
deallocated. `PrepareAsync` prepares it once, so each run after executes
it with no Prepare; it stays prepared until its text or connection
changes, then deallocated as it next runs, and is prepared again on a
session renewed, as a pooled connection is given back and taken again. A
command made by a data source's `CreateCommand` runs on a connection of
its own, opened from the source and closed with its reader. A query executed opens a cursor, its rows fetched 1000 at a time
as the reader reads them (RULES.md D20).

`ExecuteReaderAsync` gives a reader of the statement's rows as they come
([TurborelDataReader](04-api-data-reader.md));
`CommandBehavior.CloseConnection` closes the connection with it.
`ExecuteNonQueryAsync` runs the statement, reading what it gives, and
returns the rows it changed, summed over a Query's statements, or -1 of
a query or a statement that counts none. `ExecuteScalarAsync` returns
the first column of the first row, as `GetValue` gives it, or null of no
row.

`CommandTimeout` is the seconds a statement may run, 0 for no limit:
once it runs out before the statement's first reply has come, a
cursor's first rows among them, the
statement is cancelled on the server, as `Cancel` cancels it, and fails,
SQLSTATE TB920, the session kept. `Cancel` sends the cancel at once,
over a connection of its own. `CommandType` is `Text` alone.
`Transaction`, if set, is the connection's transaction.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the statement's) | | The server's failure of the statement ([Errors](30-errors.md)) |
| TB920 | -920 | The statement cancelled, by `Cancel` or its timeout |
| 22000 | -301 | A parameter's value of a type the protocol does not carry |
| TB910 | -910 | No connection, or a closed one; `Prepare` of a command of a data source, which has none; a reader open on the connection; a transaction not the connection's; a `CommandType` other than `Text`; a parameter not an input |

## EXAMPLES

See [Running Queries](../user/03-queries.md) and
[Prepared Statements](../user/04-prepared-statements.md) of the User's
Guide.

## STANDARD

ADO.NET's `DbCommand`, of `System.Data.Common`; RULES.md D2, D17, D18 and
D20. [CLIENT Running statements] and [CLIENT Prepared statements] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md);
[PROTOCOL Query 'Q'], [PROTOCOL Execute 'E'] and [PROTOCOL Fetch 'F'] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md).

## SEE ALSO

[TurborelDataReader](04-api-data-reader.md),
[TurborelParameter](05-api-parameter.md),
[Statements and Cursors](22-statements.md).
