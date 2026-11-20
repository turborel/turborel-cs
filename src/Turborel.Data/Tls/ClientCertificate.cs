// The certificate a client presents over TLS, its chain and key from two
// PEM files, by which the mechanism `certificate` proves its user
// [CLIENT Connecting].

using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Turborel.Data.Protocol;

namespace Turborel.Data.Tls;

/// <summary>Reads the certificate a client presents.</summary>
internal static class ClientCertificate
{
    /// <summary>The client's certificate and chain, if the options name them.</summary>
    /// <param name="options">How TLS is used.</param>
    /// <returns>The certificate's context; null if none is presented.</returns>
    /// <exception cref="TurborelException">A connection failure if the files cannot be read.</exception>
    internal static SslStreamCertificateContext? Of(TlsOptions options)
    {
        if (options.CertificateFile is not { } chain)
        {
            return null;
        }

        string key = options.KeyFile ?? throw ConnectionFailure.Of("[DESIGN] a client certificate is presented with its key, and none is named");
        try
        {
            using var pem = X509Certificate2.CreateFromPemFile(chain, key);
            // Windows presents a key only from a certificate loaded with it, so it is loaded again.
            X509Certificate2 leaf = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            X509Certificate2Collection rest = Pem.Certificates(chain);
            return SslStreamCertificateContext.Create(leaf, [.. rest.Skip(1)], offline: true);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
            throw new TurborelException(ConnectionFailure.SqlCode, ConnectionFailure.SqlState, $"[DESIGN] cannot present {chain}: {e.Message}", e);
        }
    }
}
