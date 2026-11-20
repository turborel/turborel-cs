// The two kinds of large object, as their byte says [PROTOCOL Values].

namespace Turborel.Data.Types;

/// <summary>Whether a large object is of characters or of octets.</summary>
public enum LargeObjectKind
{
    /// <summary>A CHARACTER LARGE OBJECT, kind 0: its octets are UTF-8.</summary>
    Character = 0,

    /// <summary>A BINARY LARGE OBJECT, kind 1.</summary>
    Binary = 1,
}
