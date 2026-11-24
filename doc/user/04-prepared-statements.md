# Prepared Statements

[Contents](README.md)

A statement prepared once runs as often as wanted, with new values each
time. Its dynamic parameters are written `?`, and the server says the
type each takes and, of a query, its columns. A prepared statement is
valid across transactions until it is deallocated or the session reset.

## Prepare, execute, read a cursor

`PrepareAsync` gives a `PreparedStatement`, whose `Inputs` are the types
its parameters take; `ExecuteAsync` runs it with one value for each. A query's rows are read all at once by
`ExecuteAsync`, or through its cursor: `OpenCursorAsync` gives its first
rows and says whether it has more, `FetchAsync` the next, and the cursor
closes with its last row or by `CloseCursorAsync`. `DeallocateAsync`
destroys the statement.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
PreparedStatement raise = await connection.PrepareAsync("UPDATE STAFF SET SALARY = SALARY + ? WHERE NAME = ?");
Console.WriteLine(string.Join(", ", raise.Inputs.Select(p => p.Type)));
foreach ((string name, decimal amount) in new[] { ("Ann", 100m), ("Bob", 250.25m) })
{
    var outcome = (DoneOutcome)await connection.ExecuteAsync(raise, [TurborelDecimal.FromDecimal(amount), name]);
    Console.WriteLine($"{name}: {outcome.Completion.RowCount} row");
}

PreparedStatement list = await connection.PrepareAsync("SELECT NAME, SALARY FROM STAFF ORDER BY NAME");
Batch first = await connection.OpenCursorAsync(list, [], rows: 1);
Console.WriteLine($"{string.Join(", ", first.Rows[0])}; more: {first.More}");
Batch rest = await connection.FetchAsync(list, rows: 0);
Console.WriteLine($"{string.Join(", ", rest.Rows[0])}; more: {rest.More}");
await connection.DeallocateAsync(list);
await connection.QueryAsync("COMMIT");
```

```
ExactType { Precision = 9, Scale = 2 }, CharacterType { Length = 20, Units = Characters, Varying = True }
Ann: 1 row
Bob: 1 row
Ann, 5300.00; more: True
Bob, 4350.75; more: False
```

A `decimal` becomes an exact number by `TurborelDecimal.FromDecimal`.

## Several cursors, and describing again

Once the server grants `cursors`, a statement has several cursors open
at once, each numbered by the program: `cursor` names one in
`OpenCursorAsync`, `FetchAsync` and `CloseCursorAsync`, 0 by default.
Once it grants `describe`, `DescribeAsync` describes a statement again,
as it is now, and `DescribeCursorAsync` an open cursor's columns.

## A CALL's output parameters

A CALL's inputs are its IN and INOUT parameters' `?` marks. Its columns
are one for each `?` of an OUT or INOUT parameter, in the procedure's
order, and executing it gives one row of their values. `Parameters`
gives every `?`, OUT ones too, with the mode and ordinal position of the
procedure's parameter it stands in, and `Procedure` the procedure's
specific name, as the server tells them to a connection granted
`parameter-modes`, which connecting asks for:

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE PROCEDURE PAY (IN WHO VARCHAR(20), OUT SALARY DECIMAL(9,2)) READS SQL DATA SELECT SALARY INTO PAY.SALARY FROM STAFF WHERE NAME = WHO");
PreparedStatement call = await connection.PrepareAsync("CALL PAY(?, ?)");
Console.WriteLine($"{call.Procedure?.Schema}.{call.Procedure?.Name}: {string.Join(", ", call.Parameters.Select(p => $"{p.Mode} of {p.Ordinal}"))}");
Console.WriteLine($"{call.Inputs.Count} input, {call.Columns.Count} column: {call.Columns[0].Name}");
var output = (RowsOutcome)await connection.ExecuteAsync(call, ["Bob"]);
Console.WriteLine(output.Rows[0][0]);
await connection.QueryAsync("COMMIT");
```

```
ANN.PAY_1: In of 1, Out of 2
1 input, 1 column: SALARY
4350.75
```

Through ADO.NET, `DeriveParametersAsync` makes a command's parameters
one for each `?`, each its `Direction` the parameter's mode. The CALL
takes the values of the `Input` and `InputOutput` ones and sets the
`Value` of the `InputOutput` and `Output` ones from its row:

```csharp
using Turborel.Data;

await using TurborelDataSource source = TurborelDataSource.Create("host=localhost:17435;user=ann");
await using TurborelConnection connection = await source.OpenConnectionAsync();
await using TurborelCommand pay = connection.CreateCommand();
pay.CommandText = "CALL PAY(?, ?)";
await pay.DeriveParametersAsync();
pay.Parameters[0].Value = "Ann";
await pay.ExecuteNonQueryAsync();
foreach (TurborelParameter parameter in pay.Parameters)
{
    Console.WriteLine($"{parameter.Direction}: {parameter.Value}");
}
```

```
Input: Ann
Output: 5301.00
```

## See also

[Statements and Cursors](../client/22-statements.md) in the API
Reference.
