// A dynamic parameter, as Prepared describes it: varying flag, then its
// data type, written all the same [PROTOCOL Composite fields].

using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>A statement's dynamic parameter, a <c>?</c>.</summary>
/// <param name="Varying">
/// Whether the server gives it CHARACTER VARYING of the longest length, as
/// it does of one whose type the statement does not decide.
/// </param>
/// <param name="Type">Its data type.</param>
public sealed record Parameter(bool Varying, DataType Type)
{
    /// <summary>Reads a parameter.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The parameter.</returns>
    internal static Parameter Read(WireReader r)
    {
        bool varying = r.ReadFlag();
        return new Parameter(varying, DataTypeCodec.Read(r));
    }

    /// <summary>Writes the parameter, as the server does; for tests.</summary>
    /// <param name="w">The writer.</param>
    internal void Write(WireWriter w)
    {
        w.WriteFlag(Varying);
        DataTypeCodec.Write(w, Type);
    }
}
