// A server reached over TCP, on port 7422 unless told another; a name is
// tried at each address it resolves to [PROTOCOL The Native Protocol]
// [CLIENT Connecting].

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Tls;

namespace Turborel.Data.Transport;

/// <summary>A server reached over TCP.</summary>
/// <param name="host">The server's name or address.</param>
/// <param name="port">Its port.</param>
public sealed class TcpTarget(string host, int port = TcpTarget.DefaultPort) : Target
{
    /// <summary>The port a server listens on unless told another [PROTOCOL The Native Protocol].</summary>
    public const int DefaultPort = 7422;

    /// <summary>Gets the server's name or address.</summary>
    public string Host { get; } = host;

    /// <summary>Gets its port.</summary>
    public int Port { get; } = port;

    /// <inheritdoc/>
    public override bool IsPlainTcp => true;

    /// <inheritdoc/>
    public override string ToString() => $"Tcp({Host}:{Port})";

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "R-LINT-3: the socket passes to the stream, which owns and disposes it.")]
    internal override async Task<(Stream Stream, TlsChannel? Channel)> OpenAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        Socket socket = await ConnectAsync(Host, Port, within, cancellationToken).ConfigureAwait(false);
        return (new NetworkStream(socket, ownsSocket: true), null);
    }

    /// <summary>Connects a socket to <paramref name="host"/>, trying each of its addresses, within <paramref name="within"/>.</summary>
    /// <param name="host">The name or address.</param>
    /// <param name="port">The port.</param>
    /// <param name="within">How long it may wait; null for no limit.</param>
    /// <param name="cancellationToken">The program's token.</param>
    /// <returns>The connected socket, its writes not delayed.</returns>
    internal static Task<Socket> ConnectAsync(string host, int port, TimeSpan? within, CancellationToken cancellationToken)
    {
        return Waits.Within(
            "connect",
            within,
            async token =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new DnsEndPoint(host, port), token).ConfigureAwait(false);
                    return socket;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            cancellationToken);
    }
}
