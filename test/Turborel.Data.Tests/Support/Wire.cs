// What the codec's tests share (RULES.md R-TEST-7): bytes from hexadecimal
// as the protocol's page prints them, frames split into kind and body, and
// the check that a failure is the connection's, SQLCODE -919 and SQLSTATE
// 08006 [PROTOCOL Frames].

using System;
using System.Text;
using Turborel.Data.Protocol;
using Xunit;

namespace Turborel.Data.Tests.Support;

/// <summary>Helpers of the codec's tests.</summary>
internal static class Wire
{
    /// <summary>The SQLCODE of a connection failure.</summary>
    internal const int FailedCode = -919;

    /// <summary>The SQLSTATE of a connection failure.</summary>
    internal const string FailedState = "08006";

    /// <summary>The bytes hexadecimal digits write, spaces ignored, as the page prints them.</summary>
    /// <param name="hex">The digits, as <c>48 24 00</c>.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));

    /// <summary>The bytes of a string's field: its length, then its UTF-8.</summary>
    /// <param name="text">The string.</param>
    /// <returns>The field's bytes.</returns>
    internal static byte[] Str(string text)
    {
        var w = new WireWriter();
        w.WriteString(text);
        return w.ToArray();
    }

    /// <summary>The frame a request encodes to.</summary>
    /// <param name="request">The request.</param>
    /// <returns>Its frame's bytes.</returns>
    internal static byte[] Encode(Request request)
    {
        var w = new WireWriter();
        request.Encode(w);
        return w.ToArray();
    }

    /// <summary>The bytes <paramref name="write"/> writes.</summary>
    /// <param name="write">What writes them.</param>
    /// <returns>The bytes.</returns>
    internal static byte[] Written(Action<WireWriter> write)
    {
        var w = new WireWriter();
        write(w);
        return w.ToArray();
    }

    /// <summary>The kind and body of one whole frame, its length checked.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <returns>Its kind and body.</returns>
    internal static (byte Kind, byte[] Body) Unframe(byte[] frame)
    {
        var header = FrameHeader.Decode(frame, FrameHeader.MaxBody);
        Assert.Equal(frame.Length - FrameHeader.Size, (int)header.Length);
        return (header.Kind, frame[FrameHeader.Size..]);
    }

    /// <summary>A frame of <paramref name="kind"/> around <paramref name="body"/>.</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="body">Its body.</param>
    /// <returns>The frame's bytes.</returns>
    internal static byte[] Frame(char kind, byte[] body)
    {
        var w = new WireWriter();
        int start = w.BeginFrame((byte)kind);
        w.WriteRaw(body);
        w.EndFrame(start);
        return w.ToArray();
    }

    /// <summary>Decodes the reply a frame holds.</summary>
    /// <param name="frame">The frame's bytes.</param>
    /// <returns>The reply.</returns>
    internal static Reply DecodeReply(byte[] frame)
    {
        (byte kind, byte[] body) = Unframe(frame);
        return Reply.Decode(kind, body);
    }

    /// <summary>Decodes a reply of <paramref name="kind"/> from its body.</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="body">Its body.</param>
    /// <returns>The reply.</returns>
    internal static Reply Decode(char kind, byte[] body) => Reply.Decode((byte)kind, body);

    /// <summary>Checks <paramref name="act"/> fails the connection, its message beginning with <paramref name="section"/>.</summary>
    /// <param name="act">What fails.</param>
    /// <param name="section">The section its message cites, if checked.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Fails(Action act, string? section = null)
    {
        TurborelException failure = Assert.Throws<TurborelException>(act);
        Assert.Equal((FailedCode, FailedState), (failure.SqlCode, failure.SqlState));
        if (section is not null)
        {
            Assert.StartsWith(section, failure.Message, StringComparison.Ordinal);
        }

        return failure;
    }

    /// <summary>Runs <paramref name="act"/>, which must fail as a value the client cannot take: SQLCODE -916, SQLSTATE TB916, not of class 08.</summary>
    /// <param name="act">What fails.</param>
    /// <param name="section">The section the failure's message begins with, if checked.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException FailsValue(Action act, string? section = null)
    {
        TurborelException failure = Assert.Throws<TurborelException>(act);
        Assert.Equal((-916, "TB916"), (failure.SqlCode, failure.SqlState));
        if (section is not null)
        {
            Assert.StartsWith(section, failure.Message, StringComparison.Ordinal);
        }

        return failure;
    }

    /// <summary>Joins byte arrays.</summary>
    /// <param name="parts">The arrays.</param>
    /// <returns>Them, one after another.</returns>
    internal static byte[] Join(params byte[][] parts)
    {
        var w = new WireWriter();
        foreach (byte[] part in parts)
        {
            w.WriteRaw(part);
        }

        return w.ToArray();
    }

    /// <summary>The ASCII bytes of a text, as a frame's kind or a fixture's letters.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Its bytes.</returns>
    internal static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
