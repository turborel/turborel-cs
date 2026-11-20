// What the server has told a connection beside each statement's replies
// [CLIENT What the server tells]: the capabilities granted, the session's
// settings as Status gave them, and whether requests go compressed.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>What the server has told a connection.</summary>
internal sealed class Told
{
    /// <summary>Gets the capabilities granted.</summary>
    internal IReadOnlyList<string> Granted { get; private set; } = [];

    /// <summary>Gets the session's settings, as the server last told them.</summary>
    internal ConcurrentDictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the shortest request sent compressed, if compression was granted.</summary>
    internal int? CompressFrom { get; private set; }

    /// <summary>Takes Ready's capabilities granted, compressing requests from <paramref name="compressFrom"/> bytes if compression is among them.</summary>
    /// <param name="granted">The capabilities granted.</param>
    /// <param name="compressFrom">The shortest request sent compressed.</param>
    internal void Grant(IReadOnlyList<string> granted, int compressFrom)
    {
        Granted = granted;
        CompressFrom = Has(Capabilities.Compression) ? compressFrom : null;
    }

    /// <summary>Whether <paramref name="capability"/> was granted.</summary>
    /// <param name="capability">The capability's name.</param>
    /// <returns>Whether it was.</returns>
    internal bool Has(string capability)
    {
        foreach (string granted in Granted)
        {
            if (granted == capability)
            {
                return true;
            }
        }

        return false;
    }
}
