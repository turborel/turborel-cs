// What a password derives under a salt and iterations, ClientKey and
// ServerKey [RFC 5802 §3], never the password nor SaltedPassword, as
// turborel's protocol/src/scram/client.rs derives them.

using System.Security.Cryptography;
using System.Text;

namespace Turborel.Data.Authentication;

/// <summary>ClientKey and ServerKey of a password.</summary>
internal sealed class ScramKeys
{
    /// <summary>The bytes of a key, a proof or a signature: SHA-256's.</summary>
    internal const int KeyBytes = 32;

    private static readonly byte[] ClientKeyText = Encoding.ASCII.GetBytes("Client Key");
    private static readonly byte[] ServerKeyText = Encoding.ASCII.GetBytes("Server Key");

    private ScramKeys(byte[] client, byte[] server)
    {
        Client = client;
        Server = server;
    }

    /// <summary>Gets ClientKey.</summary>
    internal byte[] Client { get; }

    /// <summary>Gets ServerKey.</summary>
    internal byte[] Server { get; }

    /// <summary>The keys <paramref name="password"/> derives: the slow step, PBKDF2's iterations.</summary>
    /// <param name="password">The password, taken as its UTF-8, without SASLprep, as turborel takes it.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="iterations">The iterations.</param>
    /// <returns>The keys.</returns>
    internal static ScramKeys Of(string password, byte[] salt, int iterations)
    {
        byte[] salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
        try
        {
            return new ScramKeys(HMACSHA256.HashData(salted, ClientKeyText), HMACSHA256.HashData(salted, ServerKeyText));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salted);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => "ScramKeys(..)";
}
