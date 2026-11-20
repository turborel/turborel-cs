// The driver's failure of a program's misuse of its API: a null where a
// value is needed, a wrong call (RULES.md R-ERR-1): SQLCODE -910,
// SQLSTATE TB910, as turborel names its API's misuse, so a program
// handles one kind of failure.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Turborel.Data;

/// <summary>Failures of the API's misuse.</summary>
internal static class Misuse
{
    /// <summary>The SQLCODE of the API's misuse.</summary>
    internal const int SqlCode = -910;

    /// <summary>The SQLSTATE of the API's misuse.</summary>
    internal const string SqlState = "TB910";

    /// <summary>Gives the failure of a misuse.</summary>
    /// <param name="message">What was misused, and how.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Of(string message) => new(SqlCode, SqlState, message);

    /// <summary>Fails if <paramref name="value"/> is null.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="name">Its name, as the caller wrote it.</param>
    /// <exception cref="TurborelException">SQLSTATE TB910 if it is null.</exception>
    internal static void ThrowIfNull([NotNull] object? value, [CallerArgumentExpression(nameof(value))] string name = "")
    {
        if (value is null)
        {
            throw Of($"[DESIGN] {name} is null, where a value is needed");
        }
    }
}
