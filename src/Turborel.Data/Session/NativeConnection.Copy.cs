// Bulk copy in [CLIENT Copying and exporting] [PROTOCOL CopyIn 'I']: rows
// sent into a table with no round trip but the copy's end. CopyInAsync
// begins one, naming the table and its columns; a Copying sends rows, in
// CopyData messages of about a chunk each, each with no reply; FinishAsync
// ends it [PROTOCOL CopyEnd 'N'], its reply the rows inserted, or the
// copy's failure, every row of it undone. A copy is all or nothing, within
// the transaction, or its own under autocommit.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>The connection's copies.</summary>
public sealed partial class NativeConnection
{
    /// <summary>Gets or sets a value indicating whether a copy is under way, which takes no other request until it ends.</summary>
    internal bool Copying { get; set; }

    /// <summary>
    /// Begins a copy into <paramref name="table"/>, as SQL names it, of
    /// <paramref name="columns"/>, in order, all of the table's if none: the
    /// copy the transaction's own under <see cref="Autocommit"/>.
    /// </summary>
    /// <param name="table">The table, as SQL names it.</param>
    /// <param name="columns">The columns, in order; none for all of them.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The copy under way, which takes no other request until it ends.</returns>
    /// <exception cref="TurborelException">A connection failure: <c>copy</c> not granted, or the server gone. The table not found, a column not of it, comes as <see cref="Copying.FinishAsync"/>'s failure.</exception>
    public Task<Copying> CopyInAsync(string table, IReadOnlyList<string> columns, CancellationToken cancellationToken = default) =>
        CopyBeginAsync(table, columns, Autocommit, cancellationToken);

    /// <summary>Copies <paramref name="rows"/> into <paramref name="table"/>'s <paramref name="columns"/>, as <see cref="CopyInAsync"/> does.</summary>
    /// <param name="table">The table, as SQL names it.</param>
    /// <param name="columns">The columns, in order; none for all of them.</param>
    /// <param name="rows">The rows, each a value for each column.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The rows inserted.</returns>
    /// <exception cref="TurborelException">As <see cref="CopyInAsync"/>'s and <see cref="Copying.FinishAsync"/>'s.</exception>
    public async Task<ulong> CopyRowsAsync(
        string table,
        IReadOnlyList<string> columns,
        IEnumerable<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(rows);
        Copying copying = await CopyInAsync(table, columns, cancellationToken).ConfigureAwait(false);
        await using (copying.ConfigureAwait(false))
        {
            foreach (IReadOnlyList<object?> row in rows)
            {
                await copying.SendAsync(row, cancellationToken).ConfigureAwait(false);
            }

            return await copying.FinishAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Begins a copy into a table's columns, its transaction ended with it if <paramref name="ends"/>.</summary>
    /// <param name="table">The table.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="ends">Whether the transaction ends with the copy.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The copy under way.</returns>
    internal async Task<Copying> CopyBeginAsync(string table, IReadOnlyList<string> columns, bool ends, CancellationToken cancellationToken)
    {
        Misuse.ThrowIfNull(table);
        Misuse.ThrowIfNull(columns);
        Needs(Capabilities.Copy);
        await SendAsync(new Request.CopyIn(table, columns, ends), cancellationToken).ConfigureAwait(false);
        Copying = true;
        return new Copying(this, columns.Count);
    }
}
