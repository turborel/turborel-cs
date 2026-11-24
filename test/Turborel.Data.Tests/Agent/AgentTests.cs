// The SQL-agent against servers of the test's own: connections made by
// CONNECT, each named, one current, chosen by SET CONNECTION and ended by
// DISCONNECT, each refusal as ISO/IEC 9075:1992 §15 says, and every other
// statement sent to the current connection [CLIENT The SQL-agent].

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Agent;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Agent;

/// <summary>Tests of <see cref="SqlAgent"/>.</summary>
public sealed class AgentTests
{
    /// <summary>
    /// CONNECT makes a connection current, named by AS, else by its server;
    /// SET CONNECTION chooses one; DISCONNECT ends what it names, none current
    /// after if the current one was; other statements go to the current one.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT The SQL-agent]")]
    public async Task ConnectionsAreMadeChosenAndEnded()
    {
        await using var server = ManyServer.Start();
        var connector = new Connector(server);
        var agent = new SqlAgent(connector, await connector.ConnectAsync(null, null, TestContext.Current.CancellationToken));

        Assert.Equal((null, false), (agent.CurrentName, agent.IsLost));
        Assert.Equal("00000", ((DoneOutcome)await agent.QueryAsync("CONNECT TO 'db1' AS 'alpha' USER 'bob'", TestContext.Current.CancellationToken)).Completion.SqlState);
        Assert.Equal(("ALPHA", "bob"), (agent.CurrentName, connector.Users.Last()));
        _ = await agent.QueryAsync("/* the second /* nested */ */ connect -- to it\n to ' beta '; -- done", TestContext.Current.CancellationToken);
        Assert.Equal("BETA", agent.CurrentName);
        _ = await agent.QueryAsync("SET CONNECTION 'alpha'", TestContext.Current.CancellationToken);
        Assert.Equal("ALPHA", agent.CurrentName);
        _ = await agent.QueryAsync("VALUES 1", TestContext.Current.CancellationToken);
        Assert.Equal("VALUES 1", server.Requests.Last());
        Assert.NotNull(agent.Canceller);
        Assert.Null(agent.Warning);
        _ = await agent.QueryIntoAsync("SET CONNECTION DEFAULT", null!, TestContext.Current.CancellationToken);
        Assert.Null(agent.CurrentName);
        Assert.NotNull(agent.CurrentConnection);
        _ = await agent.QueryAsync("DISCONNECT CURRENT", TestContext.Current.CancellationToken);
        Assert.Null(agent.CurrentConnection);
        Assert.Equal("08003", (await Assert.ThrowsAsync<TurborelException>(() => agent.QueryAsync("VALUES 1", TestContext.Current.CancellationToken))).SqlState);
        _ = await agent.QueryAsync("SET CONNECTION 'BETA'", TestContext.Current.CancellationToken);
        _ = await agent.QueryAsync("DISCONNECT 'alpha'", TestContext.Current.CancellationToken);
        Assert.Equal("BETA", agent.CurrentName);
        Assert.Null(await agent.ConnectionStatementAsync("VALUES 1", TestContext.Current.CancellationToken));
        _ = await agent.QueryAsync("CONNECT TO DEFAULT", TestContext.Current.CancellationToken);
        _ = await agent.QueryAsync("DISCONNECT DEFAULT", TestContext.Current.CancellationToken);
        _ = await agent.QueryAsync("DISCONNECT ALL", TestContext.Current.CancellationToken);
        Assert.Null(agent.CurrentConnection);
        await agent.TerminateAsync();
    }

    /// <summary>Text that cannot be read as tokens, a literal, delimited identifier or bracketed comment not closed, is refused as turborel's lexer refuses it, SQLCODE -101, before anything is sent.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The task.</returns>
    [Theory]
    [Verifies("[CLIENT The SQL-agent]")]
    [InlineData("CONNECT TO 'db")]
    [InlineData("CONNECT TO 'db' /* open")]
    [InlineData("SET CONNECTION \"open")]
    public async Task UnreadableTextIsLexicalError(string sql)
    {
        await using var server = ManyServer.Start();
        var agent = new SqlAgent(new Connector(server));
        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => agent.QueryAsync(sql, TestContext.Current.CancellationToken));
        Assert.Equal((-101, "42000"), (refused.SqlCode, refused.SqlState));
        await agent.TerminateAsync();
    }

    /// <summary>Each refusal of a connection statement has its SQLSTATE, as §15 says.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="state">The refusal's SQLSTATE.</param>
    /// <returns>The task.</returns>
    [Theory]
    [Verifies("[CLIENT The SQL-agent]")]
    [InlineData("CONNECT TO 'db' USER '1 bad'", "28000")]
    [InlineData("CONNECT TO 'db' AS '2x'", "2E000")]
    [InlineData("CONNECT TO 'alpha'", "08002")]
    [InlineData("CONNECT TO 'lost'", "08001")]
    [InlineData("CONNECT TO 'refused'", "08004")]
    [InlineData("SET CONNECTION 'none'", "08003")]
    [InlineData("SET CONNECTION DEFAULT", "08003")]
    [InlineData("DISCONNECT 'none'", "08003")]
    [InlineData("DISCONNECT DEFAULT", "08003")]
    [InlineData("CONNECT TO db", "42000")]
    [InlineData("CONNECT 'db'", "42000")]
    [InlineData("CONNECT TO 'db' EXTRA", "42000")]
    [InlineData("CONNECT TO 'db", "42000")]
    [InlineData("DISCONNECT", "42000")]
    [InlineData("CONNECT TO \"db\"", "42000")]
    [InlineData("CONNECT TO 'work' AS 'w'", "0A001")]
    [InlineData("DISCONNECT 'w'", "25000")]
    public async Task RefusalsSayWhy(string sql, string state)
    {
        await using var server = ManyServer.Start();
        var agent = new SqlAgent(new Connector(server));
        _ = await agent.QueryAsync("CONNECT TO 'alpha'", TestContext.Current.CancellationToken);
        if (state is "0A001" or "25000")
        {
            _ = await agent.QueryAsync(state == "25000" ? "CONNECT TO 'db' AS 'w'" : "VALUES 1", TestContext.Current.CancellationToken);
            _ = await agent.QueryAsync("BEGIN WORK", TestContext.Current.CancellationToken);
        }

        TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => agent.QueryAsync(sql, TestContext.Current.CancellationToken));
        Assert.Equal(state, refused.SqlState);
        await agent.TerminateAsync();
    }

    // Connects to the server, refusing the servers 'lost' and 'refused'.
    private sealed class Connector(ManyServer server) : IConnector
    {
        internal System.Collections.Generic.List<string?> Users { get; } = [];

        public Task<NativeConnection> ConnectAsync(string? server1, string? user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return server1 switch
            {
                "lost" => throw new TurborelException(-919, "08006", "unreachable"),
                "refused" => throw new TurborelException(-919, "28000", "refused"),
                _ => NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), cancellationToken: cancellationToken),
            };
        }
    }
}
