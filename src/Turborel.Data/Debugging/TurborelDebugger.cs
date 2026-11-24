// The routine debugger *(extension)*, as a program drives it [CLIENT
// Debugging routines]: a connection granted debugger attaches to the
// session of another connection, by that one's ID, sets breakpoints in the
// routines and triggers its user may debug, and drives that session as it
// pauses, step by step, reading its call stack. Each call is
// turborel-client's debug_ one of its name (RULES.md D23). ADO.NET has no
// debugger, so TurborelConnection gives one as the driver's extension, as
// NativeConnection does.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Types;

namespace Turborel.Data.Debugging;

/// <summary>A connection's routine debugger.</summary>
public sealed class TurborelDebugger
{
    private readonly Func<NativeConnection> _connection;

    /// <summary>Initializes a new instance of the <see cref="TurborelDebugger"/> class.</summary>
    /// <param name="connection">Gives the connection, ready for a request.</param>
    internal TurborelDebugger(Func<NativeConnection> connection)
    {
        _connection = connection;
    }

    /// <summary>Gives this connection's ID, as INFORMATION_SCHEMA.CONNECTIONS shows it: what a debugger attaches to its session by.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The ID.</returns>
    /// <exception cref="TurborelException">A connection failure: <c>debugger</c> not granted, or the server gone.</exception>
    public async Task<ulong> IdAsync(CancellationToken cancellationToken = default)
    {
        NativeConnection connection = _connection();
        return await connection.DebuggedAsync(new DebugRequest.Identify(), cancellationToken).ConfigureAwait(false) is DebugAnswer.Connection id
            ? id.Id
            : throw connection.Lose(ConnectionFailure.Of("[DESIGN] the server sent no connection's ID"));
    }

    /// <summary>
    /// Attaches this connection as the debugger of the session of
    /// connection <paramref name="id"/>: one of its own user's, or any if its
    /// user is the database's owner or holds MONITOR.
    /// </summary>
    /// <param name="id">The connection's ID.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08003 if no connection of that ID is served whose session this one may debug; TBD01 if this connection is attached already, the session is its own, or it has a debugger already.</exception>
    public Task AttachAsync(ulong id, CancellationToken cancellationToken = default) =>
        _connection().DebugDoneAsync(new DebugRequest.Attach(id), cancellationToken);

    /// <summary>Detaches this connection: its breakpoints gone, its events not yet read dropped, its target, if paused, going on.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if it is attached to none.</exception>
    public Task DetachAsync(CancellationToken cancellationToken = default) =>
        _connection().DebugDoneAsync(new DebugRequest.Detach(), cancellationToken);

    /// <summary>
    /// Sets a breakpoint at line <paramref name="line"/>, from 1, of the text
    /// of the routine of <paramref name="kind"/> named
    /// <paramref name="routine"/>: its specific name or, if it is the one
    /// routine of that kind so named, its name; or of that trigger. An empty
    /// schema is the session's default. The target pauses at each statement
    /// that begins on that line where <paramref name="condition"/>, a search
    /// condition over the frame's parameters and variables, is true, if given.
    /// </summary>
    /// <param name="kind">What the routine is.</param>
    /// <param name="routine">Its name.</param>
    /// <param name="line">The line.</param>
    /// <param name="condition">The condition, if any.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The breakpoint set, its routine named by its specific name.</returns>
    /// <exception cref="TurborelException">SQLCODE -103 if there is no such routine or line; SQLSTATE 42000, SQLCODE -104, if the user holds no DEBUG on it; TBD01 if this connection is attached to none.</exception>
    public async Task<DebugBreakpoint> BreakAsync(RoutineKind kind, SqlName routine, uint line, string? condition = null, CancellationToken cancellationToken = default)
    {
        NativeConnection connection = _connection();
        return await connection.DebuggedAsync(new DebugRequest.Break(kind, routine, line, condition), cancellationToken).ConfigureAwait(false) is DebugAnswer.Breakpoint set
            ? set.Set
            : throw connection.Lose(ConnectionFailure.Of("[DESIGN] the server sent no breakpoint"));
    }

    /// <summary>Clears the breakpoint <paramref name="number"/>.</summary>
    /// <param name="number">Its number.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if there is none of that number, or this connection is attached to none.</exception>
    public Task ClearAsync(uint number, CancellationToken cancellationToken = default) =>
        _connection().DebugDoneAsync(new DebugRequest.Clear(number), cancellationToken);

    /// <summary>Gives the breakpoints set, in the order they were set.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The breakpoints.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if this connection is attached to none.</exception>
    public async Task<IReadOnlyList<DebugBreakpoint>> BreakpointsAsync(CancellationToken cancellationToken = default)
    {
        NativeConnection connection = _connection();
        return await connection.DebuggedAsync(new DebugRequest.Breakpoints(), cancellationToken).ConfigureAwait(false) is DebugAnswer.Breakpoints all
            ? all.All
            : throw connection.Lose(ConnectionFailure.Of("[DESIGN] the server sent no breakpoints"));
    }

    /// <summary>
    /// Gives the next event of the session this connection debugs not yet
    /// read: one read among other replies and kept, else the next the server
    /// sends, waited for at most <paramref name="within"/>, without limit if
    /// null. Null if none came in time, or if the program cancelled the wait
    /// before one began to arrive; nothing is lost, and the connection goes
    /// on. A notice or a status that comes instead is taken, and gives null.
    /// </summary>
    /// <param name="within">The longest wait; null for no limit.</param>
    /// <param name="cancellationToken">Ends the wait, as its time running out does.</param>
    /// <returns>The event, or null.</returns>
    /// <exception cref="TurborelException">A connection failure: the server gone, or a reply where an event was waited for.</exception>
    public Task<DebugEvent?> WaitAsync(TimeSpan? within = null, CancellationToken cancellationToken = default) =>
        _connection().DebugWaitAsync(within, cancellationToken);

    /// <summary>Gives the target's pause, with its call stack, the outermost frame first; null if it runs.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The pause, or null.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if this connection is attached to none.</exception>
    public async Task<DebugPause?> StackAsync(CancellationToken cancellationToken = default)
    {
        NativeConnection connection = _connection();
        return await connection.DebuggedAsync(new DebugRequest.Stack(), cancellationToken).ConfigureAwait(false) is DebugAnswer.Stack stack
            ? stack.Pause
            : throw connection.Lose(ConnectionFailure.Of("[DESIGN] the server sent no stack"));
    }

    /// <summary>
    /// Tells the paused target to go on as <paramref name="how"/> says: to
    /// the next statement, into a routine it invokes or not, to the next of
    /// the frame it returns to, or to a breakpoint.
    /// </summary>
    /// <param name="how">How it goes on.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if the target is not paused, or this connection is attached to none.</exception>
    public Task ResumeAsync(ResumeAction how, CancellationToken cancellationToken = default) =>
        _connection().DebugDoneAsync(new DebugRequest.Resume(how), cancellationToken);

    /// <summary>Tells the running target to pause at the next statement it runs of a routine the debugger may debug.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE TBD01 if this connection is attached to none.</exception>
    public Task InterruptAsync(CancellationToken cancellationToken = default) =>
        _connection().DebugDoneAsync(new DebugRequest.Interrupt(), cancellationToken);
}
