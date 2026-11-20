// A notice the server sends beside a statement's replies, a warning or a
// note, never a failure [PROTOCOL Notice 'N'] [PROTOCOL Errors and
// notices]: its SQLSTATE, message and, with `diagnostics` granted, what
// it is about, as a failure's (RULES.md D9).

using Turborel.Data.Protocol;

namespace Turborel.Data;

/// <summary>A notice the server sent: a warning, or a note such as SQLSTATE 01T01.</summary>
public sealed record TurborelNotice
{
    /// <summary>Gets its SQLSTATE.</summary>
    public string SqlState { get; init; } = string.Empty;

    /// <summary>Gets its message.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets its severity, WARNING or NOTICE, if the server told it.</summary>
    public string? Severity { get; init; }

    /// <summary>Gets more than the message says, if the server told it.</summary>
    public string? Detail { get; init; }

    /// <summary>Gets what may be done about it, if the server told it.</summary>
    public string? Hint { get; init; }

    /// <summary>Gets the trigger or routine running, if the server told it.</summary>
    public string? Context { get; init; }

    /// <summary>Gets the schema of what it is about, if the server told it.</summary>
    public string? SchemaName { get; init; }

    /// <summary>Gets the table or view it is about, if the server told it.</summary>
    public string? TableName { get; init; }

    /// <summary>Gets the column it is about, if the server told it.</summary>
    public string? ColumnName { get; init; }

    /// <summary>Gets the data type of the value it is about, if the server told it.</summary>
    public string? DataTypeName { get; init; }

    /// <summary>Gets the constraint it is about, if the server told it.</summary>
    public string? ConstraintName { get; init; }

    /// <summary>Gives the notice a Notice reply carried.</summary>
    /// <param name="notice">The reply.</param>
    /// <returns>The notice.</returns>
    internal static TurborelNotice Of(Reply.Notice notice) => new()
    {
        SqlState = notice.SqlState,
        Message = notice.Message,
        Severity = notice.Diagnostics?.Severity,
        Detail = notice.Diagnostics?.Detail,
        Hint = notice.Diagnostics?.Hint,
        Context = notice.Diagnostics?.Context,
        SchemaName = notice.Diagnostics?.Schema,
        TableName = notice.Diagnostics?.Table,
        ColumnName = notice.Diagnostics?.Column,
        DataTypeName = notice.Diagnostics?.DataType,
        ConstraintName = notice.Diagnostics?.Constraint,
    };
}
