# Versioning

How turborel-cs is numbered, which turborel it follows, and how its package
is published.

[Release numbers](#release-numbers) · [Alpha](#alpha) ·
[The turborel it follows](#the-turborel-it-follows) ·
[Where the version is](#where-the-version-is) · [Publishing](#publishing)

---

## Release numbers

The driver's version is the turborel release it is made for: the same
number as turborel's server, **MAJOR.MINOR.PATCH**. A turborel release and
each driver's release share the number, so `Turborel.Data` 1.2.0 is the
driver of turborel 1.2.0, and works with its server. The driver has no
version scheme of its own. From 1.0.0, what a release may change follows
turborel's own rules, in turborel's
[VERSIONING.md](https://github.com/turborel/turborel/blob/main/VERSIONING.md).

## Alpha

turborel-cs is in alpha, version **0.0.0**, as turborel is, and speaks
protocol version 1, the only version. While the version is 0.y.z nothing
is promised: the API, the settings and what the driver sends may change in
any build, as the server and the protocol may.

## The turborel it follows

The driver is written to turborel's main branch at commit `e4f08f77`
(2026-11-19): the protocol's page, `doc/client/05-protocol.md`, and
turborel-client, its reference. The headings tests cite
(`test/Turborel.Data.Tests/Support/Citations.cs`) are that commit's. Moving
to a later commit changes this section, those headings and the code that
follows them in one change.

## Where the version is

- `Directory.Build.props`, `<Version>`, the version of the assembly and of
  the package alike.
- The package's id is `Turborel.Data`, in
  `src/Turborel.Data/Turborel.Data.csproj` (RULES.md D8).

## Publishing

The package goes to [NuGet](https://www.nuget.org) only once every turborel
driver is done, all together, and only the owner publishes it (RULES.md
R-PROC-4). Until then it is built from source, and nothing is published.

When the owner publishes a release:

1. The version in `Directory.Build.props` is the turborel release's, on a
   pushed commit that has passed every check in CI.
2. A nuget.org account of Turborel Inc. owns the id `Turborel.Data`, and
   the repository holds its API key as the secret `NUGET_API_KEY`.
3. The owner enables the last step of `.github/workflows/release.yml`,
   which is written but disabled, and runs the workflow by hand. It
   builds, tests, packs and validates the package and its symbols, and
   then pushes both.

A version on NuGet can be unlisted, so no new program takes it, but never
deleted or pushed again. A fix goes out at the number of the next
turborel release.
