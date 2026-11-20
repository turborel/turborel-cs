// A value of the JSON type: its stored form, as turborel sends it, read
// to an SQL/JSON item or written as JSON text, and made from JSON text to
// send as a parameter (turborel's RULES.md D89) [PROTOCOL Values].

using System;
using Turborel.Data.Json;

namespace Turborel.Data.Types;

/// <summary>A JSON value, held as its stored form's octets, checked whole.</summary>
public sealed class TurborelJson
{
    private TurborelJson(byte[] octets)
    {
        Octets = octets;
    }

    /// <summary>Gets the document's octets, as the protocol carries them.</summary>
    internal ReadOnlyMemory<byte> Octets { get; }

    /// <summary>Makes a value of octets already checked by <see cref="JsonBinary.Validate"/>.</summary>
    /// <param name="octets">The checked document.</param>
    /// <returns>The value.</returns>
    internal static TurborelJson Trusted(byte[] octets) => new(octets);

    /// <summary>Makes the value of an SQL/JSON item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TurborelException">SQLCODE -900 if it nests too deep or is too long.</exception>
    internal static TurborelJson FromItem(JsonItem item) => new(JsonBinaryWriter.Encode(item));

    /// <summary>Makes the value JSON text is, as turborel parses it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22032 if it is no JSON text; 22003 if a number is out of range.</exception>
    public static TurborelJson Parse(string text) => FromItem(JsonTextParser.Parse(text));

    /// <summary>Reads the SQL/JSON item it holds.</summary>
    /// <returns>The item.</returns>
    internal JsonItem ToItem() => JsonBinary.Decode(Octets.Span);

    /// <summary>Writes it as JSON text, as turborel serializes it.</summary>
    /// <returns>The text.</returns>
    /// <exception cref="TurborelException">SQLSTATE 22032 if it holds a datetime, or an approximate number not finite.</exception>
    public string ToJsonText() => JsonText.Serialize(ToItem());

    /// <summary>Writes it as JSON text, or, if it has none, says why.</summary>
    /// <returns>The text, or the reason it has none.</returns>
    public override string ToString()
    {
        try
        {
            return ToJsonText();
        }
        catch (TurborelException e)
        {
            return e.Message;
        }
    }
}
