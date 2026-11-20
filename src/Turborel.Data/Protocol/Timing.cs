// The server's time for a statement, tail tag 18 of a Complete, in
// microseconds [PROTOCOL Complete 'K'].

namespace Turborel.Data.Protocol;

/// <summary>The server's time for a statement.</summary>
/// <param name="Total">From when a worker began the request until its completion was ready.</param>
/// <param name="Queued">The time the request waited for a worker before that, not part of total.</param>
/// <param name="Locks">The part of total spent waiting for locks.</param>
internal readonly record struct Timing(ulong Total, ulong Queued, ulong Locks);
