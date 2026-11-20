// The channel binding tls-server-end-point (RFC 5929 §4): the hash of the
// server's certificate, by the hash its signature algorithm names, as
// turborel's protocol/src/scram/binding.rs computes it: SHA-256 for MD5
// and SHA-1; none for an algorithm of no hash or of two.

using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Turborel.Data.Tls;

/// <summary>Computes the channel binding tls-server-end-point.</summary>
internal static class EndPoint
{
    /// <summary>id-RSASSA-PSS [RFC 4055 §3.1].</summary>
    private const string RsaPss = "1.2.840.113549.1.1.10";

    /// <summary>id-mgf1 [RFC 4055 §2.2].</summary>
    private const string Mgf1 = "1.2.840.113549.1.1.8";

    /// <summary>id-sha1, RSASSA-PSS's hash and mask hash unless named [RFC 4055 §3.1].</summary>
    private const string Sha1 = "1.3.14.3.2.26";

    /// <summary>The tag of RSASSA-PSS-params' hashAlgorithm, [0] explicit.</summary>
    private static readonly Asn1Tag PssHash = new(TagClass.ContextSpecific, 0, isConstructed: true);

    /// <summary>The tag of RSASSA-PSS-params' maskGenAlgorithm, [1] explicit.</summary>
    private static readonly Asn1Tag PssMask = new(TagClass.ContextSpecific, 1, isConstructed: true);

    /// <summary>The signature algorithms of one hash, and the hash binding a certificate they sign.</summary>
    private static readonly Dictionary<string, HashAlgorithmName> Signatures = new(StringComparer.Ordinal)
    {
        ["1.2.840.113549.1.1.4"] = HashAlgorithmName.SHA256,
        ["1.2.840.113549.1.1.5"] = HashAlgorithmName.SHA256,
        ["1.2.840.113549.1.1.11"] = HashAlgorithmName.SHA256,
        ["1.2.840.113549.1.1.12"] = HashAlgorithmName.SHA384,
        ["1.2.840.113549.1.1.13"] = HashAlgorithmName.SHA512,
        ["1.2.840.10045.4.1"] = HashAlgorithmName.SHA256,
        ["1.2.840.10045.4.3.2"] = HashAlgorithmName.SHA256,
        ["1.2.840.10045.4.3.3"] = HashAlgorithmName.SHA384,
        ["1.2.840.10045.4.3.4"] = HashAlgorithmName.SHA512,
    };

    /// <summary>The hashes RSASSA-PSS names, and the hash binding a certificate it signs with one.</summary>
    private static readonly Dictionary<string, HashAlgorithmName> PssHashes = new(StringComparer.Ordinal)
    {
        [Sha1] = HashAlgorithmName.SHA256,
        ["2.16.840.1.101.3.4.2.1"] = HashAlgorithmName.SHA256,
        ["2.16.840.1.101.3.4.2.2"] = HashAlgorithmName.SHA384,
        ["2.16.840.1.101.3.4.2.3"] = HashAlgorithmName.SHA512,
    };

    /// <summary>The binding of the server whose certificate, as DER, is <paramref name="certificate"/>.</summary>
    /// <param name="certificate">The certificate's DER.</param>
    /// <returns>Its hash; null if its signature algorithm defines none, or it is no certificate.</returns>
    internal static byte[]? Of(ReadOnlySpan<byte> certificate)
    {
        try
        {
            var outer = new AsnReader(certificate.ToArray(), AsnEncodingRules.DER);
            AsnReader body = outer.ReadSequence();
            _ = body.ReadSequence();
            AsnReader algorithm = body.ReadSequence();
            string oid = algorithm.ReadObjectIdentifier();
            HashAlgorithmName? hash = oid == RsaPss ? Pss(algorithm)
                : Signatures.TryGetValue(oid, out HashAlgorithmName named) ? named
                : null;
            return hash is { } name ? Hash(name, certificate) : null;
        }
        catch (AsnContentException)
        {
            // Not a certificate: it binds nothing, as RFC 5929 §4.1 defines none.
            return null;
        }
    }

    private static HashAlgorithmName? Pss(AsnReader algorithm)
    {
        AsnReader fields = algorithm.ReadSequence();
        string hash = Sha1;
        if (fields.HasData && fields.PeekTag().HasSameClassAndValue(PssHash))
        {
            hash = fields.ReadSequence(PssHash).ReadSequence().ReadObjectIdentifier();
        }

        string mask = Sha1;
        if (fields.HasData && fields.PeekTag().HasSameClassAndValue(PssMask))
        {
            AsnReader generator = fields.ReadSequence(PssMask).ReadSequence();
            if (generator.ReadObjectIdentifier() != Mgf1)
            {
                return null;
            }

            mask = generator.ReadSequence().ReadObjectIdentifier();
        }

        return hash == mask && PssHashes.TryGetValue(hash, out HashAlgorithmName binding) ? binding : null;
    }

    private static byte[] Hash(HashAlgorithmName name, ReadOnlySpan<byte> data)
    {
        return name == HashAlgorithmName.SHA384 ? SHA384.HashData(data)
            : name == HashAlgorithmName.SHA512 ? SHA512.HashData(data)
            : SHA256.HashData(data);
    }
}
