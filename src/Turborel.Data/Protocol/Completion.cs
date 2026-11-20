// A statement's completion, the Complete reply: SQLCODE, SQLSTATE and
// whether a transaction is active, then its tail of a commit's position,
// more to come, rows, generated values, conditions and time
// [PROTOCOL Complete 'K'].

using System.Collections.Generic;

namespace Turborel.Data.Protocol;

/// <summary>A statement's completion, each field of its tail absent where not told.</summary>
internal sealed record Completion
{
    /// <summary>Gets its SQLCODE, 0 or 100.</summary>
    internal int SqlCode { get; init; }

    /// <summary>Gets its SQLSTATE: 00000, a warning's of class 01, or 02000.</summary>
    internal string SqlState { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether a transaction is active after it.</summary>
    internal bool Active { get; init; }

    /// <summary>Gets the log position of the commit it made (tag 4).</summary>
    internal ulong? Position { get; init; }

    /// <summary>Gets a value indicating whether another statement's replies follow (tag 5).</summary>
    internal bool More { get; init; }

    /// <summary>Gets the rows its statement counted (tag 6).</summary>
    internal ulong? Rows { get; init; }

    /// <summary>Gets each identity value an INSERT generated, as SQL writes it (tag 16).</summary>
    internal IReadOnlyList<string> Generated { get; init; } = [];

    /// <summary>Gets each condition its statement raised (tag 17).</summary>
    internal IReadOnlyList<Failure> Conditions { get; init; } = [];

    /// <summary>Gets the server's time for the statement (tag 18).</summary>
    internal Timing? Time { get; init; }
}
