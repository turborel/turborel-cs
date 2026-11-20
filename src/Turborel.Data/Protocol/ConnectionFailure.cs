// The failure that ends a connection: a message malformed, too long or
// out of turn [PROTOCOL Errors and notices].

namespace Turborel.Data.Protocol;

/// <summary>
/// Makes the failure the protocol gives a broken connection: SQLCODE -919,
/// SQLSTATE 08006 [PROTOCOL Frames].
/// </summary>
internal static class ConnectionFailure
{
    /// <summary>The SQLCODE of a connection failure [PROTOCOL Frames].</summary>
    internal const int SqlCode = -919;

    /// <summary>The SQLSTATE of a connection failure [PROTOCOL Frames].</summary>
    internal const string SqlState = "08006";

    /// <summary>
    /// Makes a connection failure saying <paramref name="message"/>.
    /// </summary>
    /// <param name="message">What broke, starting with the section of the protocol it breaks.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Of(string message) => new(SqlCode, SqlState, message);
}
