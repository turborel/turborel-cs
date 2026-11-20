// The names the server tells by Status besides the session's settings
// [PROTOCOL Status 'T']: AUTHORIZATION, SCHEMA, READ_ONLY and
// SERVER_VERSION, which the page lists, and SERVER_READ_ONLY, CATALOG and
// OWNER, which turborel's server/src/native/status.rs sends too.

namespace Turborel.Data.Session;

/// <summary>The names of what the server tells beside the session's settings.</summary>
public static class StatusNames
{
    /// <summary>The session's authorization identifier.</summary>
    public const string Authorization = "AUTHORIZATION";

    /// <summary>The session's default schema.</summary>
    public const string Schema = "SCHEMA";

    /// <summary>Whether the session's transaction is read-only, YES or NO.</summary>
    public const string ReadOnly = "READ_ONLY";

    /// <summary>The server's product and version.</summary>
    public const string ServerVersion = "SERVER_VERSION";

    /// <summary>Whether the server takes no change, YES or NO: how a client tells a replica from a primary.</summary>
    public const string ServerReadOnly = "SERVER_READ_ONLY";

    /// <summary>The session's catalog.</summary>
    public const string Catalog = "CATALOG";

    /// <summary>Whether the session's user owns the database, YES or NO.</summary>
    public const string Owner = "OWNER";
}
