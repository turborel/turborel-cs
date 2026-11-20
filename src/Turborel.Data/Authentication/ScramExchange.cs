// The client's side of SCRAM-SHA-256 and SCRAM-SHA-256-PLUS (RFC 5802,
// RFC 7677), as turborel's protocol/src/scram/client.rs writes it: its
// first message, its proof answering the server's first, bound to the
// channel its GS2 header names, and the server's signature checked
// [PROTOCOL Mechanisms].

using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Turborel.Data.Authentication;

/// <summary>One SCRAM exchange of the client's.</summary>
internal sealed class ScramExchange
{
    /// <summary>The GS2 header of a client binding no channel [RFC 5802 §7].</summary>
    internal const string Unbound = "n,,";

    /// <summary>The GS2 header of a client that could bind, but sees the server offer none [RFC 5802 §6].</summary>
    internal const string Unoffered = "y,,";

    /// <summary>The GS2 header of a client binding by tls-server-end-point [RFC 5929 §4.2].</summary>
    internal const string EndPointHeader = "p=tls-server-end-point,,";

    /// <summary>The random bytes of a nonce: 144 bits, 24 characters of base 64.</summary>
    internal const int NonceBytes = 18;

    /// <summary>The least byte of a nonce, printable ASCII [RFC 5802 §7].</summary>
    private const char FirstPrintable = '!';

    /// <summary>The last byte of a nonce, printable ASCII.</summary>
    private const char LastPrintable = '~';

    /// <summary>The fields of server-first: r, s and i.</summary>
    private const int ServerFirstFields = 3;

    private readonly string _header;
    private readonly byte[] _bound;
    private string? _bare;
    private string? _nonce;
    private byte[]? _expected;

    /// <summary>Makes an exchange of the GS2 header and binding data given.</summary>
    /// <param name="header">Its GS2 header.</param>
    /// <param name="bound">The data its channel binds; none unbound.</param>
    internal ScramExchange(string header, byte[] bound)
    {
        _header = header;
        _bound = bound;
    }

    /// <summary>Gets a value indicating whether the server proved it knows the user's verifier.</summary>
    internal bool Verified { get; private set; }

    /// <summary>Gets a value indicating whether the exchange is bound to its channel.</summary>
    internal bool IsBound => _header == EndPointHeader;

    /// <summary>A new nonce, of the system's randomness: printable, without a comma.</summary>
    /// <returns>The nonce.</returns>
    internal static string Nonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(NonceBytes));

    /// <summary>
    /// client-first, of <paramref name="nonce"/>. The user is Hello's, so the
    /// driver leaves <c>n=</c> empty, as turborel-client does; the server
    /// reads it and leaves it aside.
    /// </summary>
    /// <param name="nonce">The client's nonce.</param>
    /// <param name="user">The saslname of <c>n=</c>, empty unless a test gives RFC 7677's.</param>
    /// <returns>The message.</returns>
    internal byte[] First(string nonce, string user = "")
    {
        _bare = $"n={user.Replace("=", "=3D", StringComparison.Ordinal).Replace(",", "=2C", StringComparison.Ordinal)},r={nonce}";
        _nonce = nonce;
        return Encoding.UTF8.GetBytes(_header + _bare);
    }

    /// <summary>Reads server-first, checked against this exchange.</summary>
    /// <param name="message">The server's first message.</param>
    /// <returns>Its text, both nonces, the salt and the iterations.</returns>
    /// <exception cref="TurborelException">SQLSTATE 28000 if it is not this exchange's server-first.</exception>
    internal (string Message, string Both, byte[] Salt, uint Iterations) Read(ReadOnlySpan<byte> message)
    {
        if (_nonce is null)
        {
            throw ScramFailure.Refused("the server's first message came out of turn");
        }

        string text = Text(message);
        string[] fields = text.Split(',');
        if (fields.Length != ServerFirstFields)
        {
            throw ScramFailure.Refused("the server's first message is not r, s and i");
        }

        string both = Attribute(fields[0], 'r');
        if (!IsNonce(both) || both.Length <= _nonce.Length || !both.StartsWith(_nonce, StringComparison.Ordinal))
        {
            throw ScramFailure.Refused("the server's nonce does not extend the client's");
        }

        byte[] salt = Base64(Attribute(fields[1], 's'), "the salt");
        bool counted = uint.TryParse(Attribute(fields[2], 'i'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out uint iterations);
        return counted && iterations > 0
            ? (text, both, salt, iterations)
            : throw ScramFailure.Refused("the iterations are no positive number");
    }

    /// <summary>client-final, answering the server's first by <paramref name="keys"/>: the server's signature then expected.</summary>
    /// <param name="first">What <see cref="Read"/> gave.</param>
    /// <param name="keys">The keys its salt and iterations derive.</param>
    /// <returns>The message.</returns>
    internal byte[] Prove((string Message, string Both, byte[] Salt, uint Iterations) first, ScramKeys keys)
    {
        byte[] input = [.. Encoding.UTF8.GetBytes(_header), .. _bound];
        string withoutProof = $"c={Convert.ToBase64String(input)},r={first.Both}";
        byte[] auth = Encoding.UTF8.GetBytes($"{_bare},{first.Message},{withoutProof}");
        byte[] signature = HMACSHA256.HashData(SHA256.HashData(keys.Client), auth);
        byte[] proof = new byte[ScramKeys.KeyBytes];
        for (int i = 0; i < proof.Length; i++)
        {
            proof[i] = (byte)(keys.Client[i] ^ signature[i]);
        }

        _expected = HMACSHA256.HashData(keys.Server, auth);

        // Proved: a server-first after it is out of turn.
        _nonce = null;
        return Encoding.UTF8.GetBytes($"{withoutProof},p={Convert.ToBase64String(proof)}");
    }

    /// <summary>Checks server-final: the server's signature, proving it knew the user's verifier.</summary>
    /// <param name="message">The server's final message.</param>
    /// <exception cref="TurborelException">SQLSTATE 28000 if it is out of turn, an error, or not the signature expected.</exception>
    internal void Verify(ReadOnlySpan<byte> message)
    {
        if (_expected is null)
        {
            throw ScramFailure.Refused("the server's final message came out of turn");
        }

        string text = Text(message);
        if (text.StartsWith("e=", StringComparison.Ordinal))
        {
            throw ScramFailure.Refused("the server refused: " + text[2..]);
        }

        byte[] signature = Base64(Attribute(text, 'v'), "the server's signature");
        Verified = CryptographicOperations.FixedTimeEquals(signature, _expected)
            ? true
            : throw ScramFailure.Refused("the server did not prove it knows the user");
    }

    private static string Text(ReadOnlySpan<byte> message) =>
        System.Text.Unicode.Utf8.IsValid(message) ? Encoding.UTF8.GetString(message) : throw ScramFailure.Refused("a message is not UTF-8");

    private static string Attribute(string field, char name) =>
        field.Length > 1 && field[0] == name && field[1] == '='
            ? field[2..]
            : throw ScramFailure.Refused($"the attribute {name} was expected, not {field}");

    private static bool IsNonce(string nonce) =>
        nonce.Length > 0 && nonce.All(c => c is >= FirstPrintable and <= LastPrintable and not ',');

    // Base 64 in its canonical form alone, no white space and no spare bit
    // set [RFC 4648 §3.5], so one text alone is a value's, as
    // turborel-protocol decodes it.
    private static byte[] Base64(string text, string what)
    {
        byte[] bytes = new byte[text.Length];
        return Convert.TryFromBase64String(text, bytes, out int written) && Convert.ToBase64String(bytes, 0, written) == text
            ? bytes[..written]
            : throw ScramFailure.Refused(what + " is not base 64");
    }
}
