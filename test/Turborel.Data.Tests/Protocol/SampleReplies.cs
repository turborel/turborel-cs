// What each server sample frame other than a value's or a column type's
// means, checked of the reply it decodes to [PROTOCOL Sample frames].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Turborel.Data.Debugging;
using Turborel.Data.Protocol;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>The meanings of the server's sample frames of replies.</summary>
internal static class SampleReplies
{
    private static readonly ExactType Integer = new(10, 0);

    private static readonly DebugBreakpoint RaisePay = new(1, new SqlName("ANN", "RAISE_PAY"), 4, null);

    /// <summary>Each reply frame's meaning, checked.</summary>
    private static readonly Dictionary<string, Action<Reply>> Replies = new()
    {
        ["challenge"] = r => Assert.Equal("r=abcdef,s=c2FsdA==,i=4096", Encoding.ASCII.GetString(Assert.IsType<Reply.Challenge>(r).Bytes.Span)),
        ["ready"] = r => Ready(Assert.IsType<Reply.Ready>(r)),
        ["prepared"] = r => Prepared(Assert.IsType<Reply.Prepared>(r)),
        ["prepared-empty"] = r => Assert.Equal((2U, 0, 0), (Assert.IsType<Reply.Prepared>(r).Statement, ((Reply.Prepared)r).Parameters.Count, ((Reply.Prepared)r).Columns.Count)),
        ["prepared-declared"] = r => Assert.Equal(("INTEGER", "INTEGER"), (Assert.Single(Assert.IsType<Reply.Prepared>(r).Columns).Declared?.Name, ((Reply.Prepared)r).Columns[0].TypeWritten)),
        ["prepared-called"] = r => Called(Assert.IsType<Reply.Prepared>(r)),
        ["debugged-connection"] = r => Assert.Equal(2UL, Assert.IsType<DebugAnswer.Connection>(Debugged(r)).Id),
        ["debugged-breakpoint"] = r => Assert.Equal(RaisePay, Assert.IsType<DebugAnswer.Breakpoint>(Debugged(r)).Set),
        ["debugged-breakpoints"] = r => Assert.Equal(RaisePay, Assert.Single(Assert.IsType<DebugAnswer.Breakpoints>(Debugged(r)).All)),
        ["debugged-running"] = r => Assert.Null(Assert.IsType<DebugAnswer.Stack>(Debugged(r)).Pause),
        ["debugged-stack"] = r => Paused(Assert.IsType<DebugAnswer.Stack>(Debugged(r)).Pause),
        ["event-paused"] = r => Paused(Told(r, DebugEventKind.Paused).Pause),
        ["event-timed-out"] = r => Assert.Null(Told(r, DebugEventKind.TimedOut).Pause),
        ["event-cancelled"] = r => Assert.Null(Told(r, DebugEventKind.Cancelled).Pause),
        ["event-ended"] = r => Assert.Null(Told(r, DebugEventKind.Ended).Pause),
        ["description"] = r => Assert.Equal(["NUMERIC(10,0)", "CHARACTER VARYING(5)"], Assert.IsType<Reply.Description>(r).Columns.Select(c => c.TypeWritten)),
        ["description-empty"] = r => Assert.Empty(Assert.IsType<Reply.Description>(r).Columns),
        ["description-declared"] = r => Declared(Assert.IsType<Reply.Description>(r)),
        ["rows"] = r => Assert.Equal([[new TurborelDecimal(1, 0), "a"], [null, string.Empty]], Assert.IsType<Reply.Rows>(r).Batch),
        ["rows-none"] = r => Assert.Empty(Assert.IsType<Reply.Rows>(r).Batch),
        ["rows-empty-row"] = r => Assert.Empty(Assert.Single(Assert.IsType<Reply.Rows>(r).Batch)),
        ["suspended"] = r => Assert.IsType<Reply.Suspended>(r),
        ["complete"] = r => Assert.Equal((0, "00000", false), Head(r)),
        ["complete-active"] = r => Assert.Equal((0, "00000", true), Head(r)),
        ["complete-no-data"] = r => Assert.Equal((100, "02000", false), Head(r)),
        ["complete-position"] = r => Assert.Equal(4711UL, Completion(r).Position),
        ["complete-more"] = r => Assert.True(Completion(r).More),
        ["complete-rows"] = r => Assert.Equal(1UL, Completion(r).Rows),
        ["complete-every"] = r => Every(Completion(r)),
        ["complete-conditions"] = r => Assert.Equal(("01004", "01004", true), (Completion(r).SqlState, Assert.Single(Completion(r).Conditions).SqlState, Completion(r).Conditions[0].Active)),
        ["error"] = r => Assert.Equal((-803, "23505", false, null, null), Failed(r)),
        ["error-cited"] = r => Assert.Equal((-803, "23505", true, "ISO/IEC 9075-2:2023 §11.7", 12UL), Failed(r)),
        ["error-diagnostics"] = r => Diagnosed(Assert.IsType<Reply.Error>(r).Failure.Diagnostics!),
        ["error-connection"] = r => Assert.Equal((-919, "08006"), (Assert.IsType<Reply.Error>(r).Failure.SqlCode, ((Reply.Error)r).Failure.SqlState)),
        ["lob-data"] = r => Assert.Equal("aé", Encoding.UTF8.GetString(Assert.IsType<Reply.LobData>(r).Bytes.Span)),
        ["lob-data-empty"] = r => Assert.Equal(0, Assert.IsType<Reply.LobData>(r).Bytes.Length),
        ["lob-locator"] = r => Assert.Equal(4U, Assert.IsType<Reply.LobLocator>(r).Locator),
        ["notice"] = r => Assert.Equal(("01004", null), (Assert.IsType<Reply.Notice>(r).SqlState, ((Reply.Notice)r).Diagnostics?.Severity)),
        ["notice-diagnostics"] = r => Assert.Equal("WARNING", Assert.IsType<Reply.Notice>(r).Diagnostics?.Severity),
        ["pong"] = r => Assert.IsType<Reply.Pong>(r),
        ["status-server-version"] = r => Status(r, "SERVER_VERSION", "turborel 0.0.0 (alpha)"),
        ["status-authorization"] = r => Status(r, "AUTHORIZATION", "ANN"),
        ["status-schema"] = r => Status(r, "SCHEMA", "ANN"),
        ["status-read-only"] = r => Status(r, "READ_ONLY", "NO"),
        ["status-server-read-only"] = r => Status(r, "SERVER_READ_ONLY", "YES"),
        ["status-catalog"] = r => Status(r, "CATALOG", "TURBOREL"),
        ["status-owner"] = r => Status(r, "OWNER", "YES"),
        ["status-time-zone"] = r => Status(r, "SESSION.TIME_ZONE", "+05:30"),
        ["status-null"] = r => Status(r, "STATEMENT.TIMEOUT_MS", string.Empty),
    };

    /// <summary>Checks the reply a server frame of <paramref name="name"/> decoded to.</summary>
    /// <param name="name">The frame's name.</param>
    /// <param name="reply">The reply.</param>
    internal static void Check(string name, Reply reply) => Replies[name](reply);

    private static void Ready(Reply.Ready ready)
    {
        Assert.Equal(("ANN", "turborel 0.0.0 (alpha)"), (ready.Authorization, ready.Server));
        Assert.Equal(Enumerable.Repeat((byte)0xA5, 16), ready.Key.ToArray());
        Assert.Equal(["ping"], ready.Capabilities);
    }

    private static void Prepared(Reply.Prepared prepared)
    {
        Assert.Equal(1U, prepared.Statement);
        Assert.True(prepared.Parameters[0] is { Varying: true, Type: CharacterType { Varying: true } });
        Assert.Equal(new Parameter(false, Integer), prepared.Parameters[1]);
        Assert.Equal([new Column("K", Integer, true), new Column(null, new CharacterType(5, CharacterUnits.Characters, true), false)], prepared.Columns);
    }

    private static DebugAnswer Debugged(Reply r) => Assert.IsType<Reply.Debugged>(r).Answer;

    private static DebugEvent Told(Reply r, DebugEventKind kind)
    {
        DebugEvent told = Assert.IsType<Reply.Event>(r).Told;
        Assert.Equal(kind, told.Kind);
        return told;
    }

    // A pause at breakpoint 1, one frame of ANN.RAISE_PAY at line 4,
    // column 3, PCT 10, N null, cursor C closed.
    private static void Paused(DebugPause? pause)
    {
        Assert.Equal(1U, pause?.Breakpoint);
        DebugFrame frame = Assert.Single(pause!.Frames);
        Assert.Equal(new DebugOrigin(RoutineKind.Procedure, new SqlName("ANN", "RAISE_PAY"), new SqlName("ANN", "RAISE_PAY")), frame.Origin);
        Assert.Equal((true, 1UL, 4U, 3U), (frame.Visible, frame.Place, frame.Line, frame.Column));
        Assert.Equal(new DebugNamed("PCT", "INTEGER", new TurborelDecimal(10, 0)), Assert.Single(frame.Parameters));
        Assert.Equal(new DebugNamed("N", "INTEGER", null), Assert.Single(frame.Variables));
        Assert.Equal(new DebugCursor("C", false, "SELECT ID FROM STAFF"), Assert.Single(frame.Cursors));
    }

    // Statement 3 of CALL S.P(?, ?): its input, its output column, and tail
    // 20 of S.P, IN of parameter 1 and OUT of parameter 2.
    private static void Called(Reply.Prepared prepared)
    {
        Assert.Equal((3U, new Parameter(false, Integer)), (prepared.Statement, Assert.Single(prepared.Parameters)));
        Assert.Equal(new Column("G", Integer, true), Assert.Single(prepared.Columns));
        Assert.Equal(new SqlName("S", "P"), prepared.Called?.Procedure);
        Assert.Equal(
            [new DynamicParameter(ParameterMode.In, 1, new Parameter(false, Integer)), new DynamicParameter(ParameterMode.Out, 2, new Parameter(false, Integer))],
            prepared.Called?.Parameters ?? []);
    }

    private static void Declared(Reply.Description description)
    {
        Assert.Equal(["INTEGER", "SMALLINT", "BIGINT", "DECIMAL", "REAL", "DOUBLE PRECISION", "USER-DEFINED"], description.Columns.Select(c => c.Declared?.Name));
        Assert.Equal(["I", "S", "B", "D", "R", "P", "M"], description.Columns.Select(c => c.Name));
        Assert.Equal(("DECIMAL(9,2)", "S.MONEY"), (description.Columns[3].TypeWritten, description.Columns[6].TypeWritten));
    }

    private static void Every(Completion c)
    {
        Assert.Equal((4711UL, true, 3UL), (c.Position, c.More, c.Rows));
        Assert.Equal(["7", "8"], c.Generated);
        Assert.Equal(new Timing(1500, 20, 300), c.Time);
    }

    private static void Diagnosed(Diagnostics d)
    {
        Assert.Equal(("ERROR", "S", "T", "K", "INTEGER", "T_PK"), (d.Severity, d.Schema, d.Table, d.Column, d.DataType, d.Constraint));
        Assert.NotNull(d.Detail);
        Assert.NotNull(d.Hint);
        Assert.NotNull(d.Context);
    }

    private static Completion Completion(Reply r) => Assert.IsType<Reply.Complete>(r).Completion;

    private static (int, string, bool) Head(Reply r) => (Completion(r).SqlCode, Completion(r).SqlState, Completion(r).Active);

    private static (int, string, bool, string?, ulong?) Failed(Reply r)
    {
        Failure f = Assert.IsType<Reply.Error>(r).Failure;
        return (f.SqlCode, f.SqlState, f.Active, f.Rule, f.Position);
    }

    private static void Status(Reply r, string name, string value) =>
        Assert.Equal((name, value), (Assert.IsType<Reply.Status>(r).Name, ((Reply.Status)r).Value));
}
