// What a statement gives [CLIENT Running statements]: a query's columns
// and rows, or another statement's completion; what one streamed to a
// sink gives; a cursor's rows a batch at a time; and what a request of
// several statements gives, each outcome and the failure that stopped the
// rest [CLIENT Controlling the session].

using System.Collections.Generic;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>What a statement gave: <see cref="RowsOutcome"/> or <see cref="DoneOutcome"/>.</summary>
public abstract record Outcome
{
    // Only the kinds below are outcomes.
    private protected Outcome()
    {
    }
}

/// <summary>A query's columns and rows.</summary>
/// <param name="Columns">The columns.</param>
/// <param name="Rows">The rows, each a value for each column, null for SQL's null.</param>
public sealed record RowsOutcome(IReadOnlyList<Column> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows) : Outcome;

/// <summary>Any other statement's completion.</summary>
/// <param name="Completion">Its completion: SQLCODE 0 or 100, and its SQLSTATE.</param>
public sealed record DoneOutcome(Completed Completion) : Outcome;

/// <summary>What takes a query's rows as they come, a batch at a time.</summary>
public interface IRowSink
{
    /// <summary>Takes the columns of the rows that follow.</summary>
    /// <param name="columns">The columns.</param>
    public void Columns(IReadOnlyList<Column> columns);

    /// <summary>Takes a batch of rows.</summary>
    /// <param name="rows">The rows.</param>
    public void Rows(IReadOnlyList<IReadOnlyList<object?>> rows);
}

/// <summary>What a statement streamed to a sink gave: the rows a query sent it, or another statement's completion.</summary>
/// <param name="RowsSent">The rows that went to the sink, of a query; null of another statement.</param>
/// <param name="Completion">The completion of a statement not a query; null of a query.</param>
public sealed record Streamed(ulong? RowsSent, Completed? Completion);

/// <summary>Rows of a cursor, and whether it has more.</summary>
/// <param name="Rows">The rows.</param>
/// <param name="More">Whether the cursor has more, which a fetch reads; closed if not.</param>
public sealed record Batch(IReadOnlyList<IReadOnlyList<object?>> Rows, bool More);

/// <summary>What a request of several statements gave.</summary>
/// <param name="Outcomes">The outcomes of the statements that ran, in order.</param>
/// <param name="Failure">The failure of the statement that stopped the rest, if one did.</param>
public sealed record Ran(IReadOnlyList<Outcome> Outcomes, TurborelException? Failure);
