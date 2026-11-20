// The connection strings of the standard API's tests, to a server of the
// test's own on loopback, over plain TCP, of no password.

namespace Turborel.Data.Tests.Ado;

/// <summary>Connection strings of a test's server.</summary>
internal static class AdoServer
{
    /// <summary>Gives the connection string of the server on <paramref name="port"/>, unpooled unless <paramref name="pooled"/>.</summary>
    /// <param name="port">The server's port.</param>
    /// <param name="pooled">Whether connections are pooled.</param>
    /// <returns>The connection string.</returns>
    internal static string Of(int port, bool pooled = false) =>
        $"host=127.0.0.1:{port};user=ann;tls_mode=disable;passfile=/nonexistent;connect_timeout=5;pooling={(pooled ? "true" : "false")}";
}
