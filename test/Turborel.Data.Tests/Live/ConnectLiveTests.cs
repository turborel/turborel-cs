// Connecting to a real turboreld by every transport and mechanism, and
// what it grants and tells, ending and cancelling (RULES.md R-TEST-8)
// [PROTOCOL Opening a connection] [CLIENT Connecting].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tls;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of connecting.</summary>
/// <param name="server">The live server.</param>
public sealed class ConnectLiveTests(LiveServer server)
{
    private static TcpTarget Tcp => new("127.0.0.1", LiveServer.Port);

    private Target Tls => new TlsTarget("localhost", LiveServer.Port, new TlsOptions { RootsFile = server.Certificates!.AuthorityFile });

    /// <summary>
    /// trust over TCP: the session's authorization and the server's product
    /// as Ready told them, every capability asked for but compression
    /// granted in the table's order, and the status told at login.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[PROTOCOL Opening a connection]")]
    [Verifies("[PROTOCOL Status 'T']")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task TrustOverTcp()
    {
        server.Require();
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LiveServer.Owner, connection.Authorization);
        Assert.StartsWith("turborel ", connection.Server, StringComparison.Ordinal);
        Assert.Equal(Offer.Default.Capabilities, connection.Granted);
        Assert.Equal(LiveServer.Owner, connection.Setting(StatusNames.Authorization));
        Assert.Equal("NO", connection.Setting(StatusNames.ReadOnly));
        Assert.Equal("NO", connection.Setting(StatusNames.ServerReadOnly));
        Assert.Equal("YES", connection.Setting(StatusNames.Owner));
        Assert.Equal(connection.Server, connection.Setting(StatusNames.ServerVersion));
        Assert.NotNull(connection.Setting(StatusNames.Schema));
        Assert.False(string.IsNullOrEmpty(connection.Setting(StatusNames.Catalog)));
        Assert.NotNull(connection.Setting("SESSION.TIME_ZONE"));
        Assert.NotNull(connection.Setting("TRANSACTION.ISOLATION"));
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// At login, before Ready, one Status of each name in the page's table,
    /// in its order, then the session's own settings; CATALOG never empty;
    /// after a statement only what it changed, before its completion.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Status 'T']")]
    public async Task StatusComesInThePagesOrder()
    {
        server.Require();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", LiveServer.Port, TestContext.Current.CancellationToken);
        NetworkStream stream = tcp.GetStream();
        await stream.WriteAsync(Wire.Encode(new Request.Hello("ann", "trust", ReadOnlyMemory<byte>.Empty, [Capabilities.Status], [])), TestContext.Current.CancellationToken);

        List<Reply> login = await RepliesUntilAsync<Reply.Ready>(stream);
        string[] names = [.. login.OfType<Reply.Status>().Select(status => status.Name)];
        Assert.Equal(
            [StatusNames.ServerVersion, StatusNames.Authorization, StatusNames.Schema, StatusNames.ReadOnly, StatusNames.ServerReadOnly, StatusNames.Catalog, StatusNames.Owner],
            names[..7]);
        Assert.Contains("SESSION.TIME_ZONE", names[7..]);
        Assert.NotEmpty(login.OfType<Reply.Status>().Single(status => status.Name == StatusNames.Catalog).Value);
        await stream.WriteAsync(Wire.Encode(new Request.Query("SET SESSION.TIME_ZONE = '+01:00'", Ends: true, [])), TestContext.Current.CancellationToken);
        List<Reply> set = await RepliesUntilAsync<Reply.Complete>(stream);
        // The first statement after login tells only the setting it changed,
        // at its new value, before the completion.
        Assert.Equal(["SESSION.TIME_ZONE=+01:00"], set.OfType<Reply.Status>().Select(status => $"{status.Name}={status.Value}"));
        Assert.IsType<Reply.Status>(set[^2]);
    }

    // The replies the server sends, up to and with the first of type T.
    private static async Task<List<Reply>> RepliesUntilAsync<T>(NetworkStream stream)
        where T : Reply
    {
        var replies = new List<Reply>();
        while (replies.Count == 0 || replies[^1] is not T)
        {
            byte[] head = new byte[FrameHeader.Size];
            await stream.ReadExactlyAsync(head);
            var header = FrameHeader.Decode(head, FrameHeader.MaxBody);
            byte[] body = new byte[header.Length];
            await stream.ReadExactlyAsync(body);
            replies.Add(Reply.Decode(header.Kind, body));
        }

        return replies;
    }

    /// <summary>A connection that asks for nothing is granted nothing, and told no status.</summary>
    [Fact]
    [Verifies("[PROTOCOL The Native Protocol]")]
    [Verifies("[CLIENT What a connection asks for]")]
    public async Task OfferOfNothingSeesTheFirstForm()
    {
        server.Require();
        var options = new ConnectOptions { Offer = Offer.None() };
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), options, TestContext.Current.CancellationToken);

        Assert.Empty(connection.Granted);
        Assert.Empty(connection.Settings);
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>trust, password and SCRAM over the local socket, or on Windows the named pipe, where no TLS is needed.</summary>
    [Fact]
    [Verifies("[PROTOCOL The Native Protocol]")]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[CLIENT Connecting]")]
    public async Task LocalSocketTakesEveryMechanism()
    {
        server.Require();
        var local = new LocalTarget(server.Socket);

        foreach (Mechanism mechanism in new Mechanism[] { new TrustMechanism(), new PasswordMechanism(LiveServer.Password), new ScramMechanism(LiveServer.Password) })
        {
            NativeConnection connection = await NativeConnection.ConnectAsync(local, "ann", mechanism, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(LiveServer.Owner, connection.Authorization);
            await connection.TerminateAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Over TLS, SCRAM is SCRAM-SHA-256-PLUS bound by tls-server-end-point,
    /// the server proving it knows the verifier; a password is sent whole;
    /// a certificate proves the user its common name names.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL The Native Protocol]")]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[CLIENT Connecting]")]
    public async Task TlsTakesScramPlusPasswordAndCertificate()
    {
        server.Require();
        var scram = new ScramMechanism(LiveServer.Password) { Binding = ChannelBinding.Require };
        var presenting = new TlsTarget("localhost", LiveServer.Port, new TlsOptions
        {
            RootsFile = server.Certificates!.AuthorityFile,
            CertificateFile = server.Certificates.ClientFile,
            KeyFile = server.Certificates.ClientKeyFile,
        });

        NativeConnection plus = await NativeConnection.ConnectAsync(Tls, "ann", scram, cancellationToken: TestContext.Current.CancellationToken);
        await plus.TerminateAsync(TestContext.Current.CancellationToken);

        // The same mechanism binds afresh: over the socket, no channel to bind.
        scram.Binding = ChannelBinding.Prefer;
        await (await NativeConnection.ConnectAsync(new LocalTarget(server.Socket), "ann", scram, cancellationToken: TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ScramMechanism.MechanismName, scram.Name);
        await (await NativeConnection.ConnectAsync(Tls, "ann", new PasswordMechanism(LiveServer.Password), cancellationToken: TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        NativeConnection certified = await NativeConnection.ConnectAsync(presenting, "ann", new CertificateMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(LiveServer.Owner, certified.Authorization);
        await certified.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A TLS stream the program opened, bound to SCRAM before it is given,
    /// carries SCRAM-SHA-256-PLUS by tls-server-end-point, over TLS 1.2 and
    /// TLS 1.3 alike, the server proving it knows the verifier (RULES.md D8).
    /// </summary>
    /// <param name="protocol">The TLS version asked for.</param>
    [Theory]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[CLIENT Connecting]")]
    [InlineData(SslProtocols.Tls12)]
    [InlineData(SslProtocols.Tls13)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5398:Avoid hardcoded SslProtocols values",
        Justification = "R-LINT-3: the test asks for each TLS version in turn, to show the binding over both")]
    public async Task StreamTheProgramOpenedIsBound(SslProtocols protocol)
    {
        server.Require();
        Assert.SkipWhen(protocol == SslProtocols.Tls13 && OperatingSystem.IsMacOS(), ".NET's TLS on macOS speaks TLS 1.2 at most; Linux's run shows TLS 1.3");
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", LiveServer.Port, TestContext.Current.CancellationToken);
        var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => IssuedByTheAuthority(certificate));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "localhost", EnabledSslProtocols = protocol }, TestContext.Current.CancellationToken);
        Assert.Equal(protocol, tls.SslProtocol);
        var scram = new ScramMechanism(LiveServer.Password) { Binding = ChannelBinding.Require };
        scram.Bind(new TlsChannel(tls.RemoteCertificate!.GetRawCertData()));
        Assert.Equal(ScramMechanism.PlusName, scram.Name);

        NativeConnection connection = await NativeConnection.OpenAsync(tls, "ann", scram, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(LiveServer.Owner, connection.Authorization);
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A server whose certificate no authority trusted issued, or of another name, is refused, saying why.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task UntrustedServerIsRefused()
    {
        server.Require();
        var otherName = new TlsTarget("127.0.0.1", LiveServer.Port, new TlsOptions { RootsFile = server.Certificates!.AuthorityFile, ServerName = "elsewhere" });
        var noAuthority = new TlsTarget("localhost", LiveServer.Port, new TlsOptions { RootsFile = server.Certificates.ServerFile });

        TurborelException named = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(otherName, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("not of the name checked", named.Message, StringComparison.Ordinal);
        TurborelException issued = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(noAuthority, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("TLS handshake failed", issued.Message, StringComparison.Ordinal);
        Assert.Equal(Wire.FailedState, issued.SqlState);
    }

    /// <summary>
    /// A wrong password is refused, SQLSTATE 28000, and the key cache drops
    /// what it used; a right one's keys are kept, a second login skipping
    /// the derivation.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    [Verifies("[CLIENT The SCRAM key cache]")]
    public async Task KeyCacheSkipsTheDerivationOfALoginMadeAgain()
    {
        server.Require();
        var cache = new KeyCache();

        TurborelException wrong = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(Tls, "ann", new ScramMechanism("wrong") { Cache = cache }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("28000", wrong.SqlState);
        Assert.Equal(0, cache.Count);
        await (await NativeConnection.ConnectAsync(Tls, "ann", new ScramMechanism(LiveServer.Password) { Cache = cache }, cancellationToken: TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        await (await NativeConnection.ConnectAsync(Tls, "ann", new ScramMechanism(LiveServer.Password) { Cache = cache }, cancellationToken: TestContext.Current.CancellationToken)).TerminateAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1L, 2L), cache.Lookups);
    }

    /// <summary>A mechanism the server does not serve is refused before any exchange; another version, with 08004.</summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    public async Task VersionAndMechanismAreChecked()
    {
        server.Require();
        TurborelException mechanism = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(Tcp, "ann", new Named("no-such"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("28000", mechanism.SqlState);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", LiveServer.Port, TestContext.Current.CancellationToken);
        NetworkStream stream = tcp.GetStream();
        byte[] hello = Wire.Encode(new Request.Hello("ann", "trust", ReadOnlyMemory<byte>.Empty, [], []));
        hello[FrameHeader.Size] = 2;
        await stream.WriteAsync(hello, TestContext.Current.CancellationToken);
        byte[] reply = new byte[512];
        int got = await stream.ReadAtLeastAsync(reply, FrameHeader.Size, throwOnEndOfStream: false, cancellationToken: TestContext.Current.CancellationToken);
        Reply.Error error = Assert.IsType<Reply.Error>(Wire.DecodeReply(reply[..got]));
        Assert.Equal((-919, "08004"), (error.Failure.SqlCode, error.Failure.SqlState));
    }

    /// <summary>A user name that is no identifier is refused before any exchange, SQLSTATE 28000.</summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    public async Task UserNotAnIdentifierIsRefused()
    {
        server.Require();
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(Tcp, "1 no", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((-919, "28000"), (refused.SqlCode, refused.SqlState));
    }

    /// <summary>A statement that changes a setting is followed by its Status, which the connection keeps with no round trip.</summary>
    [Fact]
    [Verifies("[PROTOCOL Status 'T']")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task StatementsTellTheSettingsTheyChange()
    {
        server.Require();
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);

        await QueryAsync(connection, "SET SESSION.TIME_ZONE = '+05:30'");
        Assert.Equal("+05:30", connection.Setting("SESSION.TIME_ZONE"));
        await QueryAsync(connection, "SET SCHEMA 'INFORMATION_SCHEMA'");
        Assert.Equal("INFORMATION_SCHEMA", connection.Setting(StatusNames.Schema));
        Assert.Equal("INFORMATION_SCHEMA", connection.Settings[StatusNames.Schema]);
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Terminate ends the session's transaction as SESSION.COMMIT_AT_END
    /// says: committed while it is ON, rolled back once it is OFF.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Terminate 'X']")]
    [Verifies("[CLIENT Connecting]")]
    public async Task TerminateEndsTheTransactionAsTheSessionSays()
    {
        server.Require();
        string table = "ENDED_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        NativeConnection owner = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        await QueryAsync(owner, $"CREATE TABLE {table} (N INTEGER)");

        foreach ((string commitAtEnd, int n) in new[] { ("ON", 1), ("OFF", 2) })
        {
            NativeConnection session = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
            await QueryAsync(session, $"SET SESSION.COMMIT_AT_END = '{commitAtEnd}'");
            await QueryAsync(session, $"INSERT INTO {table} VALUES ({n})", ends: false);
            Assert.True(session.InTransaction);
            await session.TerminateAsync(TestContext.Current.CancellationToken);
        }

        await owner.SendAsync(new Request.Query($"SELECT N FROM {table}", Ends: true, []), TestContext.Current.CancellationToken);
        Assert.IsType<Reply.Description>(await owner.ReceiveAsync(TestContext.Current.CancellationToken));
        Reply.Rows rows = Assert.IsType<Reply.Rows>(await owner.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1", Assert.Single(rows.Batch)[0]?.ToString());
        Assert.IsType<Reply.Complete>(await owner.ReceiveAsync(TestContext.Current.CancellationToken));
        await QueryAsync(owner, $"DROP TABLE {table} CASCADE");
        await owner.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The connection's attributes in Hello are read by the server, as CONNECTION_ATTRIBUTES shows.</summary>
    [Fact]
    [Verifies("[PROTOCOL Hello 'H']")]
    [Verifies("[PROTOCOL Capabilities]")]
    public async Task HelloCarriesConnectionAttributes()
    {
        server.Require();
        var options = new ConnectOptions { Offer = Offer.Default.WithAttribute(Offer.Application, "turborel-cs-test") };
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), options, TestContext.Current.CancellationToken);

        await connection.SendAsync(new Request.Query("SELECT ATTRIBUTE_VALUE FROM INFORMATION_SCHEMA.CONNECTION_ATTRIBUTES WHERE ATTRIBUTE_NAME = 'application'", Ends: true, []), TestContext.Current.CancellationToken);
        Assert.IsType<Reply.Description>(await connection.ReceiveAsync(TestContext.Current.CancellationToken));
        Reply.Rows rows = Assert.IsType<Reply.Rows>(await connection.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Contains(rows.Batch, row => (string?)row[0] == "turborel-cs-test");
        Assert.IsType<Reply.Complete>(await connection.ReceiveAsync(TestContext.Current.CancellationToken));
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A statement running is cancelled from a connection of its own, the key Ready gave, and fails with SQLCODE -920.</summary>
    [Fact]
    [Verifies("[PROTOCOL Cancel 'Z']")]
    [Verifies("[CLIENT Running statements]")]
    public async Task CancelStopsAStatementRunning()
    {
        server.Require();
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        string table = "CANCELLED_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string rows = string.Join(", ", Enumerable.Range(0, 300).Select(i => $"({i})"));
        await QueryAsync(connection, $"CREATE TABLE {table} (N INTEGER)");
        await QueryAsync(connection, $"INSERT INTO {table} VALUES {rows}");

        await connection.SendAsync(new Request.Query($"SELECT COUNT(*) FROM {table} A, {table} B, {table} C", Ends: true, []), TestContext.Current.CancellationToken);
        Task<Reply> running = ReplyAsync(connection);
        // Cancelled once it runs: a cancel before it is of nothing.
        while (!running.IsCompleted)
        {
            await connection.Canceller!.CancelAsync(cancellationToken: TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
        }

        Reply.Error error = Assert.IsType<Reply.Error>(await running);
        Assert.Equal((-920, "TB920"), (error.Failure.SqlCode, error.Failure.SqlState));
        await QueryAsync(connection, $"DROP TABLE {table} CASCADE");
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    private static async Task QueryAsync(NativeConnection connection, string sql, bool ends = true)
    {
        await connection.SendAsync(new Request.Query(sql, ends, []), default);
        Reply reply = await connection.ReceiveAsync(default);
        Assert.True(reply is Reply.Complete, (reply as Reply.Error)?.Failure.Message);
    }

    // The statement's last reply, past its description and rows.
    private static async Task<Reply> ReplyAsync(NativeConnection connection)
    {
        Reply reply = await connection.ReceiveAsync(default);
        while (reply is Reply.Description or Reply.Rows)
        {
            reply = await connection.ReceiveAsync(default);
        }

        return reply;
    }

    /// <summary>A connection asking for compression is granted it, and its requests and replies go compressed.</summary>
    [Fact]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[PROTOCOL Opening a connection]")]
    public async Task CompressionIsGranted()
    {
        server.Require();
        var options = new ConnectOptions { Offer = Offer.Default.Compressed() with { CompressFrom = 1 } };
        NativeConnection connection = await NativeConnection.ConnectAsync(Tcp, "ann", new TrustMechanism(), options, TestContext.Current.CancellationToken);

        Assert.Contains(Capabilities.Compression, connection.Granted);
        string sql = "VALUES " + string.Join(", ", Enumerable.Range(0, 300).Select(i => $"'{new string('x', 20)}{i}'"));
        await connection.SendAsync(new Request.Query(sql, Ends: true, []), TestContext.Current.CancellationToken);
        Assert.IsType<Reply.Description>(await connection.ReceiveAsync(TestContext.Current.CancellationToken));
        Assert.Equal(300, Assert.IsType<Reply.Rows>(await connection.ReceiveAsync(TestContext.Current.CancellationToken)).Batch.Count);
        Assert.IsType<Reply.Complete>(await connection.ReceiveAsync(TestContext.Current.CancellationToken));
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
    }

    private bool IssuedByTheAuthority(X509Certificate? certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(Turborel.Data.Tls.Pem.Certificates(server.Certificates!.AuthorityFile));
        return certificate is X509Certificate2 issued && chain.Build(issued);
    }

    // A mechanism of a name and nothing else.
    private sealed class Named(string name) : Mechanism
    {
        public override string Name => name;

        public override byte[] Initial() => [];
    }
}
