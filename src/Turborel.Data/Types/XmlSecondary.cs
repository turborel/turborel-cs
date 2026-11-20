// An XML type's secondary modifier [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>An XML type's secondary modifier.</summary>
public enum XmlSecondary
{
    /// <summary>UNTYPED, 0.</summary>
    Untyped = 0,

    /// <summary>ANY, 1.</summary>
    Any = 1,

    /// <summary>XMLSCHEMA, 2, naming its schema.</summary>
    XmlSchema = 2,
}
