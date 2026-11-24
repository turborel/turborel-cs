// A connection's frames [PROTOCOL Frames]: each request encoded and sent,
// compressed once compression is granted [PROTOCOL Compressed frames];
// each reply read, inflated if it came compressed, and decoded, a Notice,
// a Status or a debugger's Event taken on the way [PROTOCOL Event 'W'];
// and the login, Challenge by Answer until Ready or an Error [PROTOCOL
// Opening a connection].

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Transport;

namespace Turborel.Data.Session;

/// <summary>The connection's frames and its login.</summary>
public sealed partial class NativeConnection
{
    /// <summary>The most of a reply's body reserved before its bytes arrive; past it, the buffer grows as they do.</summary>
    private const int Reserved = 64 * 1024;

    private readonly byte[] _first = new byte[1];

    private bool _loggedIn;

    // The read of a frame's first byte a debugger's wait began and left
    // running as its time ran out, so nothing of the stream is lost: the
    // next frame read takes it first.
    private Task<int>? _peeked;

    /// <summary>Sends <paramref name="request"/>, compressed if it may be.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The failure that lost the connection, before or as it is written.</exception>
    internal async Task SendAsync(Request request, CancellationToken cancellationToken)
    {
        // Until a copy ends, only its rows, its end and Ping go [PROTOCOL CopyIn 'I'].
        if (Copying && request is not (Request.CopyData or Request.CopyEnd or Request.Ping))
        {
            throw Misuse.Of("[DESIGN] a copy is under way: finish or abandon it first");
        }

        var w = new WireWriter();
        request.Encode(w);
        byte[] bytes = w.ToArray();
        if (_told.CompressFrom is int threshold)
        {
            w.Clear();
            _ = Compression.WriteFrame(w, bytes[0], bytes.AsSpan(FrameHeader.Size), threshold);
            bytes = w.ToArray();
        }

        await WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes frames of requests already encoded, at once, each compressed if it may be: a pipeline's window.</summary>
    /// <param name="frames">The frames.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal async Task WriteFramesAsync(IReadOnlyList<byte[]> frames, CancellationToken cancellationToken)
    {
        // A pipeline's requests wait for a copy to end, as any request does.
        if (Copying)
        {
            throw Misuse.Of("[DESIGN] a copy is under way: finish or abandon it first");
        }

        var w = new WireWriter();
        foreach (byte[] frame in frames)
        {
            if (_told.CompressFrom is int threshold)
            {
                _ = Compression.WriteFrame(w, frame[0], frame.AsSpan(FrameHeader.Size), threshold);
            }
            else
            {
                w.WriteRaw(frame);
            }
        }

        await WriteAsync(w.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes requests already encoded, at once.</summary>
    /// <param name="bytes">The frames.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        ThrowIfLost();
        try
        {
            await Waits.Within(
                "send",
                Bound,
                async token =>
                {
                    await _stream.WriteAsync(bytes, token).ConfigureAwait(false);
                    await _stream.FlushAsync(token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException e)
        {
            throw Lose(e);
        }
    }

    /// <summary>
    /// The server's next reply, each Notice and Status before it taken; a
    /// reply with a value the client cannot take read past with the rest of
    /// its request's, unless <paramref name="one"/> says one reply answers
    /// the request [PROTOCOL Values and fields].
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <param name="one">Whether the request is answered by one reply, after any Notice and Status, as a Describe of a cursor is.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="TurborelException">The failure that lost the connection; a value's, its request's alone.</exception>
    internal async Task<Reply> ReceiveAsync(CancellationToken cancellationToken, bool one = false)
    {
        while (true)
        {
            if (Took(await NextReplyAsync(cancellationToken, one).ConfigureAwait(false)) is { } reply)
            {
                return reply;
            }
        }
    }

    /// <summary>The failure <paramref name="failure"/> gives, the connection lost if it ends it [PROTOCOL Error 'E'].</summary>
    /// <param name="failure">The server's failure.</param>
    /// <returns>The exception, for the caller to throw.</returns>
    internal TurborelException Failed(Failure failure)
    {
        TurborelException e = failure.ToException();
        return failure.EndsConnection ? Lose(e) : e;
    }

    /// <summary>Loses the connection for <paramref name="failure"/>: it takes no more.</summary>
    /// <param name="failure">Why.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal TurborelException Lose(TurborelException failure)
    {
        _lost ??= failure;
        return failure;
    }

    private TimeSpan? ConnectTimeout { get; set; }

    // How long each wait is bounded now: the connect timeout during the
    // login, the read timeout after; a stream the program gave, or a named
    // pipe, is bounded by its maker.
    private TimeSpan? Bound => _bounded ? (_loggedIn ? ReadTimeout : ConnectTimeout) : null;

    private void ThrowIfLost()
    {
        if (_lost is not null)
        {
            throw new TurborelException(_lost.SqlCode, _lost.SqlState, _lost.Message, _lost);
        }
    }

    // A statement's end noted: whether its transaction goes on, whether it
    // warned, and where its commit is; a query's rows, and a locator, are
    // read within a transaction.
    private Reply Noted(Reply reply)
    {
        switch (reply)
        {
            case Reply.Complete complete:
                var completed = Completed.Of(complete.Completion);
                (InTransaction, Warning) = (completed.InTransaction, completed.IsWarning ? completed.SqlState : null);
                _told.Position = completed.Position ?? _told.Position;
                _told.Completed = completed;
                break;
            case Reply.Error error:
                (InTransaction, Warning) = (error.Failure.Active, null);
                break;
            case Reply.Description:
                (InTransaction, Warning) = (true, null);
                break;
            case Reply.Rows or Reply.Suspended or Reply.LobData or Reply.LobLocator:
                InTransaction = true;
                break;
            case Reply.Ready:
                // Ready ends a login or a ChangeUser, which renews the
                // session as Reset does, its transaction rolled back
                // [PROTOCOL ChangeUser 'U'].
                (InTransaction, Warning) = (false, null);
                break;
            default:
                break;
        }

        return reply;
    }

    private async Task LogInAsync((string User, Mechanism Mechanism) login, ConnectOptions options, CancellationToken cancellationToken)
    {
        ConnectTimeout = options.Timeouts.Connect;
        Offer offer = options.Offer;
        try
        {
            var hello = new Request.Hello(login.User, login.Mechanism.Name, login.Mechanism.Initial(), offer.Capabilities, offer.Attributes);
            await SendAsync(hello, cancellationToken).ConfigureAwait(false);
            await AuthenticateAsync(login.Mechanism, offer, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }

        ReadTimeout = options.Timeouts.Read;
        _loggedIn = true;
    }

    private async Task AuthenticateAsync(Mechanism mechanism, Offer offer, CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (await ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                case Reply.Challenge challenge:
                    await SendAsync(new Request.Answer(mechanism.Answer(challenge.Bytes.Span)), cancellationToken).ConfigureAwait(false);
                    break;
                case Reply.Ready ready:
                    mechanism.CheckReady();
                    (Authorization, Server, Key) = (ready.Authorization, ready.Server, ready.Key);
                    // Ready at login is never compressed: compression begins after it [PROTOCOL Opening a connection].
                    _told.Grant(ready.Capabilities, offer.CompressFrom);
                    return;
                case Reply.Error error:
                    mechanism.Refused();
                    throw Lose(error.Failure.ToException());
                default:
                    throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Ready"));
            }
        }
    }

    // Whether a frame begins to arrive within `within`, without limit if
    // null: nothing of it read, the read of its first byte kept for the next
    // frame's, so a wait that runs out leaves the connection as it was, as
    // turborel-client's arrives_within does; one the program cancels first
    // throws OperationCanceledException, the connection kept the same way.
    private async Task<bool> ArrivesWithinAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        ThrowIfLost();
        _peeked ??= _stream.ReadAsync(_first.AsMemory(), CancellationToken.None).AsTask();
        TimeSpan span = within is TimeSpan given && given <= Waits.Longest ? (given < TimeSpan.Zero ? TimeSpan.Zero : given) : Timeout.InfiniteTimeSpan;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task first = await Task.WhenAny(_peeked, Task.Delay(span, stop.Token)).ConfigureAwait(false);
        await stop.CancelAsync().ConfigureAwait(false);
        if (first == _peeked)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    // A reply, unless a Notice, a Status or a debugger's Event, which is
    // taken; of a statement, its end noted.
    private Reply? Took(Reply reply)
    {
        switch (reply)
        {
            case Reply.Status status:
                _told.Settings[status.Name] = status.Value;
                return null;
            case Reply.Notice notice:
                _told.Notice(TurborelNotice.Of(notice));
                return null;
            case Reply.Event told:
                _told.Events.Enqueue(told.Told);
                return null;
            default:
                return Noted(reply);
        }
    }

    // The next frame, decoded. One read whole that does not decode is
    // malformed, which loses the connection, or holds a value the client
    // cannot take, which fails its request alone once the rest of the
    // request's replies are read past, so the next request takes its own,
    // as turborel-client's frames take it.
    private async Task<Reply> NextReplyAsync(CancellationToken cancellationToken, bool one = false)
    {
        (byte kind, ReadOnlyMemory<byte> body) = await NextFrameAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Reply.Decode(kind, body);
        }
        catch (TurborelException e)
        {
            Skipped(kind, body, e);
            if (!one)
            {
                await DrainedAsync(kind, body, cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
    }

    // The next frame, its kind and body, inflated if it came compressed;
    // the connection lost if none comes whole, or it is malformed.
    private async Task<(byte Kind, ReadOnlyMemory<byte> Body)> NextFrameAsync(CancellationToken cancellationToken)
    {
        ThrowIfLost();
        try
        {
            (byte kind, ReadOnlyMemory<byte> body) = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            return Compression.Received(kind, body, _told.CompressFrom is not null, MaxBody);
        }
        catch (TurborelException e)
        {
            throw Lose(e);
        }
    }

    // A frame that did not decode, for failure: the connection lost if the
    // frame is malformed; else, of a Complete, what its head says noted.
    private void Skipped(byte kind, ReadOnlyMemory<byte> body, TurborelException failure)
    {
        if (!ValueFailure.IsValues(failure))
        {
            throw Lose(failure);
        }

        if (Reply.CompletedHead(kind, body) is { } head)
        {
            _ = Noted(new Reply.Complete(head));
        }
    }

    // Reads past the rest of a request's replies, once one held a value
    // the client cannot take, up to the one that ends them, each taken or
    // skipped as that one was.
    private async Task DrainedAsync(byte kind, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        while (!Reply.EndsRequest(kind, body))
        {
            (kind, body) = await NextFrameAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _ = Took(Reply.Decode(kind, body));
            }
            catch (TurborelException e)
            {
                Skipped(kind, body, e);
            }
        }
    }

    private Task<(byte Kind, ReadOnlyMemory<byte> Body)> ReadFrameAsync(CancellationToken cancellationToken)
    {
        return Waits.Within(
            "receive",
            Bound,
            async token =>
            {
                byte[] head = new byte[FrameHeader.Size];
                if (await ReadHeadAsync(head, token).ConfigureAwait(false) < head.Length)
                {
                    throw new EndOfStreamException("the server closed the connection");
                }

                var header = FrameHeader.Decode(head, MaxBody);
                return (header.Kind, await ReadBodyAsync((int)header.Length, token).ConfigureAwait(false));
            },
            cancellationToken);
    }

    // Read as its bytes arrive: a header alone reserves little.
    private async Task<ReadOnlyMemory<byte>> ReadBodyAsync(int length, CancellationToken cancellationToken)
    {
        byte[] body = new byte[Math.Min(length, Reserved)];
        int read = 0;
        while (read < length)
        {
            if (read == body.Length)
            {
                Array.Resize(ref body, Math.Min(length, body.Length * 2));
            }

            int got = await _stream.ReadAsync(body.AsMemory(read), cancellationToken).ConfigureAwait(false);
            read += got > 0 ? got : throw ConnectionFailure.Of("[DESIGN] cannot receive: the connection closed within a reply");
        }

        return body;
    }

    // A frame's header, as much of it as comes before the stream ends: the
    // first byte a debugger's wait read, if it did, then the rest.
    private async Task<int> ReadHeadAsync(byte[] head, CancellationToken cancellationToken)
    {
        int read = 0;
        if (_peeked is { } peeked)
        {
            _peeked = null;
            read = await peeked.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return 0;
            }

            head[0] = _first[0];
        }

        return read + await _stream.ReadAtLeastAsync(head.AsMemory(read), head.Length - read, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
    }

    // After Terminate: the server's replies before it closes, a frame at a
    // time, each Notice, Status and Event read past, as turborel-client's
    // terminate reads them: none, its Error, or another reply out of place.
    private async Task<Reply?> LastReplyAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            byte[] head = new byte[FrameHeader.Size];
            int got = await ReadHeadAsync(head, cancellationToken).ConfigureAwait(false);
            if (got == 0)
            {
                return null;
            }

            if (got < head.Length)
            {
                throw ConnectionFailure.Of("[DESIGN] cannot receive: the connection closed within a reply");
            }

            var header = FrameHeader.Decode(head, MaxBody);
            ReadOnlyMemory<byte> body = await ReadBodyAsync((int)header.Length, cancellationToken).ConfigureAwait(false);
            var reply = Reply.Decode(header.Kind, body);
            if (reply is not (Reply.Notice or Reply.Status or Reply.Pong or Reply.Event))
            {
                return reply;
            }
        }
    }
}
