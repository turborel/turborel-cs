# turborel-cs rules

How turborel-cs, the C# driver for turborel, is designed, written and
verified. Every change follows these rules; the build, `dotnet format` and
the tests check those a tool can check, and review checks the rest.

Rules (R-…) say how code and tests are written. **shall** is mandatory,
and a departure needs a recorded deviation (§6). **should** is expected, and
a reviewer may accept a reasoned exception.

Decisions (D1…) record how the driver is built and why. Change one only by
changing this file in a reviewed commit. A decision says what was chosen and
why, and the file it names holds the mechanism.

Cite both by ID in code, tests, reviews and commit messages, as in
`RULES.md D4` or `R-ERR-1`.

Before changing code, read §3 (code rules), §4 (test rules) and the
decisions covering your area. What the driver implements of the protocol
and of turborel-client is in [CONFORMANCE.md](CONFORMANCE.md), and open
work is in [TODO.md](TODO.md).

Where a convention of .NET libraries and a rule of this file differ, the
rule is kept, and the decision that keeps it says so.

[1 Decisions](#1-decisions) · [2 Layout](#2-project-layout) ·
[3 Code](#3-code-rules) · [4 Tests](#4-test-rules) ·
[5 Coverage](#5-coverage) · [6 Deviations](#6-deviations) ·
[7 Process](#7-process-and-tools) · [8 References](#8-references)

---

## 1. Decisions

| # | Decision |
|---|---|
| **The driver** | |
| D1 | The native protocol, version 1, in C#, with no native core |
| D2 | ADO.NET is the standard API, async first, over a client of turborel-client's features |
| D3 | .NET 10 is the runtime minimum, and .NET 11 is tested too |
| D4 | No runtime dependency beyond the BCL |
| D12 | One exception, `TurborelException`, a `DbException` |
| **Tooling and process** | |
| D5 | One solution, one library, one test project, settings shared |
| D6 | Every analyzer on, its warnings errors, the style in `.editorconfig` |
| D7 | xUnit v3 on Microsoft.Testing.Platform, coverage by coverlet |
| D8 | One NuGet package, `Turborel.Data` |
| D9 | A test cites what it verifies by an attribute, checked against the manual's headings |
| D10 | The rules a tool can hold, and no analyzer does, are held by tests |
| D11 | The checks run before each commit and in CI on three platforms |

### The driver

#### D1. The native protocol, version 1, in C#, with no native core

The driver speaks turborel's native protocol, version 1
(`doc/client/05-protocol.md` in turborel's repository), itself, in C#. It
neither wraps turborel-client nor loads native code, so it installs from
NuGet alone, runs wherever .NET runs, and is debugged in one language.
turborel-client, in Rust, is the reference: the driver matches what it
does, and where the protocol's page and turborel-client's code differ, the
code is right. Its layers follow turborel-client's modules (codec,
transport, authentication, session, statements, large objects, copy,
settings, pipelines and pools), so a reviewer can hold one against the
other.

#### D2. ADO.NET is the standard API, async first, over a client of turborel-client's features

A .NET program reaches a database through ADO.NET, so the driver gives
`DbConnection`, `DbCommand`, `DbDataReader`, `DbParameter`,
`DbDataSource` and `DbProviderFactory`, each `Turborel`-prefixed. Each
operation that waits on the server is async first, taking a
`CancellationToken`, with the synchronous member beside it. Beneath them is
a client with every feature of turborel-client, public for the programs
that need what ADO.NET has no member for: pipelines, copy, large object
streams, the SQL-agent.

The ADO.NET layer is built in M7, the standard API's milestone, and
audited with the rest in M8. Until then the README's example shows the API
as it will be.

ADO.NET's base classes name the exception for a caller's misuse, such as
`InvalidOperationException` for a command run on a closed connection.
R-ERR-1 makes every failure a `TurborelException`. Which of the two a
misuse throws is settled with the ADO.NET layer, in M7, by a decision
here; until then R-ERR-1 holds.

#### D3. .NET 10 is the runtime minimum, and .NET 11 is tested too

The driver supports every .NET release Microsoft still supports, and drops
each at its end of support. .NET 8 and .NET 9 ended on 2026-11-10, which
leaves .NET 10, a long-term release, and .NET 11, a standard-term one,
both supported until 2028-11-14. The package targets `net10.0` alone,
which .NET 11 runs. CI runs every test on both, the .NET 11 run rolling
the tests forward to its runtime (`DOTNET_ROLL_FORWARD=LatestMajor`). The
SDK is pinned to 10.0 by `global.json`, taking its latest feature band.

#### D4. No runtime dependency beyond the BCL

The package references no package, so a program that takes it takes no
supply chain with it. The BCL has all the protocol needs: sockets, Unix
domain sockets and named pipes; `SslStream` for TLS; SHA-256, HMAC and
PBKDF2 for SCRAM; `ZLibStream` for compressed frames; `BigInteger` for
exact numbers past `decimal`'s 96 bits. A runtime dependency needs a
decision here first, naming its rationale, version, license and review,
and must be production-grade by its own claims (R-SAFE-2). Test
dependencies are D7's.

#### D12. One exception, `TurborelException`, a `DbException`

Every failure the driver reports is a `TurborelException`
(`src/Turborel.Data/TurborelException.cs`). It derives from ADO.NET's
`DbException`, so a program that catches database failures of any
provider catches it, and carries the failure's SQLCODE (`SqlCode`, and
`ErrorCode` too) and SQLSTATE (`SqlState`, `DbException`'s own member).
The rest of a failure, its rule, position, transaction state and
diagnostics, joins it with the codec (M1). It has no constructor without a
SQLCODE and SQLSTATE, so CA1032, standard exception constructors, is
suppressed on it, citing R-ERR-1.

### Tooling and process

#### D5. One solution, one library, one test project, settings shared

`Turborel.Data.slnx` holds `src/Turborel.Data`, the library, and
`test/Turborel.Data.Tests`, its tests. `Directory.Build.props` holds what
every project shares: the version, the language, the analyzers,
deterministic builds and SourceLink. `Directory.Packages.props` holds every
package version (central package management), and each project's
`packages.lock.json` records what restore took (R-CM-2). The library's
namespaces follow its folders, the protocol's codec under
`Turborel.Data.Protocol`, internal until the API needs it public.

#### D6. Every analyzer on, its warnings errors, the style in `.editorconfig`

`AnalysisLevel` is `latest-all`, every .NET analyzer at warning;
`TreatWarningsAsErrors` and `CodeAnalysisTreatWarningsAsErrors` make each
an error; `Nullable` is enabled; `EnforceCodeStyleInBuild` makes the style
rules of `.editorconfig` part of the build; `GenerateDocumentationFile`
makes a public item without a doc comment an error (CS1591); the compiler's
`strict` feature is on. The library is checked for trimming and native AOT
(`IsAotCompatible`), and its arithmetic is checked (R-NUM-1).

The style is .NET's own, as dotnet/runtime writes it: file-scoped
namespaces, `using` outside them with `System` first and no implicit
usings, braces on every block, `var` only where the type is written on the
same line, private fields `_camelCase`, and no `this.`.
`dotnet format --verify-no-changes` checks the layout.

Turned down, each for its reason:

- CA1303, literals passed where a localized string is expected: messages
  are English, as the server's are.
- In tests only, CA1515, types that could be internal, since xUnit finds
  only public test classes; and CA2007, `ConfigureAwait`, since xUnit's
  runner owns the synchronization context (xUnit1030).

#### D7. xUnit v3 on Microsoft.Testing.Platform, coverage by coverlet

The tests run on xUnit v3 (`xunit.v3` 4.0.3, Apache-2.0, the .NET
Foundation's), on Microsoft.Testing.Platform, which `global.json` makes
`dotnet test`'s runner; the test project is an executable. Coverage is
coverlet's extension for that platform (`coverlet.MTP` 10.1.0, MIT, the
.NET Foundation's), whose threshold fails a run below the floor (R-COV-1);
its settings are `test/Turborel.Data.Tests/testconfig.json`. The size rules
read each source's syntax tree with Roslyn (`Microsoft.CodeAnalysis.CSharp`
5.9.0, MIT, Microsoft's), the compiler the SDK itself runs (D10). Each is a
test dependency only, never in the package, and each is the standard of
its kind for .NET. Dependabot proposes their updates
(`.github/dependabot.yml`).

#### D8. One NuGet package, `Turborel.Data`

The package is `Turborel.Data`, its root namespace the same, as .NET data
providers are named. Its metadata is in
`src/Turborel.Data/Turborel.Data.csproj` and `Directory.Build.props`: the
version (VERSIONING.md), the license as the SPDX expression
`BSD-2-Clause-Patent`, the repository, a README for NuGet's page
(`src/Turborel.Data/PACKAGE.md`, short and with absolute links, since NuGet
shows neither HTML pictures nor relative links), the logo as its icon, and
`LICENSE` inside it. NuGet takes no SVG icon, so the icon is
`artwork/turborel-logo.png`, `artwork/turborel-logo.svg` rendered on a
square, otherwise unchanged. Symbols go in a `.snupkg` beside it, and
SourceLink points them at the commit's sources. Nothing is published until
the owner says (VERSIONING.md).

#### D9. A test cites what it verifies by an attribute, checked against the manual's headings

A test carries `[Verifies("…")]`
(`test/Turborel.Data.Tests/Support/VerifiesAttribute.cs`), an xUnit trait,
so a runner lists and filters tests by what they verify. The citation's
tag says what it names: `[PROTOCOL heading]`, a heading of The Native
Protocol, `doc/client/05-protocol.md`; `[CLIENT heading]`, a heading of
The Client Library, `02-library.md`, or of Settings, Pipelines and Pools,
`02-library-connections.md`, which share no heading; `[DESIGN] D<n>`, a
decision here; or `[TOOL] R-…`, a rule a repository check holds.
`Support/Citations.cs` copies those chapters' headings at the turborel
commit VERSIONING.md names, and a test refuses a citation of a heading not
among them, or of a decision or rule this file does not define
(R-TRACE-2).

#### D10. The rules a tool can hold, and no analyzer does, are held by tests

No analyzer of the SDK counts a method's lines or parameters, or looks for
markers of open work. So tests in `test/Turborel.Data.Tests/Rules/` hold
them over every C# source of `src/` and `test/`: R-SIZE-1 to R-SIZE-3 by
each source's syntax tree, R-DOC-3 and R-DOC-5 by its text, and R-TRACE-1
and R-TRACE-2 by reflection over the tests. They run with every other test,
so a change that breaks a rule fails a check, not only a review. They are
C#, as the rest of the repository's automation is (R-TOOL-1).

#### D11. The checks run before each commit and in CI on three platforms

`.githooks/pre-commit` runs the fast checks before each commit: the
format, the build with every analyzer, and the tests. CI
(`.github/workflows/verify.yml`) runs them on Linux, macOS and Windows, on
.NET 10 and .NET 11, and the coverage floor on Linux, restoring in locked
mode. Every action is pinned by its full commit SHA, its version in a
comment. `release.yml` builds and checks the package, run by hand only,
and publishes nothing until the owner enables its last step.
`docs-site.yml` tells turborel.com's docs site, which reads this
repository's `doc/`, when the manual changes; so `doc/` is laid out as
turborel's own manual is, its volumes `user/`, `client/` and `appendix/`,
each chapter opening with its title and a `[Contents](README.md)` line.

---

## 2. Project layout

```
Turborel.Data.slnx      the solution (D5)
Directory.Build.props   what every project shares (D5, D6)
Directory.Packages.props
                        every package version (D5, D7)
global.json             the SDK and the test runner (D3, D7)
.editorconfig           the code style (D6)
README.md               what the driver is; how to build and use it
RULES.md                this file
CONFORMANCE.md          the parity matrix: the protocol and turborel-client
TODO.md                 open work, by milestone
VERSIONING.md           the version, and how publishing is done
CONTRIBUTING.md         changes, commit messages, pull requests, issues
SECURITY.md             how to report a vulnerability
TRADEMARK.md            how the name and logos may be used
AGENTS.md               the guide for coding agents
CLAUDE.md               points Claude Code to AGENTS.md
LICENSE                 BSD-2-Clause-Patent
artwork/                the logos, and the package's icon (D8)
doc/                    the manual: user/, client/, appendix/ (R-DOC-6, D11)
.github/                CI (workflows/), issue forms, pull request
                        template, labels, Dependabot
.githooks/pre-commit    the checks before a commit (D11)
src/Turborel.Data/      the library
  Turborel.Data.csproj  the package (D8)
  PACKAGE.md            the package's README on NuGet (D8)
  Protocol/             the codec of the native protocol (D1)
test/Turborel.Data.Tests/
                        the tests (D7)
  Protocol/             the codec's tests
  Rules/                the repository's checks (D10)
  Support/              what the tests share (R-TEST-6)
  testconfig.json       the coverage settings (D7)
```

---

## 3. Code rules

### R-STYLE: formatting and idiom

| ID | Rule | Checked by |
|---|---|---|
| R-STYLE-1 | Code **shall** be laid out as `.editorconfig` says. | `dotnet format --verify-no-changes`; IDE0055 |
| R-STYLE-2 | Names **shall** follow the .NET Framework Design Guidelines: PascalCase types and members, camelCase parameters and locals, `_camelCase` private fields, `I` before an interface, `Async` after a method that returns a task. | IDE1006; CA1707, CA1715; review |
| R-STYLE-3 | A file **shall** hold one top-level type, named as the file, in the namespace its folder names. | IDE0130; review |
| R-STYLE-4 | Library code **shall not** write to the console, `Debug` or `Trace`. | review |
| R-STYLE-5 | An awaited task in the library **shall** be `ConfigureAwait(false)`, and a method that waits on the server **shall** take a `CancellationToken`. | CA2007, CA2016; review |

### R-SIZE: short functions and files

| ID | Rule | Checked by |
|---|---|---|
| R-SIZE-1 | A method, constructor, accessor, operator or local function **shall** have at most 50 lines of code, blank lines and comments not counted. | `Rules/SizeRuleTests.cs` |
| R-SIZE-2 | A source file **shall** have at most 600 lines. | `Rules/SizeRuleTests.cs` |
| R-SIZE-3 | A function, lambda, delegate, indexer or primary constructor **shall** take at most 5 parameters; else a struct, record or options type. | `Rules/SizeRuleTests.cs` |
| R-SIZE-4 | A type **should** hold one concept, stated in its doc comment. | review |

### R-DOC: comments and documentation

| ID | Rule | Checked by |
|---|---|---|
| R-DOC-1 | Every public and protected item **shall** have an XML doc comment, with `<param>` for each parameter, `<returns>` for what it returns and `<exception>` for each exception it throws. | CS1591, CS1573; review |
| R-DOC-2 | Comments **shall** say *why*: the section implemented, the choice made, the invariant relied on. | review |
| R-DOC-3 | No commented-out code, and no `TODO`, `FIXME`, `XXX` or `HACK` comments: open work goes in TODO.md, and code never cites TODO.md. | `Rules/CommentRuleTests.cs`; review |
| R-DOC-4 | Behaviour the protocol or turborel-client governs **shall** cite it by its heading, as R-TRACE-1's tags do: `[PROTOCOL Frames]`, `[CLIENT Pipelines]`. A choice of the driver's own cites `[DESIGN]` and its decision. | review |
| R-DOC-5 | Every source file **shall** begin with a `//` comment giving its responsibility and the sections it implements. | `Rules/CommentRuleTests.cs` |
| R-DOC-6 | A change a program can see **shall** update the manual, `doc/`, in the same commit: every page stating what changed, its examples run. | review |
| R-DOC-7 | A feature a user would look for **shall** extend README.md's summary in the same commit, by a short line. | review |

### R-CONST: no magic numbers or strings

| ID | Rule | Checked by |
|---|---|---|
| R-CONST-1 | Numeric literals other than `0` or `1` **shall** be named constants, documenting the value's source. | review |
| R-CONST-2 | Every protocol tag, message kind, capability name, status name and setting name **shall** be a named constant, defined once, in the codec's file for its section, under a comment citing it. | review |
| R-CONST-3 | Strings matched or looked up **shall** be named constants. Messages for people are not, but begin with what they cite (R-ERR-2). | review |
| R-CONST-4 | Tests may use literal fixtures, such as the protocol's own bytes; an expected value restating a constant **should** use it. | review |

### R-ERR: errors, not stray exceptions

| ID | Rule | Checked by |
|---|---|---|
| R-ERR-1 | Every failure the library reports **shall** be a `TurborelException` (D12), carrying the SQLCODE, SQLSTATE and message, and the rest of the failure the server sent: its rule, position, whether a transaction is active, and its diagnostics. | review; tests |
| R-ERR-2 | A failure the driver finds itself **shall** use the SQLCODE and SQLSTATE the protocol names for it, and its message **shall** begin with what it enforces: `[PROTOCOL Frames] …`, or `[DESIGN] …`. | review; tests |
| R-ERR-3 | An exception of the BCL's, from a socket, a stream, TLS or a parser, **shall** be caught where it arises and given as a `TurborelException`, the BCL's exception its `InnerException`. No other exception **shall** leave the library, and none **shall** be swallowed: a `catch` that neither rethrows nor wraps says why in a comment. | CA1031; review |
| R-ERR-4 | Library code **shall not** call `Environment.Exit`, `Environment.FailFast` or `Debug.Fail`, nor throw from `Dispose`, a finalizer or `ToString`. | CA1065; review |
| R-ERR-5 | A connection whose protocol state is in doubt, after a malformed message or a broken stream, **shall** be closed and fail every later call, never reused. | review; tests |

### R-NUM: arithmetic and conversions

| ID | Rule | Checked by |
|---|---|---|
| R-NUM-1 | Arithmetic **shall** not overflow: the library builds with `CheckForOverflowUnderflow`, so an overflow is an exception, never a wrong number. Arithmetic meant to wrap is written `unchecked`, with a comment saying why. | the build (D6); review |
| R-NUM-2 | A narrowing conversion of a number read from the wire **shall** be checked against its bound first, failing as R-ERR-2 says. | review; tests |
| R-NUM-3 | Floating-point values **shall not** be compared for equality, except by their bits where the protocol carries bits. | review |

### R-SAFE: language subset and resources

| ID | Rule | Checked by |
|---|---|---|
| R-SAFE-1 | No `unsafe` code, no reinterpreting casts by `Unsafe` or `MemoryMarshal`, and no P/Invoke. | `AllowUnsafeBlocks` false; review |
| R-SAFE-2 | No runtime dependency without a decision in §1 (D4); test dependencies are listed in D7. A dependency **shall** be production-grade by its own claims. | review |
| R-SAFE-3 | Every length or count read from the wire **shall** be checked against its bound before memory is reserved for it, so a server cannot make the driver hold memory without bound [PROTOCOL Values and fields]. | review; tests |
| R-SAFE-4 | No mutable static state, other than a cache a decision names. | review |
| R-SAFE-5 | The library **shall not** start a thread; it runs on its caller's tasks and the thread pool. | review |
| R-SAFE-6 | A password, a SCRAM key or proof, and a cancel key **shall** never appear in a message, `ToString`, a log or an exception. | review; tests |
| R-SAFE-7 | TLS **shall** be verified as the settings ask, never weakened silently, and a secret mechanism **shall** be refused over plain TCP before anything is sent [CLIENT Connecting]. | review; tests |

### R-PLAT: platforms

| ID | Rule | Checked by |
|---|---|---|
| R-PLAT-1 | The driver **shall** run on Linux, macOS and Windows, and every test **shall** pass on each. | CI (D11) |
| R-PLAT-2 | Code for one platform, such as Windows's named pipes, **shall** be chosen at run time by `OperatingSystem`, and refused elsewhere with a `TurborelException` saying so. | CA1416; review |

---

## 4. Test rules

### R-TEST: levels and layout

| ID | Rule | Checked by |
|---|---|---|
| R-TEST-1 | Tests **shall** use xUnit v3, in `test/Turborel.Data.Tests`, mirroring the library's folders. Codec tests work on bytes; live tests talk to a server on loopback. | review |
| R-TEST-2 | Tests **shall** derive from the protocol and turborel-client, not from the code: normal cases with their boundaries, and robustness cases, each with the exact SQLCODE and SQLSTATE expected. | review |
| R-TEST-3 | A test's name **shall** say the behaviour checked, in PascalCase. | CA1707; review |
| R-TEST-4 | Tests **shall** be deterministic and independent: no clock, no randomness and no network beyond loopback, in any order. | review |
| R-TEST-5 | Every fixed defect **shall** get a regression test that fails without the fix, its doc comment saying so. | review |
| R-TEST-6 | A fixture or helper shared by test files **shall** be written once, in `Support/`. | review |
| R-TEST-7 | Test code may index, cast and throw: an exception is a failure. Every other rule applies. | review |

### R-TRACE: traceability

| ID | Rule | Checked by |
|---|---|---|
| R-TRACE-1 | Every test **shall** carry a `[Verifies("…")]` citation starting with a tag: `[PROTOCOL heading]`, `[CLIENT heading]`, `[DESIGN] D<n>` or `[TOOL] R-…` (D9). | `Rules/TraceRuleTests.cs` |
| R-TRACE-2 | A citation **shall** name what exists: a heading of the chapter its tag names, or a decision or rule of this file. | `Rules/TraceRuleTests.cs` |
| R-TRACE-3 | CONFORMANCE.md **shall** change with the change that implements or changes a row of it. | review |

---

## 5. Coverage

| ID | Rule | Checked by |
|---|---|---|
| R-COV-1 | Line and branch coverage of the library, measured by coverlet over every test, **shall not** fall below the floor, the `--coverlet-threshold` of `verify.yml`'s coverage job, 90 %. | CI |
| R-COV-2 | The floor only rises: a change raising coverage **should** raise it, and none lowers it. A change of the floor changes `verify.yml`, CONTRIBUTING.md, AGENTS.md and this rule together. | review |
| R-COV-3 | A gap is closed by a test of a requirement, or by removing dead code; never by a test without one, and never by `ExcludeFromCodeCoverage`, which the coverage settings do not honour. | review |

---

## 6. Deviations

| ID | Rule | Checked by |
|---|---|---|
| R-DEV-1 | An analyzer is suppressed only by `[SuppressMessage]` on the narrowest item, its `Justification` citing the rule that makes it right; never by `#pragma warning disable`. | IDE0079; review |
| R-DEV-2 | An analyzer is turned down in `.editorconfig` only with a decision in §1 saying why (D6). | review |

---

## 7. Process and tools

| ID | Rule | Checked by |
|---|---|---|
| R-PROC-1 | Every check **shall** pass before every commit: the format, the build with every analyzer, and the tests, the repository's checks among them. CI adds the locked restore, both runtimes, three platforms and the coverage floor. | pre-commit hook; CI |
| R-PROC-2 | Every change **shall** be reviewed against the checklist below. | review |
| R-PROC-3 | Commits **should** be small and single-purpose, in CONTRIBUTING.md's form (`area: summary`, then why), citing rules applied or changed. | review |
| R-PROC-4 | The package **shall** be published only by the owner, as VERSIONING.md says. | review |
| R-TOOL-1 | Project automation **shall** be C# or the `dotnet` CLI, never shell scripts; the git hook only hands over to them. | review |
| R-CM-1 | The SDK is pinned by `global.json` (D3); a change to it **shall** be its own commit, passing every check. | review |
| R-CM-2 | `packages.lock.json` **shall** be under version control, and CI restores in locked mode. | CI |
| R-CM-3 | Builds **shall** be deterministic, with SourceLink, so a package can be traced to its commit. | the build (D5) |

The review checklist:

- the section cited in code (R-DOC-4) and tests (R-TRACE-1);
- normal and robustness tests (R-TEST-2), a failing-first test for a fix
  (R-TEST-5);
- no magic numbers or strings (R-CONST);
- every failure a `TurborelException`, nothing swallowed (R-ERR);
- every length from the wire bounded (R-SAFE-3), no secret shown (R-SAFE-6);
- comments explain why (R-DOC-2);
- CONFORMANCE.md updated (R-TRACE-3), and TODO.md where open work changed.

---

## 8. References

- *The Native Protocol*, `doc/client/05-protocol.md`; *The Client
  Library*, `doc/client/02-library.md`; *Settings, Pipelines and Pools*,
  `doc/client/02-library-connections.md`: turborel's manual, at the commit
  VERSIONING.md names.
- turborel-client (`client/src/`) and turborel-protocol (`protocol/src/`),
  the reference implementation.
- *Framework Design Guidelines*; the ADO.NET documentation; the .NET code
  analysis and code-style rule references; the .NET support policy.
- RFC 5802 and RFC 7677, SCRAM-SHA-256; RFC 5929 and RFC 9266, channel
  bindings; RFC 1950, the zlib format.
