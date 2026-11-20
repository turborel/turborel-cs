// The standard API's source of connections (RULES.md D2, D19): a
// DbDataSource of a connection string, its settings resolved once and its
// connections pooled unless the string says not, as ConnectionPool keeps
// them [CLIENT Pools] [CLIENT Connecting by settings].

using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Configuration;
using Turborel.Data.Pooling;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>Makes connections of a connection string, pooled unless it says not.</summary>
public sealed class TurborelDataSource : DbDataSource
{
    private static readonly ConcurrentDictionary<string, TurborelDataSource> Shared = new(StringComparer.Ordinal);

    private readonly string _connectionString;

    /// <summary>Initializes a new instance of the <see cref="TurborelDataSource"/> class of a connection string or turborel:// URI.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a setting cannot be read, or no user is named.</exception>
    public TurborelDataSource(string connectionString)
    {
        Misuse.ThrowIfNull(connectionString);
        _connectionString = connectionString;
        var builder = new TurborelConnectionStringBuilder(connectionString);
        Settings = builder.ToSettings();
        var config = builder.ToPoolConfig();
        Pool = config is null ? null : new ConnectionPool(Settings, config);
    }

    /// <inheritdoc/>
    public override string ConnectionString => _connectionString;

    /// <summary>Gets the connection settings it connects by.</summary>
    public ConnectionSettings Settings { get; }

    /// <summary>Gets the pool its connections come from; null if they are not pooled.</summary>
    public ConnectionPool? Pool { get; }

    /// <summary>Makes a data source of a connection string or turborel:// URI.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The data source.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a setting cannot be read, or no user is named.</exception>
    public static TurborelDataSource Create(string connectionString) => new(connectionString);

    /// <summary>Gives a new connection of the source, opened.</summary>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">As <see cref="TurborelConnection.OpenAsync(CancellationToken)"/>'s.</exception>
    public new TurborelConnection OpenConnection() => OpenConnectionAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>Gives a new connection of the source, opened.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">As <see cref="TurborelConnection.OpenAsync(CancellationToken)"/>'s.</exception>
    public new async ValueTask<TurborelConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        TurborelConnection connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Gives a new connection of the source, closed.</summary>
    /// <returns>The connection.</returns>
    public new TurborelConnection CreateConnection() => new(this);

    /// <summary>Gives the source of a connection string, one for each string the process names, so connections of one string share a pool.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The source.</returns>
    internal static TurborelDataSource For(string connectionString) => Shared.GetOrAdd(connectionString, s => new TurborelDataSource(s));

    /// <summary>Opens a connection: from the pool, or made by the settings.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection, and what gives it back to the pool, if pooled.</returns>
    internal async Task<(NativeConnection Native, PooledConnection? Pooled)> OpenAsync(CancellationToken cancellationToken)
    {
        if (Pool is null)
        {
            return (await Settings.ConnectAsync(cancellationToken).ConfigureAwait(false), null);
        }

        PooledConnection pooled = await Pool.GetAsync(cancellationToken).ConfigureAwait(false);
        return (pooled.Connection, pooled);
    }

    /// <inheritdoc/>
    protected override DbConnection CreateDbConnection() => CreateConnection();

    /// <inheritdoc/>
    protected override DbConnection OpenDbConnection() => OpenConnection();

    /// <inheritdoc/>
    protected override async ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default) =>
        await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        if (Pool is not null)
        {
            await Pool.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && Pool is not null)
        {
            Pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }
}
