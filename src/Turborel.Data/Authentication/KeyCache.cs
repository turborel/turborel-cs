// The SCRAM key cache [CLIENT The SCRAM key cache], as turborel-client's
// scram/cache.rs keeps it: the keys a password derived under a salt and
// iterations, found by the salt, the iterations and a tag of the password
// under a random secret of the cache's own, so it holds neither the
// password nor what it could be guessed from offline. At most 64 entries,
// the oldest dropped first; an entry dropped when a login that used it
// fails. It lives in the process's memory alone, shared by its users.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Turborel.Data.Authentication;

/// <summary>Keeps the keys SCRAM logins derived, so a later login skips the derivation.</summary>
public sealed class KeyCache
{
    /// <summary>The most entries held, the oldest dropped first [CLIENT The SCRAM key cache].</summary>
    public const int Entries = 64;

    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(ScramKeys.KeyBytes);
    private readonly LinkedList<(byte[] Tag, byte[] Salt, uint Iterations, ScramKeys Keys)> _held = new();
    private readonly Lock _lock = new();
    private long _hits;
    private long _misses;

    /// <summary>Gets the entries held.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>Gets the lookups that found an entry, and those that did not.</summary>
    public (long Hits, long Misses) Lookups
    {
        get
        {
            lock (_lock)
            {
                return (_hits, _misses);
            }
        }
    }

    /// <summary>Drops every entry.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _held.Clear();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"KeyCache({Count} entries)";

    /// <summary>The keys <paramref name="password"/> derives under the salt and iterations: the kept ones, or derived and kept.</summary>
    /// <param name="password">The password.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="iterations">The iterations.</param>
    /// <returns>The keys.</returns>
    internal ScramKeys Derived(string password, byte[] salt, uint iterations)
    {
        byte[] tag = Tag(password);
        lock (_lock)
        {
            if (Find(tag, salt, iterations) is { } found)
            {
                _hits++;
                return found.Value.Keys;
            }

            _misses++;
        }

        var keys = ScramKeys.Of(password, salt, iterations);
        lock (_lock)
        {
            if (Find(tag, salt, iterations) is null)
            {
                if (_held.Count >= Entries)
                {
                    _held.RemoveFirst();
                }

                _ = _held.AddLast((tag, salt, iterations, keys));
            }
        }

        return keys;
    }

    /// <summary>Drops the entry of the password, salt and iterations, as a login that used it failed.</summary>
    /// <param name="password">The password.</param>
    /// <param name="salt">The salt.</param>
    /// <param name="iterations">The iterations.</param>
    internal void Forget(string password, byte[] salt, uint iterations)
    {
        byte[] tag = Tag(password);
        lock (_lock)
        {
            if (Find(tag, salt, iterations) is { } found)
            {
                _held.Remove(found);
            }
        }
    }

    private byte[] Tag(string password) => HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(password));

    private LinkedListNode<(byte[] Tag, byte[] Salt, uint Iterations, ScramKeys Keys)>? Find(byte[] tag, byte[] salt, uint iterations)
    {
        for (LinkedListNode<(byte[] Tag, byte[] Salt, uint Iterations, ScramKeys Keys)>? node = _held.First; node is not null; node = node.Next)
        {
            (byte[] t, byte[] s, uint i, _) = node.Value;
            if (i == iterations && t.AsSpan().SequenceEqual(tag) && s.AsSpan().SequenceEqual(salt))
            {
                return node;
            }
        }

        return null;
    }
}
