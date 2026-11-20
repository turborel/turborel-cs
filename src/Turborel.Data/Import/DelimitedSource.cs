// Delimited text as a source [CLIENT Importing]: CSV as RFC 4180 §2
// defines it, and tab-separated text, a record a line, read a line at a
// time from a stream, a record at most the dialect's bound, so a file of
// any size is read in bounded memory and a quote left open is refused at
// the bound, as turborel-client's Delimited reads them. A record ends at
// a line break, CR LF or LF alone, the last one's optional; a CSV field
// may be enclosed in double quotes, and must be to hold the delimiter, a
// quote or a line break, a quote in it written twice; a quote in a field
// not enclosed, or anything but the delimiter after a closing quote, is
// refused. A field written as the dialect's null, not enclosed, is null.
// Text is UTF-8; a byte order mark beginning the input is passed over.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Turborel.Data.Import;

/// <summary>Delimited text read from a stream, a record at a time.</summary>
/// <param name="input">The text, which the source reads and does not dispose.</param>
/// <param name="dialect">How it is written.</param>
public sealed class DelimitedSource(Stream input, DelimitedDialect dialect) : IImportSource
{
    private const byte Quote = (byte)'"';
    private const byte Lf = (byte)'\n';
    private const byte Cr = (byte)'\r';
    private const int ReadBytes = 64 * 1024;
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly List<byte> _record = [];
    private readonly byte[] _buffer = new byte[ReadBytes];
    private int _at;
    private int _filled;
    private bool _ended;
    private ulong _lines;
    private ulong _records;
    private bool _headerPending = dialect.Header;
    private bool _started;
    private bool _done;

    // Where a scan of a CSV record is: so whether a line break ends it.
    private enum Scan
    {
        FieldStart,
        Plain,
        Quoted,
        QuoteInQuoted,
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ImportHeading>?> HeadingsAsync(CancellationToken cancellationToken = default) =>
        await HeaderAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async ValueTask<ImportRecord?> NextAsync(CancellationToken cancellationToken = default)
    {
        _ = await HeaderAsync(cancellationToken).ConfigureAwait(false);
        if (_done)
        {
            return null;
        }

        ulong number = _records + 1;
        if (await FieldsAsync(number, cancellationToken).ConfigureAwait(false) is not (List<(string Text, bool Quoted)> fields, ulong line))
        {
            return null;
        }

        _records = number;
        object?[] values = new object?[fields.Count];
        for (int i = 0; i < fields.Count; i++)
        {
            values[i] = !fields[i].Quoted && fields[i].Text == dialect.Null ? null : fields[i].Text;
        }

        return new ImportRecord(values, new ImportPosition(number, line));
    }

    private static string Unclosed() => "[RFC 4180 §2 rule 5] a quoted field is not closed";

    private static string Text(List<byte> bytes, int n)
    {
        try
        {
            return Utf8.GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            throw new FormatException($"[DESIGN] field {n + 1} is not UTF-8");
        }
    }

    // The bytes of a field enclosed in quotes, its opening quote read, to
    // its closing quote, a quote written twice one [RFC 4180 §2 rule 7].
    private static int Enclosed(List<byte> record, int at, List<byte> written, int n)
    {
        while (true)
        {
            if (at >= record.Count)
            {
                throw new FormatException($"field {n + 1}: {Unclosed()}");
            }

            byte b = record[at++];
            if (b == Quote)
            {
                if (at < record.Count && record[at] == Quote)
                {
                    at++;
                }
                else
                {
                    return at;
                }
            }

            written.Add(b);
        }
    }

    private static Scan Advance(Scan was, byte b, byte delimiter)
    {
        bool quote = b == Quote;
        bool split = b == delimiter;
        return was switch
        {
            Scan.Quoted => quote ? Scan.QuoteInQuoted : Scan.Quoted,
            Scan.FieldStart or Scan.QuoteInQuoted => split ? Scan.FieldStart : quote ? Scan.Quoted : Scan.Plain,
            _ => split ? Scan.FieldStart : Scan.Plain,
        };
    }

    private async ValueTask<IReadOnlyList<ImportHeading>?> HeaderAsync(CancellationToken cancellationToken)
    {
        if (!_headerPending)
        {
            return null;
        }

        _headerPending = false;
        if (await FieldsAsync(0, cancellationToken).ConfigureAwait(false) is not (List<(string Text, bool Quoted)> fields, _))
        {
            throw ImportFailure.Input(new ImportPosition(0, 1), "[DESIGN] there is no header");
        }

        return fields.ConvertAll(f => new ImportHeading(f.Text));
    }

    // The next record's fields, and the line it began on; null at the end.
    private async ValueTask<(List<(string Text, bool Quoted)> Fields, ulong Line)?> FieldsAsync(ulong record, CancellationToken cancellationToken)
    {
        ulong line = _lines + 1;
        try
        {
            if (!await RawAsync(cancellationToken).ConfigureAwait(false))
            {
                _done = true;
                return null;
            }

            return (Split(), line);
        }
        catch (FormatException e)
        {
            _done = true;
            throw ImportFailure.Input(new ImportPosition(record, line), e.Message);
        }
        catch (IOException e)
        {
            _done = true;
            throw ImportFailure.Input(new ImportPosition(record, line), $"[DESIGN] cannot read: {e.Message}");
        }
    }

    // Reads the next record's bytes, its line break taken off: false at the end.
    private async ValueTask<bool> RawAsync(CancellationToken cancellationToken)
    {
        _record.Clear();
        Scan at = Scan.FieldStart;
        while (true)
        {
            int start = _record.Count;
            await LineAsync(cancellationToken).ConfigureAwait(false);
            if (_record.Count > dialect.MaxRecord)
            {
                throw new FormatException($"[DESIGN] the record is longer than {dialect.MaxRecord} bytes");
            }

            if (_record.Count == start)
            {
                return at == Scan.Quoted ? throw new FormatException(Unclosed()) : _record.Count > 0;
            }

            if (dialect.Quoted)
            {
                for (int i = start; i < _record.Count; i++)
                {
                    at = Advance(at, _record[i], dialect.Delimiter);
                }
            }

            _lines++;

            // A line break in a quoted field does not end the record.
            if (at != Scan.Quoted)
            {
                Ended();
                return true;
            }
        }
    }

    // Reads to the next LF, with it, or the end; at most one byte past the bound.
    private async ValueTask LineAsync(CancellationToken cancellationToken)
    {
        while (_record.Count <= dialect.MaxRecord)
        {
            if (_at == _filled)
            {
                if (_ended)
                {
                    return;
                }

                _filled = await input.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                _at = 0;
                _ended = _filled == 0;
                continue;
            }

            byte b = _buffer[_at++];
            _record.Add(b);
            if (b == Lf)
            {
                return;
            }
        }
    }

    // Takes the record's line break off, and a byte order mark off the first's.
    private void Ended()
    {
        if (_record.Count > 0 && _record[^1] == Lf)
        {
            _record.RemoveAt(_record.Count - 1);
            if (_record.Count > 0 && _record[^1] == Cr)
            {
                _record.RemoveAt(_record.Count - 1);
            }
        }

        if (!_started && _record.Count >= Bom.Length && _record[0] == Bom[0] && _record[1] == Bom[1] && _record[2] == Bom[2])
        {
            _record.RemoveRange(0, Bom.Length);
        }

        _started = true;
    }

    // The fields of the record read, written as the dialect says.
    private List<(string Text, bool Quoted)> Split()
    {
        var fields = new List<(string, bool)>();
        int at = 0;
        while (true)
        {
            int n = fields.Count;
            var written = new List<byte>();
            bool quoted = dialect.Quoted && at < _record.Count && _record[at] == Quote;
            if (quoted)
            {
                at = Enclosed(_record, at + 1, written, n);
            }
            else
            {
                while (at < _record.Count && _record[at] != dialect.Delimiter)
                {
                    if (dialect.Quoted && _record[at] == Quote)
                    {
                        throw new FormatException($"[RFC 4180 §2 rule 5] field {n + 1}: a quote in a field not enclosed in quotes");
                    }

                    written.Add(_record[at++]);
                }
            }

            fields.Add((Text(written, n), quoted));
            if (at >= _record.Count)
            {
                return fields;
            }

            if (_record[at++] != dialect.Delimiter)
            {
                throw new FormatException($"[RFC 4180 §2 rule 5] field {n + 1}: a quoted field's closing quote is followed by more");
            }
        }
    }
}
