// A connection a pool handed out [CLIENT Pools]: given back, reset, as it
// is disposed; after that it is of no more use to its borrower.

using System;
using System.Threading.Tasks;
using Turborel.Data.Session;

namespace Turborel.Data.Pooling;

/// <summary>A connection handed out by a pool, given back as it is disposed.</summary>
public sealed class PooledConnection : IAsyncDisposable
{
    private readonly ConnectionPool _pool;
    private readonly long _made;
    private NativeConnection? _connection;

    /// <summary>Initializes a new instance of the <see cref="PooledConnection"/> class.</summary>
    /// <param name="pool">The pool it came from.</param>
    /// <param name="connection">The connection.</param>
    /// <param name="made">When it was made.</param>
    internal PooledConnection(ConnectionPool pool, NativeConnection connection, long made)
    {
        (_pool, _connection, _made) = (pool, connection, made);
    }

    /// <summary>Gets the connection.</summary>
    /// <exception cref="TurborelException">SQLSTATE TB910 once it is given back.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "R-LINT-3: a connection given back is another borrower's; using it after is the API's misuse, R-ERR-1.")]
    public NativeConnection Connection => _connection ?? throw Misuse.Of("[DESIGN] the connection was given back to its pool");

    /// <summary>Gives the connection back to its pool, reset, or closes it.</summary>
    /// <returns>The task.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            await _pool.GiveBackAsync(connection, _made).ConfigureAwait(false);
        }
    }
}
