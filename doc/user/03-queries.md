# Running Queries

[Contents](README.md)

A connection runs one statement at a time, by `QueryAsync`. A query
gives its columns and rows, a `RowsOutcome`; any other statement its
completion, a `DoneOutcome`. The examples of this chapter and the next
run in order against the server of [Connecting](02-connecting.md), on a
new database whose owner ANN has a schema of the same name.

## A statement and its rows

```
SELECT ID, NAME, SALARY FROM STAFF ORDER BY ID
```

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
connection.Autocommit = true;
await connection.QueryAsync("CREATE TABLE STAFF (ID INTEGER GENERATED ALWAYS AS IDENTITY, NAME VARCHAR(20), SALARY DECIMAL(9,2))");
Outcome inserted = await connection.QueryAsync("INSERT INTO STAFF (NAME, SALARY) VALUES ('Ann', 5200.00), ('Bob', 4100.50)");
if (inserted is DoneOutcome done)
{
    Console.WriteLine($"inserted {done.Completion.RowCount}, identities {string.Join(", ", connection.LastGenerated)}");
}

if (await connection.QueryAsync("SELECT ID, NAME, SALARY FROM STAFF ORDER BY ID") is RowsOutcome result)
{
    Console.WriteLine(string.Join(" | ", result.Columns.Select(c => $"{c.Name} {c.Type}")));
    foreach (IReadOnlyList<object?> row in result.Rows)
    {
        Console.WriteLine(string.Join(", ", row));
    }
}

Console.WriteLine($"server time told: {connection.ServerTime is not null}; in a transaction: {connection.InTransaction}");
```

```
inserted 2, identities 1, 2
ID ExactType { Precision = 10, Scale = 0 } | NAME CharacterType { Length = 20, Units = Characters, Varying = True } | SALARY ExactType { Precision = 9, Scale = 2 }
1, Ann, 5200.00
2, Bob, 4100.50
server time told: True; in a transaction: False
```

Each column has its name, if it has one, its data type and whether it is
NOT NULL. Each value is of the type [Appendix A](../appendix/a-types.md)
gives its SQL type: an exact number a `TurborelDecimal`, which prints as
SQL writes it, a character string a `string`, and SQL's null `null`.

With `Autocommit` set, each statement is its own transaction: the server
commits it as it succeeds, and no COMMIT is sent. Without it, the first
statement begins a transaction that lasts until COMMIT or ROLLBACK
([Transactions](05-transactions.md)).

## What a completion tells

A statement's completion, and the connection after it, tell what the
server granted the connection to be told:

| Told | By |
|---|---|
| The rows a statement changed, or a query returned once all were read | `Completed.RowCount`, `RowsAffected` |
| The identity values an INSERT generated, in the order of its rows | `Completed.Generated`, `LastGenerated` |
| The server's time for the statement: total, queued and locks, in microseconds | `Completed.Time`, `ServerTime` |
| The log position of the commit it made | `Completed.Position`, `CommitPosition` |
| The SQLSTATE of the warning it completed with | `Completed.SqlState`, `Warning` |

A query that returns no row, or a change of no row, completes with
SQLCODE 100 and SQLSTATE 02000.

## Rows as they come

`QueryIntoAsync` gives a query's rows to a sink, an `IRowSink`, a batch
at a time as the server sends them, so no more than a batch is held.
`QueryManyAsync` runs several statements in one request, each in turn,
the first that fails stopping the rest:

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
string values = string.Join(", ", Enumerable.Range(1, 20000).Select(i => $"({i})"));
var printer = new Printer();
Streamed streamed = await connection.QueryIntoAsync($"VALUES {values}", printer);
Console.WriteLine($"{streamed.RowsSent} rows in {printer.Batches} batches, the last {printer.Last}");

Ran ran = await connection.QueryManyAsync("VALUES 1; SELECT * FROM NOWHERE; VALUES 3");
Console.WriteLine($"{ran.Outcomes.Count} ran, then SQLSTATE {ran.Failure?.SqlState}: {ran.Failure?.Message}");

sealed class Printer : IRowSink
{
    public int Batches { get; private set; }

    public object? Last { get; private set; }

    public void Columns(IReadOnlyList<Column> columns) => Console.WriteLine($"{columns.Count} column");

    public void Rows(IReadOnlyList<IReadOnlyList<object?>> rows) => (Batches, Last) = (Batches + 1, rows[^1][0]);
}
```

```
1 column
20000 rows in 8 batches, the last 20000
1 ran, then SQLSTATE 2A000: table ANN.NOWHERE is not defined
```

## See also

[Statements and Cursors](../client/22-statements.md) and
[Values](../client/23-values.md) in the API Reference.
