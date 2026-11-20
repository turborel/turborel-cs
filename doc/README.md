# The turborel-cs Documentation Set

turborel-cs is `Turborel.Data`, the ADO.NET data provider for turborel, a
relational database system: the library a .NET program connects to a
turborel server by. It speaks the server's native protocol, version 1,
itself, in C#, with no native code. This set of manuals describes it for
those who write programs with it. The server, its SQL and its operation
are described in turborel's own manuals
([The Turborel Documentation Set](https://github.com/turborel/turborel/blob/main/doc/README.md)).

turborel-cs is in alpha: what these manuals describe may change in any
release until 1.0.0 ([Versions](appendix/d-versions.md)). The driver is
being built, milestone by milestone, and each chapter is written as the
part it describes is finished; until then a chapter says what it will
cover. [CONFORMANCE.md](../CONFORMANCE.md) says what is done.

## The volumes

| Volume | For | |
|---|---|---|
| [I. User's Guide](user/README.md) | Anyone who writes a .NET program against turborel | Installing, connecting, queries, transactions, bulk work and settings, chapter by chapter |
| [II. API Reference](client/README.md) | The same, looking something up | The ADO.NET types and the low-level client, a page each |
| [Appendices](appendix/README.md) | Everyone | Types, errors, settings and versions |

Read the User's Guide from the start to learn the driver; turn to the API
Reference and the appendices to look a thing up.

## Conventions

**Examples** are C#, set in a fixed-width font, each one run against a
server of the release the manuals come with. What an example prints
follows it, as comments beginning `//`.

**SQL** in examples is written as turborel's SQL Reference Manual writes
it: key words and names in upper case.

**Citations** name the section of the protocol a behaviour comes from, as
[PROTOCOL Frames], or of turborel-client's manual, as [CLIENT Pipelines],
so a reader can look the rule up.

**Errors** are given by SQLSTATE, the code the standard defines, and
SQLCODE, the older number, as the server gives them;
[Appendix B](appendix/b-errors.md) lists them.
