// An XML value: its serialization as CONTENT, as the protocol carries it
// [PROTOCOL Values], and its nodes parsed from it, as turborel parses
// CONTENT, whitespace kept [CLIENT XML values].

using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace Turborel.Data.Types;

/// <summary>A value of an XML type, as its text; made of its text to send one.</summary>
/// <param name="Text">Its serialization as CONTENT.</param>
public sealed record TurborelXml(string Text)
{
    private const int DataSqlCode = -301;
    private const string InvalidContent = "2200N";

    /// <summary>Parses its nodes from its text, as XML content: elements, text, comments and processing instructions, whitespace kept.</summary>
    /// <returns>The nodes, in order.</returns>
    /// <exception cref="TurborelException">SQLSTATE 2200N if the text is not well-formed XML content.</exception>
    public IReadOnlyList<XNode> Nodes()
    {
        // No document type declaration is read, so none can reach out for
        // an entity.
        var settings = new XmlReaderSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        var nodes = new List<XNode>();
        try
        {
            using var reader = XmlReader.Create(new StringReader(Text), settings);
            _ = reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.XmlDeclaration)
                {
                    _ = reader.Read();
                    continue;
                }

                nodes.Add(XNode.ReadFrom(reader));
            }
        }
        catch (XmlException e)
        {
            throw new TurborelException(DataSqlCode, InvalidContent, $"[DESIGN] the value is not well-formed XML content: {e.Message}", e);
        }

        return nodes;
    }
}
