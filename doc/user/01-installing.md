# Installing

[Contents](README.md)

turborel-cs is one NuGet package, `Turborel.Data`, of C# alone: it
needs no native library and no package beyond .NET's own. This chapter
tells what it needs, how a program takes it in, and runs a first query.

## What it needs

- .NET 10 or later, on Linux, macOS or Windows. .NET 10 and .NET 11 are
  tested.
- A Turborel server of the same release, which speaks the native
  protocol, version 1
  ([running the server](https://github.com/turborel/turborel/blob/main/doc/operator/02-turboreld.md)).

## Taking the package in

No package is published until every Turborel driver is done; until then
the driver is built from its source, with the .NET 10 SDK:

```
git clone https://github.com/turborel/turborel-cs
cd turborel-cs
dotnet pack src/Turborel.Data -c Release -o artifacts/package
```

A program then takes the package from that folder:

```
dotnet add package Turborel.Data --version 0.0.0 --source /path/to/turborel-cs/artifacts/package
```

Once it is published, `dotnet add package Turborel.Data` takes it from
NuGet.

## A first query

The driver's standard interface is ADO.NET: a program makes a
`TurborelDataSource` of a connection string once, opens its connections
from it, and runs commands on them, as it would with any ADO.NET
provider. The connection string names the server and the user; this
server, on port 17435 of the program's own machine, trusts a local user,
so no password is needed:

```csharp
using Turborel.Data;

await using TurborelDataSource source = TurborelDataSource.Create("host=localhost:17435;user=ann");
await using TurborelConnection connection = await source.OpenConnectionAsync();
Console.WriteLine($"connected to {connection.ServerVersion}");

await using TurborelCommand command = connection.CreateCommand();
command.CommandText = "VALUES CAST(? AS VARCHAR(20)) || ', from turborel'";
command.Parameters.AddWithValue("greeting", "hello");
Console.WriteLine(await command.ExecuteScalarAsync());
```

```
connected to turborel 0.0.0 (alpha)
hello, from turborel
```

The command's `?` is a dynamic parameter, its value given by
`Parameters` in the order of the marks. Every member that waits on the
server is async, taking a `CancellationToken`, with a synchronous member
beside it: `ExecuteScalar` beside `ExecuteScalarAsync`.

## Two interfaces

ADO.NET, the standard API, is the interface a program and the tools
built for C#'s databases use: `TurborelDataSource`, `TurborelConnection`,
`TurborelCommand`, `TurborelDataReader`, `TurborelParameter`,
`TurborelTransaction`, `TurborelBatch` and `TurborelFactory`, each
described in the [API Reference](../client/README.md). Beneath it is the
low-level client, `NativeConnection` and its kin, which does all that
Turborel's own client library does, and which the next chapters use to
show each part of the protocol: connecting, statements, large objects,
copying, pipelines, pools and settings. A `TurborelConnection`'s
`Native` is its low-level connection, for what ADO.NET has no place for.

## See also

[Connecting](02-connecting.md), [Running Queries](03-queries.md),
[TurborelDataSource](../client/01-api-data-source.md),
[Appendix C: Settings](../appendix/c-settings.md).
