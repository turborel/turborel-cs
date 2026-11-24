# Debugging Routines

[Contents](README.md)

## NAME

`TurborelDebugger` *(extension)*: one connection driving the
procedures, functions and triggers another connection's session runs,
by breakpoints, steps and its call stack.

## SYNOPSIS

```csharp
namespace Turborel.Data.Session;

public sealed partial class NativeConnection
{
    public TurborelDebugger Debugger { get; }
}

namespace Turborel.Data;

public sealed class TurborelConnection : DbConnection
{
    public TurborelDebugger Debugger { get; }
}

namespace Turborel.Data.Debugging;

public sealed class TurborelDebugger
{
    public Task<ulong> IdAsync(CancellationToken cancellationToken = default);
    public Task AttachAsync(ulong id, CancellationToken cancellationToken = default);
    public Task DetachAsync(CancellationToken cancellationToken = default);
    public Task<DebugBreakpoint> BreakAsync(RoutineKind kind, SqlName routine, uint line,
        string? condition = null, CancellationToken cancellationToken = default);
    public Task ClearAsync(uint number, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<DebugBreakpoint>> BreakpointsAsync(CancellationToken cancellationToken = default);
    public Task<DebugEvent?> WaitAsync(TimeSpan? within = null, CancellationToken cancellationToken = default);
    public Task<DebugPause?> StackAsync(CancellationToken cancellationToken = default);
    public Task ResumeAsync(ResumeAction how, CancellationToken cancellationToken = default);
    public Task InterruptAsync(CancellationToken cancellationToken = default);
}

public enum RoutineKind { Procedure = 0, Function = 1, Trigger = 2 }
public enum ResumeAction { Into = 0, Over = 1, Out = 2, Continue = 3 }
public enum DebugEventKind { Paused = 0, TimedOut = 1, Cancelled = 2, Ended = 3 }
public sealed record DebugBreakpoint(uint Number, SqlName Routine, uint Line, string? Condition);
public sealed record DebugEvent(DebugEventKind Kind, DebugPause? Pause);
public sealed record DebugPause(uint? Breakpoint, IReadOnlyList<DebugFrame> Frames);
public sealed record DebugFrame
{
    public DebugOrigin? Origin { get; init; }
    public bool Visible { get; init; }
    public ulong? Place { get; init; }
    public uint? Line { get; init; }
    public uint? Column { get; init; }
    public IReadOnlyList<DebugNamed> Parameters { get; init; }
    public IReadOnlyList<DebugNamed> Variables { get; init; }
    public IReadOnlyList<DebugCursor> Cursors { get; init; }
}
public sealed record DebugOrigin(RoutineKind Kind, SqlName Name, SqlName Shown);
public sealed record DebugNamed(string Name, string DataType, object? Value);
public sealed record DebugCursor(string Name, bool Open, string Query);
```

## DESCRIPTION

Once the server grants `debugger`, which connecting asks for, a
connection's `Debugger` drives another connection's session, its
target, as turborel-client's `debug_` calls do. The target runs its
statements as ever; the debugger waits beside it, on a connection of
its own. A `TurborelConnection`'s `Debugger` is the driver's extension,
as ADO.NET has no debugger; each of its calls runs on the open
connection, and is refused while a reader is open on it (RULES.md D23).

`IdAsync` gives a connection's ID, as INFORMATION_SCHEMA.CONNECTIONS
shows it, and `AttachAsync` attaches the debugger to the session of the
connection of that ID: one of its own user's, or any if its user is the
database's owner or holds the role MONITOR. `DetachAsync` ends the
debugging: its breakpoints go, the events it has not read are dropped,
and its target, if paused, goes on.

`BreakAsync` sets a breakpoint at a line, from 1, of the text of a
procedure, a function or a trigger, its lines counted from its CREATE's.
The routine is named by its specific name, or by its name if it is the
one routine of that kind so named; a trigger by its name. An empty
schema is the session's default. The condition, a search condition over
the frame's parameters and variables, pauses only where it is true. The
breakpoint set names its routine by its specific name. `ClearAsync`
clears a breakpoint by its number, and `BreakpointsAsync` gives them in
the order they were set. The debugger sees into, and pauses in, only the
routines its user owns or holds DEBUG on.

`WaitAsync` gives the target's next event: a pause, with its call stack;
a timeout, as a paused target goes on by itself after
DEBUGGER.PAUSE_TIMEOUT_MS; a cancel of its statement; or its end, which
detaches the debugger. An event comes unasked, between the debugger's
requests: one read among another request's replies is kept, and
`WaitAsync` gives those kept first, oldest first. Else it waits at most
`within`, without limit if null, for the next to begin to arrive, and
gives null if none does. Cancelling its token first throws
`OperationCanceledException`. Neither loses anything of the
connection, which goes on, and a later wait takes the event that comes. A notice
or a status that comes in an event's place is taken, and gives null.

A pause names the breakpoint it came to, or none at the end of a step,
and its frames, the outermost first. A frame names its routine by its
specific name and by the name it is shown by, none of a statement run
directly; tells where it is, in characters of the routine's text and by
line and column, each from 1; and gives its parameters and variables,
each with its type as SQL writes it and its value as the protocol
carries it ([Values](23-values.md)), and its cursors, whether each is
open and its query. A trigger's parameters are its transition
variables. A frame the debugger may not see into is not `Visible`, and
gives its origin alone.

`StackAsync` gives the paused target's pause again, or null while it
runs. `ResumeAsync` lets the paused target go on: `Into` its next
statement, in whatever frame; `Over` to the next of this frame, or of
the one it returns to; `Out` to the next of the frame this one returns
to; or `Continue` to a breakpoint. `InterruptAsync` pauses the running
target at its next statement.

## ERRORS

The server refuses a request with one of these, the session kept:

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08003 | -919 | `AttachAsync` of an ID of no connection served, or of one the user may not see |
| 42000 | -104 | `BreakAsync` in a routine the user neither owns nor holds DEBUG on |
| 42000 | -103 | `BreakAsync` of no such routine, or of no such line |
| TBD01 | -910 | A request the target's state does not allow: `ResumeAsync` of a target not paused, a second `AttachAsync`, an attach to its own session, `ClearAsync` of no such breakpoint, or any request but `IdAsync` and `AttachAsync` of a debugger attached to none |

The driver's own:

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08006 | -919 | `debugger` not granted, before anything is sent; a reply where an event was waited for, or a reply of another kind than its request's, which loses the connection |
| TB910 | -910 | A `TurborelConnection`'s debugger called while it is closed or a reader is open on it |

`WaitAsync` cancelled by its token before an event begins to arrive
throws `OperationCanceledException`, the connection kept.

## EXAMPLES

See [Debugging Routines](../user/11-debugging.md) of the User's Guide.

## STANDARD

*(extension)* [PROTOCOL Debug 'V'], [PROTOCOL Debugged 'V'] and
[PROTOCOL Event 'W'] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Debugging routines] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md);
RULES.md D23.

## SEE ALSO

[Connections](21-connections.md), [Errors](30-errors.md),
[TurborelConnection](02-api-connection.md).
