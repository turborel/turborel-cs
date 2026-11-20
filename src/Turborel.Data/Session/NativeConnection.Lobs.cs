// A connection's large objects [CLIENT Large objects] [PROTOCOL Lob 'L']:
// one a result sent by its locator read by ranges, or as a stream, a chunk
// a request; a new one written a piece at a time and sent as a parameter
// by its locator; and a locator freed or held past its transaction's
// commit. One a result sent whole is read from the value itself. A
// locator is valid until the transaction it was given in ends, a held one
// until a rollback.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Session;

/// <summary>The connection's large objects.</summary>
public sealed partial class NativeConnection
{
    /// <summary>The octets a read of a large object asks for at most, and a writer sends in one Append: 256 KiB.</summary>
    public const int ChunkOctets = 262_144;

    /// <summary>The longest value a client sends whole for a large object, as the server sends one whole by default: 64 KiB.</summary>
    public const int LobInlineOctets = 64 * 1024;

    private const int DataSqlCode = -301;
    private const string DataSqlState = "22000";

    /// <summary>
    /// Reads at most a chunk of <paramref name="lob"/> from octet
    /// <paramref name="from"/>, at most <paramref name="count"/> of them; of a
    /// character large object, its characters if <paramref name="characters"/>,
    /// whole ones. Fewer than asked only at its end, or at a chunk's.
    /// </summary>
    /// <param name="lob">The large object.</param>
    /// <param name="from">The first octet or character, from 0.</param>
    /// <param name="count">The most octets or characters.</param>
    /// <param name="characters">Whether a character one is read by characters.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The octets read, UTF-8 of characters.</returns>
    /// <exception cref="TurborelException">SQLSTATE 0F001 if its locator is no longer valid; a connection failure.</exception>
    public async Task<byte[]> ReadLobAsync(TurborelLargeObject lob, ulong from, ulong count, bool characters, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(lob);
        if (lob.Data is { } held)
        {
            return HeldRange(lob.Kind, held, from, count, characters);
        }

        await SendAsync(new LobRequest.Read(lob.Locator, from, count, characters), cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.LobData data => data.Bytes.ToArray(),
            Reply.Error error => throw Failed(error.Failure),
            Reply.LobLocator => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent a locator for a Read")),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Lob reply")),
        };
    }

    /// <summary>Gives <paramref name="lob"/>'s octets as a stream, read a chunk at a time over this connection.</summary>
    /// <param name="lob">The large object.</param>
    /// <returns>The stream.</returns>
    public LobReadStream OpenLobReader(TurborelLargeObject lob)
    {
        Misuse.ThrowIfNull(lob);
        return new LobReadStream(this, lob);
    }

    /// <summary>Creates a large object of <paramref name="kind"/>, written a piece at a time.</summary>
    /// <param name="kind">Whether it is of characters or octets.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>Its writer.</returns>
    /// <exception cref="TurborelException">The server's; a connection failure.</exception>
    public async Task<LobWriteStream> CreateLobAsync(LargeObjectKind kind, CancellationToken cancellationToken = default)
    {
        await SendAsync(new LobRequest.Create(kind), cancellationToken).ConfigureAwait(false);
        return await ReceiveAsync(cancellationToken).ConfigureAwait(false) switch
        {
            Reply.LobLocator locator => new LobWriteStream(this, kind, locator.Locator),
            Reply.Error error => throw Failed(error.Failure),
            Reply.LobData => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent data for a Create")),
            _ => throw Lose(ConnectionFailure.Of("[DESIGN] the server sent no Lob reply")),
        };
    }

    /// <summary>Frees <paramref name="lob"/>'s locator: no longer valid, nor the value it named.</summary>
    /// <param name="lob">The large object.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE 0F001 if it is not valid; 22000 for one sent whole; a connection failure.</exception>
    public async Task FreeLobAsync(TurborelLargeObject lob, CancellationToken cancellationToken = default)
    {
        await LobCompletedAsync(new LobRequest.Free(LocatorOf(lob)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Holds <paramref name="lob"/>'s locator valid past its transaction's commit, until it is freed or a transaction is rolled back.</summary>
    /// <param name="lob">The large object.</param>
    /// <param name="cancellationToken">Cancels the wait, losing the connection.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">SQLSTATE 0F001 if it is not valid; 22000 for one sent whole; a connection failure.</exception>
    public async Task HoldLobAsync(TurborelLargeObject lob, CancellationToken cancellationToken = default)
    {
        await LobCompletedAsync(new LobRequest.Hold(LocatorOf(lob)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sends a Lob request answered by Complete.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    internal async Task LobCompletedAsync(LobRequest request, CancellationToken cancellationToken)
    {
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = await CompletionAsync(cancellationToken).ConfigureAwait(false);
    }

    // A failure of the program's own use of a value, as turborel-client's
    // misused() gives it.
    private static TurborelException Misused(string message) => new(DataSqlCode, DataSqlState, message);

    private static uint LocatorOf(TurborelLargeObject lob)
    {
        Misuse.ThrowIfNull(lob);
        return lob.IsLocator ? lob.Locator : throw Misused("[DESIGN] the large object was sent whole: it has no locator");
    }

    // The range of a large object held whole, in characters of a character one if asked.
    private static byte[] HeldRange(LargeObjectKind kind, ReadOnlyMemory<byte> bytes, ulong from, ulong count, bool characters)
    {
        static int At(ulong n) => n > int.MaxValue ? int.MaxValue : (int)n;
        if (!characters || kind == LargeObjectKind.Binary)
        {
            int start = Math.Min(At(from), bytes.Length);
            int end = (int)Math.Min((long)start + At(count), bytes.Length);
            return bytes[start..end].ToArray();
        }

        string text = Encoding.UTF8.GetString(bytes.Span);
        var taken = new StringBuilder();
        int index = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if ((ulong)index >= from && (ulong)index - from < count)
            {
                _ = taken.Append(rune.ToString());
            }

            index++;
        }

        return Encoding.UTF8.GetBytes(taken.ToString());
    }
}
