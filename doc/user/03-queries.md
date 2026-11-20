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

## With ADO.NET

A `TurborelCommand` runs a statement on a `TurborelConnection`, and a
`TurborelDataReader` reads a query's rows a batch at a time, as they
come. A command of parameters is prepared and executed, its query's rows
fetched through a cursor as the reader reads them; one prepared by
`PrepareAsync` is executed again with no Prepare. Outside a transaction
each statement is its own, under autocommit. Each column is read as
.NET's type of its data type, the type `GetFieldType` names
([Appendix A: Types](../appendix/a-types.md#in-the-standard-api)):

```csharp
using Turborel.Data;

await using var connection = new TurborelConnection("host=localhost:17435;user=ann");
await connection.OpenAsync();

await using (var create = new TurborelCommand(
    "CREATE TABLE TEAM (ID INTEGER PRIMARY KEY, NAME VARCHAR(20), JOINED DATE, PAY DECIMAL(9,2))", connection))
{
    await create.ExecuteNonQueryAsync();
}

await using var insert = new TurborelCommand("INSERT INTO TEAM VALUES (?, ?, ?, ?)", connection);
TurborelParameter id = insert.Parameters.AddWithValue("id", 0);
TurborelParameter name = insert.Parameters.AddWithValue("name", "");
TurborelParameter joined = insert.Parameters.AddWithValue("joined", DateOnly.MinValue);
TurborelParameter pay = insert.Parameters.AddWithValue("pay", 0m);
await insert.PrepareAsync();
foreach ((int i, string n, DateOnly j, decimal p) in new[]
{
    (1, "Ann", new DateOnly(2021, 3, 1), 5200.00m),
    (2, "Bob", new DateOnly(2022, 7, 15), 4100.50m),
    (3, "Cy", new DateOnly(2024, 1, 2), 3900.00m),
})
{
    (id.Value, name.Value, joined.Value, pay.Value) = (i, n, j, p);
    Console.WriteLine($"inserted {await insert.ExecuteNonQueryAsync()} row");
}

await using var select = new TurborelCommand("SELECT ID, NAME, JOINED, PAY FROM TEAM WHERE PAY > ? ORDER BY ID", connection);
select.Parameters.AddWithValue("least", 4000);
await using (TurborelDataReader reader = await select.ExecuteReaderAsync())
{
    for (int c = 0; c < reader.FieldCount; c++)
    {
        Console.WriteLine($"{reader.GetName(c)}: {reader.GetDataTypeName(c)}, read as {reader.GetFieldType(c).Name}");
    }

    while (await reader.ReadAsync())
    {
        Console.WriteLine($"{reader.GetInt32(0)} {reader.GetString(1)} {reader.GetDateTime(2):yyyy-MM-dd} {reader.GetDecimal(3)}");
    }
}

await using var count = new TurborelCommand("SELECT COUNT(*) FROM TEAM", connection);
Console.WriteLine($"members: {await count.ExecuteScalarAsync()}");
```

```
inserted 1 row
inserted 1 row
inserted 1 row
ID: DECIMAL(10, 0), read as Decimal
NAME: CHARACTER VARYING(20), read as String
JOINED: DATE, read as DateTime
PAY: DECIMAL(9, 2), read as Decimal
1 Ann 2021-03-01 5200.00
2 Bob 2022-07-15 4100.50
members: 3
```

`ExecuteNonQueryAsync` returns the rows a statement changed,
`ExecuteScalarAsync` the first column of the first row. A command of no
parameters, of a server that grants `multi-statement`, may hold several
statements, each result read in turn by the reader's `NextResultAsync`
([TurborelCommand](../client/03-api-command.md)).

## See also

[Statements and Cursors](../client/22-statements.md) and
[Values](../client/23-values.md) in the API Reference.
