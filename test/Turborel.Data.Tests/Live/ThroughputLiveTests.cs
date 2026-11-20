// Pipelines, pools and the SQL-agent against a real turboreld (RULES.md
// R-TEST-8): a batch of requests, one outcome each, a failure its own or
// skipping the rest; a pool's connections handed out and given back reset;
// the agent's connections made, chosen and ended [CLIENT Pipelines]
// [CLIENT Pools] [CLIENT The SQL-agent].

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Agent;
using Turborel.Data.Authentication;
using Turborel.Data.Configuration;
using Turborel.Data.Pooling;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of pipelines, pools and the SQL-agent.</summary>
/// <param name="server">The live server.</param>
public sealed class ThroughputLiveTests(LiveServer server)
{
    /// <summary>
    /// A batch runs in one round trip a window, each request its outcome, a
    /// failure its own under autocommit; skipping, the requests after a
    /// failure are undone, those before kept.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Pipelines]")]
    [Verifies("[PROTOCOL Requests]")]
    public async Task PipelinesContinueOrSkip()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("PIPED");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER PRIMARY KEY)");
        _ = await c.DoneAsync("COMMIT");
        c.Autocommit = true;
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?)", TestContext.Current.CancellationToken);

        Pipeline batch = c.Pipeline();
        foreach (int n in new[] { 1, 2, 2, 3 })
        {
            _ = batch.Execute(insert, [new TurborelDecimal(n, 0)]);
        }

        IReadOnlyList<Piped> outcomes = await batch.Query($"SELECT COUNT(*) FROM {t}").RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal([typeof(PipedDone), typeof(PipedDone), typeof(PipedFailed), typeof(PipedDone), typeof(PipedDone)], outcomes.Select(o => o.GetType()));
        Assert.Equal("3", Live.Texts(((RowsOutcome)((PipedDone)outcomes[4]).Outcome).Rows[0])[0]);
        Pipeline skipping = c.Pipeline().Execute(insert, [new TurborelDecimal(4, 0)]).Execute(insert, [new TurborelDecimal(1, 0)]).Execute(insert, [new TurborelDecimal(5, 0)]);
        skipping.OnError = PipelineOnError.Skip;
        Assert.Equal([typeof(PipedDone), typeof(PipedFailed), typeof(PipedSkipped)], (await skipping.RunAsync(TestContext.Current.CancellationToken)).Select(o => o.GetType()));
        Assert.False(c.InTransaction);
        Assert.Equal([["1"], ["2"], ["3"], ["4"]], (await c.RowsAsync($"SELECT N FROM {t} ORDER BY N")).Select(Live.Texts));
    }

    /// <summary>A pool of settings hands out a connection, takes it back reset by Reset, checks it by Ping and hands it out again.</summary>
    [Fact]
    [Verifies("[CLIENT Pools]")]
    public async Task PoolsHandOutAndTakeBack()
    {
        server.Require();
        var settings = ConnectionSettings.ResolveIn(
            ConnectionOptions.FromUri($"turborel://ann@127.0.0.1:{LiveServer.Port}/?tls_mode=disable").Set(ConnectionOptions.Passfile, "/nonexistent"),
            _ => null);
        await using var pool = new ConnectionPool(settings, new PoolConfig { Max = 1, Reset = ["SET SESSION.TIME_ZONE = '+01:00'"] });

        await using (PooledConnection lent = await pool.GetAsync(TestContext.Current.CancellationToken))
        {
            _ = await lent.Connection.DoneAsync("SET SESSION.TIME_ZONE = '+05:00'");
            _ = await lent.Connection.RowsAsync("VALUES 1");
            Assert.True(lent.Connection.InTransaction);
        }

        await using PooledConnection again = await pool.GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(("+01:00", false), (again.Connection.Setting("SESSION.TIME_ZONE"), again.Connection.InTransaction));
        Assert.Equal((1, 0), pool.Counts);
    }

    /// <summary>The agent connects through its connector, sends other statements to the current connection, and ends them.</summary>
    [Fact]
    [Verifies("[CLIENT The SQL-agent]")]
    public async Task AgentConnectsChoosesAndEnds()
    {
        server.Require();
        var agent = new SqlAgent(new Connector());

        _ = await agent.QueryAsync($"CONNECT TO '127.0.0.1:{LiveServer.Port}' AS 'main' USER 'ann'", TestContext.Current.CancellationToken);
        Assert.Equal(LiveServer.Owner, agent.Current().Authorization);
        Assert.IsType<RowsOutcome>(await agent.QueryAsync("VALUES 1", TestContext.Current.CancellationToken));
        TurborelException active = await Assert.ThrowsAsync<TurborelException>(() => agent.QueryAsync("SET CONNECTION DEFAULT", TestContext.Current.CancellationToken));
        Assert.Equal("0A001", active.SqlState);
        _ = await agent.QueryAsync("ROLLBACK", TestContext.Current.CancellationToken);
        _ = await agent.QueryAsync("DISCONNECT 'main'", TestContext.Current.CancellationToken);
        Assert.Null(agent.CurrentConnection);
        await agent.TerminateAsync();
    }

    // Connects to the address a CONNECT names, as its user, by trust.
    private sealed class Connector : IConnector
    {
        public Task<NativeConnection> ConnectAsync(string? server, string? user, CancellationToken cancellationToken = default)
        {
            string[] parts = (server ?? $"127.0.0.1:{LiveServer.Port}").Split(':');
            return NativeConnection.ConnectAsync(new TcpTarget(parts[0], int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)), user ?? LiveServer.Owner, new TrustMechanism(), cancellationToken: cancellationToken);
        }
    }
}
