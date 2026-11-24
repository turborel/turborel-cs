// The debugger's replies read whole or refused [PROTOCOL Debugged 'V']
// [PROTOCOL Event 'W']: a kind of none of their codes, a pause of no
// reason, or a routine of no kind is a malformed reply, which fails the
// connection; an Event ends no request; a frame read back is the frame
// written.

using Turborel.Data.Debugging;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="DebugCodec"/>.</summary>
public sealed class DebugCodecTests
{
    /// <summary>A code of no answer, event, reason or kind fails the connection, citing its section.</summary>
    [Fact]
    [Verifies("[PROTOCOL Debugged 'V']")]
    [Verifies("[PROTOCOL Event 'W']")]
    public void ACodeOfNoneFailsTheConnection()
    {
        _ = Wire.Fails(() => Wire.Decode('V', Wire.Hex("04")), Sections.Debugged);
        _ = Wire.Fails(() => Wire.Decode('W', Wire.Hex("04")), Sections.Event);
        _ = Wire.Fails(() => Wire.Decode('W', Wire.Hex("00 02")), Sections.Event);
        _ = Wire.Fails(() => Wire.Decode('W', Wire.Hex("00 01 01000000 01 03")), Sections.Event);
    }

    /// <summary>
    /// A frame of every field read back as written: its origin, place, line
    /// and column, parameters, variables and cursors; one with none of them;
    /// an Event ends no request, a Debugged does.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Event 'W']")]
    [Verifies("[PROTOCOL Debugged 'V']")]
    public void FramesAreReadAsWritten()
    {
        var full = new DebugFrame
        {
            Origin = new DebugOrigin(RoutineKind.Trigger, new SqlName("S", "T"), new SqlName("S", "T")),
            Visible = true,
            Place = 12,
            Line = 2,
            Column = 5,
            Parameters = [new DebugNamed("NEW_ROW", "ROW", null)],
            Variables = [new DebugNamed("V", "CHARACTER VARYING(5)", "abc")],
            Cursors = [new DebugCursor("C", true, "VALUES 1")],
        };
        var bare = new DebugFrame();
        byte[] body = Wire.Written(w => DebugCodec.WriteEvent(w, new DebugEvent(DebugEventKind.Paused, new DebugPause(3, [bare, full]))));
        DebugPause pause = Assert.IsType<Reply.Event>(Wire.Decode('W', body)).Told.Pause!;
        Assert.Equal(3U, pause.Breakpoint);
        Assert.Equal((null, false, null, null, null), (pause.Frames[0].Origin, pause.Frames[0].Visible, pause.Frames[0].Place, pause.Frames[0].Line, pause.Frames[0].Column));
        DebugFrame back = pause.Frames[1];
        Assert.Equal((full.Origin, true, 12UL, 2U, 5U), (back.Origin, back.Visible, back.Place, back.Line, back.Column));
        Assert.Equal((full.Parameters[0], full.Variables[0], full.Cursors[0]), (back.Parameters[0], back.Variables[0], back.Cursors[0]));
        Assert.False(Reply.EndsRequest(ReplyKinds.Event, body));
        Assert.True(Reply.EndsRequest(ReplyKinds.Debugged, Wire.Hex("00 0100000000000000")));
        Assert.Equal(
            Wire.Hex("02 00000000"),
            Wire.Written(w => DebugCodec.WriteAnswer(w, new DebugAnswer.Breakpoints([]))));
    }
}
