// A statement's completion, as its Complete said [PROTOCOL Complete 'K']
// [CLIENT What the server tells]: its SQLCODE and SQLSTATE, a warning's
// among them, and what the capabilities granted add: the rows it changed,
// the identity values it generated, the conditions it raised, the
// server's time and the log position of its commit.

using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>A statement's completion.</summary>
public sealed class Completed
{
    private const string WarningClass = "01";

    private Completed(Completion completion)
    {
        SqlCode = completion.SqlCode;
        SqlState = completion.SqlState;
        InTransaction = completion.Active;
        Position = completion.Position;
        RowCount = completion.Rows;
        Generated = completion.Generated;
        Conditions = [.. completion.Conditions.Select(c => c.ToException())];
        Time = completion.Time;
    }

    /// <summary>Gets its SQLCODE: 0, or 100 of a query or a change of no row.</summary>
    public int SqlCode { get; }

    /// <summary>Gets its SQLSTATE: 00000, 02000, or a warning's, of class 01.</summary>
    public string SqlState { get; }

    /// <summary>Gets a value indicating whether it completed with a warning, SQLSTATE class 01.</summary>
    public bool IsWarning => SqlState.StartsWith(WarningClass, System.StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether the session's transaction goes on after it.</summary>
    public bool InTransaction { get; }

    /// <summary>Gets the log position of the commit it made, with <c>commit-position</c> granted.</summary>
    public ulong? Position { get; }

    /// <summary>Gets the rows it changed, or a query's rows in all, with <c>row-counts</c> granted.</summary>
    public ulong? RowCount { get; }

    /// <summary>Gets each identity value an INSERT generated, as SQL writes it, in the order of its rows, with <c>generated-values</c> granted.</summary>
    public IReadOnlyList<string> Generated { get; }

    /// <summary>Gets each condition it raised, with <c>conditions</c> granted.</summary>
    public IReadOnlyList<TurborelException> Conditions { get; internal set; }

    /// <summary>Gets the server's time for it, with <c>row-counts</c> granted.</summary>
    public Timing? Time { get; }

    /// <summary>Gives a completion of <paramref name="code"/> and <paramref name="state"/> alone, as the driver completes a statement it runs itself.</summary>
    /// <param name="code">The SQLCODE.</param>
    /// <param name="state">The SQLSTATE.</param>
    /// <returns>The completion.</returns>
    internal static Completed Of(int code, string state) => new(new Completion { SqlCode = code, SqlState = state });

    /// <summary>Gives the completion a Complete carried.</summary>
    /// <param name="completion">The Complete's completion.</param>
    /// <returns>The completion.</returns>
    internal static Completed Of(Completion completion) => new(completion);

    /// <inheritdoc/>
    public override string ToString() => $"Completed(SQLCODE {SqlCode}, SQLSTATE {SqlState})";
}
