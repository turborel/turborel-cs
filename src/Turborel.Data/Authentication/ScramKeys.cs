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
    internal static ScramKeys Of(string password, byte[] salt, uint iterations)
    {
        byte[] secret = Encoding.UTF8.GetBytes(password);
        byte[] salted = iterations <= int.MaxValue
            ? Rfc2898DeriveBytes.Pbkdf2(secret, salt, (int)iterations, HashAlgorithmName.SHA256, KeyBytes)
            : Salted(secret, salt, iterations);
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

    // PBKDF2 of HMAC-SHA-256, one block of it, of more iterations than
    // .NET's own takes [RFC 8018 §5.2]: Hi of RFC 5802 §2.2.
    internal static byte[] Salted(byte[] secret, byte[] salt, uint iterations)
    {
        byte[] block = [.. salt, 0, 0, 0, 1];
        byte[] u = HMACSHA256.HashData(secret, block);
        byte[] result = [.. u];
        for (uint i = 1; i < iterations; i++)
        {
            u = HMACSHA256.HashData(secret, u);
            for (int b = 0; b < result.Length; b++)
            {
                result[b] ^= u[b];
            }
        }

        return result;
    }
}
