// How long a connection waits on its server [CLIENT Timeouts]: to connect,
// begin TLS and for each step of the login, 30 seconds by default; for each
// read or write after, no limit by default, as a statement may run as long
// as it must. A wait that runs out loses the connection.

using System;

namespace Turborel.Data.Transport;

/// <summary>How long a connection waits on its server.</summary>
/// <param name="Connect">To connect, begin TLS and for each step of the login; null for no limit.</param>
/// <param name="Read">For each read or write after the login; null for no limit.</param>
public sealed record Timeouts(TimeSpan? Connect, TimeSpan? Read)
{
    /// <summary>The connect timeout unless the program sets another: 30 seconds [CLIENT Timeouts].</summary>
    public static readonly TimeSpan ConnectWithin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long Terminate waits for the server to close, unless the read
    /// timeout says: 60 seconds [CLIENT Connecting].
    /// </summary>
    public static readonly TimeSpan TerminateWithin = TimeSpan.FromSeconds(60);

    /// <summary>Gets the timeouts unless the program sets others: 30 seconds to connect, no limit to read.</summary>
    public static Timeouts Default { get; } = new(ConnectWithin, null);
}
