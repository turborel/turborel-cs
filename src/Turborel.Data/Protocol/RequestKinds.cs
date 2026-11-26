// The kinds of the requests a client sends, as their frames' first byte
// [PROTOCOL Requests].

namespace Turborel.Data.Protocol;

/// <summary>The kind of each request.</summary>
internal static class RequestKinds
{
    /// <summary>Hello, <c>H</c>.</summary>
    internal const byte Hello = (byte)'H';

    /// <summary>Answer, <c>A</c>.</summary>
    internal const byte Answer = (byte)'A';

    /// <summary>Query, <c>Q</c>.</summary>
    internal const byte Query = (byte)'Q';

    /// <summary>Prepare, <c>P</c>.</summary>
    internal const byte Prepare = (byte)'P';

    /// <summary>Execute, <c>E</c>.</summary>
    internal const byte Execute = (byte)'E';

    /// <summary>Fetch, <c>F</c>.</summary>
    internal const byte Fetch = (byte)'F';

    /// <summary>Close, <c>C</c>.</summary>
    internal const byte Close = (byte)'C';

    /// <summary>Deallocate, <c>D</c>.</summary>
    internal const byte Deallocate = (byte)'D';

    /// <summary>Lob, <c>L</c>.</summary>
    internal const byte Lob = (byte)'L';

    /// <summary>Cancel, <c>Z</c>.</summary>
    internal const byte Cancel = (byte)'Z';

    /// <summary>Terminate, <c>X</c>.</summary>
    internal const byte Terminate = (byte)'X';

    /// <summary>Describe, <c>Y</c>.</summary>
    internal const byte Describe = (byte)'Y';

    /// <summary>Ping, <c>G</c>.</summary>
    internal const byte Ping = (byte)'G';

    /// <summary>Reset, <c>W</c>.</summary>
    internal const byte Reset = (byte)'W';

    /// <summary>Debug, <c>V</c>, with debugger <i>(extension)</i>.</summary>
    internal const byte Debug = (byte)'V';

    /// <summary>ChangeUser, <c>U</c>.</summary>
    internal const byte ChangeUser = (byte)'U';

    /// <summary>CopyIn, <c>I</c>.</summary>
    internal const byte CopyIn = (byte)'I';

    /// <summary>CopyData, <c>J</c>.</summary>
    internal const byte CopyData = (byte)'J';

    /// <summary>CopyColumns, <c>O</c>, with <c>columns</c>.</summary>
    internal const byte CopyColumns = (byte)'O';

    /// <summary>CopyEnd, <c>N</c>.</summary>
    internal const byte CopyEnd = (byte)'N';
}
