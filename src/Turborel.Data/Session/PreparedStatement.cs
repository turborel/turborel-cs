// A statement the server prepared, of one connection, by the number the
// client gave it [PROTOCOL Prepare 'P'] [CLIENT Prepared statements]:
// its dynamic parameters' types and its columns, as Prepared described
// them.

using System.Collections.Generic;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>A statement the server prepared, of the connection that prepared it.</summary>
public sealed class PreparedStatement
{
    /// <summary>Initializes a new instance of the <see cref="PreparedStatement"/> class.</summary>
    /// <param name="number">The client's number for it.</param>
    /// <param name="parameters">Its parameters' types, in order.</param>
    /// <param name="columns">Its columns, of a query; none of another.</param>
    internal PreparedStatement(uint number, IReadOnlyList<Parameter> parameters, IReadOnlyList<Column> columns)
    {
        Number = number;
        Parameters = parameters;
        Columns = columns;
    }

    /// <summary>Gets its dynamic parameters' types, in order.</summary>
    public IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Gets the columns of its rows, of a query; none of another.</summary>
    public IReadOnlyList<Column> Columns { get; }

    /// <summary>Gets the client's number for it.</summary>
    internal uint Number { get; }

    /// <inheritdoc/>
    public override string ToString() => $"PreparedStatement({Number}, {Parameters.Count} parameters, {Columns.Count} columns)";
}
