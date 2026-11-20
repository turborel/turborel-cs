// The five bytes every message of the native protocol begins with, either
// way: its kind, then its body's length [PROTOCOL Frames].

using System;
using System.Buffers.Binary;
using System.Globalization;

namespace Turborel.Data.Protocol;

/// <summary>
/// A frame's header: the kind of the message, then the length of its body,
/// the header not counted [PROTOCOL Frames].
/// </summary>
/// <param name="Kind">The kind, a <c>u8</c>, read in the direction the frame travels.</param>
/// <param name="Length">The bytes of the body, a <c>u32</c>, little-endian.</param>
internal readonly record struct FrameHeader(byte Kind, uint Length)
{
    /// <summary>The bytes of a header: the kind's one and the length's four [PROTOCOL Frames].</summary>
    internal const int Size = sizeof(byte) + sizeof(uint);

    /// <summary>
    /// The longest body the client takes unless told otherwise, 16 MiB, so
    /// a server cannot make it hold memory without bound [PROTOCOL Frames].
    /// </summary>
    internal const uint MaxBody = 16 * 1024 * 1024;

    /// <summary>Where the kind is in a header.</summary>
    private const int KindAt = 0;

    /// <summary>Where the length is in a header, after the kind.</summary>
    private const int LengthAt = KindAt + sizeof(byte);

    /// <summary>
    /// Writes the header to the first <see cref="Size"/> bytes of
    /// <paramref name="destination"/>, the length least significant byte
    /// first.
    /// </summary>
    /// <param name="destination">Where the header goes.</param>
    /// <returns>Whether <paramref name="destination"/> had room for it; nothing is written if not.</returns>
    internal bool TryEncode(Span<byte> destination)
    {
        if (destination.Length < Size)
        {
            return false;
        }

        destination[KindAt] = Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[LengthAt..], Length);
        return true;
    }

    /// <summary>
    /// Reads the header in the first <see cref="Size"/> bytes of
    /// <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The bytes read from the connection.</param>
    /// <param name="maxBody">The longest body taken; <see cref="MaxBody"/> unless the program set another.</param>
    /// <returns>The header.</returns>
    /// <exception cref="TurborelException">
    /// A connection failure, SQLCODE -919 and SQLSTATE 08006, if the header
    /// is cut short or its body would be longer than <paramref name="maxBody"/>.
    /// </exception>
    internal static FrameHeader Decode(ReadOnlySpan<byte> source, uint maxBody)
    {
        if (source.Length < Size)
        {
            throw ConnectionFailure.Of(string.Create(
                CultureInfo.InvariantCulture,
                $"[PROTOCOL Frames] a frame's header of {source.Length} bytes is cut short of {Size}"));
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(source[LengthAt..]);
        if (length > maxBody)
        {
            throw ConnectionFailure.Of(string.Create(
                CultureInfo.InvariantCulture,
                $"[PROTOCOL Frames] a message of {length} bytes is longer than the {maxBody} taken"));
        }

        return new FrameHeader(source[KindAt], length);
    }
}
