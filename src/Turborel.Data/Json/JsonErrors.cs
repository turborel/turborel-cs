// The failures of JSON a program gives or asks for, each with the SQLCODE
// and SQLSTATE turborel gives the same failure (its json.rs, json/text.rs
// and json/serialize.rs).

using System;
using System.Globalization;

namespace Turborel.Data.Json;

/// <summary>Makes the failures of JSON values the driver finds.</summary>
internal static class JsonErrors
{
    /// <summary>The SQLCODE of a data exception.</summary>
    private const int DataSqlCode = -301;

    /// <summary>The SQLCODE of what is beyond turborel's limits.</summary>
    private const int UnsupportedSqlCode = -900;

    /// <summary>The SQLSTATE of text that is not JSON text, or an item that has none.</summary>
    private const string InvalidJsonText = "22032";

    /// <summary>The SQLSTATE of a number no number of turborel's is.</summary>
    private const string OutOfRange = "22003";

    /// <summary>The SQLSTATE of what is beyond turborel's limits: feature not supported.</summary>
    private const string NotSupported = "0A000";

    /// <summary>The failure of text that is not JSON text.</summary>
    /// <param name="why">What is wrong with it.</param>
    /// <param name="at">The byte it was found at, from 0.</param>
    /// <param name="cause">The exception of the reader that found it, if one did.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException NotJsonText(string why, long at, Exception? cause = null)
    {
        return new TurborelException(
            DataSqlCode,
            InvalidJsonText,
            string.Create(CultureInfo.InvariantCulture, $"[DESIGN] the string is not a JSON text: {why} (at byte {at})"),
            cause);
    }

    /// <summary>The failure of an item that has no JSON text, such as a datetime.</summary>
    /// <param name="what">What it holds.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException NoText(string what) =>
        new(DataSqlCode, InvalidJsonText, "[DESIGN] no JSON text parses to " + what);

    /// <summary>The failure of a number out of the range of turborel's numbers.</summary>
    /// <param name="why">Why.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException NumberOutOfRange(string why) =>
        new(DataSqlCode, OutOfRange, "[DESIGN] " + why);

    /// <summary>The failure of an item beyond turborel's limits, as nested too deep.</summary>
    /// <param name="why">Why.</param>
    /// <returns>The failure, for the caller to throw.</returns>
    internal static TurborelException Beyond(string why) =>
        new(UnsupportedSqlCode, NotSupported, "[DESIGN] the SQL/JSON item is beyond turborel's limits: " + why);
}
