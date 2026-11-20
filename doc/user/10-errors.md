# Errors and Diagnostics

[Contents](README.md)

Every failure reaches a program as a `TurborelException`, with its
SQLCODE and SQLSTATE: the server's, all it said, or one of the driver's
own ([Appendix B](../appendix/b-errors.md)). A failure of a statement
leaves the connection working, its transaction active or not as
`TransactionActive` says. A failure of the connection, class 08 but
08003, or of class 28 as the connection is made, loses it: `IsLost` is
then true, and each later call fails.

```csharp
using Turborel.Data;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE TABLE ACCOUNTS (ID INTEGER PRIMARY KEY, BALANCE INTEGER CONSTRAINT FUNDED CHECK (BALANCE >= 0))");
foreach (string sql in new[] { "SELECT * FROM LEDGER", "INSERT INTO ACCOUNTS VALUES (1, -5)" })
{
    try
    {
        await connection.QueryAsync(sql);
    }
    catch (TurborelException e)
    {
        Console.WriteLine($"SQLCODE {e.SqlCode} (SQLSTATE {e.SqlState}): {e.Message}");
        Console.WriteLine($"  rule {e.Rule}; position {e.Position}; severity {e.Severity}; table {e.TableName}; constraint {e.ConstraintName}");
        Console.WriteLine($"  transaction active {e.TransactionActive}; connection lost {connection.IsLost}");
    }
}

await connection.QueryAsync("SELECT SUM(BALANCE) FROM (VALUES (4), (CAST(NULL AS INTEGER))) AS T (BALANCE)");
Console.WriteLine($"warning {connection.Warning}");
foreach (TurborelException condition in connection.TakeConditions())
{
    Console.WriteLine($"condition {condition.SqlState} ({condition.Severity}): {condition.Message}");
}

await connection.QueryAsync("ROLLBACK");
```

```
SQLCODE -103 (SQLSTATE 2A000): table ANN.LEDGER is not defined
  rule X3.135-1986 §5.4 SR5; position 14; severity ERROR; table LEDGER; constraint 
  transaction active True; connection lost False
SQLCODE -201 (SQLSTATE 23000): CHECK ANN.FUNDED of ANN.ACCOUNTS is false for a row
  rule ISO/IEC 9075:1992 §11.6 GR3; position ; severity ERROR; table ACCOUNTS; constraint FUNDED
  transaction active True; connection lost False
warning 01003
condition 01003 (WARNING): the statement completed with warning: null value eliminated in set function
```

`Rule` is the clause of a standard the message cites, and `Position` the
character of the statement it names. The diagnostics, `Severity`,
`Detail`, `Hint`, `Context`, `SchemaName`, `TableName`, `ColumnName`,
`DataTypeName` and `ConstraintName`, come once the server grants
`diagnostics`.

## Warnings and notices

A statement's warning is kept, by its SQLSTATE, as `Warning`. Its
conditions come one way: in its completion once the server grants
`conditions`, read by `Conditions` or `TakeConditions`; else as notices
once it grants `notices`; else not at all. A failing statement's
conditions come as notices. The server's own notices, as 01T01 after a
reset, always come as notices.

A notice, a `TurborelNotice`, goes to `NoticeHandler` as it comes, before
the reply of the statement that raised it; with no handler, the last 64
are kept, and `TakeNotices` takes them.

## See also

[Errors](../client/30-errors.md) in the API Reference,
[Appendix B: Errors](../appendix/b-errors.md).
