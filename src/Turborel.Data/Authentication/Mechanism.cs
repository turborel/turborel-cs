// A client's side of a mechanism of authentication [PROTOCOL Mechanisms]
// [CLIENT Connecting]: its name, its first bytes, its answer to each
// challenge, whether what it sends is a secret not sent over plain TCP,
// and the TLS channel it may bind itself to.

using System;
using Turborel.Data.Protocol;
using Turborel.Data.Tls;

namespace Turborel.Data.Authentication;

/// <summary>What a connection proves its user by.</summary>
public abstract class Mechanism
{
    /// <summary>Gets the mechanism's name, as the server knows it.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets a value indicating whether what it sends gives away what proves
    /// the user, or lets one who overhears it guess that at leisure: if so,
    /// a connection over plain TCP refuses it before anything is sent.
    /// </summary>
    public virtual bool IsSecret => false;

    /// <summary>Gives the bytes sent with Hello.</summary>
    /// <returns>The mechanism's first bytes.</returns>
    public abstract byte[] Initial();

    /// <summary>Answers the server's challenge.</summary>
    /// <param name="challenge">The challenge.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="TurborelException">If it has no answer: the connection is then given up.</exception>
    public virtual byte[] Answer(ReadOnlySpan<byte> challenge) =>
        throw ConnectionFailure.Of($"[DESIGN] {Name} has no answer to a challenge");

    /// <summary>
    /// Checks the exchange may end as the server sends Ready: a mechanism by
    /// which the server proves itself too, as SCRAM's, refuses one that did not.
    /// </summary>
    /// <exception cref="TurborelException">If the server has not proved itself.</exception>
    public virtual void CheckReady()
    {
        // Most mechanisms ask nothing of the server.
    }

    /// <summary>
    /// Takes the TLS channel the connection about to carry it is secured by:
    /// a mechanism that binds itself to its channel, as SCRAM-SHA-256-PLUS
    /// does, keeps it for this connection.
    /// </summary>
    /// <param name="channel">What the handshake gave.</param>
    public virtual void Bind(TlsChannel channel)
    {
        // Most mechanisms bind nothing.
    }

    /// <summary>The server refused the login: a mechanism that keeps what it learnt, as SCRAM's key cache, forgets it.</summary>
    public virtual void Refused()
    {
        // Most mechanisms keep nothing.
    }
}
