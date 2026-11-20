// A value the client cannot take, in a frame read whole [PROTOCOL Values
// and fields]: it fails its request alone with the value's own error, not
// of class 08, once the rest of the request's replies are read past, and
// the connection goes on, as turborel-client's frames take it. A frame that
// is itself malformed loses the connection (ConnectionFailure).

namespace Turborel.Data.Protocol;

/// <summary>The failures of values the client cannot take.</summary>
internal static class ValueFailure
{
    /// <summary>The SQLCODE of a damaged value, turborel's corrupt.</summary>
    internal const int CorruptSqlCode = -916;

    /// <summary>The SQLSTATE of a damaged value.</summary>
    internal const string CorruptSqlState = "TB916";

    /// <summary>The failure of a damaged value: SQLCODE -916, SQLSTATE TB916.</summary>
    /// <param name="message">Why.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Corrupt(string message) => new(CorruptSqlCode, CorruptSqlState, message);

    /// <summary>Whether a failure decoding a reply is a value's, its request's alone, not the connection's.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>Whether the connection goes on.</returns>
    internal static bool IsValues(TurborelException failure) => failure.SqlCode != ConnectionFailure.SqlCode;
}
