# Debugging Routines

[Contents](README.md)

*(extension)* A procedure, a function or a trigger written in SQL can be
run under a debugger. The debugger is another connection to the same
server. It attaches to the session that runs the routine, its target,
sets breakpoints at lines of the routine's text, and, each time the
target pauses, reads its call stack, each frame's parameters, variables
and cursors, and tells it how to go on. Turborel's manual describes the
debugger itself ([Debugging Routines](https://github.com/turborel/turborel/blob/main/doc/user/11-debugging.md)).

## Who may debug what

A connection attaches to the session of another connection of its own
user. The database's owner, and a user granted the role MONITOR, attach
to any connection's session. The debugger sets breakpoints in, pauses
in and sees the variables of only the routines its user owns or holds
DEBUG on, granted as EXECUTE is. A frame of any other routine shows its
name alone.

## Two connections

A connection's `Debugger` gives its ID, by `IdAsync`; another's attaches
to it by `AttachAsync`. Here the target defines a procedure, the
debugger sets a breakpoint at line 4 of its text, its UPDATE, and the
target calls it. The CALL runs as a task of its own, and completes only
once the debugger lets it go on. `WaitAsync` waits at most 10 seconds
for each pause; the first is at the breakpoint, and `ResumeAction.Over`
runs the UPDATE and pauses at the next statement, a step; there
`ResumeAction.Continue` lets the CALL run to its end. The innermost
frame is the last.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Debugging;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection target =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await using NativeConnection debugging =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await target.QueryAsync("""
    CREATE PROCEDURE RAISE_PAY(IN PCT INTEGER) MODIFIES SQL DATA
    BEGIN
      DECLARE N INTEGER DEFAULT 0;
      UPDATE STAFF SET SALARY = SALARY * (100 + PCT) / 100;
      SET N = (SELECT COUNT(*) FROM STAFF);
    END
    """);
await target.QueryAsync("COMMIT");

TurborelDebugger debugger = debugging.Debugger;
await debugger.AttachAsync(await target.Debugger.IdAsync());
DebugBreakpoint set = await debugger.BreakAsync(RoutineKind.Procedure, new SqlName("", "RAISE_PAY"), 4);
Console.WriteLine($"breakpoint {set.Number} at {set.Routine.Schema}.{set.Routine.Name} line {set.Line}");

Task<Outcome> call = target.QueryAsync("CALL RAISE_PAY(10)");
foreach (ResumeAction then in new[] { ResumeAction.Over, ResumeAction.Continue })
{
    DebugEvent? paused = await debugger.WaitAsync(TimeSpan.FromSeconds(10));
    DebugPause pause = paused!.Pause!;
    DebugFrame frame = pause.Frames[^1];
    string why = pause.Breakpoint is uint number ? $"breakpoint {number}" : "a step";
    Console.WriteLine($"paused at {why}: {frame.Origin?.Shown.Name} line {frame.Line}, column {frame.Column}");
    foreach (DebugNamed named in frame.Parameters.Concat(frame.Variables))
    {
        Console.WriteLine($"    {named.Name} {named.DataType} = {named.Value ?? "NULL"}");
    }

    await debugger.ResumeAsync(then);
}

await call;
Console.WriteLine("the CALL completed");
await debugger.DetachAsync();
await target.QueryAsync("ROLLBACK");
```

```
breakpoint 1 at ANN.RAISE_PAY_1 line 4
paused at breakpoint 1: RAISE_PAY line 4, column 3
    PCT INTEGER = 10
    N INTEGER = 0
paused at a step: RAISE_PAY line 5, column 3
    PCT INTEGER = 10
    N INTEGER = 0
the CALL completed
```

The breakpoint names the procedure by its specific name, RAISE_PAY_1
here, as a routine's name may be overloaded; an empty schema is the
session's default. The CALL is rolled back at the end, so the example
changes no salary.

## Stepping

`ResumeAsync` lets a paused target go on: `Into` its next statement,
within a routine the statement invokes too; `Over` to the next statement
of the same frame, or of the one it returns to; `Out` to the next
statement of the frame this one returns to; or `Continue` to a
breakpoint. `InterruptAsync` pauses a running target at its next
statement. A breakpoint may carry a condition over the frame's
parameters and variables, and pauses only where it is true.
`BreakpointsAsync` lists the breakpoints, `ClearAsync` clears one, and
`StackAsync` gives the paused target's frames again.

## When a pause ends by itself

A paused target holds what it holds, its locks among them. It waits for
its debugger at most DEBUGGER.PAUSE_TIMEOUT_MS and then goes on by
itself, its debugger told by an event of kind `TimedOut`. A cancel of
its statement ends a pause too, `Cancelled`. A debugger that detaches,
or whose connection ends, lets its target go on as if never debugged.
A target whose session ends detaches its debugger, which is told
`Ended`.

`WaitAsync` returns null when no event comes in the time given, or when
its token is cancelled first; the connection goes on, and a later wait
takes the event that comes.

## Through ADO.NET

ADO.NET has no debugger, so a `TurborelConnection` gives one as the
driver's extension, its `Debugger`, with the same calls. It runs on the
open connection, while no reader is open on it.

## See also

[Debugging Routines](../client/31-debugging.md) in the API Reference.
