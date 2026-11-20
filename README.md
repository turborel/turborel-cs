<p align="center">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="artwork/turborel-logo-white.svg">
      <img src="artwork/turborel-logo.svg" alt="turborel" width="360">
    </picture>
</p>

> turborel-cs is in alpha, meaning, version 0.0.0. Its version is the
> number of the Turborel release it is made for, and the alpha is made
> for Turborel 0.0.0, which speaks protocol version 1. Anything may
> change from one build to the next, API and behaviour alike. See
> [VERSIONING.md](VERSIONING.md). The latest Turborel alpha is built for
> Linux, macOS and Windows as the pre-release
> [nightly](https://github.com/turborel/turborel/releases/tag/nightly),
> and as the image `ghcr.io/turborel/turborel:nightly`.

turborel-cs is the C# driver for
[Turborel](https://github.com/turborel/turborel), a relational database
system. It connects a C# program to a Turborel server,
`turboreld`, over Turborel's [native
protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md),
version 1, which it speaks itself, in C#, with no native code and no dependency beyond .NET's own libraries.

Its standard interface is ADO.NET, async first: `TurborelDataSource`, `TurborelConnection`, `TurborelCommand`, `TurborelDataReader` and `TurborelFactory`. Beneath it is a low-level
client built to do all that Turborel's own client library,
turborel-client, does: TCP, Unix sockets and Windows named pipes, TLS,
SCRAM-SHA-256 and SCRAM-SHA-256-PLUS, prepared statements and cursors,
large objects, bulk copy, import and export, settings from a URI, the
environment and service and password files, several servers, pipelines
and pools.

The driver is built in milestones, M0 to M8.
[CONFORMANCE.md](CONFORMANCE.md) shows what is done, row by row, and
[TODO.md](TODO.md) what is open.

## Requirements

- .NET 10 or later, on Linux, macOS or Windows ([RULES.md](RULES.md) D3).
- .NET 10 and .NET 11 are tested.
- A Turborel server of the same release, which speaks protocol version 1
  ([running the server](https://github.com/turborel/turborel/blob/main/doc/operator/02-turboreld.md)).

## Installing

No package is published yet. The driver's package, `Turborel.Data`, goes to
NuGet only once every Turborel driver is done, with the others
([VERSIONING.md](VERSIONING.md#publishing)). Until then, build the
driver from source:

With the .NET 10 SDK:

```
git clone https://github.com/turborel/turborel-cs
cd turborel-cs
dotnet pack src/Turborel.Data -c Release -o artifacts/package
```

A program then takes the package from that folder:

```
dotnet add package Turborel.Data --version 0.0.0 --source /path/to/turborel-cs/artifacts/package
```

## A first query

The standard API is ADO.NET. This program runs against a server on port
17435 of its own machine, which trusts a local user:

```csharp
using Turborel.Data;

await using TurborelDataSource dataSource = TurborelDataSource.Create("host=localhost:17435;user=ann");
await using TurborelConnection connection = await dataSource.OpenConnectionAsync();

await using (TurborelCommand create = connection.CreateCommand())
{
    create.CommandText = "CREATE TABLE STAFF (ID INTEGER PRIMARY KEY, NAME VARCHAR(20))";
    await create.ExecuteNonQueryAsync();
    create.CommandText = "INSERT INTO STAFF VALUES (1, 'Ada'), (2, 'Grace'), (3, 'Edsger')";
    Console.WriteLine($"inserted {await create.ExecuteNonQueryAsync()}");
}

await using TurborelCommand command = connection.CreateCommand();
command.CommandText = "SELECT ID, NAME FROM STAFF WHERE ID < ? ORDER BY ID";
command.Parameters.Add(new TurborelParameter { Value = 3 });

await using TurborelDataReader reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetInt32(0)} {reader.GetString(1)}");
}
```

```
inserted 3
1 Ada
2 Grace
```

## The manual

[doc/](doc/README.md) is the manual: a User's Guide, an API Reference
and Appendices, published at
https://turborel.com/docs/drivers/csharp/. Each
chapter is written as the milestone that fills it lands. The SQL a
program sends, and the server, are described in Turborel's own manual,
[The Turborel Documentation Set](https://github.com/turborel/turborel/blob/main/doc/README.md).
[VERSIONING.md](VERSIONING.md) tells how the driver's releases are
numbered and published.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before making a change: how a
change is made, its commit message, pull requests and issues. Every
change passes `dotnet run tools/verify.cs`. [RULES.md](RULES.md) says how the
driver is designed, written and verified,
[CONFORMANCE.md](CONFORMANCE.md) is the parity matrix every Turborel
driver keeps, and [TODO.md](TODO.md) lists open work.

## Security

Report a vulnerability privately, never in a public issue, as
[SECURITY.md](SECURITY.md) says.

## License and trademark

The code is under the BSD-2-Clause Plus Patent License, in
[LICENSE](LICENSE). The name Turborel and its logos are not:
[TRADEMARK.md](TRADEMARK.md) says how they may be used.
