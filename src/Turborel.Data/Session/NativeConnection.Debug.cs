// The routine debugger's requests and events on a connection granted
// debugger *(extension)* [PROTOCOL Debug 'V'] [PROTOCOL Debugged 'V']
// [PROTOCOL Event 'W'] [CLIENT Debugging routines]: a request answered by
// Debugged or by a Complete, and the wait for an event, one read among
// other replies and kept, or the next the server sends, as
// turborel-client's debug module waits for it. TurborelDebugger is what
// a program calls.

using System;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Debugging;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>The connection's routine debugger.</summary>
public sealed partial class NativeConnection
{
    /// <summary>Gets the routine debugger of this connection, with <c>debugger</c> granted <i>(extension)</i>.</summary>
    public TurborelDebugger Debugger => new(() => this);

    /// <summary>Sends a Debug request answered by Debugged, and reads its answer.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="TurborelException">The server's refusal, the session kept; a connection failure: <c>debugger</c> not granted, or the server gone.</exception>
    internal async Task<DebugAnswer> DebuggedAsync(DebugRequest request, CancellationToken cancellationToken)
    {
        Needs(Capabilities.Debugger);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Debugged debugged => debugged.Answer,
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Debug reply")),
        };
    }

    /// <summary>Sends a Debug request answered by a Complete, and reads it.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The server's refusal, the session kept; a connection failure: <c>debugger</c> not granted, or the server gone.</exception>
    internal async Task DebugDoneAsync(DebugRequest request, CancellationToken cancellationToken)
    {
        Needs(Capabilities.Debugger);
        if (request is DebugRequest.Detach)
        {
            _told.Events.Clear();
        }

        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await CompletionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The next event of the session this connection debugs not yet read:
    /// one kept, else the next the server sends, waited for at most
    /// <paramref name="within"/>, without limit if null; null if none came,
    /// the connection kept. A Notice or a Status that comes is taken, and
    /// the wait gives null, as turborel-client's debug_wait does.
    /// </summary>
    /// <param name="within">The longest wait; null for no limit.</param>
    /// <param name="cancellationToken">Cancels the wait before an event begins to arrive, the connection kept.</param>
    /// <returns>The event, or null.</returns>
    /// <exception cref="TurborelException">A connection failure: the server gone, or a reply where an event was waited for.</exception>
    /// <exception cref="OperationCanceledException">The wait cancelled before an event began to arrive.</exception>
    internal async Task<DebugEvent?> DebugWaitAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        if (_told.Events.TryDequeue(out DebugEvent? kept))
        {
            return kept;
        }

        if (!await ArrivesWithinAsync(within, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        Reply reply = await NextReplyAsync(cancellationToken, one: true).ConfigureAwait(false);
        switch (reply)
        {
            case Reply.Event told:
                return told.Told;
            case Reply.Notice or Reply.Status:
                _ = Took(reply);
                return null;
            case Reply.Error error:
                throw Failed(error.Failure);
            default:
                throw Lose(ConnectionFailure.Of("[DESIGN] the server answered no request"));
        }
    }
}
