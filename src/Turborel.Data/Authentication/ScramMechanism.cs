// The client's side of `scram-sha-256` and `scram-sha-256-plus`
// [PROTOCOL Mechanisms] [CLIENT Connecting], as turborel-client's
// scram.rs works it: the client proves it knows the password, never
// sending it, and takes the server only once it proved it knows the
// user's verifier. Over TLS it binds the exchange by tls-server-end-point,
// the hash of the certificate the server presented; .NET exports no TLS
// keying material, so tls-exporter is never used. A key cache, if given,
// spares a login made again the slow derivation.

using System;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Tls;

namespace Turborel.Data.Authentication;

/// <summary>Proves the user's password by SCRAM-SHA-256, or SCRAM-SHA-256-PLUS over TLS.</summary>
/// <param name="password">The password.</param>
public sealed class ScramMechanism(string password) : Mechanism
{
    /// <summary>The mechanism's name [PROTOCOL Mechanisms].</summary>
    public const string MechanismName = "scram-sha-256";

    /// <summary>The mechanism bound to its channel [PROTOCOL Mechanisms].</summary>
    public const string PlusName = "scram-sha-256-plus";

    /// <summary>The first bytes of the server's final message, its signature.</summary>
    private const string SignaturePrefix = "v=";

    /// <summary>The first bytes of the server's final message refusing.</summary>
    private const string ErrorPrefix = "e=";

    private string _header = ScramExchange.Unbound;
    private byte[] _bound = [];
    private ScramExchange? _exchange;
    private (byte[] Salt, int Iterations)? _used;

    /// <summary>Gets or sets a value indicating whether SCRAM may be sent over plain TCP, to a server whose SERVER.SCRAM_WITHOUT_TLS is ON.</summary>
    public bool InTheClear { get; set; }

    /// <summary>Gets or sets whether the exchange binds its TLS channel.</summary>
    public ChannelBinding Binding { get; set; } = ChannelBinding.Prefer;

    /// <summary>Gets or sets the key cache the keys a login derives are kept in, if any.</summary>
    public KeyCache? Cache { get; set; }

    /// <inheritdoc/>
    public override string Name => _header == ScramExchange.EndPointHeader ? PlusName : MechanismName;

    /// <inheritdoc/>
    public override bool IsSecret => !InTheClear;

    /// <inheritdoc/>
    public override string ToString() => "ScramMechanism(..)";

    /// <inheritdoc/>
    public override byte[] Initial()
    {
        _exchange = new ScramExchange(_header, _bound);
        _used = null;
        return _exchange.First(ScramExchange.Nonce());
    }

    /// <inheritdoc/>
    public override byte[] Answer(ReadOnlySpan<byte> challenge)
    {
        try
        {
            if (challenge.StartsWith(Encoding.ASCII.GetBytes(SignaturePrefix)) || challenge.StartsWith(Encoding.ASCII.GetBytes(ErrorPrefix)))
            {
                Begun().Verify(challenge);
                return [];
            }

            return Prove(challenge);
        }
        catch (TurborelException)
        {
            Forget();
            throw;
        }
    }

    /// <inheritdoc/>
    public override void CheckReady()
    {
        if (_exchange is not { Verified: true })
        {
            throw ConnectionFailure.Of("[DESIGN] the server did not prove it knows the user's verifier");
        }
    }

    /// <inheritdoc/>
    public override void Bind(TlsChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (Binding == ChannelBinding.Disable)
        {
            return;
        }

        byte[]? endPoint = channel.Certificate is { } certificate ? EndPoint.Of(certificate.Span) : null;
        (_header, _bound) = endPoint is null ? (ScramExchange.Unoffered, []) : (ScramExchange.EndPointHeader, endPoint);
    }

    /// <inheritdoc/>
    public override void Refused() => Forget();

    private ScramExchange Begun() => _exchange ?? throw ConnectionFailure.Of("[DESIGN] SCRAM was not begun");

    private byte[] Prove(ReadOnlySpan<byte> challenge)
    {
        ScramExchange exchange = Begun();
        if (Binding == ChannelBinding.Require && !exchange.IsBound)
        {
            throw ScramFailure.Refused("channel binding is required, and this connection is not bound");
        }

        (string, string, byte[] Salt, int Iterations) first = exchange.Read(challenge);
        ScramKeys keys = Cache?.Derived(password, first.Salt, first.Iterations) ?? ScramKeys.Of(password, first.Salt, first.Iterations);
        _used = (first.Salt, first.Iterations);
        return exchange.Prove(first, keys);
    }

    private void Forget()
    {
        if (Cache is not null && _used is { } used)
        {
            Cache.Forget(password, used.Salt, used.Iterations);
        }

        _used = null;
    }
}
