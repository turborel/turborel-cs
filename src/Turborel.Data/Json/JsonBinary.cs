// A JSON value's stored form, as turborel sends it under value tag 25:
// a version octet, 1, then its root node, each node a tag and its body
// (turborel's RULES.md D89, json/binary.rs). A document received is
// checked whole before it is read, so damaged octets fail, never crash.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;
using Turborel.Data.Types;

namespace Turborel.Data.Json;

/// <summary>Checks and reads JSON documents of the stored form.</summary>
internal static class JsonBinary
{
    /// <summary>The format's version, every document's first octet.</summary>
    internal const byte FormatVersion = 1;

    /// <summary>How deep arrays and objects nest, as turborel bounds them (its json.rs MAX_NESTING).</summary>
    internal const int MaxNesting = 128;

    /// <summary>The most octets a document holds (turborel's MAX_DOCUMENT_OCTETS, 256 MiB).</summary>
    internal const int MaxDocumentOctets = 1 << 28;

    /// <summary>The SQL/JSON null.</summary>
    internal const byte Null = 0;

    /// <summary>False.</summary>
    internal const byte False = 1;

    /// <summary>True.</summary>
    internal const byte True = 2;

    /// <summary>An exact number of scale 0 an <c>i64</c> holds.</summary>
    internal const byte Integer = 3;

    /// <summary>An exact number: units <c>i64</c>, scale <c>u8</c>.</summary>
    internal const byte Decimal = 4;

    /// <summary>Any other exact number: units <c>i128</c>, scale <c>u32</c>.</summary>
    internal const byte Wide = 5;

    /// <summary>A decimal floating-point number: coefficient <c>i128</c>, exponent <c>i32</c>.</summary>
    internal const byte DecFloat = 6;

    /// <summary>An approximate number: its <c>f64</c> bits.</summary>
    internal const byte Approximate = 7;

    /// <summary>A string: its octets, <c>u32</c>, then its UTF-8.</summary>
    internal const byte String = 8;

    /// <summary>A datetime: moment, UTC instant, zone, zoned flag, precision.</summary>
    internal const byte Datetime = 9;

    /// <summary>An array: count, size, an offset of each element, the elements.</summary>
    internal const byte Array = 10;

    /// <summary>An object: count, size, an offset of each member in key order, the members.</summary>
    internal const byte Object = 11;

    /// <summary>The octets of a count, a size, an offset or a string's length.</summary>
    internal const int Word = 4;

    /// <summary>The octets of an <c>i64</c> or an <c>f64</c>.</summary>
    internal const int Long = 8;

    /// <summary>The octets of an <c>i128</c>.</summary>
    internal const int WideWord = 16;

    /// <summary>A container's head after its tag: its count and its size.</summary>
    internal const int ContainerHead = 2 * Word;

    /// <summary>A datetime's body: moment, instant, zone, zoned flag and precision.</summary>
    internal const int DatetimeBody = 1 + Long + sizeof(short) + 1 + 1;

    /// <summary>
    /// Checks <paramref name="document"/> is whole and of the format: each
    /// node within it, every string UTF-8, every offset an element's or a
    /// member's, every object's directory sorted.
    /// </summary>
    /// <param name="document">The octets.</param>
    /// <returns>Null if it is; else what is wrong.</returns>
    internal static string? Validate(ReadOnlySpan<byte> document)
    {
        if (document.Length < 2 || document[0] != FormatVersion)
        {
            return "it does not begin with its format's version";
        }

        if (document.Length > MaxDocumentOctets)
        {
            return "it is longer than 256 MiB";
        }

        var check = new JsonChecker(document[1..]);
        long length = check.Node(0, 0);
        if (length < 0)
        {
            return check.Why;
        }

        return length + 1 == document.Length ? null : "octets follow its root";
    }

    /// <summary>Reads the item a document holds, once <see cref="Validate"/> passed it.</summary>
    /// <param name="document">The octets.</param>
    /// <returns>The item.</returns>
    internal static JsonItem Decode(ReadOnlySpan<byte> document) => Item(document[1..], 0);

    /// <summary>Reads the <c>u32</c> at <paramref name="at"/>.</summary>
    /// <param name="bytes">The octets.</param>
    /// <param name="at">Where it begins.</param>
    /// <returns>The number.</returns>
    internal static uint WordAt(ReadOnlySpan<byte> bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);

    private static JsonItem Item(ReadOnlySpan<byte> body, int at)
    {
        int b = at + 1;
        return body[at] switch
        {
            Null => new JsonItem.NullItem(),
            False => new JsonItem.BooleanItem(false),
            True => new JsonItem.BooleanItem(true),
            Integer => Exact(BinaryPrimitives.ReadInt64LittleEndian(body[b..]), 0),
            Decimal => Exact(BinaryPrimitives.ReadInt64LittleEndian(body[b..]), body[b + Long]),
            Wide => Exact(BinaryPrimitives.ReadInt128LittleEndian(body[b..]), WordAt(body, b + WideWord)),
            DecFloat => new JsonItem.DecFloatItem(DecFloatAt(body, b) ?? TurborelDecFloat.Zero),
            Approximate => new JsonItem.ApproximateItem(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(body[b..]))),
            String => new JsonItem.StringItem(StringAt(body, b)),
            Datetime => new JsonItem.DatetimeItem(DatetimeAt(body, b)),
            Array => Elements(body, b),
            _ => Members(body, b),
        };
    }

    /// <summary>Reads the DECFLOAT whose body begins at <paramref name="at"/>, rounded to its one form.</summary>
    /// <param name="body">The octets.</param>
    /// <param name="at">Where its coefficient begins.</param>
    /// <returns>The value; none if out of range.</returns>
    internal static TurborelDecFloat? DecFloatAt(ReadOnlySpan<byte> body, int at)
    {
        Int128 coefficient = BinaryPrimitives.ReadInt128LittleEndian(body[at..]);
        int exponent = BinaryPrimitives.ReadInt32LittleEndian(body[(at + WideWord)..]);
        return TurborelDecFloat.TryRound((System.Numerics.BigInteger)coefficient, exponent, out TurborelDecFloat value)
            ? value
            : null;
    }

    /// <summary>Reads the string whose length begins at <paramref name="at"/>, once checked.</summary>
    /// <param name="body">The octets.</param>
    /// <param name="at">Where its length begins.</param>
    /// <returns>The string.</returns>
    internal static string StringAt(ReadOnlySpan<byte> body, int at) =>
        Encoding.UTF8.GetString(body.Slice(at + Word, (int)WordAt(body, at)));

    /// <summary>Whether the string whose length begins at <paramref name="at"/> is UTF-8.</summary>
    /// <param name="body">The octets.</param>
    /// <param name="at">Where its length begins, its octets within the body.</param>
    /// <returns>Whether it is.</returns>
    internal static bool IsUtf8At(ReadOnlySpan<byte> body, int at) =>
        Utf8.IsValid(body.Slice(at + Word, (int)WordAt(body, at)));

    private static JsonItem.ExactItem Exact(Int128 units, uint scale) => new(new TurborelDecimal(units, scale));

    private static TurborelDatetime DatetimeAt(ReadOnlySpan<byte> body, int at)
    {
        var moment = (DatetimeMoment)body[at];
        long utc = BinaryPrimitives.ReadInt64LittleEndian(body[(at + 1)..]);
        short zone = BinaryPrimitives.ReadInt16LittleEndian(body[(at + 1 + Long)..]);
        int flags = at + 1 + Long + sizeof(short);
        return new TurborelDatetime(moment, utc, zone, body[flags] == 1, body[flags + 1]);
    }

    private static JsonItem.ArrayItem Elements(ReadOnlySpan<byte> body, int at)
    {
        int count = (int)WordAt(body, at);
        int first = at + ContainerHead + (count * Word);
        var elements = new List<JsonItem>(count);
        for (int i = 0; i < count; i++)
        {
            elements.Add(Item(body, first + (int)WordAt(body, at + ContainerHead + (i * Word))));
        }

        return new JsonItem.ArrayItem(elements);
    }

    private static JsonItem.ObjectItem Members(ReadOnlySpan<byte> body, int at)
    {
        int count = (int)WordAt(body, at);
        int next = at + ContainerHead + (count * Word);
        var members = new List<JsonMember>(count);
        for (int i = 0; i < count; i++)
        {
            string key = StringAt(body, next);
            int value = next + Word + (int)WordAt(body, next);
            members.Add(new JsonMember(key, Item(body, value)));
            next = value + (int)JsonChecker.LengthAt(body, value);
        }

        return new JsonItem.ObjectItem(members);
    }
}
