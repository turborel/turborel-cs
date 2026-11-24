// What the routine debugger *(extension)* names and is told, as
// turborel-client's debug module gives it [PROTOCOL Debug 'V'] [PROTOCOL
// Debugged 'V'] [PROTOCOL Event 'W'] [CLIENT Debugging routines]: a
// routine's kind, how a paused target goes on, a breakpoint, and a pause
// with its call stack, each frame's routine, place, parameters, variables
// and cursors.

using System.Collections.Generic;
using Turborel.Data.Types;

namespace Turborel.Data.Debugging;

/// <summary>What a breakpoint's routine is, by the code the protocol carries it as.</summary>
public enum RoutineKind
{
    /// <summary>A procedure, 0.</summary>
    Procedure = 0,

    /// <summary>A function, 1.</summary>
    Function = 1,

    /// <summary>A trigger, 2.</summary>
    Trigger = 2,
}

/// <summary>How a paused target goes on, by the code the protocol carries it as.</summary>
public enum ResumeAction
{
    /// <summary>To its next statement, in whatever frame: a step into, 0.</summary>
    Into = 0,

    /// <summary>To the next statement of this frame, or of one it returns to: a step over, 1.</summary>
    Over = 1,

    /// <summary>To the next statement of the frame this one returns to: a step out, 2.</summary>
    Out = 2,

    /// <summary>To a breakpoint, 3.</summary>
    Continue = 3,
}

/// <summary>What a debugger hears of its target.</summary>
public enum DebugEventKind
{
    /// <summary>It paused, at a breakpoint or at the end of a step.</summary>
    Paused = 0,

    /// <summary>Paused longer than DEBUGGER.PAUSE_TIMEOUT_MS, it went on by itself.</summary>
    TimedOut = 1,

    /// <summary>Its statement was cancelled while it was paused.</summary>
    Cancelled = 2,

    /// <summary>Its session ended, and the debugger is detached.</summary>
    Ended = 3,
}

/// <summary>A breakpoint, as the server tells it was set.</summary>
/// <param name="Number">Its number, from 1, which clears it.</param>
/// <param name="Routine">The specific name of its routine, or the name of its trigger.</param>
/// <param name="Line">Its line of the routine's text, from 1.</param>
/// <param name="Condition">The search condition it pauses only where true, if given.</param>
public sealed record DebugBreakpoint(uint Number, SqlName Routine, uint Line, string? Condition);

/// <summary>The routine or trigger a frame runs the body of.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Name">Its specific name, or the trigger's name.</param>
/// <param name="Shown">Its name.</param>
public sealed record DebugOrigin(RoutineKind Kind, SqlName Name, SqlName Shown);

/// <summary>A frame's parameter or variable; a trigger's parameters are its transition variables.</summary>
/// <param name="Name">Its name.</param>
/// <param name="DataType">Its data type, as SQL writes it.</param>
/// <param name="Value">Its value, as the protocol carries it: the driver's own types, or null.</param>
public sealed record DebugNamed(string Name, string DataType, object? Value);

/// <summary>A cursor a frame declares.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Open">Whether it is open.</param>
/// <param name="Query">Its query, as written.</param>
public sealed record DebugCursor(string Name, bool Open, string Query);

/// <summary>A frame of the call stack: a run of a routine's or a trigger's body, or a statement run directly.</summary>
public sealed record DebugFrame
{
    /// <summary>Gets its routine; none of a statement run directly.</summary>
    public DebugOrigin? Origin { get; init; }

    /// <summary>Gets a value indicating whether the debugger may see into it; one not visible shows its origin alone.</summary>
    public bool Visible { get; init; }

    /// <summary>Gets where it is, in characters from the beginning of the routine's text, if told.</summary>
    public ulong? Place { get; init; }

    /// <summary>Gets its line, from 1, if told.</summary>
    public uint? Line { get; init; }

    /// <summary>Gets its column, from 1, if told.</summary>
    public uint? Column { get; init; }

    /// <summary>Gets its parameters, in order.</summary>
    public IReadOnlyList<DebugNamed> Parameters { get; init; } = [];

    /// <summary>Gets its variables, in order.</summary>
    public IReadOnlyList<DebugNamed> Variables { get; init; } = [];

    /// <summary>Gets its cursors, in order.</summary>
    public IReadOnlyList<DebugCursor> Cursors { get; init; } = [];
}

/// <summary>A target's pause: why, and its call stack.</summary>
/// <param name="Breakpoint">The number of the breakpoint it paused at; null of the end of a step.</param>
/// <param name="Frames">Its call stack, the outermost frame first.</param>
public sealed record DebugPause(uint? Breakpoint, IReadOnlyList<DebugFrame> Frames);

/// <summary>What a debugger hears of its target, unasked.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Pause">The pause, of <see cref="DebugEventKind.Paused"/>; null of the others.</param>
public sealed record DebugEvent(DebugEventKind Kind, DebugPause? Pause);
