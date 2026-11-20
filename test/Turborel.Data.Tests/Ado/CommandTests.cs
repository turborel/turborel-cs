// The standard API's commands against a server of the test's own, byte
// for byte: one of no parameters run as a Query, its rows read a batch at
// a time, a Query of several statements read by NextResult, a reader
// closed early reading what is left; one of parameters prepared, its
// cursor fetched as the reader reads, closed early, and deallocated
// (RULES.md D2, D20).

using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Ado;

/// <summary>Tests of <see cref="TurborelCommand"/> and <see cref="TurborelDataReader"/> over the wire.</summary>
public sealed class CommandTests
{
    private const string Select = "SELECT N FROM T WHERE N > ?";

    private static readonly Parameter[] One = [new(false, new ExactType(10, 0))];

    /// <summary>
    /// A command of no parameters runs as a Query, its rows read a batch at
    /// a time; a Query of several statements is read by NextResult, its rows
    /// changed summed; a reader closed early reads the rest; a failure,
    /// before or among the rows, is the statement's; while a reader is open
    /// the connection takes no other command, TB910.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D17")]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Running statements]")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task DirectStatementsAreReadAsTheyCome()
    {
        await using var server = FakeServer.Start(DirectScriptAsync);
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        Assert.Equal((ConnectionState.Closed, string.Empty, string.Empty), (connection.State, connection.Database, connection.ServerVersion));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Assert.Equal((ConnectionState.Open, $"127.0.0.1:{server.Port}"), (connection.State, connection.DataSource));

        await ReadRowsAndResultsAsync(connection);
        using var select = new TurborelCommand("SELECT N FROM T", connection);
        using var update = new TurborelCommand("UPDATE T SET N = 1", connection);
        Assert.Equal(4, await update.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        using var values = new TurborelCommand("VALUES 5", connection);
        Assert.Equal(5m, values.ExecuteScalar());
        using (TurborelDataReader early = select.ExecuteReader())
        {
            Assert.True(early.Read());
        }

        using var unknown = new TurborelCommand("SELECT X", connection);
        TurborelException before = await Assert.ThrowsAsync<TurborelException>(() => unknown.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("42703", before.SqlState);
        await using (TurborelDataReader failing = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.True(failing.Read());
            Assert.Equal("22012", Assert.Throws<TurborelException>(() => failing.Read()).SqlState);
        }

        using var set = new TurborelCommand("SET SCHEMA S", connection);
        Assert.Null(await set.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using TurborelDataReader open = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await open.ReadAsync(TestContext.Current.CancellationToken));
        await connection.CloseAsync();
        Assert.Equal((ConnectionState.Closed, true), (connection.State, open.IsClosed));
        await server.PlayedAsync();
    }

    /// <summary>
    /// A command of parameters is prepared, its values sent by their place,
    /// a query's cursor fetched as the reader reads, closed if the reader
    /// closes early, and deallocated; one prepared by Prepare is executed
    /// again with no Prepare; one failing to prepare sends nothing more; a
    /// cursor failing is deallocated still.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D2")]
    [Verifies("[DESIGN] D18")]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Prepared statements]")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task ParametersArePreparedAndTheirCursorFetched()
    {
        await using var server = FakeServer.Start(PreparedScriptAsync);
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using TurborelCommand select = connection.CreateCommand();
        select.CommandText = Select;
        _ = select.Parameters.AddWithValue("low", 4);
        await using (TurborelDataReader rows = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            var read = new List<decimal>();
            while (await rows.ReadAsync(TestContext.Current.CancellationToken))
            {
                read.Add(rows.GetDecimal(0));
            }

            Assert.Equal([5m, 6m, 7m], read);
        }

        await using (TurborelDataReader early = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.True(await early.ReadAsync(TestContext.Current.CancellationToken));
        }

        using var insert = new TurborelCommand("INSERT INTO T VALUES (?)", connection);
        _ = insert.Parameters.Add(new TurborelParameter(null, 8L));
        Assert.Equal(1, await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        using var prepared = new TurborelCommand("VALUES 1", connection);
        await prepared.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal((1m, 1m), (await prepared.ExecuteScalarAsync(TestContext.Current.CancellationToken), prepared.ExecuteScalar()));
        using var bad = new TurborelCommand("SELECT ? FROM NOWHERE", connection);
        _ = bad.Parameters.AddWithValue("x", 1);
        Assert.Equal("42703", (await Assert.ThrowsAsync<TurborelException>(() => bad.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);
        using var failing = new TurborelCommand("SELECT 1 / ?", connection);
        _ = failing.Parameters.AddWithValue("x", 0);
        Assert.Equal("22012", (await Assert.ThrowsAsync<TurborelException>(() => failing.ExecuteScalarAsync(TestContext.Current.CancellationToken))).SqlState);
        connection.Close();
        await server.PlayedAsync();
    }

    /// <summary>
    /// A statement prepared by Prepare, let go of as its text changes, is
    /// deallocated as the command next runs; one whose session was renewed
    /// is prepared again, its old number gone with that session.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[DESIGN] D20")]
    [Verifies("[CLIENT Prepared statements]")]
    public async Task PreparedStatementsFollowTheirSession()
    {
        await using var server = FakeServer.Start(async peer =>
        {
            await peer.LogInAsync(Capabilities.FetchEnds, Capabilities.Reset);
            Assert.Equal(Wire.Encode(new Request.Prepare(0, "VALUES ?"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
            await peer.WriteAsync('P', Replies.Prepared(0, One, Replies.Integer));
            await CursorOfOneAsync(peer, 0);
            Assert.Equal(Wire.Encode(new Request.Deallocate(0))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Query("VALUES 2", Ends: true, []))[5..], await peer.ExpectAsync(RequestKinds.Query));
            await peer.WriteAsync('D', Replies.Description(Replies.Integer));
            await peer.WriteAsync('B', Replies.Rows(2));
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Prepare(1, "VALUES ?"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
            await peer.WriteAsync('P', Replies.Prepared(1, One, Replies.Integer));
            _ = await peer.ExpectAsync(RequestKinds.Reset);
            await peer.WriteAsync('K', Replies.Complete());
            Assert.Equal(Wire.Encode(new Request.Prepare(2, "VALUES ?"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
            await peer.WriteAsync('P', Replies.Prepared(2, One, Replies.Integer));
            await CursorOfOneAsync(peer, 2);
            _ = await peer.ExpectAsync(RequestKinds.Terminate);
        });
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        using var command = new TurborelCommand("VALUES ?", connection);
        _ = command.Parameters.AddWithValue("n", 1);
        await command.PrepareAsync(TestContext.Current.CancellationToken);
        await command.PrepareAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1m, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "VALUES 2";
        command.Parameters.Clear();
        Assert.Equal(2m, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "VALUES ?";
        _ = command.Parameters.AddWithValue("n", 1);
        await command.PrepareAsync(TestContext.Current.CancellationToken);
        await connection.Native!.ResetAsync(TestContext.Current.CancellationToken);
        connection.Native.Autocommit = true;
        Assert.Equal(1m, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await connection.CloseAsync();
        await server.PlayedAsync();
    }

    // A cursor of statement number's one row, under fetch-ends.
    private static async Task CursorOfOneAsync(FakeServer.Peer peer, uint number)
    {
        Assert.Equal(Wire.Encode(new Request.Execute(new(number), [new TurborelDecimal(1, 0)], 1000, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('K', Replies.Complete());
    }

    // A Query's body, of autocommit and no attributes, its statements several.
    private static byte[] Query(string sql) => Wire.Encode(new Request.Query(sql, Ends: true, [], Multi: true))[5..];


    // A query's rows read as they come, while the connection takes no other
    // command; a Query of two statements read by NextResult.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    private static async Task ReadRowsAndResultsAsync(TurborelConnection connection)
    {
        using var select = new TurborelCommand("SELECT N FROM T", connection);
        using var other = new TurborelCommand("SELECT 1", connection);
        await using (TurborelDataReader rows = await select.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal((1, "N", true), (rows.FieldCount, rows.GetName(0), rows.HasRows));
            Assert.Equal("TB910", Assert.Throws<TurborelException>(() => other.ExecuteNonQuery()).SqlState);
            var read = new List<int>();
            while (await rows.ReadAsync(TestContext.Current.CancellationToken))
            {
                read.Add(rows.GetInt32(0));
            }

            Assert.Equal([1, 2, 3], read);
            Assert.Equal((true, -1, false), (rows.HasRows, rows.RecordsAffected, await rows.NextResultAsync(TestContext.Current.CancellationToken)));
            Assert.False(rows.HasRows);
        }

        using var both = new TurborelCommand("UPDATE T SET N = 0; SELECT N FROM T", connection);
        await using (TurborelDataReader several = await both.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal((0, false), (several.FieldCount, await several.ReadAsync(TestContext.Current.CancellationToken)));
            Assert.True(await several.NextResultAsync(TestContext.Current.CancellationToken));
            Assert.True(await several.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal((9L, 3), (several.GetInt64(0), several.RecordsAffected));
            Assert.False(several.NextResult());
        }
    }

    // The server's side of the test.
    private static async Task DirectScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync(Capabilities.FetchEnds, Capabilities.MultiStatement);
        Assert.Equal(Query("SELECT N FROM T"), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(1, 2));
        await peer.WriteAsync('B', Replies.Rows(3));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Query("UPDATE T SET N = 0; SELECT N FROM T"), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.Complete(tail: w => { w.WriteU8(TailTags.More); w.WriteU8(TailTags.Rows); w.WriteU64(3); }));
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(9));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Query("UPDATE T SET N = 1"), await peer.ExpectAsync(RequestKinds.Query));
        await peer.WriteAsync('K', Replies.CompleteRows(4));
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(5));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('B', Replies.Rows(2));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('E', Replies.Error(-206, "42703"));
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('E', Replies.Error(-301, "22012"));
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Query);
        await peer.WriteAsync('D', Replies.Description(Replies.Integer));
        await peer.WriteAsync('B', Replies.Rows(1));
        await peer.WriteAsync('E', Replies.Error(-301, "22012"));
        _ = await peer.ExpectAsync(RequestKinds.Terminate);
    }

    // The server's side of the test.
    private static async Task PreparedScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync(Capabilities.FetchEnds);
        Assert.Equal(Wire.Encode(new Request.Prepare(0, Select))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
        await peer.WriteAsync('P', Replies.Prepared(0, One, Replies.Integer));
        Assert.Equal(Wire.Encode(new Request.Execute(new(0), [new TurborelDecimal(4, 0)], 1000, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('B', Replies.Rows(5, 6));
        await peer.WriteAsync('S', []);
        Assert.Equal(Wire.Encode(new Request.Fetch(new(0), 1000))[5..], await peer.ExpectAsync(RequestKinds.Fetch));
        await peer.WriteAsync('B', Replies.Rows(7));
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Wire.Encode(new Request.Deallocate(0))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Prepare);
        await peer.WriteAsync('P', Replies.Prepared(1, One, Replies.Integer));
        _ = await peer.ExpectAsync(RequestKinds.Execute);
        await peer.WriteAsync('B', Replies.Rows(5));
        await peer.WriteAsync('S', []);
        Assert.Equal(Wire.Encode(new Request.Close(new(1)))[5..], await peer.ExpectAsync(RequestKinds.Close));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Deallocate);
        await peer.WriteAsync('K', Replies.Complete());
        Assert.Equal(Wire.Encode(new Request.Prepare(2, "INSERT INTO T VALUES (?)"))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
        await peer.WriteAsync('P', Replies.Prepared(2, One));
        Assert.Equal(Wire.Encode(new Request.Execute(new(2), [new TurborelDecimal(8, 0)], 0, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('K', Replies.CompleteRows(1));
        Assert.Equal(Wire.Encode(new Request.Deallocate(2))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Prepare);
        await peer.WriteAsync('P', Replies.Prepared(3, [], Replies.Integer));
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(Wire.Encode(new Request.Execute(new(3), [], 1000, true, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
            await peer.WriteAsync('B', Replies.Rows(1));
            await peer.WriteAsync('K', Replies.Complete());
        }

        _ = await peer.ExpectAsync(RequestKinds.Prepare);
        await peer.WriteAsync('E', Replies.Error(-206, "42703"));
        _ = await peer.ExpectAsync(RequestKinds.Prepare);
        await peer.WriteAsync('P', Replies.Prepared(5, One, Replies.Integer));
        _ = await peer.ExpectAsync(RequestKinds.Execute);
        await peer.WriteAsync('E', Replies.Error(-301, "22012"));
        Assert.Equal(Wire.Encode(new Request.Deallocate(5))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
        await peer.WriteAsync('K', Replies.Complete());
        _ = await peer.ExpectAsync(RequestKinds.Terminate);
    }
}
