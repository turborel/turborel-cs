// The failure of a read, a write or a connect: a connection failure,
// SQLCODE -919 and SQLSTATE 08006, saying whether the server did not
// answer in time, the BCL's exception kept as its cause [CLIENT Timeouts].

using System;
using Turborel.Data.Protocol;

namespace Turborel.Data.Transport;

/// <summary>Makes the failures of a connection's input and output.</summary>
internal static class IoFailure
{
    /// <summary>The failure of <paramref name="what"/>, as connect, send or receive, for <paramref name="cause"/>.</summary>
    /// <param name="what">What could not be done.</param>
    /// <param name="cause">The exception that stopped it.</param>
    /// <param name="timedOut">Whether a wait ran out.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Of(string what, Exception cause, bool timedOut)
    {
        string why = timedOut ? "the server did not answer in time" : cause.Message;
        return new TurborelException(
            ConnectionFailure.SqlCode,
            ConnectionFailure.SqlState,
            $"[DESIGN] cannot {what}: {why}",
            cause);
    }
}
