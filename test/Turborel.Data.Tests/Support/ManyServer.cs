// A server of the tests' own on loopback that takes any number of
// connections (RULES.md R-TEST-5), each logged in, granted what it is
// told, then answered as a quiet server answers: Ping by Pong, Reset and
// any other request by Complete, a Query of WORK leaving its transaction
// active, Terminate by closing; each request kept, so a test sees what the
// client sent.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Tests.Support;

/// <summary>A scripted server of many connections.</summary>
internal sealed class ManyServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _served = [];
    private readonly string[] _granted;
    private readonly Task _accepting;
    private int _connections;

    private ManyServer(string[] granted)
    {
        _granted = granted;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>Gets the port it listens on.</summary>
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Gets how many connections it has taken.</summary>
    internal int Connections => _connections;

    /// <summary>Gets each request's kind and, of a Query, its text, in the order they came.</summary>
    internal ConcurrentQueue<string> Requests { get; } = new();

    /// <summary>Gets or sets a value indicating whether the next requests are answered by closing the connection.</summary>
    internal bool Hang { get; set; }

    /// <summary>Starts a server that grants <paramref name="granted"/>.</summary>
    /// <param name="granted">The capabilities granted.</param>
    /// <returns>The server.</returns>
    internal static ManyServer Start(params string[] granted) => new(granted);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _accepting.ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // Stopped as it waited.
        }

        _listener.Dispose();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            _ = Interlocked.Increment(ref _connections);
            _served.Add(ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var peer = new FakeServer.Peer(client.GetStream(), null!);
                _ = await peer.ExpectAsync(RequestKinds.Hello).ConfigureAwait(false);
                await peer.ReadyAsync(_granted).ConfigureAwait(false);
                while (true)
                {
                    (byte kind, byte[] body) = await peer.ReadAsync().ConfigureAwait(false);
                    Requests.Enqueue(kind == RequestKinds.Query ? Encoding.UTF8.GetString(body, 4, BitConverter.ToInt32(body, 0)) : ((char)kind).ToString());
                    if (kind == RequestKinds.Terminate || Hang)
                    {
                        return;
                    }

                    // A statement of WORK leaves its transaction active.
                    bool active = kind == RequestKinds.Query && Requests.Last().Contains("WORK", StringComparison.Ordinal);
                    await (kind == RequestKinds.Ping ? peer.WriteAsync('G', []) : peer.WriteAsync('K', Replies.Complete(active: active))).ConfigureAwait(false);
                }
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // The client went.
            }
        }
    }
}
