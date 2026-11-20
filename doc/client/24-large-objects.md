# Large Objects

[Contents](README.md)

## NAME

`TurborelLargeObject`, a CLOB or BLOB value, and the connection's large
object requests: read, create, append, free and hold.

## SYNOPSIS

```csharp
namespace Turborel.Data.Types;

public sealed record TurborelLargeObject
{
    public LargeObjectKind Kind { get; }
    public ReadOnlyMemory<byte>? Data { get; }
    public ulong Octets { get; }
    public ulong Characters { get; }
    public bool IsLocator { get; }
    public static TurborelLargeObject Whole(LargeObjectKind kind, ReadOnlyMemory<byte> octets);
}
public enum LargeObjectKind { Character, Binary }

namespace Turborel.Data.Session;

public sealed partial class NativeConnection
{
    public const int ChunkOctets = 262_144;
    public const int LobInlineOctets = 65_536;
    public Task<byte[]> ReadLobAsync(TurborelLargeObject lob, ulong from, ulong count, bool characters,
        CancellationToken cancellationToken = default);
    public LobReadStream OpenLobReader(TurborelLargeObject lob);
    public Task<LobWriteStream> CreateLobAsync(LargeObjectKind kind, CancellationToken cancellationToken = default);
    public Task FreeLobAsync(TurborelLargeObject lob, CancellationToken cancellationToken = default);
    public Task HoldLobAsync(TurborelLargeObject lob, CancellationToken cancellationToken = default);
}
public sealed class LobReadStream : Stream;
public sealed class LobWriteStream : Stream
{
    public Task<TurborelLargeObject> FinishAsync(CancellationToken cancellationToken = default);
}
```

## DESCRIPTION

A large object of at most SERVER.LOB_INLINE_BYTES, 64 KiB by default,
comes whole, its octets in `Data`, UTF-8 of a CLOB. A longer one comes
by its locator, with its lengths in octets and, of a CLOB, characters;
it is valid until the transaction it was given in ends, and a result
that sent one keeps its transaction open, under autocommit too.

`ReadLobAsync` reads at most a chunk, 256 KiB, from `from`, at most
`count` octets, or of a CLOB, with `characters`, whole characters;
fewer than asked only at its end, or at a chunk's. One held whole is read
from the value itself. `OpenLobReader` gives the octets as a stream that
reads a chunk at a time, and does not seek.

`CreateLobAsync` creates a large object and gives a stream that writes
it, sending an Append of at most a chunk each time a chunk is pending, a
CLOB's cut where a character ends, so a character split between writes
is sent whole. `FinishAsync` sends what is pending and gives the value,
by its locator, to send as a parameter. `FreeLobAsync` frees a locator,
which is no longer valid, nor the value it named; `HoldLobAsync` keeps
one valid past its transaction's commit, until it is freed or a
transaction is rolled back.

The low-level client is asynchronous (RULES.md D15); as .NET streams,
`LobReadStream` and `LobWriteStream` read and write synchronously too,
by waiting on their requests.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 0F001 | -301 | A locator no longer valid, freed or of a transaction ended |
| 22000 | -301 | A large object that came whole freed or held: it has no locator |
| (the server's) | | A Create or an Append the server refuses |
| TB910 | -910 | A null where a value is needed; a stream asked to seek, or the reader to write |
| 08006 | -919 | A reply out of place, the connection lost; a reader given less than the large object's length |

## EXAMPLES

See [Large Objects](../user/06-large-objects.md).

## STANDARD

[PROTOCOL Lob 'L'] and [PROTOCOL Values] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Large objects] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[Values](23-values.md), [Copy, Import and Export](25-copy.md).
