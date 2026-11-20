// A pooled connection's check and reset [CLIENT Pools], as
// turborel-client's pool does them. Granted ping, the check is Ping,
// which the server answers with no worker; granted reset, the reset is
// Reset, which ends all the session holds and keeps its user, then the
// program's own statements. Of a server that grants neither, the check is
// ROLLBACK, and the reset one pipeline: the transaction rolled back, the
// statements prepared closed and destroyed, the user, schema and zone
// those of the login, then the program's statements. Either way
// autocommit is off after, and each wait is bounded.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Transport;

namespace Turborel.Data.Pooling;

/// <summary>Checks and resets a pooled connection.</summary>
internal static class PoolReset
{
    /// <summary>The most statements a borrower may leave prepared for the connection to be reset rather than closed.</summary>
    internal const uint MostStatements = 256;

    /// <summary>Gets how long a check and a reset wait on the server, at most.</summary>
    internal static TimeSpan Within { get; } = Timeouts.ConnectWithin;

    /// <summary>Checks that a connection answers.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure, if it is lost or does not answer.</exception>
    internal static Task AnswersAsync(NativeConnection connection, CancellationToken cancellationToken) =>
        BoundedAsync(connection, async () =>
        {
            if (connection.Has(Capabilities.Ping))
            {
                await connection.PingAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _ = await connection.QueryAsync("ROLLBACK", cancellationToken).ConfigureAwait(false);
            }
        });

    /// <summary>Gives a connection's session back as it logged in, and runs <paramref name="more"/>.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="more">The program's own statements.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">A connection failure; any statement's; too many statements left prepared.</exception>
    internal static Task ResetAsync(NativeConnection connection, IReadOnlyList<string> more, CancellationToken cancellationToken) =>
        BoundedAsync(connection, () => ResetNowAsync(connection, more, cancellationToken));

    // A name as a character string literal of a delimited identifier, as
    // SET SESSION AUTHORIZATION and SET SCHEMA take one.
    private static string Delimited(string name) => $"'\"{name.Replace("\"", "\"\"", StringComparison.Ordinal).Replace("'", "''", StringComparison.Ordinal)}\"'";

    // Runs run with each wait at most Within, or the connection's own
    // limit if shorter, the limit put back after; a connection whose maker
    // bounds its stream is run as it is bounded.
    private static async Task BoundedAsync(NativeConnection connection, Func<Task> run)
    {
        TimeSpan? before = connection.ReadTimeout;
        TimeSpan within = before is TimeSpan b && b < Within ? b : Within;
        bool limited = true;
        try
        {
            connection.SetReadTimeout(within);
        }
        catch (TurborelException)
        {
            limited = false;
        }

        try
        {
            await run().ConfigureAwait(false);
        }
        finally
        {
            if (limited && !connection.IsLost)
            {
                connection.SetReadTimeout(before);
            }
        }
    }

    private static async Task ResetNowAsync(NativeConnection connection, IReadOnlyList<string> more, CancellationToken cancellationToken)
    {
        if (connection.IsLost)
        {
            throw ConnectionFailure.Of("[DESIGN] a lost connection is not given back");
        }

        if (connection.Has(Capabilities.Reset))
        {
            connection.Autocommit = false;
            await connection.ResetAsync(cancellationToken).ConfigureAwait(false);
            // Every statement it prepared went with the session it had.
            connection.Renewed();
            foreach (string sql in more)
            {
                _ = await connection.QueryAsync(sql, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        uint prepared = connection.NextStatement;
        if (prepared > MostStatements)
        {
            throw ConnectionFailure.Of("[DESIGN] too many statements were left prepared to destroy");
        }

        connection.Autocommit = false;
        string login = connection.Authorization;
        Pipeline pipeline = connection.Pipeline().Query("ROLLBACK");
        for (uint number = 0; number < prepared; number++)
        {
            pipeline.Release(number);
        }

        _ = pipeline.Query($"SET SESSION AUTHORIZATION {Delimited(login)}").Query($"SET SCHEMA {Delimited(login)}").Query("SET TIME ZONE LOCAL");
        foreach (string sql in more)
        {
            _ = pipeline.Query(sql);
        }

        IReadOnlyList<Piped> outcomes = await pipeline.RunAsync(cancellationToken).ConfigureAwait(false);
        // The destroying of statements already gone may fail; nothing else may.
        IEnumerable<Piped> kept = outcomes.Take(1).Concat(outcomes.Skip(1 + ((int)prepared * 2)));
        if (kept.OfType<PipedFailed>().FirstOrDefault() is { } failed)
        {
            throw failed.Failure;
        }

        connection.Renewed();
    }
}
