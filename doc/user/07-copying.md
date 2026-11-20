# Copying, Importing and Exporting

[Contents](README.md)

## Copying

A copy sends rows into a table with no round trip but its end.
`CopyRowsAsync` copies the rows it is given into the columns named, or
all of the table's in order, and gives the rows inserted; `CopyInAsync`
begins a copy the program sends rows to one at a time, many to a
message, and finishes or abandons. A copy is all or nothing: a row
refused undoes every row, the failure's detail naming the row, as `row 2
of the copy`. Under `Autocommit`, a copy is its own transaction. A copy
disposed before it finishes is abandoned.

## Importing

`ImportAsync` reads a source's records and inserts them into a table, a
batch at a time, 1000 rows unless told, each batch committed; 0 commits
them all at once. A `DelimitedSource` reads CSV, or tab-separated text,
as its `DelimitedDialect` says: a header naming the columns, a delimiter,
the text of a null, the longest record. A program may give a source of
its own, an `IImportSource`. The session must have no transaction
active, as an import commits its batches.

Where the server grants `copy`, no bad row is to be skipped and no
column is a large object's, an import goes by copy; else through a
prepared INSERT, a field longer than 64 KiB for a CLOB or BLOB column
sent by creating a large object. With `Skip`, a row refused is skipped
and given to the program. A failure stops the import, the batch under
way rolled back; its `Import` says where its record began and the rows
committed and rolled back.

## Exporting

`ExportAsync` runs a query and writes its rows to a stream as they come,
a batch at a time: as CSV, a header of the columns' names, then a
record for each row, a null an empty field; or as the protocol's own
frames, which `NativeRows.ReadAsync` reads back with every value as it
was.

```csharp
using System.Text;
using Turborel.Data;
using Turborel.Data.Authentication;
using Turborel.Data.Import;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Turborel.Data.Types;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
await connection.QueryAsync("CREATE TABLE STOCK (SKU VARCHAR(8) PRIMARY KEY, QTY INTEGER CHECK (QTY >= 0))");
await connection.QueryAsync("COMMIT");

connection.Autocommit = true;
ulong copied = await connection.CopyRowsAsync("STOCK", ["SKU", "QTY"], [
    ["A-1", new TurborelDecimal(10, 0)],
    ["A-2", new TurborelDecimal(0, 0)],
]);
Console.WriteLine($"copied {copied}");
try
{
    await connection.CopyRowsAsync("STOCK", [], [["B-1", new TurborelDecimal(5, 0)], ["B-2", new TurborelDecimal(-1, 0)]]);
}
catch (TurborelException e)
{
    Console.WriteLine($"SQLSTATE {e.SqlState}: {e.Message} ({e.Detail})");
}

connection.Autocommit = false;
string csv = "sku,qty\nC-1,4\nC-2,-3\nC-3,7\n";
var rejected = new List<ImportRejected>();
Imported imported = await connection.ImportAsync(
    new DelimitedSource(new MemoryStream(Encoding.UTF8.GetBytes(csv)), DelimitedDialect.Csv().WithHeader()),
    new ImportOptions("stock") { Skip = true },
    rejected.Add);
Console.WriteLine($"imported {imported.Rows}, rejected {imported.Rejected}");
foreach (ImportRejected row in rejected)
{
    Console.WriteLine($"  {row.Position}: SQLSTATE {row.Cause.SqlState}");
}

try
{
    await connection.ImportAsync(
        new DelimitedSource(new MemoryStream(Encoding.UTF8.GetBytes("D-1,1\nD-2,x\n")), DelimitedDialect.Csv()),
        new ImportOptions("stock"));
}
catch (TurborelException e)
{
    Console.WriteLine($"SQLSTATE {e.SqlState}: {e.Message}; {e.Import}");
}

using var export = new MemoryStream();
ulong exported = await connection.ExportAsync("SELECT SKU, QTY FROM STOCK ORDER BY SKU", export, ExportFormat.Csv);
Console.Write(Encoding.UTF8.GetString(export.ToArray()));
Console.WriteLine($"exported {exported}");
```

```
copied 2
SQLSTATE 23000: CHECK ANN.STOCK_CHECK1 of ANN.STOCK is false for a row (row 2 of the copy)
imported 2, rejected 1
  record 2, line 3: SQLSTATE 23000
SQLSTATE 22018: 'x' is not a <signed numeric literal>; record 2, line 2: 0 rows committed, 1 rolled back
SKU,QTY
A-1,10
A-2,0
C-1,4
C-3,7
exported 4
```

## See also

[Copy, Import and Export](../client/25-copy.md) in the API Reference.
