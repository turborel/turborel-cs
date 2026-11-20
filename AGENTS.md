# Guide for coding agents

How to work on turborel-cs as a coding agent, with any tool. If this file
disagrees with [RULES.md](RULES.md) or [CONTRIBUTING.md](CONTRIBUTING.md),
follow those.

turborel-cs is `Turborel.Data`, the ADO.NET data provider for turborel, a
relational database system. It speaks turborel's native protocol, version
1, in C#, with no native code and no runtime dependency beyond the BCL.
See [README.md](README.md).

## Before you change anything

Read RULES.md §3 (code), §4 (tests), §7 (process) and the §1 decisions for
your area. Read CONTRIBUTING.md for how a change is made and the extra
checks for changes that touch the wire.

Read the section of the protocol, or of turborel-client's manual, that the
change implements, in turborel's repository: `doc/client/05-protocol.md`,
`doc/client/02-library.md` and `doc/client/02-library-connections.md`, at
the commit VERSIONING.md names. turborel-client's code (`client/src/`,
`protocol/src/`) is the reference: where the page and the code differ,
the code is right.

## Build and check

Every change must pass every check below before it is handed back.

```
dotnet format Turborel.Data.slnx --verify-no-changes   # the layout (R-STYLE-1)
dotnet build                                           # every analyzer, warnings as errors (D6)
dotnet test --solution Turborel.Data.slnx              # every test, the repository's checks among them (D10)
dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch
                                                       # and the coverage floor (R-COV-1)
dotnet pack src/Turborel.Data -c Release -o artifacts/package
                                                       # the package, as release.yml makes it (D8)
git config core.hooksPath .githooks                    # the fast checks before every commit (once per clone)
```

The .NET 10 SDK is required; `global.json` pins it. CI runs the checks on
Linux, macOS and Windows, on .NET 10 and .NET 11, and the coverage floor
on Linux (D11). The floor is in `.github/workflows/verify.yml` and only
rises (R-COV-2).

Project automation is C# or the `dotnet` CLI, never shell scripts
(R-TOOL-1). A rule a tool can hold, and no analyzer does, is a test in
`test/Turborel.Data.Tests/Rules/` (D10).

## Where things are

| Path | What |
|---|---|
| `src/Turborel.Data/` | the library, the NuGet package `Turborel.Data` (D8) |
| `src/Turborel.Data/Protocol/` | the codec of the native protocol, internal |
| `src/Turborel.Data/TurborelException.cs` | the one exception a failure is (D12) |
| `test/Turborel.Data.Tests/` | the tests, xUnit v3 (D7), folders as the library's |
| `test/Turborel.Data.Tests/Rules/` | the repository's checks: size, comments, citations (D10) |
| `test/Turborel.Data.Tests/Support/` | what tests share: `[Verifies]`, the manual's headings |
| `Directory.Build.props` | the version, the language, the analyzers (D5, D6) |
| `Directory.Packages.props` | every package version (D7) |
| `.editorconfig` | the code style (D6) |
| `doc/` | the manual |

The library's folders follow turborel-client's modules, as CONTRIBUTING.md's
table of areas lists them: `Protocol/`, `Transport/`, `Authentication/`,
`Tls/`, `Session/`, `Statements/`, `LargeObjects/`, `Copy/`, `Settings/`,
`Pipelines/`, `Pooling/`, and the ADO.NET types at the top.

## Rules that are broken most often

The build treats every analyzer warning as an error. RULES.md §3 and §4
have the full list.

- Every failure is a `TurborelException`, with SQLCODE, SQLSTATE and
  message; an exception of the BCL's is wrapped where it arises, never
  let out, never swallowed (R-ERR-1, R-ERR-3).
- A message the driver writes begins with what it enforces:
  `[PROTOCOL Frames] ...` (R-ERR-2).
- No `unsafe`, no `MemoryMarshal` reinterpreting, no P/Invoke (R-SAFE-1).
- Bound every length read from the wire before allocating for it
  (R-SAFE-3). Never put a password, SCRAM key or cancel key in a message,
  `ToString` or log (R-SAFE-6).
- Keep methods to 50 lines and 5 parameters, and files to 600 lines
  (R-SIZE); the repository's tests fail if not.
- No magic numbers or strings; every protocol tag is a named constant
  (R-CONST).
- `var` only where the type is written on the same line; file-scoped
  namespaces; braces on every block (D6).
- Await with `ConfigureAwait(false)` in the library, and take a
  `CancellationToken` in every method that waits (R-STYLE-5).
- Arithmetic is checked (R-NUM-1); a narrowing conversion of a wire value
  is bounded first (R-NUM-2).
- Public and protected items have XML doc comments (R-DOC-1). Files start
  with a `//` comment of what they hold (R-DOC-5).
- Comments say why (R-DOC-2). No `TODO` comments or commented-out code
  (R-DOC-3).
- Suppress an analyzer only with `[SuppressMessage]` and a `Justification`
  citing the rule; never `#pragma warning disable` (R-DEV-1).
- No new dependency without a decision in RULES.md (R-SAFE-2), and no
  thread started (R-SAFE-5).
- Platform code is chosen at run time by `OperatingSystem`, and refused
  clearly elsewhere (R-PLAT-2).

## Tests

Every test carries `[Verifies("…")]` starting with a tag the checks
accept (R-TRACE-1): `[PROTOCOL heading]` for a heading of
`05-protocol.md`, as `[PROTOCOL Frames]`; `[CLIENT heading]` for one of
`02-library.md` or `02-library-connections.md`, as `[CLIENT Pipelines]`;
`[DESIGN] D<n>` for a decision; `[TOOL] R-…` for a repository check. The
headings are listed in `Support/Citations.cs`, and a citation of anything
else fails (R-TRACE-2).

Tests are deterministic, with no clock, randomness or network beyond
loopback (R-TEST-4). Fixtures shared by files go in `Support/` (R-TEST-6).
A fixed defect gets a failing-first regression test (R-TEST-5).

## Citing the spec

Check every citation against the page before writing it, and never cite
from memory. A heading not in `Support/Citations.cs` is not a heading of
the commit the driver follows; moving to another commit is its own change
(VERSIONING.md).

Code never cites TODO.md.

## Documents

A finished feature updates these in the same change:

- CONFORMANCE.md, its rows marked done, with any choice made (R-TRACE-3)
- RULES.md, if it made a decision, including the row in §1's table
- TODO.md, which only holds open work
- [doc/](doc/README.md), for anything programs see (R-DOC-6)
- README.md, a short line in "What it has" (R-DOC-7)

Every claim in a document matches the code, and every example runs. No
benchmark numbers or timings in documents.

## Commits

- Commit only when the maintainer asks, and never push without approval.
- Follow CONTRIBUTING.md's form, with one logical change per commit.
- Add no AI or agent attribution to a commit, a pull request, a file or
  the package. That means no `Co-Authored-By` for an agent, no "Generated
  with" and no `Signed-off-by`.
- Stage with an explicit path list, `git add -- <path> ...`. Never use
  `git add -A` or `git add .` on the whole tree.
- Never stage `LICENSE` or scratch files.
- Code for one platform can't be verified locally on another. Say so when
  handing it back.
