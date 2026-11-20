// TLS begun on a stream to the server, before Hello [PROTOCOL The Native
// Protocol] [CLIENT Connecting]: the server's certificate verified against
// the authorities the options trust, and the name they check, a
// certificate that is its own authority refused, as turborel-client's
// rustls refuses one; the client's certificate presented, if any.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Transport;

namespace Turborel.Data.Tls;

/// <summary>Begins TLS on a stream, as its options say.</summary>
internal static class TlsHandshake
{
    /// <summary>The variable naming a file of authorities trusted in place of the platform's store.</summary>
    internal const string CertFileVariable = "SSL_CERT_FILE";

    /// <summary>The variable naming a directory of authorities trusted in place of the platform's store.</summary>
    internal const string CertDirVariable = "SSL_CERT_DIR";

    /// <summary>Secures <paramref name="stream"/>, checking the server as <paramref name="options"/> say.</summary>
    /// <param name="stream">The stream to the server.</param>
    /// <param name="host">The name connected to, checked unless the options name another.</param>
    /// <param name="options">How TLS is used.</param>
    /// <param name="within">How long the handshake may wait; null for no limit.</param>
    /// <param name="cancellationToken">The program's token.</param>
    /// <returns>The secured stream, and what its handshake gives a mechanism to bind.</returns>
    /// <exception cref="TurborelException">A connection failure, SQLSTATE 08006, if the handshake fails, saying why.</exception>
    internal static async Task<(SslStream Stream, TlsChannel Channel)> SecureAsync(
        Stream stream,
        string host,
        TlsOptions options,
        TimeSpan? within,
        CancellationToken cancellationToken)
    {
        var trust = Trust.Of(options);
        string? refusal = null;
        var tls = new SslStream(stream, leaveInnerStreamOpen: false, (_, certificate, chain, errors) =>
        {
            refusal = trust.Refusal(certificate as X509Certificate2, chain, errors);
            return refusal is null;
        });
        var authentication = new SslClientAuthenticationOptions
        {
            TargetHost = options.ServerName ?? host,
            ClientCertificateContext = ClientCertificate.Of(options),
        };
        try
        {
            await Waits.Within("begin TLS", within, token => tls.AuthenticateAsClientAsync(authentication, token), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TurborelException e)
        {
            await tls.DisposeAsync().ConfigureAwait(false);
            throw new TurborelException(e.SqlCode, e.SqlState, $"[DESIGN] the TLS handshake failed: {refusal ?? e.InnerException?.Message}", e);
        }

        return (tls, new TlsChannel(tls.RemoteCertificate is { } remote ? remote.GetRawCertData() : null));
    }

    /// <summary>The authorities trusted, and what is checked of the server's certificate.</summary>
    private sealed class Trust
    {
        private readonly TlsOptions _options;
        private readonly X509Certificate2Collection _custom;
        private readonly bool _system;

        private Trust(TlsOptions options, X509Certificate2Collection custom, bool system)
        {
            _options = options;
            _custom = custom;
            _system = system;
        }

        internal static Trust Of(TlsOptions options)
        {
            var custom = new X509Certificate2Collection();
            if (options.RootsFile is { } file)
            {
                custom.AddRange(Pem.Certificates(file));
            }

            bool system = false;
            if (options.SystemRoots)
            {
                X509Certificate2Collection named = Pem.FromVariables(CertFileVariable, CertDirVariable);
                custom.AddRange(named);
                system = named.Count == 0;
            }

            bool none = custom.Count == 0 && !system;
            return none && options.VerifyAuthority
                ? throw ConnectionFailure.Of("[DESIGN] no certificate authority is trusted: name a file of them, or the platform's")
                : new Trust(options, custom, system);
        }

        // Why the server's certificate is refused; null if it is taken.
        internal string? Refusal(X509Certificate2? certificate, X509Chain? presented, SslPolicyErrors errors)
        {
            if (!_options.VerifyAuthority)
            {
                return null;
            }

            if (certificate is null)
            {
                return "the server presented no certificate";
            }

            if (_options.VerifyName && errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            {
                return "the server's certificate is not of the name checked";
            }

            IEnumerable<X509Certificate2> chain = presented?.ChainElements.Select(e => e.Certificate) ?? [];
            return Issued(certificate, chain, custom: true) || (_system && Issued(certificate, chain, custom: false))
                ? null
                : "the server's certificate is not issued by an authority trusted";
        }

        private bool Issued(X509Certificate2 certificate, IEnumerable<X509Certificate2> presented, bool custom)
        {
            if (custom && _custom.Count == 0)
            {
                return false;
            }

            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ExtraStore.AddRange(presented.ToArray());
            if (custom)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(_custom);
            }

            // A certificate that is its own authority is refused: an authority issues the server's.
            return chain.Build(certificate) && chain.ChainElements.Count > 1;
        }
    }
}
