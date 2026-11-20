# Installing

[Contents](README.md)

This chapter will tell what the driver needs, .NET 10 or later on Linux,
macOS or Windows, and how a program takes `Turborel.Data` in from
NuGet. No package is published until every Turborel driver is
done; until then the driver is built from its source, as the
repository's
[README](../../README.md)
says. The chapter is written with the standard API, at milestone M7, and
again when the packages are published.

Until then, the driver is built from source, with the .NET 10 SDK:

```
git clone https://github.com/turborel/turborel-cs
cd turborel-cs
dotnet pack src/Turborel.Data -c Release -o artifacts/package
```

A program then takes the package from that folder:

```
dotnet add package Turborel.Data --version 0.0.0 --source /path/to/turborel-cs/artifacts/package
```
