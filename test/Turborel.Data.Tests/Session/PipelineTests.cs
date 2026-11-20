// A pipeline against a server of the test's own: requests grouped into
// windows of 64 KiB, written at once, one outcome each in order, a failure
// its request's own; skipping, savepoints between the requests, those
// after a failure undone and the batch committed under autocommit; a lost
// connection failing the run [CLIENT Pipelines] [PROTOCOL Requests].

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of <see cref="Pipeline"/>.</summary>
public sealed class PipelineTests
{
    /// <summary>Requests are grouped into windows of at most 64 KiB, in order, one longer than a window alone in its own; none queued, no window.</summary>
    [Fact]
    [Verifies("[CLIENT Pipelines]")]
    public void RequestsAreWrittenAWindowAtATime()
    {
        Pipeline.Queued Query(string sql) => new(Pipeline.QueuedKind.Query, sql, null, null, 0);
        Pipeline.Queued[] queued = [Query("a"), Query("VALUES 1"), Query(new string('x', Pipeline.Window / 2)), Query(new string('y', Pipeline.Window)), Query("z")];

        Assert.Equal([3, 1, 1], Pipeline.Windows(queued, false, []).Select(w => w.Count));
        Assert.Equal(queued.Sum(q => Wire.Encode(new Request.Query(q.Sql!, true, [])).Length), Pipeline.Windows(queued, true, []).SelectMany(w => w).Sum(w => w.Frame.Length));
        Assert.Empty(Pipeline.Windows([], false, []));
    }

    /// <summary>
    /// The requests are written at once and each read in turn; a failure is
    /// its request's own and the rest run; under autocommit each ends its
    /// transaction; a lost connection fails the run.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pipelines]")]
    [Verifies("[PROTOCOL Requests]")]
    public async Task EachRequestHasItsOutcome()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Prepare);
            await peer.WriteAsync('P', Replies.Prepared(0, [], Replies.Integer));
            Assert.Equal(Wire.Encode(new Request.Query("A", true, []))[5..], await peer.ExpectAsync(RequestKinds.Query));
            _ = await peer.ExpectAsync(RequestKinds.Query);
            _ = await peer.ExpectAsync(RequestKinds.Execute);
            await peer.WriteAsync('K', Replies.Complete());
            await peer.WriteAsync('E', Replies.Error(-104, "42000"));
            await peer.WriteAsync('B', Replies.Rows(5));
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('E', Replies.Error(-919, "08006"));
        });
        await using NativeConnection c = await ConnectAsync(server);
        PreparedStatement select = await c.PrepareAsync("S", TestContext.Current.CancellationToken);
        c.Autocommit = true;

        Pipeline pipeline = c.Pipeline().Query("A").Query("B").Execute(select, []);
        Assert.Equal((3, PipelineOnError.Continue), (pipeline.Count, pipeline.OnError));
        IReadOnlyList<Piped> outcomes = await pipeline.RunAsync(TestContext.Current.CancellationToken);
        Assert.IsType<DoneOutcome>(Assert.IsType<PipedDone>(outcomes[0]).Outcome);
        Assert.Equal("42000", Assert.IsType<PipedFailed>(outcomes[1]).Failure.SqlState);
        Assert.Single(Assert.IsType<RowsOutcome>(Assert.IsType<PipedDone>(outcomes[2]).Outcome).Rows);
        await Assert.ThrowsAsync<TurborelException>(() => c.Pipeline().Query("C").RunAsync(TestContext.Current.CancellationToken));
        Assert.True(c.IsLost);
        await server.PlayedAsync();
    }

    /// <summary>
    /// Skipping, a savepoint is set before each request but the first; those
    /// after a failure are undone to the savepoint after it and skipped; the
    /// first savepoint is released; and under autocommit, of a batch that
    /// began no transaction, the batch is committed, none of it ending it before.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Pipelines]")]
    public async Task SkippingUndoesWhatFollowsAFailure()
    {
        var sent = new List<string>();
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            foreach (int i in Enumerable.Range(0, 5))
            {
                byte[] body = await peer.ExpectAsync(RequestKinds.Query);
                sent.Add(Encoding.UTF8.GetString(body, 4, BitConverter.ToInt32(body, 0)));
                Assert.Equal(0, body[4 + BitConverter.ToInt32(body, 0)]);
            }

            foreach (int i in Enumerable.Range(0, 5))
            {
                await (i == 2 ? peer.WriteAsync('E', Replies.Error(-201, "23000", active: true)) : peer.WriteAsync('K', Replies.Complete(active: true)));
            }

            foreach (int i in Enumerable.Range(0, 3))
            {
                byte[] body = await peer.ExpectAsync(RequestKinds.Query);
                sent.Add(Encoding.UTF8.GetString(body, 4, BitConverter.ToInt32(body, 0)));
                await peer.WriteAsync('K', Replies.Complete(active: i < 2));
            }
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;
        Pipeline pipeline = c.Pipeline().Query("A").Query("B").Query("C");
        pipeline.OnError = PipelineOnError.Skip;

        IReadOnlyList<Piped> outcomes = await pipeline.RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal([typeof(PipedDone), typeof(PipedFailed), typeof(PipedSkipped)], outcomes.Select(o => o.GetType()));
        Assert.Equal(
            ["A", "SAVEPOINT TURBOREL_PIPELINE_1", "B", "SAVEPOINT TURBOREL_PIPELINE_2", "C", "ROLLBACK TO SAVEPOINT TURBOREL_PIPELINE_2", "RELEASE SAVEPOINT TURBOREL_PIPELINE_1", "COMMIT"],
            sent);
        Assert.False(c.InTransaction);
        await server.PlayedAsync();
    }

    private static Task<NativeConnection> ConnectAsync(FakeServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), new ConnectOptions { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) });
}
