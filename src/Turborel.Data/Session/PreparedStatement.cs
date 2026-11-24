// A statement the server prepared, of one connection, by the number the
// client gave it [PROTOCOL Prepare 'P'] [CLIENT Prepared statements]:
// the types of the dynamic parameters an execution gives values for, its
// columns, and each `?` with the mode and ordinal position of the
// parameter it stands in, as Prepared described them and as
// turborel-client's Statement gives them [PROTOCOL Parameter modes].

using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Session;

/// <summary>A statement the server prepared, of the connection that prepared it.</summary>
public sealed class PreparedStatement
{
    private readonly Called? _called;

    /// <summary>Initializes a new instance of the <see cref="PreparedStatement"/> class.</summary>
    /// <param name="number">The client's number for it.</param>
    /// <param name="inputs">The types of the parameters an execution gives values for, in order.</param>
    /// <param name="columns">Its columns, of a query or a CALL; none of another.</param>
    /// <param name="called">Of a CALL, its procedure and each <c>?</c>'s mode, as a server granting parameter-modes tells them.</param>
    internal PreparedStatement(uint number, IReadOnlyList<Parameter> inputs, IReadOnlyList<Column> columns, Called? called = null)
    {
        Number = number;
        Inputs = inputs;
        Columns = columns;
        _called = called;
        Parameters = called?.Parameters ?? [.. inputs.Select(DynamicParameter.Input)];
    }

    /// <summary>Gets the types of the dynamic parameters an execution gives values for, in order: of a CALL not those of OUT parameters.</summary>
    public IReadOnlyList<Parameter> Inputs { get; }

    /// <summary>
    /// Gets each of its dynamic parameters, in the order written: its type
    /// and, of a CALL, the mode and ordinal position of the parameter it
    /// stands in, as a server granting <c>parameter-modes</c> tells them.
    /// Told none, each is an input of no position.
    /// </summary>
    public IReadOnlyList<DynamicParameter> Parameters { get; }

    /// <summary>Gets, of a CALL of an SQL-invoked procedure, the procedure's specific name, as a server granting <c>parameter-modes</c> tells it.</summary>
    public SqlName? Procedure => _called?.Procedure;

    /// <summary>Gets the columns of its rows: a query's; of a CALL, one for each <c>?</c> of an OUT or INOUT parameter; none of another statement.</summary>
    public IReadOnlyList<Column> Columns { get; }

    /// <summary>Gets a value indicating whether the server told its parameters' modes.</summary>
    internal bool ModesTold => _called is not null;

    /// <summary>Gets the client's number for it.</summary>
    internal uint Number { get; }

    /// <inheritdoc/>
    public override string ToString() => $"PreparedStatement({Number}, {Parameters.Count} parameters, {Columns.Count} columns)";
}
