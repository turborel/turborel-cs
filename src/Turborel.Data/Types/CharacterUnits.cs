// What a character type's length counts [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>Whether a length counts characters or octets.</summary>
internal enum CharacterUnits
{
    /// <summary>Characters, 0.</summary>
    Characters = 0,

    /// <summary>Octets, 1.</summary>
    Octets = 1,
}
