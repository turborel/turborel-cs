// What a connection asks of its session beyond statements, as the server
// granted it [CLIENT Controlling the session] [CLIENT What the server
// tells]: the notices it sent, the last statement's completion and the
// log position of the last commit, the attributes each statement carries,
// a ping [PROTOCOL Ping 'G'], a reset [PROTOCOL Reset 'W'], a change of
// user [PROTOCOL ChangeUser 'U'], and a Query of several statements
// [PROTOCOL Query 'Q'].

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>The connection's control of its session.</summary>
public sealed partial class NativeConnection
{
    /// <summary>
    /// Gets or sets what takes each notice the server sends from now on, as
    /// it comes, before the reply of the statement that raised it; with none,
    /// the last 64 are kept, for <see cref="TakeNotices"/>.
    /// </summary>
    public Action<TurborelNotice>? NoticeHandler
    {
        get => _told.Handler;
        set => _told.Handler = value;
    }

    /// <summary>Gets the log position of the last commit the server told of, with <c>commit-position</c> granted.</summary>
    public ulong? CommitPosition => _told.Position;

    /// <summary>Gets the rows the last statement changed, or the last query returned once all were read, with <c>row-counts</c> granted.</summary>
    public ulong? RowsAffected => _told.Completed?.RowCount;

    /// <summary>Gets the server's time for the last statement, in microseconds, with <c>row-counts</c> granted.</summary>
    public Timing? ServerTime => _told.Completed?.Time;

    /// <summary>Gets the identity values the last INSERT generated, in the order of its rows, as SQL writes them, with <c>generated-values</c> granted.</summary>
    public IReadOnlyList<string> LastGenerated => _told.Completed?.Generated ?? [];

    /// <summary>Gets each condition the last statement raised and completed with, with <c>conditions</c> granted.</summary>
    public IReadOnlyList<TurborelException> Conditions => _told.Completed?.Conditions ?? [];

    /// <summary>Takes the notices kept with no handler set.</summary>
    /// <returns>The notices, oldest first.</returns>
    public IReadOnlyList<TurborelNotice> TakeNotices() => _told.TakeNotices();

    /// <summary>Takes the last statement's conditions, so a program that tells them after each statement tells each once.</summary>
    /// <returns>The conditions.</returns>
    public IReadOnlyList<TurborelException> TakeConditions()
    {
        IReadOnlyList<TurborelException> taken = Conditions;
        if (_told.Completed is { } completed)
        {
            completed.Conditions = [];
        }

        return taken;
    }

    /// <summary>
    /// Sets the attributes each Query and Execute carries from now on, read in
    /// SQL by <c>QUERY_ATTRIBUTE(name)</c>; <c>traceparent</c> is the request's
    /// trace context. None, for none.
    /// </summary>
    /// <param name="attributes">The attributes, by name.</param>
    /// <exception cref="TurborelException">A connection failure, for attributes not none, without <c>query-attributes</c>.</exception>
    public void SetQueryAttributes(IReadOnlyList<KeyValuePair<string, string>> attributes)
    {
        Misuse.ThrowIfNull(attributes);
        NeedsUnless(attributes.Count == 0, Capabilities.QueryAttributes);
        _told.QueryAttributes = attributes;
    }

    /// <summary>Asks whether the server answers, with no worker of its taken.</summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure: <c>ping</c> not granted, or the server gone.</exception>
    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        Needs(Capabilities.Ping);
        await SendAsync(new Request.Ping(), cancellationToken).ConfigureAwait(false);
        _ = await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.Pong => true,
            Reply.Error error => throw Failed(error.Failure),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Pong")),
        };
    }

    /// <summary>
    /// Returns the session to its state at login: its transaction rolled
    /// back, told by notice 01T01; its prepared statements, cursors,
    /// temporary tables and settings gone; its user and cancel key kept.
    /// Statements prepared before are of no use after.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure: <c>reset</c> not granted, or the server gone.</exception>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Needs(Capabilities.Reset);
        await SendAsync(new Request.Reset(), cancellationToken).ConfigureAwait(false);
        _ = await CompletionAsync(cancellationToken).ConfigureAwait(false);
        Renewals++;
    }

    /// <summary>
    /// Authenticates the connection again as <paramref name="user"/> by
    /// <paramref name="mechanism"/>, its session renewed as that user's, as
    /// <see cref="ResetAsync"/> renews it, under a new cancel key: no
    /// transaction is active after it.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="mechanism">What proves the user.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The server's refusal, which closes the connection; a connection failure: <c>change-user</c> not granted, or the server gone.</exception>
    public async Task ChangeUserAsync(string user, Mechanism mechanism, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(mechanism);
        Needs(Capabilities.ChangeUser);
        await SendAsync(new Request.ChangeUser(user, mechanism.Name, mechanism.Initial()), cancellationToken).ConfigureAwait(false);
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
                    Renewals++;
                    return;
                case Reply.Error error:
                    // As turborel-client's change of user, the mechanism's
                    // keys are kept: the refusal closes the connection.
                    throw Failed(error.Failure);
                default:
                    throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Ready"));
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="sql"/>, several statements, in one request: the
    /// server splits it and runs each in order, the transaction ended with
    /// each under <see cref="Autocommit"/>; the first that fails stops the rest.
    /// </summary>
    /// <param name="sql">The statements.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>Each statement's outcome, and the failure that stopped the rest.</returns>
    /// <exception cref="TurborelException">A connection failure: <c>multi-statement</c> not granted, or the server gone.</exception>
    public async Task<Ran> QueryManyAsync(string sql, CancellationToken cancellationToken = default)
    {
        Needs(Capabilities.MultiStatement);
        await SendAsync(new Request.Query(sql, Autocommit, _told.Attributes, Multi: true), cancellationToken).ConfigureAwait(false);
        var outcomes = new List<Outcome>();
        while (true)
        {
            (Outcome Outcome, bool More)? next;
            try
            {
                next = await OutcomeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TurborelException failure) when (!IsLost)
            {
                return new Ran(outcomes, failure);
            }

            (Outcome outcome, bool more) = next ?? throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no result"));
            outcomes.Add(outcome);
            if (!more)
            {
                return new Ran(outcomes, null);
            }
        }
    }
}
