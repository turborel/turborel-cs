// The Debug request, kind V, of a connection granted debugger
// *(extension)*, by what it asks [PROTOCOL Debug 'V']: identify, attach a
// connection's ID, detach, break, clear, breakpoints, resume, interrupt
// and stack, as turborel-protocol's DebugRequest writes them.

using Turborel.Data.Debugging;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>A Debug request, one of its nine asks.</summary>
internal abstract record DebugRequest : Request
{
    /// <inheritdoc/>
    internal override byte Kind => RequestKinds.Debug;

    /// <summary>Gets what it asks, its first byte.</summary>
    private protected abstract byte What { get; }

    /// <inheritdoc/>
    private protected override void WriteBody(WireWriter w)
    {
        w.WriteU8(What);
        WriteAsk(w);
    }

    /// <summary>Writes what follows what it asks.</summary>
    /// <param name="w">The writer.</param>
    private protected virtual void WriteAsk(WireWriter w)
    {
    }

    /// <summary>0, identify: this connection's ID.</summary>
    internal sealed record Identify : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 0;
    }

    /// <summary>1, attach: this connection made the debugger of the session of connection <paramref name="Connection"/>.</summary>
    /// <param name="Connection">The ID of the connection whose session it debugs.</param>
    internal sealed record Attach(ulong Connection) : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 1;

        /// <inheritdoc/>
        private protected override void WriteAsk(WireWriter w) => w.WriteU64(Connection);
    }

    /// <summary>2, detach.</summary>
    internal sealed record Detach : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 2;
    }

    /// <summary>3, break: a breakpoint at a line of a routine's or a trigger's text.</summary>
    /// <param name="Of">What the routine is.</param>
    /// <param name="Routine">Its specific name, or its name if one routine of that kind has it, or the trigger's; an empty schema is the session's default.</param>
    /// <param name="Line">The line, from 1.</param>
    /// <param name="Condition">The search condition it pauses only where true, if given.</param>
    internal sealed record Break(RoutineKind Of, SqlName Routine, uint Line, string? Condition) : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 3;

        /// <inheritdoc/>
        private protected override void WriteAsk(WireWriter w)
        {
            w.WriteU8((byte)Of);
            w.WriteString(Routine.Schema);
            w.WriteString(Routine.Name);
            w.WriteU32(Line);
            DebugCodec.WriteGiven(w, Condition);
        }
    }

    /// <summary>4, clear: the breakpoint of a number.</summary>
    /// <param name="Number">Its number.</param>
    internal sealed record Clear(uint Number) : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 4;

        /// <inheritdoc/>
        private protected override void WriteAsk(WireWriter w) => w.WriteU32(Number);
    }

    /// <summary>5, breakpoints: every breakpoint set.</summary>
    internal sealed record Breakpoints : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 5;
    }

    /// <summary>6, resume: the paused target told how to go on.</summary>
    /// <param name="How">How it goes on.</param>
    internal sealed record Resume(ResumeAction How) : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 6;

        /// <inheritdoc/>
        private protected override void WriteAsk(WireWriter w) => w.WriteU8((byte)How);
    }

    /// <summary>7, interrupt: the running target told to pause at its next statement.</summary>
    internal sealed record Interrupt : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 7;
    }

    /// <summary>8, stack: the target's pause, if it is paused.</summary>
    internal sealed record Stack : DebugRequest
    {
        /// <inheritdoc/>
        private protected override byte What => 8;
    }
}
