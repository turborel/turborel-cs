// What an import reads and does [CLIENT Importing]: where a record began,
// a source's headings and records, how an import goes, what it did, a row
// it skipped, and what an import's failure kept. A source is any type that
// yields records of values one at a time, so memory is bounded by a
// record; DelimitedSource reads CSV and tab-separated text.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Types;

namespace Turborel.Data.Import;

/// <summary>Where in a source a record began.</summary>
/// <param name="Record">Its number among the source's records of data, the first 1, a header not counted.</param>
/// <param name="Line">The line it began on, the first 1, of a source made of lines.</param>
public readonly record struct ImportPosition(ulong Record, ulong? Line)
{
    /// <summary>Writes it as <c>record 3, line 4</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Line is ulong line
        ? string.Create(CultureInfo.InvariantCulture, $"record {Record}, line {line}")
        : string.Create(CultureInfo.InvariantCulture, $"record {Record}");
}

/// <summary>What a source says of one of its fields: a name, which names a column of the table, and a type, if it knows one.</summary>
/// <param name="Name">The name: read as an identifier if it is one, a regular one in upper case, else as written.</param>
/// <param name="Type">The type of its values, if the source knows it.</param>
public sealed record ImportHeading(string Name, DataType? Type = null);

/// <summary>A record of a source: a value for each of its fields, and where it began.</summary>
/// <param name="Values">The values; a source that knows no types gives strings, and null.</param>
/// <param name="Position">Where it began.</param>
public sealed record ImportRecord(IReadOnlyList<object?> Values, ImportPosition Position);

/// <summary>Where rows to import come from.</summary>
/// <remarks>
/// <see cref="HeadingsAsync"/> is asked once, before the first record; then
/// <see cref="NextAsync"/> until it gives none, and none again if asked after.
/// A source holds no more than a record at a time, and refuses a record
/// past a bound of its own. It refuses what it cannot read by
/// <see cref="ImportFailure.Input"/>.
/// </remarks>
public interface IImportSource
{
    /// <summary>Reads its fields' names and types, as of a header row; none if it has none, its fields then the table's columns in order.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The headings, or null.</returns>
    /// <exception cref="TurborelException">Why it cannot be read, and where, SQLSTATE 22000.</exception>
    public ValueTask<IReadOnlyList<ImportHeading>?> HeadingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the next record; null at the end.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The record, or null.</returns>
    /// <exception cref="TurborelException">Why it cannot be read, and where, SQLSTATE 22000: a source cannot go on after.</exception>
    public ValueTask<ImportRecord?> NextAsync(CancellationToken cancellationToken = default);
}

/// <summary>What an import is to do.</summary>
/// <param name="Table">The table the rows go into, as SQL writes its name.</param>
public sealed record ImportOptions(string Table)
{
    /// <summary>How many rows an import commits at a time unless told: 1000.</summary>
    public const ulong DefaultBatch = 1000;

    /// <summary>Gets how many rows are committed at a time; 0 for all at once.</summary>
    public ulong Batch { get; init; } = DefaultBatch;

    /// <summary>Gets a value indicating whether a row refused, by the server, as a string too long for its column, or of too many or too few fields, is skipped and given to the program, rather than ending the import.</summary>
    public bool Skip { get; init; }
}

/// <summary>What an import did.</summary>
/// <param name="Rows">How many rows were inserted and committed.</param>
/// <param name="Rejected">How many were skipped.</param>
public sealed record Imported(ulong Rows, ulong Rejected);

/// <summary>A row skipped: where it began, and why.</summary>
/// <param name="Position">Where its record began.</param>
/// <param name="Cause">Why it failed: the input's, SQLSTATE 22000, or an SQL condition.</param>
public sealed record ImportRejected(ImportPosition Position, TurborelException Cause);

/// <summary>What an import's failure kept: where its record began, the rows committed, and the rows of the batch under way rolled back.</summary>
/// <param name="Position">Where the record that stopped it began, if one did.</param>
/// <param name="Committed">How many rows were committed before.</param>
/// <param name="RolledBack">How many rows of the batch under way were rolled back.</param>
public sealed record ImportFailure(ImportPosition? Position, ulong Committed, ulong RolledBack)
{
    private const int DataSqlCode = -301;
    private const string DataSqlState = "22000";

    /// <summary>Gives the failure of input that cannot be read, or does not fit the columns, at <paramref name="position"/>: SQLCODE -301, SQLSTATE 22000.</summary>
    /// <param name="position">Where the record began, if one did.</param>
    /// <param name="message">What is wrong, in plain words.</param>
    /// <returns>The failure, for a source to throw.</returns>
    public static TurborelException Input(ImportPosition? position, string message) =>
        new(DataSqlCode, DataSqlState, message) { Import = new ImportFailure(position, 0, 0) };

    /// <summary>Writes it as <c>record 3, line 4: 2 rows committed, 1 rolled back</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        string where = Position is { } position ? $"{position}: " : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{where}{Rows(Committed)} committed, {RolledBack} rolled back");
    }

    /// <summary>Writes <paramref name="n"/> rows in words: <c>1 row</c>, <c>2 rows</c>.</summary>
    /// <param name="n">How many.</param>
    /// <returns>The words.</returns>
    internal static string Rows(ulong n) => string.Create(CultureInfo.InvariantCulture, $"{n} row{(n == 1 ? string.Empty : "s")}");
}
