// Import [CLIENT Importing]: rows read from a source and inserted into a
// table over the connection, a batch to each transaction, through a
// prepared INSERT, or by bulk copy where the server grants it, no bad row
// is to be skipped and no column is a large object's.

using System;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Import;

namespace Turborel.Data.Session;

/// <summary>The connection's imports.</summary>
public sealed partial class NativeConnection
{
    /// <summary>
    /// Inserts <paramref name="source"/>'s records into the table
    /// <paramref name="how"/> names, as it says; each row skipped is given
    /// to <paramref name="rejected"/>. The session must have no transaction
    /// active, as an import commits its batches.
    /// </summary>
    /// <param name="source">Where the rows come from.</param>
    /// <param name="how">The table, the batch and whether bad rows are skipped.</param>
    /// <param name="rejected">What takes each row skipped, if any is.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The rows committed and skipped.</returns>
    /// <exception cref="TurborelException">
    /// Why it stopped, its <see cref="TurborelException.Import"/> saying where
    /// and how many rows were committed and rolled back: SQLSTATE 22000 for a
    /// transaction active, a table not named right, or input that cannot be
    /// read or does not fit the columns; the server's for a table not found,
    /// a row refused or a commit that fails; 22001 for a string too long
    /// for its column. The batch under way is rolled back.
    /// </exception>
    public Task<Imported> ImportAsync(
        IImportSource source,
        ImportOptions how,
        Action<ImportRejected>? rejected = null,
        CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(source);
        Misuse.ThrowIfNull(how);
        return Importer.ImportAsync(this, source, how, rejected ?? (_ => { }), cancellationToken);
    }
}
