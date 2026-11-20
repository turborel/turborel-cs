// A large object's octets as a .NET stream, read a chunk at a time over
// its connection [CLIENT Large objects]: one held whole read from the
// value, one by its locator by Lob reads of at most a chunk each.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Session;

/// <summary>A large object's octets, as a stream that reads and does not seek.</summary>
/// <remarks>
/// The low-level client is asynchronous (RULES.md D15); as a .NET stream
/// it reads synchronously too, by waiting on its reads.
/// </remarks>
public sealed class LobReadStream : Stream
{
    private readonly NativeConnection _connection;
    private readonly TurborelLargeObject _lob;
    private ulong _at;
    private byte[] _chunk = [];
    private int _taken;

    /// <summary>Initializes a new instance of the <see cref="LobReadStream"/> class.</summary>
    /// <param name="connection">The connection its locator is of.</param>
    /// <param name="lob">The large object.</param>
    internal LobReadStream(NativeConnection connection, TurborelLargeObject lob)
    {
        _connection = connection;
        _lob = lob;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => (long)Total;

    /// <inheritdoc/>
    public override long Position
    {
        get => (long)_at - (_chunk.Length - _taken);
        set => throw Misuse.Of("[DESIGN] a large object's reader does not seek");
    }

    private ulong Total => _lob.Data is { } held ? (ulong)held.Length : _lob.Octets;

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_taken >= _chunk.Length)
        {
            if (_at >= Total || buffer.IsEmpty)
            {
                return 0;
            }

            _chunk = await _connection.ReadLobAsync(_lob, _at, NativeConnection.ChunkOctets, characters: false, cancellationToken).ConfigureAwait(false);
            if (_chunk.Length == 0)
            {
                throw ConnectionFailure.Of("[DESIGN] the server sent less of the large object than its length");
            }

            _at += (ulong)_chunk.Length;
            _taken = 0;
        }

        int n = Math.Min(_chunk.Length - _taken, buffer.Length);
        _chunk.AsMemory(_taken, n).CopyTo(buffer);
        _taken += n;
        return n;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override void Flush()
    {
        // A reader holds nothing to write.
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw Misuse.Of("[DESIGN] a large object's reader does not seek");

    /// <inheritdoc/>
    public override void SetLength(long value) => throw Misuse.Of("[DESIGN] a large object's reader is not written");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw Misuse.Of("[DESIGN] a large object's reader is not written");
}
