// The live tests' certificates, made as the tests begin, so no secret is
// ever committed: an authority, the server's certificate it issues for
// localhost and 127.0.0.1, and a client's for the user ANN, each written
// as PEM files to the tests' own directory.

using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Turborel.Data.Tests.Server;

/// <summary>The PEM files of the live tests' authority, server and client.</summary>
internal sealed class TestCertificates
{
    private TestCertificates(string directory)
    {
        AuthorityFile = Path.Combine(directory, "ca.pem");
        ServerFile = Path.Combine(directory, "server.pem");
        ServerKeyFile = Path.Combine(directory, "server.key");
        ClientFile = Path.Combine(directory, "client.pem");
        ClientKeyFile = Path.Combine(directory, "client.key");
    }

    /// <summary>Gets the authority's certificate.</summary>
    internal string AuthorityFile { get; }

    /// <summary>Gets the server's certificate.</summary>
    internal string ServerFile { get; }

    /// <summary>Gets the server's key.</summary>
    internal string ServerKeyFile { get; }

    /// <summary>Gets the client's certificate, of the user ANN.</summary>
    internal string ClientFile { get; }

    /// <summary>Gets the client's key.</summary>
    internal string ClientKeyFile { get; }

    /// <summary>Makes the certificates in <paramref name="directory"/>.</summary>
    /// <param name="directory">Where their files go.</param>
    /// <returns>Their files.</returns>
    internal static TestCertificates Make(string directory)
    {
        var made = new TestCertificates(directory);
        using var authorityKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var authorityRequest = new CertificateRequest("CN=turborel-cs test authority", authorityKey, HashAlgorithmName.SHA256);
        authorityRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        authorityRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using X509Certificate2 authority = authorityRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllText(made.AuthorityFile, authority.ExportCertificatePem());
        Issue(authority, "CN=localhost", server: true, (made.ServerFile, made.ServerKeyFile));
        Issue(authority, "CN=ANN", server: false, (made.ClientFile, made.ClientKeyFile));
        return made;
    }

    private static void Issue(X509Certificate2 authority, string subject, bool server, (string Certificate, string Key) files)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        string usage = server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2";
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(usage)], false));
        if (server)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }

        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using X509Certificate2 issued = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29), serial);
        File.WriteAllText(files.Certificate, issued.ExportCertificatePem());
        File.WriteAllText(files.Key, key.ExportPkcs8PrivateKeyPem());
    }
}
