// What the server answers a Debug request with, Debugged 'V', and tells a
// debugger unasked, Event 'W', with debugger *(extension)* [PROTOCOL
// Debugged 'V'] [PROTOCOL Event 'W']: a connection's ID, a breakpoint, the
// breakpoints, or a stack; an event, paused with its call stack, timed
// out, cancelled or ended. A code of none of these is a malformed reply,
// which fails the connection, as turborel-protocol's debug module reads
// it. The writers are the server's, for tests.

using System.Collections.Generic;
using System.Globalization;
using Turborel.Data.Debugging;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>What a Debug request gave back, but a completion.</summary>
internal abstract record DebugAnswer
{
    /// <summary>0, identify's: the connection's ID.</summary>
    /// <param name="Id">The ID.</param>
    internal sealed record Connection(ulong Id) : DebugAnswer;

    /// <summary>1, break's: the breakpoint set.</summary>
    /// <param name="Set">The breakpoint.</param>
    internal sealed record Breakpoint(DebugBreakpoint Set) : DebugAnswer;

    /// <summary>2, breakpoints': each set, in the order they were set.</summary>
    /// <param name="All">The breakpoints.</param>
    internal sealed record Breakpoints(IReadOnlyList<DebugBreakpoint> All) : DebugAnswer;

    /// <summary>3, stack's: the target's pause; null if it runs.</summary>
    /// <param name="Pause">The pause.</param>
    internal sealed record Stack(DebugPause? Pause) : DebugAnswer;
}

/// <summary>Reads and writes the debugger's replies.</summary>
internal static class DebugCodec
{
    private const byte StepReason = 1;

    /// <summary>Reads a Debugged reply's body.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The answer.</returns>
    internal static DebugAnswer ReadAnswer(WireReader r) => r.ReadU8() switch
    {
        0 => new DebugAnswer.Connection(r.ReadU64()),
        1 => new DebugAnswer.Breakpoint(ReadBreakpoint(r)),
        2 => new DebugAnswer.Breakpoints(Reply.ReadList(r, ReadBreakpoint)),
        3 => new DebugAnswer.Stack(r.ReadFlag() ? ReadPause(r) : null),
        var what => throw r.Malformed(Sections.Debugged, string.Create(CultureInfo.InvariantCulture, $"no Debugged reply is of kind {what}")),
    };

    /// <summary>Reads an Event's body.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The event.</returns>
    internal static DebugEvent ReadEvent(WireReader r) => r.ReadU8() switch
    {
        0 => new DebugEvent(DebugEventKind.Paused, ReadPause(r)),
        var what and <= (byte)DebugEventKind.Ended => new DebugEvent((DebugEventKind)what, null),
        var what => throw r.Malformed(Sections.Event, string.Create(CultureInfo.InvariantCulture, $"no event is of kind {what}")),
    };

    /// <summary>Writes a Debugged reply's body.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="answer">The answer.</param>
    internal static void WriteAnswer(WireWriter w, DebugAnswer answer)
    {
        switch (answer)
        {
            case DebugAnswer.Connection c:
                w.WriteU8(0);
                w.WriteU64(c.Id);
                break;
            case DebugAnswer.Breakpoint b:
                w.WriteU8(1);
                WriteBreakpoint(w, b.Set);
                break;
            case DebugAnswer.Breakpoints all:
                w.WriteU8(2);
                w.WriteU32((uint)all.All.Count);
                foreach (DebugBreakpoint each in all.All)
                {
                    WriteBreakpoint(w, each);
                }

                break;
            default:
                w.WriteU8(3);
                DebugPause? pause = ((DebugAnswer.Stack)answer).Pause;
                w.WriteFlag(pause is not null);
                if (pause is not null)
                {
                    WritePause(w, pause);
                }

                break;
        }
    }

    /// <summary>Writes an Event's body.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="debugEvent">The event.</param>
    internal static void WriteEvent(WireWriter w, DebugEvent debugEvent)
    {
        w.WriteU8((byte)debugEvent.Kind);
        if (debugEvent.Pause is { } pause)
        {
            WritePause(w, pause);
        }
    }

    /// <summary>Writes a string if it is given, after its flag.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="given">The string, or null.</param>
    internal static void WriteGiven(WireWriter w, string? given)
    {
        w.WriteFlag(given is not null);
        if (given is not null)
        {
            w.WriteString(given);
        }
    }

    // A string after its flag, if given.
    private static string? ReadGiven(WireReader r) => r.ReadFlag() ? r.ReadString() : null;

    // A name: its schema, then its own.
    private static SqlName ReadName(WireReader r) => new(r.ReadString(), r.ReadString());

    private static void WriteName(WireWriter w, SqlName name)
    {
        w.WriteString(name.Schema);
        w.WriteString(name.Name);
    }

    private static DebugBreakpoint ReadBreakpoint(WireReader r) => new(r.ReadU32(), ReadName(r), r.ReadU32(), ReadGiven(r));

    private static void WriteBreakpoint(WireWriter w, DebugBreakpoint b)
    {
        w.WriteU32(b.Number);
        WriteName(w, b.Routine);
        w.WriteU32(b.Line);
        WriteGiven(w, b.Condition);
    }

    // A pause: its reason, a breakpoint's number or a step, then its frames.
    private static DebugPause ReadPause(WireReader r)
    {
        uint? breakpoint = r.ReadU8() switch
        {
            0 => r.ReadU32(),
            StepReason => null,
            var reason => throw r.Malformed(Sections.Event, string.Create(CultureInfo.InvariantCulture, $"no pause is for reason {reason}")),
        };
        return new DebugPause(breakpoint, Reply.ReadList(r, ReadFrame));
    }

    private static void WritePause(WireWriter w, DebugPause pause)
    {
        w.WriteU8(pause.Breakpoint is null ? StepReason : (byte)0);
        if (pause.Breakpoint is uint number)
        {
            w.WriteU32(number);
        }

        w.WriteU32((uint)pause.Frames.Count);
        foreach (DebugFrame frame in pause.Frames)
        {
            WriteFrame(w, frame);
        }
    }

    // A frame: its origin if it has one, whether it is visible, its place,
    // its line and column, its parameters, variables and cursors.
    private static DebugFrame ReadFrame(WireReader r)
    {
        DebugOrigin? origin = r.ReadFlag() ? new DebugOrigin(ReadKind(r), ReadName(r), ReadName(r)) : null;
        bool visible = r.ReadFlag();
        ulong? place = r.ReadFlag() ? r.ReadU64() : null;
        (uint Line, uint Column)? line = r.ReadFlag() ? (r.ReadU32(), r.ReadU32()) : null;
        return new DebugFrame
        {
            Origin = origin,
            Visible = visible,
            Place = place,
            Line = line?.Line,
            Column = line?.Column,
            Parameters = Reply.ReadList(r, ReadNamed),
            Variables = Reply.ReadList(r, ReadNamed),
            Cursors = Reply.ReadList(r, c => new DebugCursor(c.ReadString(), c.ReadFlag(), c.ReadString())),
        };
    }

    private static void WriteFrame(WireWriter w, DebugFrame frame)
    {
        w.WriteFlag(frame.Origin is not null);
        if (frame.Origin is { } origin)
        {
            w.WriteU8((byte)origin.Kind);
            WriteName(w, origin.Name);
            WriteName(w, origin.Shown);
        }

        w.WriteFlag(frame.Visible);
        w.WriteFlag(frame.Place is not null);
        if (frame.Place is ulong place)
        {
            w.WriteU64(place);
        }

        w.WriteFlag(frame.Line is not null);
        if (frame.Line is uint line)
        {
            w.WriteU32(line);
            w.WriteU32(frame.Column ?? 0);
        }

        foreach (IReadOnlyList<DebugNamed> list in new[] { frame.Parameters, frame.Variables })
        {
            w.WriteU32((uint)list.Count);
            foreach (DebugNamed named in list)
            {
                w.WriteString(named.Name);
                w.WriteString(named.DataType);
                ValueCodec.Write(w, named.Value);
            }
        }

        w.WriteU32((uint)frame.Cursors.Count);
        foreach (DebugCursor cursor in frame.Cursors)
        {
            w.WriteString(cursor.Name);
            w.WriteFlag(cursor.Open);
            w.WriteString(cursor.Query);
        }
    }

    private static RoutineKind ReadKind(WireReader r) => r.ReadU8() switch
    {
        var kind and <= (byte)RoutineKind.Trigger => (RoutineKind)kind,
        var kind => throw r.Malformed(Sections.Event, string.Create(CultureInfo.InvariantCulture, $"no routine is of kind {kind}")),
    };

    private static DebugNamed ReadNamed(WireReader r) => new(r.ReadString(), r.ReadString(), ValueCodec.Read(r));
}
