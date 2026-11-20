// A message's tail read tag by tag: each tag one the message takes, past
// the last, to the end of the body; any other fails the connection, and an
// empty tail is the message's first form [PROTOCOL The tail].

using System;
using System.Globalization;

namespace Turborel.Data.Protocol;

/// <summary>Reads the tags of a tail in order.</summary>
internal struct TailReader
{
    private byte _last;

    /// <summary>Reads the next tag; none at the end of the body.</summary>
    /// <param name="r">The reader, at the tail.</param>
    /// <param name="taken">The tags the message takes.</param>
    /// <returns>The tag, or null at the end.</returns>
    internal byte? Next(WireReader r, ReadOnlySpan<byte> taken)
    {
        if (r.AtEnd)
        {
            return null;
        }

        byte tag = r.ReadU8();
        if (tag <= _last || !taken.Contains(tag))
        {
            throw r.Malformed(
                Sections.Tail,
                string.Create(CultureInfo.InvariantCulture, $"its tail has tag {tag} out of place"));
        }

        _last = tag;
        return tag;
    }
}
