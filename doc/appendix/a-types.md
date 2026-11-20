# Appendix A: Types

[Contents](README.md)

How each data type and value of the native protocol is given in C#
([PROTOCOL Data types] and [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md)).
The codec reads and writes every one of them; the types it gives are the
driver's own, of the namespace `Turborel.Data.Types`, or .NET's where
one holds a value whole ([Values](../client/23-values.md)). A value read from a server is checked
against the bounds below. A frame that is itself malformed, cut short or
with bytes after its last field, fails the connection, SQLCODE -919 and
SQLSTATE 08006. A value the client cannot take in a frame read whole, a
units or truth octet other than 0 or 1, damaged JSON, a Complete's
condition that cannot be read, or value tags 18 and 19, fails its
request alone, SQLCODE -916 and SQLSTATE TB916, or of JSON past
Turborel's limits -900 and 0A000; the rest of the request's replies are
read past first, and the connection goes on, as turborel-client takes
it ([PROTOCOL Values and fields]).

Value tags 5, 7, 10 and 12 are read as tags 1, 6, 9 and 11, as
turborel-protocol's reader takes them: CHARACTER VARYING as CHARACTER,
BINARY VARYING as BINARY, TIME WITH TIME ZONE as TIME, and TIMESTAMP
WITH TIME ZONE as TIMESTAMP; the page lists only 1, 6, 9 and 11 as value
tags.

## Values

| Tag | Value | C# | Bounds |
|---|---|---|---|
| 0 | null | `null` (`DBNull` is sent as null) | |
| 1 | character string | `string` | UTF-8 on the wire; tag 5 is read as tag 1 |
| 2 | exact number | `TurborelDecimal`: `Int128` units, `uint` scale | the whole `i128` range and every scale, with no loss; gives a `decimal` where 96 bits and a scale of 28 hold it |
| 3 | approximate number | `double` | its bits kept, NaN and -0.0 among them |
| 4 | a plug-in's value | `TurborelPluginValue`: the type's name, the text, the bytes | kept, and sent back, unchanged |
| 6 | binary string | `byte[]` | tag 7 is read as tag 6 |
| 8, 9, 11 | date, time, timestamp | `TurborelDatetime`: moment, UTC microseconds from 0001-01-01 00:00:00 (a date its midnight's, a time from midnight UTC, within a day), displacement in minutes, zoned, precision | a displacement that fits an `i16`, -12:59 to +14:00 as the server sends it; tags 10 and 12 are read as 9 and 11 |
| 13 | interval | `TurborelInterval`: qualifier and `Int128` units | fields 0 YEAR to 5 SECOND; months of a year-month interval, microseconds of a day-time one |
| 14 | truth value | `bool` | 0 or 1; unknown is null |
| 15 | DECFLOAT | `TurborelDecFloat`: `Int128` coefficient, `int` exponent | at most 34 digits, no trailing zero, an adjusted exponent of -6143 to 6144 |
| 16 | array | `TurborelArray` | nested at most 16 deep |
| 17 | row | `TurborelRow` | nested at most 16 deep |
| 20, 21 | large object | `TurborelLargeObject`: whole, its octets, or by its locator and lengths | a character one's octets UTF-8 |
| 22 | multiset | `TurborelMultiset` | at most 65,535 elements |
| 23 | structured value | `TurborelStructuredValue`: its type's schema and name, its attributes | nested at most 16 deep |
| 24 | reference | `TurborelReference`, its octets | |
| 25 | JSON | `TurborelJson`: the stored form, read as an item or JSON text, made from JSON text | checked whole: every node, string, offset and directory; nested at most 128 deep |
| 26 | XML | `TurborelXml`, its serialization as CONTENT | |
| 27 | UUID | `Guid` | 16 octets, in RFC 4122's order |

A value of another .NET type is refused before it is sent, SQLCODE -301
and SQLSTATE 22000, the drivers' code for a value the protocol cannot
carry.

## Data types

| Tag | Type | C#, of `DataType` |
|---|---|---|
| 1, 5 | CHARACTER, CHARACTER VARYING | `CharacterType`: length, units, varying |
| 2 | exact numeric | `ExactType`: precision, scale |
| 3 | approximate numeric | `ApproximateType`: precision |
| 4 | a plug-in's type | `PluginType`: name, at most 2 parameters |
| 6, 7 | BINARY, BINARY VARYING | `BinaryType`: length, varying |
| 8 to 12 | DATE, TIME, TIME WITH TIME ZONE, TIMESTAMP, TIMESTAMP WITH TIME ZONE | `DatetimeType`: moment, zoned, precision |
| 13 | INTERVAL | `IntervalType`: qualifier |
| 14 | BOOLEAN | `BooleanType` |
| 15 | DECFLOAT | `DecFloatType`: precision |
| 16 | array | `ArrayType`: maximum cardinality, 1 to 65,535, and element type |
| 17 | row | `RowType`: its fields, one at least |
| 18, 19 | CHARACTER and BINARY LARGE OBJECT | `LargeObjectType`: kind, length, units |
| 22 | multiset | `MultisetType`: element type |
| 23 | structured type | `StructuredType`: name and proper supertypes |
| 24 | reference type | `ReferenceType`: the referenced type and its scope |
| 25 | JSON | `JsonType` |
| 26 | XML | `XmlType`: primary and secondary modifiers, and the schema of XMLSCHEMA |
| 27 | UUID | `UuidType` |

A distinct type comes as its source type, and a plug-in's type the
driver does not have is read by its name, its values shown by their
text.

## In the standard API

A `TurborelDataReader`'s `GetValue` gives each column as .NET's type of
its data type, as the server declared it where it told it
(`declared-types`), the type `GetFieldType` names; `GetDataTypeName` is
the column's type as SQL writes it, as it was declared; `GetProviderSpecificValue`
gives the value above as it is (RULES.md D18).

| Data type | `GetValue` |
|---|---|
| CHARACTER, CHARACTER VARYING, JSON, XML | `string`, JSON as its text, XML as its serialization |
| SMALLINT, INTEGER, BIGINT, as the server declares them | `short`, `int`, `long` |
| REAL, as the server declares it | `float` |
| exact numeric | `decimal`, or the `TurborelDecimal` where a `decimal` cannot hold it |
| approximate numeric | `double` |
| BINARY, BINARY VARYING | `byte[]` |
| BOOLEAN | `bool` |
| UUID | `Guid` |
| DATE, TIMESTAMP | `DateTime` of its fields, `DateTimeKind.Unspecified` |
| TIMESTAMP WITH TIME ZONE | `DateTimeOffset` |
| TIME | `TimeSpan`, the time of day |
| INTERVAL of days to seconds | `TimeSpan`, where it holds it; else the `TurborelInterval` |
| TIME WITH TIME ZONE | `TurborelDatetime` |
| INTERVAL of years and months | `TurborelInterval` |
| DECFLOAT, large objects, arrays, rows, multisets, structured types, references, a plug-in's types | the driver's type above |
| the null value | `DBNull.Value` |

A parameter's value is sent by its .NET type
([TurborelParameter](../client/05-api-parameter.md)).
