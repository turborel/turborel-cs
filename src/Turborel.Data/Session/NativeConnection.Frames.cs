// A connection's frames [PROTOCOL Frames]: each request encoded and sent,
// compressed once compression is granted [PROTOCOL Compressed frames];
// each reply read, inflated if it came compressed, and decoded, a Notice
// or a Status taken on the way; and the login, Challenge by Answer until
// Ready or an Error [PROTOCOL Opening a connection].

using System;
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

    private bool _loggedIn;


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

    /// <summary>The server's next reply, each Notice and Status before it taken.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="TurborelException">The failure that lost the connection.</exception>
    internal async Task<Reply> ReceiveAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Reply reply = await NextReplyAsync(cancellationToken).ConfigureAwait(false);
            switch (reply)
            {
                case Reply.Status status:
                    _told.Settings[status.Name] = status.Value;
                    break;
                case Reply.Notice notice:
                    _told.Notice(TurborelNotice.Of(notice));
                    break;
                default:
                    return Noted(reply);
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

    private async Task<Reply> NextReplyAsync(CancellationToken cancellationToken)
    {
        ThrowIfLost();
        try
        {
            (byte kind, ReadOnlyMemory<byte> body) = await ReadFrameAsync(cancellationToken).ConfigureAwait(false);
            (kind, body) = Compression.Received(kind, body, _told.CompressFrom is not null, MaxBody);
            return Reply.Decode(kind, body);
        }
        catch (TurborelException e)
        {
            throw Lose(e);
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
                await ReadExactAsync(head, token).ConfigureAwait(false);
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

    private async Task ReadExactAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int got = await _stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            read += got > 0 ? got : throw new EndOfStreamException("the server closed the connection");
        }
    }

    // After Terminate: the server's last reply before it closes, if any.
    private async Task<Reply?> LastReplyAsync(CancellationToken cancellationToken)
    {
        byte[] head = new byte[FrameHeader.Size];
        int got = await _stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
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
        return Reply.Decode(header.Kind, body);
    }
}
