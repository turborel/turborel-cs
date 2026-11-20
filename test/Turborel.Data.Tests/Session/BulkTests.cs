// Copies, large objects, exports and native rows against a server of the
// test's own, byte for byte: what each sends, how each reply is read, and a
// reply out of place losing the connection [CLIENT Copying and exporting]
// [CLIENT Large objects] [PROTOCOL Lob 'L'] [PROTOCOL CopyEnd 'N'].

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Transport;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Session;

/// <summary>Tests of the bulk and large object methods of <see cref="NativeConnection"/>.</summary>
public sealed class BulkTests
{
    private static readonly ConnectOptions Quick = new() { Timeouts = new Timeouts(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)) };

    /// <summary>
    /// A copy sends CopyIn, its rows in CopyData of about a chunk each, and
    /// CopyEnd, its Complete telling the rows; abandoned, or disposed
    /// unfinished, it sends CopyEnd abandoning; without <c>copy</c> nothing is sent.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL CopyIn 'I']")]
    [Verifies("[PROTOCOL CopyEnd 'N']")]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task CopySendsItsRowsAndEnds()
    {
        string big = new('x', 200_000);
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.Copy);
            Assert.Equal(Wire.Encode(new Request.CopyIn("T", ["A"], Ends: true))[5..], await peer.ExpectAsync(RequestKinds.CopyIn));
            Assert.Equal(2, CountOf(await peer.ExpectAsync(RequestKinds.CopyData)));
            Assert.Equal(1, CountOf(await peer.ExpectAsync(RequestKinds.CopyData)));
            Assert.Equal([0], await peer.ExpectAsync(RequestKinds.CopyEnd));
            await peer.WriteAsync('K', Replies.CompleteRows(3));
            _ = await peer.ExpectAsync(RequestKinds.CopyIn);
            Assert.Equal([1], await peer.ExpectAsync(RequestKinds.CopyEnd));
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.CopyIn);
            Assert.Equal([1], await peer.ExpectAsync(RequestKinds.CopyEnd));
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);
        c.Autocommit = true;

        Assert.Equal(3UL, await c.CopyRowsAsync("T", ["A"], [[big], [big], ["small"]], TestContext.Current.CancellationToken));
        Copying abandoned = await c.CopyInAsync("T", [], TestContext.Current.CancellationToken);
        TurborelException busy = await Assert.ThrowsAsync<TurborelException>(() => c.QueryAsync("SELECT 1", TestContext.Current.CancellationToken));
        Assert.Equal(("TB910", false), (busy.SqlState, c.IsLost));
        await abandoned.AbandonAsync(TestContext.Current.CancellationToken);
        await abandoned.DisposeAsync();
        await (await c.CopyInAsync("T", [], TestContext.Current.CancellationToken)).DisposeAsync();
        await server.PlayedAsync();
    }

    /// <summary>A method of copy is refused before anything is sent where the server did not grant it.</summary>
    [Fact]
    [Verifies("[PROTOCOL Capabilities]")]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task CopyNeedsItsCapability()
    {
        await using var server = FakeServer.Start(peer => peer.LogInAsync());
        await using NativeConnection c = await ConnectAsync(server);

        Assert.Contains("did not grant copy", (await Assert.ThrowsAsync<TurborelException>(() => c.CopyInAsync("T", [], TestContext.Current.CancellationToken))).Message, StringComparison.Ordinal);
        Assert.False(c.IsLost);
    }

    /// <summary>
    /// A large object is read by Lob requests, by octets or characters, as a
    /// stream a chunk at a time; created, written in Appends of a chunk at
    /// most, a character never split, freed and held; one held whole is read
    /// from the value.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Lob 'L']")]
    [Verifies("[CLIENT Large objects]")]
    public async Task LargeObjectsAreReadAndWrittenByLob()
    {
        byte[] text = Encoding.UTF8.GetBytes(new string('€', 100_000));
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            Assert.Equal(Wire.Encode(new LobRequest.Read(5, 2, 3, true))[5..], await peer.ExpectAsync(RequestKinds.Lob));
            await peer.WriteAsync('L', Replies.LobData("abc"u8.ToArray()));
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await peer.WriteAsync('L', Replies.LobData([1, 2, 3]));
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await peer.WriteAsync('L', Replies.LobData([4]));
            Assert.Equal(Wire.Encode(new LobRequest.Create(LargeObjectKind.Character))[5..], await peer.ExpectAsync(RequestKinds.Lob));
            await peer.WriteAsync('L', Replies.LobLocator(9));
            byte[] first = await peer.ExpectAsync(RequestKinds.Lob);
            Assert.Equal(0, (first.Length - 9) % 3);
            await peer.WriteAsync('K', Replies.Complete());
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new LobRequest.Free(9))[5..], await peer.ExpectAsync(RequestKinds.Lob));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new LobRequest.Hold(9))[5..], await peer.ExpectAsync(RequestKinds.Lob));
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);
        var located = TurborelLargeObject.ByLocator(LargeObjectKind.Binary, 5, (4, 0));

        Assert.Equal("abc"u8.ToArray(), await c.ReadLobAsync(located, 2, 3, characters: true, cancellationToken: TestContext.Current.CancellationToken));
        using var copied = new MemoryStream();
        await using (LobReadStream reader = c.OpenLobReader(located))
        {
            Assert.Equal((4, true, false, false), (reader.Length, reader.CanRead, reader.CanSeek, reader.CanWrite));
            await reader.CopyToAsync(copied, TestContext.Current.CancellationToken);
            Assert.Equal(4, reader.Position);
        }

        Assert.Equal([1, 2, 3, 4], copied.ToArray());
        LobWriteStream writer = await c.CreateLobAsync(LargeObjectKind.Character, TestContext.Current.CancellationToken);
        await writer.WriteAsync(text, TestContext.Current.CancellationToken);
        TurborelLargeObject made = await writer.FinishAsync(TestContext.Current.CancellationToken);
        Assert.Equal(((ulong)text.Length, 100_000UL, (long)text.Length), (made.Octets, made.Characters, writer.Position));
        await c.FreeLobAsync(made, TestContext.Current.CancellationToken);
        await c.HoldLobAsync(made, TestContext.Current.CancellationToken);
        var held = TurborelLargeObject.Whole(LargeObjectKind.Character, Encoding.UTF8.GetBytes("aé€b"));
        Assert.Equal(Encoding.UTF8.GetBytes("é€"), await c.ReadLobAsync(held, 1, 2, characters: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(Encoding.UTF8.GetBytes("a"), await c.ReadLobAsync(held, 0, 1, characters: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(await c.ReadLobAsync(held, 99, 1, characters: false, cancellationToken: TestContext.Current.CancellationToken));
        await server.PlayedAsync();
    }

    /// <summary>A large object's streams refuse what they do not do, as the API's misuse; a reply out of place loses the connection.</summary>
    /// <param name="reply">The reply the server gives.</param>
    /// <param name="wanted">What the failure says.</param>
    [Theory]
    [Verifies("[PROTOCOL Lob 'L']")]
    [InlineData("locator", "a locator for a Read")]
    [InlineData("pong", "no Lob reply")]
    [InlineData("empty", "less of the large object")]
    public async Task LobRepliesOutOfPlace(string reply, string wanted)
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            _ = await peer.ExpectAsync(RequestKinds.Lob);
            await (reply switch
            {
                "locator" => peer.WriteAsync('L', Replies.LobLocator(1)),
                "pong" => peer.WriteAsync('G', []),
                _ => peer.WriteAsync('L', Replies.LobData([])),
            });
        });
        await using NativeConnection c = await ConnectAsync(server);
        var lob = TurborelLargeObject.ByLocator(LargeObjectKind.Binary, 1, (10, 0));
        LobReadStream reader = c.OpenLobReader(lob);

        Assert.Equal("TB910", Assert.Throws<TurborelException>(() => reader.Seek(0, SeekOrigin.Begin)).SqlState);
        Assert.Throws<TurborelException>(() => reader.Position = 1);
        Assert.Throws<TurborelException>(() => reader.SetLength(1));
        Assert.Throws<TurborelException>(() => reader.Write([1], 0, 1));
        await reader.FlushAsync(TestContext.Current.CancellationToken);
        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(() => Task.Run(() => reader.Read(new byte[4], 0, 4)));
        Assert.Contains(wanted, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A query exports as CSV, a header then a record for each row, or as
    /// native rows, which read back; a statement that is no query, and a
    /// write that fails, fail the export.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Copying and exporting]")]
    public async Task ExportWritesAsTheRowsCome()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync();
            for (int i = 0; i < 3; i++)
            {
                _ = await peer.ExpectAsync(RequestKinds.Query);
                await peer.WriteAsync('D', Replies.Description(Replies.Integer, new Column(null, new CharacterType(9, CharacterUnits.Characters, true), false)));
                await peer.WriteAsync('B', Wire.Written(w =>
                {
                    w.WriteU32(2);
                    ValueCodec.WriteList(w, [new TurborelDecimal(1, 0), " pad"]);
                    ValueCodec.WriteList(w, [null, string.Empty]);
                }));
                await peer.WriteAsync('K', Replies.Complete());
            }

            _ = await peer.ExpectAsync(RequestKinds.Query);
            await peer.WriteAsync('K', Replies.Complete());
        });
        await using NativeConnection c = await ConnectAsync(server);

        using var csv = new MemoryStream();
        Assert.Equal(2UL, await c.ExportAsync("Q", csv, ExportFormat.Csv, TestContext.Current.CancellationToken));
        Assert.Equal("N,\"\"\r\n1,\" pad\"\r\n,\"\"\r\n", Encoding.UTF8.GetString(csv.ToArray()));
        using var native = new MemoryStream();
        _ = await c.ExportAsync("Q", native, ExportFormat.Native, TestContext.Current.CancellationToken);
        native.Position = 0;
        (IReadOnlyList<Column> columns, IReadOnlyList<IReadOnlyList<object?>> rows) = await NativeRows.ReadAsync(native, TestContext.Current.CancellationToken);
        Assert.Equal((2, 2, " pad"), (columns.Count, rows.Count, rows[0][1]));
        TurborelException write = await Assert.ThrowsAsync<TurborelException>(() => c.ExportAsync("Q", new Broken(), ExportFormat.Csv, TestContext.Current.CancellationToken));
        Assert.Equal(("08006", false), (write.SqlState, c.IsLost));
        Assert.Contains("cannot write the export", write.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<TurborelException>(() => c.ExportAsync("COMMIT", csv, ExportFormat.Csv, TestContext.Current.CancellationToken));
        await server.PlayedAsync();
    }

    /// <summary>Input that is not native rows, or is cut short, is refused, SQLSTATE 08006.</summary>
    /// <param name="hex">The input.</param>
    /// <param name="why">What the failure says.</param>
    [Theory]
    [Verifies("[CLIENT Copying and exporting]")]
    [InlineData("", "not native rows")]
    [InlineData("4B 0E000000 00000000 05000000 3030303030 00", "no Description first")]
    [InlineData("44 04000000 00000000 47 00000000", "a frame other than Rows")]
    public async Task NativeRowsAreChecked(string hex, string why)
    {
        TurborelException failure = await Assert.ThrowsAsync<TurborelException>(() => NativeRows.ReadAsync(new MemoryStream(Wire.Hex(hex)), TestContext.Current.CancellationToken));
        Assert.Equal("08006", failure.SqlState);
        Assert.Contains(why, failure.Message, StringComparison.Ordinal);
    }

    private static int CountOf(byte[] copyData) => BitConverter.ToInt32(copyData, 0);

    private static Task<NativeConnection> ConnectAsync(FakeServer server) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", server.Port), "ann", new TrustMechanism(), Quick);

    // A stream whose writes fail.
    private sealed class Broken : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("the disk is full");
    }
}
