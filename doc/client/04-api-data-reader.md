# TurborelDataReader

[Contents](README.md)

## NAME

`TurborelDataReader`: a statement's rows, read as they come, each value
by its typed getter, and its columns' schema.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelDataReader : DbDataReader, IDbColumnSchemaGenerator
{
    public override Task<bool> ReadAsync(CancellationToken cancellationToken);
    public override Task<bool> NextResultAsync(CancellationToken cancellationToken);
    public override Task CloseAsync();
    public override int FieldCount { get; }
    public override bool HasRows { get; }
    public override int RecordsAffected { get; }

    public override string GetName(int ordinal);
    public override int GetOrdinal(string name);
    public override string GetDataTypeName(int ordinal);
    public override Type GetFieldType(int ordinal);
    public override Type GetProviderSpecificFieldType(int ordinal);

    public override object GetValue(int ordinal);
    public override object GetProviderSpecificValue(int ordinal);
    public override T GetFieldValue<T>(int ordinal);
    public override bool IsDBNull(int ordinal);
    // GetBoolean, GetByte, GetInt16, GetInt32, GetInt64, GetDecimal, GetDouble,
    // GetFloat, GetString, GetChar, GetDateTime, GetGuid, GetBytes, GetChars.

    public ReadOnlyCollection<DbColumn> GetColumnSchema();
    public override DataTable GetSchemaTable();
}
```

## DESCRIPTION

A reader reads its statement's rows a batch at a time, as the server
sends them: `ReadAsync` moves to the next row, reading the next batch when
it must, and returns false after the last. `NextResultAsync` moves to
the next statement's result, of a Query of several statements or of a
batch. `FieldCount` is the result's columns, 0 of a statement not a
query; `RecordsAffected` the rows the statements changed, summed, or -1
if none counted any. `HasRows` says whether the result has rows, read
or not, reading its first batch if it must.

`GetValue` gives each column as .NET's type of its data type, the type
`GetFieldType` names: a `decimal` of an exact number, or the driver's
`TurborelDecimal` where a `decimal` cannot hold it; a `DateTime` of a
date or timestamp; a `DateTimeOffset` of a timestamp with time zone; a
`TimeSpan` of a time or a day-time interval; the text of JSON and XML;
and the driver's own type of what .NET has no type for
([Appendix A: Types](../appendix/a-types.md#in-the-standard-api)). The
null value is `DBNull.Value`. `GetProviderSpecificValue` gives the
codec's value as it is, of the type `GetProviderSpecificFieldType`
names. `GetDataTypeName` writes the column's data type as SQL writes it,
as it was declared where the server told it, as `INTEGER`,
`DECIMAL(9,2)` or `CHARACTER VARYING(20)`, else as its tag writes it, as
`NUMERIC(10,0)` ([Statements and Cursors](22-statements.md)). A column
declared SMALLINT, INTEGER, BIGINT or REAL reads as a `short`, an `int`,
a `long` or a `float`.

The typed getters convert what they can: `GetInt32` and the other integer
getters, `GetDecimal`, `GetDouble` and `GetFloat` read any number,
checked for overflow; `GetString` a character string, JSON, XML or a
character large object held whole; `GetDateTime` a date, timestamp, or
timestamp with time zone's fields; `GetBytes` a binary string, or the
octets of a large object held whole. `GetFieldValue<T>` gives the codec's value if it
is a `T`, else `GetValue`'s, else the number converted.
`GetColumnSchema` and `GetSchemaTable` give each column's name, place,
type, nullability, precision, scale and length.

`CloseAsync`, or disposing it, reads what is left of the statement's
rows, or closes its cursor, so the connection takes the next statement;
under autocommit, a cursor's transaction ends with it.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the statement's) | | The server's failure of the statement, among its rows; the reader takes no more after it |
| 22003 | -301 | A number past the type a getter gives |
| TB910 | -910 | A value read before `Read`, or after the last row; a column that does not exist; a getter of a type the column cannot give, or of a null; `GetBytes` or `GetChars` of an offset or length past the buffer or below 0; `GetData`, as a column holds no nested reader; the reader read after it is closed |

## EXAMPLES

See [Running Queries](../user/03-queries.md) of the User's Guide.

## STANDARD

ADO.NET's `DbDataReader` and `IDbColumnSchemaGenerator`, of
`System.Data.Common`; RULES.md D2, D17 and D18. [PROTOCOL Data types]
and [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md).

## SEE ALSO

[TurborelCommand](03-api-command.md), [Values](23-values.md),
[Appendix A: Types](../appendix/a-types.md).
