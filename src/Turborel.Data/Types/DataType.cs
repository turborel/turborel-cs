// The data types of columns, parameters and the types inside other types,
// one record a kind, as the protocol writes them after their tags
// [PROTOCOL Data types]. A distinct type comes as its source type.

using System.Collections.Generic;

namespace Turborel.Data.Types;

/// <summary>A data type, as the server describes a column or a parameter.</summary>
internal abstract record DataType
{
    /// <summary>CHARACTER (tag 1) or CHARACTER VARYING (tag 5).</summary>
    /// <param name="Length">Its length, or maximum length.</param>
    /// <param name="Units">Whether the length counts characters or octets.</param>
    /// <param name="Varying">Whether it is CHARACTER VARYING.</param>
    internal sealed record CharacterType(uint Length, CharacterUnits Units, bool Varying) : DataType;

    /// <summary>An exact numeric type (tag 2).</summary>
    /// <param name="Precision">Its precision.</param>
    /// <param name="Scale">Its scale.</param>
    internal sealed record ExactType(uint Precision, uint Scale) : DataType;

    /// <summary>An approximate numeric type (tag 3).</summary>
    /// <param name="Precision">Its precision.</param>
    internal sealed record ApproximateType(uint Precision) : DataType;

    /// <summary>A plug-in's type (tag 4), read by its name, shown by its values' text.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Parameters">Its parameters.</param>
    internal sealed record PluginType(string Name, IReadOnlyList<uint> Parameters) : DataType;

    /// <summary>BINARY (tag 6) or BINARY VARYING (tag 7).</summary>
    /// <param name="Length">Its length, or maximum length.</param>
    /// <param name="Varying">Whether it is BINARY VARYING.</param>
    internal sealed record BinaryType(uint Length, bool Varying) : DataType;

    /// <summary>DATE (8), TIME (9), TIME WITH TIME ZONE (10), TIMESTAMP (11) or TIMESTAMP WITH TIME ZONE (12).</summary>
    /// <param name="Moment">Whether it is a date, a time or a timestamp.</param>
    /// <param name="Zoned">Whether it is WITH TIME ZONE.</param>
    /// <param name="Precision">Its fractional seconds precision.</param>
    internal sealed record DatetimeType(DatetimeMoment Moment, bool Zoned, uint Precision) : DataType;

    /// <summary>INTERVAL (tag 13).</summary>
    /// <param name="Qualifier">Its fields and precisions.</param>
    internal sealed record IntervalType(IntervalQualifier Qualifier) : DataType;

    /// <summary>BOOLEAN (tag 14).</summary>
    internal sealed record BooleanType : DataType;

    /// <summary>DECFLOAT (tag 15).</summary>
    /// <param name="Precision">Its precision.</param>
    internal sealed record DecFloatType(uint Precision) : DataType;

    /// <summary>An array type (tag 16).</summary>
    /// <param name="MaxCardinality">Its maximum cardinality.</param>
    /// <param name="Element">Its element type.</param>
    internal sealed record ArrayType(uint MaxCardinality, DataType Element) : DataType;

    /// <summary>A row type (tag 17).</summary>
    /// <param name="Fields">Its fields, one at least.</param>
    internal sealed record RowType(IReadOnlyList<RowField> Fields) : DataType;

    /// <summary>CHARACTER LARGE OBJECT (tag 18) or BINARY LARGE OBJECT (tag 19).</summary>
    /// <param name="Kind">Whether it is of characters or octets.</param>
    /// <param name="Length">Its maximum length.</param>
    /// <param name="Units">Whether the length counts characters or octets.</param>
    internal sealed record LargeObjectType(LargeObjectKind Kind, ulong Length, CharacterUnits Units) : DataType;

    /// <summary>A multiset type (tag 22).</summary>
    /// <param name="Element">Its element type.</param>
    internal sealed record MultisetType(DataType Element) : DataType;

    /// <summary>A structured type (tag 23).</summary>
    /// <param name="Name">Its schema and name.</param>
    /// <param name="Supertypes">Its proper supertypes' names.</param>
    internal sealed record StructuredType(SqlName Name, IReadOnlyList<SqlName> Supertypes) : DataType;

    /// <summary>A reference type (tag 24).</summary>
    /// <param name="Referenced">The referenced structured type.</param>
    /// <param name="Scope">The table its references are scoped to, if any.</param>
    internal sealed record ReferenceType(StructuredType Referenced, SqlName? Scope) : DataType;

    /// <summary>JSON (tag 25).</summary>
    internal sealed record JsonType : DataType;

    /// <summary>An XML type (tag 26).</summary>
    /// <param name="Primary">Its primary modifier.</param>
    /// <param name="Secondary">Its secondary modifier.</param>
    /// <param name="Schema">The schema of an XMLSCHEMA type.</param>
    internal sealed record XmlType(XmlPrimary Primary, XmlSecondary Secondary, XmlSchemaName? Schema) : DataType;

    /// <summary>UUID (tag 27), with no parameters.</summary>
    internal sealed record UuidType : DataType;
}
