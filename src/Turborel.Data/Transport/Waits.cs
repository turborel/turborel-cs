// A wait on the server bounded in time and by the program's token: a wait
// that runs out, or that the program cancels, is a connection failure, as
// the connection's state is then in doubt [CLIENT Timeouts].

using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Turborel.Data.Transport;

/// <summary>Runs waits on the server within their limits.</summary>
internal static class Waits
{
    /// <summary>The longest limit a wait can be timed by; one longer is no limit, as no wait outlasts it.</summary>
    internal static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Runs <paramref name="wait"/> within <paramref name="within"/>, failing as the connection's failure.</summary>
    /// <typeparam name="T">What the wait gives.</typeparam>
    /// <param name="what">What waits, as connect, send or receive.</param>
    /// <param name="within">How long it may wait; null for no limit.</param>
    /// <param name="wait">The wait, given the token that bounds it.</param>
    /// <param name="cancellationToken">The program's token.</param>
    /// <returns>What the wait gave.</returns>
    internal static async Task<T> Within<T>(string what, TimeSpan? within, Func<CancellationToken, Task<T>> wait, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (within is TimeSpan span && span <= Longest)
        {
            limit.CancelAfter(span < TimeSpan.Zero ? TimeSpan.Zero : span);
        }

        try
        {
            return await wait(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw IoFailure.Of(what, e, timedOut: true);
        }
        catch (OperationCanceledException e)
        {
            throw IoFailure.Of(what, new OperationCanceledException("the program cancelled the wait", e), timedOut: false);
        }
        catch (Exception e) when (e is IOException or SocketException or AuthenticationException or ObjectDisposedException)
        {
            throw IoFailure.Of(what, e, timedOut: false);
        }
    }

    /// <summary>Runs <paramref name="wait"/>, which gives nothing, within <paramref name="within"/>.</summary>
    /// <param name="what">What waits.</param>
    /// <param name="within">How long it may wait; null for no limit.</param>
    /// <param name="wait">The wait.</param>
    /// <param name="cancellationToken">The program's token.</param>
    /// <returns>The wait's task.</returns>
    internal static Task Within(string what, TimeSpan? within, Func<CancellationToken, Task> wait, CancellationToken cancellationToken)
    {
        return Within<bool>(
            what,
            within,
            async token =>
            {
                await wait(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }
}
