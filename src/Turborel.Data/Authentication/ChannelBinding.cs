// Whether SCRAM binds its exchange to the TLS connection carrying it
// [CLIENT TLS by mode].

namespace Turborel.Data.Authentication;

/// <summary>Whether SCRAM binds its exchange to its TLS channel.</summary>
public enum ChannelBinding
{
    /// <summary>Binds where the connection has a binding.</summary>
    Prefer,

    /// <summary>Binds nothing.</summary>
    Disable,

    /// <summary>Refuses a connection whose exchange is not bound.</summary>
    Require,
}
