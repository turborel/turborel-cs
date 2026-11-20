// A copy under way over its connection [CLIENT Copying and exporting]:
// rows encoded as they are sent, a CopyData written once they come to
// about a chunk, and CopyEnd to finish or abandon it [PROTOCOL CopyData
// 'J'] [PROTOCOL CopyEnd 'N']. A copy disposed before it ends is
// abandoned, so the connection takes requests again.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>A copy into a table under way.</summary>
public sealed class Copying : IAsyncDisposable
{
    /// <summary>The bytes of rows a CopyData carries at most, about: 256 KiB.</summary>
    public const int ChunkBytes = 256 * 1024;

    private readonly NativeConnection _connection;
    private readonly List<IReadOnlyList<object?>> _waiting = [];
    private int _bytes;
    private bool _ended;

    /// <summary>Initializes a new instance of the <see cref="Copying"/> class.</summary>
    /// <param name="connection">Its connection.</param>
    internal Copying(NativeConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Sends <paramref name="row"/>, with those before it once they come to <see cref="ChunkBytes"/>.</summary>
    /// <param name="row">A value for each column, of the types the codec carries.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22000 for a value of no type the protocol carries; a connection failure. A row the server refuses comes as <see cref="FinishAsync"/>'s failure.</exception>
    public async Task SendAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(row);
        var size = new WireWriter();
        ValueCodec.WriteList(size, row);
        _bytes += size.Length;
        _waiting.Add(row);
        if (_bytes >= ChunkBytes)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Ends the copy, its rows sent.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The rows inserted.</returns>
    /// <exception cref="TurborelException">The copy's failure, every row of it undone: the table or a column not found, a row of too many or too few values, a value its column refuses, its detail naming the row, as <c>row 7 of the copy</c>; a connection failure.</exception>
    public async Task<ulong> FinishAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await EndAsync(abandon: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives the copy up, every row of it undone.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure.</exception>
    public async Task AbandonAsync(CancellationToken cancellationToken = default) =>
        _ = await EndAsync(abandon: true, cancellationToken).ConfigureAwait(false);

    /// <summary>Abandons the copy if it has not ended, so the connection takes requests again; a failure has lost the connection, which says so at its next request.</summary>
    /// <returns>The task.</returns>
    public async ValueTask DisposeAsync()
    {
        if (!_ended && !_connection.IsLost)
        {
            try
            {
                await AbandonAsync().ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                // The copy failed as it was abandoned; the connection says so, if it is lost.
            }
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_waiting.Count == 0)
        {
            return;
        }

        IReadOnlyList<object?>[] rows = [.. _waiting];
        _waiting.Clear();
        _bytes = 0;
        await _connection.SendAsync(new Request.CopyData(rows), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ulong> EndAsync(bool abandon, CancellationToken cancellationToken)
    {
        _ended = true;
        await _connection.SendAsync(new Request.CopyEnd(abandon), cancellationToken).ConfigureAwait(false);
        _connection.Copying = false;
        return (await _connection.CompletionAsync(cancellationToken).ConfigureAwait(false)).RowCount ?? 0;
    }
}
