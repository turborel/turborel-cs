// A Prepared's tail 20, granted parameter-modes, of a CALL of an
// SQL-invoked procedure [PROTOCOL Parameter modes]: the procedure's
// specific name, then each ? in the order written with its parameter's
// mode, ordinal position and type; a mode of no code, a position of 0, or
// the tag out of its place fails the connection.

using System.Linq;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Tests.Support;
using Turborel.Data.Types;
using Xunit;

namespace Turborel.Data.Tests.Protocol;

/// <summary>Tests of <see cref="Called"/> in a Prepared.</summary>
public sealed class CalledTests
{
    private static readonly Parameter Integer = new(false, new ExactType(10, 0));

    private static readonly Called OfP = new(
        new SqlName("S", "P"),
        [new DynamicParameter(ParameterMode.In, 1, Integer), new DynamicParameter(ParameterMode.In, 1, Integer), new DynamicParameter(ParameterMode.Out, 2, Integer), new DynamicParameter(ParameterMode.InOut, 3, Integer)]);

    /// <summary>
    /// Each ? in written order, OUT ones too, with its mode's code, its
    /// parameter's position from 1 and its type; two ? of one IN argument
    /// each that parameter's; after tag 19, the columns' declared types; a
    /// Prepared told none has no procedure.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Parameter modes]")]
    [Verifies("[PROTOCOL Prepared 'P']")]
    public void EachMarkHasItsParametersModeAndPosition()
    {
        Column declared = Replies.Integer with { Declared = new DeclaredType("INTEGER", "INTEGER") };
        Reply.Prepared prepared = Assert.IsType<Reply.Prepared>(Wire.Decode('P', Replies.PreparedCall(4, [Integer, Integer, Integer], OfP, declared, declared)));
        Assert.Equal((4U, 3), (prepared.Statement, prepared.Parameters.Count));
        Assert.Equal(["INTEGER", "INTEGER"], prepared.Columns.Select(c => c.Declared?.Name));
        Assert.Equal(new SqlName("S", "P"), prepared.Called?.Procedure);
        Assert.Equal(OfP.Parameters, prepared.Called?.Parameters ?? []);
        Assert.Equal([(byte)1, (byte)2, (byte)4], new[] { ParameterMode.In, ParameterMode.InOut, ParameterMode.Out }.Select(m => (byte)m));
        Assert.Equal(
            Wire.Hex("14 01000000 53 01000000 50 01000000 04 02000000 00 02 0A000000 00000000"),
            Wire.Written(new Called(new SqlName("S", "P"), [new DynamicParameter(ParameterMode.Out, 2, Integer)]).Write));
        Assert.Null(Assert.IsType<Reply.Prepared>(Wire.Decode('P', Replies.Prepared(5, [Integer], Replies.Integer))).Called);
    }

    /// <summary>
    /// A statement told its modes gives each ? with them, its inputs those
    /// an execution takes, and its procedure; told none, each input is IN
    /// of no position, as turborel-client's Statement gives them.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Parameter modes]")]
    [Verifies("[CLIENT Prepared statements]")]
    public void AStatementGivesItsModesOrItsInputs()
    {
        var call = new PreparedStatement(0, [Integer, Integer, Integer], [Replies.Integer, Replies.Integer], OfP);
        Assert.Same(OfP.Parameters, call.Parameters);
        Assert.Equal((3, new SqlName("S", "P")), (call.Inputs.Count, call.Procedure));
        Assert.Equal([true, true, false, true], call.Parameters.Select(p => p.TakesValue));
        Assert.Equal([false, false, true, true], call.Parameters.Select(p => p.GivesValue));
        var select = new PreparedStatement(1, [Integer], [Replies.Integer]);
        Assert.Equal([new DynamicParameter(ParameterMode.In, null, Integer)], select.Parameters);
        Assert.Null(select.Procedure);
    }

    /// <summary>A mode of no code of Table 33, a position of 0, tag 20 before tag 19, or tag 20 of a Description fails the connection.</summary>
    /// <param name="mode">The mode's code.</param>
    /// <param name="ordinal">The position.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(8, 1)]
    [InlineData(1, 0)]
    [Verifies("[PROTOCOL Parameter modes]")]
    public void AModeOfNoCodeOrAPositionOfZeroFailsTheConnection(byte mode, uint ordinal)
    {
        byte[] body = Wire.Join(Wire.Hex("00000000 00000000 00000000 14 01000000 53 01000000 50 01000000"), [mode], System.BitConverter.GetBytes(ordinal), Wire.Hex("00 02 0A000000 00000000"));
        _ = Wire.Fails(() => Wire.Decode('P', body), Sections.ParameterModes);
    }

    /// <summary>Tag 20 is a Prepared's alone, and after tag 19.</summary>
    [Fact]
    [Verifies("[PROTOCOL Parameter modes]")]
    [Verifies("[PROTOCOL The tail]")]
    public void TheTagIsAPreparedsAfterTag19()
    {
        byte[] called = Wire.Hex("14 01000000 53 01000000 50 00000000");
        byte[] declared = Wire.Hex("13 00000000");
        Assert.Equal(0, Assert.IsType<Reply.Prepared>(Wire.Decode('P', Wire.Join(Wire.Hex("00000000 00000000 00000000"), declared, called))).Called?.Parameters.Count);
        _ = Wire.Fails(() => Wire.Decode('P', Wire.Join(Wire.Hex("00000000 00000000 00000000"), called, declared)), Sections.Tail);
        _ = Wire.Fails(() => Wire.Decode('D', Wire.Join(Wire.Hex("00000000"), called)), Sections.Tail);
    }
}
