// The one exception the driver reports a failure by (RULES.md D9,
// R-ERR-1): the server's failure, or one the driver found, each with its
// SQLCODE and SQLSTATE.

using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Turborel.Data;

/// <summary>
/// A failure of a statement or of a connection to a turborel server, with
/// its SQLCODE and SQLSTATE.
/// </summary>
/// <remarks>
/// The server sends a failure's SQLCODE, SQLSTATE and message; a failure
/// the driver finds itself, such as a malformed message, carries those the
/// protocol names for it. <see cref="System.Runtime.InteropServices.ExternalException.ErrorCode"/> is the
/// SQLCODE too, as ADO.NET programs read it.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "R-ERR-2: every failure carries its SQLCODE and SQLSTATE, so no constructor leaves them out.")]
public sealed class TurborelException : DbException
{
    /// <summary>
    /// Makes a failure of the SQLCODE, SQLSTATE and message given.
    /// </summary>
    /// <param name="sqlCode">The failure's SQLCODE, a negative number.</param>
    /// <param name="sqlState">The failure's SQLSTATE, five characters.</param>
    /// <param name="message">What went wrong, in plain words.</param>
    public TurborelException(int sqlCode, string sqlState, string message)
        : this(sqlCode, sqlState, message, null)
    {
    }

    /// <summary>
    /// Makes a failure of the SQLCODE, SQLSTATE and message given, caused
    /// by another exception, such as the socket's.
    /// </summary>
    /// <param name="sqlCode">The failure's SQLCODE, a negative number.</param>
    /// <param name="sqlState">The failure's SQLSTATE, five characters.</param>
    /// <param name="message">What went wrong, in plain words.</param>
    /// <param name="innerException">The exception that caused it.</param>
    public TurborelException(int sqlCode, string sqlState, string message, Exception? innerException)
        : base(message, innerException)
    {
        SqlCode = sqlCode;
        SqlState = sqlState;
        HResult = sqlCode;
    }

    /// <summary>
    /// Gets the failure's SQLCODE: a negative number, as the server's
    /// Appendix A lists it.
    /// </summary>
    public int SqlCode { get; }

    /// <summary>
    /// Gets the failure's SQLSTATE: five characters, its class the first
    /// two.
    /// </summary>
    public override string SqlState { get; }

    /// <summary>Gets the clause of a standard the failure cites, if the server cited one.</summary>
    public string? Rule { get; internal init; }

    /// <summary>Gets where in the statement it arose, in characters, if the server placed it.</summary>
    public long? Position { get; internal init; }

    /// <summary>
    /// Gets a value indicating whether the session's transaction is still
    /// active after it, as the server said; null of a failure the driver
    /// found itself.
    /// </summary>
    public bool? TransactionActive { get; internal init; }

    /// <summary>Gets its severity: ERROR, FATAL, WARNING or NOTICE, with <c>diagnostics</c> granted.</summary>
    public string? Severity { get; internal init; }

    /// <summary>Gets more than the message says, as the key a constraint refused.</summary>
    public string? Detail { get; internal init; }

    /// <summary>Gets what may be done about it.</summary>
    public string? Hint { get; internal init; }

    /// <summary>Gets the trigger or routine running, as <c>trigger S.T</c>.</summary>
    public string? Context { get; internal init; }

    /// <summary>Gets the schema of what it is about.</summary>
    public string? SchemaName { get; internal init; }

    /// <summary>Gets the table or view it is about.</summary>
    public string? TableName { get; internal init; }

    /// <summary>Gets the column it is about.</summary>
    public string? ColumnName { get; internal init; }

    /// <summary>Gets the data type of the value it is about.</summary>
    public string? DataTypeName { get; internal init; }

    /// <summary>Gets the constraint broken.</summary>
    public string? ConstraintName { get; internal init; }

    /// <summary>Gets, of an import's failure, where its record began and what of it was kept; null of any other.</summary>
    public Import.ImportFailure? Import { get; internal init; }

    /// <summary>Gives this failure again as an import's, without its position in a statement the import made.</summary>
    /// <param name="import">Where, and what was kept.</param>
    /// <returns>The failure.</returns>
    internal TurborelException OfImport(Import.ImportFailure import) => new(SqlCode, SqlState, Message, this)
    {
        Rule = Rule,
        TransactionActive = TransactionActive,
        Severity = Severity,
        Detail = Detail,
        Hint = Hint,
        Context = Context,
        SchemaName = SchemaName,
        TableName = TableName,
        ColumnName = ColumnName,
        DataTypeName = DataTypeName,
        ConstraintName = ConstraintName,
        Import = import,
    };
}
