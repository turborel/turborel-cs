// A pool of connections against a server of the test's own: connections
// made up to max, handed out idle and checked, waited for and given up on,
// given back reset, closed past their idle time, lifetime or the idle
// kept, and of a server that grants neither ping nor reset checked and
// reset by statements [CLIENT Pools].

using System;
using System.Linq;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Pooling;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Pooling;

/// <summary>Tests of <see cref="ConnectionPool"/>.</summary>
public sealed class PoolTests
{
    /// <summary>
    /// A pool makes connections while fewer than max are made, hands out an
    /// idle one, checked by Ping, given back by Reset then the program's
    /// statements; a borrower past max waits, and gives up within wait,
    /// SQLSTATE 08001.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pools]")]
    public async Task MadeHandedOutGivenBackAndWaitedFor()
    {
        await using var server = ManyServer.Start(Capabilities.Ping, Capabilities.Reset);
        var config = new PoolConfig { Max = 2, Wait = TimeSpan.FromMilliseconds(300), Reset = ["SET SCHEMA 'S'"] };
        await using var pool = new ConnectionPool(token => ConnectAsync(server), config);

        PooledConnection first = await pool.GetAsync(TestContext.Current.CancellationToken);
        PooledConnection second = await pool.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal((2, 0), pool.Counts);
        TurborelException waited = await Assert.ThrowsAsync<TurborelException>(() => pool.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal((-919, "08001"), (waited.SqlCode, waited.SqlState));
        first.Connection.Autocommit = true;
        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => first.Connection).SqlState);
        Assert.Equal((2, 1), pool.Counts);
        PooledConnection again = await pool.GetAsync(TestContext.Current.CancellationToken);
        Assert.False(again.Connection.Autocommit);
        Assert.Equal(2, server.Connections);
        Task<PooledConnection> waiting = pool.GetAsync(TestContext.Current.CancellationToken);
        await second.DisposeAsync();
        await using PooledConnection handed = await waiting;
        await again.DisposeAsync();
        Assert.Equal(["W", "SET SCHEMA 'S'", "G", "W", "SET SCHEMA 'S'", "G", "W", "SET SCHEMA 'S'"], [.. server.Requests]);
    }

    /// <summary>
    /// A program's source that fails its own way gives up its place; a wait
    /// the program cancels fails as the driver's error; a wait longer than
    /// any can be timed by is taken.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pools]")]
    public async Task FailuresGiveTheirPlaceUp()
    {
        await using var server = ManyServer.Start(Capabilities.Ping, Capabilities.Reset);
        int calls = 0;
        var config = new PoolConfig { Max = 1, Wait = TimeSpan.MaxValue };
        await using var pool = new ConnectionPool(token => ++calls == 1 ? throw new InvalidOperationException("the program's source failed") : ConnectAsync(server), config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pool.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal((0, 0), pool.Counts);
        await using PooledConnection held = await pool.GetAsync(TestContext.Current.CancellationToken);
        using var cancel = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        TurborelException cancelled = await Assert.ThrowsAsync<TurborelException>(() => pool.GetAsync(cancel.Token));
        Assert.Equal("08006", cancelled.SqlState);
    }

    /// <summary>
    /// One idle past idle_timeout, past its lifetime, or failing its check is
    /// closed as get meets it, and another made; beyond max_idle, one given
    /// back is closed; a failure making one gives its place up.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pools]")]
    public async Task StaleOnesAreClosed()
    {
        await using var server = ManyServer.Start(Capabilities.Ping, Capabilities.Reset);
        var config = new PoolConfig { MaxIdle = 1, IdleTimeout = TimeSpan.FromMilliseconds(100) };
        await using var pool = new ConnectionPool(token => ConnectAsync(server), config);

        PooledConnection a = await pool.GetAsync(TestContext.Current.CancellationToken);
        PooledConnection b = await pool.GetAsync(TestContext.Current.CancellationToken);
        await a.DisposeAsync();
        await b.DisposeAsync();
        Assert.Equal((1, 1), pool.Counts);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await (await pool.GetAsync(TestContext.Current.CancellationToken)).DisposeAsync();
        Assert.Equal(3, server.Connections);
        server.Hang = true;
        await using PooledConnection checkedOne = await pool.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, server.Connections);
        await using var failing = new ConnectionPool(token => throw new TurborelException(-919, "08001", "no server"), new PoolConfig { Max = 1 });
        await Assert.ThrowsAsync<TurborelException>(() => failing.GetAsync(TestContext.Current.CancellationToken));
        Assert.Equal((0, 0), failing.Counts);
        await using var aging = new ConnectionPool(new Source(server), new PoolConfig { MaxLifetime = TimeSpan.Zero, Check = false });
        await (await aging.GetAsync(TestContext.Current.CancellationToken)).DisposeAsync();
        Assert.Equal((0, 0), aging.Counts);
    }

    /// <summary>
    /// Of a server that grants neither ping nor reset, a connection is checked
    /// by ROLLBACK and reset in one pipeline: ROLLBACK, each statement it
    /// prepared closed and destroyed, its user, schema and zone those of its
    /// login, then the program's statements.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pools]")]
    public async Task ResetByStatementsWithoutTheCapabilities()
    {
        await using var server = ManyServer.Start();
        await using var pool = new ConnectionPool(token => ConnectAsync(server), new PoolConfig { Reset = ["SET X = 1"] });

        PooledConnection lent = await pool.GetAsync(TestContext.Current.CancellationToken);
        lent.Connection.NextStatement = 1;
        await lent.DisposeAsync();
        await (await pool.GetAsync(TestContext.Current.CancellationToken)).DisposeAsync();
        Assert.Equal(
            ["ROLLBACK", "C", "D", "SET SESSION AUTHORIZATION '\"ANN\"'", "SET SCHEMA '\"ANN\"'", "SET TIME ZONE LOCAL", "SET X = 1", "ROLLBACK", "ROLLBACK", "SET SESSION AUTHORIZATION '\"ANN\"'"],
            [.. server.Requests.Take(10)]);
        PooledConnection many = await pool.GetAsync(TestContext.Current.CancellationToken);
        many.Connection.NextStatement = 300;
        await many.DisposeAsync();
        Assert.Equal((0, 0), pool.Counts);
        Assert.Equal((10, 10, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), TimeSpan.FromSeconds(30), true), (new PoolConfig().Max, new PoolConfig().MaxIdle, PoolConfig.DefaultIdleTimeout, PoolConfig.DefaultMaxLifetime, PoolConfig.DefaultWait, new PoolConfig().Check));
    }

    private static Task<NativeConnection> ConnectAsync(ManyServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), new ConnectOptions { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) });

    // A source of the server's connections.
    private sealed class Source(ManyServer server) : IConnectionSource
    {
        public Task<NativeConnection> ConnectAsync(System.Threading.CancellationToken cancellationToken = default) => PoolTests.ConnectAsync(server);
    }
}
