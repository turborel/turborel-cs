// How a pool keeps its connections [CLIENT Pools]: how many it makes and
// keeps idle, how long one may stay idle and live, how long a borrower
// waits, whether one is checked as it is handed out, and what statements
// reset one as it comes back; and what makes its connections.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Session;
using Turborel.Data.Transport;

namespace Turborel.Data.Pooling;

/// <summary>What makes a pool's connections: connection settings, or a function of the program's.</summary>
public interface IConnectionSource
{
    /// <summary>Makes a new connection, logged in.</summary>
    /// <param name="cancellationToken">Cancels the connecting.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">Why none can be made.</exception>
    public Task<NativeConnection> ConnectAsync(CancellationToken cancellationToken = default);
}

/// <summary>How a pool keeps its connections.</summary>
public sealed record PoolConfig
{
    /// <summary>The connections a pool makes at most by default.</summary>
    public const int DefaultMax = 10;

    /// <summary>Gets how long a connection may stay idle by default: ten minutes.</summary>
    public static TimeSpan DefaultIdleTimeout { get; } = TimeSpan.FromMinutes(10);

    /// <summary>Gets how long a connection lives by default: an hour, so a pool moves to servers added and settings changed in time.</summary>
    public static TimeSpan DefaultMaxLifetime { get; } = TimeSpan.FromHours(1);

    /// <summary>Gets how long a borrower waits by default for a connection to come back: 30 seconds.</summary>
    public static TimeSpan DefaultWait { get; } = Timeouts.ConnectWithin;

    /// <summary>Gets the most connections made at once, handed out and idle.</summary>
    public int Max { get; init; } = DefaultMax;

    /// <summary>Gets the most kept idle.</summary>
    public int MaxIdle { get; init; } = DefaultMax;

    /// <summary>Gets how long one may stay idle; null for as long as it lives.</summary>
    public TimeSpan? IdleTimeout { get; init; } = DefaultIdleTimeout;

    /// <summary>Gets how long one lives from when it was made; null for no limit.</summary>
    public TimeSpan? MaxLifetime { get; init; } = DefaultMaxLifetime;

    /// <summary>Gets how long a borrower waits for one to come back.</summary>
    public TimeSpan Wait { get; init; } = DefaultWait;

    /// <summary>Gets a value indicating whether an idle connection is checked to answer before it is handed out.</summary>
    public bool Check { get; init; } = true;

    /// <summary>Gets the statements run, after the pool's own reset, as each connection comes back: what else a program's sessions change and the next must not see.</summary>
    public IReadOnlyList<string> Reset { get; init; } = [];
}
