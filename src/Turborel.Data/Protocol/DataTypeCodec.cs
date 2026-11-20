// Data types written and read: a tag, then what its kind writes after it
// [PROTOCOL Data types]. Columns, parameters and the types inside other
// types are written this way.

using System.Collections.Generic;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>Writes and reads data types.</summary>
internal static class DataTypeCodec
{
    /// <summary>The greatest maximum cardinality of an array type, as turborel bounds it.</summary>
    internal const uint MaxArrayCardinality = 65_535;

    /// <summary>The most parameters a plug-in's type has, as turborel bounds them.</summary>
    internal const int MaxPluginParameters = 2;

    /// <summary>The byte of a reference type's scope flag that names a scope.</summary>
    private const byte Scoped = 1;

    /// <summary>The byte of an XML schema's part that is present.</summary>
    private const byte Present = 1;

    /// <summary>Reads a data type.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The data type.</returns>
    internal static DataType Read(WireReader r)
    {
        byte tag = r.ReadU8();
        return tag switch
        {
            Tags.Character => new DataType.CharacterType(r.ReadU32(), ReadUnits(r), Varying: false),
            Tags.Varying => new DataType.CharacterType(r.ReadU32(), ReadUnits(r), Varying: true),
            Tags.Exact => new DataType.ExactType(r.ReadU32(), r.ReadU32()),
            Tags.Approximate => new DataType.ApproximateType(r.ReadU32()),
            Tags.Plugin => ReadPlugin(r),
            Tags.Binary => new DataType.BinaryType(r.ReadU32(), Varying: false),
            Tags.BinaryVarying => new DataType.BinaryType(r.ReadU32(), Varying: true),
            Tags.Date => new DataType.DatetimeType(DatetimeMoment.Date, Zoned: false, r.ReadU32()),
            Tags.Time => new DataType.DatetimeType(DatetimeMoment.Time, Zoned: false, r.ReadU32()),
            Tags.TimeZoned => new DataType.DatetimeType(DatetimeMoment.Time, Zoned: true, r.ReadU32()),
            Tags.Timestamp => new DataType.DatetimeType(DatetimeMoment.Timestamp, Zoned: false, r.ReadU32()),
            Tags.TimestampZoned => new DataType.DatetimeType(DatetimeMoment.Timestamp, Zoned: true, r.ReadU32()),
            Tags.Interval => new DataType.IntervalType(ReadQualifier(r)),
            Tags.Boolean => new DataType.BooleanType(),
            Tags.DecFloat => new DataType.DecFloatType(r.ReadU32()),
            Tags.Array => r.Nested(ReadArray),
            Tags.Row => r.Nested(ReadRow),
            Tags.Clob => new DataType.LargeObjectType(LargeObjectKind.Character, r.ReadU64(), ReadUnits(r)),
            Tags.Blob => new DataType.LargeObjectType(LargeObjectKind.Binary, r.ReadU64(), ReadUnits(r)),
            Tags.Multiset => r.Nested(nested => new DataType.MultisetType(Read(nested))),
            Tags.Structured => ReadStructured(r),
            Tags.Reference => new DataType.ReferenceType(ReadStructured(r), ReadScope(r)),
            Tags.Json => new DataType.JsonType(),
            Tags.Xml => ReadXml(r),
            Tags.Uuid => new DataType.UuidType(),
            _ => throw r.Malformed(Sections.DataTypes, "no data type has this tag"),
        };
    }

    /// <summary>Reads an interval's qualifier: start and end fields, leading and fractional precisions.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The qualifier.</returns>
    internal static IntervalQualifier ReadQualifier(WireReader r)
    {
        uint start = r.ReadU32();
        uint end = r.ReadU32();
        uint leading = r.ReadU32();
        uint fraction = r.ReadU32();
        if (start > (uint)IntervalField.Second || end > (uint)IntervalField.Second)
        {
            throw r.Malformed(Sections.DataTypes, "an interval's field is out of range");
        }

        return new IntervalQualifier((IntervalField)start, (IntervalField)end, leading, fraction);
    }

    /// <summary>Writes an interval's qualifier.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="qualifier">The qualifier.</param>
    internal static void WriteQualifier(WireWriter w, IntervalQualifier qualifier)
    {
        w.WriteU32((uint)qualifier.Start);
        w.WriteU32((uint)qualifier.End);
        w.WriteU32(qualifier.LeadingPrecision);
        w.WriteU32(qualifier.FractionalPrecision);
    }

    /// <summary>Writes a data type.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="type">The data type.</param>
    internal static void Write(WireWriter w, DataType type)
    {
        switch (type)
        {
            case DataType.CharacterType c:
                w.WriteU8(c.Varying ? Tags.Varying : Tags.Character);
                w.WriteU32(c.Length);
                w.WriteU8((byte)c.Units);
                break;
            case DataType.ExactType e:
                w.WriteU8(Tags.Exact);
                w.WriteU32(e.Precision);
                w.WriteU32(e.Scale);
                break;
            case DataType.ApproximateType a:
                WriteTagged(w, Tags.Approximate, a.Precision);
                break;
            case DataType.BinaryType b:
                WriteTagged(w, b.Varying ? Tags.BinaryVarying : Tags.Binary, b.Length);
                break;
            case DataType.DatetimeType d:
                WriteTagged(w, DatetimeTag(d), d.Precision);
                break;
            case DataType.DecFloatType f:
                WriteTagged(w, Tags.DecFloat, f.Precision);
                break;
            default:
                WriteComposite(w, type);
                break;
        }
    }

    private static void WriteComposite(WireWriter w, DataType type)
    {
        switch (type)
        {
            case DataType.IntervalType i:
                w.WriteU8(Tags.Interval);
                WriteQualifier(w, i.Qualifier);
                break;
            case DataType.ArrayType a:
                WriteTagged(w, Tags.Array, a.MaxCardinality);
                Write(w, a.Element);
                break;
            case DataType.MultisetType m:
                w.WriteU8(Tags.Multiset);
                Write(w, m.Element);
                break;
            case DataType.RowType row:
                WriteRow(w, row);
                break;
            case DataType.LargeObjectType l:
                w.WriteU8(l.Kind == LargeObjectKind.Character ? Tags.Clob : Tags.Blob);
                w.WriteU64(l.Length);
                w.WriteU8((byte)l.Units);
                break;
            default:
                WriteNamed(w, type);
                break;
        }
    }

    private static void WriteNamed(WireWriter w, DataType type)
    {
        switch (type)
        {
            case DataType.PluginType p:
                WritePlugin(w, p);
                break;
            case DataType.StructuredType s:
                w.WriteU8(Tags.Structured);
                WriteNames(w, s);
                break;
            case DataType.ReferenceType reference:
                w.WriteU8(Tags.Reference);
                WriteNames(w, reference.Referenced);
                WriteScope(w, reference.Scope);
                break;
            case DataType.XmlType x:
                WriteXml(w, x);
                break;
            case DataType.JsonType:
                w.WriteU8(Tags.Json);
                break;
            case DataType.UuidType:
                w.WriteU8(Tags.Uuid);
                break;
            default:
                // BooleanType, the one kind no case above takes.
                w.WriteU8(Tags.Boolean);
                break;
        }
    }

    private static void WriteTagged(WireWriter w, byte tag, uint parameter)
    {
        w.WriteU8(tag);
        w.WriteU32(parameter);
    }

    private static byte DatetimeTag(DataType.DatetimeType d) => (d.Moment, d.Zoned) switch
    {
        (DatetimeMoment.Date, _) => Tags.Date,
        (DatetimeMoment.Time, false) => Tags.Time,
        (DatetimeMoment.Time, true) => Tags.TimeZoned,
        (DatetimeMoment.Timestamp, false) => Tags.Timestamp,
        _ => Tags.TimestampZoned,
    };

    private static CharacterUnits ReadUnits(WireReader r)
    {
        byte units = r.ReadU8();
        return units <= (byte)CharacterUnits.Octets
            ? (CharacterUnits)units
            : throw r.Malformed(Sections.DataTypes, "a length's units are neither characters, 0, nor octets, 1");
    }

    private static DataType.PluginType ReadPlugin(WireReader r)
    {
        string name = r.ReadString();
        uint count = r.ReadCount();
        var parameters = new List<uint>();
        for (uint i = 0; i < count; i++)
        {
            parameters.Add(r.ReadU32());
            if (parameters.Count > MaxPluginParameters)
            {
                throw r.Malformed(Sections.DataTypes, "a type has too many parameters");
            }
        }

        return new DataType.PluginType(name, parameters);
    }

    private static void WritePlugin(WireWriter w, DataType.PluginType p)
    {
        w.WriteU8(Tags.Plugin);
        w.WriteString(p.Name);
        w.WriteCount(p.Parameters.Count);
        foreach (uint parameter in p.Parameters)
        {
            w.WriteU32(parameter);
        }
    }

    private static DataType ReadArray(WireReader r)
    {
        uint max = r.ReadU32();
        if (max is 0 or > MaxArrayCardinality)
        {
            throw r.Malformed(Sections.DataTypes, "an array type's maximum cardinality is out of range");
        }

        return new DataType.ArrayType(max, Read(r));
    }

    private static DataType ReadRow(WireReader r)
    {
        uint count = r.ReadCount();
        var fields = new List<RowField>();
        for (uint i = 0; i < count; i++)
        {
            fields.Add(new RowField(r.ReadString(), Read(r)));
        }

        return fields.Count > 0 ? new DataType.RowType(fields) : throw r.Malformed(Sections.DataTypes, "a row type has no fields");
    }

    private static void WriteRow(WireWriter w, DataType.RowType row)
    {
        w.WriteU8(Tags.Row);
        w.WriteCount(row.Fields.Count);
        foreach (RowField field in row.Fields)
        {
            w.WriteString(field.Name);
            Write(w, field.Type);
        }
    }

    private static DataType.StructuredType ReadStructured(WireReader r)
    {
        var name = new SqlName(r.ReadString(), r.ReadString());
        uint count = r.ReadCount();
        var supertypes = new List<SqlName>();
        for (uint i = 0; i < count; i++)
        {
            supertypes.Add(new SqlName(r.ReadString(), r.ReadString()));
        }

        return new DataType.StructuredType(name, supertypes);
    }

    private static void WriteNames(WireWriter w, DataType.StructuredType s)
    {
        WriteName(w, s.Name);
        w.WriteCount(s.Supertypes.Count);
        foreach (SqlName supertype in s.Supertypes)
        {
            WriteName(w, supertype);
        }
    }

    private static void WriteName(WireWriter w, SqlName name)
    {
        w.WriteString(name.Schema);
        w.WriteString(name.Name);
    }

    private static SqlName? ReadScope(WireReader r) => r.ReadU8() switch
    {
        0 => null,
        Scoped => new SqlName(r.ReadString(), r.ReadString()),
        _ => throw r.Malformed(Sections.DataTypes, "a reference type's scope flag is neither 0 nor 1"),
    };

    private static void WriteScope(WireWriter w, SqlName? scope)
    {
        w.WriteFlag(scope is not null);
        if (scope is { } named)
        {
            WriteName(w, named);
        }
    }

    private static DataType.XmlType ReadXml(WireReader r)
    {
        byte primary = r.ReadU8();
        byte secondary = r.ReadU8();
        XmlSchemaName? schema = secondary == (byte)XmlSecondary.XmlSchema ? ReadSchema(r) : null;
        bool known = primary <= (byte)XmlPrimary.Sequence && secondary <= (byte)XmlSecondary.XmlSchema;
        bool sequenceUntyped = primary != (byte)XmlPrimary.Sequence || secondary == (byte)XmlSecondary.Untyped;
        return known && sequenceUntyped
            ? new DataType.XmlType((XmlPrimary)primary, (XmlSecondary)secondary, schema)
            : throw r.Malformed(Sections.DataTypes, "an XML type's modifiers are not one of its kinds");
    }

    private static XmlSchemaName ReadSchema(WireReader r)
    {
        var name = new SqlName(r.ReadString(), r.ReadString());
        string? space = ReadPart(r);
        return new XmlSchemaName(name, space, ReadPart(r));
    }

    private static string? ReadPart(WireReader r) => r.ReadU8() switch
    {
        0 => null,
        Present => r.ReadString(),
        _ => throw r.Malformed(Sections.DataTypes, "an XML type's namespace or element is neither absent, 0, nor present, 1"),
    };

    private static void WriteXml(WireWriter w, DataType.XmlType x)
    {
        w.WriteU8(Tags.Xml);
        w.WriteU8((byte)x.Primary);
        w.WriteU8((byte)x.Secondary);
        if (x.Schema is { } schema)
        {
            WriteName(w, schema.Name);
            WritePart(w, schema.Namespace);
            WritePart(w, schema.Element);
        }
    }

    private static void WritePart(WireWriter w, string? part)
    {
        w.WriteFlag(part is not null);
        if (part is not null)
        {
            w.WriteString(part);
        }
    }
}
