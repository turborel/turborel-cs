// How a connection is opened [CLIENT Connecting]: what it asks for, and
// how long it waits on its server.

using Turborel.Data.Transport;

namespace Turborel.Data.Session;

/// <summary>How a connection is opened.</summary>
public sealed record ConnectOptions
{
    /// <summary>Gets the options unless the program gives others.</summary>
    public static ConnectOptions Default { get; } = new();

    /// <summary>Gets what the connection asks for.</summary>
    public Offer Offer { get; init; } = Offer.Default;

    /// <summary>Gets how long it waits on its server.</summary>
    public Timeouts Timeouts { get; init; } = Timeouts.Default;
}
