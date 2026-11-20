// A large object as a value carries it: whole, its octets in the message,
// or by its locator and lengths, read later by Lob requests [PROTOCOL Values].

using System;

namespace Turborel.Data.Types;

/// <summary>A CLOB or BLOB value, whole or by its locator.</summary>
public sealed record TurborelLargeObject
{
    private TurborelLargeObject(LargeObjectKind kind, ReadOnlyMemory<byte>? octets, uint locator, (ulong Octets, ulong Characters) lengths)
    {
        Kind = kind;
        Data = octets;
        Locator = locator;
        Octets = lengths.Octets;
        Characters = lengths.Characters;
    }

    /// <summary>Gets whether it is of characters or of octets.</summary>
    public LargeObjectKind Kind { get; }

    /// <summary>Gets its octets, if it came whole; none if by its locator.</summary>
    public ReadOnlyMemory<byte>? Data { get; }

    /// <summary>Gets its locator, if it came by one.</summary>
    internal uint Locator { get; }

    /// <summary>Gets its length in octets, of one by its locator; 0 of one held whole, whose <see cref="Data"/> is its octets.</summary>
    public ulong Octets { get; }

    /// <summary>Gets its length in characters, of a character one by its locator.</summary>
    public ulong Characters { get; }

    /// <summary>Gets a value indicating whether it came by its locator.</summary>
    public bool IsLocator => Data is null;

    /// <summary>Makes a large object held whole, to send as a parameter.</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="octets">Its octets; UTF-8 of a character one.</param>
    /// <returns>The large object.</returns>
    public static TurborelLargeObject Whole(LargeObjectKind kind, ReadOnlyMemory<byte> octets) =>
        new(kind, octets, 0, (0, 0));

    /// <summary>Makes a large object reached by its locator.</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="locator">Its locator.</param>
    /// <param name="lengths">Its octets and characters.</param>
    /// <returns>The large object.</returns>
    internal static TurborelLargeObject ByLocator(LargeObjectKind kind, uint locator, (ulong Octets, ulong Characters) lengths) =>
        new(kind, null, locator, lengths);
}
