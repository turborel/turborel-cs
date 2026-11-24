// The kinds of the replies a server sends, as their frames' first byte;
// a kind is read in the direction it travels, so C from the server is
// Challenge [PROTOCOL Replies] [PROTOCOL Frames].

namespace Turborel.Data.Protocol;

/// <summary>The kind of each reply.</summary>
internal static class ReplyKinds
{
    /// <summary>Challenge, <c>C</c>.</summary>
    internal const byte Challenge = (byte)'C';

    /// <summary>Ready, <c>R</c>.</summary>
    internal const byte Ready = (byte)'R';

    /// <summary>Prepared, <c>P</c>.</summary>
    internal const byte Prepared = (byte)'P';

    /// <summary>Description, <c>D</c>.</summary>
    internal const byte Description = (byte)'D';

    /// <summary>Rows, <c>B</c>.</summary>
    internal const byte Rows = (byte)'B';

    /// <summary>Suspended, <c>S</c>.</summary>
    internal const byte Suspended = (byte)'S';

    /// <summary>Complete, <c>K</c>.</summary>
    internal const byte Complete = (byte)'K';

    /// <summary>Error, <c>E</c>.</summary>
    internal const byte Error = (byte)'E';

    /// <summary>Lob, <c>L</c>.</summary>
    internal const byte Lob = (byte)'L';

    /// <summary>Notice, <c>N</c>.</summary>
    internal const byte Notice = (byte)'N';

    /// <summary>Status, <c>T</c>.</summary>
    internal const byte Status = (byte)'T';

    /// <summary>Pong, <c>G</c>.</summary>
    internal const byte Pong = (byte)'G';

    /// <summary>Debugged, <c>V</c>, with debugger <i>(extension)</i>.</summary>
    internal const byte Debugged = (byte)'V';

    /// <summary>Event, <c>W</c>, with debugger <i>(extension)</i>: sent unasked, it ends no request.</summary>
    internal const byte Event = (byte)'W';
}
