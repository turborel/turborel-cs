// A connection's statements [CLIENT Running statements] [CLIENT Prepared
// statements]: a direct statement run by Query, its rows all at once or
// given to a sink as they come [PROTOCOL Query 'Q']; and statements
// prepared once [PROTOCOL Prepare 'P'], executed [PROTOCOL Execute 'E'],
// or, a query, a cursor opened and read a batch at a time
// [PROTOCOL Fetch 'F'] and closed [PROTOCOL Close 'C'], described again
// [PROTOCOL Describe 'Y'] and destroyed [PROTOCOL Deallocate 'D'].

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>The connection's statements.</summary>
public sealed partial class NativeConnection
{

    /// <summary>
    /// Runs <paramref name="sql"/>, one direct SQL statement, in the session:
    /// a query's columns and all its rows, or another statement's completion;
    /// the transaction ended with it under <see cref="Autocommit"/>.
    /// </summary>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>What it gave.</returns>
    /// <exception cref="TurborelException">The statement's failure, as the server reports it; a connection failure.</exception>
    public async Task<Outcome> QueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        await SendAsync(new Request.Query(sql, Autocommit, _told.Attributes), cancellationToken).ConfigureAwait(false);
        return await QueryReplyAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives a pipeline of requests on this connection, empty, its failures its requests' own.</summary>
    /// <returns>The pipeline.</returns>
    public Pipeline Pipeline() => new(this);

    /// <summary>
    /// Runs <paramref name="sql"/> as <see cref="QueryAsync"/> does, a query's
    /// columns and rows given to <paramref name="sink"/> as they come, so no
    /// more than a batch is held.
    /// </summary>
    /// <param name="sql">The statement.</param>
    /// <param name="sink">What takes the rows.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The rows sent to the sink, or another statement's completion.</returns>
    /// <exception cref="TurborelException">As <see cref="QueryAsync"/>'s; a query's failure after some rows went to the sink.</exception>
    public async Task<Streamed> QueryIntoAsync(string sql, IRowSink sink, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(sink);
        await SendAsync(new Request.Query(sql, Autocommit, _told.Attributes), cancellationToken).ConfigureAwait(false);
        switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            case Reply.Complete complete:
                return new Streamed(null, Completed.Of(complete.Completion));
            case Reply.Description description:
                sink.Columns(description.Columns);
                break;
            case Reply.Error error:
                throw Failed(error.Failure);
            default:
                throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no result"));
        }

        ulong sent = 0;
        while (true)
        {
            switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                case Reply.Rows rows:
                    sent += (ulong)rows.Batch.Count;
                    sink.Rows(rows.Batch);
                    break;
                case Reply.Complete:
                    return new Streamed(sent, null);
                case Reply.Error error:
                    throw Failed(error.Failure);
                default:
                    throw Lose(ConnectionFailure.Of("[DESIGN] the server broke off its rows"));
            }
        }
    }

    /// <summary>Prepares <paramref name="sql"/>, a preparable statement whose dynamic parameters are written <c>?</c>.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The statement, its parameters' types and its columns described.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 37000 if it is not preparable, or a <c>?</c> takes no type where it stands; a connection failure.</exception>
    public async Task<PreparedStatement> PrepareAsync(string sql, CancellationToken cancellationToken = default)
    {
        uint number = NextStatement;
        NextStatement = unchecked(NextStatement + 1);
        await SendAsync(new Request.Prepare(number, sql), cancellationToken).ConfigureAwait(false);
        return await PreparedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes <paramref name="statement"/> with <paramref name="values"/>:
    /// a query's rows, all of them, or another statement's completion; the
    /// transaction ended with it under <see cref="Autocommit"/>.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="values">One value for each parameter, of the types the codec carries, null for SQL's null.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>What it gave.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 07001 if the values are not one for each parameter; the statement's; 22000 for a value of no type the protocol carries; a connection failure.</exception>
    public async Task<Outcome> ExecuteAsync(PreparedStatement statement, IReadOnlyList<object?> values, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        var request = new Request.Execute(new StatementCursor(statement.Number), values, 0, Autocommit, _told.Attributes);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ExecuteReplyAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the cursor numbered <paramref name="cursor"/> of <paramref name="statement"/>,
    /// a query, with <paramref name="values"/>: at most <paramref name="rows"/> of
    /// its rows, all if 0. It is read within its transaction; under
    /// <see cref="Autocommit"/>, with <c>fetch-ends</c> granted, the transaction
    /// ends as the cursor does: committed once its last row is read or it is
    /// closed, rolled back if a fetch fails.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="values">One value for each parameter.</param>
    /// <param name="rows">The most rows sent now; 0 for all.</param>
    /// <param name="cursor">The cursor's number: 0, or another once <c>cursors</c> is granted.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The first rows, and whether it has more.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 24000 if the cursor is open; those of <see cref="ExecuteAsync"/>; a connection failure, before anything is sent, for a number other than 0 without <c>cursors</c>.</exception>
    public async Task<Batch> OpenCursorAsync(
        PreparedStatement statement,
        IReadOnlyList<object?> values,
        uint rows,
        uint cursor = 0,
        CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        NeedsUnless(cursor == 0, Capabilities.Cursors);
        bool ends = Autocommit && _told.Has(Capabilities.FetchEnds);
        var request = new Request.Execute(new StatementCursor(statement.Number, cursor), values, rows, ends, _told.Attributes);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<IReadOnlyList<object?>> fetched, bool more) = await RowsAsync(cancellationToken).ConfigureAwait(false);
        return new Batch(fetched, more);
    }

    /// <summary>Reads at most <paramref name="rows"/> more rows of the cursor numbered <paramref name="cursor"/> of <paramref name="statement"/>, all if 0.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="rows">The most rows sent; 0 for all.</param>
    /// <param name="cursor">The cursor's number.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The rows, and whether it has more.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 24000 if the cursor is not open; a connection failure.</exception>
    public async Task<Batch> FetchAsync(PreparedStatement statement, uint rows, uint cursor = 0, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        NeedsUnless(cursor == 0, Capabilities.Cursors);
        await SendAsync(new Request.Fetch(new StatementCursor(statement.Number, cursor), rows), cancellationToken).ConfigureAwait(false);
        (IReadOnlyList<IReadOnlyList<object?>> fetched, bool more) = await RowsAsync(cancellationToken).ConfigureAwait(false);
        return new Batch(fetched, more);
    }

    /// <summary>Closes the cursor numbered <paramref name="cursor"/> of <paramref name="statement"/>.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="cursor">The cursor's number.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 24000 if it is not open; a connection failure.</exception>
    public async Task CloseCursorAsync(PreparedStatement statement, uint cursor = 0, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        NeedsUnless(cursor == 0, Capabilities.Cursors);
        await SendAsync(new Request.Close(new StatementCursor(statement.Number, cursor)), cancellationToken).ConfigureAwait(false);
        _ = await CompletionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Describes <paramref name="statement"/> again: its parameters' types and its columns, as they are now.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The statement, described again.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 26000 if the statement is no more; a connection failure, before anything is sent, without <c>describe</c>.</exception>
    public async Task<PreparedStatement> DescribeAsync(PreparedStatement statement, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        Needs(Capabilities.Describe);
        await SendAsync(new Request.Describe(new StatementCursor(statement.Number), OfCursor: false), cancellationToken).ConfigureAwait(false);
        return await PreparedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Describes the columns of the open cursor numbered <paramref name="cursor"/> of <paramref name="statement"/>.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="cursor">The cursor's number.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>Its columns.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 24000 if it is not open; a connection failure, before anything is sent, without <c>describe</c>.</exception>
    public async Task<IReadOnlyList<Column>> DescribeCursorAsync(PreparedStatement statement, uint cursor = 0, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        Needs(Capabilities.Describe);
        await SendAsync(new Request.Describe(new StatementCursor(statement.Number, cursor), OfCursor: true), cancellationToken).ConfigureAwait(false);

        // Its Description is its last reply, as no rows follow it.
        return await ReceiveAsync(cancellationToken, one: true).ConfigureAwait(false) switch
        {
            Reply.Description description => description.Columns,
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Description")),
        };
    }

    /// <summary>Destroys <paramref name="statement"/>, of no use after.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The server's: SQLSTATE 26000 if it is no more, 24000 if its cursor is open; a connection failure.</exception>
    public async Task DeallocateAsync(PreparedStatement statement, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(statement);
        await SendAsync(new Request.Deallocate(statement.Number), cancellationToken).ConfigureAwait(false);
        _ = await CompletionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets or sets the number the next statement prepared takes.</summary>
    internal uint NextStatement { get; set; }

    /// <summary>Gets how many times the session was renewed, by Reset, ChangeUser or a pool's reset, each renewal destroying every statement it prepared.</summary>
    internal ulong Renewals { get; set; }

    /// <summary>Notes the session renewed: its statements destroyed, their numbers begun again.</summary>
    internal void Renewed() => (NextStatement, Renewals) = (0, Renewals + 1);

    /// <summary>Gets the attributes a Query or an Execute carries now.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> QueryAttributesSent => _told.Attributes;

    /// <summary>The reply to a Query sent: a query's rows, or another statement's completion.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The outcome.</returns>
    internal async Task<Outcome> QueryReplyAsync(CancellationToken cancellationToken) =>
        await OutcomeAsync(cancellationToken, suspends: true).ConfigureAwait(false) is (Outcome outcome, _)
            ? outcome
            : throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no result"));

    /// <summary>The reply to an Execute of <paramref name="statement"/> sent: a query's rows, all of them, or another statement's completion.</summary>
    /// <param name="statement">The statement executed.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The outcome.</returns>
    internal async Task<Outcome> ExecuteReplyAsync(PreparedStatement statement, CancellationToken cancellationToken)
    {
        if (statement.Columns.Count == 0)
        {
            return new DoneOutcome(await CompletionAsync(cancellationToken).ConfigureAwait(false));
        }

        (IReadOnlyList<IReadOnlyList<object?>> rows, _) = await RowsAsync(cancellationToken).ConfigureAwait(false);
        return new RowsOutcome(statement.Columns, rows);
    }

    /// <summary>The server's Complete, of a request sent.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The completion.</returns>
    internal async Task<Completed> CompletionAsync(CancellationToken cancellationToken)
    {
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Complete => _told.Completed!,
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no completion")),
        };
    }

    /// <summary>
    /// One statement's outcome, of a Query sent, and whether another
    /// statement's replies follow, as a Query of several says; null if the
    /// first reply is of no statement.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <param name="suspends">Whether a Suspended ends the rows, as turborel-client's query reads them; of several statements it does not.</param>
    /// <returns>The outcome and whether more follow.</returns>
    internal async Task<(Outcome Outcome, bool More)?> OutcomeAsync(CancellationToken cancellationToken, bool suspends = false)
    {
        IReadOnlyList<Column> columns;
        switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
        {
            case Reply.Complete complete:
                return (new DoneOutcome(_told.Completed!), complete.Completion.More);
            case Reply.Description description:
                columns = description.Columns;
                break;
            case Reply.Error error:
                throw Failed(error.Failure);
            default:
                return null;
        }

        var rows = new List<IReadOnlyList<object?>>();
        while (true)
        {
            switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                case Reply.Rows batch:
                    rows.AddRange(batch.Batch);
                    break;
                case Reply.Complete complete:
                    return (new RowsOutcome(columns, rows), complete.Completion.More);
                case Reply.Suspended when suspends:
                    return (new RowsOutcome(columns, rows), false);
                case Reply.Error error:
                    throw Failed(error.Failure);
                default:
                    throw Lose(ConnectionFailure.Of("[DESIGN] the server broke off its rows"));
            }
        }
    }

    // The rows the server sends, until it says whether the cursor has more.
    private async Task<(IReadOnlyList<IReadOnlyList<object?>> Rows, bool More)> RowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyList<object?>>();
        while (true)
        {
            switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                case Reply.Rows batch:
                    rows.AddRange(batch.Batch);
                    break;
                case Reply.Complete:
                    return (rows, false);
                case Reply.Suspended:
                    return (rows, true);
                case Reply.Error error:
                    throw Failed(error.Failure);
                default:
                    throw Lose(ConnectionFailure.Of("[DESIGN] the server broke off its rows"));
            }
        }
    }

    // The statement the server's Prepared describes.
    private async Task<PreparedStatement> PreparedAsync(CancellationToken cancellationToken)
    {
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Prepared prepared => new PreparedStatement(prepared.Statement, prepared.Parameters, prepared.Columns),
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Prepared")),
        };
    }

    // A connection failure before anything is sent, unless ok or the capability was granted.
    private void NeedsUnless(bool ok, string capability)
    {
        if (!ok)
        {
            Needs(capability);
        }
    }
}
