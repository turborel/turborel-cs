// XML and UUID values against a real turboreld (RULES.md R-TEST-8): read
// as their text, an XML value's nodes parsed from it, and each sent as a
// parameter of its text or octets [CLIENT XML values] [CLIENT UUIDs].

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of XML and UUID values.</summary>
/// <param name="server">The live server.</param>
public sealed class ValueLiveTests(LiveServer server)
{
    /// <summary>
    /// An XML parameter made of its text is read back as its serialization,
    /// its nodes parsed from it; a value with no serialization fails the
    /// fetch, SQLSTATE 2200W.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT XML values]")]
    public async Task XmlIsItsText()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("ORDERS");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID INTEGER, BODY XML(CONTENT))");

        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new TurborelDecimal(18, 0), new TurborelXml("<order no=\"18\"><item>jam</item></order>")], TestContext.Current.CancellationToken);
        TurborelXml body = Assert.IsType<TurborelXml>(Assert.Single(await c.RowsAsync($"SELECT BODY FROM {t}"))[0]);
        Assert.Equal("<order no=\"18\"><item>jam</item></order>", body.Text);
        XElement order = Assert.IsType<XElement>(Assert.Single(body.Nodes()));
        Assert.Equal(("order", "18", "jam"), (order.Name.LocalName, (string?)order.Attribute("no"), order.Element("item")?.Value));
        TurborelException unserializable = await Assert.ThrowsAsync<TurborelException>(
            () => c.QueryAsync("VALUES (XMLQUERY('attribute a {\"1\"}' RETURNING SEQUENCE BY REF EMPTY ON EMPTY))", TestContext.Current.CancellationToken));
        Assert.Equal("2200W", unserializable.SqlState);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>A UUID parameter made of its octets or of its text is read back as its octets and its text.</summary>
    [Fact]
    [Verifies("[CLIENT UUIDs]")]
    public async Task UuidsAreOctetsAndText()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("TICKETS");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID UUID, CODE VARCHAR(4))");
        byte[] octets = Convert.FromHexString("919108f752d143209bacf847db4148a8");

        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new Guid(octets, bigEndian: true), "12A"], TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [Guid.Parse("00000000-0000-4000-8000-000000000001"), "12B"], TestContext.Current.CancellationToken);
        Guid[] ids = [.. (await c.RowsAsync($"SELECT ID FROM {t} ORDER BY CODE")).Select(row => Assert.IsType<Guid>(row[0]))];
        Assert.Equal(octets, ids[0].ToByteArray(bigEndian: true));
        Assert.Equal("919108f7-52d1-4320-9bac-f847db4148a8", ids[0].ToString());
        Assert.Equal("00000000-0000-4000-8000-000000000001", ids[1].ToString());
        _ = await c.DoneAsync("ROLLBACK");
    }
}
