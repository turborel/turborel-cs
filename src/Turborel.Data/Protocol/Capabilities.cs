// The capabilities a client asks for in Hello, by name, and the server
// grants in Ready, in the order of the protocol's table
// [PROTOCOL Capabilities].

using System.Collections.Generic;

namespace Turborel.Data.Protocol;

/// <summary>The names of the capabilities.</summary>
internal static class Capabilities
{
    /// <summary>A Notice for each condition of class 01 and each notice of the server's own.</summary>
    internal const string Notices = "notices";

    /// <summary>A Status of each session setting, as the session begins and as a statement changes one.</summary>
    internal const string Status = "status";

    /// <summary>An Error's failure carries tags 7 to 15.</summary>
    internal const string Diagnostics = "diagnostics";

    /// <summary>The request Describe.</summary>
    internal const string Describe = "describe";

    /// <summary>Several cursors of one prepared statement, numbered by tag 3.</summary>
    internal const string Cursors = "cursors";

    /// <summary>The request Ping, answered by Pong.</summary>
    internal const string Ping = "ping";

    /// <summary>The request Reset.</summary>
    internal const string Reset = "reset";

    /// <summary>The request ChangeUser.</summary>
    internal const string ChangeUser = "change-user";

    /// <summary>Attributes, tag 1, on Query and Execute.</summary>
    internal const string QueryAttributes = "query-attributes";

    /// <summary>Says Hello carries attributes; every server reads them, granted or not.</summary>
    internal const string ConnectionAttributes = "connection-attributes";

    /// <summary>A Query of several statements, tag 2, and tag 5 on its Completes.</summary>
    internal const string MultiStatement = "multi-statement";

    /// <summary>Compressed frames, both ways.</summary>
    internal const string Compression = "compression";

    /// <summary>A Complete that committed carries the commit's log position, tag 4.</summary>
    internal const string CommitPosition = "commit-position";

    /// <summary>The requests CopyIn, CopyData and CopyEnd.</summary>
    internal const string Copy = "copy";

    /// <summary>A Complete carries its rows, tag 6, and the server's time, tag 18.</summary>
    internal const string RowCounts = "row-counts";

    /// <summary>A Complete of an INSERT carries the identity values it generated, tag 16.</summary>
    internal const string GeneratedValues = "generated-values";

    /// <summary>An Execute that ends the transaction ends it with its cursor.</summary>
    internal const string FetchEnds = "fetch-ends";

    /// <summary>A Complete carries its statement's conditions, tag 17, in place of Notices.</summary>
    internal const string Conditions = "conditions";

    /// <summary>A Prepared and a Description carry each column's declared type, tag 19.</summary>
    internal const string DeclaredTypes = "declared-types";

    /// <summary>A Prepared of a CALL carries its procedure and each <c>?</c> with its parameter's mode, tag 20.</summary>
    internal const string ParameterModes = "parameter-modes";

    /// <summary>The request Debug, answered by Debugged or a Complete, and the Event sent unasked <i>(extension)</i>.</summary>
    internal const string Debugger = "debugger";

    /// <summary>The request CopyColumns, with <c>copy</c>: rows of a copy by column, as text.</summary>
    internal const string Columns = "columns";

    /// <summary>Every capability, in the order of the protocol's table.</summary>
    internal static readonly IReadOnlyList<string> All =
    [
        Notices, Status, Diagnostics, Describe, Cursors, Ping, Reset, ChangeUser, QueryAttributes,
        ConnectionAttributes, MultiStatement, Compression, CommitPosition, Copy, RowCounts, GeneratedValues,
        FetchEnds, Conditions, DeclaredTypes, ParameterModes, Debugger, Columns,
    ];
}
