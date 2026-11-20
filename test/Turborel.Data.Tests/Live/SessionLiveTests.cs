// The session's control against a real turboreld (RULES.md R-TEST-8):
// ping, reset and change of user, the transaction either rolls back told
// by notice 01T01, attributes each statement carries, and a method of a
// capability not granted failing before it sends anything
// [CLIENT Controlling the session] [CLIENT What a connection asks for].

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of the session's control.</summary>
/// <param name="server">The live server.</param>
public sealed class SessionLiveTests(LiveServer server)
{
    /// <summary>
    /// Ping is answered at any point after Ready; Reset rolls the transaction
    /// back, told by notice 01T01, its settings back at the database's, its
    /// prepared statements gone, the connection and its user kept.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Ping 'G']")]
    [Verifies("[PROTOCOL Reset 'W']")]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[PROTOCOL Conversations]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task PingAndReset()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        await c.PingAsync(TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("SET SESSION.TIME_ZONE = '+03:00'");
        PreparedStatement prepared = await c.PrepareAsync("VALUES 1", TestContext.Current.CancellationToken);
        _ = await c.RowsAsync("VALUES 1");
        Assert.True(c.InTransaction);
        await c.PingAsync(TestContext.Current.CancellationToken);

        await c.ResetAsync(TestContext.Current.CancellationToken);
        Assert.False(c.InTransaction);
        TurborelNotice notice = Assert.Single(c.TakeNotices());
        Assert.Equal(("01T01", "NOTICE"), (notice.SqlState, notice.Severity));
        Assert.Equal("+00:00", c.Setting("SESSION.TIME_ZONE"));
        Assert.Equal(LiveServer.Owner, c.Authorization);
        Assert.Equal("26000", (await Assert.ThrowsAsync<TurborelException>(() => c.ExecuteAsync(prepared, [], TestContext.Current.CancellationToken))).SqlState);
        await c.ResetAsync(TestContext.Current.CancellationToken);
        Assert.Empty(c.TakeNotices());
    }

    /// <summary>Ping is answered during a copy too, which takes no other request until it ends.</summary>
    [Fact]
    [Verifies("[PROTOCOL Ping 'G']")]
    public async Task PingIsAnsweredDuringACopy()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("COPIED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER)");

        await c.SendAsync(new Request.CopyIn(t, [], Ends: false), TestContext.Current.CancellationToken);
        await c.PingAsync(TestContext.Current.CancellationToken);
        await c.SendAsync(new Request.CopyEnd(Abandon: true), TestContext.Current.CancellationToken);
        Assert.False(c.IsLost);
        _ = await c.ReceiveAsync(TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// ChangeUser logs in again as another user, the session renewed as by
    /// Reset, no transaction active after it; a refusal ends the connection.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL ChangeUser 'U']")]
    [Verifies("[PROTOCOL Errors and notices]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task ChangeUserRenewsTheSession()
    {
        server.Require();
        string user = Live.Table("BOB");
        await using NativeConnection c = await Live.ConnectAsync();
        _ = await c.DoneAsync($"CREATE USER {user} PASSWORD 'bob''s password'");
        _ = await c.DoneAsync("COMMIT");
        _ = await c.DoneAsync("SET SESSION.TIME_ZONE = '+03:00'");
        _ = await c.RowsAsync("VALUES 1");
        Assert.True(c.InTransaction);

        await c.ChangeUserAsync(user, new TrustMechanism(), TestContext.Current.CancellationToken);
        Assert.Equal(user, c.Authorization);
        Assert.False(c.InTransaction);
        Assert.Equal("01T01", Assert.Single(c.TakeNotices()).SqlState);
        Assert.Equal((user, "NO"), (c.Setting(StatusNames.Authorization), c.Setting(StatusNames.Owner)));
        Assert.Equal("+00:00", c.Setting("SESSION.TIME_ZONE"));

        await using NativeConnection local = await NativeConnection.ConnectAsync(new LocalTarget(server.Socket), LiveServer.Owner, new TrustMechanism(), cancellationToken: TestContext.Current.CancellationToken);
        await local.ChangeUserAsync(user, new PasswordMechanism("bob's password"), TestContext.Current.CancellationToken);
        Assert.Equal(user, local.Authorization);
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => local.ChangeUserAsync(LiveServer.Owner, new PasswordMechanism("wrong"), TestContext.Current.CancellationToken));
        Assert.Equal("28000", refused.SqlState);
        Assert.True(local.IsLost);
    }

    /// <summary>Attributes set once are carried by every later statement, read by QUERY_ATTRIBUTE, until set again.</summary>
    [Fact]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Controlling the session]")]
    public async Task QueryAttributesAreCarried()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        c.SetQueryAttributes([new("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]);
        const string Read = "VALUES (QUERY_ATTRIBUTE('traceparent'))";

        Assert.Equal("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01", Live.Texts(Assert.Single(await c.RowsAsync(Read)))[0]);
        PreparedStatement prepared = await c.PrepareAsync(Read, TestContext.Current.CancellationToken);
        Assert.NotNull(Live.Texts(Assert.Single(Assert.IsType<RowsOutcome>(await c.ExecuteAsync(prepared, [], TestContext.Current.CancellationToken)).Rows))[0]);
        c.SetQueryAttributes([]);
        Assert.Null(Live.Texts(Assert.Single(await c.RowsAsync(Read)))[0]);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A connection granted nothing fails each method of a capability before
    /// it sends anything, and is told nothing the capabilities would add.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT What a connection asks for]")]
    public async Task NothingGrantedNothingSent()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync(new ConnectOptions { Offer = Offer.None() });
        PreparedStatement prepared = await c.PrepareAsync("VALUES 1", TestContext.Current.CancellationToken);
        var refusals = new List<Func<Task>>
        {
            () => c.PingAsync(),
            () => c.ResetAsync(),
            () => c.ChangeUserAsync("x", new TrustMechanism()),
            () => c.QueryManyAsync("VALUES 1"),
            () => c.DescribeAsync(prepared),
            () => c.DescribeCursorAsync(prepared),
            () => c.OpenCursorAsync(prepared, [], 1, cursor: 1),
            () => c.FetchAsync(prepared, 1, cursor: 1),
            () => c.CloseCursorAsync(prepared, cursor: 1),
            () => Task.Run(() => c.SetQueryAttributes([new("k", "v")])),
        };

        foreach (Func<Task> refused in refusals)
        {
            TurborelException failure = await Assert.ThrowsAsync<TurborelException>(refused);
            Assert.Contains("did not grant", failure.Message, StringComparison.Ordinal);
        }

        Assert.False(c.IsLost);
        Assert.Equal("4", Live.Texts(Assert.Single(await c.RowsAsync("SELECT SUM(X) FROM (VALUES (4), (CAST(NULL AS INTEGER))) AS T (X)")))[0]);
        Assert.Equal("01003", c.Warning);
        Assert.Empty(c.Conditions);
        Assert.Empty(c.TakeNotices());
        Assert.Null(c.RowsAffected);
        Assert.Null(c.ServerTime);
        TurborelException missing = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT * FROM NOWHERE_AT_ALL", TestContext.Current.CancellationToken));
        Assert.Null(missing.Severity);
    }
}
