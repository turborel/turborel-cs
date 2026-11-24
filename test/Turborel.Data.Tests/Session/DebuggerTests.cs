// The routine debugger's requests, answers and events against a server of
// the test's own (RULES.md D23) [PROTOCOL Debug 'V'] [PROTOCOL Debugged
// 'V'] [PROTOCOL Event 'W'] [CLIENT Debugging routines]: each request and
// its answer, a refusal that keeps the session, an event kept from among
// a request's replies, a wait that runs out or is cancelled with nothing
// lost, and an answer out of place, which loses the connection.

using System;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Debugging;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of <see cref="TurborelDebugger"/> over the wire.</summary>
public sealed class DebuggerTests
{
    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    private static readonly DebugBreakpoint Set = new(1, new SqlName("ANN", "RAISE_PAY_1"), 4, "PCT > 5");

    private static readonly DebugPause Pause = new(null, [new DebugFrame { Origin = null, Visible = true, Line = 1, Column = 1 }]);

    /// <summary>
    /// Each request is sent and its answer read; a refusal keeps the
    /// session; an event comes after an answer and is waited for, the wait
    /// before it running out with nothing lost; one among a request's
    /// replies is kept, and a detach drops one kept; a notice in an event's
    /// place gives null; a cancelled wait throws OperationCanceledException,
    /// the connection kept and the next frame read whole; an answer of
    /// another kind loses the connection.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[PROTOCOL Debug 'V']")]
    [Verifies("[PROTOCOL Debugged 'V']")]
    [Verifies("[PROTOCOL Event 'W']")]
    [Verifies("[CLIENT Debugging routines]")]
    [Verifies("[DESIGN] D23")]
    public async Task RequestsAnswersAndEvents()
    {
        await using var server = FakeServer.Start(ScriptAsync);
        await using NativeConnection c = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken);
        CancellationToken token = TestContext.Current.CancellationToken;
        TurborelDebugger debugger = c.Debugger;
        Assert.Equal(7UL, await debugger.IdAsync(token));
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => debugger.AttachAsync(9, token));
        Assert.Equal((-910, "TBD01", false), (refused.SqlCode, refused.SqlState, c.IsLost));
        await debugger.AttachAsync(2, token);
        Assert.Equal(Set, await debugger.BreakAsync(RoutineKind.Procedure, new SqlName(string.Empty, "RAISE_PAY"), 4, "PCT > 5", token));
        Assert.Equal(Set, Assert.Single(await debugger.BreakpointsAsync(token)));
        Assert.Null(await debugger.WaitAsync(TimeSpan.FromMilliseconds(100), token));
        Assert.Null(await debugger.StackAsync(token));
        DebugEvent? paused = await debugger.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal((DebugEventKind.Paused, 1U), (paused?.Kind, Assert.Single(paused!.Pause!.Frames).Line));
        await debugger.ResumeAsync(ResumeAction.Over, token);

        _ = await c.QueryAsync("CALL P()", token);
        Assert.Equal(DebugEventKind.TimedOut, (await debugger.WaitAsync(cancellationToken: token))?.Kind);
        Assert.Null(await debugger.WaitAsync(TimeSpan.FromSeconds(5), token));
        Assert.Single(c.TakeNotices());
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
        {
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => debugger.WaitAsync(cancellationToken: cancel.Token));
            Assert.False(c.IsLost);
        }

        await debugger.InterruptAsync(token);
        await debugger.ClearAsync(1, token);
        _ = await c.QueryAsync("CALL P()", token);
        await debugger.DetachAsync(token);
        Assert.Null(await debugger.WaitAsync(TimeSpan.FromMilliseconds(10), token));
        TurborelException lost = await Assert.ThrowsAsync<TurborelException>(() => debugger.IdAsync(token));
        Assert.Equal(("08006", true), (lost.SqlState, c.IsLost));
        await server.PlayedAsync();
    }

    /// <summary>
    /// Without <c>debugger</c> granted a request fails before it is sent;
    /// an Error in an event's place is its failure; a reply of a request in
    /// an event's place loses the connection.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[PROTOCOL Event 'W']")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Debugging routines]")]
    public async Task AWaitTakesEventsAlone()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            await peer.WriteAsync('E', Replies.Error(-910, "TBD01"));
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick, TestContext.Current.CancellationToken);
        CancellationToken token = TestContext.Current.CancellationToken;
        Assert.Equal("08006", (await Assert.ThrowsAsync<TurborelException>(() => c.Debugger.IdAsync(token))).SqlState);
        Assert.False(c.IsLost);
        Assert.Equal("TBD01", (await Assert.ThrowsAsync<TurborelException>(() => c.Debugger.WaitAsync(TimeSpan.FromSeconds(5), token))).SqlState);
        Assert.Equal("08006", (await Assert.ThrowsAsync<TurborelException>(() => c.Debugger.WaitAsync(TimeSpan.FromSeconds(5), token))).SqlState);
        Assert.True(c.IsLost);
        await server.PlayedAsync();
    }

    /// <summary>A TurborelConnection's debugger runs on its open connection, refused while a reader is open, TB910.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Debugging routines]")]
    [Verifies("[DESIGN] D23")]
    public async Task AConnectionsDebuggerWaitsForItsReader()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Debugger, Capabilities.FetchEnds);
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('B', Replies.Rows(1));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Asked(new DebugRequest.Identify()), await peer.ExpectAsync(RequestKinds.Debug));
            await peer.WriteAsync('V', Answer(new DebugAnswer.Connection(3)));
            _ = await peer.ExpectAsync(RequestKinds.Terminate);
        });
        await using var connection = new TurborelConnection(Ado.AdoServer.Of(server.Port));
        CancellationToken token = TestContext.Current.CancellationToken;
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => connection.Debugger.IdAsync(token))).SqlState);
        await connection.OpenAsync(token);
        using var select = new TurborelCommand("SELECT N FROM T", connection);
        await using (TurborelDataReader reader = await select.ExecuteReaderAsync(cancellationToken: token))
        {
            Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => connection.Debugger.IdAsync(token))).SqlState);
        }

        Assert.Equal(3UL, await connection.Debugger.IdAsync(token));
        await connection.CloseAsync();
        await server.PlayedAsync();
    }

    // A Debug request's body, as the client encodes it.
    private static byte[] Asked(DebugRequest request) => Wire.Encode(request)[5..];

    private static byte[] Answer(DebugAnswer answer) => Wire.Written(w => DebugCodec.WriteAnswer(w, answer));

    private static byte[] Told(DebugEvent told) => Wire.Written(w => DebugCodec.WriteEvent(w, told));

    // The server's side of the first test.
    private static async Task ScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync(Capabilities.Debugger);
        Assert.Equal(Asked(new DebugRequest.Identify()), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('V', Answer(new DebugAnswer.Connection(7)));
        Assert.Equal(Asked(new DebugRequest.Attach(9)), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('E', Replies.Error(-910, "TBD01"));
        Assert.Equal(Asked(new DebugRequest.Attach(2)), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Asked(new DebugRequest.Break(RoutineKind.Procedure, new SqlName(string.Empty, "RAISE_PAY"), 4, "PCT > 5")), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('V', Answer(new DebugAnswer.Breakpoint(Set)));
        Assert.Equal(Asked(new DebugRequest.Breakpoints()), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('V', Answer(new DebugAnswer.Breakpoints([Set])));
        Assert.Equal(Asked(new DebugRequest.Stack()), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('V', Answer(new DebugAnswer.Stack(null)));
        await peer.WriteAsync('W', Told(new DebugEvent(DebugEventKind.Paused, Pause)));
        Assert.Equal(Asked(new DebugRequest.Resume(ResumeAction.Over)), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('W', Told(new DebugEvent(DebugEventKind.TimedOut, null)));
        await peer.WriteAsync('K', Replies.Complete());
        await peer.WriteAsync('N', Replies.Notice("01000", "a notice"));
        Assert.Equal(Asked(new DebugRequest.Interrupt()), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Asked(new DebugRequest.Clear(1)), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('W', Told(new DebugEvent(DebugEventKind.Cancelled, null)));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Asked(new DebugRequest.Detach()), await peer.ExpectAsync(RequestKinds.Debug));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Debug);
        await peer.WriteAsync('V', Answer(new DebugAnswer.Stack(null)));
    }
}
