# Contributing to turborel-cs

turborel-cs carries the data people store in turborel, and their
passwords, between their programs and the server. A value changed on the
way, a secret let out, or a connection that hangs costs them as much as a
fault of the server would. So a change is judged first on whether it keeps
values, secrets and connections right, and then on what it adds. The
process follows the Linux kernel's, adapted to pull requests.

[Before you start](#before-you-start) · [Setting up](#setting-up) ·
[Making a change](#making-a-change) ·
[Changes that touch the wire](#changes-that-touch-the-wire) ·
[Commit messages](#commit-messages) · [Pull requests](#pull-requests) ·
[Review](#review) · [Reporting issues](#reporting-issues) ·
[Security](#security) · [License](#license)

---

## Before you start

Read [RULES.md](RULES.md) §3 (code), §4 (tests), §7 (process) and the
decisions for your area. Every rule applies to every change, and the build
and the tests check those a tool can. [CONFORMANCE.md](CONFORMANCE.md) says
what is implemented of the protocol and of turborel-client, and
[TODO.md](TODO.md) lists open work.

Open an issue first for anything larger than a fix. That covers new
features, changes to a decision in RULES.md, new dependencies (R-SAFE-2)
and changes to the public API. Agreeing on the design first saves everyone
a rewrite.

The driver is written to turborel's protocol page,
`doc/client/05-protocol.md`, and to turborel-client, its reference; where
they differ, turborel-client's code is right. Read the section before
writing code, and cite it by its heading, as in `[PROTOCOL Frames]` or
`[CLIENT Pipelines]` (RULES.md D9). Never cite from memory.

## Setting up

Install the .NET 10 SDK; `global.json` takes its latest feature band.

```
git config core.hooksPath .githooks    # run the fast checks before each commit (once per clone)
dotnet format Turborel.Data.slnx --verify-no-changes
dotnet build                           # the library and its tests, every analyzer an error
dotnet test --solution Turborel.Data.slnx
dotnet test --solution Turborel.Data.slnx --coverlet --coverlet-threshold 90 --coverlet-threshold-type line,branch
                                       # and the coverage floor, as CI runs it (R-COV-1)
```

CI runs every check on Linux, macOS and Windows, on .NET 10 and .NET 11,
and the coverage floor on Linux (D11). A change that passes on one
platform and fails on another isn't done.

## Making a change

Keep one logical change per commit, and make each commit pass every check
on its own, because anyone bisecting may land on it. Move code in one
commit and change it in another. Don't mix a reformat or rename with a
change of behaviour.

Tests come with the change, at the lowest level that shows it: a codec
test on bytes before a live test against a server. Cover normal cases,
boundaries, and robustness cases with the exact SQLCODE and SQLSTATE
expected (R-TEST-2). Each test carries `[Verifies("…")]` with what it
verifies (R-TRACE-1). A fixed defect gets a test that fails without the
fix (R-TEST-5).

Documents change in the same commit. Update CONFORMANCE.md for a row
implemented or changed (R-TRACE-3), RULES.md for a decision made or
changed, and TODO.md for open work done or found. Code never cites TODO.md.

The manual changes in the same commit too (R-DOC-6). A change to what
programs see updates each page of `doc/` that states it, with its examples
run again.

Some rules deserve a reminder.

- Every failure is a `TurborelException`, with its SQLCODE and SQLSTATE,
  and no other exception leaves the library (R-ERR).
- No `unsafe` code (R-SAFE-1), and no dependency without a decision in
  RULES.md (R-SAFE-2).
- Every length read from the wire is bounded before memory is taken for
  it (R-SAFE-3), and no secret is ever shown (R-SAFE-6).
- Performance claims cite measurements, before and after, with the costs
  in memory and latency.
- The coverage floor only rises (R-COV-2).

## Changes that touch the wire

These need more than passing tests. Say in the commit message what was
done for each that applies.

For the codec, say which section of the protocol the change follows, and
test the bytes against the page's own layouts, its worked example among
them. A malformed message must fail the connection as the page says
(SQLCODE -919, SQLSTATE 08006), never be read as something else.

For authentication and TLS, say what a server in the middle, or a server
that is not the one asked for, can do against the change, and show each
attack refused.

For settings, say what a program that set nothing sees, and compare with
turborel-client's reading of the same settings.

## Commit messages

The form is Linux's.

```
area: summary in the imperative, at most 75 characters

The problem: what goes wrong, or is missing, and who it affects. A
wrong value, a secret disclosed, a crash or a hang is said first,
with the program that shows it.

Why this change, and how it solves the problem. Mention the choices
made and the alternatives rejected, the rules and sections applied
(RULES.md D12, R-ERR-3, [PROTOCOL Frames]), and what was measured,
with numbers.

Fixes: 54a4f0239f2e ("codec: refuse a frame longer than the body taken")
Closes: <link to the issue>
Reported-by: A Reporter <reporter@example.org>
```

The subject is `area: summary`, at most 75 characters, in the imperative
("refuse a frame...", not "refused"), with no full stop. It says what
changes, isn't a file name, and isn't reused within a series.

The body is wrapped at 75 columns and written for someone reading `git log`
years later with no context. Say what the change does in plain prose, since
the diff shows how. Don't write "as discussed" or "v2".

Pick the area where most of the change is. The folders are those of
`src/Turborel.Data/`.

| Area | Covers |
|---|---|
| `codec` | `Protocol/`: frames, fields, the tail, types and values, JSON, compression |
| `transport` | `Transport/`: TCP, Unix sockets, named pipes, timeouts |
| `auth` | `Authentication/`: the mechanisms, SCRAM and its key cache |
| `tls` | `Tls/`: TLS, its modes and channel binding |
| `session` | `Session/`: Hello and Ready, status, notices, cancel, ping, reset, change-user |
| `statements` | `Statements/`: queries, prepared statements, cursors, completions |
| `lobs` | `LargeObjects/`: locators, reads and writes, streams |
| `copy` | `Copy/`: copy, import and export |
| `settings` | `Settings/`: the URI, the environment, service and password files, several servers |
| `pipeline` | `Pipelines/`: pipelines |
| `pool` | `Pooling/`: pools, and the SQL-agent |
| `api` | the ADO.NET types at the top of `src/Turborel.Data/` |
| `test` | `test/Turborel.Data.Tests/`, when the tests alone change |
| `doc` | `doc/`, README.md, RULES.md, CONFORMANCE.md, TODO.md, VERSIONING.md, AGENTS.md, CLAUDE.md, SECURITY.md, this file |
| `ci` | `.github/`: workflows, issue forms, labels, Dependabot |
| `build` | the solution, project files, `Directory.*.props`, `global.json`, `.editorconfig`, `.githooks/` |
| `treewide` | a mechanical change across many areas |

Trailers go at the end, one per line, never wrapped.

| Trailer | Meaning |
|---|---|
| `Fixes: <12+ hex digits> ("<subject>")` | the commit that introduced the defect fixed |
| `Closes: <link>` | the issue this commit resolves |
| `Link: <link>` | background, such as a discussion or an issue fixed only in part |
| `Reported-by:` | who reported the defect fixed, with `Closes:` |
| `Suggested-by:` | who suggested the idea |
| `Co-authored-by:` | a person who wrote part of the change, as GitHub credits one |
| `Reviewed-by:` | a reviewer who checked the change and is satisfied with it |
| `Acked-by:` | a maintainer of the area who approves it without a full review |
| `Tested-by:` | someone who tested it, saying where if that matters |

`Reported-by:` and `Suggested-by:` may be added without asking. The others
name someone who said so on the record. `git log -1 --abbrev=12
--pretty='Fixes: %h ("%s")' <commit>` writes the `Fixes:` line.

A commit names people only. It carries no AI or agent attribution: no
`Co-Authored-By` for a tool or an agent, no "Generated with" line, and no
`Signed-off-by`.

## Pull requests

A pull request is a patch series, and its description is the cover letter.
The template asks for what and why, the series commit by commit, testing,
compatibility and performance. Each commit message still stands alone.

Base it on `main` and keep it rebased, without merging `main` into it. When
revising, rewrite the commits so the series is again what should be merged,
with no "fix review comments" commits. Force-push, and say in a comment what
changed since the last version.

Stage each commit by an explicit list of paths, `git add -- <path> ...`,
never `git add -A` or `git add .` over the whole tree, so nothing stray
goes in.

Trailers given in review go into the commits they cover. Drop them from a
commit that later changes substantially, and say so.

Keep a series to about 15 commits. Split a larger one into series that each
stand alone.

## Review

Reviewers check each commit against RULES.md §7's checklist, and that the
manual is updated (R-DOC-6). For
[changes that touch the wire](#changes-that-touch-the-wire), they also
check that the section is followed byte for byte and that the attacks were
weighed. Review comments are about the change, never the person. A
reviewer who is satisfied gives `Reviewed-by:`.

## Reporting issues

Use the issue forms, **Bug**, **Conformance**, **Performance** or
**Feature**. Try the latest `main` first, and add to an existing issue that
matches rather than opening another.

Reproduce with the smallest program that shows it, against a server whose
version you give, with the statements it runs and the settings it connects
by, passwords left out.

State the impact first. Put it in the title's first word when it is severe.

| Tag | Meaning |
|---|---|
| `[WRONG VALUE]` | a value sent or read differs from what was stored or given |
| `[CRASH]` | an exception the driver should not throw, a hang, or memory without bound |
| `[REGRESSION]` | worked in an earlier commit (give the last good commit and, if you can, the first bad one) |

Give the commit (`git rev-parse --short=12 HEAD`), the server's version,
the platform and architecture, and the .NET runtime (`dotnet --info`).

## Security

Don't open a public issue for a vulnerability. Report it privately, as
[SECURITY.md](SECURITY.md) says.

## License

turborel-cs is under the BSD-2-Clause Plus Patent License in
[LICENSE](LICENSE), and a contribution is made under that license.
Contributors keep their copyright and own their changes. Whoever
submits a change answers for all of it, however it was written.
