// A CALL's OUT and INOUT parameters through the standard API, against a
// server of the test's own granting parameter-modes [PROTOCOL Parameter
// modes] [CLIENT Prepared statements]: DeriveParameters gives each ? its
// direction, an execution sends the IN and INOUT values alone and sets
// the OUT and INOUT ones from the CALL's row, a direction not its ?'s mode
// is refused, TB910, and a batch sets them as a command does (RULES.md D2,
// D22).

using System;
using System.Data;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Ado;

/// <summary>Tests of a CALL's output parameters over the wire.</summary>
public sealed class OutputParameterTests
{
    private const string Call = "CALL P(?, ?, ?)";

    private static readonly Parameter Integer = new(false, new ExactType(10, 0));

    private static readonly Called OfP = new(
        new SqlName("S", "P"),
        [new DynamicParameter(ParameterMode.In, 1, Integer), new DynamicParameter(ParameterMode.Out, 2, Integer), new DynamicParameter(ParameterMode.InOut, 3, Integer)]);

    private static readonly Column[] Outputs = [new("B", new ExactType(10, 0), false), new("C", new ExactType(10, 0), false)];

    /// <summary>
    /// DeriveParameters makes one parameter for each ?, Input, Output and
    /// InputOutput; executed, the CALL takes the IN and INOUT values and
    /// sets the OUT and INOUT ones, a null as DBNull, its row read too; a
    /// direction not its ?'s mode is refused, TB910; a batch sets them.
    /// </summary>
    /// <returns>The task.</returns>
    [Fact]
    [Verifies("[PROTOCOL Parameter modes]")]
    [Verifies("[CLIENT Prepared statements]")]
    [Verifies("[DESIGN] D22")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1849:Call async methods when in an async method",
        Justification = "The standard API's synchronous members are tested beside its async ones (RULES.md D15).")]
    public async Task OutputParametersAreSetFromTheCallsRow()
    {
        await using var server = FakeServer.Start(ScriptAsync);
        await using var connection = new TurborelConnection(AdoServer.Of(server.Port));
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        using var call = new TurborelCommand(Call, connection);
        await call.DeriveParametersAsync(TestContext.Current.CancellationToken);
        Assert.Equal([ParameterDirection.Input, ParameterDirection.Output, ParameterDirection.InputOutput], [call.Parameters[0].Direction, call.Parameters[1].Direction, call.Parameters[2].Direction]);
        (call.Parameters[0].Value, call.Parameters[2].Value) = (5, 7);
        Assert.Equal(-1, await call.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal((6m, DBNull.Value), (call.Parameters[1].Value, call.Parameters[2].Value));

        call.Parameters[1].Direction = ParameterDirection.Input;
        Assert.Equal("TB910", (await Assert.ThrowsAsync<TurborelException>(() => call.ExecuteNonQueryAsync(TestContext.Current.CancellationToken))).SqlState);

        call.Prepare();
        call.DeriveParameters();
        (call.Parameters[0].Value, call.Parameters[2].Value) = (5, 7);
        await using (TurborelDataReader row = await call.ExecuteReaderAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal((9m, 8m), (call.Parameters[1].Value, call.Parameters[2].Value));
            Assert.True(await row.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal((9, 8), (row.GetInt32(0), row.GetInt32(1)));
        }

        using TurborelBatch batch = connection.CreateBatch();
        var command = new TurborelBatchCommand(Call);
        _ = command.Parameters.AddWithValue("a", 5);
        _ = command.Parameters.Add(new TurborelParameter { Direction = ParameterDirection.Output });
        _ = command.Parameters.Add(new TurborelParameter("c", 7) { Direction = ParameterDirection.InputOutput });
        batch.BatchCommands.Add(command);
        _ = await batch.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        Assert.Equal((4m, 3m), (command.Parameters[1].Value, command.Parameters[2].Value));
        await connection.CloseAsync();
        await server.PlayedAsync();
    }

    // Rows' body of one row of two values.
    private static byte[] Row(int? b, int? c) => Wire.Written(w =>
    {
        w.WriteU32(1);
        ValueCodec.WriteList(w, [b is int x ? new TurborelDecimal(x, 0) : null, c is int y ? new TurborelDecimal(y, 0) : null]);
    });

    // Prepare of statement number, answered of P.
    private static async Task PreparedAsync(FakeServer.Peer peer, uint number)
    {
        Assert.Equal(Wire.Encode(new Request.Prepare(number, Call))[5..], await peer.ExpectAsync(RequestKinds.Prepare));
        await peer.WriteAsync('P', Replies.PreparedCall(number, [Integer, Integer], OfP, Outputs));
    }

    // Deallocate of statement number.
    private static async Task DeallocatedAsync(FakeServer.Peer peer, uint number)
    {
        Assert.Equal(Wire.Encode(new Request.Deallocate(number))[5..], await peer.ExpectAsync(RequestKinds.Deallocate));
        await peer.WriteAsync('K', Replies.Complete());
    }

    // An Execute of statement number with 5 and 7, its row answered.
    private static async Task CalledAsync(FakeServer.Peer peer, uint number, uint rows, bool ends, byte[] row)
    {
        Assert.Equal(Wire.Encode(new Request.Execute(new(number), [new TurborelDecimal(5, 0), new TurborelDecimal(7, 0)], rows, ends, []))[5..], await peer.ExpectAsync(RequestKinds.Execute));
        await peer.WriteAsync('B', row);
        await peer.WriteAsync('K', Replies.Complete());
    }

    // The server's side of the test.
    private static async Task ScriptAsync(FakeServer.Peer peer)
    {
        await peer.LogInAsync(Capabilities.FetchEnds, Capabilities.ParameterModes);
        await PreparedAsync(peer, 0);
        await DeallocatedAsync(peer, 0);
        await PreparedAsync(peer, 1);
        await CalledAsync(peer, 1, 1000, ends: true, Row(6, null));
        await DeallocatedAsync(peer, 1);
        await PreparedAsync(peer, 2);
        await DeallocatedAsync(peer, 2);
        await PreparedAsync(peer, 3);
        await CalledAsync(peer, 3, 1000, ends: true, Row(9, 8));
        await PreparedAsync(peer, 4);
        await CalledAsync(peer, 4, 0, ends: false, Row(4, 3));
        while ((await peer.ReadAsync()).Kind != RequestKinds.Terminate)
        {
            await peer.WriteAsync('K', Replies.Complete());
        }
    }
}
