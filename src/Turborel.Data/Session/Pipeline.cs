// Pipelining [CLIENT Pipelines] [PROTOCOL Requests]: a pipeline of a
// connection queues requests, writes them at once, and reads one outcome
// for each, in order, as the server answers each request alone and in
// turn, as turborel-client's does. The requests are written a window of
// at most 64 KiB at a time, its replies read before the next is written,
// so a window fits the two sockets' buffers and is written whole even
// while the server writes its first replies.
//
// A failure is by default its request's outcome alone, the rest running.
// Skipping makes the batch stop at its first failure: a savepoint set
// before each request but the first, the requests after a failed one
// undone to the savepoint after it, or never sent if their window was not
// yet written; its requests are of one transaction, which under
// autocommit, of a batch that began none, is committed as it ends.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>What a pipeline does after a request fails.</summary>
public enum PipelineOnError
{
    /// <summary>Every request runs; a failure is its request's outcome alone.</summary>
    Continue,

    /// <summary>The batch stops at its first failure: the requests after it undone or never sent, each skipped.</summary>
    Skip,
}

/// <summary>The outcome of one request of a pipeline: <see cref="PipedDone"/>, <see cref="PipedFailed"/> or <see cref="PipedSkipped"/>.</summary>
public abstract record Piped
{
    // Only the kinds below are outcomes.
    private protected Piped()
    {
    }
}

/// <summary>What the request gave.</summary>
/// <param name="Outcome">Its outcome.</param>
public sealed record PipedDone(Outcome Outcome) : Piped;

/// <summary>How the request failed, as the server reported it.</summary>
/// <param name="Failure">The failure.</param>
public sealed record PipedFailed(TurborelException Failure) : Piped;

/// <summary>Not run, or run and undone, after an earlier request failed under <see cref="PipelineOnError.Skip"/>.</summary>
public sealed record PipedSkipped : Piped;

/// <summary>Requests queued on a connection, written at once by <see cref="RunAsync"/>.</summary>
public sealed class Pipeline
{
    /// <summary>The most bytes of requests written before their replies are read: 64 KiB.</summary>
    public const int Window = 64 * 1024;

    /// <summary>What the savepoints a skipping batch sets are named by, then each its place.</summary>
    private const string SavepointName = "TURBOREL_PIPELINE";

    private readonly NativeConnection _connection;
    private readonly List<Queued> _queued = [];

    /// <summary>Initializes a new instance of the <see cref="Pipeline"/> class.</summary>
    /// <param name="connection">Its connection.</param>
    internal Pipeline(NativeConnection connection)
    {
        _connection = connection;
    }

    /// <summary>Gets or sets what the batch does after a request fails.</summary>
    public PipelineOnError OnError { get; set; }

    /// <summary>Gets how many requests are queued.</summary>
    public int Count => _queued.Count;

    /// <summary>Queues <paramref name="sql"/>, run as <see cref="NativeConnection.QueryAsync"/> runs it.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The pipeline.</returns>
    public Pipeline Query(string sql)
    {
        Misuse.ThrowIfNull(sql);
        _queued.Add(new Queued(QueuedKind.Query, sql, null, null, 0));
        return this;
    }

    /// <summary>Queues an execution of <paramref name="statement"/> with <paramref name="values"/>, as <see cref="NativeConnection.ExecuteAsync"/> runs it: all of a query's rows.</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="values">One value for each parameter.</param>
    /// <returns>The pipeline.</returns>
    public Pipeline Execute(PreparedStatement statement, IReadOnlyList<object?> values)
    {
        Misuse.ThrowIfNull(statement);
        Misuse.ThrowIfNull(values);
        _queued.Add(new Queued(QueuedKind.Execute, null, statement, values, 0));
        return this;
    }

    /// <summary>Writes the requests, a window at a time, and reads one outcome for each, in the order they were queued.</summary>
    /// <param name="cancellationToken">Cancels the waits, losing the connection.</param>
    /// <returns>Each request's outcome.</returns>
    /// <exception cref="TurborelException">A connection failure, once the connection is lost: what the requests answered before it is not given; under skipping, the failure of undoing the requests after a failed one.</exception>
    public async Task<IReadOnlyList<Piped>> RunAsync(CancellationToken cancellationToken = default)
    {
        bool skip = OnError == PipelineOnError.Skip;
        bool began = _connection.InTransaction;
        List<Queued> sent = skip ? Guarded(_queued) : [.. _queued];
        bool ends = _connection.Autocommit && !skip;
        var outcomes = new List<Piped>(sent.Count);
        foreach (List<(Queued Queued, byte[] Frame)> window in Windows(sent, ends, _connection.QueryAttributesSent))
        {
            if (skip && outcomes.Any(o => o is PipedFailed))
            {
                outcomes.AddRange(window.Select(_ => (Piped)new PipedSkipped()));
                continue;
            }

            outcomes.AddRange(await ReadWindowAsync(window, cancellationToken).ConfigureAwait(false));
        }

        if (skip)
        {
            await SettleAsync(sent, outcomes, began, cancellationToken).ConfigureAwait(false);
        }

        return [.. sent.Zip(outcomes).Where(p => p.First.Kind != QueuedKind.Savepoint).Select(p => p.Second)];
    }

    /// <summary>Queues the closing of statement <paramref name="number"/>'s cursor, then its destruction: a pool's reset of what a borrower left.</summary>
    /// <param name="number">The statement's number.</param>
    internal void Release(uint number)
    {
        _queued.Add(new Queued(QueuedKind.Close, null, null, null, number));
        _queued.Add(new Queued(QueuedKind.Deallocate, null, null, null, number));
    }

    /// <summary>Groups requests into windows of at most <see cref="Window"/> bytes, in order, a request longer than that alone in its own.</summary>
    /// <param name="queued">The requests.</param>
    /// <param name="ends">Whether the transaction ends with each.</param>
    /// <param name="attributes">The attributes each statement of the program's carries.</param>
    /// <returns>The windows, each request with its frame.</returns>
    internal static List<List<(Queued Queued, byte[] Frame)>> Windows(IReadOnlyList<Queued> queued, bool ends, IReadOnlyList<KeyValuePair<string, string>> attributes)
    {
        var windows = new List<List<(Queued, byte[])>>();
        int filled = Window;
        foreach (Queued q in queued)
        {
            var w = new WireWriter();
            RequestOf(q, ends, attributes).Encode(w);
            byte[] frame = w.ToArray();
            filled += frame.Length;
            if (filled > Window)
            {
                windows.Add([]);
                filled = frame.Length;
            }

            windows[^1].Add((q, frame));
        }

        return windows;
    }

    // The savepoint set before the request of place n.
    private static string Savepoint(int n) => string.Create(CultureInfo.InvariantCulture, $"{SavepointName}_{n}");

    // The requests, a savepoint of its own before each but the first.
    private static List<Queued> Guarded(List<Queued> queued)
    {
        var sent = new List<Queued>(queued.Count * 2);
        for (int n = 0; n < queued.Count; n++)
        {
            if (n > 0)
            {
                sent.Add(new Queued(QueuedKind.Savepoint, null, null, null, (uint)n));
            }

            sent.Add(queued[n]);
        }

        return sent;
    }

    // The request q sends, the transaction ended with it if ends, a
    // statement of the program's carrying the attributes.
    private static Request RequestOf(Queued q, bool ends, IReadOnlyList<KeyValuePair<string, string>> attributes) => q.Kind switch
    {
        QueuedKind.Query => new Request.Query(q.Sql!, ends, attributes),
        QueuedKind.Execute => new Request.Execute(new StatementCursor(q.Statement!.Number), q.Values!, 0, ends, attributes),
        QueuedKind.Close => new Request.Close(new StatementCursor(q.Number)),
        QueuedKind.Deallocate => new Request.Deallocate(q.Number),
        _ => new Request.Query($"SAVEPOINT {Savepoint((int)q.Number)}", Ends: false, []),
    };

    // Writes a window at once and reads its outcomes.
    private async Task<List<Piped>> ReadWindowAsync(List<(Queued Queued, byte[] Frame)> window, CancellationToken cancellationToken)
    {
        await _connection.WriteFramesAsync([.. window.Select(w => w.Frame)], cancellationToken).ConfigureAwait(false);
        var outcomes = new List<Piped>(window.Count);
        foreach ((Queued q, _) in window)
        {
            try
            {
                Outcome outcome = q.Kind switch
                {
                    QueuedKind.Execute => await _connection.ExecuteReplyAsync(q.Statement!, cancellationToken).ConfigureAwait(false),
                    QueuedKind.Close or QueuedKind.Deallocate => new DoneOutcome(await _connection.CompletionAsync(cancellationToken).ConfigureAwait(false)),
                    _ => await _connection.QueryReplyAsync(cancellationToken).ConfigureAwait(false),
                };
                outcomes.Add(new PipedDone(outcome));
            }
            catch (TurborelException failure) when (!_connection.IsLost)
            {
                outcomes.Add(new PipedFailed(failure));
            }
        }

        return outcomes;
    }

    // Ends a skipping batch: the requests after the first that failed
    // undone, to the savepoint set after it, each marked skipped; the
    // savepoints released; and, under autocommit of a batch that began no
    // transaction, its work committed.
    private async Task SettleAsync(List<Queued> sent, List<Piped> outcomes, bool began, CancellationToken cancellationToken)
    {
        int failed = outcomes.FindIndex(o => o is PipedFailed);
        var statements = new List<string>();
        if (failed >= 0)
        {
            if (failed + 1 < sent.Count && sent[failed + 1].Kind == QueuedKind.Savepoint && outcomes[failed + 1] is PipedDone)
            {
                statements.Add($"ROLLBACK TO SAVEPOINT {Savepoint((int)sent[failed + 1].Number)}");
            }

            for (int i = failed + 1; i < outcomes.Count; i++)
            {
                outcomes[i] = new PipedSkipped();
            }
        }

        if (sent.Count > 1 && sent[1].Kind == QueuedKind.Savepoint && outcomes[1] is PipedDone)
        {
            statements.Add($"RELEASE SAVEPOINT {Savepoint((int)sent[1].Number)}");
        }

        if (!began && _connection.Autocommit)
        {
            statements.Add("COMMIT");
        }

        foreach (string sql in statements)
        {
            // Within the transaction, whatever autocommit says: a statement
            // of the batch's own, which must not end it.
            await _connection.SendAsync(new Request.Query(sql, Ends: false, []), cancellationToken).ConfigureAwait(false);
            _ = await _connection.QueryReplyAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>What a request queued is.</summary>
    internal enum QueuedKind
    {
        /// <summary>A direct statement.</summary>
        Query,

        /// <summary>An execution of a prepared statement.</summary>
        Execute,

        /// <summary>A cursor closed, of a pool's reset.</summary>
        Close,

        /// <summary>A statement destroyed, of a pool's reset.</summary>
        Deallocate,

        /// <summary>The savepoint a skipping batch sets before a request.</summary>
        Savepoint,
    }

    /// <summary>A request queued.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Sql">A query's text.</param>
    /// <param name="Statement">An execution's statement.</param>
    /// <param name="Values">An execution's values.</param>
    /// <param name="Number">A statement's number, or a savepoint's place.</param>
    internal sealed record Queued(QueuedKind Kind, string? Sql, PreparedStatement? Statement, IReadOnlyList<object?>? Values, uint Number);
}
