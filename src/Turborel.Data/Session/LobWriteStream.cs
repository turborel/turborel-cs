// A new large object written a piece at a time as a .NET stream, sent in
// Appends of a chunk, a character one cut where a character ends so a
// character split between writes is sent whole; FinishAsync gives the
// value to send as a parameter, by its locator [CLIENT Large objects]
// [PROTOCOL Lob 'L'].

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Session;

/// <summary>A new large object, written as a stream that writes and does not seek.</summary>
/// <remarks>
/// The low-level client is asynchronous (RULES.md D15); as a .NET stream
/// it writes synchronously too, by waiting on its writes.
/// </remarks>
public sealed class LobWriteStream : Stream
{
    private readonly NativeConnection _connection;
    private readonly LargeObjectKind _kind;
    private readonly uint _locator;
    private readonly List<byte> _pending = [];
    private ulong _octets;
    private ulong _characters;

    /// <summary>Initializes a new instance of the <see cref="LobWriteStream"/> class.</summary>
    /// <param name="connection">The connection it is created over.</param>
    /// <param name="kind">Whether it is of characters or octets.</param>
    /// <param name="locator">Its locator.</param>
    internal LobWriteStream(NativeConnection connection, LargeObjectKind kind, uint locator)
    {
        _connection = connection;
        _kind = kind;
        _locator = locator;
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => (long)_octets;

    /// <inheritdoc/>
    public override long Position
    {
        get => (long)_octets;
        set => throw Misuse.Of("[DESIGN] a large object's writer does not seek");
    }

    /// <summary>Sends what is written and not yet sent, and gives the large object, to send as a parameter by its locator: valid until its transaction ends.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The large object.</returns>
    /// <exception cref="TurborelException">The server's refusal of an Append; a connection failure.</exception>
    public async Task<TurborelLargeObject> FinishAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return TurborelLargeObject.ByLocator(_kind, _locator, (_octets, _characters));
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _pending.AddRange(buffer.Span);
        _octets += (ulong)buffer.Length;
        if (_kind == LargeObjectKind.Character)
        {
            // Each character counted at its lead octet, so one a write splits is counted once.
            foreach (byte b in buffer.Span)
            {
                _characters += (b & 0xC0) != 0x80 ? 1UL : 0UL;
            }
        }

        while (_pending.Count >= NativeConnection.ChunkOctets)
        {
            int cut = _kind == LargeObjectKind.Character ? CharacterCut(_pending, NativeConnection.ChunkOctets) : NativeConnection.ChunkOctets;
            await AppendAsync(Math.Clamp(cut, 1, _pending.Count), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count > 0)
        {
            await AppendAsync(_pending.Count, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override void Flush() => FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw Misuse.Of("[DESIGN] a large object's writer is not read");

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw Misuse.Of("[DESIGN] a large object's writer does not seek");

    /// <inheritdoc/>
    public override void SetLength(long value) => throw Misuse.Of("[DESIGN] a large object's writer does not seek");

    /// <summary>
    /// The longest start of <paramref name="bytes"/>, at most <paramref name="most"/>
    /// octets, of whole UTF-8 characters: where a chunk of a character large
    /// object is cut, a character the end splits left for the next, as
    /// turborel's char_cut cuts it.
    /// </summary>
    /// <param name="bytes">The octets.</param>
    /// <param name="most">The most octets.</param>
    /// <returns>The cut.</returns>
    internal static int CharacterCut(IReadOnlyList<byte> bytes, int most)
    {
        int cut = Math.Min(most, bytes.Count);
        if (cut < bytes.Count)
        {
            while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
            {
                cut--;
            }

            return cut;
        }

        for (int back = 1; back <= 4 && cut - back >= 0; back++)
        {
            byte lead = bytes[cut - back];
            if ((lead & 0xC0) != 0x80)
            {
                int width = (lead & 0xE0) == 0xC0 ? 2 : (lead & 0xF0) == 0xE0 ? 3 : (lead & 0xF8) == 0xF0 ? 4 : 1;
                return cut - back + width > cut ? cut - back : cut;
            }
        }

        return cut;
    }

    private async Task AppendAsync(int count, CancellationToken cancellationToken)
    {
        byte[] bytes = [.. _pending.GetRange(0, count)];
        _pending.RemoveRange(0, count);
        await _connection.LobCompletedAsync(new LobRequest.Append(_locator, bytes), cancellationToken).ConfigureAwait(false);
    }
}
