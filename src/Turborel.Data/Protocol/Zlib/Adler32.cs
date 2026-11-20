// Adler-32, the checksum a zlib stream ends with (RFC 1950 §8.2): two sums
// modulo 65521, the first of the bytes plus one, the second of the
// first's running values.

using System;

namespace Turborel.Data.Protocol.Zlib;

/// <summary>Computes Adler-32.</summary>
internal static class Adler32
{
    /// <summary>RFC 1950's modulus, the largest prime below 2^16.</summary>
    private const uint Modulus = 65_521;

    /// <summary>
    /// The most bytes summed before reducing: the largest n for which
    /// 255n(n+1)/2 + (n+1)(65520) stays below 2^32 (RFC 1950 §9, zlib's NMAX).
    /// </summary>
    private const int Nmax = 5552;

    /// <summary>The bits of the first sum, below the second.</summary>
    private const int Shift = 16;

    /// <summary>The Adler-32 of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The checksum.</returns>
    internal static uint Of(ReadOnlySpan<byte> data)
    {
        uint a = 1;
        uint b = 0;
        while (!data.IsEmpty)
        {
            ReadOnlySpan<byte> chunk = data[..Math.Min(Nmax, data.Length)];
            foreach (byte octet in chunk)
            {
                a += octet;
                b += a;
            }

            a %= Modulus;
            b %= Modulus;
            data = data[chunk.Length..];
        }

        return (b << Shift) | a;
    }
}
