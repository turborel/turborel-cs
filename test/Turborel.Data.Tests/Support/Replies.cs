// The bodies of the server's replies, as the server writes them, for the
// scripted server's scripts (RULES.md R-TEST-5) [PROTOCOL Replies].

using System;
using System.Collections.Generic;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data.Tests.Support;

/// <summary>Reply bodies.</summary>
internal static class Replies
{
    /// <summary>A column of an integer, named.</summary>
    internal static readonly Column Integer = new("N", new ExactType(10, 0), NotNull: false);

    /// <summary>Complete's body.</summary>
    /// <param name="code">Its SQLCODE.</param>
    /// <param name="state">Its SQLSTATE.</param>
    /// <param name="active">Whether the transaction goes on.</param>
    /// <param name="tail">Writes its tail, if any.</param>
    /// <returns>The body.</returns>
    internal static byte[] Complete(int code = 0, string state = "00000", bool active = false, Action<WireWriter>? tail = null) => Wire.Written(w =>
    {
        w.WriteI32(code);
        w.WriteString(state);
        w.WriteFlag(active);
        tail?.Invoke(w);
    });

    /// <summary>Description's body.</summary>
    /// <param name="columns">The columns.</param>
    /// <returns>The body.</returns>
    internal static byte[] Description(params Column[] columns) => Wire.Written(w => Columns(w, columns));

    /// <summary>Prepared's body.</summary>
    /// <param name="statement">The statement's number.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="columns">Its columns.</param>
    /// <returns>The body.</returns>
    internal static byte[] Prepared(uint statement, IReadOnlyList<Parameter> parameters, params Column[] columns) => Wire.Written(w =>
    {
        w.WriteU32(statement);
        w.WriteU32((uint)parameters.Count);
        foreach (Parameter parameter in parameters)
        {
            parameter.Write(w);
        }

        Columns(w, columns);
    });

    /// <summary>Rows' body, of integers.</summary>
    /// <param name="values">One integer for each row.</param>
    /// <returns>The body.</returns>
    internal static byte[] Rows(params int[] values) => Wire.Written(w =>
    {
        w.WriteU32((uint)values.Length);
        foreach (int value in values)
        {
            ValueCodec.WriteList(w, [new TurborelDecimal(value, 0)]);
        }
    });

    /// <summary>Notice's body, with no diagnostics.</summary>
    /// <param name="state">Its SQLSTATE.</param>
    /// <param name="message">Its message.</param>
    /// <returns>The body.</returns>
    internal static byte[] Notice(string state, string message) => Wire.Join(Wire.Str(state), Wire.Str(message));

    /// <summary>Error's body.</summary>
    /// <param name="code">Its SQLCODE.</param>
    /// <param name="state">Its SQLSTATE.</param>
    /// <param name="active">Whether the transaction goes on.</param>
    /// <returns>The body.</returns>
    internal static byte[] Error(int code, string state, bool active = false) =>
        Wire.Written(new Failure { SqlCode = code, SqlState = state, Message = $"failed {state}", Active = active }.Write);

    private static void Columns(WireWriter w, Column[] columns)
    {
        w.WriteU32((uint)columns.Length);
        foreach (Column column in columns)
        {
            column.Write(w);
        }
    }
}
