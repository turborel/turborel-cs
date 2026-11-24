// Whether a server takes changes [CLIENT Several servers], as
// turborel-client's role reads it: a server alone does, and of a
// replicated group its primary; a replica following the group's log takes
// none. A server that grants status tells every session by
// SERVER_READ_ONLY, read with no query; of one that does not, the role is
// read from INFORMATION_SCHEMA.REPLICAS, which shows it to the database's
// owner alone, so another session cannot see a replica's role.

using System;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Session;

namespace Turborel.Data.Configuration;

/// <summary>What a server is, to a connection's session.</summary>
public enum ServerRoleKind
{
    /// <summary>It takes changes.</summary>
    ReadWrite,

    /// <summary>It takes none: a replica following its group.</summary>
    ReadOnly,

    /// <summary>A replica whose role the session cannot see.</summary>
    Unknown,
}

/// <summary>A server's role, as a connection's session sees it.</summary>
public static class ServerRole
{
    /// <summary>The query of a server's own role in its group.</summary>
    public const string RoleQuery = "SELECT ROLE FROM INFORMATION_SCHEMA.REPLICAS WHERE IS_SELF = 'YES'";

    private const string Yes = "YES";
    private const string Primary = "PRIMARY";

    // The SQLSTATE class of a table that does not exist, as a server alone
    // answers RoleQuery: of a syntax error or access rule violation
    // [ISO/IEC 9075-2:2023 §24.1].
    private const string NoSuchTableClass = "42";

    /// <summary>Gives the role of <paramref name="connection"/>'s server, as its session sees it: as SERVER_READ_ONLY last told it, else asked as a statement that is its own transaction.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The role.</returns>
    /// <exception cref="TurborelException">A connection failure; the query's, but of a server alone, which has no REPLICAS.</exception>
    public static async Task<ServerRoleKind> OfAsync(NativeConnection connection, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(connection);
        if (connection.Setting(StatusNames.ServerReadOnly) is { } told)
        {
            return told == Yes ? ServerRoleKind.ReadOnly : ServerRoleKind.ReadWrite;
        }

        bool autocommit = connection.Autocommit;
        connection.Autocommit = true;
        try
        {
            return await connection.QueryAsync(RoleQuery, cancellationToken).ConfigureAwait(false) is RowsOutcome { Rows: [var row, ..] }
                ? (row[0] as string)?.TrimEnd() == Primary ? ServerRoleKind.ReadWrite : ServerRoleKind.ReadOnly
                : ServerRoleKind.Unknown;
        }
        catch (TurborelException e) when (!connection.IsLost && e.SqlState.StartsWith(NoSuchTableClass, StringComparison.Ordinal))
        {
            // A server alone has no REPLICAS, and takes changes.
            return ServerRoleKind.ReadWrite;
        }
        finally
        {
            connection.Autocommit = autocommit;
        }
    }

    /// <summary>Gives a role in words.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The words.</returns>
    public static string Describe(ServerRoleKind role) => role switch
    {
        ServerRoleKind.ReadWrite => "read-write",
        ServerRoleKind.ReadOnly => "read-only",
        _ => "of a role this session cannot see",
    };

    /// <summary>Refuses a connection, ended, if its server is not of the role asked, SQLSTATE 08001.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="target">The role asked.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal static async Task CheckAsync(NativeConnection connection, TargetRole target, CancellationToken cancellationToken)
    {
        if (target == TargetRole.Any)
        {
            return;
        }

        ServerRoleKind wanted = target == TargetRole.ReadWrite ? ServerRoleKind.ReadWrite : ServerRoleKind.ReadOnly;
        ServerRoleKind found;
        try
        {
            found = await OfAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (found != wanted)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw ConnectionOptions.Invalid($"[DESIGN] the server is {Describe(found)}, and {Describe(wanted)} is asked");
        }
    }
}
