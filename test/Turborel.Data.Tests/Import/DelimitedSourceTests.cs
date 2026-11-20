// Delimited text read as a source, as turborel-client's delimited tests
// read it (RULES.md R-TEST-9): CSV's records and fields, the text of a
// null and a byte order mark, a header and each record's position, errors
// saying where, records bounded, tab-separated text and delimiters
// [CLIENT Importing].

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Import;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Import;

/// <summary>Tests of <see cref="DelimitedSource"/> and <see cref="DelimitedDialect"/>.</summary>
public sealed class DelimitedSourceTests
{
    /// <summary>
    /// CSV's records end at CR LF or LF, the last optional; fields split at
    /// commas, spaces kept; a field in quotes holds commas, line breaks and
    /// quotes written twice; an empty field is null, one of empty quotes the
    /// empty string; an empty line is a record of one null field.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task CsvRecordsAndFields()
    {
        Assert.Equal([["a", "b"], ["c", "d"]], await FieldsAsync("a,b\r\nc,d\n", DelimitedDialect.Csv()));
        Assert.Equal([["a", "b"], ["c", "d"]], await FieldsAsync("a,b\nc,d", DelimitedDialect.Csv()));
        Assert.Equal([[" a ", " b "]], await FieldsAsync(" a , b ", DelimitedDialect.Csv()));
        Assert.Equal([["a,b", "say \"hi\""]], await FieldsAsync("\"a,b\",\"say \"\"hi\"\"\"", DelimitedDialect.Csv()));
        Assert.Equal([["one\r\ntwo\nthree", "x"]], await FieldsAsync("\"one\r\ntwo\nthree\",x\n", DelimitedDialect.Csv()));
        Assert.Equal([[null, string.Empty, null]], await FieldsAsync(",\"\",", DelimitedDialect.Csv()));
        Assert.Equal([["a"], [null], ["b"]], await FieldsAsync("a\n\nb\n", DelimitedDialect.Csv()));
        Assert.Equal([["\""]], await FieldsAsync("\"\"\"\"", DelimitedDialect.Csv()));
        Assert.Empty(await FieldsAsync(string.Empty, DelimitedDialect.Csv()));
        Assert.Equal([["é", "\U0001F600"]], await FieldsAsync("é,\U0001F600", DelimitedDialect.Csv()));
    }

    /// <summary>The text for null is null only not in quotes; a byte order mark beginning the input is passed over, and nowhere else.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task CsvNullTextAndByteOrderMark()
    {
        Assert.Equal([[null, "NULL", string.Empty, "null"]], await FieldsAsync("NULL,\"NULL\",,null", DelimitedDialect.Csv().WithNull("NULL")));
        Assert.Equal([["a", "b"], ["﻿c"]], await FieldsAsync("﻿a,b\n﻿c", DelimitedDialect.Csv()));
    }

    /// <summary>
    /// A header names the fields, read once, whether or not asked for;
    /// records are numbered from the first after it, each saying the line it
    /// began on, past quoted line breaks; a header missing is refused.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task CsvHeaderAndPositions()
    {
        const string Text = "ID,\"FULL NAME\"\n1,\"Ada\nLovelace\"\n2,Grace\n";
        var source = new DelimitedSource(Of(Text), DelimitedDialect.Csv().WithHeader());

        IReadOnlyList<ImportHeading>? headings = await source.HeadingsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["ID", "FULL NAME"], headings!.Select(h => h.Name));
        Assert.All(headings!, h => Assert.Null(h.Type));
        Assert.Null(await source.HeadingsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new ImportPosition(1, 2), (await source.NextAsync(TestContext.Current.CancellationToken))!.Position);
        Assert.Equal(new ImportPosition(2, 4), (await source.NextAsync(TestContext.Current.CancellationToken))!.Position);
        Assert.Null(await source.NextAsync(TestContext.Current.CancellationToken));
        Assert.Null(await source.NextAsync(TestContext.Current.CancellationToken));
        Assert.Equal([["1", "Ada\nLovelace"], ["2", "Grace"]], await FieldsAsync(Text, DelimitedDialect.Csv().WithHeader()));
        TurborelException missing = await Assert.ThrowsAsync<TurborelException>(
            async () => await new DelimitedSource(Of(string.Empty), DelimitedDialect.Csv().WithHeader()).HeadingsAsync(TestContext.Current.CancellationToken));
        Assert.Equal((new ImportPosition(0, 1), "[DESIGN] there is no header"), (missing.Import?.Position, missing.Message));
        Assert.Equal((-301, "22000"), (missing.SqlCode, missing.SqlState));
        Assert.Null(await new DelimitedSource(Of("a"), DelimitedDialect.Csv()).HeadingsAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A quote in a field not quoted, anything but the delimiter after a
    /// closing quote, a quote left open, and a field not UTF-8 are refused,
    /// saying the record, its line and the field; nothing more comes after.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task CsvErrorsSayWhere()
    {
        TurborelException quote = await ErrorAsync("a\nb,c\"d\n"u8.ToArray(), DelimitedDialect.Csv());
        Assert.Equal((new ImportPosition(2, 2), "[RFC 4180 §2 rule 5] field 2: a quote in a field not enclosed in quotes"), (quote.Import?.Position, quote.Message));
        Assert.Equal("[RFC 4180 §2 rule 5] field 1: a quoted field's closing quote is followed by more", (await ErrorAsync("\"a\"b,c"u8.ToArray(), DelimitedDialect.Csv())).Message);
        TurborelException open = await ErrorAsync("a\nb\n\"c\nd\ne\n"u8.ToArray(), DelimitedDialect.Csv());
        Assert.Equal((new ImportPosition(3, 3), "[RFC 4180 §2 rule 5] a quoted field is not closed"), (open.Import?.Position, open.Message));
        Assert.Equal("[RFC 4180 §2 rule 5] a quoted field is not closed", (await ErrorAsync("\"c"u8.ToArray(), DelimitedDialect.Csv())).Message);
        TurborelException bytes = await ErrorAsync([.. "ok,"u8, 0xFF, 0xFE], DelimitedDialect.Csv());
        Assert.Equal("record 1, line 1: [DESIGN] field 2 is not UTF-8", $"{bytes.Import?.Position}: {bytes.Message}");
        Assert.Equal("[DESIGN] field 2 is not UTF-8", (await ErrorAsync([.. "ok\t"u8, 0xFF], DelimitedDialect.Tsv())).Message);
        var source = new DelimitedSource(Of("\"a\"b\nc\n"), DelimitedDialect.Csv());
        await Assert.ThrowsAsync<TurborelException>(async () => await source.NextAsync(TestContext.Current.CancellationToken));
        Assert.Null(await source.NextAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A record longer than the bound is refused once the bound is read, so
    /// an endless input, or a quote never closed, is read in bounded memory;
    /// a record of the bound is read; the default bound is the protocol's
    /// longest body; a reader's failure is told.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task RecordsAreBounded()
    {
        Assert.Equal("[DESIGN] the record is longer than 1024 bytes", (await ErrorAsync(new Endless((byte)'a', long.MaxValue), DelimitedDialect.Csv().WithMaxRecord(1024))).Message);
        Assert.Equal("[DESIGN] the record is longer than 4096 bytes", (await ErrorAsync(new Endless((byte)'"', 10_000), DelimitedDialect.Csv().WithMaxRecord(4096))).Message);
        Assert.Equal([["abcd"]], await FieldsAsync("abcd\n", DelimitedDialect.Csv().WithMaxRecord(5)));
        await ErrorAsync("abcde\n"u8.ToArray(), DelimitedDialect.Csv().WithMaxRecord(5));
        Assert.Equal(DelimitedDialect.DefaultMaxRecord, (int)Turborel.Data.Protocol.FrameHeader.MaxBody);
        Assert.Equal("[DESIGN] cannot read: the disk is gone", (await ErrorAsync(new Failing(), DelimitedDialect.Csv())).Message);
    }

    /// <summary>Tab-separated text splits at tabs, with no quoting; an empty field is null.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task TabSeparatedRecords()
    {
        Assert.Equal([["a", "b"], ["\"c", "\"d\""]], await FieldsAsync("a\tb\r\n\"c\t\"d\"\n", DelimitedDialect.Tsv()));
        Assert.Equal([["a", null, "b,c"]], await FieldsAsync("a\t\tb,c", DelimitedDialect.Tsv()));
        Assert.Equal([["a", null]], await FieldsAsync("a\t\\N", DelimitedDialect.Tsv().WithNull("\\N")));
    }

    /// <summary>A delimiter is one ASCII character, not a quote, CR or LF; fields then split at it.</summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[CLIENT Importing]")]
    public async Task Delimiters()
    {
        Assert.Equal([["a", "b;c", "d,e"]], await FieldsAsync("a;\"b;c\";d,e", DelimitedDialect.Csv().WithDelimiter(';')));
        Assert.Equal([["a", "b\tc"]], await FieldsAsync("a|b\tc", DelimitedDialect.Tsv().WithDelimiter('|')));
        foreach (char wrong in new[] { '"', '\n', '\r', 'é' })
        {
            TurborelException refused = Assert.Throws<TurborelException>(() => DelimitedDialect.Csv().WithDelimiter(wrong));
            Assert.StartsWith("[DESIGN] a delimiter is one ASCII character", refused.Message, StringComparison.Ordinal);
            Assert.Equal("TB910", refused.SqlState);
        }
    }

    private static MemoryStream Of(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<List<string?[]>> FieldsAsync(string text, DelimitedDialect dialect)
    {
        var source = new DelimitedSource(Of(text), dialect);
        var records = new List<string?[]>();
        while (await source.NextAsync() is { } record)
        {
            records.Add([.. record.Values.Select(v => (string?)v)]);
        }

        return records;
    }

    private static Task<TurborelException> ErrorAsync(byte[] bytes, DelimitedDialect dialect) => ErrorAsync(new MemoryStream(bytes), dialect);

    private static async Task<TurborelException> ErrorAsync(Stream input, DelimitedDialect dialect)
    {
        var source = new DelimitedSource(input, dialect);
        return await Assert.ThrowsAsync<TurborelException>(async () =>
        {
            while (await source.NextAsync() is not null)
            {
            }
        });
    }

    // A stream of one byte repeated, so many times, then line breaks forever.
    private sealed class Endless(byte b, long count) : Stream
    {
        private long _left = count;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = _left-- > 0 ? b : (byte)'\n';
            }

            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A stream whose reads fail.
    private sealed class Failing : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("the disk is gone");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
