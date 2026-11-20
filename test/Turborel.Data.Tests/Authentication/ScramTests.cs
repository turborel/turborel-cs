// SCRAM-SHA-256 by RFC 7677's test vector (RULES.md R-TEST-9), its
// refusals, its binding to the TLS channel, and the key cache
// [PROTOCOL Mechanisms] [CLIENT The SCRAM key cache].

using System;
using System.Linq;
using System.Text;
using Turborel.Data.Authentication;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tests.Tls;
using Turborel.Data.Tls;
using Xunit;

namespace Turborel.Data.Tests.Authentication;

/// <summary>Tests of <see cref="ScramExchange"/>, <see cref="ScramMechanism"/> and <see cref="KeyCache"/>.</summary>
public sealed class ScramTests
{
    private const string ServerFirst = "r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096";

    /// <summary>RFC 7677's exchange: client-first, client-final and the server's signature, byte for byte.</summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms] [RFC 7677 §3]")]
    public void Rfc7677TestVector()
    {
        var exchange = new ScramExchange(ScramExchange.Unbound, []);

        Assert.Equal("n,,n=user,r=rOprNGfwEbeRWgbNEkqO", Encoding.UTF8.GetString(exchange.First("rOprNGfwEbeRWgbNEkqO", "user")));
        (string Message, string Both, byte[] Salt, uint Iterations) first = exchange.Read(Encoding.UTF8.GetBytes(ServerFirst));
        byte[] final = exchange.Prove(first, ScramKeys.Of("pencil", first.Salt, first.Iterations));
        Assert.Equal(
            "c=biws,r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0,p=dHzbZapWIk4jUhN+Ute9ytag9zjfMHgsqmmiz7AndVQ=",
            Encoding.UTF8.GetString(final));
        Refused(() => exchange.Read(Encoding.UTF8.GetBytes(ServerFirst)));
        exchange.Verify(Encoding.UTF8.GetBytes("v=6rriTRBi23WpRR/wtup+mMhUZUn/dB5nLTJRsjl95G4="));
        Assert.True(exchange.Verified);
    }

    /// <summary>A server-first that does not extend the client's nonce, malformed, or out of turn is refused, SQLSTATE 28000.</summary>
    /// <param name="serverFirst">The server's first message.</param>
    [Theory]
    [Verifies("[PROTOCOL Mechanisms]")]
    [InlineData("r=other,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqO,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=!!!,i=4096")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=0")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gQ==")]
    [InlineData("s=W22ZaJ0SNY7soEsUEjb6gQ==,r=rOprNGfwEbeRWgbNEkqOx,i=1")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22Z aJ0SNY7soEsUEjb6gQ==,i=4096")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gR==,i=4096")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=-1")]
    [InlineData("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4294967296")]
    public void BadServerFirstIsRefused(string serverFirst)
    {
        var exchange = new ScramExchange(ScramExchange.Unbound, []);
        _ = exchange.First("rOprNGfwEbeRWgbNEkqO");

        Refused(() => exchange.Read(Encoding.UTF8.GetBytes(serverFirst)));
        Refused(() => new ScramExchange(ScramExchange.Unbound, []).Read(Encoding.UTF8.GetBytes(ServerFirst)));
    }

    /// <summary>Iterations up to the largest <c>u32</c> are taken, a leading + too, as turborel-protocol reads them; past .NET's own PBKDF2, the keys are derived alike.</summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms]")]
    public void IterationsAreAnyPositiveU32()
    {
        var exchange = new ScramExchange(ScramExchange.Unbound, []);
        _ = exchange.First("rOprNGfwEbeRWgbNEkqO");
        Assert.Equal(uint.MaxValue, exchange.Read(Encoding.UTF8.GetBytes("r=rOprNGfwEbeRWgbNEkqOx,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4294967295")).Iterations);
        var signed = new ScramExchange(ScramExchange.Unbound, []);
        _ = signed.First("rOprNGfwEbeRWgbNEkqO");
        Assert.Equal(7U, signed.Read(Encoding.UTF8.GetBytes("r=rOprNGfwEbeRWgbNEkqOx,s=,i=+7")).Iterations);
        byte[] salt = Convert.FromBase64String("W22ZaJ0SNY7soEsUEjb6gQ==");
        Assert.Equal(
            System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2("pencil"u8, salt, 4096, System.Security.Cryptography.HashAlgorithmName.SHA256, 32),
            ScramKeys.Salted(Encoding.UTF8.GetBytes("pencil"), salt, 4096));
    }

    /// <summary>A wrong signature, or the server's error, is refused; a server that never proved itself is not taken at Ready.</summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[CLIENT Connecting]")]
    public void ServerMustProveItself()
    {
        var mechanism = new ScramMechanism("pencil");
        _ = mechanism.Initial();

        Wire.Fails(mechanism.CheckReady);
        Refused(() => mechanism.Answer(Encoding.UTF8.GetBytes("v=AAAA")));
        Refused(() => mechanism.Answer(Encoding.UTF8.GetBytes("e=invalid-proof")));
        Assert.Equal("ScramMechanism(..)", mechanism.ToString());
        Assert.True(mechanism.IsSecret);
    }

    /// <summary>
    /// Bound to a TLS channel, the mechanism is SCRAM-SHA-256-PLUS by
    /// tls-server-end-point; with binding disabled, or required and
    /// unbound, it is not.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms]")]
    [Verifies("[CLIENT Connecting]")]
    public void BindsByTheServersCertificate()
    {
        byte[] certificate = EndPointTests.Certificate(EndPointTests.Algorithm(EndPointTests.Sha256Ecdsa, []));
        var bound = new ScramMechanism("p");
        bound.Bind(new TlsChannel(certificate));
        var disabled = new ScramMechanism("p") { Binding = ChannelBinding.Disable };
        disabled.Bind(new TlsChannel(certificate));
        var unoffered = new ScramMechanism("p");
        unoffered.Bind(new TlsChannel(null));

        Assert.Equal((ScramMechanism.PlusName, ScramMechanism.MechanismName, ScramMechanism.MechanismName), (bound.Name, disabled.Name, unoffered.Name));
        Assert.StartsWith("p=tls-server-end-point,,", Encoding.UTF8.GetString(bound.Initial()), StringComparison.Ordinal);
        Assert.StartsWith("y,,", Encoding.UTF8.GetString(unoffered.Initial()), StringComparison.Ordinal);
        var required = new ScramMechanism("p") { Binding = ChannelBinding.Require };
        byte[] first = required.Initial();
        string nonce = Encoding.UTF8.GetString(first).Split("r=")[1];
        Refused(() => required.Answer(Encoding.UTF8.GetBytes($"r={nonce}x,s=AAAA,i=1")));
    }

    /// <summary>
    /// The key cache keeps a login's keys, found again by the salt,
    /// iterations and password's tag; counts its lookups; holds 64 at most,
    /// the oldest dropped; and drops an entry a failed login used.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The SCRAM key cache]")]
    public void KeyCacheKeepsAndForgets()
    {
        var cache = new KeyCache();
        byte[] salt = [1, 2, 3];

        ScramKeys first = cache.Derived("pencil", salt, 4096);
        Assert.Same(first, cache.Derived("pencil", salt, 4096));
        Assert.NotSame(first, cache.Derived("other", salt, 4096));
        Assert.Equal((1L, 2L), cache.Lookups);
        cache.Forget("pencil", salt, 4096);
        Assert.Equal(1, cache.Count);
        foreach (byte i in Enumerable.Range(0, KeyCache.Entries).Select(i => (byte)i))
        {
            _ = cache.Derived("p", [i], 1);
        }

        Assert.Equal(KeyCache.Entries, cache.Count);
        Assert.Equal((1L, 2L + KeyCache.Entries), cache.Lookups);
        cache.Clear();
        Assert.Equal("KeyCache(0 entries)", cache.ToString());
    }

    /// <summary>A mechanism given a cache uses it, and forgets what a login it refused used.</summary>
    [Fact]
    [Verifies("[CLIENT The SCRAM key cache]")]
    public void MechanismUsesItsCache()
    {
        var cache = new KeyCache();
        var mechanism = new ScramMechanism("pencil") { Cache = cache };
        string nonce = Encoding.UTF8.GetString(mechanism.Initial()).Split("r=")[1];

        _ = mechanism.Answer(Encoding.UTF8.GetBytes($"r={nonce}x,s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096"));
        Assert.Equal(1, cache.Count);
        mechanism.Refused();
        Assert.Equal(0, cache.Count);
    }

    private static void Refused(Action act)
    {
        TurborelException failure = Assert.Throws<TurborelException>(act);
        Assert.Equal((-919, "28000"), (failure.SqlCode, failure.SqlState));
    }
}
