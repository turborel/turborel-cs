// Cancel, the first and only message of a connection of its own, the key
// Ready gave; a cancel gives up in time [PROTOCOL Cancel 'Z']
// [CLIENT Running statements].

using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of <see cref="Canceller"/>.</summary>
public sealed class CancelTests
{
    /// <summary>
    /// A cancel connects again and sends Cancel alone, with Ready's key,
    /// reading to the server's close; the canceller never shows the key.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Cancel 'Z']")]
    [Verifies("[PROTOCOL Ready 'R']")]
    public async Task CancelSendsTheKeyAlone()
    {
        byte[] key = [.. Enumerable.Range(1, WireWriter.KeyBytes).Select(i => (byte)i)];
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.ReadyAsync(key);
            (TcpClient client, FakeServer.Peer cancel) = await peer.AcceptAsync();
            using (client)
            {
                (byte kind, byte[] body) = await cancel.ReadAsync();
                Assert.Equal(RequestKinds.Cancel, kind);
                Assert.Equal(key, body);
            }
        });
        var options = new ConnectOptions { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), null) };
        await using NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), options, TestContext.Current.CancellationToken);
        Canceller canceller = connection.Canceller!;

        await canceller.CancelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await server.PlayedAsync();
        Assert.Equal("Canceller(..)", canceller.ToString());
    }

    /// <summary>A cancel to a server that never closes gives up within its limit, so the thread cancelling is free again.</summary>
    [Fact]
    [Verifies("[CLIENT Running statements]")]
    public async Task CancelGivesUpInTime()
    {
        byte[]? sent = null;
        await using var server = FakeServer.Start(async peer =>
        {
            (byte kind, byte[] body) = await peer.ReadAsync();
            sent = [kind, .. body];
            await Task.Delay(TimeSpan.FromSeconds(2));
        });
        var canceller = new Canceller(new TcpTarget("127.0.0.1", server.Port), Enumerable.Repeat((byte)7, 16).ToArray());

        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(() => canceller.CancelAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        Assert.Contains("in time", failure.Message, StringComparison.Ordinal);
        Assert.Equal([RequestKinds.Cancel, .. Enumerable.Repeat((byte)7, 16)], sent!);
        Assert.Equal(TimeSpan.FromSeconds(10), Canceller.CancelWithin);
    }
}
