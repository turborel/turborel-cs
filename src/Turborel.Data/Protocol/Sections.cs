// The sections of The Native Protocol a message of the codec cites, each
// as a failure's message begins with it (RULES.md R-ERR-2, R-DOC-4).

namespace Turborel.Data.Protocol;

/// <summary>The citations of the protocol's sections, by their headings.</summary>
internal static class Sections
{
    /// <summary>Frames, their header and the longest body taken.</summary>
    internal const string Frames = "[PROTOCOL Frames]";

    /// <summary>Compressed frames, kind <c>z</c>.</summary>
    internal const string CompressedFrames = "[PROTOCOL Compressed frames]";

    /// <summary>The fields every message is made of.</summary>
    internal const string Fields = "[PROTOCOL Values and fields]";

    /// <summary>Data types, by their tags.</summary>
    internal const string DataTypes = "[PROTOCOL Data types]";

    /// <summary>Values, by their tags.</summary>
    internal const string Values = "[PROTOCOL Values]";

    /// <summary>Rows, columns, parameters and failures.</summary>
    internal const string CompositeFields = "[PROTOCOL Composite fields]";

    /// <summary>A message's tail of tagged fields.</summary>
    internal const string Tail = "[PROTOCOL The tail]";

    /// <summary>A Prepared's tail 20, of a CALL's parameter modes.</summary>
    internal const string ParameterModes = "[PROTOCOL Parameter modes]";

    /// <summary>The replies a client receives.</summary>
    internal const string Replies = "[PROTOCOL Replies]";
}
