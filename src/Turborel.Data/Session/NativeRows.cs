// Native rows read back [CLIENT Copying and exporting]: what an export as
// the protocol's own frames wrote, a Description, Rows, then a Complete,
// every value as it was, of whatever type.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;

namespace Turborel.Data.Session;

/// <summary>Reads native rows, as <see cref="ExportFormat.Native"/> writes them.</summary>
public static class NativeRows
{
    /// <summary>Reads native rows from <paramref name="input"/>: the columns, and the rows.</summary>
    /// <param name="input">What an export as native rows wrote.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The columns, and the rows.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08006 for input that is not native rows, or cut short.</exception>
    public static async Task<(IReadOnlyList<Column> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows)> ReadAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(input);
        if (await NextAsync(input, cancellationToken).ConfigureAwait(false) is not Reply.Description description)
        {
            throw NotNative("no Description first");
        }

        var rows = new List<IReadOnlyList<object?>>();
        while (true)
        {
            switch (await NextAsync(input, cancellationToken).ConfigureAwait(false))
            {
                case Reply.Rows batch:
                    rows.AddRange(batch.Batch);
                    break;
                case Reply.Complete:
                    return (description.Columns, rows);
                default:
                    throw NotNative("a frame other than Rows before the Complete");
            }
        }
    }

    private static TurborelException NotNative(string why) => ConnectionFailure.Of($"[DESIGN] not native rows: {why}");

    private static async Task<Reply> NextAsync(Stream input, CancellationToken cancellationToken)
    {
        try
        {
            byte[] head = new byte[FrameHeader.Size];
            await input.ReadExactlyAsync(head, cancellationToken).ConfigureAwait(false);
            var header = FrameHeader.Decode(head, FrameHeader.MaxBody);
            byte[] body = new byte[header.Length];
            await input.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
            return Reply.Decode(header.Kind, body);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException)
        {
            throw NotNative(e.Message);
        }
    }
}
