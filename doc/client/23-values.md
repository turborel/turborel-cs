# Values

[Contents](README.md)

## NAME

The value types: what the codec gives each of the protocol's values as,
and takes to send one.

## SYNOPSIS

```csharp
namespace Turborel.Data.Types;

public readonly record struct TurborelDecimal(Int128 Units, uint Scale)
{
    public static TurborelDecimal FromDecimal(decimal value);
    public bool TryToDecimal(out decimal value);
}
public readonly record struct TurborelDecFloat
{
    public Int128 Coefficient { get; }
    public int Exponent { get; }
    public static bool TryCreate(Int128 coefficient, int exponent, out TurborelDecFloat value);
}
public readonly record struct TurborelDatetime(DatetimeMoment Moment, long UtcMicroseconds, short ZoneMinutes,
    bool Zoned, byte Precision);
public readonly record struct TurborelInterval(IntervalQualifier Qualifier, Int128 Units);
public sealed class TurborelJson
{
    public static TurborelJson Parse(string text);
    public string ToJsonText();
}
public sealed record TurborelXml(string Text)
{
    public IReadOnlyList<XNode> Nodes();
}
public sealed record TurborelArray(IReadOnlyList<object?> Elements);
public sealed record TurborelRow(IReadOnlyList<object?> Fields);
public sealed record TurborelMultiset(IReadOnlyList<object?> Elements);
public sealed record TurborelStructuredValue(string Schema, string Name, IReadOnlyList<object?> Attributes);
public sealed record TurborelReference(ReadOnlyMemory<byte> Bytes);
public sealed record TurborelPluginValue(string TypeName, string Text, ReadOnlyMemory<byte> Bytes);
public sealed record TurborelLargeObject;

public abstract record DataType;
public sealed record CharacterType(uint Length, CharacterUnits Units, bool Varying) : DataType;
public sealed record ExactType(uint Precision, uint Scale) : DataType;
// ... one record a data type, as Appendix A lists them
```

## DESCRIPTION

Each value of the native protocol is read as a type of the driver's own,
or a .NET type where one holds it whole: `string`, `double`, `bool`,
`byte[]` and `Guid`; SQL's null is `null`. A parameter takes the same
types. [Appendix A](../appendix/a-types.md) lists each, with its bounds,
and the data type, a record derived from `DataType`, that describes a
column or a parameter of each.

An exact number is a `TurborelDecimal`, its units an `Int128` and its
scale a `uint`, as the protocol carries them, since .NET's `decimal`
holds 96 bits; `FromDecimal` makes one of a `decimal`, and
`TryToDecimal` gives one back where a `decimal` holds it. Its text is
SQL's. A datetime is a `TurborelDatetime`: its instant, microseconds in
UTC from 0001-01-01 00:00:00, a date's its midnight's and a time's
from midnight UTC, within a day; and the zone's displacement in minutes.

A JSON value is a `TurborelJson`, the stored form the server keeps,
checked whole as it is read; `ToJsonText` writes it as the server writes
JSON text, and `Parse` makes one of JSON text as the server parses it.
An XML value is a `TurborelXml`, its serialization as CONTENT; `Nodes`
parses its nodes from it, whitespace kept, no document type declaration
read, and one is sent as a parameter made of its text. A value the
server cannot serialize, as an attribute standing alone, fails the
fetch with SQLSTATE 2200W. A UUID is a `Guid`: `ToByteArray(bigEndian:
true)` gives its 16 octets in their order, `ToString` its text, and a
parameter is made of its octets by `new Guid(octets, bigEndian: true)`
or of its text by `Guid.Parse`. A large object is a
`TurborelLargeObject`, its use the subject of milestone M4.

## EXAMPLES

```csharp
using System.Xml.Linq;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE TABLE ORDERS (ID UUID, BODY XML(CONTENT), DETAIL JSON)");
PreparedStatement insert = await connection.PrepareAsync("INSERT INTO ORDERS VALUES (?, ?, ?)");
await connection.ExecuteAsync(insert, [
    Guid.Parse("919108f7-52d1-4320-9bac-f847db4148a8"),
    new TurborelXml("<order no=\"18\"><item>jam</item></order>"),
    TurborelJson.Parse("{\"rush\": true, \"weight\": 1.50}"),
]);
var rows = (RowsOutcome)await connection.QueryAsync("SELECT ID, BODY, DETAIL FROM ORDERS");
var id = (Guid)rows.Rows[0][0]!;
var body = (TurborelXml)rows.Rows[0][1]!;
var detail = (TurborelJson)rows.Rows[0][2]!;
Console.WriteLine($"{id} = {Convert.ToHexString(id.ToByteArray(bigEndian: true))}");
var order = (XElement)body.Nodes()[0];
Console.WriteLine($"{body.Text}: order {order.Attribute("no")?.Value} of {order.Element("item")?.Value}");
Console.WriteLine(detail.ToJsonText());
await connection.QueryAsync("ROLLBACK");
```

```
919108f7-52d1-4320-9bac-f847db4148a8 = 919108F752D143209BACF847DB4148A8
<order no="18"><item>jam</item></order>: order 18 of jam
{"rush":true,"weight":1.50}
```

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08006 | -919 | A value read from the server is malformed or past its bounds |
| 22000 | -301 | A value to send is of no type the protocol carries |
| 22032 | -301 | Text is not JSON text, or a JSON value holds a datetime or an approximate number not finite, which have no text |
| 22003 | -301 | JSON text holds a number no number of turborel's is |
| 2200N | -301 | An XML value's text is not well-formed content |
| 2200W | -301 | The server cannot serialize an XML value it would send |
| 0A000 | -900 | A JSON value nests deeper than 128, or its stored form is longer than 256 MiB |

## STANDARD

[PROTOCOL Data types] and [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[PROTOCOL JSON values] for the JSON value's stored form;
[CLIENT XML values] and [CLIENT UUIDs] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[Appendix A: Types](../appendix/a-types.md), [Errors](30-errors.md).
