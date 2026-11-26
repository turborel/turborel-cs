// The requests a client sends, each encoded as a frame of its kind, its
// fields in order and its tail after them [PROTOCOL Requests]. A secret a
// request carries, a password, a SCRAM proof or a cancel key, is never in
// its text (RULES.md R-SAFE-4).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Turborel.Data.Protocol;

/// <summary>A request, encoded as the frame the client sends.</summary>
internal abstract record Request
{
    /// <summary>The protocol's version, Hello's first field.</summary>
    internal const uint Version = 1;

    /// <summary>The <c>what</c> of a Describe of a statement.</summary>
    private const byte DescribeStatement = 0;

    /// <summary>The <c>what</c> of a Describe of a cursor.</summary>
    private const byte DescribeCursor = 1;

    /// <summary>Gets the request's kind.</summary>
    internal abstract byte Kind { get; }

    /// <summary>Appends the request, framed, to <paramref name="w"/>.</summary>
    /// <param name="w">The writer.</param>
    internal void Encode(WireWriter w)
    {
        int start = w.BeginFrame(Kind);
        WriteBody(w);
        w.EndFrame(start);
    }

    /// <summary>Writes the request's body.</summary>
    /// <param name="w">The writer.</param>
    private protected abstract void WriteBody(WireWriter w);

    /// <summary>
    /// Writes tail tag 1, attributes, unless there are none; more than the
    /// server takes are refused before anything is sent, as the server would
    /// fail the connection over them [PROTOCOL The tail].
    /// </summary>
    /// <param name="w">The writer.</param>
    /// <param name="attributes">The attributes, each a name and a value.</param>
    /// <exception cref="TurborelException">
    /// SQLCODE -919, SQLSTATE 08006, past 32 attributes, a name not of 1 to 64
    /// bytes, or a value past 1024.
    /// </exception>
    private protected static void WriteAttributes(WireWriter w, IReadOnlyList<KeyValuePair<string, string>> attributes)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        if (attributes.Count > TailTags.MaxAttributes || !attributes.All(Bounded))
        {
            throw ConnectionFailure.Of(
                $"{Sections.Tail} a message takes at most {TailTags.MaxAttributes} attributes, each name of 1 to "
                + $"{TailTags.MaxAttributeName} bytes and each value of at most {TailTags.MaxAttributeValue}");
        }

        w.WriteU8(TailTags.Attributes);
        w.WriteCount(attributes.Count);
        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            w.WriteString(attribute.Key);
            w.WriteString(attribute.Value);
        }
    }

    private static bool Bounded(KeyValuePair<string, string> attribute)
    {
        int name = Encoding.UTF8.GetByteCount(attribute.Key);
        return name is > 0 and <= TailTags.MaxAttributeName
            && Encoding.UTF8.GetByteCount(attribute.Value) <= TailTags.MaxAttributeValue;
    }

    /// <summary>Writes tail tag 3, the cursor, unless it is 0.</summary>
    /// <param name="w">The writer.</param>
    /// <param name="cursor">The cursor's number.</param>
    private protected static void WriteCursor(WireWriter w, uint cursor)
    {
        if (cursor != 0)
        {
            w.WriteU8(TailTags.Cursor);
            w.WriteU32(cursor);
        }
    }

    /// <summary>Hello: version, user, mechanism, its first bytes, capabilities; tail 1 [PROTOCOL Hello 'H'].</summary>
    /// <param name="User">The user, read by the server as an identifier.</param>
    /// <param name="Mechanism">The mechanism of authentication.</param>
    /// <param name="Initial">The mechanism's first bytes, a password's UTF-8 of <c>password</c>.</param>
    /// <param name="Capabilities">The capabilities asked for.</param>
    /// <param name="Attributes">The connection's attributes.</param>
    internal sealed record Hello(
        string User,
        string Mechanism,
        ReadOnlyMemory<byte> Initial,
        IReadOnlyList<string> Capabilities,
        IReadOnlyList<KeyValuePair<string, string>> Attributes) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Hello;

        /// <inheritdoc/>
        public override string ToString() => $"Hello {{ User = {User}, Mechanism = {Mechanism} }}";

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU32(Version);
            w.WriteString(User);
            w.WriteString(Mechanism);
            w.WriteBytes(Initial.Span);
            w.WriteStrings(Capabilities);
            WriteAttributes(w, Attributes);
        }
    }

    /// <summary>Answer: the client's answer to a Challenge [PROTOCOL Answer 'A'].</summary>
    /// <param name="Bytes">The answer.</param>
    internal sealed record Answer(ReadOnlyMemory<byte> Bytes) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Answer;

        /// <inheritdoc/>
        public override string ToString() => "Answer { .. }";

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w) => w.WriteBytes(Bytes.Span);
    }

    /// <summary>Query: a statement, ends; tail 1 and 2 [PROTOCOL Query 'Q'].</summary>
    /// <param name="Sql">The statement, or with <paramref name="Multi"/> several.</param>
    /// <param name="Ends">Whether the transaction ends with the statement.</param>
    /// <param name="Attributes">The request's attributes.</param>
    /// <param name="Multi">Whether the text is several statements.</param>
    internal sealed record Query(
        string Sql,
        bool Ends,
        IReadOnlyList<KeyValuePair<string, string>> Attributes,
        bool Multi = false) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Query;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteString(Sql);
            w.WriteFlag(Ends);
            WriteAttributes(w, Attributes);
            if (Multi)
            {
                w.WriteU8(TailTags.Multi);
            }
        }
    }

    /// <summary>Prepare: the client's number for a statement, its text [PROTOCOL Prepare 'P'].</summary>
    /// <param name="Statement">The client's number.</param>
    /// <param name="Sql">The statement.</param>
    internal sealed record Prepare(uint Statement, string Sql) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Prepare;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU32(Statement);
            w.WriteString(Sql);
        }
    }

    /// <summary>Execute: statement, values, rows, ends; tail 1 and 3 [PROTOCOL Execute 'E'].</summary>
    /// <param name="Target">The statement, and the cursor a query opens.</param>
    /// <param name="Values">One value for each parameter.</param>
    /// <param name="Rows">The most rows sent now; 0 for all.</param>
    /// <param name="Ends">Whether the transaction ends with the statement.</param>
    /// <param name="Attributes">The request's attributes.</param>
    internal sealed record Execute(
        StatementCursor Target,
        IReadOnlyList<object?> Values,
        uint Rows,
        bool Ends,
        IReadOnlyList<KeyValuePair<string, string>> Attributes) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Execute;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU32(Target.Statement);
            ValueCodec.WriteList(w, Values);
            w.WriteU32(Rows);
            w.WriteFlag(Ends);
            WriteAttributes(w, Attributes);
            WriteCursor(w, Target.Cursor);
        }
    }

    /// <summary>Fetch: statement, rows; tail 3 [PROTOCOL Fetch 'F'].</summary>
    /// <param name="Target">The statement and its open cursor.</param>
    /// <param name="Rows">The most rows sent; 0 for all.</param>
    internal sealed record Fetch(StatementCursor Target, uint Rows) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Fetch;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU32(Target.Statement);
            w.WriteU32(Rows);
            WriteCursor(w, Target.Cursor);
        }
    }

    /// <summary>Close: statement; tail 3 [PROTOCOL Close 'C'].</summary>
    /// <param name="Target">The statement and its open cursor.</param>
    internal sealed record Close(StatementCursor Target) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Close;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU32(Target.Statement);
            WriteCursor(w, Target.Cursor);
        }
    }

    /// <summary>Deallocate: statement [PROTOCOL Deallocate 'D'].</summary>
    /// <param name="Statement">The statement's number.</param>
    internal sealed record Deallocate(uint Statement) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Deallocate;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w) => w.WriteU32(Statement);
    }

    /// <summary>Cancel: the key a connection's Ready gave [PROTOCOL Cancel 'Z'].</summary>
    /// <param name="Key">The key, 16 bytes.</param>
    internal sealed record Cancel(ReadOnlyMemory<byte> Key) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Cancel;

        /// <inheritdoc/>
        public override string ToString() => "Cancel { .. }";

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w) => w.WriteRaw(Key.Span);
    }

    /// <summary>Terminate, with no body [PROTOCOL Terminate 'X'].</summary>
    internal sealed record Terminate : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Terminate;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            // Terminate is its header alone.
        }
    }

    /// <summary>Describe: what, statement, cursor [PROTOCOL Describe 'Y'].</summary>
    /// <param name="Target">The statement, and of a cursor its number.</param>
    /// <param name="OfCursor">Whether it describes the open cursor, not the statement.</param>
    internal sealed record Describe(StatementCursor Target, bool OfCursor) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Describe;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteU8(OfCursor ? DescribeCursor : DescribeStatement);
            w.WriteU32(Target.Statement);
            w.WriteU32(OfCursor ? Target.Cursor : 0);
        }
    }

    /// <summary>Ping, with no body [PROTOCOL Ping 'G'].</summary>
    internal sealed record Ping : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Ping;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            // Ping is its header alone.
        }
    }

    /// <summary>Reset, with no body [PROTOCOL Reset 'W'].</summary>
    internal sealed record Reset : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.Reset;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            // Reset is its header alone.
        }
    }

    /// <summary>ChangeUser: user, mechanism, its first bytes [PROTOCOL ChangeUser 'U'].</summary>
    /// <param name="User">The new user.</param>
    /// <param name="Mechanism">The mechanism of authentication.</param>
    /// <param name="Initial">The mechanism's first bytes.</param>
    internal sealed record ChangeUser(string User, string Mechanism, ReadOnlyMemory<byte> Initial) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.ChangeUser;

        /// <inheritdoc/>
        public override string ToString() => $"ChangeUser {{ User = {User}, Mechanism = {Mechanism} }}";

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteString(User);
            w.WriteString(Mechanism);
            w.WriteBytes(Initial.Span);
        }
    }

    /// <summary>CopyIn: table, columns, ends [PROTOCOL CopyIn 'I'].</summary>
    /// <param name="Table">The table, as SQL names it.</param>
    /// <param name="Columns">Its columns the rows give, in order; none for every column.</param>
    /// <param name="Ends">Whether the transaction ends with the copy.</param>
    internal sealed record CopyIn(string Table, IReadOnlyList<string> Columns, bool Ends) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.CopyIn;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteString(Table);
            w.WriteStrings(Columns);
            w.WriteFlag(Ends);
        }
    }

    /// <summary>CopyData: a list of rows [PROTOCOL CopyData 'J'].</summary>
    /// <param name="Rows">The rows, each a value for each column.</param>
    internal sealed record CopyData(IReadOnlyList<IReadOnlyList<object?>> Rows) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.CopyData;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            w.WriteCount(Rows.Count);
            foreach (IReadOnlyList<object?> row in Rows)
            {
                ValueCodec.WriteList(w, row);
            }
        }
    }

    /// <summary>
    /// CopyColumns: rows of the copy by column, as text, with <c>columns</c>
    /// [PROTOCOL CopyColumns 'O']: rows, columns, then of each column its
    /// null bits, row 0 the low bit of the first byte, the ends of its
    /// cells in its text, a null's where the one before it ends, and its
    /// cells' UTF-8 one after another.
    /// </summary>
    /// <param name="Rows">The rows, each a cell of text, or null, for each column; every row as wide, and at least one wide.</param>
    internal sealed record CopyColumns(IReadOnlyList<IReadOnlyList<string?>> Rows) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.CopyColumns;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w)
        {
            int width = Rows.Count == 0 ? 0 : Rows[0].Count;
            w.WriteCount(Rows.Count);
            w.WriteCount(width);
            for (int column = 0; column < width; column++)
            {
                byte[] nulls = new byte[(Rows.Count + 7) / 8];
                uint[] ends = new uint[Rows.Count];
                using var text = new System.IO.MemoryStream();
                for (int row = 0; row < Rows.Count; row++)
                {
                    if (Rows[row][column] is { } cell)
                    {
                        text.Write(Encoding.UTF8.GetBytes(cell));
                    }
                    else
                    {
                        nulls[row / 8] |= (byte)(1 << (row % 8));
                    }

                    ends[row] = (uint)text.Length;
                }

                w.WriteBytes(nulls);
                w.WriteCount(ends.Length);
                foreach (uint end in ends)
                {
                    w.WriteU32(end);
                }

                w.WriteBytes(text.ToArray());
            }
        }
    }

    /// <summary>CopyEnd: abandon [PROTOCOL CopyEnd 'N'].</summary>
    /// <param name="Abandon">Whether every row is undone.</param>
    internal sealed record CopyEnd(bool Abandon) : Request
    {
        /// <inheritdoc/>
        internal override byte Kind => RequestKinds.CopyEnd;

        /// <inheritdoc/>
        private protected override void WriteBody(WireWriter w) => w.WriteFlag(Abandon);
    }
}
