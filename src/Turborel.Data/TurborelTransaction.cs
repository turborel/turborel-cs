// The standard API's transaction (RULES.md D2) [CLIENT Running
// statements]: begun with its isolation level set by SET TRANSACTION, the
// connection out of autocommit until it ends by COMMIT or ROLLBACK; its
// savepoints by SAVEPOINT, ROLLBACK TO SAVEPOINT and RELEASE SAVEPOINT.

using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Import;

namespace Turborel.Data;

/// <summary>A transaction of a connection.</summary>
public sealed class TurborelTransaction : DbTransaction
{
    private readonly TurborelConnection _connection;
    private bool _ended;

    private TurborelTransaction(TurborelConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel;
    }

    /// <inheritdoc/>
    public override IsolationLevel IsolationLevel { get; }

    /// <inheritdoc/>
    public override bool SupportsSavepoints => true;

    /// <inheritdoc/>
    protected override DbConnection DbConnection => _connection;

    /// <inheritdoc/>
    public override void Commit() => CommitAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task CommitAsync(CancellationToken cancellationToken = default) => EndAsync("COMMIT", cancellationToken);

    /// <inheritdoc/>
    public override void Rollback() => RollbackAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task RollbackAsync(CancellationToken cancellationToken = default) => EndAsync("ROLLBACK", cancellationToken);

    /// <inheritdoc/>
    public override void Save(string savepointName) => SaveAsync(savepointName).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default) =>
        RunAsync($"SAVEPOINT {Name(savepointName)}", cancellationToken);

    /// <inheritdoc/>
    public override void Rollback(string savepointName) => RollbackAsync(savepointName).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default) =>
        RunAsync($"ROLLBACK TO SAVEPOINT {Name(savepointName)}", cancellationToken);

    /// <inheritdoc/>
    public override void Release(string savepointName) => ReleaseAsync(savepointName).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default) =>
        RunAsync($"RELEASE SAVEPOINT {Name(savepointName)}", cancellationToken);

    /// <summary>Begins a transaction on <paramref name="connection"/>, of <paramref name="isolationLevel"/>.</summary>
    /// <param name="connection">The connection, open.</param>
    /// <param name="isolationLevel">The isolation level; unspecified for the session's.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910 if one is begun already, or the level is none turborel has.</exception>
    internal static async Task<TurborelTransaction> BeginAsync(TurborelConnection connection, IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        if (connection.Transaction is not null)
        {
            throw Misuse.Of("[DESIGN] a transaction of the connection is begun already");
        }

        string? level = isolationLevel switch
        {
            IsolationLevel.Unspecified => null,
            IsolationLevel.ReadUncommitted => "READ UNCOMMITTED",
            IsolationLevel.ReadCommitted => "READ COMMITTED",
            IsolationLevel.RepeatableRead or IsolationLevel.Snapshot => "REPEATABLE READ",
            IsolationLevel.Serializable => "SERIALIZABLE",
            _ => throw Misuse.Of($"[DESIGN] turborel has no isolation level {isolationLevel}"),
        };
        Session.NativeConnection native = connection.Opened();
        native.Autocommit = false;
        try
        {
            if (level is not null)
            {
                _ = await native.QueryAsync($"SET TRANSACTION ISOLATION LEVEL {level}", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (TurborelException)
        {
            native.Autocommit = true;
            throw;
        }

        var transaction = new TurborelTransaction(connection, isolationLevel);
        connection.Transaction = transaction;
        return transaction;
    }

    /// <summary>Ends the transaction by ROLLBACK, if it has not ended, as it is disposed.</summary>
    /// <returns>The task.</returns>
    public override async ValueTask DisposeAsync()
    {
        if (!_ended && _connection.Transaction == this)
        {
            await RollbackAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_ended && _connection.Transaction == this)
        {
            Rollback();
        }

        base.Dispose(disposing);
    }

    // A savepoint's name, as a delimited identifier.
    private static string Name(string savepointName)
    {
        Misuse.ThrowIfNull(savepointName);
        return SqlNames.Delimited(savepointName);
    }

    private async Task RunAsync(string sql, CancellationToken cancellationToken)
    {
        if (_ended)
        {
            throw Misuse.Of("[DESIGN] the transaction has ended");
        }

        _ = await _connection.Opened().QueryAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    private async Task EndAsync(string sql, CancellationToken cancellationToken)
    {
        try
        {
            await RunAsync(sql, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // A commit that fails rolls the transaction back: either way it has ended.
            _ended = true;
            _connection.Transaction = null;
            if (_connection.Native is { } native)
            {
                native.Autocommit = true;
            }
        }
    }
}
