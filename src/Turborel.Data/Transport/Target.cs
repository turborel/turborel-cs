// Where a server is, and how a client reaches it [CLIENT Connecting]: over
// TCP, a Unix socket or, on Windows, a named pipe, or TLS over TCP. A
// connection remembers its target, so a Cancel reaches the server again.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Tls;

namespace Turborel.Data.Transport;

/// <summary>Where a server is, and how to reach it.</summary>
public abstract class Target
{
    private protected Target()
    {
    }

    /// <summary>Gets a value indicating whether the target is TCP without TLS, over which no secret is sent.</summary>
    public virtual bool IsPlainTcp => false;

    /// <summary>Gets a value indicating whether the stream's reads and writes may be bounded in time once open.</summary>
    internal virtual bool BoundsReads => true;

    /// <summary>Opens a stream to the server, each step within <paramref name="within"/>.</summary>
    /// <param name="within">How long each step waits; null for no limit.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The stream, and of TLS what its handshake gives a mechanism to bind.</returns>
    /// <exception cref="TurborelException">A connection failure, SQLSTATE 08006, if it cannot be reached in time.</exception>
    internal abstract Task<(Stream Stream, TlsChannel? Channel)> OpenAsync(TimeSpan? within, CancellationToken cancellationToken);
}
