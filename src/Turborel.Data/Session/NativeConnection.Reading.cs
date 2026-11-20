// A statement's rows read a reply at a time, as the standard API's reader
// pulls them [CLIENT Running statements] [PROTOCOL Query 'Q']: a Query's
// first reply, its columns or its completion; then each Rows reply in
// turn, until its Complete, or a cursor's Suspended.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>The connection's rows read a reply at a time.</summary>
public sealed partial class NativeConnection
{
    /// <summary>Sends <paramref name="sql"/>, its statements one or more where <c>multi-statement</c> is granted, and reads the first statement's first reply.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The step: a query's columns, or another statement's completion.</returns>
    internal async Task<RowsStep> BeginQueryAsync(string sql, CancellationToken cancellationToken)
    {
        bool multi = _told.Has(Capabilities.MultiStatement);
        await SendAsync(new Request.Query(sql, Autocommit, _told.Attributes, multi), cancellationToken).ConfigureAwait(false);
        return await NextStatementAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the first reply of a Query's next statement, as its last Complete said more follow.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The step: a query's columns, or another statement's completion.</returns>
    internal async Task<RowsStep> NextStatementAsync(CancellationToken cancellationToken)
    {
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Description description => new RowsStep(description.Columns, null, RowsEnd.None),
            Reply.Complete complete => new RowsStep(null, null, complete.Completion.More ? RowsEnd.CompleteMore : RowsEnd.Complete),
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no result")),
        };
    }

    /// <summary>Reads the next reply of rows: a batch, or the statement's Complete, or a cursor's Suspended.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The step.</returns>
    internal async Task<RowsStep> NextRowsAsync(CancellationToken cancellationToken)
    {
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Rows rows => new RowsStep(null, rows.Batch, RowsEnd.None),
            Reply.Complete complete => new RowsStep(null, null, complete.Completion.More ? RowsEnd.CompleteMore : RowsEnd.Complete),
            Reply.Suspended => new RowsStep(null, null, RowsEnd.Suspended),
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server broke off its rows")),
        };
    }

    /// <summary>Sends an Execute opening a cursor of <paramref name="statement"/>, its first rows to be read by <see cref="NextRowsAsync"/>.</summary>
    /// <param name="statement">The statement, a query.</param>
    /// <param name="values">One value for each parameter.</param>
    /// <param name="rows">The most rows sent now.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal Task BeginCursorAsync(PreparedStatement statement, IReadOnlyList<object?> values, uint rows, CancellationToken cancellationToken)
    {
        bool ends = Autocommit && _told.Has(Capabilities.FetchEnds);
        return SendAsync(new Request.Execute(new StatementCursor(statement.Number), values, rows, ends, _told.Attributes), cancellationToken);
    }

    /// <summary>Sends a Fetch of a cursor's next rows, to be read by <see cref="NextRowsAsync"/>.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="rows">The most rows sent.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal Task BeginFetchAsync(PreparedStatement statement, uint rows, CancellationToken cancellationToken) =>
        SendAsync(new Request.Fetch(new StatementCursor(statement.Number), rows), cancellationToken);
}

/// <summary>How a reply read a step at a time ends what it reads.</summary>
internal enum RowsEnd
{
    /// <summary>It does not: the columns or rows follow.</summary>
    None,

    /// <summary>The statement is complete, the last of its Query.</summary>
    Complete,

    /// <summary>The statement is complete, another of its Query's following.</summary>
    CompleteMore,

    /// <summary>The cursor has more rows, to be fetched.</summary>
    Suspended,
}

/// <summary>One reply, read a step at a time.</summary>
/// <param name="Columns">A query's columns, if it gave them.</param>
/// <param name="Batch">A batch of rows, if it gave one.</param>
/// <param name="End">What it ends.</param>
internal readonly record struct RowsStep(IReadOnlyList<Column>? Columns, IReadOnlyList<IReadOnlyList<object?>>? Batch, RowsEnd End);
