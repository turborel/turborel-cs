// An SQL/JSON item written as a document of the stored form, as turborel
// writes one (its json/binary/write.rs): each node after its tag, an exact
// number in the narrowest of its three forms, an array's and an object's
// offsets reserved and filled once their elements or members are written,
// and an object's directory sorted by key octets, members of one key in
// the order written.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Json;

/// <summary>Writes SQL/JSON items as documents of the stored form.</summary>
internal static class JsonBinaryWriter
{
    /// <summary>Writes the document of <paramref name="item"/>.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The document's octets.</returns>
    /// <exception cref="TurborelException">
    /// SQLCODE -900 if arrays and objects nest deeper than turborel takes,
    /// or the document would be longer than 256 MiB.
    /// </exception>
    internal static byte[] Encode(JsonItem item)
    {
        var w = new WireWriter();
        w.WriteU8(JsonBinary.FormatVersion);
        Node(w, item, 0);
        return w.Length <= JsonBinary.MaxDocumentOctets
            ? w.ToArray()
            : throw JsonErrors.Beyond("its stored form is longer than 256 MiB");
    }

    private static void Node(WireWriter w, JsonItem item, int depth)
    {
        if (depth > JsonBinary.MaxNesting)
        {
            throw JsonErrors.Beyond("arrays and objects nest too deep");
        }

        switch (item)
        {
            case JsonItem.BooleanItem b:
                w.WriteU8(b.Value ? JsonBinary.True : JsonBinary.False);
                break;
            case JsonItem.ExactItem e:
                Exact(w, e.Value);
                break;
            case JsonItem.DecFloatItem f:
                w.WriteU8(JsonBinary.DecFloat);
                w.WriteI128(f.Value.Coefficient);
                w.WriteI32(f.Value.Exponent);
                break;
            case JsonItem.ApproximateItem a:
                w.WriteU8(JsonBinary.Approximate);
                w.WriteU64(BitConverter.DoubleToUInt64Bits(a.Value));
                break;
            case JsonItem.StringItem s:
                w.WriteU8(JsonBinary.String);
                w.WriteString(s.Value);
                break;
            case JsonItem.DatetimeItem d:
                Datetime(w, d.Value);
                break;
            case JsonItem.ArrayItem array:
                Elements(w, array.Elements, depth + 1);
                break;
            case JsonItem.ObjectItem o:
                Members(w, o.Members, depth + 1);
                break;
            default:
                w.WriteU8(JsonBinary.Null);
                break;
        }
    }

    private static void Exact(WireWriter w, TurborelDecimal d)
    {
        bool narrow = d.Units >= long.MinValue && d.Units <= long.MaxValue;
        if (narrow && d.Scale == 0)
        {
            w.WriteU8(JsonBinary.Integer);
            w.WriteI64((long)d.Units);
        }
        else if (narrow && d.Scale <= byte.MaxValue)
        {
            w.WriteU8(JsonBinary.Decimal);
            w.WriteI64((long)d.Units);
            w.WriteU8((byte)d.Scale);
        }
        else
        {
            w.WriteU8(JsonBinary.Wide);
            w.WriteI128(d.Units);
            w.WriteU32(d.Scale);
        }
    }

    private static void Datetime(WireWriter w, TurborelDatetime d)
    {
        w.WriteU8(JsonBinary.Datetime);
        w.WriteU8((byte)d.Moment);
        w.WriteI64(d.UtcMicroseconds);
        w.WriteI16(d.ZoneMinutes);
        w.WriteFlag(d.Zoned);
        w.WriteU8(d.Precision);
    }

    // A container's tag, count and size, its offsets reserved: where its
    // size is, and where its first offset is.
    private static (int Size, int Offsets) Open(WireWriter w, byte tag, int count)
    {
        w.WriteU8(tag);
        w.WriteCount(count);
        int size = w.Length;
        w.WriteU32(0);
        int offsets = w.Length;
        for (int i = 0; i < count; i++)
        {
            w.WriteU32(0);
        }

        return (size, offsets);
    }

    private static void Elements(WireWriter w, IReadOnlyList<JsonItem> elements, int depth)
    {
        (int size, int offsets) = Open(w, JsonBinary.Array, elements.Count);
        int body = w.Length;
        for (int i = 0; i < elements.Count; i++)
        {
            w.PatchU32(offsets + (i * JsonBinary.Word), (uint)(w.Length - body));
            Node(w, elements[i], depth);
        }

        w.PatchU32(size, (uint)(w.Length - body));
    }

    private static void Members(WireWriter w, IReadOnlyList<JsonMember> members, int depth)
    {
        (int size, int offsets) = Open(w, JsonBinary.Object, members.Count);
        int body = w.Length;
        var directory = new List<(byte[] Key, uint Offset)>(members.Count);
        foreach (JsonMember member in members)
        {
            directory.Add((Encoding.UTF8.GetBytes(member.Key), (uint)(w.Length - body)));
            w.WriteString(member.Key);
            Node(w, member.Value, depth);
        }

        // OrderBy is stable, so the members of one key keep the order given.
        int at = offsets;
        foreach ((byte[] _, uint offset) in directory.OrderBy(entry => entry.Key, KeyOrder.Instance))
        {
            w.PatchU32(at, offset);
            at += JsonBinary.Word;
        }

        w.PatchU32(size, (uint)(w.Length - body));
    }

    // Keys compared octet by octet, which is Unicode code point order.
    private sealed class KeyOrder : IComparer<byte[]>
    {
        internal static readonly KeyOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
