// The bits of a DEFLATE stream, read from the least significant end of
// each byte (RFC 1951 §3.1.1), and where in the stream they stand, so the
// caller knows exactly where its last block ends.

using System;

namespace Turborel.Data.Protocol.Zlib;

/// <summary>A reader of a stream's bits.</summary>
internal ref struct InflateBits
{
    /// <summary>The bits of a byte.</summary>
    private const int ByteBits = 8;

    private readonly ReadOnlySpan<byte> _data;
    private uint _buffer;
    private int _count;

    /// <summary>Makes a reader of <paramref name="data"/>.</summary>
    /// <param name="data">The stream's bytes.</param>
    internal InflateBits(ReadOnlySpan<byte> data)
    {
        _data = data;
        At = 0;
        _buffer = 0;
        _count = 0;
    }

    /// <summary>Gets the bytes read whole, the bits of a partial byte dropped as a block ends.</summary>
    internal int At { get; private set; }

    /// <summary>Reads the next <paramref name="n"/> bits, at most 16, the first read the least significant.</summary>
    /// <param name="n">How many.</param>
    /// <returns>The bits.</returns>
    internal int Bits(int n)
    {
        while (_count < n)
        {
            if (At >= _data.Length)
            {
                throw Inflater.Malformed("the stream ends inside a block");
            }

            _buffer |= (uint)_data[At] << _count;
            At++;
            _count += ByteBits;
        }

        int value = (int)(_buffer & ((1u << n) - 1));
        _buffer = n >= sizeof(uint) * ByteBits ? 0 : _buffer >> n;
        _count -= n;
        return value;
    }

    /// <summary>Drops the rest of the current byte, as a stored block begins.</summary>
    internal void Align()
    {
        _buffer = 0;
        _count = 0;
    }

    /// <summary>Reads <paramref name="n"/> whole bytes, after <see cref="Align"/>.</summary>
    /// <param name="n">How many.</param>
    /// <returns>The bytes.</returns>
    internal ReadOnlySpan<byte> Bytes(int n)
    {
        if (n > _data.Length - At)
        {
            throw Inflater.Malformed("the stream ends inside a stored block");
        }

        ReadOnlySpan<byte> taken = _data.Slice(At, n);
        At += n;
        return taken;
    }
}
