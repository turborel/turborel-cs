// What the live tests share: a connection to the live server as its owner,
// a table name of each test's own, so tests run side by side, and a
// statement's completion or rows read at once (RULES.md R-TEST-8).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Tests.Server;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Live;

/// <summary>Helpers of the live tests.</summary>
internal static class Live
{
    /// <summary>Connects to the live server over TCP as its owner, by trust.</summary>
    /// <param name="options">What the connection asks for; the defaults if null.</param>
    /// <returns>The connection.</returns>
    internal static Task<NativeConnection> ConnectAsync(ConnectOptions? options = null) =>
        NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", LiveServer.Port), LiveServer.Owner, new TrustMechanism(), options);

    /// <summary>A table name no other test uses.</summary>
    /// <param name="prefix">What the name begins with.</param>
    /// <returns>The name.</returns>
    internal static string Table(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)].ToUpperInvariant();

    /// <summary>Runs a statement that is not a query.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="sql">The statement.</param>
    /// <returns>Its completion.</returns>
    internal static async Task<Completed> DoneAsync(this NativeConnection connection, string sql) =>
        Assert.IsType<DoneOutcome>(await connection.QueryAsync(sql)).Completion;

    /// <summary>Runs a query.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="sql">The query.</param>
    /// <returns>Its rows.</returns>
    internal static async Task<IReadOnlyList<IReadOnlyList<object?>>> RowsAsync(this NativeConnection connection, string sql) =>
        Assert.IsType<RowsOutcome>(await connection.QueryAsync(sql)).Rows;

    /// <summary>The text of each value of a row, null of SQL's null.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The texts.</returns>
    internal static string?[] Texts(IReadOnlyList<object?> row)
    {
        string?[] texts = new string?[row.Count];
        for (int i = 0; i < row.Count; i++)
        {
            texts[i] = row[i]?.ToString();
        }

        return texts;
    }
}
