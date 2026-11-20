// Reads the fields of a message received, front to back: integers
// little-endian, strings and bytes after their length, lists after their
// count [PROTOCOL Values and fields]. A field cut short or malformed is a
// connection failure, never an exception of the BCL's (RULES.md R-ERR-1).

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Turborel.Data.Protocol;

/// <summary>
/// A reader of one message's body, or of a part of one, that fails the
/// connection on any field it cannot read.
/// </summary>
internal sealed class WireReader
{
    /// <summary>
    /// How deep arrays, rows, multisets and structured values nest, of a
    /// value or of a type, as turborel reads them (its MAX_TYPE_NESTING).
    /// </summary>
    internal const int MaxNesting = 16;

    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly string _what;
    private int _at;
    private int _depth;

    /// <summary>Makes a reader of <paramref name="bytes"/>, which hold a <paramref name="what"/>.</summary>
    /// <param name="bytes">The bytes received.</param>
    /// <param name="what">What they hold, for a failure's message, as "reply".</param>
    internal WireReader(ReadOnlyMemory<byte> bytes, string what)
    {
        _bytes = bytes;
        _what = what;
    }

    /// <summary>Gets a value indicating whether every byte was read.</summary>
    internal bool AtEnd => _at >= _bytes.Length;

    /// <summary>Gets the bytes left to read.</summary>
    internal int Remaining => _bytes.Length - _at;

    /// <summary>Makes the failure of a field that is malformed, at the current byte.</summary>
    /// <param name="section">The section of the protocol the field breaks.</param>
    /// <param name="why">What is wrong with it.</param>
    /// <returns>The connection failure, for the caller to throw.</returns>
    internal TurborelException Malformed(string section, string why)
    {
        return ConnectionFailure.Of(string.Create(
            CultureInfo.InvariantCulture,
            $"{section} a {_what} received is malformed at byte {_at}: {why}"));
    }

    /// <summary>
    /// Makes the failure of a value whose body the client cannot take, in a
    /// frame read whole: SQLCODE -916, SQLSTATE TB916, its request's alone,
    /// the connection kept [PROTOCOL Values and fields].
    /// </summary>
    /// <param name="section">The section of the protocol the value breaks.</param>
    /// <param name="why">What is wrong with it.</param>
    /// <returns>The value's failure, for the caller to throw.</returns>
    internal TurborelException Corrupt(string section, string why) => ValueFailure.Corrupt(string.Create(
        CultureInfo.InvariantCulture,
        $"{section} a value of a {_what} received cannot be taken, at byte {_at}: {why}"));

    /// <summary>Reads <paramref name="count"/> bytes.</summary>
    /// <param name="count">How many.</param>
    /// <returns>The bytes, a slice of those received.</returns>
    internal ReadOnlyMemory<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw Malformed(Sections.Frames, "it ends too soon");
        }

        ReadOnlyMemory<byte> taken = _bytes.Slice(_at, count);
        _at += count;
        return taken;
    }

    /// <summary>Reads every byte left.</summary>
    /// <returns>The bytes left.</returns>
    internal ReadOnlyMemory<byte> Rest() => Take(Remaining);

    /// <summary>Reads a <c>u8</c>.</summary>
    /// <returns>The byte.</returns>
    internal byte ReadU8() => Take(sizeof(byte)).Span[0];

    /// <summary>Reads a <c>u32</c>, little-endian.</summary>
    /// <returns>The number.</returns>
    internal uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(sizeof(uint)).Span);

    /// <summary>Reads a <c>u64</c>, little-endian.</summary>
    /// <returns>The number.</returns>
    internal ulong ReadU64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(sizeof(ulong)).Span);

    /// <summary>Reads an <c>i32 as u32</c>: the signed number's bits.</summary>
    /// <returns>The number.</returns>
    internal int ReadI32() => BinaryPrimitives.ReadInt32LittleEndian(Take(sizeof(int)).Span);

    /// <summary>Reads an <c>i64 as u64</c>: the signed number's bits.</summary>
    /// <returns>The number.</returns>
    internal long ReadI64() => BinaryPrimitives.ReadInt64LittleEndian(Take(sizeof(long)).Span);

    /// <summary>Reads an <c>i128</c>, two's complement, least significant byte first.</summary>
    /// <returns>The number.</returns>
    internal Int128 ReadI128() => BinaryPrimitives.ReadInt128LittleEndian(Take(WireWriter.I128Bytes).Span);

    /// <summary>Reads a flag: 0 or 1, any other byte failing the connection.</summary>
    /// <returns>The flag.</returns>
    internal bool ReadFlag()
    {
        byte flag = ReadU8();
        return flag switch
        {
            0 => false,
            1 => true,
            _ => throw Malformed(Sections.Fields, "a flag is neither 0 nor 1"),
        };
    }

    /// <summary>Reads bytes: their length, a <c>u32</c>, then the bytes.</summary>
    /// <returns>The bytes, a slice of those received.</returns>
    internal ReadOnlyMemory<byte> ReadBytes() => Take(Length());

    /// <summary>Reads a string: its length in bytes, then its UTF-8.</summary>
    /// <returns>The string.</returns>
    internal string ReadString()
    {
        ReadOnlySpan<byte> bytes = ReadBytes().Span;
        return Utf8.IsValid(bytes)
            ? Encoding.UTF8.GetString(bytes)
            : throw Malformed(Sections.Fields, "a string is not UTF-8");
    }

    /// <summary>Reads a key: 16 bytes, with no length before them.</summary>
    /// <returns>The key's bytes.</returns>
    internal byte[] ReadKey() => Take(WireWriter.KeyBytes).ToArray();

    /// <summary>
    /// Reads a list's count. Nothing is reserved for it: a count larger
    /// than what follows fails as the items run out [PROTOCOL Values and fields].
    /// </summary>
    /// <returns>The count.</returns>
    internal uint ReadCount() => ReadU32();

    /// <summary>Reads a list of strings.</summary>
    /// <returns>The strings.</returns>
    internal string[] ReadStrings()
    {
        uint count = ReadCount();
        var strings = new System.Collections.Generic.List<string>();
        for (uint i = 0; i < count; i++)
        {
            strings.Add(ReadString());
        }

        return [.. strings];
    }

    /// <summary>
    /// Reads a part nested within what it reads, an element, a field or an
    /// attribute, failing past <see cref="MaxNesting"/>.
    /// </summary>
    /// <typeparam name="T">What the part is.</typeparam>
    /// <param name="read">Reads the part.</param>
    /// <returns>The part.</returns>
    internal T Nested<T>(Func<WireReader, T> read)
    {
        if (_depth >= MaxNesting)
        {
            throw Malformed(Sections.Values, "its arrays and rows are nested too deep");
        }

        _depth++;
        try
        {
            return read(this);
        }
        finally
        {
            _depth--;
        }
    }

    /// <summary>Checks every byte was read: bytes left over fail the connection.</summary>
    internal void Finish()
    {
        if (!AtEnd)
        {
            throw Malformed(Sections.Frames, "bytes follow its end");
        }
    }

    private int Length()
    {
        uint length = ReadU32();
        if (length > (uint)Remaining)
        {
            throw Malformed(Sections.Frames, "it ends too soon");
        }

        return (int)length;
    }
}
