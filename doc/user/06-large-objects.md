# Large Objects

[Contents](README.md)

A CLOB or BLOB value is a `TurborelLargeObject`. One of at most
SERVER.LOB_INLINE_BYTES, 64 KiB unless the server's operator says, comes
whole in its row, its octets in `Data`. A longer one comes by its
locator, `IsLocator` true, with its lengths, `Octets` and, of a CLOB,
`Characters`; the program reads it over the connection, by ranges or as
a stream. A locator is valid until the transaction it was given in ends,
so a result that sent one keeps its transaction open, under autocommit
too.

```csharp
using System.Text;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE TABLE MANUALS (ID INTEGER, BODY CLOB(10M))");

LobWriteStream writer = await connection.CreateLobAsync(LargeObjectKind.Character);
for (int chapter = 1; chapter <= 3000; chapter++)
{
    await writer.WriteAsync(Encoding.UTF8.GetBytes($"Chapter {chapter}: the café's menu. "));
}

TurborelLargeObject written = await writer.FinishAsync();
PreparedStatement insert = await connection.PrepareAsync("INSERT INTO MANUALS VALUES (?, ?)");
await connection.ExecuteAsync(insert, [new TurborelDecimal(1, 0), written]);
await connection.ExecuteAsync(insert, [new TurborelDecimal(2, 0), "a short one"]);

var rows = (RowsOutcome)await connection.QueryAsync("SELECT ID, BODY FROM MANUALS ORDER BY ID");
foreach (IReadOnlyList<object?> row in rows.Rows)
{
    var body = (TurborelLargeObject)row[1]!;
    Console.WriteLine(body.IsLocator
        ? $"{row[0]}: by its locator, {body.Characters} characters in {body.Octets} octets"
        : $"{row[0]}: whole, {SqlLength(body)} octets");
}

var manual = (TurborelLargeObject)rows.Rows[0][1]!;
Console.WriteLine(Encoding.UTF8.GetString(await connection.ReadLobAsync(manual, 11, 21, characters: true)));
using var reader = new StreamReader(connection.OpenLobReader(manual));
string all = await reader.ReadToEndAsync();
Console.WriteLine($"read whole: {all.Length} characters, ending '{all[^20..]}'");
await connection.QueryAsync("COMMIT");

static int SqlLength(TurborelLargeObject lob) => lob.Data!.Value.Length;
```

```
1: by its locator, 91893 characters in 94893 octets
2: whole, 11 octets
the café's menu. Chap
read whole: 91893 characters, ending '0: the café's menu. '
```

`CreateLobAsync` makes a large object written a piece at a time through
a `LobWriteStream`, sent to the server a chunk at a time; a character
split between writes is sent whole. `FinishAsync` gives the value, to
pass as a parameter by its locator. A CLOB's octets are UTF-8.

`ReadLobAsync` reads a range, by octets or, of a CLOB, by characters, at
most 256 KiB a request; `OpenLobReader` gives the whole value as a
stream, read a chunk at a time. `FreeLobAsync` frees a locator, and
`HoldLobAsync` keeps one valid past its transaction's commit, until it is
freed or a rollback ends it. A large object that came whole has no
locator to free or hold: SQLSTATE 22000.

## See also

[Large Objects](../client/24-large-objects.md) in the API Reference.
