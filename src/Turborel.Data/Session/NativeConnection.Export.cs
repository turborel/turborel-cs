// Export [CLIENT Copying and exporting]: a query's rows streamed from the
// server and written as they come, a batch at a time, so memory holds a
// batch whatever the result's size. As CSV [RFC 4180]: a header of the
// columns' names, then a record for each row, ended by CR LF, a field
// quoted where it holds a comma, a quote, a line break or spaces at either
// end, or is empty, a quote in it written twice; a null an empty field,
// unquoted. Or as native rows: the protocol's own frames, a Description,
// then Rows, then a Complete, which NativeRows reads back, every value as
// it was.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Session;

/// <summary>How an export writes rows.</summary>
public enum ExportFormat
{
    /// <summary>CSV, with a header of the columns' names [RFC 4180].</summary>
    Csv,

    /// <summary>The protocol's frames: a Description, Rows, a Complete.</summary>
    Native,
}

/// <summary>The connection's exports.</summary>
public sealed partial class NativeConnection
{
    /// <summary>Runs the query <paramref name="sql"/> and writes its rows to <paramref name="output"/> as <paramref name="format"/> says, as they come.</summary>
    /// <param name="sql">The query.</param>
    /// <param name="output">Where the rows go.</param>
    /// <param name="format">How they are written.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>How many rows were written.</returns>
    /// <exception cref="TurborelException">The query's, as <see cref="QueryIntoAsync"/>'s; a statement that is no query; a write to <paramref name="output"/> that failed (SQLSTATE 08006, the export's, not the connection's).</exception>
    public async Task<ulong> ExportAsync(string sql, Stream output, ExportFormat format, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(output);
        var writer = new ExportWriter(output, format);
        Streamed streamed = await QueryIntoAsync(sql, writer, cancellationToken).ConfigureAwait(false);
        if (streamed.RowsSent is not ulong count)
        {
            throw ConnectionFailure.Of("[DESIGN] an export runs a query");
        }

        if (format == ExportFormat.Native)
        {
            writer.Frame(ReplyKinds.Complete, w =>
            {
                w.WriteI32(0);
                w.WriteString("00000");
                w.WriteFlag(false);
            });
        }

        try
        {
            writer.ThrowIfFailed();
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException e)
        {
            throw new TurborelException(ConnectionFailure.SqlCode, ConnectionFailure.SqlState, $"[DESIGN] cannot write the export: {e.Message}", e);
        }

        return count;
    }

    // A query's rows, written to a stream as they come.
    private sealed class ExportWriter(Stream output, ExportFormat format) : IRowSink
    {
        private IOException? _failed;

        public void Columns(IReadOnlyList<Column> columns)
        {
            if (format == ExportFormat.Csv)
            {
                Write(CsvRecord(columns.Select(c => CsvField(c.Name ?? string.Empty))));
                return;
            }

            Frame(ReplyKinds.Description, w =>
            {
                w.WriteU32((uint)columns.Count);
                foreach (Column column in columns)
                {
                    column.Write(w);
                }
            });
        }

        public void Rows(IReadOnlyList<IReadOnlyList<object?>> rows)
        {
            if (format == ExportFormat.Csv)
            {
                Write([.. rows.SelectMany(row => CsvRecord(row.Select(CsvValue)))]);
                return;
            }

            Frame(ReplyKinds.Rows, w =>
            {
                w.WriteU32((uint)rows.Count);
                foreach (IReadOnlyList<object?> row in rows)
                {
                    ValueCodec.WriteList(w, row);
                }
            });
        }

        // Writes a frame of kind, its body as write writes it.
        public void Frame(byte kind, Action<WireWriter> write)
        {
            var w = new WireWriter();
            int start = w.BeginFrame(kind);
            write(w);
            w.EndFrame(start);
            Write(w.ToArray());
        }

        public void ThrowIfFailed()
        {
            if (_failed is { } failed)
            {
                throw failed;
            }
        }

        private void Write(byte[] bytes)
        {
            if (_failed is not null)
            {
                return;
            }

            try
            {
                output.Write(bytes);
            }
            catch (IOException e)
            {
                _failed = e;
            }
        }
    }

    /// <summary>Writes a field as CSV writes it: quoted where it must be [RFC 4180 §2].</summary>
    /// <param name="field">The field's text.</param>
    /// <returns>The field.</returns>
    internal static string CsvField(string field)
    {
        bool quoted = field.Length == 0
            || field.AsSpan().IndexOfAny(",\"\n\r") >= 0
            || char.IsWhiteSpace(field[0])
            || char.IsWhiteSpace(field[^1]);
        return quoted ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
    }

    /// <summary>Writes a value as a CSV field: a null empty and unquoted, any other its text, as turborel shows it.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The field.</returns>
    internal static string CsvValue(object? value) => value is null ? string.Empty : CsvField(SqlText.Text(value));

    // A CSV record of fields, ended by CR LF [RFC 4180 §2].
    private static byte[] CsvRecord(IEnumerable<string> fields) => Encoding.UTF8.GetBytes(string.Join(",", fields) + "\r\n");
}
