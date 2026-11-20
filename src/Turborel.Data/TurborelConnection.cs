// The standard API's connection (RULES.md D2, D19): one session of a
// server's, opened by its data source, from its pool or made by its
// settings, and closed, its transaction rolled back, given back or ended.
// Outside a transaction each statement is its own, as ADO.NET's programs
// expect: the session is under autocommit until a transaction begins.

using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Pooling;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>A connection to a turborel server: one session of the server's.</summary>
public sealed class TurborelConnection : DbConnection
{
    private TurborelDataSource? _source;
    private PooledConnection? _pooled;
    private ConnectionState _state = ConnectionState.Closed;

    /// <summary>Initializes a new instance of the <see cref="TurborelConnection"/> class, of no connection string yet.</summary>
    public TurborelConnection()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelConnection"/> class of a connection string or turborel:// URI.</summary>
    /// <param name="connectionString">The connection string.</param>
    public TurborelConnection(string connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelConnection"/> class of a data source.</summary>
    /// <param name="source">The source.</param>
    internal TurborelConnection(TurborelDataSource source)
    {
        _source = source;
    }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ConnectionString
    {
        get => _source?.ConnectionString ?? string.Empty;
        set
        {
            if (_state != ConnectionState.Closed)
            {
                throw Misuse.Of("[DESIGN] a connection's string is set while it is closed");
            }

            _source = string.IsNullOrEmpty(value) ? null : TurborelDataSource.For(value);
        }
    }

    /// <summary>Gets the catalog of the server's one database, as the server told it; empty while closed.</summary>
    public override string Database => Native?.Setting(StatusNames.Catalog) ?? string.Empty;

    /// <summary>Gets the servers the connection is made to, as its settings name them.</summary>
    public override string DataSource => _source is null
        ? string.Empty
        : string.Join(",", _source.Settings.Hosts.Select(h => h switch
        {
            Configuration.TcpHost tcp => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tcp.Name}:{tcp.Port}"),
            Configuration.LocalHost local => local.Path,
            _ => h.ToString(),
        }));

    /// <summary>Gets the server's product, as <c>turborel 0.0.0 (alpha)</c>; empty while closed.</summary>
    public override string ServerVersion => Native?.Server ?? string.Empty;

    /// <inheritdoc/>
    public override ConnectionState State => _state;

    /// <inheritdoc/>
    public override bool CanCreateBatch => true;

    /// <summary>Gets the low-level connection beneath, for what the standard API has no place for; null while closed.</summary>
    public NativeConnection? Native { get; private set; }

    /// <summary>Gets the transaction begun, if any.</summary>
    internal TurborelTransaction? Transaction { get; set; }

    /// <summary>Gets or sets the reader open on the connection, which takes no other statement until it closes.</summary>
    internal TurborelDataReader? Reader { get; set; }

    /// <inheritdoc/>
    protected override DbProviderFactory DbProviderFactory => TurborelFactory.Instance;

    /// <inheritdoc/>
    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_state != ConnectionState.Closed)
        {
            throw Misuse.Of("[DESIGN] the connection is open already");
        }

        TurborelDataSource source = _source ?? throw Misuse.Of("[DESIGN] the connection has no connection string");
        _state = ConnectionState.Connecting;
        try
        {
            (NativeConnection native, _pooled) = await source.OpenAsync(cancellationToken).ConfigureAwait(false);
            native.Autocommit = true;
            Native = native;
            _state = ConnectionState.Open;
        }
        finally
        {
            _state = Native is null ? ConnectionState.Closed : _state;
        }
    }

    /// <inheritdoc/>
    public override void Close() => CloseAsync().GetAwaiter().GetResult();

    /// <summary>Closes the connection: its transaction rolled back, then given back to its pool, or ended.</summary>
    /// <returns>The task.</returns>
    public override async Task CloseAsync()
    {
        if (Native is not { } native)
        {
            return;
        }

        if (Reader is { } reader)
        {
            try
            {
                await reader.DisposeAsync().ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                // Closed all the same: a statement failing as its reader closes
                // leaves the session to the pool's check, or its end.
            }
        }

        (Native, Transaction, _state) = (null, null, ConnectionState.Closed);
        if (_pooled is { } pooled)
        {
            _pooled = null;
            await pooled.DisposeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            if (!native.IsLost && native.InTransaction)
            {
                _ = await native.QueryAsync("ROLLBACK").ConfigureAwait(false);
            }

            await native.TerminateAsync().ConfigureAwait(false);
        }
        catch (TurborelException)
        {
            // Closed all the same: a connection that fails as it ends is gone.
            await native.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Refuses: a turborel server serves one database.</summary>
    /// <param name="databaseName">The database.</param>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    public override void ChangeDatabase(string databaseName) => throw Misuse.Of("[DESIGN] a turborel server serves one database");

    /// <summary>Refuses: a turborel connection takes part in no System.Transactions transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    public override void EnlistTransaction(System.Transactions.Transaction? transaction) =>
        throw Misuse.Of("[DESIGN] a turborel connection takes part in no System.Transactions transaction");

    /// <summary>Refuses: the driver gives no schema collections; INFORMATION_SCHEMA describes the database.</summary>
    /// <returns>Never.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    public override DataTable GetSchema() => throw NoSchema();

    /// <summary>Refuses: the driver gives no schema collections; INFORMATION_SCHEMA describes the database.</summary>
    /// <param name="collectionName">The collection.</param>
    /// <returns>Never.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    public override DataTable GetSchema(string collectionName) => throw NoSchema();

    /// <summary>Refuses: the driver gives no schema collections; INFORMATION_SCHEMA describes the database.</summary>
    /// <param name="collectionName">The collection.</param>
    /// <param name="restrictionValues">Its restrictions.</param>
    /// <returns>Never.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    public override DataTable GetSchema(string collectionName, string?[] restrictionValues) => throw NoSchema();

    /// <summary>Gives a new command of the connection.</summary>
    /// <returns>The command.</returns>
    public new TurborelCommand CreateCommand() => new() { Connection = this };

    /// <summary>Gives a new batch of the connection.</summary>
    /// <returns>The batch.</returns>
    public new TurborelBatch CreateBatch() => new() { Connection = this };

    /// <summary>Begins a transaction of <paramref name="isolationLevel"/>.</summary>
    /// <param name="isolationLevel">Its isolation level.</param>
    /// <returns>The transaction.</returns>
    public new TurborelTransaction BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Unspecified) =>
        TurborelTransaction.BeginAsync(this, isolationLevel, CancellationToken.None).GetAwaiter().GetResult();

    private static TurborelException NoSchema() => Misuse.Of("[DESIGN] the driver gives no schema collections: INFORMATION_SCHEMA describes the database");

    /// <summary>Gives the open connection's session.</summary>
    /// <returns>The low-level connection.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910 while it is closed.</exception>
    internal NativeConnection Opened() => Native ?? throw Misuse.Of("[DESIGN] the connection is closed");

    /// <inheritdoc/>
    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        await TurborelTransaction.BeginAsync(this, isolationLevel, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => BeginTransaction(isolationLevel);

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand() => CreateCommand();

    /// <inheritdoc/>
    protected override DbBatch CreateDbBatch() => CreateBatch();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }
}
