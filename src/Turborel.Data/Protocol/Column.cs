// A column of a result, as Prepared and Description describe it: named
// flag, its name if named, its data type, not null flag
// [PROTOCOL Composite fields].

using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>A column of a query's rows.</summary>
/// <param name="Name">Its name, if it has one.</param>
/// <param name="Type">Its data type.</param>
/// <param name="NotNull">Whether it is constrained NOT NULL.</param>
public sealed record Column(string? Name, DataType Type, bool NotNull)
{
    /// <summary>Gets the type it was declared with, if the server told it, granted <c>declared-types</c>.</summary>
    public DeclaredType? Declared { get; init; }

    /// <summary>Gets its type as SQL writes it: as it was declared, if told; else as its tag and parameters say, so INTEGER is NUMERIC(10,0).</summary>
    public string TypeWritten => Declared?.Written ?? SqlText.Written(Type);

    /// <summary>Reads a column.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The column.</returns>
    internal static Column Read(WireReader r)
    {
        string? name = r.ReadFlag() ? r.ReadString() : null;
        DataType type = DataTypeCodec.Read(r);
        return new Column(name, type, r.ReadFlag());
    }

    /// <summary>Writes the column, as the server does; for tests and for export.</summary>
    /// <param name="w">The writer.</param>
    internal void Write(WireWriter w)
    {
        w.WriteFlag(Name is not null);
        if (Name is not null)
        {
            w.WriteString(Name);
        }

        DataTypeCodec.Write(w, Type);
        w.WriteFlag(NotNull);
    }
}
