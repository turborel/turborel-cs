// A pool of connections [CLIENT Pools], as turborel-client's: a bounded
// set the program's tasks share. GetAsync hands out an idle connection
// that answers, or makes one while fewer than Max are made, or waits for
// one to come back, at most Wait. A connection handed out comes back as
// its PooledConnection is disposed, reset to the state it logged in with;
// one lost, or whose reset fails, is closed instead, so no borrower sees
// another's session. Idle beyond IdleTimeout, or older than MaxLifetime,
// a connection is closed rather than handed out, and no more than MaxIdle
// are kept idle. It starts no thread: each wait is the waiting borrower's.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Session;

namespace Turborel.Data.Pooling;

/// <summary>A pool of connections, shared by everything that holds it.</summary>
public sealed class ConnectionPool : IAsyncDisposable
{
    private readonly IConnectionSource _source;
    private readonly Lock _lock = new();
    private readonly Stack<Idle> _idle = new();
    private TaskCompletionSource _returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _made;

    /// <summary>Initializes a new instance of the <see cref="ConnectionPool"/> class: of connections <paramref name="source"/> makes, kept as <paramref name="config"/> says.</summary>
    /// <param name="source">What makes its connections.</param>
    /// <param name="config">How it keeps them.</param>
    public ConnectionPool(IConnectionSource source, PoolConfig? config = null)
    {
        Misuse.ThrowIfNull(source);
        _source = source;
        Config = config ?? new PoolConfig();
    }

    /// <summary>Initializes a new instance of the <see cref="ConnectionPool"/> class: of connections <paramref name="connect"/> makes.</summary>
    /// <param name="connect">Makes a connection.</param>
    /// <param name="config">How it keeps them.</param>
    public ConnectionPool(Func<CancellationToken, Task<NativeConnection>> connect, PoolConfig? config = null)
        : this(new FunctionSource(Checked(connect)), config)
    {
    }

    /// <summary>Gets how it keeps its connections.</summary>
    public PoolConfig Config { get; }

    /// <summary>Gets how many connections are made, handed out and idle, and how many of them are idle.</summary>
    public (int Made, int Idle) Counts
    {
        get
        {
            lock (_lock)
            {
                return (_made, _idle.Count);
            }
        }
    }

    /// <summary>Gets a connection: an idle one that answers, else a new one while the pool has room, else the first to come back within <see cref="PoolConfig.Wait"/>.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The connection, given back as it is disposed.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if none comes back in time; the source's failure to make one.</exception>
    public async Task<PooledConnection> GetAsync(CancellationToken cancellationToken = default)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(Config.Wait.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            (Idle? idle, bool make) = await NextAsync(deadline, cancellationToken).ConfigureAwait(false);
            if (make)
            {
                return await MakeAsync(cancellationToken).ConfigureAwait(false);
            }

            if (idle is null)
            {
                throw new TurborelException(-919, "08001", $"[DESIGN] no connection of the pool's {Config.Max} came back within {Config.Wait}");
            }

            if (await CheckedAsync(idle, cancellationToken).ConfigureAwait(false) is { } pooled)
            {
                return pooled;
            }
        }
    }

    /// <summary>Closes the idle connections; those handed out are closed as they come back.</summary>
    /// <returns>The task.</returns>
    public async ValueTask DisposeAsync()
    {
        Idle[] idle;
        lock (_lock)
        {
            idle = [.. _idle];
            _idle.Clear();
            _made -= idle.Length;
            Closed = true;
        }

        foreach (Idle connection in idle)
        {
            await connection.Connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Takes a connection back, reset; or closes it, if it is lost, its reset fails, it has lived its time, or enough are idle.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="made">When it was made.</param>
    /// <returns>The task.</returns>
    internal async Task GiveBackAsync(NativeConnection connection, long made)
    {
        bool lived = Config.MaxLifetime is TimeSpan most && Stopwatch.GetElapsedTime(made) >= most;
        bool kept = !lived;
        if (kept)
        {
            try
            {
                await PoolReset.ResetAsync(connection, Config.Reset, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                kept = false;
            }
        }

        lock (_lock)
        {
            if (kept && !Closed && _idle.Count < Config.MaxIdle)
            {
                _idle.Push(new Idle(connection, made, Stopwatch.GetTimestamp()));
                connection = null!;
            }
            else
            {
                _made--;
            }
        }

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        Signal();
    }

    // Whether the pool is disposed: what comes back is closed.
    private bool Closed { get; set; }

    private static Func<CancellationToken, Task<NativeConnection>> Checked(Func<CancellationToken, Task<NativeConnection>> connect)
    {
        Misuse.ThrowIfNull(connect);
        return connect;
    }

    // What GetAsync takes next: an idle connection, a place to make one,
    // or neither, the wait out; stale connections closed on the way.
    private async Task<(Idle? Idle, bool Make)> NextAsync(long deadline, CancellationToken cancellationToken)
    {
        while (true)
        {
            var stale = new List<NativeConnection>();
            (Idle? Idle, bool Make)? next = null;
            Task returned;
            lock (_lock)
            {
                while (next is null && _idle.TryPop(out Idle? idle))
                {
                    if (Fresh(idle))
                    {
                        next = (idle, false);
                    }
                    else
                    {
                        _made--;
                        stale.Add(idle.Connection);
                    }
                }

                if (next is null && _made < Config.Max)
                {
                    _made++;
                    next = (null, true);
                }

                returned = _returned.Task;
            }

            foreach (NativeConnection connection in stale)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            if (next is { } taken)
            {
                return taken;
            }

            TimeSpan left = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
            if (left <= TimeSpan.Zero)
            {
                return (null, false);
            }

            try
            {
                await returned.WaitAsync(left, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return (null, false);
            }
        }
    }

    // Whether an idle connection may still be handed out.
    private bool Fresh(Idle idle)
    {
        bool lived = Config.MaxLifetime is not TimeSpan most || Stopwatch.GetElapsedTime(idle.Made) < most;
        bool rested = Config.IdleTimeout is not TimeSpan idleMost || Stopwatch.GetElapsedTime(idle.Since) < idleMost;
        return lived && rested && !idle.Connection.IsLost;
    }

    // An idle connection, handed out if it answers or need not be checked;
    // else closed, its place given up.
    private async Task<PooledConnection?> CheckedAsync(Idle idle, CancellationToken cancellationToken)
    {
        if (Config.Check)
        {
            try
            {
                await PoolReset.AnswersAsync(idle.Connection, cancellationToken).ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                await idle.Connection.DisposeAsync().ConfigureAwait(false);
                Unmade();
                return null;
            }
        }

        return new PooledConnection(this, idle.Connection, idle.Made);
    }

    // A new connection, in the place NextAsync took for it.
    private async Task<PooledConnection> MakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            NativeConnection connection = await _source.ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new PooledConnection(this, connection, Stopwatch.GetTimestamp());
        }
        catch (TurborelException)
        {
            Unmade();
            throw;
        }
    }

    // One fewer connection made, so a waiter may make one.
    private void Unmade()
    {
        lock (_lock)
        {
            _made--;
        }

        Signal();
    }

    // Wakes the waiters, each to look again for a connection or a place.
    private void Signal()
    {
        TaskCompletionSource woken;
        lock (_lock)
        {
            woken = _returned;
            _returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        woken.SetResult();
    }

    // A connection idle in the pool: when it was made, and since when idle.
    private sealed record Idle(NativeConnection Connection, long Made, long Since);

    // A source of a function of the program's.
    private sealed class FunctionSource(Func<CancellationToken, Task<NativeConnection>> connect) : IConnectionSource
    {
        public Task<NativeConnection> ConnectAsync(CancellationToken cancellationToken = default) => connect(cancellationToken);
    }
}
