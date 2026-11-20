// A failure as the server sends it, in an Error or as a condition: its
// SQLCODE, SQLSTATE and message, the rule it cites, its position, whether
// a transaction is active after it, and its diagnostics
// [PROTOCOL Composite fields].

namespace Turborel.Data.Protocol;

/// <summary>A failure, as the server wrote it.</summary>
internal sealed record Failure
{
    /// <summary>The SQLSTATE class of a connection exception.</summary>
    private const string ConnectionClass = "08";

    /// <summary>The SQLSTATE class of an invalid authorization.</summary>
    private const string AuthorizationClass = "28";

    /// <summary>Connection does not exist: a connection exception of another connection.</summary>
    private const string DoesNotExist = "08003";

    /// <summary>Gets its SQLCODE.</summary>
    internal int SqlCode { get; init; }

    /// <summary>Gets its SQLSTATE, five characters.</summary>
    internal string SqlState { get; init; } = string.Empty;

    /// <summary>Gets what went wrong, in plain words.</summary>
    internal string Message { get; init; } = string.Empty;

    /// <summary>Gets the clause of a standard it cites, if it cites one.</summary>
    internal string? Rule { get; init; }

    /// <summary>Gets where in the statement, in characters, if known.</summary>
    internal ulong? Position { get; init; }

    /// <summary>Gets a value indicating whether the session's transaction is active after it.</summary>
    internal bool Active { get; init; }

    /// <summary>Gets its diagnostics, tags 7 to 15, if any were told.</summary>
    internal Diagnostics? Diagnostics { get; init; }

    /// <summary>Reads a failure, its tail of diagnostics running to the end of what the reader holds.</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The failure.</returns>
    internal static Failure Read(WireReader r)
    {
        int sqlCode = r.ReadI32();
        string sqlState = r.ReadString();
        string message = r.ReadString();
        string? rule = r.ReadFlag() ? r.ReadString() : null;
        ulong? position = r.ReadFlag() ? r.ReadU64() : null;
        bool active = r.ReadFlag();
        return new Failure
        {
            SqlCode = sqlCode,
            SqlState = sqlState,
            Message = message,
            Rule = rule,
            Position = position,
            Active = active,
            Diagnostics = Diagnostics.Read(r),
        };
    }

    /// <summary>
    /// Gets a value indicating whether the connection it came on is lost:
    /// a connection exception, SQLSTATE class 08 but 08003, which names
    /// another connection; or credentials refused as it is made, class 28
    /// of a connection exception's SQLCODE [PROTOCOL Error 'E'].
    /// </summary>
    internal bool EndsConnection =>
        (SqlState.StartsWith(ConnectionClass, System.StringComparison.Ordinal) && SqlState != DoesNotExist)
        || (SqlCode == ConnectionFailure.SqlCode && SqlState.StartsWith(AuthorizationClass, System.StringComparison.Ordinal));

    /// <summary>Gives the failure as the driver's error, all it says carried.</summary>
    /// <returns>The exception.</returns>
    internal TurborelException ToException() => new(SqlCode, SqlState, Message)
    {
        Rule = Rule,
        Position = Position is ulong p && p <= long.MaxValue ? (long)p : null,
        TransactionActive = Active,
        Severity = Diagnostics?.Severity,
        Detail = Diagnostics?.Detail,
        Hint = Diagnostics?.Hint,
        Context = Diagnostics?.Context,
        SchemaName = Diagnostics?.Schema,
        TableName = Diagnostics?.Table,
        ColumnName = Diagnostics?.Column,
        DataTypeName = Diagnostics?.DataType,
        ConstraintName = Diagnostics?.Constraint,
    };

    /// <summary>Writes the failure, as the server does; for tests and for export.</summary>
    /// <param name="w">The writer.</param>
    internal void Write(WireWriter w)
    {
        w.WriteI32(SqlCode);
        w.WriteString(SqlState);
        w.WriteString(Message);
        w.WriteFlag(Rule is not null);
        if (Rule is not null)
        {
            w.WriteString(Rule);
        }

        w.WriteFlag(Position is not null);
        if (Position is ulong position)
        {
            w.WriteU64(position);
        }

        w.WriteFlag(Active);
        Diagnostics?.Write(w);
    }
}
