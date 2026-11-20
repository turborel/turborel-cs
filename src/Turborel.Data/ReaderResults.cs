// Where the standard API's reader takes its rows from (RULES.md D2): a
// statement's result read a reply at a time, a Query's or a cursor's,
// fetched a batch at a time; or a batch's results, gathered by a pipeline.
// Each says its columns, its next batch of rows, and how many rows its
// statement changed; closed early, a statement's rows are read to their
// end, or its cursor closed, so the connection takes the next.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>The results a reader reads.</summary>
internal abstract class ReaderResults
{
    /// <summary>Gets the columns of the result read; none of a statement not a query.</summary>
    internal abstract IReadOnlyList<Column> Columns { get; }

    /// <summary>Gets the rows the result's statement changed; -1 of a query.</summary>
    internal abstract int RecordsAffected { get; }

    /// <summary>Reads the result's next batch of rows; null at its end.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The batch, or null.</returns>
    internal abstract Task<IReadOnlyList<IReadOnlyList<object?>>?> NextBatchAsync(CancellationToken cancellationToken);

    /// <summary>Moves to the next result, if any.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>Whether there is one.</returns>
    internal abstract Task<bool> NextResultAsync(CancellationToken cancellationToken);

    /// <summary>Reads what is left, so the connection takes the next statement.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal abstract Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A statement's results, read a reply at a time: a Query's, one for each
/// statement of its text, or a cursor's, fetched a batch at a time.
/// </summary>
internal sealed class StatementResults : ReaderResults
{
    private readonly NativeConnection _connection;
    private readonly PreparedStatement? _cursor;
    private readonly bool _deallocate;
    private readonly uint _fetch;
    private IReadOnlyList<Column> _columns = [];
    private RowsEnd _state;
    private long _affected = -1;

    /// <summary>Initializes a new instance of the <see cref="StatementResults"/> class, of a Query's first reply read.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="first">The first reply.</param>
    internal StatementResults(NativeConnection connection, RowsStep first)
    {
        _connection = connection;
        Begin(first);
    }

    /// <summary>Initializes a new instance of the <see cref="StatementResults"/> class, of a statement executed.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="cursor">The statement whose cursor is read, its rows to come; null of a statement completed.</param>
    /// <param name="fetch">The rows each fetch asks for.</param>
    /// <param name="deallocate">Whether the statement is destroyed as the reader closes.</param>
    internal StatementResults(NativeConnection connection, PreparedStatement cursor, uint fetch, bool deallocate)
    {
        (_connection, _cursor, _fetch, _deallocate) = (connection, cursor, fetch, deallocate);
        (_columns, _state) = (cursor.Columns, RowsEnd.None);
    }

    /// <summary>Initializes a new instance of the <see cref="StatementResults"/> class, of a statement executed and complete.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="completion">Its completion.</param>
    internal StatementResults(NativeConnection connection, Completed completion)
    {
        _connection = connection;
        (_columns, _state) = ([], RowsEnd.Complete);
        Count(completion.RowCount);
    }

    /// <inheritdoc/>
    internal override IReadOnlyList<Column> Columns => _columns;

    /// <inheritdoc/>
    internal override int RecordsAffected => (int)System.Math.Min(_affected, int.MaxValue);

    /// <summary>Gets a value indicating whether the statement's first reply is yet to be read: a cursor's, opened.</summary>
    internal bool Waiting { get => _cursor is not null && _state == RowsEnd.None && !field; private set; }

    /// <summary>Gets or sets the timer bounding the statement until its first reply comes.</summary>
    internal System.IDisposable? Timer { get; set; }

    /// <inheritdoc/>
    internal override async Task<IReadOnlyList<IReadOnlyList<object?>>?> NextBatchAsync(CancellationToken cancellationToken)
    {
        while (_columns.Count > 0 && _state is RowsEnd.None or RowsEnd.Suspended)
        {
            if (_state == RowsEnd.Suspended)
            {
                await Guarded(_connection.BeginFetchAsync(_cursor!, _fetch, cancellationToken)).ConfigureAwait(false);
                _state = RowsEnd.None;
            }

            RowsStep step = await Guarded(_connection.NextRowsAsync(cancellationToken)).ConfigureAwait(false);
            Replied();
            if (step.Batch is not null)
            {
                return step.Batch;
            }

            _state = Ended(step.End);
        }

        return null;
    }

    /// <inheritdoc/>
    internal override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        await DrainAsync(cancellationToken).ConfigureAwait(false);
        if (_state != RowsEnd.CompleteMore)
        {
            _columns = [];
            return false;
        }

        Begin(await Guarded(_connection.NextStatementAsync(cancellationToken)).ConfigureAwait(false));
        return true;
    }

    /// <inheritdoc/>
    internal override async Task CloseAsync(CancellationToken cancellationToken)
    {
        Replied();
        if (_connection.IsLost)
        {
            return;
        }

        while (await NextResultAsync(cancellationToken).ConfigureAwait(false))
        {
        }

        if (_deallocate && _cursor is not null)
        {
            await _connection.DeallocateAsync(_cursor, cancellationToken).ConfigureAwait(false);
        }

        // Under autocommit a cursor's transaction ends with it; a server that
        // grants no fetch-ends leaves it to the client.
        if (_cursor is not null && _connection.Autocommit && _connection.InTransaction)
        {
            _ = await _connection.QueryAsync("COMMIT", cancellationToken).ConfigureAwait(false);
        }
    }

    // Takes a statement's first reply: its columns, or its completion,
    // whose rows changed are counted.
    private void Begin(RowsStep first)
    {
        (_columns, _state) = (first.Columns ?? [], first.End);
        if (first.Columns is null)
        {
            Count(_connection.RowsAffected);
        }
    }

    // Counts a statement's rows changed, if it says.
    private void Count(ulong? rows)
    {
        if (rows is ulong n)
        {
            _affected = System.Math.Max(_affected, 0) + (long)System.Math.Min(n, int.MaxValue);
        }
    }

    // What a reply ends: a Suspended of a Query's rows, which has no
    // cursor to fetch from, ends them, as turborel-client's query reads it.
    private void Replied()
    {
        Waiting = true;
        Timer?.Dispose();
        Timer = null;
    }

    private RowsEnd Ended(RowsEnd end) => end == RowsEnd.Suspended && _cursor is null ? RowsEnd.Complete : end;

    // Reads the rest of the result's rows, a cursor's closed once it is suspended.
    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (_columns.Count > 0 && _state == RowsEnd.None)
        {
            _state = Ended((await Guarded(_connection.NextRowsAsync(cancellationToken)).ConfigureAwait(false)).End);
        }

        if (_state == RowsEnd.Suspended)
        {
            _state = RowsEnd.Complete;
            await _connection.CloseCursorAsync(_cursor!, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // What is read; a failure ends the statement's replies, so nothing more is waited for.
    private async Task<T> Guarded<T>(Task<T> reading)
    {
        try
        {
            return await reading.ConfigureAwait(false);
        }
        catch (TurborelException)
        {
            _state = RowsEnd.Complete;
            throw;
        }
    }

    private async Task Guarded(Task sending)
    {
        try
        {
            await sending.ConfigureAwait(false);
        }
        catch (TurborelException)
        {
            _state = RowsEnd.Complete;
            throw;
        }
    }
}

/// <summary>A batch's results, gathered: each statement's rows, or its rows changed.</summary>
/// <param name="outcomes">The outcomes, in order.</param>
internal sealed class GatheredResults(IReadOnlyList<Outcome> outcomes) : ReaderResults
{
    private int _at;
    private bool _given;

    /// <inheritdoc/>
    internal override IReadOnlyList<Column> Columns => outcomes.Count > 0 && outcomes[_at] is RowsOutcome rows ? rows.Columns : [];

    /// <inheritdoc/>
    internal override int RecordsAffected
    {
        get
        {
            long sum = 0;
            bool any = false;
            foreach (Outcome outcome in outcomes)
            {
                if (outcome is DoneOutcome { Completion.RowCount: ulong n })
                {
                    (sum, any) = (sum + (long)n, true);
                }
            }

            return any ? (int)System.Math.Min(sum, int.MaxValue) : -1;
        }
    }

    /// <inheritdoc/>
    internal override Task<IReadOnlyList<IReadOnlyList<object?>>?> NextBatchAsync(CancellationToken cancellationToken)
    {
        bool give = !_given && outcomes.Count > 0 && outcomes[_at] is RowsOutcome;
        _given = true;
        return Task.FromResult(give ? ((RowsOutcome)outcomes[_at]).Rows : null);
    }

    /// <inheritdoc/>
    internal override Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        bool next = _at + 1 < outcomes.Count;
        (_at, _given) = next ? (_at + 1, false) : (_at, true);
        return Task.FromResult(next);
    }

    /// <inheritdoc/>
    internal override Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
