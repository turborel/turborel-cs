<p align="center">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="artwork/turborel-logo-white.svg">
      <img src="artwork/turborel-logo.svg" alt="turborel" width="360">
    </picture>
</p>

> turborel-cs is in alpha, meaning, version 0.0.0, as turborel is: a
> driver's version is the turborel release it is made for, the same
> number as the server's, and the alpha speaks protocol version 1.
> Anything may change from one build to the next, API and settings
> alike. See [VERSIONING.md](VERSIONING.md).

turborel-cs is `Turborel.Data`, the ADO.NET data provider for
[turborel](https://github.com/turborel/turborel) (TUR-boh-rel), a
relational database system. It speaks turborel's native protocol, version
1, itself, in C#: no native code, and no dependency beyond .NET's own
libraries. It is to match everything turborel-client, turborel's own
client library, does, and give it to .NET programs through ADO.NET.

## What it has

- The frame header of the native protocol, encoded and decoded.

The rest is being built, milestone by milestone: [TODO.md](TODO.md) lists
what is open, and [CONFORMANCE.md](CONFORMANCE.md) every part of the
protocol and of turborel-client, each as it is done.

## What it needs

- .NET 10 or later. It is tested on .NET 10 and .NET 11.
- Linux, macOS or Windows.
- A turborel server of the same release, which speaks protocol version 1
  ([running the server](https://github.com/turborel/turborel/blob/main/doc/operator/02-turboreld.md)).

## Installing

The package is not yet published to NuGet; it will be once every turborel
driver is done (VERSIONING.md). Until then, build it from source, with the
.NET 10 SDK:

```
git clone https://github.com/turborel/turborel-cs
cd turborel-cs
dotnet pack src/Turborel.Data -c Release -o artifacts/package
```

and take it into a program from that folder:

```
dotnet add package Turborel.Data --version 0.0.0 --source /path/to/turborel-cs/artifacts/package
```

## A first query

This is what the API will be. None of it is built yet: the ADO.NET layer
comes in milestone M7, and until the parity audit, M8, closes, it may
change.

```csharp
using Turborel.Data;

await using TurborelDataSource dataSource =
    TurborelDataSource.Create("host=db.example;user=ann;tls_mode=verify-full;tls_roots=/etc/turborel/ca.pem");
await using TurborelConnection connection = await dataSource.OpenConnectionAsync();

await using TurborelCommand command = connection.CreateCommand();
command.CommandText = "SELECT ID, NAME FROM STAFF WHERE ID < ? ORDER BY ID";
command.Parameters.Add(new TurborelParameter { Value = 3 });

await using TurborelDataReader reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    Console.WriteLine($"{reader.GetInt32(0)} {reader.GetString(1)}");
}
// 1 Ada
// 2 Grace
```

The password comes from the password file, as turborel-client reads it,
and is proved by SCRAM-SHA-256; the settings' names are turborel-client's.

## Building and testing

```
dotnet build                                      # the library and its tests, every analyzer on
dotnet test --solution Turborel.Data.slnx         # every test
dotnet format Turborel.Data.slnx --verify-no-changes
```

[CONTRIBUTING.md](CONTRIBUTING.md) gives every check a change must pass.

## Documents

- [doc/](doc/README.md): the manual.
- [CONFORMANCE.md](CONFORMANCE.md): what is implemented of the protocol
  and of turborel-client.
- [RULES.md](RULES.md): how the driver is designed, written and verified.
- [TODO.md](TODO.md): open work.
- [VERSIONING.md](VERSIONING.md): the version, and publishing.
- [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md):
  contributing, and reporting an issue or a vulnerability.
- [LICENSE](LICENSE) and [TRADEMARK.md](TRADEMARK.md): the code is
  under the BSD-2-Clause Plus Patent License; the name Turborel and its
  logos are not, and TRADEMARK.md says how they may be used.
