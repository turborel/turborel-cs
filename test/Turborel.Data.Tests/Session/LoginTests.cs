// A connection's login, waits and end against a server of the test's own,
// byte for byte [PROTOCOL Opening a connection] [CLIENT Connecting]
// [CLIENT Timeouts].

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of <see cref="NativeConnection"/> against a scripted server.</summary>
public sealed class LoginTests
{
    private static readonly string[] Steps = ["one", "two"];

    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    /// <summary>
    /// Hello first, asking for every capability but compression; each Status
    /// before Ready kept; Ready's authorization, server and capabilities
    /// granted taken; Terminate ends it as the server closes.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    [Verifies("[CLIENT What a connection asks for]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task LogsInByHelloAndReady()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            (byte kind, byte[] body) = await peer.ReadAsync();
            Assert.Equal(RequestKinds.Hello, kind);
            Assert.Equal(Wire.Encode(new Request.Hello("ann", "trust", ReadOnlyMemory<byte>.Empty, Offer.Default.Capabilities, []))[5..], body);
            await peer.WriteAsync('T', Wire.Join(Wire.Str("SCHEMA"), Wire.Str("ANN")));
            await peer.ReadyAsync("status", "ping");
            Assert.Equal(RequestKinds.Terminate, (await peer.ReadAsync()).Kind);
        });

        NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken);
        Assert.Equal(("ANN", "turborel 0.0.0 (alpha)"), (connection.Authorization, connection.Server));
        Assert.Equal(["status", "ping"], connection.Granted);
        Assert.Equal("ANN", connection.Setting("SCHEMA"));
        Assert.Null(connection.Setting("SESSION.TIME_ZONE"));
        Assert.False(connection.InTransaction);
        await connection.TerminateAsync(TestContext.Current.CancellationToken);
        Assert.True(connection.IsLost);
        await server.PlayedAsync();
    }

    /// <summary>Each Challenge is answered by the mechanism, until Ready.</summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    public async Task ChallengesAreAnsweredUntilReady()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            foreach (string step in Steps)
            {
                await peer.WriteAsync('C', Wire.Written(w => w.WriteBytes(Encoding.ASCII.GetBytes(step))));
                (byte kind, byte[] body) = await peer.ReadAsync();
                Assert.Equal((RequestKinds.Answer, step.ToUpperInvariant()), (kind, Encoding.ASCII.GetString(body[4..])));
            }

            await peer.ReadyAsync();
        });

        await using NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new Echo(), Quick, TestContext.Current.CancellationToken);
        Assert.Empty(connection.Granted);
        await server.PlayedAsync();
    }

    /// <summary>A refusal at login is the server's failure, all of it, and the connection is lost.</summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    [Verifies("[PROTOCOL Error 'E']")]
    public async Task RefusalIsTheServersFailure()
    {
        var refusal = new Failure { SqlCode = -919, SqlState = "28000", Message = "the password is wrong", Diagnostics = new Diagnostics { Severity = "FATAL" } };
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.WriteAsync('E', Wire.Written(refusal.Write));
        });

        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken));
        Assert.Equal((-919, "28000", "the password is wrong", "FATAL", false), (failure.SqlCode, failure.SqlState, failure.Message, failure.Severity, failure.TransactionActive));
        Assert.True(refusal.EndsConnection);
        Assert.False(new Failure { SqlCode = -919, SqlState = "08003" }.EndsConnection);
        Assert.False(new Failure { SqlCode = -104, SqlState = "28000" }.EndsConnection);
    }

    /// <summary>A server that sends no Ready, or answers late, loses the connection, saying so.</summary>
    [Fact]
    [Verifies("[CLIENT Timeouts]")]
    public async Task LateOrWrongLoginLosesTheConnection()
    {
        await using var silent = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
        });
        var late = new ConnectOptions { Timeouts = new Timeouts(TimeSpan.FromMilliseconds(200), null) };

        TurborelException timedOut = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", silent.Port), "ann", new TrustMechanism(), late, TestContext.Current.CancellationToken));
        Assert.Equal((Wire.FailedCode, Wire.FailedState), (timedOut.SqlCode, timedOut.SqlState));
        Assert.Contains("the server did not answer in time", timedOut.Message, StringComparison.Ordinal);

        await using var wrong = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.WriteAsync('G', []);
        });
        TurborelException noReady = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", wrong.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken));
        Assert.Contains("no Ready", noReady.Message, StringComparison.Ordinal);
    }

    /// <summary>A reply longer than the body taken loses the connection; the bound is the program's to set.</summary>
    [Fact]
    [Verifies("[CLIENT Timeouts]")]
    [Verifies("[PROTOCOL Frames]")]
    public async Task ReplyPastTheBodyTakenLosesTheConnection()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.Stream.WriteAsync(Wire.Hex("43 01000001"));
            await peer.Stream.FlushAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
        });

        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(
            () => NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken));
        Assert.StartsWith(Sections.Frames, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Once compression is granted, Ready itself uncompressed, a long request
    /// goes compressed; a method needing a capability not granted fails before
    /// it sends anything.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Opening a connection]")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT What a connection asks for]")]
    public async Task CompressionBeginsAfterReady()
    {
        string sql = "SELECT " + string.Concat(Enumerable.Repeat("1, ", 1000)) + "1";
        await using var server = FakeServer.Start(async peer =>
        {
            (_, byte[] hello) = await peer.ReadAsync();
            Assert.Contains("compression", Encoding.ASCII.GetString(hello), StringComparison.Ordinal);
            await peer.ReadyAsync("compression");
            (byte kind, byte[] body) = await peer.ReadAsync();
            Assert.Equal(Compression.Kind, kind);
            (byte held, byte[] inflated) = Compression.Decompress(body, FrameHeader.MaxBody);
            Assert.Equal(Wire.Encode(new Request.Query(sql, Ends: false, []))[5..], inflated);
            Assert.Equal(RequestKinds.Query, held);
        });

        ConnectOptions options = Quick with { Offer = Offer.None().Compressed() };
        await using NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), options, TestContext.Current.CancellationToken);
        Assert.True(connection.Has("compression"));
        Wire.Fails(() => connection.Needs(Capabilities.Ping));
        await connection.SendAsync(new Request.Query(sql, Ends: false, []), TestContext.Current.CancellationToken);
        await server.PlayedAsync();
    }

    /// <summary>Terminate gives a failing commit's failure, which the server sends before it closes.</summary>
    [Fact]
    [Verifies("[PROTOCOL Terminate 'X']")]
    [Verifies("[CLIENT Connecting]")]
    public async Task TerminateGivesAFailingCommit()
    {
        var commit = new Failure { SqlCode = -913, SqlState = "40002", Message = "a deferred constraint is not satisfied" };
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.ReadyAsync();
            _ = await peer.ReadAsync();
            await peer.WriteAsync('E', Wire.Written(commit.Write));
        });

        NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken);
        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(() => connection.TerminateAsync(TestContext.Current.CancellationToken));
        Assert.Equal("40002", failure.SqlState);
    }

    /// <summary>Terminate waits at most the read timeout for the server to close.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    [Verifies("[CLIENT Timeouts]")]
    public async Task TerminateWaitsAtMostTheReadTimeout()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.ReadyAsync();
            _ = await peer.ReadAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
        });

        NativeConnection connection = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken);
        connection.SetReadTimeout(TimeSpan.FromMilliseconds(200));
        Assert.Equal(TimeSpan.FromMilliseconds(200), connection.ReadTimeout);
        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(() => connection.TerminateAsync(TestContext.Current.CancellationToken));
        Assert.Contains("in time", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A secret mechanism is refused over plain TCP before anything is sent, unless SCRAM is asked in the clear.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task SecretsAreRefusedOverPlainTcp()
    {
        var nowhere = new TcpTarget("127.0.0.1", 1);

        foreach (Mechanism secret in new Mechanism[] { new PasswordMechanism("p"), new ScramMechanism("p") })
        {
            TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(nowhere, "ann", secret, Quick, TestContext.Current.CancellationToken));
            Assert.Equal((-919, "28000"), (refused.SqlCode, refused.SqlState));
        }

        var clear = new ScramMechanism("p") { InTheClear = true };
        TurborelException unreached = await Assert.ThrowsAsync<TurborelException>(() => NativeConnection.ConnectAsync(nowhere, "ann", clear, Quick, TestContext.Current.CancellationToken));
        Assert.Equal(Wire.FailedState, unreached.SqlState);
    }

    /// <summary>
    /// A connection over a stream the program gave is bounded by its maker;
    /// the timeouts by default wait 30 seconds to connect, without limit to read.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Timeouts]")]
    public async Task StreamGivenIsBoundedByItsMaker()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            _ = await peer.ReadAsync();
            await peer.ReadyAsync();
        });
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", server.Port, TestContext.Current.CancellationToken);

        await using NativeConnection connection = await NativeConnection.OpenAsync(client.GetStream(), "ann", new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(connection.Target);
        Assert.Null(connection.Canceller);
        Wire.Fails(() => connection.SetReadTimeout(TimeSpan.FromSeconds(1)));
        Assert.Equal(new Timeouts(TimeSpan.FromSeconds(30), null), Timeouts.Default);
        Assert.Equal(!OperatingSystem.IsWindows(), new LocalTarget("x").BoundsReads);
    }

    /// <summary>The offer asks for every capability but compression unless told; none, compression and attributes are the program's.</summary>
    [Fact]
    [Verifies("[CLIENT What a connection asks for]")]
    public void OfferAsksForAllButCompression()
    {
        Assert.Equal(Capabilities.All.Count - 1, Offer.Default.Capabilities.Count);
        Assert.DoesNotContain(Capabilities.Compression, Offer.Default.Capabilities);
        Assert.Empty(Offer.None().Capabilities);
        Assert.Equal(["compression"], Offer.None().Compressed().Compressed().Capabilities);
        Assert.Equal(new("application", "app"), Assert.Single(Offer.None().WithAttribute(Offer.Application, "app").Attributes));
    }

    // A mechanism that answers each challenge by its letters in upper case.
    private sealed class Echo : Mechanism
    {
        public override string Name => "echo";

        public override byte[] Initial() => [];

        public override byte[] Answer(ReadOnlySpan<byte> challenge) => Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(challenge).ToUpperInvariant());
    }
}
