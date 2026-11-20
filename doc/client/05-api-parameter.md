# TurborelParameter

[Contents](README.md)

## NAME

`TurborelParameter` and `TurborelParameterCollection`: the values of a
statement's dynamic parameters, in the order of its `?` marks.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelParameter : DbParameter
{
    public TurborelParameter();
    public TurborelParameter(string? parameterName, object? value);

    public override object? Value { get; set; }
    public override string ParameterName { get; set; }
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }   // Input
    public override void ResetDbType();
}

public sealed class TurborelParameterCollection : DbParameterCollection
{
    public TurborelParameter AddWithValue(string? parameterName, object? value);
    public override int Add(object value);
    public override void Insert(int index, object value);
    public override int IndexOf(string parameterName);
    // and the rest of DbParameterCollection's members
}
```

## DESCRIPTION

A statement's dynamic parameters are written `?` and given by their
place: the collection's first parameter is the value of the first `?`,
and so on. A parameter's name is the program's own, to find it by in the
collection; it is not sent. A parameter is an input alone.

`Value` is sent as the protocol's value of its .NET type (RULES.md D18):

| .NET | Sent as |
|---|---|
| `null`, `DBNull` | the null value |
| `int`, `long`, `short`, `byte` and the other integers, `decimal` | an exact number |
| `double`, `float` | an approximate number |
| `string`, `char` | a character string |
| `byte[]` | a binary string |
| `bool` | a truth value |
| `Guid` | a UUID |
| `DateTime` | a timestamp of its fields, of microseconds |
| `DateTimeOffset` | a timestamp with time zone |
| `DateOnly` | a date |
| `TimeOnly` | a time, of microseconds |
| `TimeSpan` | an interval DAY TO SECOND, of microseconds |
| the driver's own types, as `TurborelDecimal` or `TurborelLargeObject` | as they are ([Values](23-values.md)) |

The server types each `?` where it stands, and casts the value sent to
that type, so `DbType` is not sent: it is what the program sets, else
the type the value suggests.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 22000 | -301 | A value of another .NET type, as the statement runs |
| 07001 | -921 | The values are not one for each `?` |
| TB910 | -910 | A parameter other than a `TurborelParameter` added; a name or place not in the collection; a parameter not an input, as the statement runs |

## EXAMPLES

See [Prepared Statements](../user/04-prepared-statements.md) of the
User's Guide.

## STANDARD

ADO.NET's `DbParameter` and `DbParameterCollection`, of
`System.Data.Common`; RULES.md D2, D17 and D18. [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md).

## SEE ALSO

[TurborelCommand](03-api-command.md), [Values](23-values.md),
[Appendix A: Types](../appendix/a-types.md).
