// Large objects against a real turboreld (RULES.md R-TEST-8): one of at
// most SERVER.LOB_INLINE_BYTES whole in its row, a longer one by its
// locator, read by ranges and as a stream, freed and held; one created a
// piece at a time and sent as a parameter [CLIENT Large objects]
// [PROTOCOL Lob 'L'] [PROTOCOL Values].

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Live tests of large objects.</summary>
/// <param name="server">The live server.</param>
public sealed class LobLiveTests(LiveServer server)
{
    /// <summary>
    /// A large object of at most 64 KiB comes whole; a longer one by its
    /// locator and lengths, read by octets or characters a chunk at most, or
    /// whole as a stream, valid until its transaction ends.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Values]")]
    [Verifies("[PROTOCOL Lob 'L']")]
    [Verifies("[CLIENT Large objects]")]
    public async Task LongOnesComeByTheirLocator()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("LOBS");
        string text = string.Concat(Enumerable.Repeat("é€x", 120_000));
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, C CLOB(10M), B BLOB(10M))");
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?, ?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new TurborelDecimal(1, 0), "short", new byte[] { 1, 2 }], TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new TurborelDecimal(2, 0), text, new byte[300_000]], TestContext.Current.CancellationToken);

        IReadOnlyList<IReadOnlyList<object?>> rows = await c.RowsAsync($"SELECT C, B FROM {t} ORDER BY N");
        TurborelLargeObject small = Assert.IsType<TurborelLargeObject>(rows[0][0]);
        Assert.Equal(("short", false), (Encoding.UTF8.GetString(small.Data!.Value.Span), small.IsLocator));
        Assert.Equal("or", Encoding.UTF8.GetString(await c.ReadLobAsync(small, 2, 2, characters: true, cancellationToken: TestContext.Current.CancellationToken)));
        TurborelLargeObject clob = Assert.IsType<TurborelLargeObject>(rows[1][0]);
        Assert.True(clob.IsLocator);
        Assert.Equal(((ulong)Encoding.UTF8.GetByteCount(text), (ulong)text.Length, LargeObjectKind.Character), (clob.Octets, clob.Characters, clob.Kind));
        Assert.Equal("€xé", Encoding.UTF8.GetString(await c.ReadLobAsync(clob, 1, 3, characters: true, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Equal(Encoding.UTF8.GetBytes("é"), await c.ReadLobAsync(clob, 0, 2, characters: false, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(NativeConnection.ChunkOctets, (await c.ReadLobAsync(clob, 0, 10_000_000, characters: false, cancellationToken: TestContext.Current.CancellationToken)).Length);
        using var whole = new MemoryStream();
        await c.OpenLobReader(clob).CopyToAsync(whole, TestContext.Current.CancellationToken);
        Assert.Equal(text, Encoding.UTF8.GetString(whole.ToArray()));
        TurborelLargeObject blob = Assert.IsType<TurborelLargeObject>(rows[1][1]);
        Assert.Equal(300_000UL, blob.Octets);
        _ = await c.DoneAsync("ROLLBACK");
        Assert.Equal("0F001", (await Assert.ThrowsAsync<TurborelException>(() => c.ReadLobAsync(clob, 0, 1, characters: false, cancellationToken: TestContext.Current.CancellationToken))).SqlState);
    }

    /// <summary>A locator freed is no longer valid; one held lasts past the commit, until a rollback; one sent whole has none to free.</summary>
    [Fact]
    [Verifies("[PROTOCOL Lob 'L']")]
    [Verifies("[CLIENT Large objects]")]
    public async Task LocatorsAreFreedAndHeld()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("HELD");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, B BLOB(1M))");
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?, ?)", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [new TurborelDecimal(1, 0), new byte[100_000]], TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("COMMIT");

        var freed = (TurborelLargeObject)(await c.RowsAsync($"SELECT B FROM {t}"))[0][0]!;
        await c.FreeLobAsync(freed, TestContext.Current.CancellationToken);
        Assert.Equal("0F001", (await Assert.ThrowsAsync<TurborelException>(() => c.ReadLobAsync(freed, 0, 1, false, TestContext.Current.CancellationToken))).SqlState);
        var held = (TurborelLargeObject)(await c.RowsAsync($"SELECT B FROM {t}"))[0][0]!;
        await c.HoldLobAsync(held, TestContext.Current.CancellationToken);
        _ = await c.DoneAsync("COMMIT");
        Assert.Single(await c.ReadLobAsync(held, 0, 1, false, TestContext.Current.CancellationToken));
        _ = await c.RowsAsync("VALUES 1");
        _ = await c.DoneAsync("ROLLBACK");
        await Assert.ThrowsAsync<TurborelException>(() => c.ReadLobAsync(held, 0, 1, false, TestContext.Current.CancellationToken));
        TurborelException whole = await Assert.ThrowsAsync<TurborelException>(() => c.FreeLobAsync(TurborelLargeObject.Whole(LargeObjectKind.Binary, new byte[] { 1 }), TestContext.Current.CancellationToken));
        Assert.Equal((-301, "22000"), (whole.SqlCode, whole.SqlState));
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A large object created a piece at a time, a character split between
    /// writes, sent in chunks, passes as a parameter by its locator and reads
    /// back whole.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Lob 'L']")]
    [Verifies("[PROTOCOL Values]")]
    [Verifies("[CLIENT Large objects]")]
    public async Task CreatedPieceByPiece()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("WRITTEN");
        _ = await c.DoneAsync($"CREATE TABLE {t} (C CLOB(10M))");
        byte[] octets = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("€", 200_000)));

        LobWriteStream writer = await c.CreateLobAsync(LargeObjectKind.Character, TestContext.Current.CancellationToken);
        await writer.WriteAsync(octets.AsMemory(0, 1), TestContext.Current.CancellationToken);
        await writer.WriteAsync(octets.AsMemory(1, 300_000), TestContext.Current.CancellationToken);
        await Task.Run(() => writer.Write(octets, 300_001, octets.Length - 300_001), TestContext.Current.CancellationToken);
        TurborelLargeObject lob = await writer.FinishAsync(TestContext.Current.CancellationToken);
        Assert.Equal(((ulong)octets.Length, 200_000UL), (lob.Octets, lob.Characters));
        _ = await c.ExecuteAsync(await c.PrepareAsync($"INSERT INTO {t} VALUES (?)", TestContext.Current.CancellationToken), [lob], TestContext.Current.CancellationToken);
        var read = (TurborelLargeObject)(await c.RowsAsync($"SELECT C FROM {t}"))[0][0]!;
        using var back = new MemoryStream();
        await c.OpenLobReader(read).CopyToAsync(back, TestContext.Current.CancellationToken);
        Assert.Equal(octets, back.ToArray());
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>
    /// A character large object is checked as UTF-8 by the server as each
    /// Append comes: octets not UTF-8 are refused by their Append, 22021;
    /// one that ends within a character is refused where it is used.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Lob 'L']")]
    [Verifies("[CLIENT Large objects]")]
    public async Task ClobsAreCheckedAsUtf8()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("CHECKED_CLOB");
        _ = await c.DoneAsync($"CREATE TABLE {t} (C CLOB(1M))");
        _ = await c.DoneAsync("COMMIT");

        LobWriteStream bad = await c.CreateLobAsync(LargeObjectKind.Character, TestContext.Current.CancellationToken);
        await bad.WriteAsync(new byte[] { 0x61, 0xFF }, TestContext.Current.CancellationToken);
        Assert.Equal("22021", (await Assert.ThrowsAsync<TurborelException>(() => bad.FinishAsync(TestContext.Current.CancellationToken))).SqlState);
        _ = await c.DoneAsync("ROLLBACK");
        LobWriteStream cut = await c.CreateLobAsync(LargeObjectKind.Character, TestContext.Current.CancellationToken);
        await cut.WriteAsync(Encoding.UTF8.GetBytes("a€").AsMemory(0, 3), TestContext.Current.CancellationToken);
        TurborelLargeObject ending = await cut.FinishAsync(TestContext.Current.CancellationToken);
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (?)", TestContext.Current.CancellationToken);
        Assert.Equal("22021", (await Assert.ThrowsAsync<TurborelException>(() => c.ExecuteAsync(insert, [ending], TestContext.Current.CancellationToken))).SqlState);
        Assert.False(c.IsLost);
        _ = await c.DoneAsync("ROLLBACK");
    }

    /// <summary>A <c>?</c> in the rows of an INSERT of several takes its large object column's type, so each value is stored whole.</summary>
    [Fact]
    [Verifies("[CLIENT Large objects]")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task SeveralRowsKeepTheirLargeObjectsWhole()
    {
        server.Require();
        await using NativeConnection c = await Live.ConnectAsync();
        string t = Live.Table("ROWS_OF_LOBS");
        _ = await c.DoneAsync($"CREATE TABLE {t} (N INTEGER, B BLOB(10M), C CLOB(10M))");
        byte[] octets = [.. Enumerable.Range(0, 200_000).Select(i => (byte)i)];
        string text = new('x', 80_000);
        PreparedStatement insert = await c.PrepareAsync($"INSERT INTO {t} VALUES (1, ?, ?), (2, X'0102', 'short')", TestContext.Current.CancellationToken);
        _ = await c.ExecuteAsync(insert, [TurborelLargeObject.Whole(LargeObjectKind.Binary, octets), TurborelLargeObject.Whole(LargeObjectKind.Character, Encoding.UTF8.GetBytes(text))], TestContext.Current.CancellationToken);
        Assert.Equal([["1", "200000", "80000"], ["2", "2", "5"]], (await c.RowsAsync($"SELECT N, OCTET_LENGTH(B), CHAR_LENGTH(C) FROM {t} ORDER BY N")).Select(Live.Texts));
        _ = await c.DoneAsync("ROLLBACK");
    }
}
