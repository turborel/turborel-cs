// What a TLS connection's handshake gives a mechanism to bind itself to
// [CLIENT Connecting]: the certificate the server presented. .NET's
// SslStream exports no keying material, so there is no tls-exporter.

using System;

namespace Turborel.Data.Tls;

/// <summary>What a TLS handshake gives a mechanism to bind itself to.</summary>
/// <param name="Certificate">The certificate the server presented, the first of its chain, as DER.</param>
public sealed record TlsChannel(ReadOnlyMemory<byte>? Certificate);
