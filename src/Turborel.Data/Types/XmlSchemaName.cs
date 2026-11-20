// The XML Schema an XML type of XMLSCHEMA names [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>The registered XML Schema an XML type is typed by.</summary>
/// <param name="Name">The schema's schema and name.</param>
/// <param name="Namespace">Its namespace, if one is named.</param>
/// <param name="Element">Its element, if one is named.</param>
internal sealed record XmlSchemaName(SqlName Name, string? Namespace, string? Element);
