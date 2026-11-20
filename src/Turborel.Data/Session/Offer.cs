// What a connection asks for as it opens [CLIENT What a connection asks
// for]: every capability but compression unless the program says, the
// connection's attributes, and the shortest request sent compressed once
// compression is granted.

using System;
using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>What a connection asks for as it opens.</summary>
public sealed record Offer
{
    /// <summary>The connection attribute that names the program.</summary>
    public const string Application = "application";

    /// <summary>Gets the offer unless the program makes another: every capability but compression, no attributes.</summary>
    public static Offer Default { get; } = new()
    {
        Capabilities = [.. Protocol.Capabilities.All.Where(name => name != Protocol.Capabilities.Compression)],
    };

    /// <summary>Gets the capabilities asked for, by name.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Gets the connection's attributes, each a name and a value, as the server shows them.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; init; } = [];

    /// <summary>Gets the shortest request, header included, sent compressed once compression is granted.</summary>
    public int CompressFrom { get; init; } = Compression.DefaultThreshold;

    /// <summary>Gets an offer of no capability: the protocol's first form.</summary>
    /// <returns>The offer.</returns>
    public static Offer None() => new();

    /// <summary>Gives this offer, compression asked for too.</summary>
    /// <returns>The offer.</returns>
    public Offer Compressed() =>
        Capabilities.Contains(Protocol.Capabilities.Compression, StringComparer.Ordinal)
            ? this
            : this with { Capabilities = [.. Capabilities, Protocol.Capabilities.Compression] };

    /// <summary>Gives this offer with the attribute <paramref name="name"/> of <paramref name="value"/>.</summary>
    /// <param name="name">The attribute's name.</param>
    /// <param name="value">Its value.</param>
    /// <returns>The offer.</returns>
    public Offer WithAttribute(string name, string value) => this with { Attributes = [.. Attributes, new(name, value)] };
}
