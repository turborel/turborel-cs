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
}
