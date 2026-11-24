// The headings a citation may name (RULES.md D6, R-TRACE-2), copied from
// Turborel's documents at the commit CONFORMANCE.md names, 1afa44c2, each
// page's title among them, so a citation names a section that exists. A
// change of those documents' headings changes these with it.

using System.Collections.Frozen;

namespace Turborel.Data.Tests.Support;

/// <summary>The sections of Turborel's documents a citation may name.</summary>
internal static class Citations
{
    /// <summary>
    /// The headings of The Native Protocol, <c>doc/client/05-protocol.md</c>,
    /// cited after the tag PROTOCOL. <c>Lob 'L'</c> is printed twice,
    /// under Requests and under Replies.
    /// </summary>
    internal static readonly FrozenSet<string> Protocol = new[]
    {
        "The Native Protocol",
        "Frames",
        "Compressed frames",
        "Values and fields",
        "Data types",
        "Values",
        "Declared types",
        "Composite fields",
        "JSON values",
        "The tail",
        "Opening a connection",
        "Mechanisms",
        "Capabilities",
        "Requests",
        "Hello 'H'",
        "Answer 'A'",
        "Query 'Q'",
        "Prepare 'P'",
        "Execute 'E'",
        "Fetch 'F'",
        "Close 'C'",
        "Deallocate 'D'",
        "Lob 'L'",
        "Cancel 'Z'",
        "Terminate 'X'",
        "Describe 'Y'",
        "Ping 'G'",
        "Reset 'W'",
        "ChangeUser 'U'",
        "CopyIn 'I'",
        "CopyData 'J'",
        "CopyEnd 'N'",
        "Replies",
        "Challenge 'C'",
        "Ready 'R'",
        "Prepared 'P'",
        "Parameter modes",
        "Description 'D'",
        "Rows 'B'",
        "Suspended 'S'",
        "Complete 'K'",
        "Error 'E'",
        "Notice 'N'",
        "Status 'T'",
        "Pong 'G'",
        "Conversations",
        "Errors and notices",
        "Sample frames",
        "A worked example",
    }.ToFrozenSet();

    /// <summary>
    /// The headings of The Client Library, <c>doc/client/02-library.md</c>,
    /// and of Settings, Pipelines and Pools,
    /// <c>doc/client/02-library-connections.md</c>, cited
    /// after the tag CLIENT. No heading is in both.
    /// </summary>
    internal static readonly FrozenSet<string> Client = new[]
    {
        "The Client Library",
        "Connecting",
        "Timeouts",
        "Running statements",
        "Prepared statements",
        "Large objects",
        "XML values",
        "UUIDs",
        "Importing",
        "What a connection asks for",
        "What the server tells",
        "Controlling the session",
        "Copying and exporting",
        "The SQL-agent",
        "Settings, Pipelines and Pools",
        "Connecting by settings",
        "Where settings come from",
        "The connection URI",
        "The service file",
        "The password file",
        "Several servers",
        "TLS by mode",
        "Pipelines",
        "Pools",
        "The SCRAM key cache",
    }.ToFrozenSet();
}
