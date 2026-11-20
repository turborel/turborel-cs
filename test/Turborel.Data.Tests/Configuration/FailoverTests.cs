// Connecting by settings against servers of the test's own: hosts ordered
// at random by their seed, the failure of none kept, a server's role read
// from its status or its REPLICAS, TLS preferred and given up for plain
// TCP, and settings no connection can be made by refused before any
// [CLIENT Several servers] [CLIENT TLS by mode] [CLIENT Connecting by
// settings].

using System;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Configuration;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Configuration;

/// <summary>Tests of <see cref="Failover"/> and <see cref="ServerRole"/>.</summary>
public sealed class FailoverTests
{
    /// <summary>Hosts ordered at random are each host once, in the order the seed decides; different seeds give different orders.</summary>
    [Fact]
    [Verifies("[CLIENT Several servers]")]
    public void HostsAreShuffledByTheirSeed()
    {
        int[] Order(ulong seed)
        {
            int[] hosts = [.. Enumerable.Range(0, 8)];
            Failover.Shuffle(hosts, seed);
            return hosts;
        }

        Assert.Equal(Enumerable.Range(0, 8), Order(7).Order());
        Assert.Equal(Order(7), Order(7));
        Assert.True(Enumerable.Range(0, 16).Select(s => string.Join(",", Order((ulong)s))).Distinct().Count() > 8);
        int[] one = [1];
        Failover.Shuffle(one, 3);
        Failover.Shuffle(Array.Empty<int>(), 3);
        Assert.Equal([1], one);
    }

    /// <summary>Of one server tried, its own failure; of several, 08001 naming each and why, or 28000 where the last refused the user; of none, 08001.</summary>
    [Fact]
    [Verifies("[CLIENT Several servers]")]
    public void NoServerKeptSaysWhyEachWasNot()
    {
        var lost = new TurborelException(-919, "08006", "cannot connect");
        var refused = new TurborelException(-919, "28000", "no");

        Assert.Same(refused, Failover.NoneKept([("a", refused)]));
        TurborelException both = Failover.NoneKept([("a", refused), ("b", lost)]);
        Assert.Equal(("08001", "[DESIGN] no server was kept: a: no; b: cannot connect"), (both.SqlState, both.Message));
        Assert.Equal("28000", Failover.NoneKept([("b", lost), ("a", refused)]).SqlState);
        Assert.Equal(("08001", "[DESIGN] no server is named"), (Failover.NoneKept([]).SqlState, Failover.NoneKept([]).Message));
    }

    /// <summary>
    /// A server's role is SERVER_READ_ONLY as told; of a server that tells
    /// none, read from REPLICAS by a statement of its own transaction: PRIMARY
    /// read-write, another read-only, no row unknown, no such table a server
    /// alone, read-write.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Several servers]")]
    public async Task RoleIsToldOrQueried()
    {
        Column role = new("ROLE", new Turborel.Data.Types.CharacterType(8, Turborel.Data.Types.CharacterUnits.Characters, true), false);
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ExpectAsync(RequestKinds.Hello);
            await peer.ReadyAsync();
            foreach (string? answer in new[] { "PRIMARY ", "BACKUP", null })
            {
                byte[] query = await peer.ExpectAsync(RequestKinds.Query);
                Assert.Contains(ServerRole.RoleQuery, Encoding.UTF8.GetString(query), StringComparison.Ordinal);
                await peer.WriteAsync('D', Replies.Description(role));
                await peer.WriteAsync('B', Wire.Written(w =>
                {
                    w.WriteU32(answer is null ? 0u : 1u);
                    if (answer is not null)
                    {
                        ValueCodec.WriteList(w, [answer]);
                    }
                }));
                await peer.WriteAsync('K', Replies.Complete());
            }

            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-103, "2A000"));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-551, "TB551"));
            await peer.WriteAsync('T', Wire.Join(Wire.Str(StatusNames.ServerReadOnly), Wire.Str("YES")));
        });
        await using NativeConnection c = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ServerRoleKind.ReadWrite, await ServerRole.OfAsync(c, TestContext.Current.CancellationToken));
        Assert.Equal(ServerRoleKind.ReadOnly, await ServerRole.OfAsync(c, TestContext.Current.CancellationToken));
        Assert.Equal(ServerRoleKind.Unknown, await ServerRole.OfAsync(c, TestContext.Current.CancellationToken));
        Assert.Equal(ServerRoleKind.ReadWrite, await ServerRole.OfAsync(c, TestContext.Current.CancellationToken));
        Assert.Equal("TB551", (await Assert.ThrowsAsync<TurborelException>(() => ServerRole.OfAsync(c, TestContext.Current.CancellationToken))).SqlState);
        Assert.False(c.Autocommit);
        Assert.Equal(("read-write", "read-only", "of a role this session cannot see"), (ServerRole.Describe(ServerRoleKind.ReadWrite), ServerRole.Describe(ServerRoleKind.ReadOnly), ServerRole.Describe(ServerRoleKind.Unknown)));
    }

    /// <summary>
    /// Under prefer, a server whose TLS handshake fails is connected to again
    /// over plain TCP; the program's name goes as the attribute application;
    /// the role asked is checked, a server not of it left.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT TLS by mode]")]
    [Verifies("[CLIENT Connecting by settings]")]
    [Verifies("[CLIENT Several servers]")]
    public async Task PreferGivesUpTlsForPlainTcp()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            byte[] hello = new byte[5];
            await peer.Stream.ReadExactlyAsync(hello);
            Assert.Equal(22, hello[0]);
            peer.Stream.Close();
            (TcpClient client, FakeServer.Peer plain) = await peer.AcceptAsync();
            using (client)
            {
                (_, byte[] body) = await plain.ReadAsync();
                Assert.Contains("payroll", Encoding.UTF8.GetString(body), StringComparison.Ordinal);
                await plain.WriteAsync('T', Wire.Join(Wire.Str(StatusNames.ServerReadOnly), Wire.Str("NO")));
                await plain.ReadyAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        });
        ConnectionOptions options = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Host, $"127.0.0.1:{server.Port}")
            .Set(ConnectionOptions.ApplicationName, "payroll").Set(ConnectionOptions.Target, "read-only").Set(ConnectionOptions.Passfile, "/nonexistent");

        TurborelException left = await Assert.ThrowsAsync<TurborelException>(() => ConnectionSettings.ResolveIn(options, _ => null).ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("08001", "[DESIGN] the server is read-write, and read-only is asked"), (left.SqlState, left.Message));
        await server.PlayedAsync();
    }

    /// <summary>
    /// Settings no connection can be made by are refused before any: verify
    /// modes with no authority trusted, a certificate with no key; a binding
    /// required where none can be; a name that resolves to nothing.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="value">Its value.</param>
    /// <param name="state">The failure's SQLSTATE.</param>
    /// <param name="says">What it says.</param>
    /// <returns>The task.</returns>
    [Theory]
    [Verifies("[CLIENT TLS by mode]")]
    [Verifies("[CLIENT Connecting by settings]")]
    [InlineData("tls_mode", "verify-ca", "08006", "no certificate authority is trusted")]
    [InlineData("tls_certificate", "c.pem", "08006", "presented with its key")]
    [InlineData("channel_binding", "require", "28000", "channel binding is required")]
    [InlineData("host", "no-such-host.invalid", "08006", "cannot resolve the name")]
    public async Task SettingsThatCannotConnectAreRefused(string setting, string value, string state, string says)
    {
        await using var server = FakeServer.Start(async peer => await Task.Delay(TimeSpan.FromMilliseconds(100)));
        ConnectionOptions options = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Host, $"127.0.0.1:{server.Port}")
            .Set(ConnectionOptions.TlsMode, setting == ConnectionOptions.TlsCertificate ? "require" : "disable").Set(ConnectionOptions.Passfile, "/nonexistent").Set(setting, value);

        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => ConnectionSettings.ResolveIn(options, _ => null).ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(state, refused.SqlState);
        Assert.Contains(says, refused.Message, StringComparison.Ordinal);
    }
}
