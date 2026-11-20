# turborel-cs rules

How turborel-cs is designed, written and verified. Every change follows
these rules; `dotnet run tools/verify.cs` checks those a tool can check, and
review checks the rest.

Rules (R-…) say how code and tests are written. **shall** is mandatory,
and a departure needs a decision recorded here (§7). **should** is
expected, and a reviewer may accept a reasoned exception.

Decisions (D1…) record how the driver is built and why. Change one only
by changing this file in a reviewed commit. A decision says what was
chosen and why, and the module it names holds the mechanism.

Cite both by ID in code, tests, reviews and commit messages, as in
`RULES.md D6` or `R-ERR-2`.

The rules are modelled on Turborel's own
[RULES.md](https://github.com/turborel/turborel/blob/main/RULES.md),
adapted to drivers. Every Turborel driver keeps the same requirement IDs
with the same meaning, and the same decisions D1 to D8, so a rule is
cited the same way in every repository; a driver's own decisions begin
at D9, and its own requirements are in families of their own. Where
C#'s library conventions and a rule here disagree, the rule is
kept, and D8 lists where.

Before changing code, read §3 (code rules), §4 (test rules) and the
decisions covering your area. What the driver implements is in
[CONFORMANCE.md](CONFORMANCE.md), and open work is in [TODO.md](TODO.md).

[1 Decisions](#1-decisions) · [2 Layout](#2-project-layout) ·
[3 Code](#3-code-rules) · [4 Tests](#4-test-rules) ·
[5 Coverage](#5-coverage) · [6 Process](#6-process-and-tools) ·
[7 The decisions](#7-the-decisions) · [8 References](#8-references)

---

## 1. Decisions

Each is told in full in [§7](#7-the-decisions).

| # | Decision |
|---|---|
| **Shared by every Turborel driver** | |
| D1 | The native protocol, version 1, in layers that follow turborel-client |
| D2 | The language's standard API, over a low-level client of turborel-client's features |
| D3 | The runtime minimum follows the ecosystem's own support |
| D4 | One command runs every check, before each commit and in CI on three platforms |
| D5 | The version is the Turborel release's: 0.0.0 in the alpha, speaking protocol 1 |
| D6 | Tests and code cite Turborel's documents by their headings |
| D7 | A coverage floor, enforced in CI, that only rises |
| D8 | Where these rules depart from C#'s conventions |
| **This driver's own** | |
| D9 | One exception, `TurborelException`, a `DbException` |
| D10 | One solution, one library, one test project, settings shared |
| D11 | Every analyzer on, its warnings errors, the style in `.editorconfig` |
| D12 | xUnit v3 on Microsoft.Testing.Platform, coverage by coverlet |
| D13 | One NuGet package, `Turborel.Data` |
| D14 | The checks tool is `tools/verify.cs` and the tests of `Rules/` |
| D15 | The low-level client is asynchronous alone |
| D16 | An import's failure is a `TurborelException` that says what the import kept |
| D17 | A misuse of the standard API is the driver's error, SQLSTATE TB910 |
| D18 | A value is mapped by its .NET type, and read as the type its column names |
| D19 | A connection string names the settings, as a URI's query does, and the pool's |
| D20 | A command runs directly, or prepared once it has parameters, its rows read as they come |

---

## 2. Project layout

| Path | What |
|---|---|
| `Turborel.Data.slnx` | the solution: the library and its tests (D10) |
| `Directory.Build.props` | what every project shares: the version, the language, the analyzers, deterministic builds and SourceLink (D10, D11) |
| `Directory.Packages.props` | every package version, the test project's alone (D12) |
| `global.json` | the SDK, and the test runner (D3, D12) |
| `.editorconfig` | the code style (D11) |
| `tools/verify.cs` | the checks tool: every check, as one command (D4, D14) |
| `src/Turborel.Data/` | the library, the NuGet package `Turborel.Data` (D13) |
| `src/Turborel.Data/Protocol/` | `codec`: frames, fields, the tail, data types and values, compression |
| `src/Turborel.Data/Json/` | `codec`: the JSON value's stored form, and its text |
| `src/Turborel.Data/Types/` | the values and data types the codec gives and takes |
| `src/Turborel.Data/TurborelException.cs` | the driver's error (D9) |
| `src/Turborel.Data/Transport/`, `Authentication/`, `Tls/`, `Session/`, `Statements/`, `LargeObjects/`, `Copy/`, `Settings/`, `Pipelines/`, `Pooling/` | the other layers of D1, each as its milestone builds it |
| `test/Turborel.Data.Tests/` | the tests, xUnit v3 (D12), in the library's folders |
| `test/Turborel.Data.Tests/Rules/` | the checks tool's rules: sizes, comments, citations, the manual (D14) |
| `test/Turborel.Data.Tests/Support/` | what the tests share: `[Verifies]` and the headings of Turborel's documents (D6) |
| `doc/` | the manual (R-DOC-7) |

---

## 3. Code rules

The tools named by "the formatter", "the linters", "the type checker" and
"the checks tool" are D4's.

### R-STYLE: formatting and idiom

| ID | Rule | Checked by |
|---|---|---|
| R-STYLE-1 | Code **shall** be formatted by the formatter. | the formatter |
| R-STYLE-2 | Names **shall** follow the language's own conventions; code implementing a message, field or setting is named after it, as the spec names it. | the linters; review |
| R-STYLE-3 | Library code **shall not** print, or write to a stream it was not given. | the linters or the checks tool |
| R-STYLE-4 | What is not public **shall** be private, by the language's own means, so the public API is what is documented. | review |

### R-SIZE: short functions and files

| ID | Rule | Checked by |
|---|---|---|
| R-SIZE-1 | A function **shall** have at most 50 lines of code, blank lines and comments not counted. | the linters or the checks tool |
| R-SIZE-2 | A source file **shall** have at most 600 lines; split by the spec's sections or by layer. | the linters or the checks tool |
| R-SIZE-3 | A function **shall** take at most 5 parameters; else a structure or an options object. | the linters |
| R-SIZE-4 | A module **should** hold one concept, stated in its doc comment. | review |
| R-SIZE-5 | A function's complexity **shall** be at most 15, and its blocks nested at most 5 deep. | the linters |

### R-DOC: comments and documentation

| ID | Rule | Checked by |
|---|---|---|
| R-DOC-1 | Every public item **shall** have a doc comment in the language's documentation format, saying each parameter, what it returns, and each failure it gives. | the linters; review |
| R-DOC-2 | Comments **shall** say *why*: the section implemented, the choice made, the invariant relied on. | review |
| R-DOC-3 | The API reference, where the language's tools build one, **shall** build without warnings. | the checks tool |
| R-DOC-4 | Behaviour the protocol or turborel-client governs **shall** cite it by its heading, `[PROTOCOL Frames]`, `[CLIENT Pools]`; the driver's own choices cite their decision (D6). | review; the checks tool |
| R-DOC-5 | No commented-out code, and no `TODO`, `FIXME` or `XXX` comments: open work goes in TODO.md, and code never cites TODO.md. | the linters or the checks tool |
| R-DOC-6 | Every source file **shall** begin with a doc comment giving its responsibility and the sections it implements. | review |
| R-DOC-7 | A change a program can see **shall** update the manual, `doc/`, in the same commit; every claim is checked against the code, and every example is run. The manual keeps Turborel's layout, which turborel.com's documentation site reads: each page opens with its `# Title` and a line holding only `[Contents](README.md)`, each volume's README.md lists its pages, links within `doc/` and to other files of this repository are relative links that resolve (the site points the latter at GitHub, at the same tag), a link to Turborel's own manual is its GitHub address under `https://github.com/turborel/turborel/blob/main/doc/`, `user/01-installing.md` keeps its name, and `doc/` holds no images. | review; the checks tool |

### R-CONST: no magic numbers or strings

| ID | Rule | Checked by |
|---|---|---|
| R-CONST-1 | Numbers in library code other than `0` or `1` **shall** be named constants, documenting the value's source. | the linters; review |
| R-CONST-2 | Every protocol tag **shall** be a named constant, defined once: a message's kind, a data type's and a value's tag, a tail tag, a capability's name, a mechanism's name, a status name, and each SQLSTATE the driver raises. | review |
| R-CONST-3 | Strings matched or looked up, such as settings' names, URI parts and environment variables, **shall** be named constants. Messages for people are not. | review |
| R-CONST-4 | Related constants **shall** live together in their layer's module, under a comment naming the section they come from. | review |
| R-CONST-5 | Tests may use literal fixtures, as the spec prints its bytes; an expected value restating a constant **should** use it. | review |

### R-ERR: errors

| ID | Rule | Checked by |
|---|---|---|
| R-ERR-1 | Every failure the library gives **shall** be the driver's error (D2): no stray exception, panic, abort, assertion or other error of the language or its libraries reaches a program, and no library code ends the process. | the linters; tests; review |
| R-ERR-2 | Each failure **shall** carry its SQLCODE, SQLSTATE and message, and, once read from a server, the rest of it: the rule it cites, its position, whether a transaction is still active, and its diagnostics. A failure the driver raises itself carries the SQLCODE and SQLSTATE turborel-client gives the same failure. | tests |
| R-ERR-3 | No failure **shall** be silenced. One caught and given again as the driver's keeps its cause; a result left unread says why it cannot matter. | the linters; review |
| R-ERR-4 | A connection that fails **shall** be closed, and every request waiting on it given that failure; a failed call leaks nothing and leaves nothing waiting. | tests; review |

### R-NUM: integers and numbers

| ID | Rule | Checked by |
|---|---|---|
| R-NUM-1 | Every integer of the protocol **shall** be read and written at its width, little-endian, explicitly: never by the platform's byte order, by casting a buffer, or by bitwise operators on numbers wider than the language's operators keep exact. | review; tests |
| R-NUM-2 | A value out of range for its field, or for the type it is read into, **shall** be refused with the driver's error, never truncated, wrapped or rounded. | tests |
| R-NUM-3 | An exact number (units `i128`, scale `u32`) and a DECFLOAT **shall** never pass through a binary floating-point type. | review; tests |
| R-NUM-4 | An approximate number **shall** keep its bits: NaN and -0.0 are read and written as the double they are, and floats are compared bit for bit, never for equality. | tests; review |
| R-NUM-5 | In a language whose integers are bounded, arithmetic on sizes, counts and offsets **shall** be shown not to overflow before it is done. | the linters; review |

### R-LINT: warnings

| ID | Rule | Checked by |
|---|---|---|
| R-LINT-1 | A warning is an error: of the compiler, the linters and the type checker, and of the runtime itself while the tests run. | the checks tool |
| R-LINT-2 | The strictest standard linters and type checking the language has **shall** be on; a check left off says why in the configuration that leaves it off. | review |
| R-LINT-3 | A warning **shall** be silenced only on its line, naming its rule and saying why; a suppression no longer needed **shall** be removed. | the linters; review |

### R-DEP: dependencies

| ID | Rule | Checked by |
|---|---|---|
| R-DEP-1 | The library **shall** depend on nothing but the language's standard library and runtime, unless a decision here admits a dependency, with its reason, version, license and review. | review |
| R-DEP-2 | A dependency, of the library or of its tools, **shall** be production-grade by its makers' own claims: a stable release they support. | review |
| R-DEP-3 | Tools **shall** be pinned by version, with the ecosystem's lock file under version control where it has one; a change of a tool's version is a commit of its own. | review |

### R-SAFE: what a server sends, and secrets

| ID | Rule | Checked by |
|---|---|---|
| R-SAFE-1 | A length or count a server, or a file the driver reads, gives **shall** be checked against what follows and the limit taken before anything is held for it, so no reply takes the program's memory without bound. | tests; review |
| R-SAFE-2 | What a server sends **shall** never be run, loaded or deserialized as code. | the linters; review |
| R-SAFE-3 | TLS **shall** be used and verified as the settings ask, and a connection **shall not** fall back to less than its mode allows. | tests |
| R-SAFE-4 | A secret (a password, anything derived from one, a SCRAM key or proof, a cancel key) **shall** never be logged, put in a failure's message or an object's display form, or sent where it was refused; where the language allows, it is cleared from memory once used. | tests; review |
| R-SAFE-5 | No module-level mutable state: what lasts belongs to an object a program made, such as a connection, a pool or a key cache. | review |
| R-SAFE-6 | The library **shall** start no thread, timer or task but for an object a program made, and a closed connection, cursor or pool leaves none behind. | tests; review |

### R-PLAT: platforms

| ID | Rule | Checked by |
|---|---|---|
| R-PLAT-1 | The driver **shall** run on Linux, macOS and Windows, on every runtime D3 supports; CI checks each (D4). | CI |
| R-PLAT-2 | Code of one platform, such as a Unix socket or a named pipe, **shall** be chosen by the platform, and elsewhere refused with the driver's error, saying so; never silently something else. | tests; review |
| R-PLAT-3 | Paths **shall** be made by the language's path functions, and files kept with LF line endings (`.gitattributes`). | review |
| R-PLAT-4 | Code built for one platform only **shall** say, when handed back, that it was not run on the others. | review |

### R-UNSAFE: no unsafe code

| ID | Rule | Checked by |
|---|---|---|
| R-UNSAFE-1 | No `unsafe` code, no reinterpreting casts by `Unsafe` or `MemoryMarshal`, and no P/Invoke. | `AllowUnsafeBlocks` false; review |

### R-ASYNC: async first

| ID | Rule | Checked by |
|---|---|---|
| R-ASYNC-1 | Every awaited task in the library **shall** be `ConfigureAwait(false)`, so a program's synchronization context is never captured. | CA2007 |
| R-ASYNC-2 | Every method that waits on the server **shall** take a `CancellationToken` and pass it on; the standard API gives each its synchronous form beside it (D15). | CA2016; review |

---

## 4. Test rules

### R-TEST: levels and layout

| ID | Rule | Checked by |
|---|---|---|
| R-TEST-1 | A test **shall** sit at the lowest level that shows its requirement: a codec test of bytes, a test of a layer over a stream the test controls, a test of the API, or a live test against a server. | review |
| R-TEST-2 | Tests **shall** derive from the spec and turborel-client, not from the code: normal cases, boundaries included, and robustness cases with the exact SQLCODE and SQLSTATE expected. | review |
| R-TEST-3 | A test's name **shall** say the behaviour checked. | review |
| R-TEST-4 | Test code may assert, print and use literal fixtures; every other rule applies. | the linters' configuration |
| R-TEST-5 | Tests **shall** be deterministic and independent: no clock, randomness, network beyond loopback, or order between them. A case generator uses a fixed seed. A live test runs against a server on loopback that its own set-up starts, on its own port and data directory. | review |
| R-TEST-6 | Every fixed defect **shall** get a regression test that fails without the fix, its doc comment saying so. | review |
| R-TEST-7 | A fixture or helper shared by tests **shall** be written once, in one place. Tests use the one framework D9 or later names, no other. | review |
| R-TEST-8 | Live tests **shall** run against `ghcr.io/turborel/turborel:nightly` in CI, set up by the repository's own server set-up, with test certificates, SCRAM users and SERVER.TRUST_LOCAL. | CI |
| R-TEST-9 | SCRAM **shall** be tested by RFC 7677's test vector, and settings by the URI, service file, password file and environment cases of turborel-client's settings tests, so every driver reads settings as turborel-client does. | review |

### R-TRACE: traceability

| ID | Rule | Checked by |
|---|---|---|
| R-TRACE-1 | Every test **shall** cite what it verifies, by its `[Verifies("…")]` attribute, beginning with a tag of D6: `[PROTOCOL <heading>]`, `[CLIENT <heading>]`, `[DESIGN] D<n>` or `[TOOL] R-<id>`. | the checks tool |
| R-TRACE-2 | A citation **shall** name a heading, decision or rule that exists. | the checks tool |
| R-TRACE-3 | Code implementing a requirement **shall** cite it (R-DOC-4). | review |
| R-TRACE-4 | CONFORMANCE.md **shall** change with the change that implements or changes a row. A row is marked done only where the driver's code for it exists and tests citing its section pass, or, of a driver built over another, as CONFORMANCE.md says. | review |

---

## 5. Coverage

| ID | Rule | Checked by |
|---|---|---|
| R-COV-1 | Coverage **shall** be measured by `dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch`, over the library and every test. | the checks tool |
| R-COV-2 | Coverage **shall not** fall below the floor (D7); a change raising it **should** raise the floor. | the checks tool, in CI |
| R-COV-3 | A gap is closed by a test derived from a requirement, or by removing dead code; never by a test of no requirement, and never by excluding the code. | review |

---

## 6. Process and tools

| ID | Rule | Checked by |
|---|---|---|
| R-PROC-1 | `dotnet run tools/verify.cs` **shall** pass before every commit; CI adds every platform, every supported runtime and the coverage floor (D4). | pre-commit hook; CI |
| R-PROC-2 | Every change **shall** be reviewed against the checklist below. | review |
| R-PROC-3 | Commits **should** be small and single-purpose, in CONTRIBUTING.md's form (`area: summary`, then why), citing the rules applied or changed. | review |
| R-PROC-4 | A threshold change **shall** change RULES.md and the configuration that holds it together. | review |
| R-PROC-5 | Project automation **shall** be written in the language's own tooling, never shell scripts; the git hook only hands over to the checks tool. | review |
| R-PROC-6 | CI's actions **shall** be pinned by their full commit SHA, with the version in a comment. | review |
| R-PROC-7 | A release **shall** be published only as VERSIONING.md says, by the owner (D5). | review |

The review checklist:

- the spec or turborel-client cited in code (R-DOC-4) and tests
  (R-TRACE-1);
- normal and robustness tests (R-TEST-2), and a failing-first test for a
  fix (R-TEST-6);
- every failure the driver's, carrying its condition (R-ERR);
- no magic numbers or strings, protocol tags named (R-CONST);
- every length a server sends bounded, no secret disclosed (R-SAFE);
- comments explain why (R-DOC-2);
- CONFORMANCE.md (R-TRACE-4), the manual (R-DOC-7) and TODO.md updated
  where they change.

---

## 7. The decisions

### Shared by every Turborel driver

#### D1. The native protocol, version 1, in layers that follow turborel-client

The driver speaks Turborel's native protocol, version 1, as
`doc/client/05-protocol.md` in Turborel's repository lays it out,
itself, in C#, with no native code and no dependency beyond .NET's own libraries. Where that page and turborel-protocol's code differ,
the code is right, and the driver follows it; CONFORMANCE.md names each
such place.

The reference is turborel-client. The driver does all it does before it
is called done, row by row in CONFORMANCE.md, and is built in the same
layers as turborel-client's modules, so a reviewer checks one against
the other. Each layer is a commit area of CONTRIBUTING.md.

| Layer | What it covers | turborel-client |
|---|---|---|
| `codec` | the 5-byte frame header; fields; the tail and its tag order; the data types and values; JSON values; zlib `z` frames within the length bound; the 16 MiB body limit | `protocol/src` |
| `transport` | TCP on 7422; Unix sockets; the Windows named pipe `\\.\pipe\turborel`; TLS begun on the same port; the connect and read timeouts | `target.rs`, `timeouts.rs`, `pipe.rs` |
| `auth`, `tls` | trust, password, certificate, SCRAM-SHA-256 and -PLUS; secrets refused over plain TCP; the SCRAM key cache; TLS | `mechanism.rs`, `scram.rs`, `tls.rs` |
| `session` | Hello, Ready, Status, notices, conditions, diagnostics, autocommit, cancel, terminate, ping, reset, change-user, query attributes, multi-statement | `connection.rs`, `session.rs`, `cancel.rs`, `told.rs` |
| `statements` | queries and streamed queries; prepare, execute, fetch, close, deallocate; numbered cursors; describe; completions | `statements.rs`, `frames.rs` |
| `lobs`, `copy` | locators, their reads, writes and streams; copy, import and export | `lobs.rs`; `copy.rs`, `import/`, `export.rs` |
| `settings` | the URI, options, variables, service and password files, several hosts, roles, TLS modes, channel binding | `settings/`, `failover.rs`, `role.rs` |
| `pipeline`, `pool` | pipelines; pools; the SQL-agent | `pipeline.rs`; `pool/`, `agent.rs` |
| `api` | the standard API (D2) | the language's own |

Each layer is a folder of `src/Turborel.Data/`, and a namespace:
`Protocol/` and `Json/` are the codec, `Types/` the values it gives and
takes, and the later layers come as `Transport/`, `Authentication/`,
`Tls/`, `Session/`, `Statements/`, `LargeObjects/`, `Copy/`,
`Settings/` and `Pipelines/` and `Pooling/`, with the ADO.NET types at
the top. What a layer holds is internal until the API needs it public.
The BCL gives all the protocol needs: sockets, Unix domain sockets and
named pipes; `SslStream` for TLS, which gives no keying material
exporter, so SCRAM-SHA-256-PLUS binds by `tls-server-end-point`;
SHA-256, HMAC and PBKDF2 for SCRAM; `ZLibStream` to write compressed
frames; and `Int128` and `BigInteger` for exact numbers. Compressed
frames are read by an inflater ported from turborel's protocol crate
(`Protocol/Zlib/`), since the BCL's leaves the bytes after a stream
unread, and the codec must refuse them.

#### D2. The language's standard API, over a low-level client of turborel-client's features

The driver's standard interface is ADO.NET, async first: `TurborelDataSource`, `TurborelConnection`, `TurborelCommand`, `TurborelDataReader` and `TurborelFactory`, so a program, and the
tools built for C#'s database interface, use it as they use
any other driver. It sits over a low-level client that has every feature
of turborel-client. What the standard interface has no place for, such
as pipelines, pools, copy, large objects by locator, notices and the
session's status, is reached through the low-level client or the
driver's own extensions of the standard interface.

Every failure is the driver's error, carrying its SQLCODE and SQLSTATE
(R-ERR). The standard interface is built at milestone M7 and checked in
M8's audit; until then its names may change.

The ADO.NET types are `TurborelDataSource` (a `DbDataSource`),
`TurborelConnection`, `TurborelCommand`, `TurborelDataReader`,
`TurborelParameter`, `TurborelTransaction`, `TurborelBatch` (over
pipelines) and `TurborelFactory` (a `DbProviderFactory`). Every member
that waits on the server is async first, taking a `CancellationToken`,
with the synchronous member beside it. The driver's error is
`TurborelException`, a `DbException` (D9). An exact number is a
`TurborelDecimal` of `Int128` units and a `uint` scale, since .NET's
`decimal` holds 96 bits, and gives a `decimal` where one holds it.

ADO.NET's base classes name the exception for a caller's misuse, such as
`InvalidOperationException` for a command run on a closed connection;
R-ERR-1 makes it the driver's error, and D17 settles it: every misuse
is SQLSTATE TB910.

#### D3. The runtime minimum follows the ecosystem's own support

The driver supports .NET 10 or later. The rule is the ecosystem's own:
support every release its makers still support, and drop each at its
end of life. Where a major framework of the language sets a higher
floor, the driver matches it. The minimum changes in a commit of its
own, which changes this decision, the package's metadata, CI and
README.md together.

.NET 8 and .NET 9 ended on 2026-11-10, which leaves .NET 10, a
long-term release, and .NET 11, a standard-term one, both supported
until 2028-11-14. The package targets `net10.0` alone, which .NET 11
runs; CI runs every test on both, the .NET 11 run rolling the tests
forward to its runtime (`--roll-forward LatestMajor`). `global.json`
pins the SDK to 10.0, taking its latest feature band.

#### D4. One command runs every check, before each commit and in CI on three platforms

`dotnet run tools/verify.cs` runs every check a change must pass, stopping at the
first failure: the formatter, the linters, the type checker where the
language has one, the checks tool's own rules (lengths a linter does not
measure, citations and their targets, open-work markers, the manual's
titles, `[Contents](README.md)` lines and links), and every test.
`dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch` measures coverage and holds it to its floor (D7).

`.githooks/pre-commit` hands over to the same command. It is a script
only because git runs hooks as scripts. CI runs it on Linux, macOS and
Windows, over every runtime D3 supports, with the live tests against the
nightly image and coverage on Linux. Every action is pinned by its full
commit SHA, its version in a comment, and Dependabot proposes each bump.

The checks tool is `tools/verify.cs`, a C# program the .NET 10 SDK runs
from its one file. It restores in locked mode, then checks the format
with `dotnet format --verify-no-changes`, the formatter, by
`.editorconfig`; builds with every .NET analyzer and xUnit's, the
linters, and the C# compiler with nullable reference types, the type
checker, every warning an error (D11); and runs every test with
coverlet's floor (D7, D12). The rules no linter holds are tests in
`test/Turborel.Data.Tests/Rules/`, run with the others (D14).
`--roll-forward <policy>` runs the tests on a later runtime, as CI's
.NET 11 run does.

#### D5. The version is the Turborel release's: 0.0.0 in the alpha, speaking protocol 1

The driver has no version scheme of its own. Its version is the number
of the Turborel release it is made for, the same as the server's: a
Turborel release and each driver's release for it share the number,
**MAJOR.MINOR.PATCH**, as Turborel's VERSIONING.md numbers it. The alpha
is 0.0.0, as Turborel's is, and speaks protocol version 1.

The number is written once, in `Directory.Build.props`'s `<Version>`. Nothing is
published, and no release is tagged, until every Turborel driver is
done; then the owner publishes them together (VERSIONING.md).

#### D6. Tests and code cite Turborel's documents by their headings

A test cites what it verifies, and code what it implements, by the
heading of the section that says it, as printed:

- `[PROTOCOL <heading>]`, a heading of The Native Protocol,
  `doc/client/05-protocol.md`, as `[PROTOCOL Frames]`;
- `[CLIENT <heading>]`, a heading of The Client Library,
  `doc/client/02-library.md`, or of Settings, Pipelines and Pools,
  `doc/client/02-library-connections.md`, which share no heading, as
  `[CLIENT Pipelines]`;
- `[DESIGN] D<n>`, a decision of this file;
- `[TOOL] R-<id>`, a rule the checks tool holds, for the tool's own
  tests.

What else a test relies on follows the tag, as an RFC by its section:
`[PROTOCOL Mechanisms] [RFC 7677 §3]`. The heading `Lob 'L'` is printed
twice in The Native Protocol, under Requests and under Replies, so a
citation of it says which it means.

The checks tool keeps the headings of those three documents, copied at
the Turborel commit CONFORMANCE.md names, and refuses a citation of a
heading, decision or rule that is not there (R-TRACE-2). In C#,
a test cites by its `[Verifies("…")]` attribute.

#### D7. A coverage floor, enforced in CI, that only rises

`dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch` measures the library's coverage over every test,
and CI fails below the floor. In every Turborel driver the floor is at
least 90 % of lines, and of branches where the tool measures them. It
only rises: a change that raises coverage well above it raises it. A
floor below 100 % says why.

The floor is 90 % of lines and of branches, kept in `tools/verify.cs`
and in `dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch` alike. It is below 100 % because code of one platform,
such as Windows's named pipes, is measured where it does not run: the
floor is held on each platform's run.

#### D8. Where these rules depart from C#'s conventions

The driver follows its language's library conventions for layout,
naming, packaging, documentation and testing. Where one conflicts with a
rule here, the rule is kept:

- Libraries commonly leave the length of functions and files to taste.
  These are bounded (R-SIZE).
- Libraries commonly fail an argument out of range with the language's
  own error. Here every failure is the driver's, an argument's too, with
  the SQLCODE and SQLSTATE turborel-client gives it (R-ERR-1), so a
  program handles one kind of failure.

- .NET code commonly writes `var` throughout, or never. Here `var` is
  written only where the type is on the same line, as dotnet/runtime
  writes it (D11).
- ADO.NET names other exceptions for a caller's misuse (D2); R-ERR-1 is
  kept until M7 decides.
- A test cites what it verifies by an attribute, an xUnit trait, rather
  than by its doc comment, so a runner lists and filters tests by it
  (D14).
- CA1032's standard constructors are left out of `TurborelException`:
  every failure carries its SQLCODE and SQLSTATE (D9).
- CA1303, localizable literals, is off: messages are English, as the
  server's are. In tests, CA1515, since xUnit finds only public classes,
  CA2007, since xUnit's runner owns the synchronization context, and
  CA1062, since xUnit gives a theory its arguments from its data, are
  off.

Where the driver departs from turborel-client, the reference it follows
(D1), the owner has decided it, and it is recorded here:

- SCRAM-SHA-256-PLUS binds by `tls-server-end-point`, the hash of the
  server's certificate, over TLS 1.2 and TLS 1.3 alike, where
  turborel-client binds by `tls-exporter` over TLS 1.3. .NET's
  `SslStream` exports no keying material, and the driver takes no
  dependency beyond the BCL (R-DEP-1). The server takes either binding
  over TLS 1.3 (Turborel's manual, Users and Authentication).

### This driver's own

#### D9. One exception, `TurborelException`, a `DbException`

Every failure the driver gives is a `TurborelException`
(`src/Turborel.Data/TurborelException.cs`). It derives from ADO.NET's
`DbException`, so a program that catches any provider's database
failures catches it, and carries the failure's SQLCODE (`SqlCode`, and
`ErrorCode` too) and SQLSTATE (`SqlState`, `DbException`'s own member).
The rest of a failure, its rule, position, transaction state and
diagnostics, joins it with the statements (M3). It has no constructor
without a SQLCODE and SQLSTATE, so CA1032's standard constructors are
suppressed on it, citing R-ERR-2.

#### D10. One solution, one library, one test project, settings shared

`Turborel.Data.slnx` holds `src/Turborel.Data`, the library, and
`test/Turborel.Data.Tests`, its tests. `Directory.Build.props` holds
what every project shares: the version (D5), the language, the
analyzers, deterministic builds and SourceLink. `Directory.Packages.props`
holds every package version, and each project's `packages.lock.json`
records what restore took, which the checks restore in locked mode
(R-DEP-3).

#### D11. Every analyzer on, its warnings errors, the style in `.editorconfig`

`AnalysisLevel` is `latest-all`, every .NET analyzer at warning;
`TreatWarningsAsErrors` and `CodeAnalysisTreatWarningsAsErrors` make
each an error; `Nullable` is enabled; `EnforceCodeStyleInBuild` makes
the style rules of `.editorconfig` part of the build;
`GenerateDocumentationFile` makes a public item without a doc comment an
error (CS1591); the compiler's `strict` feature is on. The library is
checked for trimming and native AOT (`IsAotCompatible`), and builds with
`CheckForOverflowUnderflow`, so an overflow is the runtime's error, never
a wrong number (R-NUM-5). The style is dotnet/runtime's: file-scoped
namespaces, `using` outside them with `System` first and no implicit
usings, braces on every block, `var` only where the type is written on
the same line, private fields `_camelCase`, and no `this.`.

#### D12. xUnit v3 on Microsoft.Testing.Platform, coverage by coverlet

The tests run on xUnit v3 (`xunit.v3` 4.0.3, Apache-2.0, the .NET
Foundation's), on Microsoft.Testing.Platform, which `global.json` makes
`dotnet test`'s runner; the test project is an executable. Coverage is
coverlet's extension for that platform (`coverlet.MTP` 10.1.1, MIT, the
.NET Foundation's), whose threshold fails a run below the floor (D7);
its settings are `test/Turborel.Data.Tests/testconfig.json`, which do
not honour `ExcludeFromCodeCoverage` (R-COV-3). The checks of sizes read
each source's syntax tree with Roslyn (`Microsoft.CodeAnalysis.CSharp`
5.12.0, MIT, Microsoft's), the compiler the SDK runs. Each is a test
dependency only, never in the package (R-DEP-1), and Dependabot proposes
their updates.

#### D13. One NuGet package, `Turborel.Data`

The package is `Turborel.Data`, its root namespace the same, as .NET data
providers are named. Its metadata is in
`src/Turborel.Data/Turborel.Data.csproj`: the license as the SPDX
expression `BSD-2-Clause-Patent`, the repository, the manual's address,
https://turborel.com/docs/drivers/csharp/, as its project page, a
README for NuGet's
page (`src/Turborel.Data/PACKAGE.md`, with absolute links, since NuGet
shows no HTML picture and no relative link), the logo as its icon, and
`LICENSE` inside it. NuGet takes no SVG icon, so the icon is
`artwork/turborel-logo.png`, `artwork/turborel-logo.svg` rendered on a
square. Symbols go in a `.snupkg` beside it, SourceLink pointing them at
the commit's sources, and builds are deterministic.

#### D14. The checks tool is `tools/verify.cs` and the tests of `Rules/`

`tools/verify.cs` runs the checks (D4). The rules no analyzer holds are
tests in `test/Turborel.Data.Tests/Rules/`, over every C# source of
`src/`, `test/` and `tools/`: R-SIZE-1 to R-SIZE-3 by each source's
syntax tree; R-DOC-5 and R-DOC-6 by its text; R-TRACE-1 and R-TRACE-2 by
reflection over the tests, a test's citation being its `[Verifies]`
attribute (`Support/VerifiesAttribute.cs`), and by the citations,
decisions and rules the sources name; and R-DOC-7 by the manual's pages,
its installing chapter, its want of images, and every Markdown file's
links. `Support/Citations.cs` holds the
headings of The Native Protocol, The Client Library and Settings,
Pipelines and Pools at the Turborel commit CONFORMANCE.md names (D6).

#### D15. The low-level client is asynchronous alone

Each method of the low-level client that waits on the server returns a
task and takes a `CancellationToken` (R-ASYNC-2); none has a
synchronous form. A synchronous wrapper over each would double the
low-level API for programs that seldom want it, and blocking on a task
is the caller's to choose. The standard API, ADO.NET, gives the
synchronous members its interfaces declare, `Open` beside `OpenAsync`,
over the same tasks (D2). A timeout, or a token cancelled, during a read
or write loses the connection (SQLSTATE 08006), since the frame it was in
cannot be finished. A large object's reader and writer are .NET streams,
so they read and write synchronously too, by waiting on their requests.

#### D16. An import's failure is a `TurborelException` that says what the import kept

turborel-client gives an import's failure as an error of its own, its
cause, the input's or an SQL condition, beside where its record began
and the rows committed and rolled back. Here every failure is a
`TurborelException` (D9), so an import's is its cause's, an SQL
condition's codes, or of input that cannot be read SQLCODE -301 and
SQLSTATE 22000, with `Import`, an `ImportFailure`, saying where and what
was kept. A source a program writes refuses its input the same way, by
`ImportFailure.Input`. A row skipped carries its cause as a
`TurborelException` too.

#### D17. A misuse of the standard API is the driver's error, SQLSTATE TB910

ADO.NET's own providers throw `InvalidOperationException` for a command
run on a closed connection, a second reader on a connection, or a reader
read before its first row; `ArgumentException` for a parameter of
another provider's, or `ArgumentOutOfRangeException` for a place past a
collection; and ADO.NET's base classes throw `NotSupportedException` of
what a provider does not give, as `GetSchema`'s collections, a
`System.Transactions` enlistment, or a nested reader by `GetData`. Here
each is a `TurborelException` of SQLCODE -910 and SQLSTATE TB910, as the low-level client's misuse is (R-ERR-1), so a
program catches one exception for every failure the driver gives. Each
is thrown before anything is sent, and leaves the connection working.

#### D18. A value is mapped by its .NET type, and read as the type its column names

A parameter's value is sent as the protocol's: an integer of any width,
or a `decimal`, an exact number; a `float` or `double` an approximate
one; a `string` or `char` a character string; a `byte[]` a binary
string; a `bool` a boolean; a `Guid` a UUID; a `DateTime` a timestamp of
its fields, a `DateTimeOffset` one with time zone, a `DateOnly` a date,
a `TimeOnly` a time, a `TimeSpan` an interval DAY TO SECOND; null or
`DBNull` the null value; and the driver's own types, as
`TurborelDecimal` or `TurborelLargeObject`, as they are. Its `DbType` is
not read: the server types each `?` where it stands, and casts the
value sent to it.

`GetValue` and `GetFieldType` give each column as .NET's type of its data
type: an exact number a `decimal`, or the `TurborelDecimal` itself where
a `decimal` cannot hold it; an approximate number a `double`; a date or
timestamp a `DateTime`; a timestamp with time zone a `DateTimeOffset`; a
time a `TimeSpan`, as ADO.NET's providers give one; a day-time interval
a `TimeSpan`, where it holds it; JSON and XML their text; and what .NET
has no type for, a time with time zone, a year-month interval, a
decimal floating-point number, a large object by locator, an array, row,
multiset, structured value or reference, the driver's own type.
`GetProviderSpecificValue` gives the codec's value as it is.

#### D19. A connection string names the settings, as a URI's query does, and the pool's

A connection string is key=value pairs of the connection settings, by
the names a URI's query and a service file give them (`host`, `user`,
`tls_mode` and the rest), with the pool's own: `pooling`, `pool_max`,
`pool_max_idle`, `pool_idle_timeout`, `pool_max_lifetime`, `pool_wait`
and `pool_check`, its times whole seconds and its switches `true` or
`false` in any case, as .NET writes a `bool`. A whole `turborel://` URI
is taken as a connection string too. The settings are laid over the
environment and the service, as `ConnectionSettings.Resolve` lays them.
A key that names no setting is refused, SQLSTATE 08001, so a misspelt
one is not dropped.

A `TurborelDataSource` keeps one pool. A `TurborelConnection` made of a
string shares the data source, and its pool, of every other connection
of the same string in the process, as ADO.NET's pooled providers do.

#### D20. A command runs directly, or prepared once it has parameters, its rows read as they come

A command's dynamic parameters are written `?` and given by their place,
as SQL writes them; a parameter's name is the program's own. A command of
no parameters, not prepared, runs as a Query: one round trip, its text
one statement or, of a server that grants `multi-statement`, several,
each read by `NextResult`. A command of
parameters is prepared, executed and deallocated; one prepared by
`Prepare` stays prepared until its text or connection changes. A query
executed opens a cursor, its rows fetched 1000 at a time as the reader
reads them. A reader closed early reads what is left of a Query's rows,
or closes its cursor.

Outside a transaction a connection is under autocommit, each statement
its own transaction, as ADO.NET's programs expect; `BeginTransaction`
turns it off until the transaction ends. A connection runs one statement
at a time: while a reader is open, it takes no other command.
`CommandTimeout` cancels a statement on the server, as `Cancel` does, if
its first reply has not come within it.

---

## 8. References

- Turborel, `doc/client/05-protocol.md`, *The Native Protocol*,
  version 1: the requirements of the codec and every message.
- Turborel, `doc/client/02-library.md`, *The Client Library*, and
  `doc/client/02-library-connections.md`, *Settings, Pipelines and
  Pools*, and the crates turborel-client and turborel-protocol: the
  features to match, and the reference where the spec is silent or
  differs.
- Turborel, `RULES.md` D89, the JSON value's binary tree, and D184, the
  UUID's tag.
- RFC 1950, *ZLIB Compressed Data Format Specification*; RFC 5802 and
  RFC 7677, SCRAM and SCRAM-SHA-256; RFC 5929 and RFC 9266, the
  `tls-server-end-point` and `tls-exporter` channel bindings.
- The standards of C# and of its database interface, named in
  D2 and D3.
