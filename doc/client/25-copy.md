# Copy, Import and Export

[Contents](README.md)

## NAME

The connection's bulk copy, import from a source of records, and export
of a query's rows.

## SYNOPSIS

```csharp
namespace Turborel.Data.Session;

public sealed partial class NativeConnection
{
    public Task<Copying> CopyInAsync(string table, IReadOnlyList<string> columns, CancellationToken cancellationToken = default);
    public Task<ulong> CopyRowsAsync(string table, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows,
        CancellationToken cancellationToken = default);
    public Task<Imported> ImportAsync(IImportSource source, ImportOptions how, Action<ImportRejected>? rejected = null,
        CancellationToken cancellationToken = default);
    public Task<ulong> ExportAsync(string sql, Stream output, ExportFormat format, CancellationToken cancellationToken = default);
}
public sealed class Copying : IAsyncDisposable
{
    public const int ChunkBytes = 262_144;
    public Task SendAsync(IReadOnlyList<object?> row, CancellationToken cancellationToken = default);
    public Task<ulong> FinishAsync(CancellationToken cancellationToken = default);
    public Task AbandonAsync(CancellationToken cancellationToken = default);
}
public enum ExportFormat { Csv, Native }
public static class NativeRows
{
    public static Task<(IReadOnlyList<Column> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)> ReadAsync(
        Stream input, CancellationToken cancellationToken = default);
}

namespace Turborel.Data.Import;

public interface IImportSource
{
    ValueTask<IReadOnlyList<ImportHeading>?> HeadingsAsync(CancellationToken cancellationToken = default);
    ValueTask<ImportRecord?> NextAsync(CancellationToken cancellationToken = default);
}
public sealed class DelimitedSource(Stream input, DelimitedDialect dialect) : IImportSource;
public sealed record DelimitedDialect
{
    public const int DefaultMaxRecord = 16_777_216;
    public static DelimitedDialect Csv();
    public static DelimitedDialect Tsv();
    public DelimitedDialect WithDelimiter(char delimiter);
    public DelimitedDialect WithHeader();
    public DelimitedDialect WithNull(string text);
    public DelimitedDialect WithMaxRecord(int bytes);
}
public readonly record struct ImportPosition(ulong Record, ulong? Line);
public sealed record ImportHeading(string Name, DataType? Type = null);
public sealed record ImportRecord(IReadOnlyList<object?> Values, ImportPosition Position);
public sealed record ImportOptions(string Table) { ulong Batch = 1000; bool Skip; }
public sealed record Imported(ulong Rows, ulong Rejected);
public sealed record ImportRejected(ImportPosition Position, TurborelException Cause);
public sealed record ImportFailure(ImportPosition? Position, ulong Committed, ulong RolledBack)
{
    public static TurborelException Input(ImportPosition? position, string message);
}
```

## DESCRIPTION

### Copy

`CopyInAsync`, once `copy` is granted, begins a copy into `table`, as
SQL names it, of `columns` in order, all of the table's if none; under
`Autocommit` the copy is its own transaction. Until it ends the
connection sends only rows, and Ping. `SendAsync` sends a row, with those
before it once they come to about `ChunkBytes`, with no reply.
`FinishAsync` ends the copy and gives the rows inserted; `AbandonAsync`
undoes every row; a copy disposed before it ends is abandoned. The copy
is all or nothing: the table or a column not found, a row of too many or
too few values, a value its column refuses, fails `FinishAsync`, every
row undone, the failure's detail naming the row, as `row 7 of the copy`.
`CopyRowsAsync` copies the rows an enumerable gives.

### Import

`ImportAsync` inserts a source's records into the table `how` names, as
SQL writes a table's name. The source's headings name the columns, read
as identifiers, a regular one in upper case; with none, its fields are
the table's columns in order. A batch of `Batch` rows is committed at a
time, all at once if 0. The session must have no transaction active.

Where `copy` is granted, `Skip` is not set and no column is a large
object's, the rows go a batch to a copy, each committing as it ends, a
row the copy refuses named by its failure's detail. Else they go through
a prepared INSERT; a field longer than 64 KiB for a CLOB or BLOB column
is sent by creating a large object. A string for a character string
column is assigned to it as INSERT assigns it, so one too long is
refused, SQLSTATE 22001, not cut; any other value is cast by the server.
With `Skip`, a row refused, by the server, as a string too long, or of
too many or too few fields, is skipped and given to `rejected`, while
its batch's transaction goes on.

A failure stops the import, the batch under way rolled back. It is the
cause's `TurborelException`, its `Import` saying where the record began,
the rows committed and the rows rolled back. A source refuses what it
cannot read by `ImportFailure.Input`, SQLSTATE 22000, at its position.

`DelimitedSource` reads CSV as RFC 4180 §2 defines it, or tab-separated
text, a record a line, in bounded memory: a record longer than
`MaxRecord`, 16 MiB by default, is refused once that much is read. A CSV
field may be enclosed in double quotes, and must be to hold the
delimiter, a quote or a line break, a quote in it written twice; a quote
in a field not enclosed, or anything but the delimiter after a closing
quote, is refused. A field written as the dialect's null, not enclosed,
is null; by default an empty one, so `,,` is a null and `,"",` the empty
string. Text is UTF-8; a byte order mark beginning the input is passed
over.

### Export

`ExportAsync` runs a query and writes its rows to `output` as they come,
a batch at a time, and gives how many. As `Csv`: a header of the
columns' names, then a record for each row, each ended by CR LF, a field
quoted where it holds a comma, a quote, a line break or spaces at either
end, or is empty, a quote in it written twice; a null an empty field,
and any other value its text, as turborel shows it. As `Native`: the
protocol's own frames, a Description, then Rows, then a Complete, which
`NativeRows.ReadAsync` reads back, every value as it was.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the server's) | | A copy's or an import's row refused, its detail naming a copy's row; a table not found |
| 22000 | -301 | An import's input that cannot be read or does not fit the columns; a transaction active; a table not named right |
| 22001 | -301 | An import's string too long for its column |
| 08006 | -919 | `copy` not granted, before anything is sent; an export of a statement that is no query, or whose write failed; input that is not native rows; the connection lost |
| TB910 | -910 | A null where a value is needed; a delimiter not one ASCII character other than a quote or a line break |

## EXAMPLES

See [Copying, Importing and Exporting](../user/07-copying.md).

## STANDARD

[PROTOCOL CopyIn 'I'], [PROTOCOL CopyData 'J'], [PROTOCOL CopyEnd 'N'],
[PROTOCOL Capabilities] and [PROTOCOL Conversations] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Copying and exporting] and [CLIENT Importing] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md);
RFC 4180 for CSV.

## SEE ALSO

[Large Objects](24-large-objects.md), [Statements and Cursors](22-statements.md).
