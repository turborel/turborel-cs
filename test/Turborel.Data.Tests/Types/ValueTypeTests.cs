// What a program does with the value types itself: an XML value's nodes
// parsed from its text, and a JSON value written as its text
// [CLIENT XML values] [PROTOCOL Values].

using System.Linq;
using System.Xml.Linq;
using Turborel.Data.Json;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Types;

/// <summary>Tests of <see cref="TurborelXml"/> and <see cref="TurborelJson"/>.</summary>
public sealed class ValueTypeTests
{
    /// <summary>
    /// An XML value's nodes are parsed from its text as content, whitespace
    /// and a declaration kept out, no document type declaration read; text
    /// not well-formed content is refused, SQLSTATE 2200N.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT XML values]")]
    public void XmlNodesAreParsedFromItsText()
    {
        var value = new TurborelXml("<?xml version=\"1.0\"?><a b=\"1\">x</a> <!--c--><?p d?>tail");

        XNode[] nodes = [.. value.Nodes()];
        Assert.Equal([System.Xml.XmlNodeType.Element, System.Xml.XmlNodeType.Text, System.Xml.XmlNodeType.Comment, System.Xml.XmlNodeType.ProcessingInstruction, System.Xml.XmlNodeType.Text], nodes.Select(n => n.NodeType));
        Assert.Equal("1", (string?)((XElement)nodes[0]).Attribute("b"));
        Assert.Empty(new TurborelXml(string.Empty).Nodes());
        foreach (string bad in new[] { "<a>", "<!DOCTYPE a [<!ENTITY e \"x\">]><a>&e;</a>" })
        {
            TurborelException failure = Assert.Throws<TurborelException>(() => new TurborelXml(bad).Nodes());
            Assert.Equal((-301, "2200N"), (failure.SqlCode, failure.SqlState));
        }
    }

    /// <summary>A JSON value's text is its JSON text; of one with none, why it has none.</summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    public void JsonIsWrittenAsItsText()
    {
        Assert.Equal("{\"a\":[1,\"x\"]}", TurborelJson.Parse("{ \"a\" : [ 1 , \"x\" ] }").ToString());
        var datetime = TurborelJson.FromItem(new JsonItem.DatetimeItem(new TurborelDatetime(DatetimeMoment.Date, 0, 0, false, 0)));
        Assert.Contains("datetime", datetime.ToString(), System.StringComparison.Ordinal);
    }
}
