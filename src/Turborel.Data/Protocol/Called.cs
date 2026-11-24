// Tail 20 of a Prepared, granted parameter-modes, of a CALL of an
// SQL-invoked procedure [PROTOCOL Parameter modes]: the procedure's
// specific name, its schema and name, then every `?` of the statement in
// the order written, OUT ones too, each its parameter's mode u8 (1 IN,
// 2 INOUT, 4 OUT), its ordinal position u32 from 1, and its type, as a
// head's parameter is. A mode of another code, or a position of 0, fails
// the connection, as turborel-protocol's called.rs reads it.

using System.Collections.Generic;
using System.Globalization;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>What a Prepared of a CALL tells of the procedure and of each <c>?</c>.</summary>
/// <param name="Procedure">The procedure's specific name.</param>
/// <param name="Parameters">Each <c>?</c> of the statement, in the order written.</param>
internal sealed record Called(SqlName Procedure, IReadOnlyList<DynamicParameter> Parameters)
{
    /// <summary>Reads the field after its tag.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The field.</returns>
    /// <exception cref="TurborelException">A connection failure, SQLCODE -919 and SQLSTATE 08006, of a mode of no code or a position of 0.</exception>
    internal static Called Read(WireReader r) =>
        new(new SqlName(r.ReadString(), r.ReadString()), Reply.ReadList(r, ReadParameter));

    /// <summary>Writes the field, its tag first, as the server does; for tests.</summary>
    /// <param name="w">The writer.</param>
    internal void Write(WireWriter w)
    {
        w.WriteU8(TailTags.Called);
        w.WriteString(Procedure.Schema);
        w.WriteString(Procedure.Name);
        w.WriteU32((uint)Parameters.Count);
        foreach (DynamicParameter p in Parameters)
        {
            w.WriteU8((byte)p.Mode);
            w.WriteU32(p.Ordinal ?? 0);
            p.Parameter.Write(w);
        }
    }

    // One `?`: its mode, its parameter's ordinal position and its type.
    private static DynamicParameter ReadParameter(WireReader r)
    {
        byte code = r.ReadU8();
        if (code is not ((byte)ParameterMode.In or (byte)ParameterMode.InOut or (byte)ParameterMode.Out))
        {
            throw r.Malformed(Sections.ParameterModes, string.Create(CultureInfo.InvariantCulture, $"{code} is no parameter mode's code"));
        }

        uint ordinal = r.ReadU32();
        return ordinal == 0
            ? throw r.Malformed(Sections.ParameterModes, "a parameter's ordinal position is from 1")
            : new DynamicParameter((ParameterMode)code, ordinal, Parameter.Read(r));
    }
}
