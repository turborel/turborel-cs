// The replies a client receives, each decoded from its frame's kind and
// body, every byte of the body read [PROTOCOL Replies]. A kind no reply
// has, a body cut short, or bytes left over fail the connection
// [PROTOCOL Frames].

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Turborel.Data.Types;

namespace Turborel.Data.Protocol;

/// <summary>A reply, as the server sent it.</summary>
internal abstract record Reply
{
    /// <summary>The <c>what</c> of a Lob reply of data.</summary>
    private const byte LobDataWhat = 0;

    /// <summary>The <c>what</c> of a Lob reply of a locator.</summary>
    private const byte LobLocatorWhat = 1;

    /// <summary>The tags a Description's tail takes.</summary>
    private static readonly byte[] DeclaredTags = [TailTags.Declared];

    /// <summary>The tags a Prepared's tail takes.</summary>
    private static readonly byte[] PreparedTags = [TailTags.Declared, TailTags.Called];

    /// <summary>The tags a Complete's tail takes.</summary>
    private static readonly byte[] CompleteTags =
    [
        TailTags.Position, TailTags.More, TailTags.Rows, TailTags.Generated, TailTags.Conditions, TailTags.Time,
    ];

    /// <summary>
    /// Whether a reply of kind <paramref name="kind"/> and body
    /// <paramref name="body"/>, decoded or not, is the last its request is
    /// answered with, by the order of a statement's replies: a Description,
    /// Rows, a Notice, a Status or a debugger's Event has more after it; a Complete has if its
    /// tail says more (tag 5); any other reply ends its request's, as
    /// turborel-protocol's <c>ends_request</c> tells it.
    /// </summary>
    /// <param name="kind">The reply's kind.</param>
    /// <param name="body">Its body.</param>
    /// <returns>Whether it is its request's last.</returns>
    internal static bool EndsRequest(byte kind, ReadOnlyMemory<byte> body) => kind switch
    {
        ReplyKinds.Description or ReplyKinds.Rows or ReplyKinds.Notice or ReplyKinds.Status or ReplyKinds.Event => false,
        ReplyKinds.Complete => CompletedHead(kind, body) is not { More: true },
        _ => true,
    };

    /// <summary>
    /// The head of a reply of kind <paramref name="kind"/>, if it is a
    /// Complete whose head reads: its SQLCODE, SQLSTATE and whether a
    /// transaction is active, and of its tail the commit's position and
    /// whether more replies follow, which come before a condition, the one
    /// field a client may fail to take, as turborel-protocol's
    /// <c>completed_head</c> reads it.
    /// </summary>
    /// <param name="kind">The reply's kind.</param>
    /// <param name="body">Its body.</param>
    /// <returns>The head, or null.</returns>
    internal static Completion? CompletedHead(byte kind, ReadOnlyMemory<byte> body)
    {
        if (kind != ReplyKinds.Complete)
        {
            return null;
        }

        try
        {
            var r = new WireReader(body, "reply");
            var head = new Completion { SqlCode = r.ReadI32(), SqlState = r.ReadString(), Active = r.ReadFlag() };
            while (!r.AtEnd)
            {
                switch (r.ReadU8())
                {
                    case TailTags.Position:
                        head = head with { Position = r.ReadU64() };
                        break;
                    case TailTags.More:
                        head = head with { More = true };
                        break;
                    default:
                        return head;
                }
            }

            return head;
        }
        catch (TurborelException)
        {
            return null;
        }
    }

    /// <summary>Decodes the reply of kind <paramref name="kind"/> whose body is <paramref name="body"/>.</summary>
    /// <param name="kind">The frame's kind.</param>
    /// <param name="body">The frame's body.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="TurborelException">A connection failure, SQLCODE -919 and SQLSTATE 08006, if it is malformed.</exception>
    internal static Reply Decode(byte kind, ReadOnlyMemory<byte> body)
    {
        var r = new WireReader(body, "reply");
        Reply reply = kind switch
        {
            ReplyKinds.Challenge => new Challenge(r.ReadBytes().ToArray()),
            ReplyKinds.Ready => new Ready(r.ReadString(), r.ReadKey(), r.ReadStrings(), r.ReadString()),
            ReplyKinds.Prepared => ReadPrepared(r),
            ReplyKinds.Description => new Description(ReadColumns(r)),
            ReplyKinds.Rows => new Rows(ReadList(r, ValueCodec.ReadList)),
            ReplyKinds.Suspended => new Suspended(),
            ReplyKinds.Complete => new Complete(ReadCompletion(r)),
            ReplyKinds.Error => new Error(Failure.Read(r)),
            ReplyKinds.Lob => ReadLob(r),
            ReplyKinds.Notice => new Notice(r.ReadString(), r.ReadString(), Diagnostics.Read(r)),
            ReplyKinds.Status => new Status(r.ReadString(), r.ReadString()),
            ReplyKinds.Pong => new Pong(),
            ReplyKinds.Debugged => new Debugged(DebugCodec.ReadAnswer(r)),
            ReplyKinds.Event => new Event(DebugCodec.ReadEvent(r)),
            _ => throw r.Malformed(
                Sections.Frames,
                string.Create(CultureInfo.InvariantCulture, $"no reply is of kind {kind}")),
        };
        r.Finish();
        return reply;
    }

    /// <summary>Reads a list: its count, then each item; nothing reserved for the count.</summary>
    /// <typeparam name="T">What each item is.</typeparam>
    /// <param name="r">The reader.</param>
    /// <param name="item">Reads an item.</param>
    /// <returns>The items.</returns>
    internal static IReadOnlyList<T> ReadList<T>(WireReader r, Func<WireReader, T> item)
    {
        uint count = r.ReadCount();
        var items = new List<T>();
        for (uint i = 0; i < count; i++)
        {
            items.Add(item(r));
        }

        return items;
    }

    /// <summary>Reads columns, then their tail: each column's declared type, tag 19, if told [PROTOCOL Declared types].</summary>
    /// <param name="r">The reader.</param>
    /// <returns>The columns.</returns>
    internal static IReadOnlyList<Column> ReadColumns(WireReader r)
    {
        IReadOnlyList<Column> columns = ReadList(r, Column.Read);
        var tail = default(TailReader);
        while (tail.Next(r, DeclaredTags) is not null)
        {
            columns = ReadDeclared(r, columns);
        }

        return columns;
    }

    /// <summary>Writes a Prepared's columns and tail as the server does: their declared types, then what it tells of a CALL; for tests.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="columns">The columns.</param>
    /// <param name="called">What it tells of a CALL, if anything.</param>
    internal static void WritePreparedColumns(WireWriter w, IReadOnlyList<Column> columns, Called? called)
    {
        WriteColumns(w, columns);
        called?.Write(w);
    }

    /// <summary>Writes columns as the server does, their declared types in the tail if any is told; for tests and export.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="columns">The columns.</param>
    internal static void WriteColumns(WireWriter w, IReadOnlyList<Column> columns)
    {
        w.WriteU32((uint)columns.Count);
        foreach (Column column in columns)
        {
            column.Write(w);
        }

        if (columns.Any(c => c.Declared is not null))
        {
            w.WriteU8(TailTags.Declared);
            w.WriteU32((uint)columns.Count);
            foreach (Column column in columns)
            {
                w.WriteString(column.Declared?.Name ?? SqlText.Named(column.Type));
                w.WriteString(column.TypeWritten);
            }
        }
    }

    // A Prepared: its statement, parameters and columns, then its tail of
    // the columns' declared types, tag 19, and of a CALL its procedure and
    // each ?'s mode, tag 20 [PROTOCOL Parameter modes].
    private static Prepared ReadPrepared(WireReader r)
    {
        (uint statement, IReadOnlyList<Parameter> parameters) = (r.ReadU32(), ReadList(r, Parameter.Read));
        IReadOnlyList<Column> columns = ReadList(r, Column.Read);
        Called? called = null;
        var tail = default(TailReader);
        while (tail.Next(r, PreparedTags) is byte tag)
        {
            if (tag == TailTags.Declared)
            {
                columns = ReadDeclared(r, columns);
                continue;
            }

            called = Called.Read(r);
        }

        return new Prepared(statement, parameters, columns, called);
    }

    // Tag 19's field: each column's declared type, one for each; a count of
    // another number is a malformed reply and fails the connection, as
    // turborel-protocol's read_declared reads it.
    private static IReadOnlyList<Column> ReadDeclared(WireReader r, IReadOnlyList<Column> columns)
    {
        IReadOnlyList<DeclaredType> declared = ReadList(r, d => new DeclaredType(d.ReadString(), d.ReadString()));
        return declared.Count != columns.Count
            ? throw r.Malformed(Sections.Tail, "its declared types are not one for each column")
            : [.. columns.Zip(declared, (c, d) => c with { Declared = d })];
    }

    private static Reply ReadLob(WireReader r) => r.ReadU8() switch
    {
        LobDataWhat => new LobData(r.ReadBytes().ToArray()),
        LobLocatorWhat => new LobLocator(r.ReadU32()),
        _ => throw r.Malformed(Sections.Replies, "no Lob reply is of this kind"),
    };

    private static Completion ReadCompletion(WireReader r)
    {
        var c = new Completion { SqlCode = r.ReadI32(), SqlState = r.ReadString(), Active = r.ReadFlag() };
        var tail = default(TailReader);
        while (tail.Next(r, CompleteTags) is byte tag)
        {
            c = tag switch
            {
                TailTags.Position => c with { Position = r.ReadU64() },
                TailTags.More => c with { More = true },
                TailTags.Rows => c with { Rows = r.ReadU64() },
                TailTags.Generated => c with { Generated = r.ReadStrings() },
                TailTags.Conditions => c with { Conditions = ReadList(r, ReadCondition) },
                _ => c with { Time = new Timing(r.ReadU64(), r.ReadU64(), r.ReadU64()) },
            };
        }

        return c;
    }

    // A condition is a failure written as bytes, so its own tail ends
    // where its length does [PROTOCOL Complete 'K'].
    // One that cannot be read is the Complete's value failure, its head
    // still read, as turborel-client reads a condition apart from the frame.
    private static Failure ReadCondition(WireReader r)
    {
        var inner = new WireReader(r.ReadBytes(), "condition");
        try
        {
            var condition = Failure.Read(inner);
            inner.Finish();
            return condition;
        }
        catch (TurborelException e) when (!ValueFailure.IsValues(e))
        {
            throw new TurborelException(ValueFailure.CorruptSqlCode, ValueFailure.CorruptSqlState, e.Message, e);
        }
    }

    /// <summary>Challenge: the next step of an authentication exchange [PROTOCOL Challenge 'C'].</summary>
    /// <param name="Bytes">The challenge.</param>
    internal sealed record Challenge(ReadOnlyMemory<byte> Bytes) : Reply;

    /// <summary>Ready: the client is authenticated [PROTOCOL Ready 'R'].</summary>
    /// <param name="Authorization">The session's authorization identifier.</param>
    /// <param name="Key">The key that cancels the session's statements, a secret.</param>
    /// <param name="Capabilities">The capabilities granted.</param>
    /// <param name="Server">The server's product, as <c>turborel 0.0.0 (alpha)</c>.</param>
    internal sealed record Ready(string Authorization, ReadOnlyMemory<byte> Key, IReadOnlyList<string> Capabilities, string Server) : Reply
    {
        /// <inheritdoc/>
        public override string ToString() => $"Ready {{ Authorization = {Authorization}, Server = {Server} }}";
    }

    /// <summary>Prepared: a statement prepared, or described [PROTOCOL Prepared 'P'].</summary>
    /// <param name="Statement">The statement's number.</param>
    /// <param name="Parameters">Its input parameters, those an Execute gives values for, in the order of their <c>?</c> marks.</param>
    /// <param name="Columns">Its columns: a query's; of a CALL, those of its OUT and INOUT parameters' <c>?</c> marks; none of another statement.</param>
    /// <param name="Called">Granted parameter-modes, of a CALL of an SQL-invoked procedure, the procedure and each <c>?</c> with its parameter's mode [PROTOCOL Parameter modes].</param>
    internal sealed record Prepared(uint Statement, IReadOnlyList<Parameter> Parameters, IReadOnlyList<Column> Columns, Called? Called = null) : Reply;

    /// <summary>Description: the columns of the rows that follow [PROTOCOL Description 'D'].</summary>
    /// <param name="Columns">The columns.</param>
    internal sealed record Description(IReadOnlyList<Column> Columns) : Reply;

    /// <summary>Rows: a batch of rows [PROTOCOL Rows 'B'].</summary>
    /// <param name="Batch">The rows, each a list of values.</param>
    internal sealed record Rows(IReadOnlyList<IReadOnlyList<object?>> Batch) : Reply;

    /// <summary>Suspended: the rows asked for were sent and the cursor has more [PROTOCOL Suspended 'S'].</summary>
    internal sealed record Suspended : Reply;

    /// <summary>Complete: a statement's completion [PROTOCOL Complete 'K'].</summary>
    /// <param name="Completion">The completion.</param>
    internal sealed record Complete(Completion Completion) : Reply;

    /// <summary>Error: a statement's failure, or the connection's [PROTOCOL Error 'E'].</summary>
    /// <param name="Failure">The failure.</param>
    internal sealed record Error(Failure Failure) : Reply;

    /// <summary>Lob reply of data: what a Lob read asked for [PROTOCOL Lob 'L'], the reply.</summary>
    /// <param name="Bytes">The data.</param>
    internal sealed record LobData(ReadOnlyMemory<byte> Bytes) : Reply;

    /// <summary>Lob reply of a locator: what a Lob create made [PROTOCOL Lob 'L'], the reply.</summary>
    /// <param name="Locator">The locator.</param>
    internal sealed record LobLocator(uint Locator) : Reply;

    /// <summary>Notice: a condition a statement raised, or the server's own notice [PROTOCOL Notice 'N'].</summary>
    /// <param name="SqlState">Its SQLSTATE.</param>
    /// <param name="Message">Its message.</param>
    /// <param name="Diagnostics">Its diagnostics, if any were told.</param>
    internal sealed record Notice(string SqlState, string Message, Diagnostics? Diagnostics) : Reply;

    /// <summary>Status: a setting of the session [PROTOCOL Status 'T'].</summary>
    /// <param name="Name">The setting's name.</param>
    /// <param name="Value">Its value, as SQL writes it; empty for a null.</param>
    internal sealed record Status(string Name, string Value) : Reply;

    /// <summary>Pong: the answer to Ping [PROTOCOL Pong 'G'].</summary>
    internal sealed record Pong : Reply;

    /// <summary>Debugged: what a Debug request gave back, but a completion [PROTOCOL Debugged 'V'].</summary>
    /// <param name="Answer">The answer.</param>
    internal sealed record Debugged(DebugAnswer Answer) : Reply;

    /// <summary>Event: what a debugger hears of its target, sent unasked between requests [PROTOCOL Event 'W'].</summary>
    /// <param name="Told">The event.</param>
    internal sealed record Event(Debugging.DebugEvent Told) : Reply;
}
