// The headings a test may cite (RULES.md R-TRACE-2), copied from turborel's
// manual at the commit this driver follows, so a citation names a section
// that exists. A change of the manual's headings changes these with it.

using System.Collections.Frozen;

namespace Turborel.Data.Tests.Support;

/// <summary>The sections of turborel's manual a citation may name.</summary>
internal static class Citations
{
    /// <summary>
    /// The headings of The Native Protocol, <c>doc/client/05-protocol.md</c>,
    /// cited as <c>[PROTOCOL heading]</c>.
    /// </summary>
    internal static readonly FrozenSet<string> Protocol = new[]
    {
        "Frames",
        "Compressed frames",
        "Values and fields",
        "Data types",
        "Values",
        "Composite fields",
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
        "A worked example",
    }.ToFrozenSet();

    /// <summary>
    /// The headings of The Client Library, <c>doc/client/02-library.md</c>,
    /// and of Settings, Pipelines and Pools,
    /// <c>doc/client/02-library-connections.md</c>, cited as
    /// <c>[CLIENT heading]</c>. No heading is in both.
    /// </summary>
    internal static readonly FrozenSet<string> Client = new[]
    {
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
