// Values written and read: a tag, then its body [PROTOCOL Values]. A
// value's tag says how it is written, not always its column's type's tag:
// a CHARACTER VARYING value is written with tag 1.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Unicode;
using Turborel.Data.Json;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>Writes and reads values, each as the .NET type the driver gives it as.</summary>
internal static class ValueCodec
{
    /// <summary>The greatest cardinality of a multiset, as turborel bounds it.</summary>
    internal const uint MaxMultisetCardinality = DataTypeCodec.MaxArrayCardinality;

    /// <summary>The octets of a UUID.</summary>
    internal const int UuidOctets = 16;

    /// <summary>The zoned byte of a datetime WITH TIME ZONE.</summary>
    private const byte ZonedByte = 1;

    /// <summary>The SQLCODE of a value the driver cannot send, a data exception.</summary>
    private const int UncarriedSqlCode = -301;

    /// <summary>The SQLSTATE of a value the driver cannot send: a data exception, as the drivers' own failures give it.</summary>
    private const string UncarriedSqlState = "22000";

    /// <summary>Reads a value.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The value; null for the null value.</returns>
    internal static object? Read(WireReader r)
    {
        byte tag = r.ReadU8();
        return tag switch
        {
            Tags.Null => null,
            Tags.Character or Tags.Varying => r.ReadString(),
            Tags.Exact => new TurborelDecimal(r.ReadI128(), r.ReadU32()),
            Tags.Approximate => BitConverter.UInt64BitsToDouble(r.ReadU64()),
            Tags.Plugin => new TurborelPluginValue(r.ReadString(), r.ReadString(), r.ReadBytes().ToArray()),
            Tags.Binary or Tags.BinaryVarying => r.ReadBytes().ToArray(),
            Tags.Date => ReadDatetime(r, DatetimeMoment.Date),
            Tags.Time or Tags.TimeZoned => ReadDatetime(r, DatetimeMoment.Time),
            Tags.Timestamp or Tags.TimestampZoned => ReadDatetime(r, DatetimeMoment.Timestamp),
            Tags.Interval => new TurborelInterval(DataTypeCodec.ReadQualifier(r), r.ReadI128()),
            Tags.Boolean => ReadTruth(r),
            Tags.DecFloat => ReadDecFloat(r),
            Tags.Array => new TurborelArray(r.Nested(ReadList)),
            Tags.Row => new TurborelRow(r.Nested(ReadList)),
            Tags.Multiset => ReadMultiset(r),
            Tags.LargeObjectWhole => ReadWhole(r),
            Tags.LargeObjectLocator => TurborelLargeObject.ByLocator(ReadKind(r), r.ReadU32(), (r.ReadU64(), r.ReadU64())),
            Tags.Structured => r.Nested(ReadStructured),
            Tags.Reference => new TurborelReference(r.ReadBytes().ToArray()),
            Tags.Json => ReadJson(r),
            Tags.Xml => new TurborelXml(r.ReadString()),
            Tags.Uuid => ReadUuid(r),
            Tags.Clob or Tags.Blob => throw r.Corrupt(Sections.Values, "a large object's stored form is no value the protocol sends"),
            _ => throw r.Malformed(Sections.Values, "no value has this tag"),
        };
    }

    /// <summary>Reads a row: a list of values [PROTOCOL Composite fields].</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The values.</returns>
    internal static IReadOnlyList<object?> ReadList(WireReader r)
    {
        uint count = r.ReadCount();
        var values = new List<object?>();
        for (uint i = 0; i < count; i++)
        {
            values.Add(Read(r));
        }

        return values;
    }

    /// <summary>Writes a row: its count, then each value [PROTOCOL Composite fields].</summary>
    /// <param name="w">The writer.</param>
    /// <param name="values">The values.</param>
    internal static void WriteList(WireWriter w, IReadOnlyList<object?> values)
    {
        w.WriteCount(values.Count);
        foreach (object? value in values)
        {
            Write(w, value);
        }
    }

    /// <summary>Writes a value.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="value">The value, of a type <see cref="Read"/> gives, or null.</param>
    /// <exception cref="TurborelException">SQLSTATE 22000 if it is of no type the protocol carries.</exception>
    internal static void Write(WireWriter w, object? value)
    {
        switch (value)
        {
            case null:
            case DBNull:
                w.WriteU8(Tags.Null);
                break;
            case string s:
                w.WriteU8(Tags.Character);
                w.WriteString(s);
                break;
            case TurborelDecimal d:
                w.WriteU8(Tags.Exact);
                w.WriteI128(d.Units);
                w.WriteU32(d.Scale);
                break;
            case double x:
                w.WriteU8(Tags.Approximate);
                w.WriteU64(BitConverter.DoubleToUInt64Bits(x));
                break;
            case bool truth:
                w.WriteU8(Tags.Boolean);
                w.WriteFlag(truth);
                break;
            case byte[] bytes:
                w.WriteU8(Tags.Binary);
                w.WriteBytes(bytes);
                break;
            default:
                WriteKept(w, value);
                break;
        }
    }

    private static void WriteKept(WireWriter w, object value)
    {
        switch (value)
        {
            case TurborelDatetime d:
                WriteDatetime(w, d);
                break;
            case TurborelInterval i:
                w.WriteU8(Tags.Interval);
                DataTypeCodec.WriteQualifier(w, i.Qualifier);
                w.WriteI128(i.Units);
                break;
            case TurborelDecFloat f:
                w.WriteU8(Tags.DecFloat);
                w.WriteI128(f.Coefficient);
                w.WriteI32(f.Exponent);
                break;
            case Guid g:
                WriteUuid(w, g);
                break;
            case TurborelLargeObject l:
                WriteLargeObject(w, l);
                break;
            default:
                WriteConstructed(w, value);
                break;
        }
    }

    private static void WriteConstructed(WireWriter w, object value)
    {
        switch (value)
        {
            case TurborelArray a:
                WriteTaggedList(w, Tags.Array, a.Elements);
                break;
            case TurborelRow row:
                WriteTaggedList(w, Tags.Row, row.Fields);
                break;
            case TurborelMultiset m:
                WriteTaggedList(w, Tags.Multiset, m.Elements);
                break;
            case TurborelStructuredValue s:
                w.WriteU8(Tags.Structured);
                w.WriteString(s.Schema);
                w.WriteString(s.Name);
                WriteList(w, s.Attributes);
                break;
            default:
                WriteOpaque(w, value);
                break;
        }
    }

    private static void WriteOpaque(WireWriter w, object value)
    {
        switch (value)
        {
            case TurborelPluginValue p:
                w.WriteU8(Tags.Plugin);
                w.WriteString(p.TypeName);
                w.WriteString(p.Text);
                w.WriteBytes(p.Bytes.Span);
                break;
            case TurborelReference reference:
                w.WriteU8(Tags.Reference);
                w.WriteBytes(reference.Bytes.Span);
                break;
            case TurborelJson json:
                w.WriteU8(Tags.Json);
                w.WriteBytes(json.Octets.Span);
                break;
            case TurborelXml xml:
                w.WriteU8(Tags.Xml);
                w.WriteString(xml.Text);
                break;
            default:
                throw new TurborelException(
                    UncarriedSqlCode,
                    UncarriedSqlState,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Sections.Values} a value of {value.GetType().Name} is of no type the protocol carries"));
        }
    }

    private static void WriteTaggedList(WireWriter w, byte tag, IReadOnlyList<object?> values)
    {
        w.WriteU8(tag);
        WriteList(w, values);
    }

    private static TurborelDatetime ReadDatetime(WireReader r, DatetimeMoment moment)
    {
        bool zoned = r.ReadU8() == ZonedByte;
        byte precision = r.ReadU8();
        uint zone = r.ReadU32();
        if (zone > ushort.MaxValue)
        {
            throw r.Malformed(Sections.Values, "a time zone displacement is out of range");
        }

        return new TurborelDatetime(moment, r.ReadI64(), unchecked((short)(ushort)zone), zoned, precision);
    }

    private static void WriteDatetime(WireWriter w, TurborelDatetime d)
    {
        w.WriteU8(d.Moment switch
        {
            DatetimeMoment.Date => Tags.Date,
            DatetimeMoment.Time => Tags.Time,
            _ => Tags.Timestamp,
        });
        w.WriteFlag(d.Zoned);
        w.WriteU8(d.Precision);
        // The displacement's i16 bits, in the low half of a u32.
        w.WriteU32(unchecked((ushort)d.ZoneMinutes));
        w.WriteI64(d.UtcMicroseconds);
    }

    private static bool ReadTruth(WireReader r) => r.ReadU8() switch
    {
        0 => false,
        1 => true,
        _ => throw r.Corrupt(Sections.Values, "a truth value is neither 0 nor 1"),
    };

    private static TurborelDecFloat ReadDecFloat(WireReader r)
    {
        Int128 coefficient = r.ReadI128();
        int exponent = r.ReadI32();
        return TurborelDecFloat.TryCreate(coefficient, exponent, out TurborelDecFloat value)
            ? value
            : throw r.Malformed(Sections.Values, "a DECFLOAT value is not in its one form");
    }

    private static TurborelMultiset ReadMultiset(WireReader r)
    {
        IReadOnlyList<object?> elements = r.Nested(ReadList);
        return elements.Count <= MaxMultisetCardinality
            ? new TurborelMultiset(elements)
            : throw r.Malformed(Sections.Values, "a multiset has more elements than its maximum cardinality");
    }

    private static LargeObjectKind ReadKind(WireReader r)
    {
        byte kind = r.ReadU8();
        return kind <= (byte)LargeObjectKind.Binary
            ? (LargeObjectKind)kind
            : throw r.Malformed(Sections.Values, "not a kind of large object");
    }

    private static TurborelLargeObject ReadWhole(WireReader r)
    {
        LargeObjectKind kind = ReadKind(r);
        ReadOnlyMemory<byte> octets = r.ReadBytes();
        if (kind == LargeObjectKind.Character && !IsUtf8(octets.Span))
        {
            throw r.Malformed(Sections.Values, "a character large object is not UTF-8");
        }

        return TurborelLargeObject.Whole(kind, octets.ToArray());
    }

    private static void WriteLargeObject(WireWriter w, TurborelLargeObject l)
    {
        if (l.Data is { } octets)
        {
            w.WriteU8(Tags.LargeObjectWhole);
            w.WriteU8((byte)l.Kind);
            w.WriteBytes(octets.Span);
            return;
        }

        w.WriteU8(Tags.LargeObjectLocator);
        w.WriteU8((byte)l.Kind);
        w.WriteU32(l.Locator);
        w.WriteU64(l.Octets);
        w.WriteU64(l.Characters);
    }

    private static TurborelStructuredValue ReadStructured(WireReader r)
    {
        string schema = r.ReadString();
        string name = r.ReadString();
        return new TurborelStructuredValue(schema, name, ReadList(r));
    }

    private static TurborelJson ReadJson(WireReader r)
    {
        ReadOnlyMemory<byte> octets = r.ReadBytes();
        return JsonBinary.Validate(octets.Span) switch
        {
            null => TurborelJson.Trusted(octets.ToArray()),
            var beyond when beyond is JsonBinary.TooDeep or JsonBinary.TooLong => throw JsonErrors.Beyond(beyond),
            var why => throw r.Corrupt(Sections.Values, "a JSON value's octets are damaged: " + why),
        };
    }

    private static Guid ReadUuid(WireReader r)
    {
        ReadOnlyMemory<byte> octets = r.ReadBytes();
        return octets.Length == UuidOctets
            ? new Guid(octets.Span, bigEndian: true)
            : throw r.Malformed(Sections.Values, "a UUID is not 16 octets");
    }

    private static void WriteUuid(WireWriter w, Guid g)
    {
        Span<byte> octets = stackalloc byte[UuidOctets];
        _ = g.TryWriteBytes(octets, bigEndian: true, out _);
        w.WriteU8(Tags.Uuid);
        w.WriteBytes(octets);
    }

    private static bool IsUtf8(ReadOnlySpan<byte> octets) => Utf8.IsValid(octets);
}
