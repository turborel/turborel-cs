// The standard API's batch (RULES.md D2): several statements sent at once
// over a pipeline, one round trip a window [CLIENT Pipelines]. A
// statement of parameters is prepared first. The batch stops at its first
// failure, the statements before it kept, those after skipped, and the
// failure thrown; its results read in turn, each statement's rows or its
// rows changed.

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>Several statements sent at once.</summary>
public sealed class TurborelBatch : DbBatch
{
    /// <summary>Gets the batch's statements, in order.</summary>
    public new TurborelBatchCommandCollection BatchCommands { get; } = [];

    /// <summary>Gets or sets the connection it runs on.</summary>
    public new TurborelConnection? Connection { get; set; }

    /// <summary>Gets or sets the transaction it runs in: the connection's, or none.</summary>
    public new TurborelTransaction? Transaction { get; set; }

    /// <summary>Gets or sets the seconds the batch may run before the statement running is cancelled; 0, the default, for no limit.</summary>
    public override int Timeout { get; set; }

    /// <inheritdoc/>
    protected override DbBatchCommandCollection DbBatchCommands => BatchCommands;

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => Connection;
        set => Connection = value as TurborelConnection ?? (value is null ? null : throw Misuse.Of("[DESIGN] a batch of turborel's runs on a TurborelConnection"));
    }

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => Transaction;
        set => Transaction = value as TurborelTransaction ?? (value is null ? null : throw Misuse.Of("[DESIGN] a batch of turborel's runs in a TurborelTransaction"));
    }

    /// <inheritdoc/>
    public override int ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default)
    {
        TurborelDataReader reader = await ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            return reader.RecordsAffected;
        }
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default)
    {
        TurborelDataReader reader = await ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0 ? reader.GetValue(0) : null;
        }
    }

    /// <summary>Runs the statements at once and reads their results in turn.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The reader of each statement's result.</returns>
    /// <exception cref="TurborelException">The first statement's failure, those after it skipped; SQLSTATE TB910 for a batch not ready to run.</exception>
    public new async Task<TurborelDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default)
    {
        TurborelConnection connection = Connection ?? throw Misuse.Of("[DESIGN] the batch has no connection");
        NativeConnection native = connection.Opened();
        if (connection.Reader is not null || (Transaction is not null && Transaction != connection.Transaction))
        {
            throw Misuse.Of("[DESIGN] the batch's connection has a reader open, or another transaction");
        }

        foreach (TurborelBatchCommand command in BatchCommands)
        {
            command.Affected = -1;
        }

        var prepared = new List<PreparedStatement>();
        using var timer = new StatementTimer(native, Timeout);
        try
        {
            Pipeline pipeline = native.Pipeline();
            pipeline.OnError = PipelineOnError.Skip;
            foreach (TurborelBatchCommand command in BatchCommands)
            {
                if (command.Parameters.Count == 0)
                {
                    _ = pipeline.Query(command.CommandText);
                    continue;
                }

                PreparedStatement statement = await native.PrepareAsync(command.CommandText, cancellationToken).ConfigureAwait(false);
                prepared.Add(statement);
                _ = pipeline.Execute(statement, command.Parameters.Values());
            }

            IReadOnlyList<Piped> outcomes = await pipeline.RunAsync(cancellationToken).ConfigureAwait(false);
            Outcome[] done = [.. outcomes.OfType<PipedDone>().Select(o => o.Outcome)];
            for (int i = 0; i < done.Length; i++)
            {
                BatchCommands[i].Affected = done[i] is DoneOutcome { Completion.RowCount: ulong n } ? (int)System.Math.Min(n, int.MaxValue) : -1;
            }

            return outcomes.OfType<PipedFailed>().FirstOrDefault() is { } failed
                ? throw failed.Failure
                : new TurborelDataReader(connection, new GatheredResults(done), closeConnection: false);
        }
        finally
        {
            if (!native.IsLost)
            {
                foreach (PreparedStatement statement in prepared)
                {
                    await native.DeallocateAsync(statement, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <inheritdoc/>
    public override void Prepare()
    {
        // Each statement of parameters is prepared as the batch runs.
    }

    /// <inheritdoc/>
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public override void Cancel()
    {
        if (Connection?.Native?.Canceller is { } canceller)
        {
            canceller.CancelAsync().GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc/>
    protected override DbBatchCommand CreateDbBatchCommand() => new TurborelBatchCommand();

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The reader is given to the caller, who disposes it.")]
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ExecuteReaderAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken) =>
        await ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>A statement of a batch.</summary>
public sealed class TurborelBatchCommand : DbBatchCommand
{
    /// <summary>Initializes a new instance of the <see cref="TurborelBatchCommand"/> class.</summary>
    public TurborelBatchCommand()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelBatchCommand"/> class of a statement.</summary>
    /// <param name="commandText">The statement, its parameters written <c>?</c>.</param>
    public TurborelBatchCommand(string commandText)
    {
        CommandText = commandText;
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    /// <summary>Gets or sets how the text is read: a statement alone.</summary>
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc/>
    public override int RecordsAffected => Affected;

    /// <summary>Gets its parameters, in the order of the statement's <c>?</c> marks.</summary>
    public new TurborelParameterCollection Parameters { get; } = [];

    /// <summary>Gets or sets the rows it changed, once run; -1 of a query or before.</summary>
    internal int Affected { get; set; } = -1;

    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => Parameters;
}

/// <summary>A batch's statements.</summary>
public sealed class TurborelBatchCommandCollection : DbBatchCommandCollection
{
    private readonly List<TurborelBatchCommand> _items = [];

    /// <inheritdoc/>
    public override int Count => _items.Count;

    /// <inheritdoc/>
    public override bool IsReadOnly => false;

    /// <summary>Gives a statement by its place.</summary>
    /// <param name="index">The place.</param>
    /// <returns>The statement.</returns>
    public new TurborelBatchCommand this[int index] => _items[TurborelParameterCollection.Place(index, _items.Count - 1)];

    /// <inheritdoc/>
    public override void Add(DbBatchCommand item) => _items.Add(Of(item));

    /// <inheritdoc/>
    public override void Clear() => _items.Clear();

    /// <inheritdoc/>
    public override bool Contains(DbBatchCommand item) => item is TurborelBatchCommand c && _items.Contains(c);

    /// <inheritdoc/>
    public override void CopyTo(DbBatchCommand[] array, int arrayIndex)
    {
        Misuse.ThrowIfNull(array);
        _ = TurborelParameterCollection.Place(arrayIndex, array.Length - _items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            array[arrayIndex + i] = _items[i];
        }
    }

    /// <inheritdoc/>
    public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    public override int IndexOf(DbBatchCommand item) => item is TurborelBatchCommand c ? _items.IndexOf(c) : -1;

    /// <inheritdoc/>
    public override void Insert(int index, DbBatchCommand item) => _items.Insert(TurborelParameterCollection.Place(index, _items.Count), Of(item));

    /// <inheritdoc/>
    public override bool Remove(DbBatchCommand item) => item is TurborelBatchCommand c && _items.Remove(c);

    /// <inheritdoc/>
    public override void RemoveAt(int index) => _items.RemoveAt(TurborelParameterCollection.Place(index, _items.Count - 1));

    /// <inheritdoc/>
    protected override DbBatchCommand GetBatchCommand(int index) => this[index];

    /// <inheritdoc/>
    protected override void SetBatchCommand(int index, DbBatchCommand batchCommand) => _items[TurborelParameterCollection.Place(index, _items.Count - 1)] = Of(batchCommand);

    private static TurborelBatchCommand Of(DbBatchCommand item) =>
        item as TurborelBatchCommand ?? throw Misuse.Of("[DESIGN] a batch of turborel's holds TurborelBatchCommands");
}
