// A copy under way over its connection [CLIENT Copying and exporting]:
// rows gathered as they are sent and written once they come to about a
// chunk, and CopyEnd to finish or abandon it [PROTOCOL CopyData 'J']
// [PROTOCOL CopyEnd 'N']. Granted columns, rows of a copy of the columns
// named whose every value is text or null, as many as the columns, go by
// column as CopyColumns, each cell the text CopyData would carry; any
// other row goes by row as CopyData, and a row of another kind than those
// gathered sends them first (RULES.md D24)
// [PROTOCOL CopyColumns 'O']. A copy disposed before it ends is
// abandoned, so the connection takes requests again.

using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly int _named;
    private readonly List<IReadOnlyList<object?>> _waiting = [];
    private int _bytes;
    private bool _ended;

    // The width of the rows gathered, if they go by column: each of its
    // values text or null, of columns granted.
    private int? _width;

    /// <summary>Initializes a new instance of the <see cref="Copying"/> class.</summary>
    /// <param name="connection">Its connection.</param>
    /// <param name="named">The columns the copy names; 0 for all of the table's.</param>
    internal Copying(NativeConnection connection, int named)
    {
        (_connection, _named) = (connection, named);
    }

    /// <summary>Sends <paramref name="row"/>, with those before it once they come to <see cref="ChunkBytes"/>.</summary>
    /// <param name="row">A value for each column, of the types the codec carries.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22000 for a value of no type the protocol carries; a connection failure. A row the server refuses comes as <see cref="FinishAsync"/>'s failure.</exception>
    public async Task SendAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(row);
        int? width = _connection.Has(Capabilities.Columns) && _named > 0 && row.Count == _named && row.All(v => v is null or string) ? row.Count : null;
        if (_waiting.Count > 0 && width != _width)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        _width = width;
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
        Request request = _width is null
            ? new Request.CopyData(rows)
            : new Request.CopyColumns([.. rows.Select(r => (IReadOnlyList<string?>)[.. r.Cast<string?>()])]);
        await _connection.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ulong> EndAsync(bool abandon, CancellationToken cancellationToken)
    {
        _ended = true;
        await _connection.SendAsync(new Request.CopyEnd(abandon), cancellationToken).ConfigureAwait(false);
        _connection.Copying = false;
        return (await _connection.CompletionAsync(cancellationToken).ConfigureAwait(false)).RowCount ?? 0;
    }
}
