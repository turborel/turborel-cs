// A statement's time limit in the standard API (RULES.md D20): once its
// seconds run out, the statement running is cancelled on the server, as
// a Canceller cancels it, over a connection of its own [CLIENT Running
// statements]; the statement then fails, SQLSTATE TB920, the session
// kept.

using System;
using System.Threading;
using Turborel.Data.Session;

namespace Turborel.Data;

/// <summary>Cancels the statement running on a connection once its time runs out.</summary>
internal sealed class StatementTimer : IDisposable
{
    private readonly Timer? _timer;

    /// <summary>Initializes a new instance of the <see cref="StatementTimer"/> class.</summary>
    /// <param name="native">The connection.</param>
    /// <param name="seconds">The seconds the statement may run; 0 for no limit.</param>
    internal StatementTimer(NativeConnection native, int seconds)
    {
        if (seconds > 0 && native.Canceller is { } canceller)
        {
            _timer = new Timer(_ => _ = canceller.CancelAsync(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _timer?.Dispose();
}
