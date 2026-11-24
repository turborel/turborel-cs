// What the server has told a connection beside each statement's replies
// [CLIENT What the server tells]: the capabilities granted, the session's
// settings as Status gave them, whether requests go compressed, the
// notices it sent, the last statement's completion and the log position
// of the last commit; and the attributes each statement carries
// [CLIENT Controlling the session].

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>What the server has told a connection.</summary>
internal sealed class Told
{
    /// <summary>Gets the capabilities granted.</summary>
    internal IReadOnlyList<string> Granted { get; private set; } = [];

    /// <summary>Gets the session's settings, as the server last told them.</summary>
    internal ConcurrentDictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    /// <summary>The most notices kept with no handler set, the oldest dropped first.</summary>
    internal const int KeptNotices = 64;

    private readonly Queue<TurborelNotice> _notices = new();

    /// <summary>Gets the debugger's events read among other replies, kept until a wait takes them, oldest first.</summary>
    internal Queue<Debugging.DebugEvent> Events { get; } = new();

    /// <summary>Gets the shortest request sent compressed, if compression was granted.</summary>
    internal int? CompressFrom { get; private set; }

    /// <summary>Gets or sets what takes each notice as it comes; with none, they are kept.</summary>
    internal Action<TurborelNotice>? Handler { get; set; }

    /// <summary>Gets or sets the attributes each Query and Execute carries.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> QueryAttributes { get; set; } = [];

    /// <summary>Gets or sets the last statement's completion.</summary>
    internal Completed? Completed { get; set; }

    /// <summary>Gets or sets the log position of the last commit the server told of.</summary>
    internal ulong? Position { get; set; }

    /// <summary>Gets the attributes a Query or an Execute carries: none unless <c>query-attributes</c> was granted.</summary>
    internal IReadOnlyList<KeyValuePair<string, string>> Attributes => Has(Capabilities.QueryAttributes) ? QueryAttributes : [];

    /// <summary>Takes a notice: to the handler, or kept.</summary>
    /// <param name="notice">The notice.</param>
    internal void Notice(TurborelNotice notice)
    {
        if (Handler is { } handler)
        {
            handler(notice);
            return;
        }

        if (_notices.Count == KeptNotices)
        {
            _ = _notices.Dequeue();
        }

        _notices.Enqueue(notice);
    }

    /// <summary>Takes the notices kept.</summary>
    /// <returns>The notices, oldest first.</returns>
    internal IReadOnlyList<TurborelNotice> TakeNotices()
    {
        TurborelNotice[] taken = [.. _notices];
        _notices.Clear();
        return taken;
    }

    /// <summary>Takes Ready's capabilities granted, compressing requests from <paramref name="compressFrom"/> bytes if compression is among them.</summary>
    /// <param name="granted">The capabilities granted.</param>
    /// <param name="compressFrom">The shortest request sent compressed.</param>
    internal void Grant(IReadOnlyList<string> granted, int compressFrom)
    {
        // The capabilities the driver knows alone, in the order of the
        // protocol's table, as turborel-client's keeps them.
        Granted = [.. Capabilities.All.Where(granted.Contains)];
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
