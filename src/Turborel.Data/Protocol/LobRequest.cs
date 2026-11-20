// The Lob request, kind L, by its op: read, create, append, free and hold
// a large object by its locator [PROTOCOL Lob 'L'], the request.

using System;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>A Lob request, one of its five ops.</summary>
internal abstract record LobRequest : Request
{
    /// <summary>Op 0, read.</summary>
    private const byte ReadOp = 0;

    /// <summary>Op 1, create.</summary>
    private const byte CreateOp = 1;

    /// <summary>Op 2, append.</summary>
    private const byte AppendOp = 2;

    /// <summary>Op 3, free.</summary>
    private const byte FreeOp = 3;

    /// <summary>Op 4, hold.</summary>
    private const byte HoldOp = 4;

    /// <inheritdoc/>
    internal override byte Kind => RequestKinds.Lob;

    /// <summary>Read: locator, from, count, characters flag.</summary>
    /// <param name="Locator">The large object's locator.</param>
    /// <param name="From">The first octet, or character, from 0.</param>
    /// <param name="Count">The most octets, or characters.</param>
    /// <param name="Characters">Whether it reads characters, not octets.</param>
    internal sealed record Read(uint Locator, ulong From, ulong Count, bool Characters) : LobRequest
    {
        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(ReadOp);
            w.WriteU32(Locator);
            w.WriteU64(From);
            w.WriteU64(Count);
            w.WriteFlag(Characters);
        }
    }

    /// <summary>Create: kind, 0 character, 1 binary.</summary>
    /// <param name="Of">The kind of large object made.</param>
    internal sealed record Create(LargeObjectKind Of) : LobRequest
    {
        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(CreateOp);
            w.WriteU8((byte)Of);
        }
    }

    /// <summary>Append: locator, bytes.</summary>
    /// <param name="Locator">The locator Create gave.</param>
    /// <param name="Bytes">The next octets.</param>
    internal sealed record Append(uint Locator, ReadOnlyMemory<byte> Bytes) : LobRequest
    {
        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(AppendOp);
            w.WriteU32(Locator);
            w.WriteBytes(Bytes.Span);
        }
    }

    /// <summary>Free: locator.</summary>
    /// <param name="Locator">The locator ended.</param>
    internal sealed record Free(uint Locator) : LobRequest
    {
        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(FreeOp);
            w.WriteU32(Locator);
        }
    }

    /// <summary>Hold: locator.</summary>
    /// <param name="Locator">The locator kept past its transaction's commit.</param>
    internal sealed record Hold(uint Locator) : LobRequest
    {
        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(HoldOp);
            w.WriteU32(Locator);
        }
    }
}
