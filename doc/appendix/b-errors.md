# Appendix B: Errors

[Contents](README.md)

Every failure reaches a program as a `TurborelException`, with its
SQLCODE and SQLSTATE ([Errors](../client/30-errors.md)).

## The driver's own failures

When the driver fails something itself, rather than giving the server's
failure, it uses the codes every Turborel driver uses, those
turborel-client gives the same case:

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08006 | -919 | A malformed frame, cut short, with bytes after its last field or of a kind not known; a frame too long, a reply out of place, the connection lost or timed out, TLS refused; a method of a capability not granted, the connection kept; a read timeout set on a connection its maker bounds |
| 28000 | -919 | A secret mechanism, a password or SCRAM, over plain TCP; a SCRAM exchange refused, as channel binding required and impossible, or a server that does not prove itself |
| TB910 | -910 | The API misused where turborel-client has no such case: a null where a value is needed; a large object's stream asked to seek; a delimiter not one ASCII character other than a quote or a line break; in the standard API, each misuse ADO.NET's providers answer with `InvalidOperationException`, as a command run on a closed connection or while a reader is open, a reader read before its first row, a transaction begun while one is, or a database changed (RULES.md D17) |
| 22000 | -301 | A value to send of a .NET type the protocol does not carry; a large object that came whole freed or held; an import's input that cannot be read or does not fit the columns, a transaction active, or a table not named right |
| 22003, 22032 | -301 | JSON text that is no JSON text, or a number out of range |
| 2200N | -301 | XML text that is not well-formed content |
| TB916 | -916 | A value read from the server that the client cannot take, in a frame read whole: a length's units or a truth value other than 0 or 1, damaged JSON, a Complete's condition that cannot be read, value tags 18 and 19. Its request fails, the rest of its replies read past first, and the connection goes on |
| 0A000 | -900 | A JSON value nested deeper than 128, or longer than 256 MiB: one to send refused before it is sent; one read, its request failed as TB916's is |

Two codes changed at milestone M3, to these: a null argument, which was
.NET's `ArgumentNullException`, and a value of a type the protocol does
not carry, which was SQLCODE -921 and SQLSTATE 07006.

## The server's failures

A failure the server sends keeps its SQLCODE and SQLSTATE. Its class,
the SQLSTATE's first two characters, says what failed, as the SQL
standard and Turborel's manual define it; among them:

| Class | What | The connection |
|---|---|---|
| 01 | A warning, as a statement's condition or a notice | kept |
| 02 | No data | kept |
| 07 | Dynamic SQL, as 07001 for values not one for each parameter | kept |
| 08 | A connection exception | lost, but 08003, which names another connection |
| 22 | A data exception | kept |
| 23 | A constraint violated | kept |
| 24 | An invalid cursor state | kept |
| 26 | An invalid statement name | kept |
| 28 | Invalid authorization | lost, as the connection is made |
| 2A, 42 | A syntax error or access rule violated | kept |
| 40 | A transaction rolled back | kept |
| TB | Turborel's own, as TB920 for a statement cancelled | kept |

## STANDARD

[PROTOCOL Error 'E'] and [PROTOCOL Errors and notices] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md).

## SEE ALSO

[Errors](../client/30-errors.md),
[Errors and Diagnostics](../user/10-errors.md).
