// The routine debugger against a real turboreld (RULES.md R-TEST-8, D23)
// [CLIENT Debugging routines]: one connection attaches to another's
// session, sets a breakpoint, hears the pause, reads the frames, steps
// over and continues, interrupts, and detaches; the refusals keep the
// session: an ID not seen, 08003; an action the state does not allow,
// TBD01; a breakpoint in a routine without DEBUG, 42000 and -104.

using System;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Debugging;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of the routine debugger.</summary>
/// <param name="server">The live server.</param>
public sealed class DebuggerLiveTests(LiveServer server)
{
    private static readonly TimeSpan Comes = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Refusals keep the session: attached to none, an action, TBD01; an ID
    /// not seen, 08003 and -919; its own session, TBD01; attached, a second
    /// attach, a resume of a target not paused, a clear of no breakpoint,
    /// TBD01; a breakpoint at no line, 42000 and -103, or of no such
    /// routine, -103.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Debugging routines]")]
    [Verifies("[PROTOCOL Debug 'V']")]
    [Verifies("[PROTOCOL Capabilities]")]
    public async Task RefusalsKeepTheSession()
    {
        server.Require();
        await using NativeConnection target = await Live.ConnectAsync();
        await using NativeConnection debugging = await Live.ConnectAsync();
        string p = await DefinedAsync(target);
        TurborelDebugger debugger = debugging.Debugger;
        Assert.True(debugging.Has(Capabilities.Debugger));
        TurborelException none = await Assert.ThrowsAsync<TurborelException>(() => debugger.StackAsync(TestContext.Current.CancellationToken));
        Assert.Equal((-910, "TBD01"), (none.SqlCode, none.SqlState));
        TurborelException unseen = await Assert.ThrowsAsync<TurborelException>(() => debugger.AttachAsync(ulong.MaxValue, TestContext.Current.CancellationToken));
        Assert.Equal((-919, "08003"), (unseen.SqlCode, unseen.SqlState));
        ulong own = await debugger.IdAsync(TestContext.Current.CancellationToken);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => debugger.AttachAsync(own, TestContext.Current.CancellationToken))).SqlState);
        ulong id = await target.Debugger.IdAsync(TestContext.Current.CancellationToken);
        await debugger.AttachAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => debugger.AttachAsync(id, TestContext.Current.CancellationToken))).SqlState);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => debugger.ResumeAsync(ResumeAction.Into, TestContext.Current.CancellationToken))).SqlState);
        TurborelException nowhere = await Assert.ThrowsAsync<TurborelException>(() => debugger.BreakAsync(RoutineKind.Procedure, new SqlName(string.Empty, p), 99, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((-103, "42000"), (nowhere.SqlCode, nowhere.SqlState));
        Assert.Equal(-103, (await Assert.ThrowsAsync<TurborelException>(() => debugger.BreakAsync(RoutineKind.Function, new SqlName(string.Empty, p), 4, cancellationToken: TestContext.Current.CancellationToken))).SqlCode);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => debugger.ClearAsync(99, TestContext.Current.CancellationToken))).SqlState);
        Assert.False(debugging.IsLost);
        await debugger.DetachAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Over two connections, the debugger sets a breakpoint by line, hears
    /// its target pause there with the frame's place and values, reads the
    /// stack again, steps over to the next line, and continues, the target's
    /// CALL then completing; a breakpoint whose condition is false pauses
    /// nothing; an interrupt pauses at the next statement; detached, it is
    /// attached to none.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Debugging routines]")]
    [Verifies("[PROTOCOL Debug 'V']")]
    [Verifies("[PROTOCOL Debugged 'V']")]
    [Verifies("[PROTOCOL Event 'W']")]
    public async Task ADebuggerDrivesAnotherConnectionsProcedure()
    {
        server.Require();
        await using NativeConnection target = await Live.ConnectAsync();
        await using NativeConnection debugging = await Live.ConnectAsync();
        string p = await DefinedAsync(target);
        TurborelDebugger debugger = debugging.Debugger;
        await debugger.AttachAsync(await target.Debugger.IdAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        DebugBreakpoint set = await debugger.BreakAsync(RoutineKind.Procedure, new SqlName(string.Empty, p), 4, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(("ANN", 4U, (string?)null), (set.Routine.Schema, set.Line, set.Condition));
        target.Autocommit = true;
        Task<Outcome> call = target.QueryAsync($"CALL {p} (3)", TestContext.Current.CancellationToken);
        DebugPause at = await PausedAsync(debugger);
        DebugFrame frame = at.Frames[^1];
        Assert.Equal((set.Number, true, 4U), (at.Breakpoint, frame.Visible, frame.Line));
        Assert.Equal((RoutineKind.Procedure, new SqlName("ANN", p)), (frame.Origin?.Kind, frame.Origin?.Shown));
        Assert.Equal(new DebugNamed("X", "INTEGER", new TurborelDecimal(3, 0)), Assert.Single(frame.Parameters));
        Assert.Equal(new DebugNamed("Y", "INTEGER", new TurborelDecimal(0, 0)), Assert.Single(frame.Variables));
        Assert.Equal(at.Frames.Count, (await debugger.StackAsync(TestContext.Current.CancellationToken))?.Frames.Count);

        await debugger.ResumeAsync(ResumeAction.Over, TestContext.Current.CancellationToken);
        DebugPause stepped = await PausedAsync(debugger);
        Assert.Equal(((uint?)null, 5U), (stepped.Breakpoint, stepped.Frames[^1].Line));
        Assert.Equal(new TurborelDecimal(6, 0), Assert.Single(stepped.Frames[^1].Variables).Value);
        await debugger.ResumeAsync(ResumeAction.Continue, TestContext.Current.CancellationToken);
        Assert.IsType<DoneOutcome>(await call);

        await debugger.ClearAsync(set.Number, TestContext.Current.CancellationToken);
        _ = await debugger.BreakAsync(RoutineKind.Procedure, new SqlName("ANN", p), 4, "X > 5", TestContext.Current.CancellationToken);
        Assert.IsType<DoneOutcome>(await target.QueryAsync($"CALL {p} (4)", TestContext.Current.CancellationToken));
        Assert.Null(await debugger.WaitAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        Assert.Single(await debugger.BreakpointsAsync(TestContext.Current.CancellationToken));

        await debugger.InterruptAsync(TestContext.Current.CancellationToken);
        call = target.QueryAsync($"CALL {p} (5)", TestContext.Current.CancellationToken);
        Assert.Null((await PausedAsync(debugger)).Breakpoint);
        await debugger.ResumeAsync(ResumeAction.Continue, TestContext.Current.CancellationToken);
        Assert.IsType<DoneOutcome>(await call);
        await debugger.DetachAsync(TestContext.Current.CancellationToken);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => debugger.StackAsync(TestContext.Current.CancellationToken))).SqlState);
    }

    /// <summary>
    /// A user attached to its own other connection may set no breakpoint in
    /// a routine it neither owns nor holds DEBUG on, 42000 and -104, the
    /// session kept; granted DEBUG, it may.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Debugging routines]")]
    [Verifies("[PROTOCOL Debug 'V']")]
    public async Task ABreakpointNeedsDebugOnItsRoutine()
    {
        server.Require();
        await using NativeConnection ann = await Live.ConnectAsync();
        string p = await DefinedAsync(ann);
        string bob = Live.Table("BOB");
        _ = await ann.DoneAsync($"CREATE USER {bob} PASSWORD 'bob''s password'");
        _ = await ann.DoneAsync("COMMIT");
        await using NativeConnection debugging = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", LiveServer.Port), bob, new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        await using NativeConnection target = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", LiveServer.Port), bob, new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        await debugging.Debugger.AttachAsync(await target.Debugger.IdAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => debugging.Debugger.BreakAsync(RoutineKind.Procedure, new SqlName("ANN", p), 4, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal((-104, "42000", false), (refused.SqlCode, refused.SqlState, debugging.IsLost));
        _ = await ann.DoneAsync($"GRANT DEBUG ON PROCEDURE {p} TO {bob}");
        _ = await ann.DoneAsync("COMMIT");
        Assert.Equal(4U, (await debugging.Debugger.BreakAsync(RoutineKind.Procedure, new SqlName("ANN", p), 4, cancellationToken: TestContext.Current.CancellationToken)).Line);
        await debugging.Debugger.DetachAsync(TestContext.Current.CancellationToken);
    }

    // A table and a procedure of ANN's, the procedure's lines numbered from
    // its CREATE's: line 4 sets Y, line 5 inserts it; the procedure's name.
    private static async Task<string> DefinedAsync(NativeConnection ann)
    {
        string t = Live.Table("DEBUGGED");
        string p = Live.Table("DOUBLE_IT");
        _ = await ann.DoneAsync($"CREATE TABLE {t} (K INTEGER)");
        _ = await ann.DoneAsync(
            $"CREATE PROCEDURE {p} (IN X INTEGER) MODIFIES SQL DATA\nBEGIN\n  DECLARE Y INTEGER DEFAULT 0;\n  SET Y = X * 2;\n  INSERT INTO {t} VALUES (Y);\nEND");
        _ = await ann.DoneAsync("COMMIT");
        return p;
    }

    // The next pause the debugger hears of.
    private static async Task<DebugPause> PausedAsync(TurborelDebugger debugger)
    {
        DebugEvent? told = await debugger.WaitAsync(Comes, TestContext.Current.CancellationToken);
        Assert.Equal(DebugEventKind.Paused, told?.Kind);
        return told!.Pause!;
    }
}
