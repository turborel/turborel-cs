// Certificates read from PEM files [CLIENT Connecting]: a file of
// authorities, the client's chain, and the files SSL_CERT_FILE and
// SSL_CERT_DIR name, as the platform's authorities in place of its store.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Turborel.Data.Protocol;

namespace Turborel.Data.Tls;

/// <summary>Reads certificates from PEM files.</summary>
internal static class Pem
{
    /// <summary>The certificates of a PEM file, one at least.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its certificates.</returns>
    /// <exception cref="TurborelException">A connection failure if it cannot be read or holds none.</exception>
    internal static X509Certificate2Collection Certificates(string path)
    {
        var certificates = new X509Certificate2Collection();
        try
        {
            certificates.ImportFromPemFile(path);
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new TurborelException(ConnectionFailure.SqlCode, ConnectionFailure.SqlState, $"[DESIGN] cannot read {path}: {e.Message}", e);
        }

        return certificates.Count > 0 ? certificates : throw ConnectionFailure.Of($"[DESIGN] {path} holds no certificate");
    }

    /// <summary>The certificates of the files the variables name: a file, and each file of a directory.</summary>
    /// <param name="fileVariable">The variable naming a file.</param>
    /// <param name="directoryVariable">The variable naming a directory.</param>
    /// <returns>Their certificates; none if neither variable is set.</returns>
    internal static X509Certificate2Collection FromVariables(string fileVariable, string directoryVariable)
    {
        var certificates = new X509Certificate2Collection();
        if (Environment.GetEnvironmentVariable(fileVariable) is { Length: > 0 } file)
        {
            certificates.AddRange(Certificates(file));
        }

        if (Environment.GetEnvironmentVariable(directoryVariable) is { Length: > 0 } directory && Directory.Exists(directory))
        {
            foreach (string each in Directory.EnumerateFiles(directory))
            {
                var some = new X509Certificate2Collection();
                try
                {
                    some.ImportFromPemFile(each);
                }
                catch (CryptographicException)
                {
                    // A file of the directory that holds no certificate is passed over.
                    continue;
                }

                certificates.AddRange(some);
            }
        }

        return certificates;
    }
}
