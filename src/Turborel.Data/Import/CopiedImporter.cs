// An import by bulk copy [CLIENT Importing] [CLIENT Copying and
// exporting], as turborel-client's does: its rows checked as the INSERT's
// parameters take them, then sent a batch to a copy, each copy committing
// its batch as it ends, with no round trip but the copy's end. A row the
// server refuses fails its copy, every row of the batch undone, the
// failure's detail naming the row, so the import says its record as it
// would of a row inserted alone.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;

namespace Turborel.Data.Import;

/// <summary>An import by copy.</summary>
internal sealed class CopiedImporter
{
    /// <summary>What the server's detail of a row a copy refused begins with, and ends.</summary>
    private const string Row = "row ";
    private const string OfTheCopy = " of the copy";

    private readonly ImportOptions _how;
    private readonly IReadOnlyList<Parameter> _types;
    private readonly List<ImportPosition> _batch = [];
    private ulong _committed;

    private CopiedImporter(ImportOptions how, IReadOnlyList<Parameter> types)
    {
        (_how, _types) = (how, types);
    }

    /// <summary>Imports a source's records into a table's columns, as SQL names them, of the parameters' types given, over a connection with no transaction active.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="source">The source.</param>
    /// <param name="into">What the import is to do, the table and its columns, delimited.</param>
    /// <param name="types">The INSERT's parameters.</param>
    /// <param name="cancellationToken">Cancels the waits.</param>
    /// <returns>What it did.</returns>
    internal static async Task<Imported> ImportAsync(
        NativeConnection connection,
        IImportSource source,
        (ImportOptions How, string Table, IReadOnlyList<string> Columns) into,
        IReadOnlyList<Parameter> types,
        CancellationToken cancellationToken)
    {
        var run = new CopiedImporter(into.How, types);
        while (await run.CopiedAsync(connection, source, (into.Table, into.Columns), cancellationToken).ConfigureAwait(false))
        {
        }

        return new Imported(run._committed, 0);
    }

    /// <summary>The row of a copy a failure names, from 1, if it names one.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The row, or null.</returns>
    internal static int? RowOf(TurborelException failure)
    {
        string? detail = failure.Detail;
        if (detail is null || !detail.StartsWith(Row, System.StringComparison.Ordinal) || !detail.EndsWith(OfTheCopy, System.StringComparison.Ordinal))
        {
            return null;
        }

        string number = detail[Row.Length..^OfTheCopy.Length];
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int row) ? row : null;
    }

    // Copies a batch of records, its copy begun at its first record and
    // ended at its last, abandoned if a record fails first: whether records may follow.
    private async Task<bool> CopiedAsync(NativeConnection connection, IImportSource source, (string Table, IReadOnlyList<string> Columns) into, CancellationToken cancellationToken)
    {
        if (await CheckedAsync(source, cancellationToken).ConfigureAwait(false) is not { } first)
        {
            return false;
        }

        Copying copy;
        try
        {
            copy = await connection.CopyBeginAsync(into.Table, into.Columns, ends: true, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e) when (e.Import is null)
        {
            throw Importer.Stopped(e, null, _committed, 0);
        }

        await using (copy.ConfigureAwait(false))
        {
            (ImportPosition At, IReadOnlyList<object?> Values)? next = first;
            while (next is var (at, values))
            {
                try
                {
                    await copy.SendAsync(values, cancellationToken).ConfigureAwait(false);
                }
                catch (TurborelException e) when (e.Import is null)
                {
                    throw Importer.Stopped(e, at, _committed, (ulong)_batch.Count);
                }

                _batch.Add(at);
                if ((ulong)_batch.Count == _how.Batch)
                {
                    await EndedAsync(copy, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                next = await CheckedAsync(source, cancellationToken).ConfigureAwait(false);
            }

            await EndedAsync(copy, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    // The next record, checked as the INSERT's parameters take it.
    private async Task<(ImportPosition At, IReadOnlyList<object?> Values)?> CheckedAsync(IImportSource source, CancellationToken cancellationToken)
    {
        if (await Importer.RecordAsync(source, (_committed, (ulong)_batch.Count), cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return null;
        }

        if (record.Values.Count != _types.Count)
        {
            throw Importer.Stopped(Importer.Fields(record.Values.Count, _types.Count), record.Position, _committed, (ulong)_batch.Count);
        }

        try
        {
            return (record.Position, Assignment.Assigned(record.Values, _types));
        }
        catch (TurborelException e)
        {
            throw Importer.Stopped(e, record.Position, _committed, (ulong)_batch.Count);
        }
    }

    // Ends the batch's copy: its rows committed, or why it failed, at the row it names.
    private async Task EndedAsync(Copying copy, CancellationToken cancellationToken)
    {
        try
        {
            _committed += await copy.FinishAsync(cancellationToken).ConfigureAwait(false);
            _batch.Clear();
        }
        catch (TurborelException failure) when (failure.Import is null)
        {
            int? row = RowOf(failure) is int n && n > 0 && n <= _batch.Count ? n : null;
            ImportPosition? position = row is int r ? _batch[r - 1] : null;
            ulong before = row is int b ? (ulong)(b - 1) : (ulong)_batch.Count;
            throw Importer.Stopped(failure, position, _committed, before);
        }
    }
}
