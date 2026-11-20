// How a connection over TCP uses TLS [CLIENT Connecting] [CLIENT TLS by
// mode]: the authorities it trusts, from a PEM file, the platform's, or
// both; the certificate it presents, if any; the name the server's
// certificate is checked against; and how much of the certificate it
// verifies.

namespace Turborel.Data.Tls;

/// <summary>How a connection uses TLS.</summary>
public sealed record TlsOptions
{
    /// <summary>Gets the PEM file of the certificate authorities trusted, if any.</summary>
    public string? RootsFile { get; init; }

    /// <summary>
    /// Gets a value indicating whether the platform's authorities are trusted
    /// too: the system's store, or the files SSL_CERT_FILE and SSL_CERT_DIR
    /// name instead, if either is set.
    /// </summary>
    public bool SystemRoots { get; init; }

    /// <summary>Gets the PEM file of the client's certificate chain, presented to the server, if any.</summary>
    public string? CertificateFile { get; init; }

    /// <summary>Gets the PEM file of the client certificate's private key.</summary>
    public string? KeyFile { get; init; }

    /// <summary>Gets the name the server's certificate is checked against; the host's if null.</summary>
    public string? ServerName { get; init; }

    /// <summary>Gets a value indicating whether the server's certificate must be signed by an authority trusted.</summary>
    public bool VerifyAuthority { get; init; } = true;

    /// <summary>Gets a value indicating whether the server's certificate must be of the name checked.</summary>
    public bool VerifyName { get; init; } = true;
}
