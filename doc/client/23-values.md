# Values

[Contents](README.md)

This page will describe the values the low-level client gives and takes
for each of the protocol's types, among them exact numbers, datetimes
and intervals, arrays, rows, multisets and structured values, JSON, XML
and UUIDs; Appendix A maps each to its C# type. It is filled
at milestone M1, and completed at M3.

## NAME

The value types: what the codec gives each of the protocol's values as,
and takes to send one.

## DESCRIPTION

Each value of the native protocol is read as a type of the driver's own,
or a .NET type where one holds it whole: `string`, `double`, `bool`,
`byte[]` and `Guid`. [Appendix A](../appendix/a-types.md) lists each,
with its bounds. The types are internal today, used by the codec alone;
they become public with the low-level client, which this page then
describes member by member.

An exact number is a `TurborelDecimal`, its units an `Int128` and its
scale a `uint`, as the protocol carries them, since .NET's `decimal`
holds 96 bits. A JSON value is a `TurborelJson`, the stored form the
server keeps, checked whole as it is read; it gives JSON text as the
server writes it, and is made from JSON text as the server parses it.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08006 | -919 | A value read from the server is malformed or past its bounds |
| 07006 | -921 | A value to send is of no type the protocol carries |
| 22032 | -301 | Text is not JSON text, or a JSON value holds a datetime or an approximate number not finite, which have no text |
| 22003 | -301 | JSON text holds a number no number of turborel's is |
| 0A000 | -900 | A JSON value nests deeper than 128, or its stored form is longer than 256 MiB |

## STANDARD

[PROTOCOL Data types] and [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
Turborel's RULES.md D89 for the JSON value's stored form.

## SEE ALSO

[Appendix A: Types](../appendix/a-types.md), [Errors](30-errors.md).
