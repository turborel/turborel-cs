// A source's records inserted into a table [CLIENT Importing], as
// turborel-client's import does: through one prepared INSERT, its columns
// the source's headings, or else the table's, as a prepared query's
// description gives them; each value assigned as Assignment says, a long
// one for a large object column sent ahead by a locator. A batch of rows
// is committed at a time; a failure rolls the batch back and stops, or, if
// asked, a bad row is skipped and given to the program, and the import
// goes on. Where the server grants copy, no row is skipped and no column
// is a large object's, the rows go by bulk copy (CopiedImporter).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Types;

namespace Turborel.Data.Import;

/// <summary>An import through a prepared INSERT.</summary>
internal sealed class Importer
{
    private const int CancelledSqlCode = -920;

    private readonly NativeConnection _connection;
    private readonly PreparedStatement _insert;
    private readonly ImportOptions _how;
    private readonly Action<ImportRejected> _rejected;
    private ulong _committed;
    private ulong _inBatch;
    private ulong _skipped;

    private Importer(NativeConnection connection, PreparedStatement insert, ImportOptions how, Action<ImportRejected> rejected)
    {
        (_connection, _insert, _how, _rejected) = (connection, insert, how, rejected);
    }

    /// <summary>Imports a source's records, as <see cref="NativeConnection.ImportAsync"/> says.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="source">The source.</param>
    /// <param name="how">What the import is to do.</param>
    /// <param name="rejected">What takes each row skipped.</param>
    /// <param name="cancellationToken">Cancels the waits.</param>
    /// <returns>What it did.</returns>
    internal static async Task<Imported> ImportAsync(
        NativeConnection connection,
        IImportSource source,
        ImportOptions how,
        Action<ImportRejected> rejected,
        CancellationToken cancellationToken)
    {
        (PreparedStatement insert, string table, IReadOnlyList<string> columns) = await PreparedAsync(connection, source, how, cancellationToken).ConfigureAwait(false);
        if (connection.Has(Capabilities.Copy) && !how.Skip && !insert.Inputs.Any(p => p.Type is LargeObjectType))
        {
            // Its parameters' types known, the INSERT is of no more use.
            await Before(() => connection.DeallocateAsync(insert, cancellationToken)).ConfigureAwait(false);
            return await CopiedImporter.ImportAsync(connection, source, (how, table, columns), insert.Inputs, cancellationToken).ConfigureAwait(false);
        }

        var run = new Importer(connection, insert, how, rejected);
        TurborelException? stopped = null;
        Imported? imported = null;
        try
        {
            imported = await run.RowsAsync(source, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e) when (e.Import is not null)
        {
            stopped = e;
        }
        catch (Exception e) when (e is not TurborelException && !connection.IsLost)
        {
            // A program's source that failed its own way: the INSERT let go of all the same.
            await connection.DeallocateAsync(insert, cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (connection.IsLost)
        {
            throw stopped!;
        }

        // Let go of even if the import stopped, so the server holds nothing.
        try
        {
            await connection.DeallocateAsync(insert, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e) when (stopped is null && !connection.IsLost)
        {
            throw e.OfImport(new ImportFailure(null, imported!.Rows, 0));
        }

        return stopped is null ? imported! : throw stopped;
    }

    /// <summary>Stops an import at <paramref name="position"/>, for <paramref name="cause"/>.</summary>
    /// <param name="cause">Why.</param>
    /// <param name="position">Where its record began, if one did.</param>
    /// <param name="committed">The rows committed.</param>
    /// <param name="rolledBack">The rows of the batch under way rolled back.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Stopped(TurborelException cause, ImportPosition? position, ulong committed, ulong rolledBack) =>
        cause.OfImport(new ImportFailure(position, committed, rolledBack));

    /// <summary>Reads a source's next record, its failure stopping the import with what was kept.</summary>
    /// <param name="source">The source.</param>
    /// <param name="kept">The rows committed, and of the batch under way.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The record, or null at the end.</returns>
    internal static async Task<ImportRecord?> RecordAsync(IImportSource source, (ulong Committed, ulong InBatch) kept, CancellationToken cancellationToken)
    {
        try
        {
            return await source.NextAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e) when (e.Import is { } at)
        {
            throw Stopped(e, at.Position, kept.Committed, kept.InBatch);
        }
    }

    /// <summary>The failure of a record of too many or too few fields.</summary>
    /// <param name="fields">Its fields.</param>
    /// <param name="wanted">The columns.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Fields(int fields, int wanted) =>
        ImportFailure.Input(null, $"[DESIGN] {fields} fields, for {wanted} columns");

    // Runs a step of the import before any row: a failure stops it, nothing committed.
    private static async Task Before(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (TurborelException e) when (e.Import is null)
        {
            throw Stopped(e, null, 0, 0);
        }
    }

    // The INSERT that puts the source's records into the table, its table
    // name as SQL text, and its columns, delimited.
    private static async Task<(PreparedStatement Insert, string Table, IReadOnlyList<string> Columns)> PreparedAsync(
        NativeConnection connection,
        IImportSource source,
        ImportOptions how,
        CancellationToken cancellationToken)
    {
        if (connection.InTransaction)
        {
            throw ImportFailure.Input(null, "[DESIGN] an import begins with no transaction active, as it commits its batches");
        }

        string table = SqlNames.TableName(how.Table)
            ?? throw ImportFailure.Input(null, $"[ISO/IEC 9075:1992 §5.4] {how.Table} is not a <table name>");
        IReadOnlyList<ImportHeading>? headings = await source.HeadingsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> names = headings is null
            ? await DescribedAsync(connection, table, cancellationToken).ConfigureAwait(false)
            : [.. headings.Select(h => SqlNames.ColumnName(h.Name))];
        string[] columns = [.. names.Select(SqlNames.Delimited)];
        string marks = string.Join(", ", Enumerable.Repeat("?", columns.Length));
        PreparedStatement? insert = null;
        await Before(async () => insert = await connection.PrepareAsync($"INSERT INTO {table} ({string.Join(", ", columns)}) VALUES ({marks})", cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        return (insert!, table, columns);
    }

    // The names of a table's columns, in order, as a query of them describes them.
    private static async Task<IReadOnlyList<string>> DescribedAsync(NativeConnection connection, string table, CancellationToken cancellationToken)
    {
        PreparedStatement? query = null;
        await Before(async () => query = await connection.PrepareAsync($"SELECT * FROM {table}", cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Before(() => connection.DeallocateAsync(query!, cancellationToken)).ConfigureAwait(false);
        return [.. query!.Columns.Select(c => c.Name ?? string.Empty)];
    }

    // Inserts each record, then commits the last batch.
    private async Task<Imported> RowsAsync(IImportSource source, CancellationToken cancellationToken)
    {
        while (await NextAsync(source, cancellationToken).ConfigureAwait(false) is { } record)
        {
            await RowAsync(record, cancellationToken).ConfigureAwait(false);
        }

        await CommitAsync(cancellationToken).ConfigureAwait(false);
        return new Imported(_committed, _skipped);
    }

    // The source's next record; its failure stops the import, the batch
    // under way rolled back, as turborel-client's stop rolls it back.
    private async Task<ImportRecord?> NextAsync(IImportSource source, CancellationToken cancellationToken)
    {
        try
        {
            return await source.NextAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e)
        {
            throw await RolledBackAsync(e, e.Import?.Position).ConfigureAwait(false);
        }
        catch (Exception) when (!_connection.IsLost && _connection.InTransaction)
        {
            // A program's source that fails its own way: the batch is still undone.
            _ = await RolledBackAsync(ImportFailure.Input(null, "[DESIGN] the source failed"), null).ConfigureAwait(false);
            throw;
        }
    }

    // Inserts a record, committing its batch if it ends one.
    private async Task RowAsync(ImportRecord record, CancellationToken cancellationToken)
    {
        int wanted = _insert.Inputs.Count;
        if (record.Values.Count != wanted)
        {
            await RejectAsync(record.Position, Fields(record.Values.Count, wanted)).ConfigureAwait(false);
            return;
        }

        try
        {
            IReadOnlyList<object?> values = await LocatedAsync(Assignment.Assigned(record.Values, _insert.Inputs), cancellationToken).ConfigureAwait(false);
            _ = await _connection.ExecuteAsync(_insert, values, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException failure) when (failure.Import is null)
        {
            // A row is skipped only if it alone was undone: the batch's
            // transaction goes on, and the connection.
            bool alone = _inBatch == 0 || _connection.InTransaction;
            if (!alone || _connection.IsLost || failure.SqlCode == CancelledSqlCode)
            {
                throw await RolledBackAsync(failure, record.Position).ConfigureAwait(false);
            }

            await RejectAsync(record.Position, failure).ConfigureAwait(false);
            return;
        }

        _inBatch++;
        if (_inBatch == _how.Batch)
        {
            await CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // Each long value for a large object parameter, sent ahead a piece at a
    // time and given by its locator, so no request holds it whole.
    private async Task<IReadOnlyList<object?>> LocatedAsync(IReadOnlyList<object?> values, CancellationToken cancellationToken)
    {
        object?[] located = [.. values];
        for (int i = 0; i < located.Length; i++)
        {
            if (_insert.Inputs[i].Type is not LargeObjectType lob)
            {
                continue;
            }

            byte[]? bytes = located[i] switch
            {
                string s when Encoding.UTF8.GetByteCount(s) > NativeConnection.LobInlineOctets => Encoding.UTF8.GetBytes(s),
                byte[] b when b.Length > NativeConnection.LobInlineOctets => b,
                TurborelLargeObject { Data: { } held } when held.Length > NativeConnection.LobInlineOctets => held.ToArray(),
                _ => null,
            };
            if (bytes is not null)
            {
                LobWriteStream writer = await _connection.CreateLobAsync(lob.Kind, cancellationToken).ConfigureAwait(false);
                await using (writer.ConfigureAwait(false))
                {
                    await writer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    located[i] = await writer.FinishAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return located;
    }

    // Skips a row and gives it to the program, if bad rows are skipped; else stops.
    private async Task RejectAsync(ImportPosition position, TurborelException cause)
    {
        if (!_how.Skip)
        {
            throw await RolledBackAsync(cause, position).ConfigureAwait(false);
        }

        _skipped++;
        _rejected(new ImportRejected(position, cause.OfImport(new ImportFailure(position, 0, 0))));
    }

    // Stops the import at a record: the batch under way rolled back.
    private async Task<TurborelException> RolledBackAsync(TurborelException cause, ImportPosition? position)
    {
        if (!_connection.IsLost && _connection.InTransaction)
        {
            try
            {
                _ = await _connection.QueryAsync("ROLLBACK").ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                // The import has failed already: a rollback that fails too
                // loses the connection, which ends the session's transaction.
            }
        }

        return Stopped(cause, position, _committed, _inBatch);
    }

    // Commits the batch under way, if a transaction is active.
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_connection.InTransaction)
        {
            try
            {
                _ = await _connection.QueryAsync("COMMIT", cancellationToken).ConfigureAwait(false);
            }
            catch (TurborelException failure) when (failure.Import is null)
            {
                throw Stopped(failure, null, _committed, _inBatch);
            }
        }

        _committed += _inBatch;
        _inBatch = 0;
    }
}
