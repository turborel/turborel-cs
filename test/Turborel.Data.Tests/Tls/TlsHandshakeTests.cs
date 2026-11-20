// TLS begun with a server of the test's own on loopback, its certificate
// the test's authority issued: taken when trusted and of the name checked,
// refused, saying why, otherwise; the client's certificate presented
// [CLIENT Connecting].

using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tls;
using Xunit;

namespace Turborel.Data.Tests.Tls;

/// <summary>Tests of <see cref="TlsHandshake"/> and <see cref="TlsTarget"/>.</summary>
public sealed class TlsHandshakeTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("turborel-cs-tls-");
    private readonly TestCertificates _certificates;

    /// <summary>Initializes a new instance of the <see cref="TlsHandshakeTests"/> class, its certificates made.</summary>
    public TlsHandshakeTests()
    {
        _certificates = TestCertificates.Make(_directory.FullName);
    }

    /// <inheritdoc/>
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>A server trusted and of the name checked is taken, its certificate given to bind; the client's is presented.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task TrustedServerIsTaken()
    {
        var options = new TlsOptions
        {
            RootsFile = _certificates.AuthorityFile,
            CertificateFile = _certificates.ClientFile,
            KeyFile = _certificates.ClientKeyFile,
        };
        X509Certificate? presented = null;

        await Serve(async tls => presented = tls.RemoteCertificate, async port =>
        {
            (Stream stream, TlsChannel? channel) = await new TlsTarget("localhost", port, options).OpenAsync(TimeSpan.FromSeconds(10), default);
            await stream.DisposeAsync();
            Assert.NotNull(channel?.Certificate);
        });
        Assert.Equal("CN=ANN", presented?.Subject);
    }

    /// <summary>A server of another name, or whose certificate no authority trusted issued, is refused, saying why.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task UntrustedServerIsRefused()
    {
        var otherName = new TlsOptions { RootsFile = _certificates.AuthorityFile, ServerName = "elsewhere" };
        var otherAuthority = new TlsOptions { RootsFile = _certificates.ClientFile };

        foreach ((TlsOptions options, string why) in new[] { (otherName, "not of the name checked"), (otherAuthority, "not issued by an authority trusted") })
        {
            await Serve(_ => Task.CompletedTask, async port =>
            {
                TurborelException failure = await Assert.ThrowsAsync<TurborelException>(
                    () => new TlsTarget("localhost", port, options).OpenAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                Assert.Contains(why, failure.Message, StringComparison.Ordinal);
                Assert.Equal(Wire.FailedState, failure.SqlState);
            });
        }
    }

    /// <summary>A client certificate is presented with its key: one named without it is refused before the handshake.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task CertificateNeedsItsKey()
    {
        var keyless = new TlsOptions { RootsFile = _certificates.AuthorityFile, CertificateFile = _certificates.ClientFile };

        await Serve(_ => Task.CompletedTask, async port =>
        {
            TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => new TlsTarget("localhost", port, keyless).OpenAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.Contains("presented with its key, and none is named", refused.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>With nothing verified, as under prefer and require, any certificate is taken; with nothing trusted, none can be verified.</summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task VerifyingNothingTakesAnyCertificate()
    {
        var unverified = new TlsOptions { VerifyAuthority = false };

        await Serve(_ => Task.CompletedTask, async port =>
        {
            (Stream stream, _) = await new TlsTarget("127.0.0.1", port, unverified).OpenAsync(TimeSpan.FromSeconds(10), default);
            await stream.DisposeAsync();
        });
        Wire.Fails(() => new TlsTarget("localhost", 1, new TlsOptions()).OpenAsync(null, default).GetAwaiter().GetResult());
    }

    /// <summary>
    /// The platform's authorities are the files SSL_CERT_FILE names, in
    /// place of the system's store, when it is set; the system's store,
    /// which never issued the test's server, when neither variable is.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Connecting]")]
    public async Task PlatformAuthoritiesAreTheVariablesFiles()
    {
        var platform = new TlsOptions { SystemRoots = true };
        string? file = Environment.GetEnvironmentVariable(TlsHandshake.CertFileVariable);
        string? directory = Environment.GetEnvironmentVariable(TlsHandshake.CertDirVariable);
        try
        {
            Environment.SetEnvironmentVariable(TlsHandshake.CertFileVariable, _certificates.AuthorityFile);
            Environment.SetEnvironmentVariable(TlsHandshake.CertDirVariable, null);
            await Serve(_ => Task.CompletedTask, async port =>
            {
                (Stream stream, _) = await new TlsTarget("localhost", port, platform).OpenAsync(TimeSpan.FromSeconds(10), default);
                await stream.DisposeAsync();
            });

            Environment.SetEnvironmentVariable(TlsHandshake.CertFileVariable, null);
            await Serve(_ => Task.CompletedTask, async port =>
            {
                TurborelException failure = await Assert.ThrowsAsync<TurborelException>(
                    () => new TlsTarget("localhost", port, platform).OpenAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
                Assert.Contains("not issued by an authority trusted", failure.Message, StringComparison.Ordinal);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(TlsHandshake.CertFileVariable, file);
            Environment.SetEnvironmentVariable(TlsHandshake.CertDirVariable, directory);
        }
    }

    // The test's server takes a client presenting a certificate or none:
    // which one is the client's own concern.
    private static bool AnyClient(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        errors != SslPolicyErrors.RemoteCertificateNameMismatch;

    private async Task Serve(Func<SslStream, Task> serve, Func<int, Task> client)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var pem = X509Certificate2.CreateFromPemFile(_certificates.ServerFile, _certificates.ServerKeyFile);
        using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
        var served = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(accepted.GetStream(), false, AnyClient);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, ClientCertificateRequired = true });
                await serve(tls);
            }
            catch (Exception e) when (e is IOException or System.Security.Authentication.AuthenticationException)
            {
                // The client refused the server, as the test means it to.
            }
        });
        await client(port);
        await served;
    }
}
