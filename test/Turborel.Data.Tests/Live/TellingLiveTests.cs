// What the server tells beside a statement's replies, against a real
// turboreld (RULES.md R-TEST-8): the rows it changed, the identity values
// it generated, the server's time, the log position of the commit, and
// the conditions it raised, in Complete or as Notices, to a handler or
// kept [CLIENT What the server tells] [PROTOCOL Complete 'K']
// [PROTOCOL Errors and notices].

using System.Collections.Generic;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of what the server tells.</summary>
/// <param name="server">The live server.</param>
public sealed class TellingLiveTests(LiveServer server)
{
    private const string NullEliminated = "SELECT SUM(X) FROM (VALUES (4), (CAST(NULL AS INTEGER))) AS T (X)";

    /// <summary>
    /// A completion tells the rows a statement changed, a query's rows in
    /// all, the identity values an INSERT generated in the order of its rows,
    /// the server's time, and the log position of the commit it made.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Complete 'K']")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task CompletionTellsWhatTheStatementDid()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("TOLD");
        _ = await c.DoneAsync($"CREATE TABLE {t} (ID INTEGER GENERATED ALWAYS AS IDENTITY, N INTEGER)");
        _ = await c.DoneAsync("COMMIT");
        ulong? before = c.CommitPosition;
        Assert.NotNull(before);

        c.Autocommit = true;
        Completed inserted = await c.DoneAsync($"INSERT INTO {t} (N) VALUES (10), (20), (30)");
        Assert.Equal(3UL, inserted.RowCount);
        Assert.Equal(["1", "2", "3"], c.LastGenerated);
        Assert.True(c.CommitPosition > before);
        Assert.Equal(c.CommitPosition, inserted.Position);
        Timing time = Assert.IsType<Timing>(c.ServerTime);
        Assert.True(time.Total >= time.Locks);
        _ = await c.RowsAsync($"SELECT N FROM {t}");
        Assert.Equal(3UL, c.RowsAffected);
        Assert.Empty(c.LastGenerated);
    }

    /// <summary>
    /// With <c>conditions</c> granted, a statement's conditions come in its
    /// Complete, and its warning is told; taken, they are told once.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Complete 'K']")]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task ConditionsComeInTheCompletion()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();

        _ = await c.RowsAsync(NullEliminated);
        Assert.Equal("01003", c.Warning);
        TurborelException condition = Assert.Single(c.TakeConditions());
        Assert.Equal(("01003", "WARNING"), (condition.SqlState, condition.Severity));
        Assert.Empty(c.Conditions);
        Assert.Empty(c.TakeNotices());
        _ = await c.DoneAsync("ROLLBACK");
        Assert.Null(c.Warning);
    }

    /// <summary>
    /// Without <c>conditions</c>, a statement's conditions come as Notices,
    /// given to a handler as they come, or with none the last 64 kept.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[CLIENT What the server tells]")]
    public async Task ConditionsComeAsNoticesOtherwise()
    {
        server.Require();
        Offer offer = Offer.None() with { Capabilities = [Capabilities.Notices, Capabilities.Status, Capabilities.Diagnostics] };
        await using NativeConnection c = await Live.ConnectAsync(new ConnectOptions { Offer = offer });

        for (int i = 0; i < 70; i++)
        {
            _ = await c.RowsAsync(NullEliminated);
        }

        IReadOnlyList<TurborelNotice> kept = c.TakeNotices();
        Assert.Equal(64, kept.Count);
        Assert.All(kept, notice => Assert.Equal(("01003", "WARNING"), (notice.SqlState, notice.Severity)));
        Assert.Empty(c.Conditions);
        var heard = new List<string>();
        c.NoticeHandler = notice => heard.Add(notice.SqlState);
        _ = await c.RowsAsync(NullEliminated);
        Assert.Equal(["01003"], heard);
        Assert.Empty(c.TakeNotices());
        _ = await c.DoneAsync("ROLLBACK");
    }
}
