// A server reached over TCP, TLS begun on the same port before Hello
// [PROTOCOL The Native Protocol] [CLIENT Connecting].

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Transport;

namespace Turborel.Data.Tls;

/// <summary>A server reached over TLS on TCP.</summary>
/// <param name="host">The server's name or address.</param>
/// <param name="port">Its port.</param>
/// <param name="options">How TLS is used.</param>
public sealed class TlsTarget(string host, int port, TlsOptions options) : Target
{
    /// <summary>Gets the server's name or address.</summary>
    public string Host { get; } = host;

    /// <summary>Gets its port.</summary>
    public int Port { get; } = port;

    /// <summary>Gets how TLS is used.</summary>
    public TlsOptions Options { get; } = options;

    /// <inheritdoc/>
    public override string ToString() => $"Tls({Host}:{Port})";

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "R-LINT-3: the socket passes to the stream, which owns and disposes it.")]
    internal override async Task<(Stream Stream, TlsChannel? Channel)> OpenAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        Socket socket = await TcpTarget.ConnectAsync(Host, Port, within, cancellationToken).ConfigureAwait(false);
        (Stream stream, TlsChannel channel) = await TlsHandshake.SecureAsync(
            new NetworkStream(socket, ownsSocket: true),
            Host,
            Options,
            within,
            cancellationToken).ConfigureAwait(false);
        return (stream, channel);
    }
}
