// Writes the fields of messages the client sends, and frames them
// [PROTOCOL Frames] [PROTOCOL Values and fields]: integers little-endian,
// strings and bytes after their length, lists after their count.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Turborel.Data.Protocol;

/// <summary>
/// A growing buffer of fields, and of the frames that hold them.
/// </summary>
internal sealed class WireWriter
{
    /// <summary>The bytes of an <c>i128</c> [PROTOCOL Values and fields].</summary>
    internal const int I128Bytes = 16;

    /// <summary>The bytes of a key, with no length before them [PROTOCOL Values and fields].</summary>
    internal const int KeyBytes = 16;

    /// <summary>The buffer's first size, enough for most requests.</summary>
    private const int FirstCapacity = 256;

    private byte[] _buffer = new byte[FirstCapacity];

    /// <summary>Gets the bytes written.</summary>
    internal ReadOnlySpan<byte> Written => _buffer.AsSpan(0, Length);

    /// <summary>Gets the count of bytes written.</summary>
    internal int Length { get; private set; }

    /// <summary>Gives the bytes written as an array of their own.</summary>
    /// <returns>A copy of the bytes written.</returns>
    internal byte[] ToArray() => Written.ToArray();

    /// <summary>Forgets every byte written, keeping the buffer.</summary>
    internal void Clear() => Length = 0;

    /// <summary>
    /// Begins a frame of kind <paramref name="kind"/>, its length to be
    /// written by <see cref="EndFrame"/> once its body is.
    /// </summary>
    /// <param name="kind">The message's kind.</param>
    /// <returns>Where the frame begins, for <see cref="EndFrame"/>.</returns>
    internal int BeginFrame(byte kind)
    {
        int start = Length;
        WriteU8(kind);
        WriteU32(0);
        return start;
    }

    /// <summary>Writes the length of the frame begun at <paramref name="start"/>.</summary>
    /// <param name="start">What <see cref="BeginFrame"/> gave.</param>
    internal void EndFrame(int start)
    {
        int body = Length - start - FrameHeader.Size;
        var header = new FrameHeader(_buffer[start], (uint)body);
        _ = header.TryEncode(_buffer.AsSpan(start, FrameHeader.Size));
    }

    /// <summary>Writes a frame of kind <paramref name="kind"/> with no body.</summary>
    /// <param name="kind">The message's kind.</param>
    internal void WriteEmptyFrame(byte kind) => EndFrame(BeginFrame(kind));

    /// <summary>Writes a <c>u32</c> over four bytes written before, at <paramref name="at"/>.</summary>
    /// <param name="at">Where the four bytes begin.</param>
    /// <param name="value">The number.</param>
    internal void PatchU32(int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(at, sizeof(uint)), value);

    /// <summary>Writes an <c>i16</c>, little-endian.</summary>
    /// <param name="value">The number.</param>
    internal void WriteI16(short value) => BinaryPrimitives.WriteInt16LittleEndian(Reserve(sizeof(short)), value);

    /// <summary>Writes a <c>u8</c>.</summary>
    /// <param name="value">The byte.</param>
    internal void WriteU8(byte value) => Reserve(sizeof(byte))[0] = value;

    /// <summary>Writes a <c>u32</c>.</summary>
    /// <param name="value">The number.</param>
    internal void WriteU32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);

    /// <summary>Writes a <c>u64</c>.</summary>
    /// <param name="value">The number.</param>
    internal void WriteU64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Reserve(sizeof(ulong)), value);

    /// <summary>Writes an <c>i32 as u32</c>.</summary>
    /// <param name="value">The number.</param>
    internal void WriteI32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Reserve(sizeof(int)), value);

    /// <summary>Writes an <c>i64 as u64</c>.</summary>
    /// <param name="value">The number.</param>
    internal void WriteI64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Reserve(sizeof(long)), value);

    /// <summary>Writes an <c>i128</c>.</summary>
    /// <param name="value">The number.</param>
    internal void WriteI128(Int128 value) => BinaryPrimitives.WriteInt128LittleEndian(Reserve(I128Bytes), value);

    /// <summary>Writes a flag, 0 or 1.</summary>
    /// <param name="value">The flag.</param>
    internal void WriteFlag(bool value) => WriteU8(value ? (byte)1 : (byte)0);

    /// <summary>Writes bytes as they are, with no length before them.</summary>
    /// <param name="value">The bytes.</param>
    internal void WriteRaw(ReadOnlySpan<byte> value) => value.CopyTo(Reserve(value.Length));

    /// <summary>Writes bytes: their length, then the bytes.</summary>
    /// <param name="value">The bytes.</param>
    internal void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteCount(value.Length);
        WriteRaw(value);
    }

    /// <summary>Writes a string: its length in bytes, then its UTF-8.</summary>
    /// <param name="value">The string.</param>
    internal void WriteString(string value)
    {
        int length = Encoding.UTF8.GetByteCount(value);
        WriteCount(length);
        _ = Encoding.UTF8.GetBytes(value, Reserve(length));
    }

    /// <summary>Writes a list's count.</summary>
    /// <param name="count">The count.</param>
    internal void WriteCount(int count) => WriteU32((uint)count);

    /// <summary>Writes a list of strings.</summary>
    /// <param name="values">The strings.</param>
    internal void WriteStrings(IReadOnlyCollection<string> values)
    {
        WriteCount(values.Count);
        foreach (string value in values)
        {
            WriteString(value);
        }
    }

    private Span<byte> Reserve(int count)
    {
        int needed = Length + count;
        if (needed > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(needed, _buffer.Length * 2));
        }

        Span<byte> reserved = _buffer.AsSpan(Length, count);
        Length = needed;
        return reserved;
    }
}
