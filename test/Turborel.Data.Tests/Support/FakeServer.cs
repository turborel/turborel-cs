// A server of the tests' own on loopback (RULES.md R-TEST-5): it takes a
// connection, and any more the script asks for, and plays a script, reading the client's frames and writing
// the replies the test gives, so a connection's behaviour is tested with
// no turboreld, byte for byte.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Tests.Support;

/// <summary>A scripted server on loopback.</summary>
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _played;

    private FakeServer(Func<FakeServer.Peer, Task> script)
    {
        _listener.Start();
        _played = PlayAsync(script);
    }

    /// <summary>Gets the port it listens on.</summary>
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts a server that plays <paramref name="script"/> with its one client.</summary>
    /// <param name="script">What it does with the client.</param>
    /// <returns>The server.</returns>
    internal static FakeServer Start(Func<Peer, Task> script) => new(script);

    /// <summary>Waits for the script to end, so its failures fail the test.</summary>
    /// <returns>The task.</returns>
    internal Task PlayedAsync() => _played;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        try
        {
            await _played.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            // The client went, as some scripts mean it to.
        }

        _listener.Dispose();
    }

    private async Task PlayAsync(Func<Peer, Task> script)
    {
        using TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        await using NetworkStream stream = client.GetStream();
        await script(new Peer(stream, this)).ConfigureAwait(false);
    }

    // The next connection, and its client, which the caller disposes.
    private async Task<(TcpClient Client, Peer Peer)> AcceptAsync()
    {
        TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
        return (client, new Peer(client.GetStream(), this));
    }

    /// <summary>The client, as the script sees it.</summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="server">The server it connected to.</param>
    internal sealed class Peer(Stream stream, FakeServer server)
    {
        /// <summary>Gets the connection's stream.</summary>
        internal Stream Stream { get; } = stream;

        /// <summary>Reads the client's next frame.</summary>
        /// <returns>Its kind and body.</returns>
        internal async Task<(byte Kind, byte[] Body)> ReadAsync()
        {
            byte[] head = new byte[FrameHeader.Size];
            await Stream.ReadExactlyAsync(head).ConfigureAwait(false);
            var header = FrameHeader.Decode(head, FrameHeader.MaxBody);
            byte[] body = new byte[header.Length];
            await Stream.ReadExactlyAsync(body).ConfigureAwait(false);
            return (header.Kind, body);
        }

        /// <summary>Writes a reply of <paramref name="kind"/> around <paramref name="body"/>.</summary>
        /// <param name="kind">Its kind.</param>
        /// <param name="body">Its body.</param>
        /// <returns>The task.</returns>
        internal async Task WriteAsync(char kind, byte[] body)
        {
            await Stream.WriteAsync(Wire.Frame(kind, body)).ConfigureAwait(false);
            await Stream.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>Writes Ready, of ANN, a key of sevens, the capabilities given and the server's product.</summary>
        /// <param name="capabilities">The capabilities granted.</param>
        /// <returns>The task.</returns>
        internal Task ReadyAsync(params string[] capabilities) => ReadyAsync(new byte[WireWriter.KeyBytes], capabilities);

        /// <summary>Writes Ready, its cancel key <paramref name="key"/>.</summary>
        /// <param name="key">The cancel key.</param>
        /// <param name="capabilities">The capabilities granted.</param>
        /// <returns>The task.</returns>
        internal Task ReadyAsync(byte[] key, params string[] capabilities)
        {
            byte[] body = Wire.Written(w =>
            {
                w.WriteString("ANN");
                w.WriteRaw(key);
                w.WriteStrings(capabilities);
                w.WriteString("turborel 0.0.0 (alpha)");
            });
            return WriteAsync('R', body);
        }

        /// <summary>Takes the server's next connection, as a cancel makes.</summary>
        /// <returns>The connection's client, to dispose, and its peer.</returns>
        internal Task<(TcpClient Client, Peer Peer)> AcceptAsync() => server.AcceptAsync();
    }
}
