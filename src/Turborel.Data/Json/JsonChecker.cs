// The check of a JSON document received, node by node, before any of it is
// read (turborel's RULES.md D89, json/binary/node.rs): each node within the
// document, every string UTF-8, every offset where its element or member
// is, every directory sorted, and nesting bounded. A failure says why,
// never throws, so the codec gives it as a connection failure.

using System;
using System.Collections.Generic;
using Turborel.Data.Types;

namespace Turborel.Data.Json;

/// <summary>Checks the nodes of a document's body, recording why one is damaged.</summary>
internal ref struct JsonChecker
{
    /// <summary>The value a check gives when the node is damaged.</summary>
    internal const long Damaged = -1;

    /// <summary>The datetime moments a node may name: date, time and timestamp.</summary>
    private const byte LastMoment = (byte)DatetimeMoment.Timestamp;

    private readonly ReadOnlySpan<byte> _body;

    /// <summary>Makes a checker of a document's body, the octets after its version.</summary>
    /// <param name="body">The body.</param>
    internal JsonChecker(ReadOnlySpan<byte> body)
    {
        _body = body;
        Why = null;
    }

    /// <summary>Gets why the document is damaged, once a check gave <see cref="Damaged"/>.</summary>
    internal string? Why { get; private set; }

    /// <summary>The octets of the node at <paramref name="at"/> of a body already checked.</summary>
    /// <param name="body">The body.</param>
    /// <param name="at">Where the node's tag is.</param>
    /// <returns>Its octets, its tag's among them.</returns>
    internal static long LengthAt(ReadOnlySpan<byte> body, int at)
    {
        int b = at + 1;
        return body[at] switch
        {
            JsonBinary.Integer or JsonBinary.Approximate => 1 + JsonBinary.Long,
            JsonBinary.Decimal => 1 + JsonBinary.Long + 1,
            JsonBinary.Wide or JsonBinary.DecFloat => 1 + JsonBinary.WideWord + JsonBinary.Word,
            JsonBinary.String => 1 + JsonBinary.Word + (long)JsonBinary.WordAt(body, b),
            JsonBinary.Datetime => 1 + JsonBinary.DatetimeBody,
            JsonBinary.Array or JsonBinary.Object => 1 + JsonBinary.ContainerHead
                + ((long)JsonBinary.WordAt(body, b) * JsonBinary.Word) + JsonBinary.WordAt(body, b + JsonBinary.Word),
            _ => 1,
        };
    }

    /// <summary>Checks the node at <paramref name="at"/>, within <paramref name="depth"/> containers.</summary>
    /// <param name="at">Where its tag is.</param>
    /// <param name="depth">How many arrays and objects hold it.</param>
    /// <returns>Its octets; <see cref="Damaged"/> if it is damaged.</returns>
    internal long Node(long at, int depth)
    {
        if (depth > JsonBinary.MaxNesting)
        {
            return Fail("arrays and objects nest too deep");
        }

        if (!Has(at, 1) || _body[(int)at] > JsonBinary.Object)
        {
            return Fail(Has(at, 1) ? "a node's tag is no kind's" : "a node runs past its document");
        }

        int start = (int)at;
        long head = HeadLength(start);
        if (head == Damaged || !Has(at, LengthAt(_body, start)))
        {
            return Fail("a node runs past its document");
        }

        return _body[start] switch
        {
            JsonBinary.String => JsonBinary.IsUtf8At(_body, start + 1) ? LengthAt(_body, start) : Fail("a string is not UTF-8"),
            JsonBinary.Datetime => CheckDatetime(start),
            JsonBinary.DecFloat => JsonBinary.DecFloatAt(_body, start + 1) is null ? Fail("a number is out of range") : LengthAt(_body, start),
            JsonBinary.Array => CheckElements(start, depth),
            JsonBinary.Object => CheckMembers(start, depth),
            _ => LengthAt(_body, start),
        };
    }

    // The octets a node's head needs before its length can be read: a
    // string's length, or a container's count and size.
    private readonly long HeadLength(int at)
    {
        long needed = _body[at] switch
        {
            JsonBinary.String => 1 + JsonBinary.Word,
            JsonBinary.Array or JsonBinary.Object => 1 + JsonBinary.ContainerHead,
            _ => 1,
        };
        return Has(at, needed) ? needed : Damaged;
    }

    private long CheckDatetime(int at)
    {
        int b = at + 1;
        byte moment = _body[b];
        byte zoned = _body[b + 1 + JsonBinary.Long + sizeof(short)];
        if (moment > LastMoment || zoned > 1)
        {
            return Fail(moment > LastMoment ? "a datetime's moment is no moment" : "a datetime's zoned flag is neither 0 nor 1");
        }

        return LengthAt(_body, at);
    }

    private long CheckElements(int at, int depth)
    {
        int b = at + 1;
        uint count = JsonBinary.WordAt(_body, b);
        long first = b + JsonBinary.ContainerHead + ((long)count * JsonBinary.Word);
        long expected = 0;
        for (uint i = 0; i < count; i++)
        {
            uint offset = JsonBinary.WordAt(_body, b + JsonBinary.ContainerHead + (int)(i * JsonBinary.Word));
            if (offset != expected)
            {
                return Fail("an element's offset is not where it is");
            }

            long element = Node(first + offset, depth + 1);
            if (element == Damaged)
            {
                return Damaged;
            }

            expected += element;
        }

        return expected == JsonBinary.WordAt(_body, b + JsonBinary.Word) ? LengthAt(_body, at) : Fail("an array's size is not its elements'");
    }

    private long CheckMembers(int at, int depth)
    {
        int b = at + 1;
        uint count = JsonBinary.WordAt(_body, b);
        long first = b + JsonBinary.ContainerHead + ((long)count * JsonBinary.Word);
        var starts = new HashSet<long>();
        long next = 0;
        for (uint i = 0; i < count; i++)
        {
            long member = Member(first + next, depth);
            if (member == Damaged)
            {
                return Damaged;
            }

            _ = starts.Add(next);
            next += member;
        }

        if (next != JsonBinary.WordAt(_body, b + JsonBinary.Word))
        {
            return Fail("an object's size is not its members'");
        }

        return Sorted(b, first, starts) ? LengthAt(_body, at) : Fail("an object's key directory is not sorted");
    }

    // A member at `at`: its key, a string, then its value node.
    private long Member(long at, int depth)
    {
        if (!Has(at, JsonBinary.Word) || !Has(at + JsonBinary.Word, JsonBinary.WordAt(_body, (int)at)))
        {
            return Fail("a member runs past its document");
        }

        if (!JsonBinary.IsUtf8At(_body, (int)at))
        {
            return Fail("a key is not UTF-8");
        }

        long key = JsonBinary.Word + (long)JsonBinary.WordAt(_body, (int)at);
        long value = Node(at + key, depth + 1);
        return value == Damaged ? Damaged : key + value;
    }

    // That the directory names each member once, sorted by key octets and,
    // of one key, by the order written.
    private readonly bool Sorted(int b, long first, HashSet<long> starts)
    {
        uint count = JsonBinary.WordAt(_body, b);
        ReadOnlySpan<byte> previousKey = default;
        long previousOffset = Damaged;
        for (uint i = 0; i < count; i++)
        {
            uint offset = JsonBinary.WordAt(_body, b + JsonBinary.ContainerHead + (int)(i * JsonBinary.Word));
            if (!starts.Contains(offset))
            {
                return false;
            }

            int keyAt = (int)(first + offset);
            ReadOnlySpan<byte> key = _body.Slice(keyAt + JsonBinary.Word, (int)JsonBinary.WordAt(_body, keyAt));
            int order = key.SequenceCompareTo(previousKey);
            if (previousOffset != Damaged && (order < 0 || (order == 0 && offset <= previousOffset)))
            {
                return false;
            }

            previousKey = key;
            previousOffset = offset;
        }

        return true;
    }

    private readonly bool Has(long at, long count) => at >= 0 && count >= 0 && at + count <= _body.Length;

    private long Fail(string why)
    {
        Why ??= why;
        return Damaged;
    }
}
