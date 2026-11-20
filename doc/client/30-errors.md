# Errors

[Contents](README.md)

## NAME

`TurborelException`, the driver's one error, and `TurborelNotice`, a
notice the server sends.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelException : DbException
{
    public TurborelException(int sqlCode, string sqlState, string message);
    public TurborelException(int sqlCode, string sqlState, string message, Exception? innerException);

    public int SqlCode { get; }
    public override string SqlState { get; }
    public string? Rule { get; }
    public long? Position { get; }
    public bool? TransactionActive { get; }
    public string? Severity { get; }
    public string? Detail { get; }
    public string? Hint { get; }
    public string? Context { get; }
    public string? SchemaName { get; }
    public string? TableName { get; }
    public string? ColumnName { get; }
    public string? DataTypeName { get; }
    public string? ConstraintName { get; }
}

public sealed record TurborelNotice
{
    public string SqlState { get; init; }
    public string Message { get; init; }
    public string? Severity { get; init; }
    public string? Detail { get; init; }
    public string? Hint { get; init; }
    public string? Context { get; init; }
    public string? SchemaName { get; init; }
    public string? TableName { get; init; }
    public string? ColumnName { get; init; }
    public string? DataTypeName { get; init; }
    public string? ConstraintName { get; init; }
}
```

## DESCRIPTION

Every failure is a `TurborelException`, a `DbException` of ADO.NET, so a
program handles one kind of failure (RULES.md D9). It carries the
failure's SQLCODE, as `SqlCode` and as `ErrorCode`, and its SQLSTATE, as
`SqlState`. A failure the server sends carries all it said: the message;
`Rule`, the clause of a standard it cites; `Position`, the character of
the statement it names; `TransactionActive`, whether the session's
transaction is active after it; and, once `diagnostics` is granted,
`Severity`, ERROR or FATAL, and what it is about. A failure the driver
finds itself carries the SQLCODE and SQLSTATE turborel-client gives the
same case, and `InnerException` its cause, as the socket's error;
[Appendix B](../appendix/b-errors.md) lists them.

A failure of class 08 but 08003, or of class 28 as the connection is
made, loses the connection: `NativeConnection.IsLost` is then true, and
each later call fails with the failure that lost it. Any other failure
leaves the connection working.

A condition a statement completed with, a warning of class 01 or a
condition of class 02, is a `TurborelException` too, among
`Conditions`. A notice is a `TurborelNotice`: a warning or a note, its
SQLSTATE and message, and, once `diagnostics` is granted, its severity,
WARNING or NOTICE, and what it is about.

## ERRORS

The driver's own failures:

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08006 | -919 | A malformed reply, a reply too long or out of place, the connection lost or timed out, TLS refused; a method of a capability not granted, the connection kept |
| 28000 | -919 | A secret mechanism over plain TCP; SCRAM refused, as channel binding required and impossible |
| TB910 | -910 | A null where a value is needed |
| 22000 | -301 | A value to send of no type the protocol carries |
| 22003, 22032, 2200N | -301 | JSON text or XML content that cannot be read |
| 0A000 | -900 | Past Turborel's limits: a JSON value nested deeper than 128, or longer than 256 MiB |

## EXAMPLES

See [Errors and Diagnostics](../user/10-errors.md).

## STANDARD

[PROTOCOL Error 'E'], [PROTOCOL Notice 'N'] and [PROTOCOL Errors and
notices] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Running statements] and [CLIENT What the server tells] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[Appendix B: Errors](../appendix/b-errors.md),
[Statements and Cursors](22-statements.md).
