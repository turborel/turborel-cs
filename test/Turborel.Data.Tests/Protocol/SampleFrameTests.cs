// Every sample frame of The Native Protocol at 45cb0d7f (RULES.md D21):
// the file read as its page lays it out; each client frame the driver
// sends encoded byte for byte; each server frame decoded to what its
// meaning says, its capabilities granted; each compressed one inflated to
// the frame its meaning names [PROTOCOL Sample frames].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Turborel.Data.Debugging;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of the driver against the protocol's sample frames.</summary>
public sealed class SampleFrameTests
{
    private static readonly IReadOnlyList<KeyValuePair<string, string>> NoAttributes = [];

    /// <summary>The requests the client frames are, by name.</summary>
    private static readonly Dictionary<string, Request> Requests = new()
    {
        ["hello-ping"] = Hello("trust", [], [Capabilities.Ping], NoAttributes),
        ["hello-none"] = Hello("trust", [], [], NoAttributes),
        ["hello-every"] = Hello("trust", [], [Capabilities.Status, Capabilities.Describe, Capabilities.Cursors, Capabilities.Compression, Capabilities.DeclaredTypes], [new("application", "app")]),
        ["hello-attribute-longest"] = Hello("trust", [], [], [new(new string('a', 64), new string('v', 1024))]),
        ["hello-attributes-most"] = Hello("trust", [], [], [.. Enumerable.Range(0, 32).Select(i => new KeyValuePair<string, string>($"a{i}", string.Empty))]),
        ["hello-scram"] = Hello("scram-sha-256", Encoding.ASCII.GetBytes("n,,n=,r=abc"), [], NoAttributes),
        ["answer"] = new Request.Answer(Encoding.ASCII.GetBytes("c=biws,r=abc,p=xyz")),
        ["answer-empty"] = new Request.Answer(Array.Empty<byte>()),
        ["cancel"] = new Request.Cancel(Enumerable.Repeat((byte)0xA5, 16).ToArray()),
        ["query"] = new Request.Query("VALUES 1", Ends: true, NoAttributes),
        ["query-empty"] = new Request.Query(string.Empty, Ends: false, NoAttributes),
        ["query-attributes"] = new Request.Query("VALUES QUERY_ATTRIBUTE('trace')", Ends: false, [new("trace", "t1")]),
        ["query-multi"] = new Request.Query("VALUES 1; VALUES 2", Ends: true, NoAttributes, Multi: true),
        ["prepare"] = new Request.Prepare(1, "SELECT K FROM T WHERE K > ?"),
        ["execute"] = new Request.Execute(new(1), [null, string.Empty, true], 2, false, NoAttributes),
        ["execute-cursor"] = new Request.Execute(new(1, 7), [], 0, true, [new("a", "b")]),
        ["fetch"] = new Request.Fetch(new(1), 100),
        ["fetch-cursor"] = new Request.Fetch(new(1, 7), uint.MaxValue),
        ["close"] = new Request.Close(new(1)),
        ["close-cursor"] = new Request.Close(new(1, 7)),
        ["deallocate"] = new Request.Deallocate(uint.MaxValue),
        ["terminate"] = new Request.Terminate(),
        ["lob-read"] = new LobRequest.Read(3, 0, 65536, Characters: true),
        ["lob-create"] = new LobRequest.Create(LargeObjectKind.Binary),
        ["lob-append"] = new LobRequest.Append(4, new byte[] { 0xC3 }),
        ["lob-free"] = new LobRequest.Free(4),
        ["lob-hold"] = new LobRequest.Hold(3),
        ["describe-statement"] = new Request.Describe(new(1), OfCursor: false),
        ["describe-cursor"] = new Request.Describe(new(1, 7), OfCursor: true),
        ["ping"] = new Request.Ping(),
        ["reset"] = new Request.Reset(),
        ["change-user"] = new Request.ChangeUser("BOB", "password", Encoding.ASCII.GetBytes("secret")),
        ["copy-in"] = new Request.CopyIn("S.T", ["ID", "NAME"], Ends: true),
        ["copy-in-every-column"] = new Request.CopyIn("T", [], Ends: false),
        ["copy-data"] = new Request.CopyData([["1", null], ["2", "b"]]),
        ["copy-columns"] = new Request.CopyColumns([["1", "b"], [null, string.Empty]]),
        ["copy-end"] = new Request.CopyEnd(Abandon: false),
        ["copy-end-abandon"] = new Request.CopyEnd(Abandon: true),
        ["debug-identify"] = new DebugRequest.Identify(),
        ["debug-attach"] = new DebugRequest.Attach(2),
        ["debug-detach"] = new DebugRequest.Detach(),
        ["debug-break"] = new DebugRequest.Break(RoutineKind.Procedure, new SqlName("ANN", "RAISE_PAY"), 4, "PCT > 5"),
        ["debug-clear"] = new DebugRequest.Clear(1),
        ["debug-breakpoints"] = new DebugRequest.Breakpoints(),
        ["debug-resume"] = new DebugRequest.Resume(ResumeAction.Over),
        ["debug-interrupt"] = new DebugRequest.Interrupt(),
        ["debug-stack"] = new DebugRequest.Stack(),
    };

    /// <summary>Gets the client frames' names, the compressed one apart.</summary>
    public static TheoryData<string> ClientFrames => [.. SampleFrame.All.Where(f => f.Direction == "client" && f.Kind != Compression.Kind).Select(f => f.Name)];

    /// <summary>Gets the server frames' names, the compressed one apart.</summary>
    public static TheoryData<string> ServerFrames => [.. SampleFrame.All.Where(f => f.Direction == "server" && f.Kind != Compression.Kind).Select(f => f.Name)];

    /// <summary>The file holds frames of both sides, each named once, of five fields, its frames' lengths those their headers say.</summary>
    [Fact]
    [Verifies("[PROTOCOL Sample frames]")]
    [Verifies("[PROTOCOL Frames]")]
    public void TheFileIsReadAsItsPageLaysItOut()
    {
        Assert.Equal(167, SampleFrame.All.Count);
        Assert.Equal(SampleFrame.All.Count, SampleFrame.All.Select(f => f.Name).Distinct().Count());
        Assert.All(SampleFrame.All, f => Assert.Contains(f.Direction, (string[])["client", "server"]));
        Assert.All(SampleFrame.All, f => Assert.Equal((uint)f.Body.Length, BitConverter.ToUInt32(f.Frame, 1)));
        Assert.All(SampleFrame.All.SelectMany(f => f.Needs), need => Assert.Contains(need, Capabilities.All));
        Assert.Equal(48, SampleFrame.All.Count(f => f.Direction == "client"));
        Assert.Throws<FormatException>(() => SampleFrame.Read("client\tx\t-\tzz\tbad\n"));
    }

    /// <summary>Each client frame is the driver's request, encoded byte for byte.</summary>
    /// <param name="name">The frame's name.</param>
    [Theory]
    [Verifies("[PROTOCOL Sample frames]")]
    [Verifies("[PROTOCOL Requests]")]
    [Verifies("[PROTOCOL Debug 'V']")]
    [Verifies("[PROTOCOL CopyColumns 'O']")]
    [MemberData(nameof(ClientFrames))]
    public void ClientFramesAreEncodedByteForByte(string name)
    {
        Assert.Equal(Convert.ToHexString(SampleFrame.Of(name).Frame), Convert.ToHexString(Wire.Encode(Requests[name])));
    }

    /// <summary>Each server frame decodes, whole, to what its meaning says.</summary>
    /// <param name="name">The frame's name.</param>
    [Theory]
    [Verifies("[PROTOCOL Sample frames]")]
    [Verifies("[PROTOCOL Values]")]
    [Verifies("[PROTOCOL Data types]")]
    [Verifies("[PROTOCOL Declared types]")]
    [Verifies("[PROTOCOL The tail]")]
    [Verifies("[PROTOCOL Debugged 'V']")]
    [Verifies("[PROTOCOL Event 'W']")]
    [MemberData(nameof(ServerFrames))]
    public void ServerFramesDecodeToWhatTheyMean(string name)
    {
        var frame = SampleFrame.Of(name);
        var reply = Reply.Decode(frame.Kind, frame.Body);
        SampleMeanings.Check(name, reply);
    }

    /// <summary>A compressed frame inflates to the frame its meaning names: a Query of 2000 x, and Rows of 64 rows.</summary>
    [Fact]
    [Verifies("[PROTOCOL Sample frames]")]
    [Verifies("[PROTOCOL Compressed frames]")]
    public void CompressedFramesInflateToTheFrameTheyName()
    {
        var query = SampleFrame.Of("query-compressed");
        (byte kind, ReadOnlyMemory<byte> body) = Compression.Received(query.Kind, query.Body, granted: true, FrameHeader.MaxBody);
        byte[] expected = Wire.Encode(new Request.Query($"SELECT '{new string('x', 2000)}' FROM T", Ends: true, NoAttributes));
        Assert.Equal(expected[0], kind);
        Assert.Equal(expected[5..], body.ToArray());
        Assert.Equal(2021, body.Length);
        var rows = SampleFrame.Of("rows-compressed");
        (kind, body) = Compression.Received(rows.Kind, rows.Body, granted: true, FrameHeader.MaxBody);
        Reply.Rows inflated = Assert.IsType<Reply.Rows>(Reply.Decode(kind, body));
        Assert.Equal((3972, 64), (body.Length, inflated.Batch.Count));
        Assert.Equal(new TurborelDecimal(63, 0), inflated.Batch[63][0]);
    }

    /// <summary>
    /// Every data type has its DATA_TYPE name; columns with a declared type
    /// are written with tail 19 and read back, one without written as its
    /// type says; declared types not one for each column fail the
    /// connection.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Declared types]")]
    [Verifies("[PROTOCOL The tail]")]
    public void DeclaredTypesAreWrittenAndNamed()
    {
        var shape = new StructuredType(new SqlName("S", "SHAPE"), []);
        DataType[] types =
        [
            new CharacterType(1, CharacterUnits.Characters, false), new CharacterType(1, CharacterUnits.Characters, true), new ExactType(10, 0), new ApproximateType(53),
            new BinaryType(1, false), new BinaryType(1, true), new DatetimeType(DatetimeMoment.Date, false, 0), new DatetimeType(DatetimeMoment.Time, true, 0),
            new DatetimeType(DatetimeMoment.Timestamp, false, 6), new IntervalType(new IntervalQualifier(IntervalField.Day, IntervalField.Day, 2, 0)), new BooleanType(),
            new DecFloatType(34), new ArrayType(2, new BooleanType()), new RowType([new RowField("A", new BooleanType())]), new LargeObjectType(LargeObjectKind.Character, 1, CharacterUnits.Octets),
            new LargeObjectType(LargeObjectKind.Binary, 1, CharacterUnits.Octets), new MultisetType(new BooleanType()), shape, new ReferenceType(shape, null), new JsonType(),
            new XmlType(XmlPrimary.Content, XmlSecondary.Any, null), new UuidType(), new PluginType("ST_GEOMETRY", []),
        ];
        Assert.Equal(
            [
                "CHARACTER", "CHARACTER VARYING", "NUMERIC", "FLOAT", "BINARY", "BINARY VARYING", "DATE", "TIME WITH TIME ZONE", "TIMESTAMP", "INTERVAL", "BOOLEAN",
                "DECFLOAT", "ARRAY", "ROW", "CHARACTER LARGE OBJECT", "BINARY LARGE OBJECT", "MULTISET", "USER-DEFINED", "REF", "JSON", "XML", "UUID", "ST_GEOMETRY",
            ],
            types.Select(SqlText.Named));
        Assert.Equal(("CHARACTER LARGE OBJECT(1 OCTETS)", "INTERVAL DAY(2)"), (SqlText.Written(types[14]), SqlText.Written(types[9])));
        Column[] columns = [new Column("I", new ExactType(10, 0), false) { Declared = new DeclaredType("INTEGER", "INTEGER") }, new Column("F", new ApproximateType(53), true)];
        byte[] body = Wire.Written(w => Reply.WriteColumns(w, columns));
        Reply.Description back = Assert.IsType<Reply.Description>(Reply.Decode(ReplyKinds.Description, body));
        Assert.Equal([("INTEGER", "INTEGER"), ("FLOAT", "FLOAT(53)")], back.Columns.Select(c => (c.Declared!.Name, c.TypeWritten)));
        byte[] mismatched = Wire.Written(w =>
        {
            w.WriteU32(1);
            columns[1].Write(w);
            w.WriteU8(TailTags.Declared);
            w.WriteU32(2);
            for (int i = 0; i < 4; i++)
            {
                w.WriteString("A");
            }
        });
        _ = Wire.Fails(() => Reply.Decode(ReplyKinds.Description, mismatched), Sections.Tail);
    }

    private static Request.Hello Hello(string mechanism, byte[] initial, IReadOnlyList<string> capabilities, IReadOnlyList<KeyValuePair<string, string>> attributes) =>
        new("ANN", mechanism, initial, capabilities, attributes);
}
