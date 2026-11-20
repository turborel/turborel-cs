// Cancelling a connection's running statement from another thread
// [PROTOCOL Cancel 'Z'] [CLIENT Running statements]: a new connection to
// the same server, whose first and only message is Cancel with the key the
// first's Ready gave; the server closes it, saying nothing, and the
// statement fails with SQLCODE -920. A cancel gives up after 10 seconds,
// or a limit of the program's, so a server that hangs leaves the thread free.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Transport;

namespace Turborel.Data.Session;

/// <summary>What cancels the statement a connection runs, from any thread.</summary>
public sealed class Canceller
{
    /// <summary>How long a cancel waits for the server, each step, unless told another [CLIENT Running statements].</summary>
    public static readonly TimeSpan CancelWithin = TimeSpan.FromSeconds(10);

    private readonly Target _target;
    private readonly ReadOnlyMemory<byte> _key;

    internal Canceller(Target target, ReadOnlyMemory<byte> key)
    {
        _target = target;
        _key = key;
    }

    /// <summary>Cancels the statement its connection runs, if one runs.</summary>
    /// <param name="within">How long each step waits; 10 seconds if null.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure if the server cannot be reached, or does not answer in time.</exception>
    public async Task CancelAsync(TimeSpan? within = null, CancellationToken cancellationToken = default)
    {
        TimeSpan limit = within ?? CancelWithin;
        (Stream stream, _) = await _target.OpenAsync(limit, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var w = new WireWriter();
            new Request.Cancel(_key).Encode(w);
            byte[] bytes = w.ToArray();
            await Waits.Within(
                "cancel",
                limit,
                async token =>
                {
                    await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                    // The server closes it, saying nothing.
                    byte[] rest = new byte[1];
                    while (await stream.ReadAsync(rest, token).ConfigureAwait(false) > 0)
                    {
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => "Canceller(..)";
}
