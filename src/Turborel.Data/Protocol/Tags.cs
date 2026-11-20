// The tags of data types and values, one table of numbers for both, as
// the protocol gives them [PROTOCOL Data types] [PROTOCOL Values].

namespace Turborel.Data.Protocol;

/// <summary>The tag each data type and each value is written after.</summary>
internal static class Tags
{
    /// <summary>The null value.</summary>
    internal const byte Null = 0;

    /// <summary>CHARACTER; a character string value, of either character type.</summary>
    internal const byte Character = 1;

    /// <summary>Exact numeric; an exact number.</summary>
    internal const byte Exact = 2;

    /// <summary>Approximate numeric; an approximate number.</summary>
    internal const byte Approximate = 3;

    /// <summary>A plug-in's type; a plug-in's value.</summary>
    internal const byte Plugin = 4;

    /// <summary>CHARACTER VARYING.</summary>
    internal const byte Varying = 5;

    /// <summary>BINARY; a binary string value, of either binary type.</summary>
    internal const byte Binary = 6;

    /// <summary>BINARY VARYING.</summary>
    internal const byte BinaryVarying = 7;

    /// <summary>DATE; a date.</summary>
    internal const byte Date = 8;

    /// <summary>TIME; a time.</summary>
    internal const byte Time = 9;

    /// <summary>TIME WITH TIME ZONE.</summary>
    internal const byte TimeZoned = 10;

    /// <summary>TIMESTAMP; a timestamp.</summary>
    internal const byte Timestamp = 11;

    /// <summary>TIMESTAMP WITH TIME ZONE.</summary>
    internal const byte TimestampZoned = 12;

    /// <summary>INTERVAL; an interval.</summary>
    internal const byte Interval = 13;

    /// <summary>BOOLEAN; a truth value.</summary>
    internal const byte Boolean = 14;

    /// <summary>DECFLOAT; a decimal floating-point value.</summary>
    internal const byte DecFloat = 15;

    /// <summary>An array type; an array.</summary>
    internal const byte Array = 16;

    /// <summary>A row type; a row.</summary>
    internal const byte Row = 17;

    /// <summary>CHARACTER LARGE OBJECT.</summary>
    internal const byte Clob = 18;

    /// <summary>BINARY LARGE OBJECT.</summary>
    internal const byte Blob = 19;

    /// <summary>A large object's value, whole.</summary>
    internal const byte LargeObjectWhole = 20;

    /// <summary>A large object's value, by its locator.</summary>
    internal const byte LargeObjectLocator = 21;

    /// <summary>A multiset type; a multiset.</summary>
    internal const byte Multiset = 22;

    /// <summary>A structured type; a structured value.</summary>
    internal const byte Structured = 23;

    /// <summary>A reference type; a reference.</summary>
    internal const byte Reference = 24;

    /// <summary>JSON; a JSON value, its stored form.</summary>
    internal const byte Json = 25;

    /// <summary>XML; an XML value, its serialization.</summary>
    internal const byte Xml = 26;

    /// <summary>
    /// UUID, with no parameters; a UUID, its 16 octets as bytes. The
    /// protocol's page does not list it yet; turborel's RULES.md D184 and
    /// protocol/src/fields.rs give it.
    /// </summary>
    internal const byte Uuid = 27;
}
