// The failures of a SCRAM exchange [PROTOCOL Mechanisms]: credentials
// refused, SQLCODE -919 and SQLSTATE 28000, as turborel's SCRAM refuses.

namespace Turborel.Data.Authentication;

/// <summary>Makes the failures of a SCRAM exchange.</summary>
internal static class ScramFailure
{
    /// <summary>The SQLCODE of a connection exception.</summary>
    private const int ConnectionCode = -919;

    /// <summary>Invalid authorization specification.</summary>
    private const string InvalidAuthorization = "28000";

    /// <summary>The exchange refused, for <paramref name="why"/>.</summary>
    /// <param name="why">What is wrong.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Refused(string why) => Credentials("[DESIGN] SCRAM: " + why);

    /// <summary>Credentials refused, SQLSTATE 28000, saying <paramref name="message"/>.</summary>
    /// <param name="message">The message, beginning with what it cites.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Credentials(string message) => new(ConnectionCode, InvalidAuthorization, message);
}
