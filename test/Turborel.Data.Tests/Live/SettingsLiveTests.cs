// Connecting by settings against a real turboreld (RULES.md R-TEST-8):
// several hosts, one unreachable passed; each TLS mode; a password from
// the password file proved by SCRAM, its keys kept for every connection
// the settings make; a client certificate; a local socket; the role asked
// [CLIENT Connecting by settings] [CLIENT Several servers] [CLIENT TLS by
// mode] [CLIENT The SCRAM key cache].

using System;
using System.IO;
using System.Threading.Tasks;
using Turborel.Data.Configuration;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of connecting by settings.</summary>
/// <param name="server">The live server.</param>
public sealed class SettingsLiveTests(LiveServer server)
{
    private static string Port => LiveServer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A host that cannot be reached is passed for the next; the program's name is told as the attribute application; random order connects too.</summary>
    [Fact]
    [Verifies("[CLIENT Several servers]")]
    [Verifies("[CLIENT Connecting by settings]")]
    [Verifies("[CLIENT Where settings come from]")]
    public async Task UnreachableHostsArePassed()
    {
        server.Require();
        // An application of its own, as the database's owner sees every connection's attributes.
        string application = Live.Table("LEDGER");
        ConnectionSettings settings = Settings($"turborel://ann@127.0.0.1:1,localhost:{Port}/?tls_mode=disable&application_name={application}&connect_timeout=5");

        await using NativeConnection c = await settings.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LiveServer.Owner, c.Authorization);
        Assert.Single(await c.RowsAsync($"SELECT ATTRIBUTE_VALUE FROM INFORMATION_SCHEMA.CONNECTION_ATTRIBUTES WHERE ATTRIBUTE_NAME = 'application' AND ATTRIBUTE_VALUE = '{application}'"));
        _ = await c.DoneAsync("ROLLBACK");
        await using NativeConnection random = await Settings($"turborel://ann@127.0.0.1:{Port},localhost:{Port}/?tls_mode=disable&load_balance=random").ConnectAsync(TestContext.Current.CancellationToken);
        TurborelException none = await Assert.ThrowsAsync<TurborelException>(() => Settings("turborel://ann@127.0.0.1:1,127.0.0.1:2/?tls_mode=disable").ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal("08001", none.SqlState);
        Assert.Contains("127.0.0.1 (127.0.0.1:1): ", none.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// require takes any certificate; verify-ca checks the authority, not the
    /// name; verify-full checks both, against server_name else the host's;
    /// a client certificate proves its user.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT TLS by mode]")]
    [Verifies("[CLIENT Connecting by settings]")]
    public async Task EachTlsMode()
    {
        server.Require();
        string roots = Uri.EscapeDataString(server.Certificates!.AuthorityFile);

        await (await Settings($"turborel://ann@localhost:{Port}/?tls_mode=require").ConnectAsync(TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        await (await Settings($"turborel://ann@127.0.0.1:{Port}/?tls_mode=verify-ca&tls_roots={roots}&server_name=elsewhere").ConnectAsync(TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        await (await Settings($"turborel://ann@localhost:{Port}/?tls_mode=verify-full&tls_roots={roots}").ConnectAsync(TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        TurborelException named = await Assert.ThrowsAsync<TurborelException>(() => Settings($"turborel://ann@localhost:{Port}/?tls_mode=verify-full&tls_roots={roots}&server_name=elsewhere").ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Contains("not of the name checked", named.Message, StringComparison.Ordinal);
        string certificate = Uri.EscapeDataString(server.Certificates.ClientFile);
        string key = Uri.EscapeDataString(server.Certificates.ClientKeyFile);
        await using NativeConnection certified = await Settings($"turborel://ann@localhost:{Port}/?tls_mode=verify-full&tls_roots={roots}&tls_certificate={certificate}&tls_key={key}").ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LiveServer.Owner, certified.Authorization);
    }

    /// <summary>
    /// A password from the password file is proved by SCRAM-SHA-256-PLUS over
    /// TLS; one cache keeps its keys for every connection the same settings
    /// make; a binding required over a local socket is refused, SQLSTATE 28000.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The password file]")]
    [Verifies("[CLIENT The SCRAM key cache]")]
    [Verifies("[CLIENT TLS by mode]")]
    public async Task PasswordFileAndSharedKeyCache()
    {
        server.Require();
        string passfile = Path.Combine(server.Directory, $"pass-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(passfile, $"localhost:{Port}:ann:{LiveServer.Password.Replace(":", "\\:", StringComparison.Ordinal)}\n", TestContext.Current.CancellationToken);
        ConnectionSettings settings = Settings(
            $"turborel://ann@localhost:{Port}/?tls_mode=verify-full&channel_binding=require&tls_roots={Uri.EscapeDataString(server.Certificates!.AuthorityFile)}&passfile={Uri.EscapeDataString(passfile)}");

        await (await settings.ConnectAsync(TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        await (await (settings with { TlsMode = TlsMode.VerifyFull }).ConnectAsync(TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1L, 1L), settings.KeyCache.Lookups);
        TurborelException unbound = await Assert.ThrowsAsync<TurborelException>(
            () => Settings($"turborel://ann@{Uri.EscapeDataString(server.Socket)}/?channel_binding=require&passfile={Uri.EscapeDataString(passfile)}").ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal("28000", unbound.SqlState);
    }

    /// <summary>A local socket's host is connected to with no TLS; the role asked is kept, or the server left, SQLSTATE 08001.</summary>
    [Fact]
    [Verifies("[CLIENT Several servers]")]
    [Verifies("[CLIENT TLS by mode]")]
    public async Task LocalSocketsAndRoles()
    {
        server.Require();
        await using NativeConnection local = await Settings($"turborel://ann@{Uri.EscapeDataString(server.Socket)}/?target=read-write").ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServerRoleKind.ReadWrite, await ServerRole.OfAsync(local, TestContext.Current.CancellationToken));
        TurborelException replica = await Assert.ThrowsAsync<TurborelException>(() => Settings($"turborel://ann@127.0.0.1:{Port}/?tls_mode=disable&target=read-only").ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("08001", "[DESIGN] the server is read-write, and read-only is asked"), (replica.SqlState, replica.Message));
    }

    private static ConnectionSettings Settings(string uri) =>
        ConnectionSettings.ResolveIn(ConnectionOptions.FromUri(uri).Set(ConnectionOptions.Passfile, ConnectionOptions.FromUri(uri).Get(ConnectionOptions.Passfile) ?? "/nonexistent"), _ => null);
}
