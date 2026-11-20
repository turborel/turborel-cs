// The standard API's command (RULES.md D2, D18): a statement whose dynamic
// parameters are written `?`, given by place. One of no parameters runs
// as a direct statement, its rows read as they come; one of parameters,
// or prepared, is prepared and executed, a query's rows read through its
// cursor a batch at a time [CLIENT Running statements] [CLIENT Prepared
// statements]. CommandTimeout cancels the statement on the server once it
// runs out, as a Canceller cancels it [CLIENT Running statements].

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>A statement to run on a connection.</summary>
public sealed class TurborelCommand : DbCommand
{
    /// <summary>The rows a cursor's fetch asks for at once.</summary>
    internal const uint FetchRows = 1000;

    private const int DefaultTimeout = 30;

    private readonly List<(PreparedStatement Statement, NativeConnection On, ulong Renewals)> _stale = [];
    private (PreparedStatement Statement, NativeConnection On, ulong Renewals)? _kept;
    private bool _prepares;
    private string _text = string.Empty;

    /// <summary>Initializes a new instance of the <see cref="TurborelCommand"/> class.</summary>
    public TurborelCommand()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelCommand"/> class of a statement and a connection.</summary>
    /// <param name="commandText">The statement, its parameters written <c>?</c>.</param>
    /// <param name="connection">The connection.</param>
    public TurborelCommand(string commandText, TurborelConnection? connection = null)
    {
        (CommandText, Connection) = (commandText, connection);
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            Unprepare();
        }
    }

    /// <summary>Gets or sets the seconds the statement may run before it is cancelled; 0 for no limit; 30 by default.</summary>
    public override int CommandTimeout { get; set; } = DefaultTimeout;

    /// <summary>Gets or sets how the text is read: a statement alone, <see cref="CommandType.Text"/>.</summary>
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc/>
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <summary>Gets or sets the connection it runs on.</summary>
    public new TurborelConnection? Connection
    {
        get;
        set
        {
            field = value;
            Unprepare();
        }
    }

    /// <summary>Gets its parameters, in the order of the statement's <c>?</c> marks.</summary>
    public new TurborelParameterCollection Parameters { get; } = [];

    /// <summary>Gets or sets the transaction it runs in: the connection's, or none.</summary>
    public new TurborelTransaction? Transaction { get; set; }

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => Connection;
        set => Connection = value as TurborelConnection ?? (value is null ? null : throw Misuse.Of("[DESIGN] a command of turborel's runs on a TurborelConnection"));
    }

    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => Parameters;

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => Transaction;
        set => Transaction = value as TurborelTransaction ?? (value is null ? null : throw Misuse.Of("[DESIGN] a command of turborel's runs in a TurborelTransaction"));
    }

    /// <inheritdoc/>
    public override void Prepare() => PrepareAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Prepares the statement, so each run after executes it with no Prepare,
    /// until its text or connection changes; prepared again if its session
    /// was renewed, as a pooled connection is given back and taken again.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    public override async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        NativeConnection native = Ready(Connection ?? throw Misuse.Of("[DESIGN] the command has no connection: a command of a data source is prepared on none"));
        await DropStaleAsync(native, cancellationToken).ConfigureAwait(false);
        _prepares = true;
        _ = await KeptAsync(native, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        TurborelDataReader reader = await ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            await reader.CloseAsync().ConfigureAwait(false);
            return reader.RecordsAffected;
        }
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        TurborelDataReader reader = await ExecuteReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0 ? reader.GetValue(0) : null;
        }
    }

    /// <summary>Runs the statement and reads its rows as they come.</summary>
    /// <param name="behavior">How the reader behaves: <see cref="CommandBehavior.CloseConnection"/> closes the connection with it.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="TurborelException">The statement's failure; SQLSTATE TB910 for a command not ready to run.</exception>
    public new async Task<TurborelDataReader> ExecuteReaderAsync(CommandBehavior behavior = CommandBehavior.Default, CancellationToken cancellationToken = default)
    {
        if (Connection is not null || Source is not { } source)
        {
            return await ExecuteOnAsync(Connection ?? throw Misuse.Of("[DESIGN] the command has no connection"), behavior, cancellationToken).ConfigureAwait(false);
        }

        // A command of a data source runs on a connection of its own, closed with its reader.
        TurborelConnection opened = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteOnAsync(opened, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await opened.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gets or sets the data source a command made by it opens its connection from, as it runs with no connection.</summary>
    internal TurborelDataSource? Source { get; set; }

    // Runs the statement on a connection, reading its rows as they come.
    private async Task<TurborelDataReader> ExecuteOnAsync(TurborelConnection connection, CommandBehavior behavior, CancellationToken cancellationToken)
    {
        NativeConnection native = Ready(connection);
        if (CommandType != CommandType.Text)
        {
            throw Misuse.Of($"[DESIGN] a command of turborel's is a statement's text, not {CommandType}");
        }

        var timer = new StatementTimer(native, CommandTimeout);
        try
        {
            ReaderResults results = await RunAsync(native, cancellationToken).ConfigureAwait(false);
            if (results is StatementResults { Waiting: true } waiting)
            {
                // A cursor's first rows are read by the reader: until they come, the time runs.
                (waiting.Timer, timer) = (timer, null);
            }

            return new TurborelDataReader(connection, results, behavior.HasFlag(CommandBehavior.CloseConnection));
        }
        finally
        {
            timer?.Dispose();
        }
    }

    /// <summary>Runs the statement and reads its rows as they come.</summary>
    /// <param name="behavior">How the reader behaves.</param>
    /// <returns>The reader.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The reader is given to the caller, who disposes it.")]
    public new TurborelDataReader ExecuteReader(CommandBehavior behavior = CommandBehavior.Default) =>
        ExecuteReaderAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Cancels the statement running, from another thread, over a connection of its own.</summary>
    public override void Cancel()
    {
        if (Connection?.Native?.Canceller is { } canceller)
        {
            canceller.CancelAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc/>
    protected override DbParameter CreateDbParameter() => new TurborelParameter();

    /// <inheritdoc/>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
        await ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteReader(behavior);

    // The open connection the command runs on, ready for it.
    private NativeConnection Ready(TurborelConnection connection)
    {
        NativeConnection native = connection.Opened();
        if (connection.Reader is not null)
        {
            throw Misuse.Of("[DESIGN] a reader is open on the connection: close it first");
        }

        if (Transaction is not null && Transaction != connection.Transaction)
        {
            throw Misuse.Of("[DESIGN] the command's transaction is not its connection's");
        }

        return native;
    }

    // Runs the statement: directly, if it has no parameters and is not
    // prepared; else prepared, a query's cursor opened.
    private async Task<ReaderResults> RunAsync(NativeConnection native, CancellationToken cancellationToken)
    {
        await DropStaleAsync(native, cancellationToken).ConfigureAwait(false);
        PreparedStatement? kept = await KeptAsync(native, cancellationToken).ConfigureAwait(false);
        if (kept is null && Parameters.Count == 0)
        {
            return new StatementResults(native, await native.BeginQueryAsync(CommandText, cancellationToken).ConfigureAwait(false));
        }

        bool own = kept is null;
        PreparedStatement statement = kept ?? await native.PrepareAsync(CommandText, cancellationToken).ConfigureAwait(false);
        try
        {
            if (statement.Columns.Count == 0)
            {
                Outcome done = await native.ExecuteAsync(statement, Parameters.Values(), cancellationToken).ConfigureAwait(false);
                if (own)
                {
                    await native.DeallocateAsync(statement, cancellationToken).ConfigureAwait(false);
                }

                return new StatementResults(native, ((DoneOutcome)done).Completion);
            }

            await native.BeginCursorAsync(statement, Parameters.Values(), FetchRows, cancellationToken).ConfigureAwait(false);
            return new StatementResults(native, statement, FetchRows, own);
        }
        catch (TurborelException) when (own && !native.IsLost)
        {
            await native.DeallocateAsync(statement, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // The statement the command keeps prepared on this session, prepared
    // again if the session was renewed; none if it prepares none.
    private async Task<PreparedStatement?> KeptAsync(NativeConnection native, CancellationToken cancellationToken)
    {
        if (!_prepares)
        {
            return null;
        }

        if (_kept is { } kept && ReferenceEquals(kept.On, native) && kept.Renewals == native.Renewals)
        {
            return kept.Statement;
        }

        PreparedStatement statement = await native.PrepareAsync(CommandText, cancellationToken).ConfigureAwait(false);
        _kept = (statement, native, native.Renewals);
        return statement;
    }

    // Lets go of the statement kept, to be destroyed at the next run on its session.
    private void Unprepare()
    {
        if (_kept is { } kept)
        {
            _stale.Add(kept);
        }

        (_kept, _prepares) = (null, false);
    }

    // Destroys the statements let go of that are still of this session.
    private async Task DropStaleAsync(NativeConnection native, CancellationToken cancellationToken)
    {
        foreach ((PreparedStatement statement, NativeConnection on, ulong renewals) in _stale.ToArray())
        {
            _ = _stale.Remove((statement, on, renewals));
            if (ReferenceEquals(on, native) && renewals == native.Renewals && !native.IsLost)
            {
                await native.DeallocateAsync(statement, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
