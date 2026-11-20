// The channel binding tls-server-end-point, as turborel's binding.rs
// tests it: the signature's hash chooses the binding's, RSASSA-PSS of one
// hash binds by it, and no binding of an algorithm of none or two
// [PROTOCOL Mechanisms] [RFC 5929 §4.1].

using System;
using System.Security.Cryptography;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tls;
using Xunit;

namespace Turborel.Data.Tests.Tls;

/// <summary>Tests of <see cref="EndPoint"/>.</summary>
public sealed class EndPointTests
{
    /// <summary>ecdsa-with-SHA256's DER.</summary>
    internal static readonly byte[] Sha256Ecdsa = [0x2a, 0x86, 0x48, 0xce, 0x3d, 0x04, 0x03, 0x02];

    private static readonly byte[] RsaPss = [0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x0a];
    private static readonly byte[] Mgf1 = [0x2a, 0x86, 0x48, 0x86, 0xf7, 0x0d, 0x01, 0x01, 0x08];
    private static readonly byte[] Sha1 = [0x2b, 0x0e, 0x03, 0x02, 0x1a];
    private static readonly byte[] Sha256Hash = [0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01];
    private static readonly byte[] Sha384Hash = [0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x02];
    private static readonly byte[] Sha512Hash = [0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x03];

    /// <summary>The signature algorithms of one hash, and the hash binding the certificates they sign.</summary>
    /// <returns>Each algorithm's DER and the binding's length.</returns>
    public static TheoryData<string, int> Signatures() => new()
    {
        { "2a864886f70d010104", 32 },
        { "2a864886f70d010105", 32 },
        { "2a8648ce3d0401", 32 },
        { "2a864886f70d01010b", 32 },
        { "2a8648ce3d040302", 32 },
        { "2a864886f70d01010c", 48 },
        { "2a8648ce3d040303", 48 },
        { "2a864886f70d01010d", 64 },
        { "2a8648ce3d040304", 64 },
    };

    /// <summary>A certificate signed with MD5 or SHA-1 is bound by SHA-256 of its whole DER; with SHA-2, by that hash.</summary>
    /// <param name="oid">The signature algorithm's DER, in hexadecimal.</param>
    /// <param name="length">The binding's length in bytes.</param>
    [Theory]
    [Verifies("[PROTOCOL Mechanisms] [RFC 5929 §4.1]")]
    [MemberData(nameof(Signatures))]
    public void SignatureHashChoosesTheBinding(string oid, int length)
    {
        byte[] der = Certificate(Algorithm(Convert.FromHexString(oid), [0x05, 0]));

        byte[]? binding = EndPoint.Of(der);
        Assert.Equal(length, binding?.Length);
        Assert.Equal(length == 32 ? SHA256.HashData(der) : length == 48 ? SHA384.HashData(der) : SHA512.HashData(der), binding);
    }

    /// <summary>RSASSA-PSS is bound by its hash when its MGF1 is of the same; of two hashes, or a mask not MGF1, it has none.</summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms] [RFC 5929 §4.1]")]
    public void RsaPssIsBoundByItsOneHash()
    {
        byte[] same = Certificate(Pss(Sha384Hash, Sha384Hash));
        byte[] defaults = Certificate(Pss(null, null));

        Assert.Equal(SHA384.HashData(same), EndPoint.Of(same));
        Assert.Equal(SHA256.HashData(defaults), EndPoint.Of(defaults));
        Assert.NotNull(EndPoint.Of(Certificate(Pss(Sha1, null))));
        Assert.Null(EndPoint.Of(Certificate(Pss(Sha256Hash, null))));
        Assert.Null(EndPoint.Of(Certificate(Pss(Sha512Hash, Sha256Hash))));
        Assert.Null(EndPoint.Of(Certificate(Pss(Mgf1, Mgf1))));
        Assert.Null(EndPoint.Of(Certificate(Algorithm(RsaPss, []))));
    }

    /// <summary>An algorithm of no hash, as Ed25519, has no binding; nor has DER not of a certificate, cut anywhere.</summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms] [RFC 5929 §4.1]")]
    public void NoBindingWithoutOneKnownHash()
    {
        byte[] whole = Certificate(Algorithm(Sha256Ecdsa, []));

        Assert.Null(EndPoint.Of(Certificate(Algorithm([0x2b, 0x65, 0x70], []))));
        Assert.NotNull(EndPoint.Of(whole));
        for (int cut = 0; cut < whole.Length; cut++)
        {
            Assert.Null(EndPoint.Of(whole.AsSpan(0, cut)));
        }
    }

    /// <summary>A certificate's DER, signed by <paramref name="signature"/>, an AlgorithmIdentifier.</summary>
    /// <param name="signature">The signature algorithm's DER.</param>
    /// <returns>The DER.</returns>
    internal static byte[] Certificate(byte[] signature) =>
        Tlv(0x30, [.. Tlv(0x30, [0x02, 1, 7]), .. signature, .. Tlv(0x03, [0])]);

    /// <summary>An AlgorithmIdentifier of <paramref name="oid"/> and its parameters.</summary>
    /// <param name="oid">The object identifier's body.</param>
    /// <param name="parameters">The parameters' DER.</param>
    /// <returns>The DER.</returns>
    internal static byte[] Algorithm(byte[] oid, byte[] parameters) => Tlv(0x30, [.. Tlv(0x06, oid), .. parameters]);

    private static byte[] Pss(byte[]? hash, byte[]? mask)
    {
        byte[] hashPart = hash is null ? [] : Tlv(0xa0, Algorithm(hash, [0x05, 0]));
        byte[] maskPart = mask is null ? [] : Tlv(0xa1, Algorithm(Mgf1, Algorithm(mask, [0x05, 0])));
        return Algorithm(RsaPss, Tlv(0x30, [.. hashPart, .. maskPart]));
    }

    private static byte[] Tlv(byte tag, byte[] body) => [tag, (byte)body.Length, .. body];
}
